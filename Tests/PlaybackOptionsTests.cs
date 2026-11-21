using System;
using CustomSoundtrack;
static class PlaybackOptionsTests
{
    static void Check(bool ok,string why) { if(!ok) throw new Exception(why); }
    public static void Run()
    {
        Check(UiPackRules.Stem("UI/BackpackOpen")=="BackpackOpen","flat backpack filename");
        Check(UiPackRules.Stem("Chat/IncomingMessage")=="IncomingMessage","flat chat filename");
        Check(UiPackRules.Stem("Notifications/GeneralNotification")=="GeneralNotification","flat notification filename");
        Check(UiPackRules.AudioFile("BackpackOpen.WAV"),"case-insensitive audio extension");
        Check(!UiPackRules.AudioFile("BackpackOpen.wav.txt"),"text placeholder is not audio");
        foreach(CombatOverrideMode mode in Enum.GetValues(typeof(CombatOverrideMode)))
        foreach(bool personal in new[]{false,true})
        {
            var s=new MusicState { CombatOverride=mode }; s.BeginRaid();
            if(personal) s.TogglePersonal(0);
            s.CombatEvent(1,20);
            bool expected=mode==CombatOverrideMode.Both || (personal ? mode==CombatOverrideMode.PersonalOnly : mode==CombatOverrideMode.AmbientOnly);
            Check(s.InCombat(2)==expected,"combat mode/playlist matrix");
            Check(s.Select(2,true,false)==(expected ? MusicKind.Combat : personal ? MusicKind.Personal : MusicKind.RaidAmbient),"policy selects correct channel");
            s.CombatEvent(3,20);
            Check(s.InCombat(22)==expected,"repeated trigger preserves interrupted category");
            Check(s.Select(24,true,false)==(!expected && personal ? MusicKind.Personal : MusicKind.RaidAmbient),"combat exit preserves only uninterrupted personal intent");
        }
        var a=new MusicState { CombatOverride=CombatOverrideMode.AmbientOnly };a.BeginRaid();a.CombatEvent(1,20);
        Check(a.TogglePersonal(2) && a.Select(3,true,false)==MusicKind.Personal,"personal can replace ambient-only combat");
        a.SetEnabled(MusicKind.Personal,false);a.SetEnabled(MusicKind.Combat,false);a.SetEnabled(MusicKind.RaidAmbient,false);a.CombatEvent(4,20);
        Check(a.Select(5,true,false)==MusicKind.None,"disabled raid categories stay silent");
        a.SetEnabled(MusicKind.Death,false);a.EndRaid(MusicKind.Death);
        Check(a.Select(6,true,false)==MusicKind.MainMenu,"disabled death returns to menu");
        Check(BossMusicRules.DeathAction(BossDeathMode.Random,.1,0)==BossDeathMode.ContinueMusic,"0 percent static");
        Check(BossMusicRules.DeathAction(BossDeathMode.Random,.99,1)==BossDeathMode.Static,"100 percent static");
        Check(BossMusicRules.DeathAction(BossDeathMode.Random,.2,.5)==BossDeathMode.Static,"static below chance");
        Check(BossMusicRules.DeathAction(BossDeathMode.Random,.8,.5)==BossDeathMode.ContinueMusic,"continue above chance");
        Check(BossMusicRules.DeathAction(BossDeathMode.StopMusic,0,1)==BossDeathMode.StopMusic,"explicit stop ignores chance");
        Check(PlayerUiRules.SeekPosition(-5,60)==0,"seek clamps start");
        Check(Math.Abs(PlayerUiRules.SeekPosition(90,60)-59.95)<.001,"seek clamps end");
        Check(PlayerUiRules.SeekPosition(20,60)==20,"seek preserves valid position");
        Check(PlayerUiRules.SeekPosition(double.NaN,60)==0,"seek rejects invalid input");
        Console.WriteLine("PASS: combat override matrix, category switches, boss death chance, seek boundaries.");
    }
}
