using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Common.Config.Components
{
    public static class GeneralConfigComponentExtensions
    {
        public const float MIN_SCALE = 0.1f;
        public const float MAX_SCALE = 10f;

        public static float GetScale(this GeneralConfigComponent config)
        {
            return config == null ? 1f : MathHelper.Clamp(config.InternalScale, MIN_SCALE, MAX_SCALE);
        }

        public static void SetScale(this GeneralConfigComponent config, float value)
        {
            if (config != null)
                config.InternalScale = MathHelper.Clamp(value, MIN_SCALE, MAX_SCALE);
        }
    }

    public static class ColorConfigComponentExtensions
    {
        static readonly Color DefaultErrorColor =
            new Color { PackedValue = 0xFF202060u };

        static readonly Color DefaultWarningColor =
            new Color { PackedValue = 0xFF10A0E0u };

        public static Color ResolveHeaderColor(this ColorConfigComponent config, IMyTerminalBlock block)
        {
            return config == null
                ? GetDefaultHeaderColor(block)
                : config.HeaderColor.Get(!config.CustomizedColors, () => GetDefaultHeaderColor(block));
        }

        public static Color ResolveErrorColor(this ColorConfigComponent config)
        {
            return config == null
                ? DefaultErrorColor
                : config.ErrorColor.Get(!config.CustomizedColors, () => DefaultErrorColor);
        }

        public static Color ResolveWarningColor(this ColorConfigComponent config)
        {
            return config == null
                ? DefaultWarningColor
                : config.WarningColor.Get(!config.CustomizedColors, () => DefaultWarningColor);
        }

        static Color GetDefaultHeaderColor(IMyTerminalBlock block)
        {
            return block == null
                ? FactionHelperCommon.DefaultColor
                : FactionHelperCommon.GetAccent(block);
        }
    }
}
