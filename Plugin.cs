using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
namespace CustomSoundtrack
{
    [BepInPlugin(Guid, "Custom Soundtrack by Nemorz", "0.5.0")]
    [BepInDependency("com.wrussell.customhideoutradio", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.nemorz.customsoundtrack";
        private static Plugin instance;
        private Harmony harmony;
        private CustomHideoutRadio.Plugin hideoutRadio;
        private readonly MusicState state = new MusicState();
        private readonly OutcomeClock outcome = new OutcomeClock();
        private readonly ConcurrentQueue<Action> events = new ConcurrentQueue<Action>();
        private readonly Dictionary<MusicKind, Channel> channels = new Dictionary<MusicKind, Channel>();
        private readonly Dictionary<string, ConfigEntry<float>> fades = new Dictionary<string, ConfigEntry<float>>();
        private readonly System.Random random = new System.Random();
        private ConfigEntry<bool> enabledSetting, debug, nearbyShots, ownShots, damageTrigger, nearbyGrenades, runThrough, suppressDefault, suppressOutcomeJingles;
        private ConfigEntry<float> master, combatQuiet, combatRadius, grenadeRadius, deathLimit, extractionLimit, startupDelay;
        private ConfigEntry<KeyboardShortcut> playKey, stopKey, nextKey, previousKey;
        private MusicKind active;
        private int raidEpoch;
        private object world, localPlayer, gui;
        private bool ready, loading, menuAllowed, faulted;
        private double menuShownAt = -1;
        private AudioSource mutedNative;
        private bool priorNativeMute;
        private static double Now => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        private static bool IsOutcome(MusicKind kind) => kind == MusicKind.Death || kind == MusicKind.Extraction;
        private void Awake()
        {
            instance = this;
            enabledSetting = Config.Bind("General", "Enabled", true, "Enable soundtrack. Radios have a separate switch. Restart after enabling.");
            suppressDefault = Config.Bind("General", "SuppressDefaultMenuMusic", true, "Silence vanilla menu music, including loading gaps.");
            suppressOutcomeJingles = Config.Bind("General", "SuppressDefaultOutcomeJingles", true, "Replace vanilla outcome jingles when custom music exists.");
            startupDelay = FloatSetting("Loading", "MenuDelaySeconds", 4, 0, 60, "Wait this many seconds after the menu loads.");
            master = FloatSetting("Volumes", "Master", 0.6f, 0, 1, "Multiplies all enabled mod audio, including bosses, radios and UI replacements.");
            debug = Config.Bind("Advanced Debug", "Enabled", false, "Log combat reasons and playback recovery. No per-frame snapshots.");
            combatQuiet = FloatSetting("Combat", "QuietSeconds", 20, 1, 180, "Seconds without triggers before combat music ends.");
            ownShots = Config.Bind("Combat", "YourGunfire", true, "Your shots trigger combat music.");
            damageTrigger = Config.Bind("Combat", "CombatDamage", true, "Combat damage to you triggers combat music.");
            nearbyShots = Config.Bind("Combat", "NearbyGunfire", true, "Nearby shots trigger combat, including allies.");
            combatRadius = FloatSetting("Combat", "GunfireRadiusMetres", 50, 1, 200, "Nearby gunfire distance.");
            nearbyGrenades = Config.Bind("Combat", "NearbyGrenadeExplosions", true, "Nearby grenade explosions trigger combat.");
            grenadeRadius = FloatSetting("Combat", "GrenadeRadiusMetres", 35, 1, 200, "Grenade explosion trigger distance.");
            deathLimit = FloatSetting("Outcomes", "DeathSeconds", 0, 0, 600, "0 = full song. Otherwise, limit playback to these seconds.");
            extractionLimit = FloatSetting("Outcomes", "ExtractionSeconds", 0, 0, 600, "0 = full song. Otherwise, limit playback to these seconds.");
            runThrough = Config.Bind("Outcomes", "RunThroughCountsAsExtraction", true, "Include Run Through results.");
            playKey = Config.Bind("Hotkeys", "PlayPause", new KeyboardShortcut(KeyCode.UpArrow), "Pause/resume the current playlist; toggle hideout radios.");
            stopKey = Config.Bind("Hotkeys", "Stop", new KeyboardShortcut(KeyCode.DownArrow), "Stop the current playlist; switch both hideout radios off.");
            nextKey = Config.Bind("Hotkeys", "Next", new KeyboardShortcut(KeyCode.RightArrow), "Next track in the current playlist.");
            previousKey = Config.Bind("Hotkeys", "Previous", new KeyboardShortcut(KeyCode.LeftArrow), "Previous track in the current playlist.");
            ConfigureVolumeHotkeys();
            ConfigurePlayerUi();
            ConfigurePlaybackOptions();
            ConfigureBossMusic();
            ConfigureAudioExtensions();
            foreach (var key in new[] { "MenuFadeIn", "MenuToHideoutOrLoading", "MenuToRaid", "RaidAmbientFadeIn", "AmbientToPersonal", "PersonalToAmbient", "CombatToAmbient", "RaidToDeath", "RaidToExtraction", "DeathToMenu", "ExtractionToMenu", "OtherTransitions" })
                fades[key] = FloatSetting("Fades", key, 2, 0, 15, "Fade seconds. 0 = instant. Combat entry is always instant.");
            foreach (MusicKind kind in Enum.GetValues(typeof(MusicKind)))
            {
                if (kind == MusicKind.None) continue;
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
                source.ignoreListenerPause = true; source.volume = 0;
                channels.Add(kind, new Channel { Source = source,
                    Volume = FloatSetting("Volumes", kind.ToString(), 0.65f, 0, 1, kind + " volume; live adjustment.") });
                fades[kind + "TrackChange"] = FloatSetting("Fades", kind + "TrackChange", 2, 0, 15, "Seconds to fade out a track and then fade in the next in this category.");
            }
            try
            {
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.wrussell.customhideoutradio"))
                    Logger.LogError("Standalone Custom Hideout Radio is still loaded. Integrated radio skipped to prevent double patching. Close Tarkov and run the unified installer; check for a renamed standalone radio DLL.");
                else
                {
                    hideoutRadio = gameObject.AddComponent<CustomHideoutRadio.Plugin>();
                    hideoutRadio.Initialize(Config, Logger);
                    Logger.LogInfo("Integrated hideout radio initialized; settings and volume controls share this plugin.");
                }
            }
            catch (Exception ex) { Logger.LogError("Integrated hideout radio initialization failed: " + ex); }
            ConfigureMenuLayout();
            if (!enabledSetting.Value) return;
            try { harmony = new Harmony(Guid); InstallHooks(); InstallPlayerUiInputHook(); InstallNotificationSounds(); InstallEnvironmentControls(); InstallActionTriggers(); ConfigureMenuLayout(); }
            catch (Exception ex) { Fail("Hook setup failed", ex); }
        }
        private ConfigEntry<float> FloatSetting(string section, string name, float value, float min, float max, string description)
            => Config.Bind(section, name, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
        private void Fail(string message, Exception ex)
        {
            faulted = true; active = MusicKind.None; Logger.LogError(message + "; soundtrack suspended; native music restored. " + ex);
            foreach (var c in channels.Values) c.Source.Stop();
            RestoreNative(); harmony?.UnpatchSelf();
        }
        private static void Dispatch(Action<Plugin> action)
        { var p = instance; if (p != null && !p.faulted) p.events.Enqueue(() => action(p)); }
        private void Update()
        {
            UpdatePlaybackOptions();
            UpdateAudioExtensions();
            UpdateEnvironmentControls();
            UpdateInjuryAnnouncements();
            UpdateMenuVisibility();
            UpdateBossMusic();
            UpdateNotificationSounds();
            if (faulted) { ClosePlayerUi(true); return; }
            try
            {
                for (int i = 0; i < 256 && events.TryDequeue(out var action); i++) action();
                if (!enabledSetting.Value)
                {
                    ClosePlayerUi(true);
                    foreach (var c in channels.Values) { c.Source.Pause(); c.Gain.Set(0, 0, Now); }
                    active = MusicKind.None; RestoreNative(); return;
                }
                var native = NativeSource();
                if (suppressDefault.Value) MuteNative(native);
                if (!loading && native != null && menuShownAt >= 0 && Now - menuShownAt >= startupDelay.Value)
                { loading = true; StartCoroutine(LoadAll()); }
                if (!ready) return;
                double now = Now;
                if (state.InRaid && localPlayer != null)
                {
                    var health = Read(localPlayer, "HealthController");
                    if (health != null && Read(health, "IsAlive") is bool alive && !alive) FinishRaid("Killed");
                }
                bool hideout = Read(gui, "HideoutSoundActive") is bool h && h;
                var desired = state.Select(now, menuAllowed, hideout);
                UpdatePlayerUi();
                if (!playerUiVisible && !playerInputCaptured) HandleVolumeHotkeys(desired, hideout);
                bool outcomeOverridden = !playerUiVisible && !playerInputCaptured && HandleOutcomeOverride(hideout);
                if (!outcomeOverridden && !playerUiVisible && !playerInputCaptured)
                {
                    if (!state.InRaid && hideout && hideoutRadio != null && desired == MusicKind.None) HandleHideoutHotkeys();
                    else HandleUnifiedHotkeys(desired);
                }
                SwitchTo(state.Select(now, menuAllowed, hideout));
                if (recoveryRequested && !recovering) StartCoroutine(RecoverRuntime());
                TickChannels(now);
                MaintainPlayback(now);
                if (!suppressDefault.Value)
                {
                    if (channels.Values.Any(c => c.Source.isPlaying && c.Gain.Value(now) > 0)) MuteNative(native);
                    else RestoreNative();
                }
            }
            catch (Exception ex) { Fail("Update failed", ex); }
        }
        private void HandleHotkeys(double now)
        {
            var c = channels[MusicKind.Personal];
            if (playKey.Value.IsDown()) { if (c.Tracks.Count > 0) state.TogglePersonal(now); else Logger.LogInfo("Personal playlist is empty."); }
            if (stopKey.Value.IsDown()) { state.StopPersonal(); c.ResetWhenSilent = true; }
            var target = PlaybackRouting.RaidTrackTarget(state.PersonalPlaying);
            var selected = channels[target];
            if (!selected.Loading && nextKey.Value.IsDown()) QueueTrack(target, 1, false);
            if (!selected.Loading && previousKey.Value.IsDown()) QueueTrack(target,
                target == MusicKind.Personal && selected.SavedPosition > 3 ? 0 : -1, false);
        }
        private float FadeSeconds(MusicKind from, MusicKind to)
        {
            if (to == MusicKind.Combat) return 0;
            if (masterFadeEnabled.Value) return masterFadeSeconds.Value;
            if (to == MusicKind.Death) return FadeValue("RaidToDeath");
            if (to == MusicKind.Extraction) return FadeValue("RaidToExtraction");
            if (from == MusicKind.Death) return FadeValue("DeathToMenu");
            if (from == MusicKind.Extraction) return FadeValue("ExtractionToMenu");
            if (from == MusicKind.Combat) return FadeValue("CombatToAmbient");
            if (from == MusicKind.Personal && to == MusicKind.RaidAmbient) return FadeValue("PersonalToAmbient");
            if (from == MusicKind.RaidAmbient && to == MusicKind.Personal) return FadeValue("AmbientToPersonal");
            if (from == MusicKind.MainMenu && to == MusicKind.RaidAmbient) return FadeValue("MenuToRaid");
            if (from == MusicKind.MainMenu) return FadeValue("MenuToHideoutOrLoading");
            if (to == MusicKind.MainMenu) return FadeValue("MenuFadeIn");
            if (to == MusicKind.RaidAmbient) return FadeValue("RaidAmbientFadeIn");
            return FadeValue("OtherTransitions");
        }
        private void SwitchTo(MusicKind desired)
        {
            if (active == desired) return;
            var previous = active; float seconds = FadeSeconds(previous, desired); active = desired;
            Logger.LogInfo("Music state -> " + desired + "; fade=" + seconds + "s");
            foreach (var pair in channels)
            {
                if (pair.Key == desired) { pair.Value.FadeInSeconds = seconds; continue; }
                pair.Value.Gain.Set(0, seconds, Now);
                if (seconds == 0) pair.Value.Source.Pause();
                if (pair.Key == previous && (pair.Key == MusicKind.Combat || (IsOutcome(pair.Key) && !state.PersonalPlaying))) pair.Value.ResetWhenSilent = true;
            }
           bool randomStart =
    desired == MusicKind.MainMenu ||
    desired == MusicKind.RaidAmbient ||
    desired == MusicKind.Combat;

if (randomStart &&
    !IsPaused(desired) &&
    !(desired == MusicKind.MainMenu && startupTemporary))
{
    var c = channels[desired];

    if (!c.Loading && c.Tracks.Count > 0)
    {
        c.Source.Stop();
        c.Started = false;
        c.EndingTrack = false;
        c.ResetWhenSilent = false;
        c.SavedPosition = 0;
        c.Gain.Set(0, 0, Now);
        c.FadeInSeconds = seconds;

        QueueTrack(desired, 1, true);
    }
}

if (desired != MusicKind.None) EnsureActive();
        }
        private void EnsureActive()
        {
            if (active == MusicKind.None || IsPaused(active)) return;
            var c = channels[active];
            if (c.Loading || c.Failed) return;
            c.ResetWhenSilent = false;
            if (c.PendingIndex >= 0)
            {
                if (c.Source.isPlaying && !c.Gain.Finished(Now)) return;
                if (c.Source.isPlaying && c.Gain.Value(Now) > 0) return;
                ApplySelection(active); return;
            }
            if (!Valid(c.Source.clip))
            {
                if (c.Index >= 0) { c.Source.Stop(); c.Source.clip = null; c.Gain.Set(0,0,Now); c.PendingIndex = c.Index; c.RestorePosition = true; Logger.LogInfo("Restoring " + active + " at " + c.SavedPosition + "s; same selected file."); RequestRecovery("restore " + active, active); }
                else QueueTrack(active, 1, active != MusicKind.Personal && active != MusicKind.MainMenu);
                return;
            }
            if (!c.Source.isPlaying)
            {
                c.Source.volume = master.Value * c.Volume.Value * c.Gain.Value(Now);
                c.Source.Play(); c.Source.time = Math.Min(c.SavedPosition, Math.Max(0, c.Source.clip.length - 0.05f));
                c.Started = true;
            }
            if (c.Gain.Target != 1) c.Gain.Set(1, c.FadeInSeconds, Now);
        }
        private void QueueTrack(MusicKind kind, int direction, bool shuffle)
        {
            var c = channels[kind]; int n = c.Tracks.Count;
            if(kind==MusicKind.Personal && direction>0 && personalPlayback.Value==PersonalPlaybackMode.Shuffle) shuffle=true;
            if (n == 0) { c.Failed = true; Logger.LogWarning("Empty playlist: " + kind); return; }
            int index = c.Index < 0 ? (shuffle ? random.Next(n) : 0) : (c.Index + direction + n) % n;
            if (shuffle && c.Index >= 0 && n > 1) { index = random.Next(n - 1); if (index >= c.Index) index++; }
            QueueTrackIndex(kind, index);
        }
        private void QueueTrackIndex(MusicKind kind, int index)
        {
            var c = channels[kind];
            if (index < 0 || index >= c.Tracks.Count || c.Loading) return;
            c.PendingIndex = index; c.RestorePosition = false; c.Failed = false;
            if (c.Started || c.Source.isPlaying || active != kind) c.FadeInSeconds = FadeValue(kind + "TrackChange");
            if (active == kind && c.Source.isPlaying) c.Gain.Set(0, c.FadeInSeconds, Now);
            else ApplySelection(kind);
        }
        private void ApplySelection(MusicKind kind)
        {
            var c = channels[kind]; if (c.PendingIndex < 0 || c.Loading) return;
            var asset = c.Tracks[c.PendingIndex];
            if (!Valid(asset.Clip))
            {
                if (Now >= asset.NextRetry) RequestRecovery("selected " + kind, kind);
                else { c.Failed = true; Logger.LogWarning("Selected audio unavailable: " + asset.File); }
                return;
            }
            c.Source.Stop(); c.Index = c.PendingIndex; c.PendingIndex = -1;
            c.Source.clip = asset.Clip;
            if (!c.RestorePosition) { c.SavedPosition = 0; if (IsOutcome(kind)) outcome.Reset(); }
            c.RestorePosition = false; c.Started = false; c.EndingTrack = false;
            c.Gain.Set(0, 0, Now);
            if (active == kind && !faulted && enabledSetting.Value && !IsPaused(kind))
            {
                c.Source.volume = 0; c.Source.Play(); c.Source.time = Math.Min(c.SavedPosition, Math.Max(0, asset.Clip.length - 0.05f));
                c.Started = true; c.Gain.Set(1, c.FadeInSeconds, Now);
            }
            if(kind==MusicKind.Personal && trackToast.Value) ShowToast("Now playing: "+System.IO.Path.GetFileNameWithoutExtension(asset.File));
            Logger.LogInfo("Track " + kind + ": " + System.IO.Path.GetFileName(asset.File) + "; position=" + c.SavedPosition);
        }
        private void TickChannels(double now)
        {
            foreach (var pair in channels)
            {
                var c = pair.Value;
                if (c.Source.isPlaying && Valid(c.Source.clip))
                {
                    if (IsOutcome(pair.Key) && pair.Key == active && c.Source.time + 0.5 < outcome.Position)
                        c.Source.time = (float)Math.Min(outcome.Position, c.Source.clip.length - 0.01);
                    c.SavedPosition = c.Source.time;
                    if (IsOutcome(pair.Key) && pair.Key == active) outcome.Observe(c.SavedPosition, c.Source.clip.length);
                }
                c.Source.volume = master.Value * c.Volume.Value * c.Gain.Value(now);
                if (c.Gain.Target == 0 && c.Gain.Finished(now))
                {
                    c.Source.Pause();
                    if (c.ResetWhenSilent)
                    { c.Source.Stop(); c.Started = false; c.SavedPosition = 0; c.PendingIndex = -1; c.ResetWhenSilent = false; if (pair.Key != MusicKind.Personal && pair.Key != MusicKind.MainMenu) { c.Source.clip = null; c.Index = -1; } }
                    if (pair.Key == active && c.PendingIndex >= 0 && !IsPaused(pair.Key)) ApplySelection(pair.Key);
                }
            }
        }
        private void MaintainPlayback(double now)
        {
            if (active == MusicKind.None || IsPaused(active)) return;
            var c = channels[active];
            if (MaintainStartup(now)) return;
            if (c.Loading) return;
            if (c.Failed && active == MusicKind.Personal) { state.StopPersonal(); SwitchTo(state.Select(now,menuAllowed,false)); return; }
            if (IsOutcome(active))
            {
                double limit = active == MusicKind.Death ? deathLimit.Value : extractionLimit.Value;
                if (c.Failed || c.Tracks.Count == 0 || outcome.Complete(c.Source.isPlaying, limit))
                {
                    Logger.LogInfo("Outcome finished: " + active + "; position=" + outcome.Position + "; duration=" + outcome.Duration + "; cap=" + limit + "; failed=" + c.Failed);
                    state.FinishOutcome(); SwitchTo(state.Select(now, menuAllowed, false)); return;
                }
                // Fade the final seconds without shortening the track or starting menu music early.
                double fade = FadeValue(active == MusicKind.Death ? "DeathToMenu" : "ExtractionToMenu");
                if (outcome.Remaining(limit) <= fade && c.Gain.Target != 0) c.Gain.Set(0, outcome.Remaining(limit), now);
                if (!Valid(c.Source.clip) || !c.Source.isPlaying) EnsureActive();
                return;
            }
            if (c.PendingIndex >= 0) { EnsureActive(); return; }
            if (c.EndingTrack && !c.Source.isPlaying) { c.EndingTrack = false; QueueTrack(active, 1, active != MusicKind.Personal && active != MusicKind.MainMenu); return; }
            if (c.Source.isPlaying && Valid(c.Source.clip))
            {
                double remaining = c.Source.clip.length - c.Source.time;
                float seconds = FadeValue(active + "TrackChange");
                if (seconds > 0 && remaining <= seconds)
                { if (!c.EndingTrack) { c.EndingTrack = true; c.Gain.Set(0, remaining, now); } return; }
            }
            if (c.Started && Valid(c.Source.clip) && !c.Source.isPlaying && c.Gain.Target == 1 && c.SavedPosition >= c.Source.clip.length - 0.3f)
            { QueueTrack(active, 1, active != MusicKind.Personal && active != MusicKind.MainMenu); return; }
            EnsureActive();
        }
        private void BeginWorld(object candidate)
        {
            if (candidate == null || candidate.GetType().Name.IndexOf("Hideout", StringComparison.OrdinalIgnoreCase) >= 0 || ReferenceEquals(world, candidate)) return;
            var player = Read(candidate, "MainPlayer");
            if (player == null || !(Read(player, "IsYourPlayer") is bool local) || !local) return;
            RestoreStartupPlaylist(); state.MenuSelection=MusicKind.MainMenu;
            world = candidate; localPlayer = player; state.BeginRaid(); outcome.Reset(); pausedCategories.Clear();
            raidEpoch++; recoveryKinds.Clear(); recoveryRequested = false;
            foreach (var pair in channels.Where(x => x.Key != MusicKind.MainMenu))
            { var c = pair.Value; c.Source.Stop(); c.Source.clip = null; c.Gain.Set(0,0,Now); c.Index = -1; c.PendingIndex = -1; c.SavedPosition = 0; c.Started = false; c.Failed = false; c.ResetWhenSilent = false; c.EndingTrack = false; }
            // Preload one combat track after ambient so subsequent triggers can switch immediately.
            if(state.Enabled(MusicKind.RaidAmbient)) RequestRecovery("raid start", MusicKind.RaidAmbient); if(state.Enabled(MusicKind.Combat)) RequestRecovery("combat prewarm", MusicKind.Combat);
            Logger.LogInfo("Raid started: " + candidate.GetType().Name);
        }
        private void FinishRaid(string status)
        {
            if(state.ResultHandled) return;
            MusicKind result = RaidResultMusic(status);
            if (!state.EndRaid(result)) return;
            result=state.Outcome;
            if(string.Equals(status,"Runner",StringComparison.OrdinalIgnoreCase) || string.Equals(status,"RunThrough",StringComparison.OrdinalIgnoreCase))
                state.MenuSelection=runThroughMusic.Value==RunThroughMusic.Personal ? MusicKind.Personal : runThroughMusic.Value==RunThroughMusic.None ? MusicKind.None : MusicKind.MainMenu;
            else state.MenuSelection=MusicKind.MainMenu;
            if (result != MusicKind.None) { menuPaused = false; pausedCategories.Remove(result); } // Outcome completion resumes menu music.
            outcome.Reset(); if (result != MusicKind.None) { var c = channels[result]; c.Failed = false; c.Index = -1; c.PendingIndex = -1; c.SavedPosition = 0; }
            Logger.LogInfo("Raid result: " + status + " -> " + result);
        }
        private void CombatSignal(object shooter, bool damage)
        {
            if (!state.InRaid || localPlayer == null) return;
            bool mine = ReferenceEquals(shooter, localPlayer);
            if (damage) { if (!mine || !damageTrigger.Value) return; }
            else
            {
                double distance = 0;
                if (!mine) { if (!(shooter is Component other) || !(localPlayer is Component own)) return; distance = Vector3.Distance(other.transform.position, own.transform.position); }
                if (!CombatRules.Shot(mine, ownShots.Value, nearbyShots.Value, distance, combatRadius.Value)) return;
            }
            state.CombatEvent(Now, combatQuiet.Value);
            if (debug.Value) Logger.LogInfo("Combat trigger: " + (damage ? "damage" : mine ? "your shot" : "nearby shot"));
        }
        private void GrenadeSignal(Vector3 position)
        {
            if (!state.InRaid || !(localPlayer is Component own)) return;
            if (!CombatRules.Grenade(nearbyGrenades.Value, Vector3.Distance(position, own.transform.position), grenadeRadius.Value)) return;
            state.CombatEvent(Now, combatQuiet.Value);
            if (debug.Value) Logger.LogInfo("Combat trigger: nearby grenade explosion");
        }
        private AudioSource NativeSource() => Read(gui, "audioSource_3") as AudioSource;
        private void MuteNative(AudioSource source)
        {
            if (source == null) return;
            if (mutedNative != source) { RestoreNative(); mutedNative = source; priorNativeMute = source.mute; }
            source.mute = true;
        }
        private void RestoreNative() { if (mutedNative != null) mutedNative.mute = priorNativeMute; mutedNative = null; }
        private void OnDestroy()
        {
            RestoreStartupPlaylist(); ClearBossMusic();
            if(bossStaticClip!=null) Destroy(bossStaticClip);
            ClosePlayerUi(true);
            DestroyPlayerArtwork();
            DestroyNotificationSounds();
            harmony?.UnpatchSelf(); RestoreEnvironmentControls(); RestoreNative();
            if (hideoutRadio != null) Destroy(hideoutRadio);
            foreach (var c in channels.Values) { c.Source.Stop(); foreach (var asset in c.Tracks) asset.Dispose(); }
            if (instance == this) instance = null;
        }
    }
}
