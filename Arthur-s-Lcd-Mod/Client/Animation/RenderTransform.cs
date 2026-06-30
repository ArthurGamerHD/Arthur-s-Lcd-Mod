using System;
using VRageMath;

namespace LcdMod.Client.Animation
{
    /// <summary>
    /// Describes a visual-only transformation applied after a control has been
    /// arranged. A render transform must not change layout bounds or hit tests.
    /// </summary>
    public abstract class RenderTransform
    {
        /// <summary>
        /// Gets a transform which leaves rendered sprites unchanged.
        /// </summary>
        public static RenderTransform Identity => ScaleTransform.Identity;

        /// <summary>
        /// Gets whether this transform leaves rendered sprites unchanged.
        /// </summary>
        public abstract bool IsIdentity { get; }

        /// <summary>
        /// Transforms a sprite position relative to the control bounds.
        /// </summary>
        /// <param name="position">The original sprite position.</param>
        /// <param name="bounds">The arranged control bounds.</param>
        public abstract Vector2 TransformPosition(Vector2 position, RectangleF bounds);

        /// <summary>
        /// Transforms the size of a texture or rectangle sprite.
        /// </summary>
        public abstract Vector2 TransformSize(Vector2 size);

        /// <summary>
        /// Transforms the scale used by a text sprite.
        /// </summary>
        public abstract float TransformTextScale(float scale);

        /// <summary>
        /// Interpolates between two compatible render transforms.
        /// Null values are treated as <see cref="Identity"/>.
        /// </summary>
        public static RenderTransform Interpolate(RenderTransform from, RenderTransform to, float progress)
        {
            from = from ?? Identity;
            to = to ?? Identity;
            return from.InterpolateTo(to, progress);
        }

        /// <summary>
        /// Interpolates from this transform to a compatible target transform.
        /// </summary>
        protected internal abstract RenderTransform InterpolateTo(RenderTransform target, float progress);
    }
}
