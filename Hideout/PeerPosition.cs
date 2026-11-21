namespace CustomHideoutRadio
{
    public static class PeerPosition
    {
        public static bool IsUsable(float position, float duration, float pitch)
        {
            return !float.IsNaN(position) && !float.IsInfinity(position) &&
                !float.IsNaN(duration) && !float.IsInfinity(duration) &&
                !float.IsNaN(pitch) && !float.IsInfinity(pitch) && pitch > 0 &&
                position >= 0 && duration > 0 && duration - position > 0.05f;
        }
    }
}
