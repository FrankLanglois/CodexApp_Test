using System;
using System.Globalization;
using System.Windows.Data;

namespace VideoLibrary.ViewModels
{
    public class SizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long l)
            {
                if (l > 1_000_000_000) return $"{l / 1_000_000_000.0:F2} GB";
                if (l > 1_000_000) return $"{l / 1_000_000.0:F2} MB";
                if (l > 1_000) return $"{l / 1_000.0:F2} KB";
                return $"{l} B";
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
