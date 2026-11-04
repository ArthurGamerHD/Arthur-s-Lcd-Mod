using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using VRageMath;

namespace LcdMod.Client.Gui.Styling
{
    /// <summary>
    /// The surface design-time data is made for: the IDE preview's default LCD (its background, the block's font
    /// colour, the owner faction's header colour) and the theme an app gets from them (<see cref="DesignThemeResources"/>).
    /// A *.AcruxDesign.cs file: compiled for the preview, not packed by MDK.
    /// </summary>
    internal static class DesignSurface
    {
        public const string Font = "White";
        public const float FontScale = 1f;

        public static readonly Color Background = new Color(0, 88, 151);

        public static Color Foreground => DesignThemeResources.Default.Foreground;
        public static Color Header => DesignThemeResources.Default.Seed;
        public static Color Accent => Theme(ThemeResources.AccentColor, Header);
        public static Color AccentContainer => Theme(ThemeResources.AccentContainerColor, Header);
        public static Color SecondaryContainer => Theme(ThemeResources.SecondaryContainerColor, Background);
        public static Color OnSecondaryContainer => Theme(ThemeResources.OnSecondaryContainerColor, Foreground);
        public static Color Surface => Theme(ThemeResources.SurfaceColor, Background);
        public static Color Muted => Theme(ThemeResources.MutedTextColor, Foreground);
        public static Color Warning => ColorConfigComponentExtensions.ResolveWarningColor(null);
        public static Color Error => ColorConfigComponentExtensions.ResolveErrorColor(null);
        public static Color Success => Theme(ThemeResources.SuccessColor, Color.LimeGreen);

        public static Color Theme(ResourceKey<Color> key, Color fallback)
        {
            return DesignThemeResources.Default.Resolve(key, fallback);
        }
    }
}
