using System;
using System.Globalization;
using Acrux.Data;
using Acrux.Data.Converters;

namespace LcdMod.Client.Apps.Views
{
    /// <summary>True when an enum value's name matches the converter parameter.</summary>
    public sealed class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is Enum) || parameter == null)
                return BindingOperations.DoNothing;
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }
}
