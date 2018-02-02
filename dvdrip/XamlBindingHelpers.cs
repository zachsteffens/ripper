using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace dvdrip
{
    public class ColumnViewportConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double columnHeight = System.Convert.ToDouble(value);
            return new Rect(0, 0, 1, columnHeight * 2);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("Source shouldn't be updated");
        }

        #endregion
    }
    [ValueConversion(typeof(object), typeof(string))]
    public class TabSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ((double)value + 25).ToString() + ", 26";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
    [ValueConversion(typeof(object), typeof(string))]
    public class TabSizeZeroConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ((double)value).ToString() + ", 0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
    public class rippingDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate progressBarDataTemplate { get; set; }
        public DataTemplate completeDataTemplate { get; set; }
        public DataTemplate waitingTextDataTemplate { get; set; }
        public DataTemplate failedRipDataTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            QueuedItem thisItem = (QueuedItem)item;
            if (thisItem.ripping)
            {
                return progressBarDataTemplate;
            }
            else
            {
                if (thisItem.ripped == true)
                {
                    return completeDataTemplate;
                }
                else
                {
                    if (thisItem.failedRip == true)
                    {
                        return failedRipDataTemplate;
                    }
                    else
                        return waitingTextDataTemplate;
                }
            }


        }
    }
    public class copyingDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate progressBarDataTemplate { get; set; }
        public DataTemplate completeDataTemplate { get; set; }
        public DataTemplate waitingTextDataTemplate { get; set; }


        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            QueuedItem thisItem = (QueuedItem)item;
            if (thisItem.copying)
            {
                return progressBarDataTemplate;
            }
            else
            {
                if (thisItem.copied == true)
                {
                    return completeDataTemplate;
                }
                else
                {
                    return waitingTextDataTemplate;
                }
            }


        }
    }
    public class compressingDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate progressBarDataTemplate { get; set; }
        public DataTemplate completeDataTemplate { get; set; }
        public DataTemplate waitingTextDataTemplate { get; set; }
        public DataTemplate failedCompressDataTemplate { get; set; }
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            QueuedItem thisItem = (QueuedItem)item;
            if (thisItem.compressing)
            {
                return progressBarDataTemplate;
            }
            else
            {
                if (thisItem.compressed == true)
                {
                    return completeDataTemplate;
                }
                else
                {
                    if (thisItem.failedCompression)
                        return failedCompressDataTemplate;
                    else
                        return waitingTextDataTemplate;
                }
            }


        }
    }
}
