using System;
using VRageMath;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;

namespace LcdMod.Client.Animation
{
    /// <summary>
    /// Applies a uniform visual scale around an origin inside the arranged
    /// control bounds.
    /// </summary>
    public sealed class ScaleTransform : RenderTransform, IEquatable<ScaleTransform>
    {
        /// <summary>
        /// The normalized center of a control's bounds.
        /// </summary>
        public static readonly Vector2 CenterOrigin = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// A centered transform with a scale of one.
        /// </summary>
        public new static readonly ScaleTransform Identity = new ScaleTransform(1f, CenterOrigin);

        /// <summary>
        /// Creates a centered uniform scale transform.
        /// </summary>
        public ScaleTransform(float scale)
            : this(scale, CenterOrigin)
        {
        }

        /// <summary>
        /// Creates a uniform scale transform around a normalized origin.
        /// </summary>
        /// <param name="scale">The uniform scale multiplier.</param>
        /// <param name="origin">
        /// The normalized origin inside the control bounds. (0,0) is the
        /// top-left corner and (1,1) is the bottom-right corner.
        /// </param>
        public ScaleTransform(float scale, Vector2 origin)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale))
                throw new ArgumentOutOfRangeException(nameof(scale));

            Scale = scale;
            Origin = origin;
        }

        /// <summary>
        /// Gets the uniform scale multiplier.
        /// </summary>
        public float Scale { get; }

        /// <summary>
        /// Gets the normalized scale origin inside the control bounds.
        /// </summary>
        public Vector2 Origin { get; }

        public override bool IsIdentity => Math.Abs(Scale - 1f) < 0.001;

        public override Vector2 TransformPosition(Vector2 position, RectangleF bounds)
        {
            Vector2 pivot = new Vector2(
                bounds.X + bounds.Width * Origin.X,
                bounds.Y + bounds.Height * Origin.Y);

            return pivot + (position - pivot) * Scale;
        }

        public override Vector2 TransformSize(Vector2 size)
        {
            return size * Scale;
        }

        public override float TransformTextScale(float scale)
        {
            return scale * Scale;
        }

        /// <summary>
        /// Returns a copy with a different scale.
        /// </summary>
        public ScaleTransform WithScale(float scale)
        {
            return new ScaleTransform(scale, Origin);
        }

        /// <summary>
        /// Returns a copy with a different normalized origin.
        /// </summary>
        public ScaleTransform WithOrigin(Vector2 origin)
        {
            return new ScaleTransform(Scale, origin);
        }

        /// <summary>
        /// Interpolates between two scale transforms.
        /// Null values are treated as <see cref="Identity"/>.
        /// </summary>
        public static ScaleTransform Interpolate(ScaleTransform from, ScaleTransform to, float progress)
        {
            from = from ?? Identity;
            to = to ?? Identity;

            return new ScaleTransform(
                MathHelper.Lerp(from.Scale, to.Scale, progress),
                from.Origin + (to.Origin - from.Origin) * progress);
        }

        protected internal override RenderTransform InterpolateTo(RenderTransform target, float progress)
        {
            ScaleTransform scaleTarget = target as ScaleTransform;
            if (scaleTarget == null)
                throw new ArgumentException(
                    "ScaleTransform can only interpolate to another ScaleTransform.",
                    nameof(target));

            return Interpolate(this, scaleTarget, progress);
        }

        public bool Equals(ScaleTransform other)
        {
            return !ReferenceEquals(other, null) &&
                   Scale.Equals(other.Scale) &&
                   Origin.Equals(other.Origin);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ScaleTransform);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return Scale.GetHashCode() * 397 ^ Origin.GetHashCode();
            }
        }

        public static bool operator ==(ScaleTransform left, ScaleTransform right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (ReferenceEquals(left, null))
                return false;
            return left.Equals(right);
        }

        public static bool operator !=(ScaleTransform left, ScaleTransform right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return "ScaleTransform { Scale: " + Scale + ", Origin: " + Origin + " }";
        }
    }
}
