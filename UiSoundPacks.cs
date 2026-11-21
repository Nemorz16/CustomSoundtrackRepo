using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
namespace CustomSoundtrack
{
    public sealed partial class Plugin
    {
        private ConfigEntry<string> uiPack;
        private ConfigEntry<bool> refreshUiPacks;
        private string activeUiPack;
        private int uiPackGeneration;
        private readonly List<KeyValuePair<Asset,double>> retiredUiAssets=new List<KeyValuePair<Asset,double>>();
        private string UiPacksRoot => Path.Combine(UiSoundRoot,"Packs");
        private void ConfigureUiPacks()
        {
            Directory.CreateDirectory(UiPacksRoot);
            var choices=DiscoverUiPacks();
            string initial=choices.Contains(UiPackRules.Legacy) ? UiPackRules.Legacy : UiPackRules.Vanilla;
            uiPack=Config.Bind("UI Sounds","SoundPack",initial,new ConfigDescription("Choose a UI sound pack. Missing sounds keep vanilla.",new AcceptableValueList<string>(choices)));
            refreshUiPacks=Config.Bind("UI Sounds","RefreshPacks",false,"Turn on to rescan packs and reload sounds; resets automatically.");
        }
        private string[] DiscoverUiPacks()
        {
            var choices=new List<string> { UiPackRules.Vanilla };
            if(new[]{"UI","Chat","Notifications"}.Any(g => Directory.Exists(Path.Combine(UiSoundRoot,g)) && Directory.GetFiles(Path.Combine(UiSoundRoot,g),"*",SearchOption.AllDirectories).Any(UiPackRules.AudioFile))) choices.Add(UiPackRules.Legacy);
            choices.AddRange(Directory.GetDirectories(UiPacksRoot).Select(Path.GetFileName)
                .Where(n=>n!=UiPackRules.Vanilla && n!=UiPackRules.Legacy).OrderBy(n=>n,StringComparer.OrdinalIgnoreCase));
            return choices.ToArray();
        }
        private void UpdateUiPackSelection()
        {
            if(uiPack==null) return;
            if(!uiSoundsLoading)
                for(int i=retiredUiAssets.Count-1;i>=0;i--)
                    if(Now>=retiredUiAssets[i].Value) { retiredUiAssets[i].Key.Dispose(); retiredUiAssets.RemoveAt(i); }
            bool refreshed=false;
            try
            {
                if(refreshUiPacks.Value)
                {
                    refreshUiPacks.Value=false;
                    string selected=uiPack.Value;
                    var choices=DiscoverUiPacks();
                    // Rebind with the new finite list: Configuration Manager renders it as a dropdown.
                    Config.Remove(new ConfigDefinition("UI Sounds","SoundPack"));
                    uiPack=Config.Bind("UI Sounds","SoundPack",choices.Contains(selected) ? selected : UiPackRules.Vanilla,
                        new ConfigDescription("Choose a UI sound pack. Missing sounds keep vanilla.",new AcceptableValueList<string>(choices)));
                    refreshed=true; ConfigureMenuLayout();
                    Logger.LogInfo("UI packs rescanned. Close/reopen the settings menu to refresh its dropdown.");
                }
                if(activeUiPack==uiPack.Value && !refreshed) return;
                activeUiPack=uiPack.Value; uiPackGeneration++;
                // Allow already-triggered one-shots to finish before releasing the old pack.
                foreach(var list in uiSoundAssets.Values)
                    foreach(var asset in list) retiredUiAssets.Add(new KeyValuePair<Asset,double>(asset,Now+(Valid(asset.Clip) ? asset.Clip.length : 0)+2));
                uiSoundAssets.Clear(); uiSoundReloads.Clear(); uiSoundRetry.Clear();
                uiSoundsStarted=false;
                Logger.LogInfo("UI sound pack selected: " + activeUiPack);
            }
            catch(Exception ex) { Logger.LogWarning("UI pack refresh failed; existing selection retained: " + ex.Message); }
        }
        private string[] UiPackFiles(string key)
        {
            if(activeUiPack==UiPackRules.Vanilla || string.IsNullOrEmpty(activeUiPack)) return Array.Empty<string>();
            if(activeUiPack==UiPackRules.Legacy)
            {
                string folder=Path.Combine(UiSoundRoot,key);
                return Directory.Exists(folder) ? Directory.GetFiles(folder).Where(UiPackRules.AudioFile).OrderBy(f=>f).ToArray() : Array.Empty<string>();
            }
            string pack=Path.Combine(UiPacksRoot,activeUiPack);
            if(!Directory.Exists(pack)) return Array.Empty<string>();
            string stem=UiPackRules.Stem(key);
            // WAV wins if both formats exist; one replacement per event, matched without case sensitivity.
            var matches=Directory.GetFiles(pack).Where(f=>UiPackRules.AudioFile(f) && string.Equals(Path.GetFileNameWithoutExtension(f),stem,StringComparison.OrdinalIgnoreCase))
                .OrderBy(f=>string.Equals(Path.GetExtension(f),".wav",StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(f=>f,StringComparer.Ordinal).ToArray();
            return matches.Take(1).ToArray();
        }
    }
}
