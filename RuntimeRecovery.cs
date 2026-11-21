using System.Collections;
using System.Collections.Generic;
using System.Linq;
namespace CustomSoundtrack
{
    public sealed partial class Plugin
    {
        private bool recoveryRequested, recovering;
        private readonly HashSet<MusicKind> recoveryKinds = new HashSet<MusicKind>();
        private void RequestRecovery(string reason, MusicKind kind = MusicKind.None)
        {
            if (kind == MusicKind.None) return; // No global reload on raid disposal.
            recoveryKinds.Add(kind); recoveryRequested = true;
            if (debug.Value) Logger.LogInfo("Audio requested: " + kind + "; " + reason);
        }
        private IEnumerator RecoverRuntime()
        {
            if (recovering) yield break;
            recovering = true;
            try
            {
                while (recoveryKinds.Count > 0)
                {
                    var kind = recoveryKinds.Contains(active) ? active : recoveryKinds.First();
                    recoveryKinds.Remove(kind);
                    var c = channels[kind];
                    if (c.Tracks.Count == 0) { c.Failed = true; continue; }
                    if (c.PendingIndex < 0) c.PendingIndex = c.Index >= 0 ? c.Index : kind == MusicKind.Personal ? 0 : random.Next(c.Tracks.Count);
                    int epoch = raidEpoch; bool cancelled = false;
                    c.Loading = true;
                    try
                    {
                        int original = c.PendingIndex;
                        bool valid = false;
                        for (int attempt = 0; attempt < c.Tracks.Count; attempt++)
                        {
                            int index = (original + attempt) % c.Tracks.Count;
                            var asset = c.Tracks[index];
                            if (!Valid(asset.Clip) && Now >= asset.NextRetry)
                            {
                                Logger.LogInfo("Preparing " + kind + ": " + System.IO.Path.GetFileName(asset.File));
                                yield return Decode(asset);
                                if (epoch != raidEpoch || faulted) { if(!c.Tracks.Contains(asset)) asset.Dispose(); cancelled = true; break; }
                                asset.NextRetry = AudioRetryRules.AfterDecode(Valid(asset.Clip), double.IsPositiveInfinity(asset.NextRetry), Now);
                            }
                            if (Valid(asset.Clip)) { c.PendingIndex = index; valid = true; break; }
                            // A played outcome must not silently change to a different random song.
                            if (IsOutcome(kind) && outcome.Position > 0) break;
                        }
                        if (cancelled) continue;
                        c.Failed = !valid;
                        if (!valid) { c.PendingIndex = -1; Logger.LogWarning("No playable selection for " + kind + "; stopped until another request/raid."); }
                    }
                    finally { c.Loading = false; }
                    if (!c.Failed && !c.Source.isPlaying) ApplySelection(kind);
                    yield return null;
                }
            }
            finally { recovering = false; recoveryRequested = recoveryKinds.Count > 0; }
        }
    }
}
