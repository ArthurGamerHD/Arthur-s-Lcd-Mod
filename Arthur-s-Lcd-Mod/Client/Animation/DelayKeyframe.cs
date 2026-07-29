using ArgumentOutOfRangeException = LcdMod.Common.Exceptions.ArgumentOutOfRangeException;

namespace LcdMod.Client.Animation
{
    /// <summary>
    /// A sequential pause. It does not request redraws while active.
    /// </summary>
    public sealed class DelayKeyframe : IAnimationStep
    {
        public DelayKeyframe(int durationFrames)
        {
            if (durationFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(durationFrames));

            DurationFrames = durationFrames;
        }

        public int DurationFrames { get; private set; }

        public bool RequiresRedraw => false;

        public void Begin()
        {
        }

        public void Apply(float progress)
        {
        }
    }
}
