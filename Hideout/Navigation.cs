using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace CustomHideoutRadio
{
    public sealed partial class Plugin
    {
        private sealed class ReplayContext
        {
            public readonly bool Spatial;
            public readonly Action Finish, Cancel;
            public PlaybackTicket Ticket;
            public bool ObservedPlaying;
            public ReplayContext(bool spatial, Action finish, Action cancel)
            { Spatial = spatial; Finish = finish; Cancel = cancel; }
        }
        private readonly Dictionary<Component, ReplayContext> replayContexts = new Dictionary<Component, ReplayContext>();
        private MethodInfo nativePlay;
        private Type radioType, applianceType, actionType;
        private FieldInfo actionName, actionCallback;
        private PlaylistHistory playlist;
        private bool navigationRestart;
        private double lastMenuRestart = double.NegativeInfinity;

        private void InstallNavigation(MethodInfo play)
        {
            nativePlay = play;
            radioType = play.DeclaringType;
            try
            {
                var assembly = radioType.Assembly;
                applianceType = assembly.GetType("EFT.Interactive.Appliance", true);
                actionType = assembly.GetType("EFT.UI.InteractionAction", true);
                actionName = AccessTools.Field(actionType, "Name");
                actionCallback = AccessTools.Field(actionType, "Action");
                var helper = assembly.GetType("EFT.InteractionContextHelper", true);
                var method = AccessTools.Method(helper, "GetAvailableActions", new[] { applianceType });
                if (method == null || actionName == null || actionCallback == null)
                    throw new MissingMemberException("Radio menu API differs from the inspected client.");
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(Plugin), nameof(AddRadioActions)));
                Logger.LogInfo("Radio playlist menu patch installed.");
                InstallLinkedRadioSwitches();
            }
            catch (Exception ex) { Logger.LogError("Menu setup failed; ordinary radio playback remains available. " + ex); }
        }

        private void EnsurePlaylist()
        {
            if (playlist == null) playlist = new PlaylistHistory(tracks.Count, shuffle.Value, random);
            sharedTrack = playlist.Current;
        }

        private Component RadioForAppliance(Component appliance)
        {
            var objects = AccessTools.Field(applianceType, "_ambianceObjects")?.GetValue(appliance) as IEnumerable;
            if (objects == null) return null;
            foreach (var item in objects)
            {
                var component = item as Component;
                if (component == null) continue;
                var radio = component.GetComponent(radioType);
                if (radio != null && radio.gameObject.name == "BoomboxAudio") return radio;
            }
            return null;
        }

        private static void AddRadioActions(object appliance, object __result)
        {
            var plugin = instance;
            if (plugin == null || !plugin.ready || !plugin.enabledSetting.Value || __result == null) return;
            try
            {
                var target = appliance as Component;
                if (target == null || plugin.RadioForAppliance(target) == null) return;
                var type = __result.GetType();
                var actions = (AccessTools.Field(type, "Actions")?.GetValue(__result) ??
                    AccessTools.Property(type, "Actions")?.GetValue(__result)) as IList;
                if (actions == null) return;
                plugin.ConfigureRadioSwitchActions(actions, target);
                plugin.AddAction(actions, "Next track", () => plugin.Navigate(false));
                plugin.AddAction(actions, "Previous track", () => plugin.Navigate(true));
            }
            catch (Exception ex) { plugin.Logger.LogWarning("Radio menu failed: " + ex); }
        }

        private void AddAction(IList actions, string label, Action callback)
        {
            foreach (var existing in actions)
                if ((string)actionName.GetValue(existing) == label) return;
            var action = Activator.CreateInstance(actionType);
            actionName.SetValue(action, label);
            actionCallback.SetValue(action, callback);
            actions.Add(action);
        }

        private IEnumerable<AudioSource> Sources(Component radio, bool spatial)
        {
            var getter = radio.GetType().GetMethod("GetSources", new[] { typeof(bool) });
            return getter?.Invoke(radio, new object[] { spatial }) as IEnumerable<AudioSource> ?? Array.Empty<AudioSource>();
        }

        private bool Navigate(bool previous, bool automatic = false)
        {
            if (!ready || !enabledSetting.Value || navigationRestart) return false;
            try
            {
                EnsurePlaylist();
                var playing = new List<KeyValuePair<Component, ReplayContext>>();
                double position = 0;
                foreach (var entry in replayContexts.ToArray())
                {
                    if (entry.Key == null) { replayContexts.Remove(entry.Key); continue; }
                    var source = Sources(entry.Key, entry.Value.Spatial).FirstOrDefault(s =>
                        s != null && s.isActiveAndEnabled && s.clip == tracks[sharedTrack] &&
                        entry.Value.Ticket != null && entry.Value.Ticket.IsCurrent(playlistGeneration));
                    if (source == null) continue;
                    if (playing.Count == 0) position = source.time;
                    playing.Add(entry);
                }
                if (automatic && playing.Count == 0) return false;
                double now = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
                // An immediate second Previous must step back even if native playback
                // has not exposed its new zero position yet.
                if (now - lastMenuRestart < 3) position = 0;
                // Reject an unusable destination before altering current playback/history.
                var savedHistory = playlist.Capture();
                int candidate = previous ? playlist.Previous(position) : playlist.Next(shuffle.Value);
                if (!ValidateTrack(candidate, "playlist selection"))
                {
                    playlist.Restore(savedHistory);
                    RequestRadioRecovery();
                    Logger.LogWarning("Playlist selection rejected; current track/history retained.");
                    return false;
                }
                playlistGeneration++;
                sharedTrack = candidate;
                sharedStart = playing.Count > 0 ? now : -1;
                lastMenuRestart = now;
                navigationRestart = true;
                try
                {
                    foreach (var entry in playing)
                    {
                        if (entry.Key == null) continue;
                        // Use native Stop/cancel/start behaviour, preserving its callbacks.
                        // Only already-playing radios are restarted; off radios stay off.
                        nativePlay.Invoke(entry.Key, new object[] { tracks[sharedTrack], entry.Value.Spatial, 0f,
                            entry.Value.Finish, entry.Value.Cancel });
                    }
                }
                finally { navigationRestart = false; }
                Logger.LogInfo((automatic ? "Playlist automatic: " : "Playlist menu: ") + (previous ? "Previous/restart" : "Next") +
                    " -> " + tracks[sharedTrack].name + "; restarted " + playing.Count + " active radio(s).");
                return true;
            }
            catch (Exception ex) { Logger.LogError("Playlist navigation failed: " + ex); return false; }
        }
    }
}
