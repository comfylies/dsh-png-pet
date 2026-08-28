using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace PetHelper;

/// <summary>Live image thumbnails decode from the base64 kept by the Helper; history images render a placeholder.</summary>
public sealed class DialogueImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DialogueImage { DataBase64: { Length: > 0 } data })
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(System.Convert.FromBase64String(data));
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return System.Windows.DependencyProperty.UnsetValue;
            }
        }
        return System.Windows.DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
