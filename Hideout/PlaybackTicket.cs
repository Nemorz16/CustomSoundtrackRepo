using System.Threading;
namespace CustomHideoutRadio
{
    // Native async completion/cancellation can arrive after a track has been skipped.
    public sealed class PlaybackTicket
    {
        public readonly long Generation;
        private int cancelled;
        private int queued;
        public PlaybackTicket(long generation) { Generation = generation; }
        public bool IsCurrent(long generation) => Generation == generation && Volatile.Read(ref cancelled) == 0;
        public void AllowRequeue() { Interlocked.Exchange(ref queued, 0); }
        public void Cancel() { Interlocked.Exchange(ref cancelled, 1); }
        public bool QueueOnce() => Volatile.Read(ref cancelled) == 0 && Interlocked.Exchange(ref queued, 1) == 0;
    }
}
