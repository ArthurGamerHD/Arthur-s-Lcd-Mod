using System;
using System.Globalization;
using Acrux.Data;
using Acrux.Data.Converters;

namespace LcdMod.Client.Apps.Views
{
    /// <summary>
    /// An enum value as a style class: its name in lower case (<c>Classes="{Binding Mode, Converter={StaticResource
    /// EnumClass}}"</c>), so styles select the mode as <c>UserControl.card</c>.
    /// </summary>
    public sealed class EnumClassConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is Enum))
                return BindingOperations.DoNothing;
            return value.ToString().ToLowerInvariant();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }
}
