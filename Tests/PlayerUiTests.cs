using System;
using CustomSoundtrack;
static class PlayerUiTests
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    public static void Run()
    {
        Check(PlayerUiRules.TargetIndex(0,-1,-1,0)==-1,"empty playlist cannot play");
        Check(PlayerUiRules.TargetIndex(4,-1,-1,0)==0,"initial play chooses first entry");
        Check(PlayerUiRules.TargetIndex(4,0,3,0)==3,"selected track wins over current");
        Check(PlayerUiRules.TargetIndex(4,3,3,1)==0,"next wraps");
        Check(PlayerUiRules.TargetIndex(4,0,0,-1)==3,"previous wraps");
        Check(PlayerUiRules.TargetIndex(1,0,0,1)==0,"single track stays valid");
        Check(PlayerUiRules.TargetIndex(4,2,99,0)==2,"stale selection falls back to current");
        Check(PlayerUiRules.Matches("My Track.wav","  track  "),"search ignores case and outer whitespace");
        Check(!PlayerUiRules.Matches("My Track.wav","combat"),"unmatched search is excluded");
        Check(PlayerUiRules.Progress(50,100)==0.5,"progress halfway");
        Check(PlayerUiRules.Progress(50,0)==0,"unknown duration does not divide by zero");
        Check(PlayerUiRules.Progress(500,100)==1,"progress clamps above duration");
        Check(PlayerUiRules.Progress(-10,100)==0,"negative progress clamps");
        Check(PlayerUiRules.Progress(double.NaN,100)==0,"invalid progress is safe");
        Check(PlayerUiRules.TimeLabel(125)=="02:05","time formatting");
        Check(PlayerUiRules.TimeLabel(double.NaN)=="00:00","invalid time display");
        Check(Math.Abs(PlayerUiRules.SkinScale(0.66,1920,1080)-0.66)<0.000001,"native skin uses requested scale when it fits");
        double fitted=PlayerUiRules.SkinScale(1,1280,720);
        Check(fitted*1008<=700.00001 && fitted*733<=1260.00001,"image fits inside screen margins");
        Check(Math.Abs((fitted*733)/(fitted*1008)-733.0/1008)<0.000001,"uniform scaling preserves skin aspect ratio");
        Check(Math.Abs(PlayerUiRules.SkinScale(double.NaN,1920,1080)-0.66)<0.000001,"invalid scale has a safe default");
        Check(PlayerUiRules.PlaylistLabel(2,0,1)=="Playlist 1/2","selection counter follows selection");
        Check(PlayerUiRules.PlaylistLabel(14,4,0)=="Playlist 5/14","selection counter uses full playlist index");
        Check(PlayerUiRules.PlaylistLabel(0,-1,-1)=="Playlist 0/0","empty selection counter");
        Check(PlayerUiRules.PlaylistLabel(2,99,1)=="Playlist 2/2","stale selection uses current track");
        Check(BossMusicRules.NameForRole("bossKilla")=="Killa", "Killa role");
        Check(BossMusicRules.NameForRole("followerBirdEye")=="Birdeye", "Goons individual role");
        Check(BossMusicRules.NameForRole("followerBully")==null, "ordinary guards excluded");
        Check(BossMusicRules.Gain(40,3,40)==0,"boss radius cutoff");
        Check(BossMusicRules.Gain(2,3,40)==1,"boss full distance");
        Check(Math.Abs(BossMusicRules.Gain(21.5,3,40)-.5)<.0001,"boss linear distance fade");
        Check(BossMusicRules.Gain(10,50,10)==0,"full distance above radius is safe");
        var state=new MusicState(); state.BeginRaid(); state.TogglePersonal(0);
        state.CombatEvent(1,20);
        Check(!state.TogglePersonal(2),"player cannot bypass current combat policy");
        Check(!state.PersonalPlaying,"combat clears personal intent");
        Check(state.Select(21,true,false)==MusicKind.RaidAmbient,"ambient returns after combat");
        Check(state.TogglePersonal(22),"explicit player Play resumes personal after combat");
        state.StopPersonal(); Check(state.Select(23,true,false)==MusicKind.RaidAmbient,"player Stop returns ambient");
        Console.WriteLine("PASS: player selection/wrapping/search/time/progress and existing combat priority.");
    }
}
