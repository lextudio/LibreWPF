using System;
using System.Globalization;
using System.Windows.Data;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public sealed class SmokeItemDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is SmokeItem item
            ? $"{item.Name}={item.Value}/{item.Category}"
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
