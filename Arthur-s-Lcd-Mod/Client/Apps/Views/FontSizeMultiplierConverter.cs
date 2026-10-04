using System;
using System.Globalization;
using Adk.Gui.Data;
using Adk.Gui.Data.Converters;

namespace LcdMod.Client.Apps.Views
{
    public sealed class FontSizeMultiplierConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Scale(value, targetType, parameter, culture, false);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Scale(value, targetType, parameter, culture, true);
        }

        static object Scale(object value, Type targetType, object parameter, CultureInfo culture, bool inverse)
        {
            double size, multiplier;
            culture = culture ?? CultureInfo.CurrentCulture;
            if (value == null || parameter == null ||
                !double.TryParse(System.Convert.ToString(value, culture), NumberStyles.Float, culture, out size) ||
                !double.TryParse(System.Convert.ToString(parameter, culture), NumberStyles.Float, culture, out multiplier) ||
                size <= 0d || multiplier <= 0d || double.IsNaN(multiplier) || double.IsInfinity(multiplier))
                return BindingOperations.DoNothing;
            double result = inverse ? size / multiplier : size * multiplier;
            if (result <= 0d || double.IsNaN(result) || double.IsInfinity(result))
                return BindingOperations.DoNothing;
            if (targetType == typeof(double) || targetType == typeof(double?) || targetType == typeof(object)) return result;
            if ((targetType == typeof(float) || targetType == typeof(float?)) &&
                result <= float.MaxValue && (float)result > 0f) return (float)result;
            return BindingOperations.DoNothing;
        }
    }
}
