namespace CustomHideoutRadio
{
    public static class ClipGuard
    {
        public static bool ValidDuration(float seconds) => !float.IsNaN(seconds) && !float.IsInfinity(seconds) && seconds >= 0.1f && seconds <= 36000f;
        public static bool CanComplete(bool observedPlaying, float duration) => observedPlaying && ValidDuration(duration);
    }
}
