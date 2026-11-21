using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace CustomHideoutRadio
{
    public sealed partial class Plugin
    {
        private sealed class PendingStartup
        {
            public AudioClip OriginalClip;
            public bool Spatial;
            public Action Finish, Cancel;
            public readonly PlaybackTicket Ticket = new PlaybackTicket(0);
            public double Deadline = -1;
        }
        private readonly Dictionary<Component, PendingStartup> pendingStartup = new Dictionary<Component, PendingStartup>();

        private void RememberStartupRequest(Component radio, AudioClip sound, bool spatial, Action finish, ref Action cancel)
        {
            var pending = new PendingStartup { OriginalClip = sound, Spatial = spatial, Finish = finish, Cancel = cancel };
            pendingStartup[radio] = pending;
            cancel = () => { pending.Ticket.Cancel(); pending.Cancel?.Invoke(); };
            if (diagnostics.Value) Logger.LogInfo("Startup handover: radio requested playback before custom tracks were ready; remembering active request.");
            // Original sound, offset and finish callback are untouched while loading.
        }

        private void TryStartupHandover()
        {
            if (!ready || pendingStartup.Count == 0) return;
            double now = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
            foreach (var entry in pendingStartup.ToArray())
            {
                var pending = entry.Value;
                if (entry.Key == null || !enabledSetting.Value || !pending.Ticket.IsCurrent(0))
                { pendingStartup.Remove(entry.Key); continue; }
                try
                {
                    if (pending.Deadline < 0) pending.Deadline = now + 5;
                    var source = Sources(entry.Key, pending.Spatial).FirstOrDefault(s => s != null &&
                        s.isActiveAndEnabled && s.isPlaying && s.clip == pending.OriginalClip);
                    if (source == null)
                    {
                        if (now >= pending.Deadline)
                        {
                            pendingStartup.Remove(entry.Key);
                            Logger.LogInfo("Startup handover: no matching active original playback; leaving radio untouched.");
                        }
                        continue;
                    }
                    // Remove before calling native playback; its Stop cancels the old request.
                    pendingStartup.Remove(entry.Key);
                    EnsurePlaylist();
                    if (!ValidateTrack(sharedTrack, "startup handover"))
                    {
                        Logger.LogWarning("Radio handover retained vanilla audio because the recovered track is still invalid; toggle the radio after 30 seconds to retry.");
                        continue;
                    }
                    nativePlay.Invoke(entry.Key, new object[] { tracks[sharedTrack], pending.Spatial, 0f, pending.Finish, pending.Cancel });
                    Logger.LogInfo("Startup handover: switched an already-playing radio to the custom playlist.");
                }
                catch (Exception ex)
                {
                    pendingStartup.Remove(entry.Key);
                    Logger.LogWarning("Startup handover failed; existing playback retained where possible. " + ex);
                }
            }
        }
    }
}
