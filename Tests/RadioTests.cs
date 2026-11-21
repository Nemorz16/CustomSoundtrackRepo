using System;
using CustomHideoutRadio;
static class RadioTests
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    public static void Run()
    {
        var gate = new LinkedSwitchGate();
        Check(!gate.TryEnter(true,false,true), "directional mode keeps independent switches");
        Check(!gate.TryEnter(false,true,true), "disabled radio module leaves native switches alone");
        Check(!gate.TryEnter(true,true,false), "unrelated appliances are not linked");
        Check(gate.TryEnter(true,true,true), "whole-hideout radio off starts a group operation");
        Check(!gate.TryEnter(true,true,true), "nested peer TurnOff cannot recurse");
        gate.Exit();
        Check(gate.TryEnter(true,true,true), "later off operations remain usable");
        gate.Exit();
        var offTicket = new PlaybackTicket(5); offTicket.QueueOnce();
        Check(!offTicket.IsCurrent(6), "group stop invalidates already queued completion");
        double start = -1;
        Check(PlaybackPosition.GetOffset(100, 200, ref start) == 0, "first radio starts at zero");
        Check(PlaybackPosition.GetOffset(115, 200, ref start) == 15, "second radio joins 15s in");
        Check(PlaybackPosition.GetOffset(115, 200, ref start) == 15, "same-time requests agree");
        Check(PlaybackPosition.GetOffset(300, 200, ref start) == 0, "full-song wrap");
        Check(PlaybackPosition.GetOffset(90115, 200, ref start) == 15, "long interval while radios off");
        start = -1; PlaybackPosition.GetOffset(0, 200, ref start);
        Check(PlaybackPosition.GetOffset(199.9995, 200, ref start) == 0, "early native completion restarts without zero-delay recursion");
        Check(PlaybackPosition.GetOffset(199.9995, 200, ref start) == 0, "other radio shares early restart");
        foreach (float length in new[] { 0.1f, 130.36f, 220.63f, 269.12f, 36000f })
        {
            start = -1;
            double[] due = { 0, Math.Min(15, length / 2) };
            for (int i = 0; i < 2000; i++)
            {
                int radio = due[0] <= due[1] ? 0 : 1;
                double now = due[radio];
                float offset = PlaybackPosition.GetOffset(now, length, ref start);
                int milliseconds = (int)((length - offset) * 1000f);
                Check(milliseconds > 0, "native playback must yield instead of synchronous recursion");
                Check(offset >= 0 && offset < length, "valid seek range");
                Check(offset == PlaybackPosition.GetOffset(now, length, ref start), "stable shared offset");
                due[radio] = now + milliseconds / 1000.0;
            }
        }
        foreach (double bad in new[] { 0, double.NaN, double.PositiveInfinity, -1, 40000 })
        {
            bool rejected = false; start = -1;
            try { PlaybackPosition.GetOffset(0, bad, ref start); } catch (ArgumentException) { rejected = true; }
            Check(rejected && start == -1, "reject invalid duration without changing start");
        }
        Check(PeerPosition.IsUsable(51.751f, 208.44f, 1.028238f), "accept measured faster-pitch peer");
        Check(!PeerPosition.IsUsable(208.439f, 208.44f, 1.028238f), "reject nearly ended peer to avoid zero-delay loop");
        Check(!PeerPosition.IsUsable(float.NaN, 208.44f, 1), "reject invalid peer position");
        Check(!PeerPosition.IsUsable(50, 208.44f, 0), "reject paused-by-pitch peer");
        // Replay the measured snapshot: using a live peer removes the observed 1.311s
        // offset error at that instant, without pretending to test Unity seek latency.
        float observedPeer = 51.751f, oldPosition = 50.440f;
        Check(System.Math.Abs(observedPeer - oldPosition - 1.311f) < .001f, "reproduce logged discrepancy");
        var sequence = new PlaylistHistory(3, false, new Random(1));
        Check(sequence.Current == 0, "initial sequential track");
        Check(sequence.Next(false) == 1 && sequence.Next(false) == 2, "next tracks");
        Check(sequence.Previous(15) == 2, "previous after 3 seconds restarts");
        Check(sequence.Previous(0) == 1, "immediate previous returns in history");
        Check(sequence.Next(false) == 2, "next restores forward history");
        Check(sequence.Next(false) == 0, "sequential wraps");
        var shuffled = new PlaylistHistory(8, true, new Random(42));
        int first = shuffled.Current, second = shuffled.Next(true);
        Check(second != first, "shuffle avoids immediate repeat");
        Check(shuffled.Previous(0) == first, "shuffle previous follows actual history");
        Check(shuffled.Next(true) == second, "shuffle forward reuses actual history");
        var one = new PlaylistHistory(1, true, new Random(1));
        Check(one.Next(true) == 0 && one.Previous(0) == 0, "single-track navigation");
        bool emptyRejected = false;
        try { new PlaylistHistory(0, true, new Random()); } catch (ArgumentException) { emptyRejected = true; }
        Check(emptyRejected, "empty playlist rejected");
        long generation = 10;
        var radioA = new PlaybackTicket(generation);
        var radioB = new PlaybackTicket(generation);
        Check(radioA.QueueOnce() && !radioA.QueueOnce(), "deduplicate one radio callback");
        Check(radioB.QueueOnce(), "both radios can finish together");
        int autoAdvances = 0;
        foreach (var ticket in new[] { radioA, radioB })
            if (ticket.IsCurrent(generation)) { autoAdvances++; generation++; }
        Check(autoAdvances == 1, "only one playlist advance for two simultaneous finishes");
        var switchedOff = new PlaybackTicket(generation);
        switchedOff.Cancel();
        Check(!switchedOff.QueueOnce() && !switchedOff.IsCurrent(generation), "off radio cannot advance or restart");
        var queuedThenOff = new PlaybackTicket(generation);
        Check(queuedThenOff.QueueOnce(), "queued before off"); queuedThenOff.Cancel();
        Check(!queuedThenOff.IsCurrent(generation), "off between callback and Update invalidates event");
        var skipped = new PlaybackTicket(generation); skipped.QueueOnce(); generation++;
        Check(!skipped.IsCurrent(generation), "manual skip invalidates delayed completion");
        var slowPitch = new PlaybackTicket(generation); slowPitch.QueueOnce(); slowPitch.AllowRequeue();
        Check(slowPitch.QueueOnce(), "early timer can defer to actual audio finish");
        Check(!ClipGuard.CanComplete(false, 200), "failed-to-start audio must not advance");
        Check(!ClipGuard.CanComplete(true, 0), "lost clip data must not advance");
        Check(!ClipGuard.CanComplete(true, float.NaN), "invalid metadata must not advance");
        Check(ClipGuard.CanComplete(true, 200), "observed valid track can finish");
        var transaction = new PlaylistHistory(3, false, new Random(1));
        var checkpoint = transaction.Capture();
        transaction.Next(false); transaction.Restore(checkpoint);
        Check(transaction.Current == 0 && transaction.Next(false) == 1, "failed destination does not consume history");
        transaction.Next(false); checkpoint = transaction.Capture();
        transaction.Previous(0); transaction.Restore(checkpoint);
        Check(transaction.Current == 2 && transaction.Previous(0) == 1, "failed Previous rolls back cleanly");
        Console.WriteLine("PASS: late joins, same-time requests, rollover, long absence, early completion, invalid durations and 10,000 native-delay events, sequential/shuffle history, restart/previous and single-track navigation, duplicate finish suppression, switch-off/skip cancellation and early-timer deferral, invalid/never-started completion rejection and failed-navigation rollback.");
    }
}
