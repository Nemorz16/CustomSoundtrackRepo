using System.Collections;
using UnityEngine;
namespace CustomHideoutRadio
{
    public sealed partial class Plugin
    {
        private bool radioRecovering;
        private float nextRadioRecovery;
        private void RequestRadioRecovery()
        {
            if (radioRecovering || Time.realtimeSinceStartup < nextRadioRecovery || !enabledSetting.Value) return;
            radioRecovering = true;
            ready = false;
            // A bounded sequential reload, never decode in the native audio callback.
            StartCoroutine(RecoverRadioAudio());
        }
        private IEnumerator RecoverRadioAudio()
        {
            yield return null;
            try
            {
                Logger.LogWarning("Hideout runtime recovery: custom audio data was lost; reloading invalid clips.");
                for (int i = 0; i < tracks.Count; i++)
                {
                    if (!ValidateTrack(i, "runtime audit")) yield return LoadTrack(trackFiles[i], i);
                    yield return null;
                }
                Logger.LogInfo("Hideout runtime recovery finished; pending active radios will use the existing handover checks.");
            }
            finally
            {
                radioRecovering = false;
                ready = tracks.Count > 0;
                nextRadioRecovery = Time.realtimeSinceStartup + 30f;
            }
        }
    }
}
