using System.Collections.Generic;
using LcdMod.Client.Extensions;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public sealed class ControlStyle
    {
        public IReadOnlyDictionary<string, Color> ThemeColors { get; set; }

        public ControlStyle(Color textColor, Color panelColor)
        {
            TextColor = textColor;
            PanelColor = panelColor;
        }

        public Color TextColor { get; private set; }
        public Color PanelColor { get; private set; }
        public Color? HoverPanelColor { get; set; }
        public Color? HoverTextColor { get; set; }
        public string TextRole { get; set; }
        public string PanelRole { get; set; }
        public string HoverPanelRole { get; set; }
        public string HoverTextRole { get; set; }
        public float BorderPercentage { get; set; } = 0.2f;
        
        /// <summary>
        /// Left, Top, Right, Bottom and are expressed as 0..1 percentages of the control bounds.
        /// Padding affects the visual view box only; the original bounds are kept for layout and hit testing.
        /// </summary>
        public Vector4 Padding { get; set; }
        public bool InheritParentColors { get; set; }
        public bool InheritParentBorderPercentage { get; set; }

        public static ControlStyle FromThemeRoles(
            string textRole,
            string panelRole,
            string hoverPanelRole,
            string hoverTextRole)
        {
            return FromThemeRoles(
                textRole,
                panelRole,
                hoverPanelRole,
                hoverTextRole,
                null);
        }

        public static ControlStyle FromThemeRoles(
            string textRole,
            string panelRole,
            string hoverPanelRole,
            string hoverTextRole,
            IReadOnlyDictionary<string, Color> theme)
        {
            return new ControlStyle(Color.Transparent, Color.Transparent)
            {
                TextRole = textRole,
                PanelRole = panelRole,
                HoverPanelRole = hoverPanelRole,
                HoverTextRole = hoverTextRole,
                ThemeColors = theme,
            };
        }

        public static ControlStyle PaddingOnly(Vector4 padding)
        {
            return new ControlStyle(Color.White, Color.Transparent)
            {
                Padding = padding,
                InheritParentColors = true,
                InheritParentBorderPercentage = true
            };
        }

        public void SetColors(Color textColor, Color panelColor)
        {
            TextColor = textColor;
            PanelColor = panelColor;
        }

        public Color GetPanelColor(bool hovered)
        {
            if (!hovered)
                return string.IsNullOrEmpty(PanelRole) ? PanelColor : GetThemeColor(ThemeColors, PanelRole);

            return string.IsNullOrEmpty(HoverPanelRole)
                ? HoverPanelColor ?? GetPanelColor(false).DeriveAccentColor()
                : GetThemeColor(ThemeColors, HoverPanelRole);
        }

        public Color GetTextColor(bool hovered)
        {
            if (!hovered)
                return string.IsNullOrEmpty(TextRole) ? TextColor : GetThemeColor(ThemeColors, TextRole);

            return string.IsNullOrEmpty(HoverTextRole)
                ? HoverTextColor ?? GetTextColor(false)
                : GetThemeColor(ThemeColors, HoverTextRole);
        }

        public ControlStyle ResolveAgainst(ControlStyle parent)
        {
            return ResolveAgainst(parent, parent == null ? null : parent.ThemeColors);
        }

        public ControlStyle ResolveAgainst(ControlStyle parent, IReadOnlyDictionary<string, Color> parentTheme)
        {
            if (!InheritParentColors &&
                !InheritParentBorderPercentage &&
                (ThemeColors != null || parentTheme == null))
                return this;

            var resolved = Clone();
            if (parent != null)
            {
                if (InheritParentColors)
                {
                    resolved.TextColor = parent.TextColor;
                    resolved.PanelColor = parent.PanelColor;
                    resolved.HoverPanelColor = parent.HoverPanelColor;
                    resolved.HoverTextColor = parent.HoverTextColor;
                    resolved.TextRole = parent.TextRole;
                    resolved.PanelRole = parent.PanelRole;
                    resolved.HoverPanelRole = parent.HoverPanelRole;
                    resolved.HoverTextRole = parent.HoverTextRole;
                    resolved.ThemeColors = parent.ThemeColors;
                }

                if (InheritParentBorderPercentage)
                    resolved.BorderPercentage = parent.BorderPercentage;
            }

            if (resolved.ThemeColors == null)
                resolved.ThemeColors = parentTheme;

            return resolved;
        }

        internal ControlStyle ResolveTheme(IReadOnlyDictionary<string, Color> theme)
        {
            if (ThemeColors != null || theme == null)
                return this;

            var resolved = Clone();
            resolved.ThemeColors = theme;
            return resolved;
        }

        ControlStyle Clone()
        {
            return new ControlStyle(TextColor, PanelColor)
            {
                HoverPanelColor = HoverPanelColor,
                HoverTextColor = HoverTextColor,
                TextRole = TextRole,
                PanelRole = PanelRole,
                HoverPanelRole = HoverPanelRole,
                HoverTextRole = HoverTextRole,
                BorderPercentage = BorderPercentage,
                Padding = Padding,
                InheritParentColors = InheritParentColors,
                InheritParentBorderPercentage = InheritParentBorderPercentage,
                ThemeColors = ThemeColors
            };
        }

        static Color GetThemeColor(IReadOnlyDictionary<string, Color> theme, string role)
        {
            if (theme == null || string.IsNullOrEmpty(role))
                throw new ResourceKeyNotFoundException(role, "ThemeColors");

            Color color;
            if (!theme.TryGetValue(role, out color))
                throw new ResourceKeyNotFoundException(role, "ThemeColors");

            return color;
        }
    }
}
