using System;
using System.Globalization;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Extensions;
using Adk.Gui.Markup;
using Adk.Gui.Styling;
using LcdMod.Client.Extensions;
using VRageMath;

namespace LcdMod.Client.Apps.Views
{
    /// <summary>
    /// Adjusts a color until it reaches a minimum contrast ratio (3:1) against a background. ConverterParameter
    /// names the background as a resource key or a color literal; without one, the nearest ancestor Border's
    /// background is used.
    /// </summary>
    internal sealed class MinimumContrastConverter : IContextValueConverter
    {
        const double MINIMUM_CONTRAST = 3.0;
        static readonly PropertyConverter Literals = new PropertyConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture, Control target)
        {
            Color color;
            if (!TryGetColor(value, out color))
                return value;
            Color background;
            if (TryResolveBackground(parameter, target, out background))
                color = color.EnsureMinimalContrast(background, MINIMUM_CONTRAST);
            return targetType == typeof(BackgroundBrush) ? new SolidColorBrush(color) : (object)color;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Convert(value, targetType, parameter, culture, null);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }

        static bool TryResolveBackground(object parameter, Control target, out Color background)
        {
            if (TryGetColor(parameter, out background))
                return true;
            var text = parameter as string;
            if (!string.IsNullOrWhiteSpace(text))
                return target != null && target.TryFindResource(text.Trim(), out background) || TryParse(text, out background);

            for (VisualElement current = (target as VisualElement)?.Parent; current != null; current = current.Parent)
            {
                var border = current as Border;
                if (border == null)
                    continue;
                Color candidate = border.BackgroundColor;
                if (candidate.A == 0)
                    continue;
                background = candidate;
                return true;
            }
            return false;
        }

        static bool TryGetColor(object value, out Color color)
        {
            if (value is Color)
            {
                color = (Color)value;
                return true;
            }
            var brush = value as BackgroundBrush;
            color = brush == null ? default(Color) : brush.FallbackColor;
            return brush != null;
        }

        static bool TryParse(string text, out Color color)
        {
            try
            {
                color = (Color)Literals.Convert(text, typeof(Color));
                return true;
            }
            catch (FormatException)
            {
                color = default(Color);
                return false;
            }
        }
    }
}
