using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class CharacterWindowTests
{
    [Fact]
    public void Character_window_lays_out_import_controls_and_releases_preview_on_close()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            CharacterWindow? window = null;
            try
            {
                window = new CharacterWindow(new CharacterLibrary(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
                    _ => Task.FromResult(true), () => null);
                var image = (Image)window.FindName("PreviewImage");
                window.ShowPreview(null);
                Assert.True(window.PreviewTimerRunning);
                ((ListBox)window.FindName("Characters")).ItemsSource = new[] {
                    new { Label = "默认人物 · 当前", Thumbnail = image.Source },
                    new { Label = "新人物", Thumbnail = image.Source } };
                ((StackPanel)window.FindName("ImportSettings")).Visibility = Visibility.Visible;
                ((TextBox)window.FindName("CharacterName")).Text = "新人物";
                ((TextBlock)window.FindName("PreviewTitle")).Text = "预览新人物";
                ((Button)window.FindName("ApplyButton")).Content = "导入并使用";
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(704, 742));
                content.Arrange(new Rect(0, 0, 704, 742));
                content.UpdateLayout();
                Assert.True(((Button)window.FindName("ApplyButton")).ActualWidth > 70);
                Assert.True(image.ActualWidth >= 200);
                var qaDirectory = Environment.GetEnvironmentVariable("DSH_CHARACTER_QA_DIR");
                if (!string.IsNullOrEmpty(qaDirectory))
                {
                    Directory.CreateDirectory(qaDirectory);
                    var bitmap = new RenderTargetBitmap(704, 742, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(qaDirectory, "character-window.png"));
                    encoder.Save(output);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                window?.Close();
                if (window is not null) Assert.False(window.PreviewTimerRunning);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
