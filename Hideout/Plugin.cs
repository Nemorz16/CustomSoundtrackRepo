using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CustomHideoutRadio
{
    public sealed partial class Plugin : MonoBehaviour
    {
        public const string Guid = "com.wrussell.customsoundtrack.hideout";
        private ConfigFile Config;
        private BepInEx.Logging.ManualLogSource Logger;
        private ConfigFile legacyConfig;
        private bool initialized;
        public ConfigEntry<float> VolumeSetting => radioVolume;
        private T Legacy<T>(string section, string key, T fallback)
            => legacyConfig == null ? fallback : legacyConfig.Bind(section, key, fallback).Value;
        private static Plugin instance;
        private readonly List<AudioClip> tracks = new List<AudioClip>();
        // Retain successful decoders alongside their clips until plugin shutdown.
        private readonly List<DownloadHandler> audioHandlers = new List<DownloadHandler>();
        private readonly List<string> trackFiles = new List<string>();
        private readonly List<float> loadedLengths = new List<float>();
        private int sharedTrack = -1;
        private int diagnosticCalls;
        private double sharedStart = -1;
        private readonly System.Random random = new System.Random();
        private Harmony harmony;
        private ConfigEntry<bool> enabledSetting;
        private ConfigEntry<bool> shuffle;
        private ConfigEntry<bool> diagnostics;
        private ConfigEntry<bool> debugCapture;
        private ConfigEntry<int> captureSeconds;
        private ConfigEntry<float> sampleInterval;
        private bool ready;
        private readonly List<Component> observedRadios = new List<Component>();
        private bool observationStarted;
        private double observationStart;

        public void Initialize(ConfigFile config, BepInEx.Logging.ManualLogSource logger)
        {
            if (initialized) return;
            Config = config; Logger = logger;
            string legacyPath = Path.Combine(Paths.ConfigPath, "com.wrussell.customhideoutradio.cfg");
            if (File.Exists(legacyPath))
            {
                try { legacyConfig = new ConfigFile(legacyPath, false) { SaveOnConfigSet = false }; }
                catch (Exception ex) { Logger.LogWarning("Could not read previous radio settings; using defaults: " + ex.Message); }
            }
            instance = this;
            ConfigureRadioAudio();
            enabledSetting = Config.Bind("Hideout General", "Enabled", Legacy("General", "Enabled", true), "Enable custom radios. Restart after changing.");
            shuffle = Config.Bind("Hideout General", "Shuffle", Legacy("General", "Shuffle", true), "Random order without immediate repeats; false uses alphabetical filename order.");
            diagnostics = Config.Bind("Hideout General", "LogEachTrack", Legacy("General", "LogEachTrack", true), "Log radio track changes.");
            debugCapture = Config.Bind("Hideout Advanced Debug", "Enabled", Legacy("Advanced Debug", "Enabled", false), "Record radio diagnostics. Restart for a new capture.");
            captureSeconds = Config.Bind("Hideout Advanced Debug", "CaptureSeconds", Legacy("Advanced Debug", "Length of the debug recording.", 120),
                new ConfigDescription("Capture duration in real seconds.", new AcceptableValueRange<int>(10, 600)));
            sampleInterval = Config.Bind("Hideout Advanced Debug", "SampleIntervalSeconds", Legacy("Advanced Debug", "Seconds between debug samples.", 1f),
                new ConfigDescription("Seconds between source snapshots.", new AcceptableValueRange<float>(0.25f, 10f)));
            initialized = true;
            if (!enabledSetting.Value) return;
            try
            {
                // Resolve at runtime, so the build does not redistribute or require Assembly-CSharp.
                // Refuse ambiguous signatures instead of patching an unrelated method.
                var candidates = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(SafeTypes)
                    .Where(t => t.Name == "AudioArray" && typeof(Component).IsAssignableFrom(t))
                    .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    .Where(m => m.Name == "PlayWithOffset" && m.GetParameters().Any(p => p.Name == "sound" && p.ParameterType == typeof(AudioClip)))
                    .ToArray();
                if (candidates.Length != 1)
                    throw new InvalidOperationException("Expected one AudioArray.PlayWithOffset(sound: AudioClip), found " + candidates.Length + ". No patch installed.");
                harmony = new Harmony(Guid);
                harmony.Patch(candidates[0], prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforePlay)),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(ObservePlayback)));
                Logger.LogInfo("Radio hook: " + candidates[0].DeclaringType.FullName + "." + candidates[0]);
                InstallNavigation(candidates[0]);
                StartCoroutine(LoadMusic());
            }
            catch (Exception ex) { Logger.LogError("Radio initialization failed; vanilla audio retained. " + ex); }
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch { return Array.Empty<Type>(); }
        }

        private IEnumerator LoadMusic()
        {
            string folder = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "Music", "Hideout");
            string[] files;
            try
            {
                Directory.CreateDirectory(folder);
                files = Directory.GetFiles(folder).Where(f =>
                    CustomSoundtrack.AudioFormats.Supported(f))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception ex) { Logger.LogError("Cannot read Music folder: " + ex.Message); yield break; }
            foreach (string file in files) yield return LoadTrack(file);
            yield return null;

            // No playlist exists yet: recover early startup data loss before choosing a song.
            // One attempt per admitted track, never an unbounded retry loop.
            int initialCount = tracks.Count;
            int attempted = 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (ValidateTrack(i, "post-load audit")) continue;
                attempted++;
                Logger.LogWarning("Startup recovery: reloading " + Path.GetFileName(trackFiles[i]));
                yield return LoadTrack(trackFiles[i], i);
            }
            yield return null;
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                if (ValidateTrack(i, "recovery audit")) continue;
                Logger.LogWarning("Startup recovery: excluded for this session: " +
                    Path.GetFileName(trackFiles[i]) + ". File unchanged; restart to retry.");
                if (tracks[i] != null) Destroy(tracks[i]);
                audioHandlers[i].Dispose();
                tracks.RemoveAt(i); loadedLengths.RemoveAt(i);
                audioHandlers.RemoveAt(i); trackFiles.RemoveAt(i);
            }
            Logger.LogInfo("Startup recovery complete: " + tracks.Count + "/" + initialCount +
                " admitted tracks valid; reload attempts=" + attempted + "; excluded=" + (initialCount - tracks.Count));
            ready = tracks.Count > 0;
            Logger.LogInfo("Loaded " + tracks.Count + " custom radio track(s). " +
                (ready ? "Ready: enter hideout now." : "No playable custom tracks; vanilla radio retained. See loading warnings."));
        }

        private IEnumerator LoadTrack(string file, int replaceIndex = -1)
        {
            AudioType type = CustomSoundtrack.AudioFormats.Type(file);
            UnityWebRequest request = null;
            try { request = UnityWebRequestMultimedia.GetAudioClip(new Uri(Path.GetFullPath(file)).AbsoluteUri, type); }
            catch (Exception ex) { Logger.LogWarning("Cannot open " + Path.GetFileName(file) + ": " + ex.Message); }
            if (request == null) yield break;
            using (request)
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { Logger.LogWarning("Cannot load " + Path.GetFileName(file) + ": " + request.error); yield break; }
                AudioClip clip = null;
                bool adopted = false;
                try
                {
                    clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip == null) throw new InvalidDataException("Decoder returned no audio clip");
                    clip.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    if (!ClipGuard.ValidDuration(clip.length) || clip.samples <= 0 || clip.frequency <= 0 ||
                        clip.loadState != AudioDataLoadState.Loaded)
                        throw new InvalidDataException("Invalid decoded audio: duration=" + clip.length +
                            "; samples=" + clip.samples + "; frequency=" + clip.frequency + "; state=" + clip.loadState);
                    clip.name = "CustomRadio: " + Path.GetFileName(file);
                    if (replaceIndex < 0)
                    {
                        tracks.Add(clip); loadedLengths.Add(clip.length);
                        audioHandlers.Add(request.downloadHandler); trackFiles.Add(file);
                    }
                    else
                    {
                        var oldClip = tracks[replaceIndex];
                        var oldHandler = audioHandlers[replaceIndex];
                        tracks[replaceIndex] = clip; loadedLengths[replaceIndex] = clip.length;
                        audioHandlers[replaceIndex] = request.downloadHandler;
                        if (oldClip != null) Destroy(oldClip);
                        oldHandler.Dispose();
                    }
                    request.disposeDownloadHandlerOnDispose = false;
                    adopted = true;
                    Logger.LogInfo((replaceIndex < 0 ? "Audio loaded: " : "Audio reloaded: ") + clip.name +
                        "; duration=" + clip.length + "; samples=" + clip.samples + "; frequency=" + clip.frequency);
                }
                catch (Exception ex) { Logger.LogWarning("Cannot load " + Path.GetFileName(file) + ": " + ex.Message); }
                finally { if (!adopted && clip != null) Destroy(clip); }
            }
        }

        // Change only the input clip; the game's radio still handles volume, position and on/off.
        // Supply the shared position through the native offset argument; keep native callbacks intact.
        private static void BeforePlay(Component __instance, ref AudioClip sound, ref float offset, bool volumetric, ref Action onFinish, ref Action onCancel)
        {
            var plugin = instance;
            if (plugin == null || !plugin.enabledSetting.Value || __instance == null ||
                __instance.gameObject.name != "BoomboxAudio") return;
            try
            {
                if (!plugin.ready)
                {
                    plugin.RememberStartupRequest(__instance, sound, volumetric, onFinish, ref onCancel);
                    return;
                }
                plugin.pendingStartup.Remove(__instance);
                if (!plugin.observedRadios.Contains(__instance)) plugin.observedRadios.Add(__instance);
                bool logThisCall = plugin.diagnostics.Value && plugin.diagnosticCalls++ < 20;
                if (logThisCall) plugin.Logger.LogInfo("Position test: entered radio replacement.");
                // Shared selection is controlled by menu commands and automatic advancement.
                // No extra patches, per-frame updates or direct AudioSource control.
                if (plugin.sharedTrack < 0)
                {
                    plugin.EnsurePlaylist();
                    plugin.Logger.LogInfo("Position test: selected shared track #" + (plugin.sharedTrack + 1));
                }
                int index = plugin.sharedTrack;
                var replacement = plugin.tracks[index];
                if (!plugin.ValidateTrack(index, "playback"))
                {
                    plugin.RememberStartupRequest(__instance, sound, volumetric, onFinish, ref onCancel);
                    plugin.RequestRadioRecovery();
                    return;
                }
                if (logThisCall) plugin.Logger.LogInfo("Position test: calculating shared offset; original offset=" + offset);
                double now = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
                float sharedOffset;
                string positionSource;
                if (plugin.navigationRestart)
                {
                    sharedOffset = 0;
                    positionSource = "menu restart";
                }
                else if (plugin.TryPeerPosition(__instance, replacement, volumetric, out sharedOffset))
                    positionSource = "playing peer";
                else
                {
                    sharedOffset = PlaybackPosition.GetOffset(now, replacement.length, ref plugin.sharedStart);
                    positionSource = "fallback clock (no eligible playing peer)";
                }
                sound = replacement;
                offset = sharedOffset;
                plugin.WrapCompletion(__instance, volumetric, ref onFinish, ref onCancel);
                if (logThisCall) plugin.Logger.LogInfo("Position test: assigned " + replacement.name + "; shared offset=" + sharedOffset.ToString("F3") + "s; position source=" + positionSource + "; returning to native playback.");
            }
            catch (Exception ex) { plugin.Logger.LogWarning("Radio replacement failed: " + ex.Message); }
        }

        private bool ValidateTrack(int index, string operation)
        {
            if (index < 0 || index >= tracks.Count) return false;
            var clip = tracks[index];
            if (clip == null)
            {
                Logger.LogWarning("Audio rejected during " + operation + ": track #" + (index + 1) + " was destroyed; loaded duration=" + loadedLengths[index]);
                return false;
            }
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            float currentLength = clip.length;
            if (ClipGuard.ValidDuration(currentLength) && clip.loadState == AudioDataLoadState.Loaded) return true;
            Logger.LogWarning("Audio rejected during " + operation + ": " + clip.name +
                "; loaded duration=" + loadedLengths[index] + "; current duration=" + currentLength +
                "; state=" + clip.loadState + "; samples=" + clip.samples + "; frequency=" + clip.frequency);
            return false;
        }

        private bool TryPeerPosition(Component joiningRadio, AudioClip expectedClip, bool volumetric, out float position)
        {
            position = 0;
            foreach (var radio in observedRadios.ToArray())
            {
                if (radio == null) { observedRadios.Remove(radio); continue; }
                if (radio == joiningRadio) continue;
                try
                {
                    var getter = radio.GetType().GetMethod("GetSources", new[] { typeof(bool) });
                    var sources = getter?.Invoke(radio, new object[] { volumetric }) as IEnumerable<AudioSource>;
                    if (sources == null) continue;
                    foreach (var source in sources)
                    {
                        if (source == null || !source.isActiveAndEnabled || !source.isPlaying || source.clip != expectedClip) continue;
                        float actualPosition = source.time;
                        if (!PeerPosition.IsUsable(actualPosition, expectedClip.length, source.pitch)) continue;
                        position = actualPosition;
                        return true;
                    }
                }
                catch (Exception ex) { Logger.LogWarning("Peer position read failed; trying fallback. " + ex.Message); }
            }
            return false;
        }

        // Read-only observation: inspect the sources returned by the game's own
        // GetSources(bool), including child/2D sources missed by GetComponents.
        private static void ObservePlayback(Component __instance, AudioClip sound, float offset, bool volumetric)
        {
            var plugin = instance;
            if (plugin == null || !plugin.ready || !plugin.debugCapture.Value || __instance == null ||
                __instance.gameObject.name != "BoomboxAudio") return;
            try
            {
                if (!plugin.observedRadios.Contains(__instance)) plugin.observedRadios.Add(__instance);
                plugin.Logger.LogInfo("OBS request radio=" + __instance.GetInstanceID() + " volumetric=" + volumetric +
                    " requested=" + offset.ToString("F3") + "s clip=" + (sound == null ? "null" : sound.name));
                if (!plugin.observationStarted)
                {
                    plugin.observationStarted = true;
                    plugin.observationStart = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
                    plugin.StartCoroutine(plugin.ObserveForTwoMinutes());
                }
            }
            catch (Exception ex) { plugin.Logger.LogWarning("OBS setup failed: " + ex); }
        }

        private IEnumerator ObserveForTwoMinutes()
        {
            // Delay to avoid reading halfway through the game's initial audio setup.
            yield return null;
            double end = observationStart + captureSeconds.Value;
            while (ready && debugCapture.Value &&
                (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency < end)
            {
                LogObservedSources();
                yield return new WaitForSecondsRealtime(sampleInterval.Value);
            }
            Logger.LogInfo("OBS finished or disabled; restart game for another observation window.");
        }

        private void LogObservedSources()
        {
            double now = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
            foreach (var radio in observedRadios.ToArray())
            {
                if (radio == null) { observedRadios.Remove(radio); continue; }
                try
                {
                    var getter = radio.GetType().GetMethod("GetSources", new[] { typeof(bool) });
                    if (getter == null) { Logger.LogWarning("OBS missing GetSources"); continue; }
                    foreach (bool spatial in new[] { true, false })
                    {
                        var sources = getter.Invoke(radio, new object[] { spatial }) as IEnumerable<AudioSource>;
                        if (sources == null) continue;
                        foreach (var source in sources)
                        {
                            if (source == null) continue;
                            var clip = source.clip;
                            // All source accesses here are getters. Never Play/Stop/seek/set loop.
                            Logger.LogInfo("OBS t=" + (now - observationStart).ToString("F3") +
                                " radio=" + radio.GetInstanceID() + " source=" + source.GetInstanceID() +
                                " spatial=" + spatial + " playing=" + source.isPlaying +
                                " active=" + source.isActiveAndEnabled + " time=" + source.time.ToString("F3") +
                                " samples=" + source.timeSamples + " pitch=" + source.pitch +
                                " volume=" + source.volume + " mute=" + source.mute + " loop=" + source.loop +
                                " clip=" + (clip == null ? "null" : clip.name));
                        }
                    }
                }
                catch (Exception ex) { Logger.LogWarning("OBS read failed: " + ex); }
            }
        }

        private void OnDestroy()
        {
            ready = false;
            RestoreRadioAudio();
            harmony?.UnpatchSelf();
            // Normal supported removal is with the game closed. Clips can still be referenced
            // by live radio sources if a third-party tool unloads this plugin dynamically.
            if (instance == this) instance = null;
            // These clips are owned by the plugin and pinned against unused-asset cleanup.
            foreach (var clip in tracks) if (clip != null) Destroy(clip);
            tracks.Clear(); loadedLengths.Clear(); trackFiles.Clear();
            foreach (var handler in audioHandlers) handler.Dispose();
            audioHandlers.Clear();
        }
    }
}
