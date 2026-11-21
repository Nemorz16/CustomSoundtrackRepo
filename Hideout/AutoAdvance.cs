using System;
using System.Collections.Concurrent;
using System.Linq;
using UnityEngine;
namespace CustomHideoutRadio
{
    public sealed partial class Plugin
    {
        private sealed class CompletedPlayback
        {
            public readonly Component Radio;
            public readonly ReplayContext Context;
            public CompletedPlayback(Component radio, ReplayContext context) { Radio = radio; Context = context; }
        }
        private long playlistGeneration;
        private double nextEndObservation;
        private readonly ConcurrentQueue<CompletedPlayback> completed = new ConcurrentQueue<CompletedPlayback>();

        private void WrapCompletion(Component radio, bool spatial, ref Action finish, ref Action cancel)
        {
            var context = new ReplayContext(spatial, finish, cancel) { Ticket = new PlaybackTicket(playlistGeneration) };
            replayContexts[radio] = context;
            // Async callbacks only queue work or invalidate their own request.
            // All game-object checks and native playback calls happen in Update.
            finish = () =>
            {
                if (context.Ticket.QueueOnce()) completed.Enqueue(new CompletedPlayback(radio, context));
            };
            cancel = () =>
            {
                context.Ticket.Cancel();
                context.Cancel?.Invoke();
            };
        }

        private void ObserveNaturalEnds()
        {
            if (!ready || sharedTrack < 0 || navigationRestart) return;
            double now = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
            if (now < nextEndObservation) return;
            nextEndObservation = now + 0.1;
            foreach (var entry in replayContexts.ToArray())
            {
                if (entry.Key == null || entry.Value.Ticket == null || !entry.Value.Ticket.IsCurrent(playlistGeneration)) continue;
                try
                {
                    var sources = Sources(entry.Key, entry.Value.Spatial).Where(s => s != null &&
                        s.isActiveAndEnabled && s.clip == tracks[sharedTrack]).ToArray();
                    if (sources.Length == 0) continue;
                    if (sources.Any(s => s.isPlaying)) entry.Value.ObservedPlaying = true;
                    else if (entry.Value.ObservedPlaying && entry.Value.Ticket.QueueOnce())
                        completed.Enqueue(new CompletedPlayback(entry.Key, entry.Value));
                }
                catch (Exception ex)
                {
                    // Back off after a read failure; native finish remains available.
                    entry.Value.ObservedPlaying = false;
                    Logger.LogWarning("Natural-end observation failed: " + ex.Message);
                    nextEndObservation = now + 10;
                    break;
                }
            }
        }

        private void Update()
        {
            if (!initialized) return;
            TryStartupHandover();
            ObserveNaturalEnds();
            // Drain a bounded amount of callback work, with no timers or source seeking.
            // A successful transition returns immediately: at most one automatic advance/frame.
            for (int count = 0; count < 16 && completed.TryDequeue(out var item); count++)
            {
                if (!ready || item.Radio == null || !item.Context.Ticket.IsCurrent(playlistGeneration)) continue;
                if (!replayContexts.TryGetValue(item.Radio, out var current) || !ReferenceEquals(current, item.Context)) continue;
                try
                {
                    // A source naturally stops at the end, so isPlaying is NOT required.
                    // Explicit off/Stop cancels the ticket; disabled sources are also excluded.
                    var activeSources = Sources(item.Radio, item.Context.Spatial).Where(s => s != null &&
                        s.isActiveAndEnabled && s.clip == tracks[sharedTrack]).ToArray();
                    bool active = activeSources.Length > 0;
                    if (activeSources.Any(s => s.isPlaying))
                    {
                        // At pitch < 1 the native duration timer can fire before audio ends.
                        item.Context.Ticket.AllowRequeue();
                        continue;
                    }
                    if (!active) { item.Context.Ticket.Cancel(); continue; }
                    if (!enabledSetting.Value)
                    {
                        item.Context.Finish?.Invoke();
                        continue;
                    }
                    // A stopped source alone is not evidence that a song played to completion.
                    if (!ClipGuard.CanComplete(item.Context.ObservedPlaying, tracks[sharedTrack] == null ? 0 : tracks[sharedTrack].length))
                    {
                        item.Context.Ticket.Cancel();
                        Logger.LogWarning("Automatic advance suppressed: playback was never observed or clip duration became invalid. Keeping playlist history.");
                        continue;
                    }
                    if (!ValidateTrack(sharedTrack, "completion")) { item.Context.Ticket.Cancel(); continue; }
                    if (Navigate(false, true)) return;
                }
                catch (Exception ex) { Logger.LogError("Automatic playlist advance failed: " + ex); }
            }
        }
    }
}
