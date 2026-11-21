using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
namespace CustomSoundtrack
{
    public sealed partial class Plugin
    {
        public enum AfterStartupTrack { MainMenuPlaylist, SelectedTracksPlaylist, Silence }
        public enum RunThroughMusic { None, Personal, Death, Extraction, MainMenu }
        private ConfigEntry<CombatReturnMode> combatReturn;
        private ConfigEntry<RunThroughMusic> runThroughMusic;
        private ConfigEntry<AfterStartupTrack> afterStartup;
        private ConfigEntry<string> startupTrack;
        private ConfigEntry<bool> refreshStartup;
        private const string StartupMenu="Main Menu Playlist", StartupSilence="Silence";
        private bool startupApplied, startupOneTrack, startupTemporary;
        private readonly List<Asset> savedMenuTracks=new List<Asset>();
        private string MusicRoot => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location),"Music");
        private void ConfigureSessionChoices()
        {
            combatReturn=Config.Bind("Combat","AfterCombat",CombatReturnMode.Previous,"Resume the interrupted playlist, choose ambient/personal, or stay silent.");
            runThroughMusic=Config.Bind("Outcomes","RunThroughMusic",runThrough.Value ? RunThroughMusic.Extraction : RunThroughMusic.None,"Choose music for a Run Through result.");
            afterStartup=Config.Bind("General","AfterStartupTrack",AfterStartupTrack.MainMenuPlaylist,"After the startup song: main menu, its folder playlist, or silence. Startup tracks use Main Menu volume.");
            refreshStartup=Config.Bind("General","RefreshStartupTracks",false,"Rescan music filenames for the startup dropdown; selection takes effect next launch.");
            BindStartupTracks(null);
        }
        private void BindStartupTracks(string selected)
        {
            var choices=new List<string>{StartupMenu,StartupSilence};
            Directory.CreateDirectory(MusicRoot);
            choices.AddRange(Directory.GetFiles(MusicRoot,"*",SearchOption.AllDirectories).Where(AudioFormats.Supported).Select(f=>f.Substring(MusicRoot.Length+1).Replace('\\','/')).OrderBy(f=>f,StringComparer.OrdinalIgnoreCase));
            if(selected!=null) Config.Remove(new ConfigDefinition("General","StartupTrack"));
            startupTrack=Config.Bind("General","StartupTrack",selected!=null && choices.Contains(selected) ? selected : StartupMenu,new ConfigDescription("*** First song after startup delay. Lists all music folders. Main Menu volume applies.",new AcceptableValueList<string>(choices.ToArray())));
        }
        private void UpdateSessionChoices()
        {
            if(refreshStartup.Value) { refreshStartup.Value=false; try { BindStartupTracks(startupTrack.Value); ConfigureMenuLayout(); } catch(Exception ex) { Logger.LogWarning("Startup track scan: "+ex.Message); } }
            state.AdvanceCombat(Now);
            if(state.RandomPersonalRequested)
            { state.RandomPersonalRequested=false; if(ready && channels[MusicKind.Personal].Tracks.Count>0) QueueTrack(MusicKind.Personal,1,true); }
            if(!ready || !enabledSetting.Value) return;
            bool hideout=Read(gui,"HideoutSoundActive") is bool h && h;
            if(startupTemporary && (state.InRaid || hideout || !menuAllowed)) { RestoreStartupPlaylist(); state.MenuSelection=MusicKind.MainMenu; }
            if(startupApplied || state.InRaid || hideout || !menuAllowed || state.Outcome!=MusicKind.None) return;
            startupApplied=true;
            if(startupTrack.Value==StartupMenu) return;
            if(startupTrack.Value==StartupSilence) { state.MenuSelection=MusicKind.None; return; }
            try
            {
                string file=Path.GetFullPath(Path.Combine(MusicRoot,startupTrack.Value.Replace('/',Path.DirectorySeparatorChar)));
                if(!File.Exists(file) || !file.StartsWith(Path.GetFullPath(MusicRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) return;
                var channel=channels[MusicKind.MainMenu];
                savedMenuTracks.AddRange(channel.Tracks); channel.Tracks.Clear();
                channel.Tracks.Add(new Asset { File=file });
                channel.Tracks.AddRange(Directory.GetFiles(Path.GetDirectoryName(file)).Where(f=>f!=file && AudioFormats.Supported(f)).OrderBy(f=>f).Select(f=>new Asset {File=f}));
                ResetMenuChannel(); startupTemporary=true; startupOneTrack=true;
                QueueTrackIndex(MusicKind.MainMenu,0);
            }
            catch(Exception ex) { RestoreStartupPlaylist(); Logger.LogWarning("Startup selection retained menu playlist: "+ex.Message); }
        }
        private void ResetMenuChannel()
        {
            var c=channels[MusicKind.MainMenu]; c.Source.Stop(); c.Source.clip=null; c.Index=-1; c.PendingIndex=-1; c.SavedPosition=0; c.Started=false; c.Failed=false; c.ResetWhenSilent=false; c.EndingTrack=false; c.RestorePosition=false;
        }
        private void RestoreStartupPlaylist()
        {
            if(!startupTemporary && savedMenuTracks.Count==0) return;
            raidEpoch++; recoveryKinds.Remove(MusicKind.MainMenu);
            var c=channels[MusicKind.MainMenu]; ResetMenuChannel();
            foreach(var asset in c.Tracks) asset.Dispose();
            c.Tracks.Clear(); c.Tracks.AddRange(savedMenuTracks); savedMenuTracks.Clear();
            startupTemporary=false; startupOneTrack=false;
        }
        private bool MaintainStartup(double now)
        {
            if(!startupOneTrack || active!=MusicKind.MainMenu) return false;
            var c=channels[MusicKind.MainMenu];
            if(c.Loading || IsPaused(active) || c.PendingIndex>=0) return true;
            if(c.Failed || (c.Started && !c.Source.isPlaying && Valid(c.Source.clip) && c.SavedPosition>=c.Source.clip.length-.3f))
            {
                startupOneTrack=false;
                if(afterStartup.Value==AfterStartupTrack.SelectedTracksPlaylist && !c.Failed) { QueueTrack(MusicKind.MainMenu,1,false); return true; }
                RestoreStartupPlaylist();
                state.MenuSelection=afterStartup.Value==AfterStartupTrack.Silence ? MusicKind.None : MusicKind.MainMenu;
                active=MusicKind.None; SwitchTo(state.Select(now,menuAllowed,false)); return true;
            }
            if(c.Source.isPlaying && Valid(c.Source.clip))
            {
                double remaining=c.Source.clip.length-c.Source.time;
                if(remaining<=FadeValue("MainMenuTrackChange") && !c.EndingTrack) { c.EndingTrack=true; c.Gain.Set(0,remaining,now); }
            }
            else EnsureActive();
            return true;
        }
        private bool ShouldSuppressResultJingle(string status)
        {
            if(string.Equals(status,"Runner",StringComparison.OrdinalIgnoreCase) || string.Equals(status,"RunThrough",StringComparison.OrdinalIgnoreCase))
            {
                if(runThroughMusic.Value==RunThroughMusic.None) return true;
                MusicKind kind=runThroughMusic.Value==RunThroughMusic.Personal ? MusicKind.Personal : runThroughMusic.Value==RunThroughMusic.MainMenu ? MusicKind.MainMenu : runThroughMusic.Value==RunThroughMusic.Death ? MusicKind.Death : MusicKind.Extraction;
                return state.Enabled(kind) && channels[kind].Tracks.Count>0;
            }
            return OutcomeAudioRules.SuppressJingle(status,runThrough.Value,state.Enabled(MusicKind.Extraction) ? channels[MusicKind.Extraction].Tracks.Count : 0,state.Enabled(MusicKind.Death) ? channels[MusicKind.Death].Tracks.Count : 0);
        }
        private MusicKind RaidResultMusic(string status)
        {
            if(!string.Equals(status,"Runner",StringComparison.OrdinalIgnoreCase) && !string.Equals(status,"RunThrough",StringComparison.OrdinalIgnoreCase)) return OutcomeAudioRules.FromStatus(status,runThrough.Value);
            switch(runThroughMusic.Value)
            {
                case RunThroughMusic.Death: return MusicKind.Death;
                case RunThroughMusic.Extraction: return MusicKind.Extraction;
                default: return MusicKind.None;
            }
        }
    }
}
