using System;
using CustomSoundtrack;
class Program
{
    static int checks;
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static void Main()
    {
        Check(!OutcomePolicy.Finished(true,true,10000,0), "whole song has no time limit");
        Check(OutcomePolicy.Finished(true,false,3,0), "whole song ends naturally");
        Check(!OutcomePolicy.Finished(true,true,29.9,30), "timed song plays before cap");
        Check(OutcomePolicy.Finished(true,true,30,30), "30-second cap ends at boundary");
        Check(OutcomePolicy.Finished(true,false,5,30), "short song does not loop to fill timer");
        Check(OutcomePolicy.Finished(false,false,0,30), "empty outcome immediately returns control");
        AudioExtensionTests.Run(); PlaybackOptionsTests.Run(); HeaderTests.Run(); TransitionTests.Run(); RadioTests.Run(); PlayerUiTests.Run();
        var s = new MusicState();
        Check(s.Select(0, true, false) == MusicKind.MainMenu, "menu outside raid");
        Check(s.Select(0, false, false) == MusicKind.None, "no music when menus are suppressed");
        Check(!s.TogglePersonal(0), "personal cannot start outside raid");
        s.BeginRaid();
        Check(s.Select(10, true, false) == MusicKind.RaidAmbient, "raid overrides menu");
        Check(s.TogglePersonal(10), "play allowed in raid");
        Check(s.Select(10, true, false) == MusicKind.Personal, "personal overrides ambient");
        s.TogglePersonal(11);
        Check(s.Select(11, true, false) == MusicKind.RaidAmbient, "pause returns ambient");
        s.TogglePersonal(12); s.CombatEvent(13,20);
        Check(s.Select(13, true, false) == MusicKind.Combat, "combat overrides personal");
        Check(!s.PersonalPlaying, "combat clears personal play intent");
        Check(!s.TogglePersonal(14), "play cannot bypass combat");
        s.CombatEvent(25,20);
        Check(s.Select(34, true, false) == MusicKind.Combat, "new trigger extends quiet period");
        Check(s.Select(45, true, false) == MusicKind.RaidAmbient, "combat end returns ambient");
        Check(!s.PersonalPlaying, "personal does not automatically resume");
        s.TogglePersonal(46);
        Check(s.Select(46, false, false) == MusicKind.Personal, "manual resume works");
        s.StopPersonal(); Check(!s.PersonalPlaying, "stop clears play intent");
        s.CombatEvent(50,20);
        Check(s.EndRaid(MusicKind.Death), "death accepted");
        Check(s.Select(51, true, false) == MusicKind.Death, "death overrides combat");
        Check(!s.EndRaid(MusicKind.Death), "result-screen fallback cannot replay immediate death");
        s.LeaveWorld(); Check(s.Outcome == MusicKind.Death, "world disposal preserves outcome");
        s.FinishOutcome(); Check(s.Select(55, true, false) == MusicKind.MainMenu, "finished death returns menu");
        Check(!s.EndRaid(MusicKind.Death), "late duplicate result cannot replay completed song");
        s.BeginRaid(); Check(!s.ResultHandled, "next raid resets result latch");
        s.EndRaid(MusicKind.Extraction);
        Check(s.Select(60, false, false) == MusicKind.Extraction, "extraction can play before menu opens");
        s.FinishOutcome(); Check(s.Select(61, false, false) == MusicKind.None, "wait for actual menu availability");
        Check(s.Select(62, true, false) == MusicKind.MainMenu, "menu resumes when available");
        Check(s.Select(62, true, true) == MusicKind.None, "hideout suppresses companion");
        s.BeginRaid(); s.EndRaid(MusicKind.None);
        Check(s.Select(65, true, false) == MusicKind.MainMenu, "other exit status has no sting");
        s.BeginRaid(); s.TogglePersonal(70); s.LeaveWorld();
        Check(!s.PersonalPlaying && !s.InRaid, "world disposal clears raid intent");
        for(int i=0;i<10000;i++)
        {
            s.BeginRaid(); s.TogglePersonal(i); s.CombatEvent(i+1,20);
            Check(s.Select(i+21,true,false)==MusicKind.RaidAmbient,"repeated raids never restore personal automatically");
            s.EndRaid(MusicKind.Death); s.FinishOutcome();
            Check(!s.EndRaid(MusicKind.Extraction),"conflicting duplicate cannot replace completed outcome");
        }
        Console.WriteLine("PASS: " + checks + " assertions: priority, pause/stop, combat extension/manual resume, hideout exclusion, duplicate outcomes, world disposal and repeated raids.");
    }
}
