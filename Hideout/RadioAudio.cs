using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
namespace CustomHideoutRadio
{
    public sealed partial class Plugin
    {
        private ConfigEntry<float> radioVolume, directionalGain, directionalReach;
        private ConfigEntry<bool> wholeHideout;
        private bool audioControlsFailed;
        private sealed class SourceAudio
        {
            public float NativeVolume, NativeBlend, AppliedVolume, AppliedBlend, NativeMin, NativeMax;
        }
        private readonly Dictionary<AudioSource, SourceAudio> controlledAudio = new Dictionary<AudioSource, SourceAudio>();
        private void ConfigureRadioAudio()
        {
            directionalGain=Config.Bind("Hideout Audio","DirectionalGain",1f,new ConfigDescription("Directional radio loudness. Unity caps the final source volume at 1.",new AcceptableValueRange<float>(0,4)));
            directionalReach=Config.Bind("Hideout Audio","DirectionalReach",1f,new ConfigDescription("Multiply native audible range while keeping directional sound. 1 = vanilla.",new AcceptableValueRange<float>(1,20)));
            radioVolume = Config.Bind("Hideout Audio", "Volume", Legacy("Audio", "Volume", 1f),
                new ConfigDescription("Radio volume. Game hideout/master volume still applies.", new AcceptableValueRange<float>(0f, 1f)));
            wholeHideout = Config.Bind("Hideout Audio", "WholeHideoutPlayback", Legacy("Audio", "On: room-wide audio and linked off switches. Off: directional radios.", true),
                "ON: whole-hideout music without distance attenuation; turning either radio off switches both off. One source is audible to avoid doubling. OFF: original directional radio sound with independent switches. Applies live; Volume controls either mode.");
        }
        private void LateUpdate()
        {
            if (!initialized || audioControlsFailed) return;
            if (!ready || !enabledSetting.Value) { RestoreRadioAudio(); return; }
            try
            {
                // Only the game's sources currently playing this plugin's custom clips.
                var active = replayContexts.Where(p => p.Key != null && p.Value.Ticket != null && p.Value.Ticket.IsCurrent(playlistGeneration))
                    .SelectMany(p => Sources(p.Key, p.Value.Spatial))
                    .Where(s => s != null && s.isActiveAndEnabled && s.isPlaying && s.clip != null && tracks.Contains(s.clip))
                    .Distinct().OrderBy(s => s.GetInstanceID()).ToArray();
                foreach (var source in controlledAudio.Keys.ToArray())
                    if (source == null || !active.Contains(source)) ReleaseRadioSource(source);
                AudioSource audible = active.FirstOrDefault();
                foreach (var source in active)
                {
                    if (!controlledAudio.TryGetValue(source, out var saved))
                    {
                        saved = new SourceAudio { NativeVolume = source.volume, NativeBlend = source.spatialBlend,
                            AppliedVolume = source.volume, AppliedBlend = source.spatialBlend, NativeMin=source.minDistance, NativeMax=source.maxDistance };
                        controlledAudio.Add(source, saved);
                    }
                    // Follow native fades/changes without multiplying our previous output again.
                    if (!Mathf.Approximately(source.volume, saved.AppliedVolume)) saved.NativeVolume = source.volume;
                    if (!Mathf.Approximately(source.spatialBlend, saved.AppliedBlend)) saved.NativeBlend = source.spatialBlend;
                    saved.AppliedBlend = wholeHideout.Value ? 0f : saved.NativeBlend;
                    saved.AppliedVolume = Mathf.Clamp01(saved.NativeVolume * radioVolume.Value * CustomSoundtrack.Plugin.MasterGain *
                        (wholeHideout.Value ? (source != audible ? 0f : 1f) : directionalGain.Value));
                    source.minDistance=saved.NativeMin*(wholeHideout.Value ? 1 : directionalReach.Value);
                    source.maxDistance=saved.NativeMax*(wholeHideout.Value ? 1 : directionalReach.Value);
                    source.spatialBlend = saved.AppliedBlend;
                    source.volume = saved.AppliedVolume;
                }
            }
            catch (Exception ex)
            {
                // A volume/position failure must not stop native playback or the playlist.
                audioControlsFailed = true;
                RestoreRadioAudio();
                Logger.LogWarning("Radio audio controls disabled for this session; native audio settings restored. " + ex.Message);
            }
        }
        private void ReleaseRadioSource(AudioSource source)
        {
            if (!controlledAudio.TryGetValue(source, out var saved)) return;
            if (source != null)
            {
                source.minDistance=saved.NativeMin; source.maxDistance=saved.NativeMax;
                if (Mathf.Approximately(source.volume, saved.AppliedVolume)) source.volume = saved.NativeVolume;
                if (Mathf.Approximately(source.spatialBlend, saved.AppliedBlend)) source.spatialBlend = saved.NativeBlend;
            }
            controlledAudio.Remove(source);
        }
        private void RestoreRadioAudio()
        {
            foreach (var source in controlledAudio.Keys.ToArray())
            {
                try { ReleaseRadioSource(source); }
                catch { controlledAudio.Remove(source); }
            }
        }
    }
}
