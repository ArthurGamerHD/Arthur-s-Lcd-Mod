using System;
using System.Globalization;
using Acrux.Data;
using Acrux.Data.Converters;

namespace LcdMod.Client.Apps.Views
{
    /// <summary>Formats a 0-100 percentage like the legacy LCD percentage labels ("45%").</summary>
    public sealed class PercentTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double percentage;
            if (value == null ||
                !double.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out percentage))
                return BindingOperations.DoNothing;
            return (Math.Max(0d, Math.Min(100d, percentage)) / 100d)
                .ToString("P0", culture ?? CultureInfo.CurrentCulture).Replace(" ", string.Empty);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }
}
