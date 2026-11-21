using System;
using CustomSoundtrack;
static class AudioExtensionTests
{
    static int checks;
    static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    public static void Run()
    {
        foreach(var mode in new[]{CombatReturnMode.Previous,CombatReturnMode.Ambient,CombatReturnMode.PersonalSaved,CombatReturnMode.PersonalRandom,CombatReturnMode.Silence})
        foreach(bool personal in new[]{false,true})
        {
            var s=new MusicState {CombatReturn=mode}; s.BeginRaid(); if(personal) s.StartPersonal(0,false);
            s.CombatEvent(1,10); Check(s.Select(2,true,false)==MusicKind.Combat,"combat takes priority");
            s.CombatEvent(9,10); Check(s.Select(18,true,false)==MusicKind.Combat,"extended combat retains victim");
            var expected=mode==CombatReturnMode.Silence ? MusicKind.None : mode==CombatReturnMode.PersonalSaved || mode==CombatReturnMode.PersonalRandom || mode==CombatReturnMode.Previous && personal ? MusicKind.Personal : MusicKind.RaidAmbient;
            Check(s.Select(19,true,false)==expected,"return mode matrix");
            Check(s.RandomPersonalRequested==(mode==CombatReturnMode.PersonalRandom),"random selection only when requested");
            Check(s.Select(20,true,false)==expected,"return mode does not toggle each frame");
        }
        foreach(var result in new[]{MusicKind.Death,MusicKind.Extraction})
        {
            var s=new MusicState(); s.BeginRaid(); s.EndRaid(result);
            Check(s.StartPersonal(10,true),"personal allowed over menu outcome");
            Check(s.Select(11,true,false)==MusicKind.Personal,"personal temporarily overrides outcome");
            Check(s.Outcome==result,"outcome preserved while personal plays");
            s.StopPersonal(); Check(s.Select(12,true,false)==result,"stop resumes prior outcome");
            s.FinishOutcome();Check(s.Select(13,true,false)==MusicKind.MainMenu,"outcome completion restores menu");
        }
        var silent=new MusicState {MenuSelection=MusicKind.None};
        Check(silent.Select(0,true,false)==MusicKind.None,"silent startup");
        Check(silent.StartPersonal(1,true) && silent.Select(1,true,false)==MusicKind.Personal,"personal can override silence");
        Check(silent.Select(2,true,true)==MusicKind.None,"personal menu override does not leak into hideout");
        var disabled=new MusicState {CombatReturn=CombatReturnMode.Previous}; disabled.BeginRaid(); disabled.StartPersonal(0,false);disabled.CombatEvent(1,2);disabled.SetEnabled(MusicKind.Personal,false);
        Check(disabled.Select(4,true,false)==MusicKind.RaidAmbient,"disabled personal is not resumed");
        Check(BossMusicRules.DeathAction(BossDeathMode.Random,.2,.2,.3)==BossDeathMode.StopMusic,"static/silence boundary");
        Check(BossMusicRules.DeathAction(BossDeathMode.Random,.5,.2,.3)==BossDeathMode.ContinueMusic,"silence/music boundary");
        Check(BossMusicRules.DeathAction(BossDeathMode.Random,.999,0,1)==BossDeathMode.StopMusic,"100 percent silence");
        Check(BossMusicRules.DeathAction(BossDeathMode.Random,.9,.8,.8)==BossDeathMode.StopMusic,"overfull chances capped");
        Check(Math.Abs(BossMusicRules.ContinueChance(.2,.3)-.5)<1e-6,"remaining probability");
        Check(BossMusicRules.ContinueChance(.8,.8)==0,"remaining cannot become negative");
        Check(Math.Abs(BossMusicRules.HelmetCutoff(1,2000,12000)-2000)<.01,"front cutoff");
        Check(Math.Abs(BossMusicRules.HelmetCutoff(-1,2000,12000)-12000)<.01,"rear cutoff");
        double previous=22000;
        for(int i=0;i<=100;i++) { double current=BossMusicRules.HelmetCutoff(-1+i*.02,2000,12000);Check(current<=previous+.001 && current>=1999,"smooth directional filter");previous=current; }
        Check(UiPackRules.AudioFile("track.MP3"),"MP3 recognized"); Check(!UiPackRules.AudioFile("track.mp3.tmp"),"temporary files excluded");
        Console.WriteLine("PASS: "+checks+" new assertions: combat returns, menu override/outcome persistence, silence, death probabilities, filter direction and MP3.");
    }
}
