using System.Collections.Generic;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public sealed class ControlStyleOverride
    {
        public Color? TextColor { get; set; }
        public Color? PanelColor { get; set; }
        public Color? HoverPanelColor { get; set; }
        public Color? HoverTextColor { get; set; }

        public string TextRole { get; set; }
        public string PanelRole { get; set; }
        public string HoverPanelRole { get; set; }
        public string HoverTextRole { get; set; }

        public float? BorderRadiusPixels { get; set; }
        public Vector4? Padding { get; set; }

        public ControlStyle ResolveAgainst(ControlStyle parent, IReadOnlyDictionary<string, Color> theme)
        {
            var source = parent ?? new ControlStyle(Color.White, Color.Gray);
            var resolved = source.Clone();

            if (TextColor.HasValue)
            {
                resolved.SetTextColor(TextColor.Value);
                resolved.TextRole = null;
            }

            if (PanelColor.HasValue)
            {
                resolved.SetPanelColor(PanelColor.Value);
                resolved.PanelRole = null;
            }

            if (HoverPanelColor.HasValue)
            {
                resolved.HoverPanelColor = HoverPanelColor.Value;
                resolved.HoverPanelRole = null;
            }

            if (HoverTextColor.HasValue)
            {
                resolved.HoverTextColor = HoverTextColor.Value;
                resolved.HoverTextRole = null;
            }

            if (TextRole != null)
                resolved.TextRole = TextRole;

            if (PanelRole != null)
                resolved.PanelRole = PanelRole;

            if (HoverPanelRole != null)
                resolved.HoverPanelRole = HoverPanelRole;

            if (HoverTextRole != null)
                resolved.HoverTextRole = HoverTextRole;

            if (BorderRadiusPixels.HasValue)
                resolved.BorderRadiusPixels = BorderRadiusPixels.Value;

            resolved.Padding = Padding.HasValue ? Padding.Value : Vector4.Zero;
            resolved.ThemeColors = theme ?? source.ThemeColors;
            return resolved;
        }
    }
}
