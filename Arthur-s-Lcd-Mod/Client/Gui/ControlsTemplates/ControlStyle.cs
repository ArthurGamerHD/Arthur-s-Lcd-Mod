using LcdMod.Client.Extensions;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public sealed class ControlStyle
    {
        public ControlStyle(Color textColor, Color panelColor)
        {
            TextColor = textColor;
            PanelColor = panelColor;
        }

        public Color TextColor { get; private set; }
        public Color PanelColor { get; private set; }
        public Color? HoverPanelColor { get; set; }
        public Color? HoverTextColor { get; set; }
        public float BorderPercentage { get; set; } = 0.2f;

        public Color GetPanelColor(bool hovered)
        {
            if (!hovered)
                return PanelColor;

            return HoverPanelColor ?? PanelColor.DeriveAccentColor();
        }

        public Color GetTextColor(bool hovered)
        {
            if (!hovered)
                return TextColor;

            return HoverTextColor ?? TextColor;
        }
    }
}
