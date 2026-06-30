using System;
using VRageMath;

namespace LcdMod.Client.Animation
{
    public delegate T AnimationInterpolator<T>(T from, T to, float progress);

    public static class AnimationInterpolators
    {
        public static readonly AnimationInterpolator<float> Float = MathHelper.Lerp;
        public static readonly AnimationInterpolator<double> Double = InterpolateDouble;
        public static readonly AnimationInterpolator<int> Int32 = InterpolateInt;
        public static readonly AnimationInterpolator<Color> Color = InterpolateColor;
        public static readonly AnimationInterpolator<RenderTransform> RenderTransform =
            LcdMod.Client.Animation.RenderTransform.Interpolate;
        public static readonly AnimationInterpolator<ScaleTransform> ScaleTransform =
            LcdMod.Client.Animation.ScaleTransform.Interpolate;
        static double InterpolateDouble(double from, double to, float progress) => MathHelper.Lerp(from, to, (double)progress);
        static int InterpolateInt(int from, int to, float progress) => (int)Math.Round(MathHelper.Lerp(from, to, progress), MidpointRounding.AwayFromZero);

        static Color InterpolateColor(Color from, Color to, float progress)
        {
            return new Color(
                InterpolateByte(from.R, to.R, progress),
                InterpolateByte(from.G, to.G, progress),
                InterpolateByte(from.B, to.B, progress),
                InterpolateByte(from.A, to.A, progress));
        }

        static byte InterpolateByte(byte from, byte to, float progress)
        {
            float value = MathHelper.Lerp(from, to, progress);
            value = MathHelper.Clamp(value, byte.MinValue, byte.MaxValue);
            return (byte)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
