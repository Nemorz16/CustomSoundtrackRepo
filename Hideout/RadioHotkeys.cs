using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace CustomHideoutRadio
{
    public sealed partial class Plugin
    {
        private float hotkeyResumePosition;
        private bool hotkeyPaused;
        public void HandleTransport(int command)
        {
            if (!initialized || !enabledSetting.Value || applianceType == null || applianceEnabled == null || applianceTurnOff == null) return;
            try
            {
                var appliances = Resources.FindObjectsOfTypeAll(applianceType).OfType<Component>()
                    .Where(a => a != null && a.gameObject.scene.IsValid() && a.gameObject.scene.isLoaded && RadioForAppliance(a) != null).ToArray();
                if (appliances.Length == 0) return;
                if (command == -1 || command == 1)
                {
                    if (Navigate(command == -1)) { hotkeyResumePosition = 0; hotkeyPaused = false; }
                    return;
                }
                bool on = appliances.Any(a => applianceEnabled.GetValue(a) is bool value && value);
                if (command == 2 || on)
                {
                    hotkeyResumePosition = 0;
                    if (command != 2 && sharedTrack >= 0 && sharedTrack < tracks.Count)
                    {
                        var source = appliances.Select(RadioForAppliance).SelectMany(r => Sources(r,false).Concat(Sources(r,true)))
                            .FirstOrDefault(s => s != null && s.isPlaying && s.clip == tracks[sharedTrack]);
                        if (source != null) hotkeyResumePosition = source.time;
                    }
                    hotkeyPaused = command != 2;
                    // Explicit keyboard Stop/Pause affects BOTH radios in either audio mode.
                    TurnOffRadioGroup(null);
                    Logger.LogInfo(command == 2 ? "Radio hotkey: stopped both radios." : "Radio hotkey: paused both radios.");
                    return;
                }
                var turnOn = AccessTools.Method(applianceType,"TurnOn",Type.EmptyTypes);
                if (turnOn == null) throw new MissingMethodException("Appliance.TurnOn");
                if (ready)
                {
                    EnsurePlaylist();
                    double now = (double)System.Diagnostics.Stopwatch.GetTimestamp()/System.Diagnostics.Stopwatch.Frequency;
                    sharedStart = now - (hotkeyPaused ? hotkeyResumePosition : 0);
                }
                foreach (var appliance in appliances) turnOn.Invoke(appliance,null);
                hotkeyPaused = false;
                Logger.LogInfo("Radio hotkey: native TurnOn requested for both radios (power/fuel still required).");
            }
            catch (Exception ex) { Logger.LogWarning("Radio keyboard control failed: " + ex.Message); }
        }
    }
}
