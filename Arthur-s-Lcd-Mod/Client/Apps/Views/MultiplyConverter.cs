using System;
using System.Globalization;
using Acrux.Data;
using Acrux.Data.Converters;

namespace LcdMod.Client.Apps.Views
{
    /// <summary>Multiplies a number by the converter parameter.</summary>
    public sealed class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double number, factor;
            if (!TryParse(value, out number) || !TryParse(parameter, out factor))
                return BindingOperations.DoNothing;
            double result = number * factor;
            if (targetType == typeof(float) || targetType == typeof(float?))
                return (float)result;
            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }

        static bool TryParse(object value, out double number)
        {
            number = 0d;
            return value != null && double.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) &&
                   !double.IsInfinity(number);
        }
    }
}
