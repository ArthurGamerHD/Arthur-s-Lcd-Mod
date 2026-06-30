namespace LcdMod.Client.Animation
{
    /// <summary>
    /// One sequential step in an animation. The controller calls Begin exactly
    /// once when this step becomes active, then applies progress from 0 to 1.
    /// </summary>
    public interface IAnimationStep
    {
        int DurationFrames { get; }

        /// <summary>
        /// True when applying this step changes rendered output and should keep
        /// the visual tree dirty while it is active.
        /// </summary>
        bool RequiresRedraw { get; }

        void Begin();

        void Apply(float progress);
    }
}
