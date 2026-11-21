using System;
using CustomSoundtrack;
static class TransitionTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Near(double a, double b) => Math.Abs(a-b)<0.0001;
    public static void Run()
    {
        Check(AudioRetryRules.AfterDecode(true,false,100) == 0,"successful decode must permit immediate restore");
        Check(101.22 >= AudioRetryRules.AfterDecode(true,false,100),"clip lost 1.22 seconds after load must not be blocked for 30 seconds");
        Check(AudioRetryRules.AfterDecode(false,false,100) == 130,"actual decoder failure retains bounded retry cooldown");
        Check(double.IsPositiveInfinity(AudioRetryRules.AfterDecode(false,true,100)),"malformed WAV is not retried");
        Check(OutcomeAudioRules.SuppressJingle("Survived",true,4,4),"survived jingle replaced with extraction playlist");
        Check(!OutcomeAudioRules.SuppressJingle("Survived",true,0,4),"no extraction files retains native jingle");
        Check(OutcomeAudioRules.SuppressJingle("Killed",true,4,4),"death jingle replaced with death playlist");
        Check(!OutcomeAudioRules.SuppressJingle("Killed",true,4,0),"no death files retains native jingle");
        Check(!OutcomeAudioRules.SuppressJingle("Runner",false,4,4),"disabled run-through keeps native jingle");
        Check(OutcomeAudioRules.SuppressJingle("Runner",true,4,4),"enabled run-through uses extraction playlist");
        Check(!OutcomeAudioRules.SuppressJingle("Left",true,4,4),"unhandled results leave native sounds alone");
        Check(!OutcomeAudioRules.SuppressJingle(null,true,4,4),"unknown results leave native sounds alone");
        Check(PlaybackRouting.RaidTrackTarget(false)==MusicKind.RaidAmbient,"ambient skip must not queue personal music");
        Check(PlaybackRouting.RaidTrackTarget(true)==MusicKind.Personal,"personal skip still targets personal playlist");
        var overrideState = new MusicState();
        overrideState.BeginRaid(); overrideState.EndRaid(MusicKind.Death);
        Check(!overrideState.TryDismissOutcome(false,false,false),"override waits for menus");
        Check(!overrideState.TryDismissOutcome(true,false,true),"override cannot run while raid world is present");
        Check(!overrideState.TryDismissOutcome(true,true,false),"hideout is not a menu override context");
        Check(overrideState.TryDismissOutcome(true,false,false),"death can be explicitly overridden");
        Check(overrideState.Select(0,true,false)==MusicKind.MainMenu,"override selects menu music");
        Check(!overrideState.EndRaid(MusicKind.Death),"late result cannot replay overridden death");
        Check(!overrideState.TryDismissOutcome(true,false,false),"press after override is ordinary menu control");
        overrideState.BeginRaid(); overrideState.EndRaid(MusicKind.Extraction);
        Check(overrideState.TryDismissOutcome(true,false,false),"extraction can also be overridden");
        Check(!overrideState.EndRaid(MusicKind.Extraction),"late result cannot replay overridden extraction");
        var fade = new FadeEnvelope();
        fade.Set(1,2,0);
        Check(Near(fade.Value(0),0),"fade-in starts silent");
        Check(Near(fade.Value(1),0.5),"two-second fade is halfway at one second");
        Check(Near(fade.Value(2),1)&&fade.Finished(2),"fade completes at two seconds");
        fade.Set(0,2,2);
        Check(Near(fade.Value(3),0.5),"fade-out midpoint");
        fade.Set(1,2,3);
        Check(Near(fade.Value(3),0.5),"reverse must not jump volume");
        Check(Near(fade.Value(4),0.75),"reverse rises from current volume");
        fade.Set(0,0,4);
        Check(Near(fade.Value(4),0),"combat entry immediately silences outgoing audio");
        fade.Set(1,0,4);
        Check(Near(fade.Value(4),1),"combat entry immediately enables prepared audio");
        var loadingFade=new FadeEnvelope();
        loadingFade.Set(1,0,0); loadingFade.Set(0,3.5,10);
        Check(Near(loadingFade.Value(11.75),0.5),"early loading fade has progressed before a delayed stop callback");
        Check(Near(loadingFade.Value(13.5),0),"early loading fade reaches silence at configured deadline");
        var outgoing=new FadeEnvelope(); var incoming=new FadeEnvelope();
        outgoing.Set(1,0,0); outgoing.Set(0,2,10); incoming.Set(1,2,10);
        for(int i=0;i<=20;i++)
            Check(Near(outgoing.Value(10+i/10.0)+incoming.Value(10+i/10.0),1),"linear crossfade gains sum to one");
        var clock=new OutcomeClock();
        clock.Observe(1.215986,148.356995);
        clock.Observe(0,0);
        Check(!clock.Complete(false,0),"recorded extraction unload is not completion");
        Check(Near(clock.Position,1.215986),"recorded extraction resume position survives loss of metadata");
        clock.Observe(50,148);
        Check(!clock.Complete(false,0),"scene stop is not full-track completion");
        clock.Observe(0,0);
        Check(Near(clock.Position,50)&&Near(clock.Duration,148),"lost metadata preserves playback progress");
        clock.Observe(51,148);
        Check(!clock.Complete(true,0),"resumed outcome remains active");
        clock.Observe(147.9,148);
        Check(!clock.Complete(true,0),"full song cannot finish while still playing before end");
        Check(clock.Complete(false,0),"natural completion recognized after final sample window");
        clock.Reset(); clock.Observe(29.9,148);
        Check(!clock.Complete(true,30),"30-second cap not early");
        clock.Observe(30,148); Check(clock.Complete(true,30),"30-second cap works");
        var state=new MusicState(); state.BeginRaid(); state.EndRaid(MusicKind.Extraction);
        state.LeaveWorld();
        Check(state.Select(1,true,true)==MusicKind.Extraction,"menu/hideout audio flag must not cancel extraction");
        Check(!state.EndRaid(MusicKind.Death),"late conflicting result cannot replace extraction");
        state.FinishOutcome(); Check(state.Select(2,true,false)==MusicKind.MainMenu,"menu follows explicit outcome completion");
        state.BeginRaid(); Check(state.Select(3,false,true)==MusicKind.RaidAmbient,"live raid overrides stale hideout-audio flag");
        Check(!CombatRules.Shot(true,false,true,0,50),"disabled own shots cannot fall through to nearby detection");
        Check(CombatRules.Shot(true,true,false,0,50),"own shot enabled");
        Check(CombatRules.Shot(false,false,true,50,50),"nearby boundary included");
        Check(!CombatRules.Shot(false,true,true,51,50),"outside radius rejected");
        Check(!CombatRules.Shot(false,true,false,1,50),"nearby disabled");
        Check(!CombatRules.Grenade(false,1,35),"grenade toggle off");
        Check(CombatRules.Grenade(true,35,35)&&!CombatRules.Grenade(true,36,35),"grenade radius respected");
        Console.WriteLine("PASS: fade timing/reversal, instant combat, outcome persistence/timers and trigger policy.");
    }
}
