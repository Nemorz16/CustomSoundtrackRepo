using System;
namespace CustomHideoutRadio
{
    // Pure arithmetic; one start timestamp, no scheduler, Unity access, or looping work.
    public static class PlaybackPosition
    {
        public static float GetOffset(double now, double duration, ref double start)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentException("Invalid clock timestamp.");
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration < 0.1 || duration > 36000)
                throw new ArgumentException("Track duration must be between 0.1 and 36000 seconds.");
            if (start < 0 || now < start) start = now;
            double offset = (now - start) % duration;
            // Native PlayWithOffset truncates remaining milliseconds before Task.Delay.
            // An early completion must restart instead of recursing via a zero delay.
            if (duration - offset <= 0.025)
            {
                start = now;
                offset = 0;
            }
            return (float)offset;
        }
    }
}
