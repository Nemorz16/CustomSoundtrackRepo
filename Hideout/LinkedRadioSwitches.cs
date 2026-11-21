using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace CustomHideoutRadio
{
    public sealed partial class Plugin
    {
        private readonly LinkedSwitchGate linkedSwitchGate = new LinkedSwitchGate();
        private MethodInfo applianceTurnOff;
        private PropertyInfo applianceEnabled;
        private void InstallLinkedRadioSwitches()
        {
            try
            {
                applianceTurnOff = AccessTools.Method(applianceType, "TurnOff", Type.EmptyTypes);
                applianceEnabled = AccessTools.Property(applianceType, "Enabled");
                if (applianceTurnOff == null || applianceEnabled == null)
                    throw new MissingMemberException("Expected Appliance.TurnOff() and Enabled.");
                Logger.LogInfo("Radio context-menu switch handling ready; native TurnOff is not globally patched.");
            }
            catch (Exception ex) { Logger.LogWarning("Linked radio switches unavailable; native switches retained: " + ex.Message); }
        }
        private int switchDiagnostics;
        private bool warnedSwitchAction;
        private void ConfigureRadioSwitchActions(IList actions, Component target)
        {
            bool found = false;
            foreach (var action in actions)
            {
                var original = actionCallback.GetValue(action) as Action;
                if (original == null || !ReferenceEquals(original.Target, target)) continue;
                // Match the inspected native delegates, not localized menu labels.
                if (original.Method.Name == "TurnOff")
                {
                    found = true;
                    actionCallback.SetValue(action, new Action(() => MenuRadioOff(target, original)));
                }
                else if (original.Method.Name == "TurnOn")
                {
                    found = true;
                    actionCallback.SetValue(action, new Action(() =>
                    {
                        LogRadioSwitch(target, "Turn on requested");
                        original();
                        StartCoroutine(ObserveRadioSwitch(target));
                    }));
                }
            }
            if (!found && !warnedSwitchAction)
            {
                warnedSwitchAction = true;
                Logger.LogWarning("Native radio switch delegate not recognized; keeping original menu actions unchanged.");
            }
        }
        private void MenuRadioOff(Component origin, Action nativeOff)
        {
            // Always execute the exact action chosen by the player; native/internal TurnOff calls are untouched.
            LogRadioSwitch(origin, "Turn off requested");
            nativeOff();
            if (applianceTurnOff == null || applianceEnabled == null || origin == null) return;
            if (!linkedSwitchGate.TryEnter(enabledSetting.Value, wholeHideout.Value, true)) return;
            try { TurnOffRadioGroup(origin); }
            catch (Exception ex) { Logger.LogWarning("Linked context-menu off failed: " + ex.Message); }
            finally { linkedSwitchGate.Exit(); }
        }
        private IEnumerator ObserveRadioSwitch(Component appliance)
        {
            yield return new WaitForSecondsRealtime(1f);
            LogRadioSwitch(appliance, "One second after Turn on");
        }
        private void LogRadioSwitch(Component appliance, string reason)
        {
            if (appliance == null || switchDiagnostics++ >= 20) return;
            try
            {
                var radio = RadioForAppliance(appliance);
                if (radio == null) { Logger.LogInfo("Radio switch: " + reason + "; no mapped audio component."); return; }
                var sources = Sources(radio, false).Concat(Sources(radio, true)).Where(x => x != null).Distinct().ToArray();
                Logger.LogInfo("Radio switch: " + reason + "; nativeEnabled=" + applianceEnabled?.GetValue(appliance) +
                    "; wholeHideout=" + wholeHideout.Value + "; volume=" + radioVolume.Value + "; sources=" + sources.Length);
                foreach (var source in sources)
                    Logger.LogInfo("Radio source: active=" + source.isActiveAndEnabled + "; playing=" + source.isPlaying +
                        "; mute=" + source.mute + "; volume=" + source.volume + "; clip=" +
                        (source.clip == null ? "null" : source.clip.name));
            }
            catch (Exception ex) { Logger.LogWarning("Radio switch diagnostics failed: " + ex.Message); }
        }
        private void TurnOffRadioGroup(Component origin)
        {
            // Inactive scene objects are included; prefab assets are explicitly excluded.
            var radios = Resources.FindObjectsOfTypeAll(applianceType).OfType<Component>()
                .Where(a => a != null && a.gameObject.scene.IsValid() && a.gameObject.scene.isLoaded)
                .Select(a => new { Appliance = a, Radio = RadioForAppliance(a) })
                .Where(pair => pair.Radio != null).ToArray();
            // Invalidate queued finishes before native cancellation, so an old callback cannot restart a radio.
            playlistGeneration++;
            foreach (var context in replayContexts.Values) context.Ticket?.Cancel();
            foreach (var pending in pendingStartup.Values) pending.Ticket.Cancel();
            pendingStartup.Clear();
            sharedStart = -1;
            int peersOff = 0;
            foreach (var pair in radios)
            {
                if (pair.Appliance == origin) continue; // The originating native TurnOff already completed.
                try
                {
                    if (applianceEnabled.GetValue(pair.Appliance) is bool on && on)
                    {
                        applianceTurnOff.Invoke(pair.Appliance, null);
                        peersOff++;
                    }
                }
                catch (Exception ex) { Logger.LogWarning("Could not switch off radio " + pair.Appliance.name + ": " + ex.Message); }
            }
            Logger.LogInfo("Whole-hideout context-menu off: native switch states updated; other radios switched off=" + peersOff + ".");
        }
    }
}
