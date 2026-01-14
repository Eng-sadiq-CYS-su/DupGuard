using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using Models;

namespace DupGuard.Utilities
{
    /// <summary>
    /// Converts file size in bytes to human readable format
    /// </summary>
    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                return bytes.ToFileSizeString();
            }
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Inverts boolean values
    /// </summary>
    public class BooleanInverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Convert(value, targetType, parameter, culture);
        }
    }

    /// <summary>
    /// Converts total size of duplicate groups to readable format
    /// </summary>
    public class TotalSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is System.Collections.ObjectModel.ObservableCollection<DuplicateGroup> groups)
            {
                var totalSavings = groups.Sum(g => g.PotentialSavingsIfKeepNewest);
                return $"مساحة محررة محتملة: {totalSavings.ToFileSizeString()}";
            }
            return "لا توجد مكررات";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts progress width for visual representation
    /// </summary>
    public class ProgressWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double progress && parameter is string maxWidthStr &&
                double.TryParse(maxWidthStr, out double maxWidth))
            {
                return (progress / 100.0) * maxWidth;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
