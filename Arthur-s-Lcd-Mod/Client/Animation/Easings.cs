using System;

namespace LcdMod.Client.Animation
{
    public static class Easings
    {
        public static float Apply(EasingMode mode, float progress)
        {
            progress = Clamp01(progress);

            switch (mode)
            {
                case EasingMode.EaseInQuadratic:
                    return progress * progress;

                case EasingMode.EaseOutQuadratic:
                    return 1f - (1f - progress) * (1f - progress);

                case EasingMode.EaseInOutQuadratic:
                    return progress < 0.5f
                        ? 2f * progress * progress
                        : 1f - (float)Math.Pow(-2f * progress + 2f, 2d) / 2f;

                case EasingMode.EaseInCubic:
                    return progress * progress * progress;

                case EasingMode.EaseOutCubic:
                {
                    float inverse = 1f - progress;
                    return 1f - inverse * inverse * inverse;
                }

                case EasingMode.EaseInOutCubic:
                    return progress < 0.5f
                        ? 4f * progress * progress * progress
                        : 1f - (float)Math.Pow(-2f * progress + 2f, 3d) / 2f;

                case EasingMode.SmoothStep:
                    return progress * progress * (3f - 2f * progress);

                default:
                    return progress;
            }
        }

        public static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;

            return value > 1f ? 1f : value;
        }
    }
}
