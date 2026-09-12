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
                // The built-in idle state has one action, so there is nothing to choose between.
                var actions = (ComboBox)window.FindName("PreviewAction");
                Assert.Single(actions.Items);
                Assert.False(actions.IsEnabled);
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

    [Fact]
    public void The_preview_action_combo_follows_the_selected_state()
    {
        Exception? failure = null;
        var root = Path.Combine(Path.GetTempPath(), "dsh-character-window-" + Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            CharacterWindow? window = null;
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(Path.Combine(root, "idle.gif"), CharacterLibraryTests.TinyGif());
                File.WriteAllBytes(Path.Combine(root, "stretch.png"), CharacterLibraryTests.TinyPng());
                File.WriteAllText(Path.Combine(root, "character.json"), """
                    {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
                     "actions":{"idle":{
                       "primary":{"type":"gif","file":"idle.gif"},
                       "extras":[{"name":"伸懒腰","type":"png","file":"stretch.png","frameDurationMs":1500}]}}}
                    """);
                var library = new CharacterLibrary(Path.Combine(root, "library-root"));
                using (var draft = library.PrepareDirectory(root, CancellationToken.None))
                {
                    var info = library.Commit(draft, "多动作", new(.5, .1), .95);
                    window = new CharacterWindow(library, _ => Task.FromResult(true), () => info.Id);
                    window.ShowPreview(library.Load(info.Id));

                    var actions = (ComboBox)window.FindName("PreviewAction");
                    Assert.Equal(2, actions.Items.Count);
                    Assert.True(actions.IsEnabled);
                    Assert.Contains("主动作", actions.Items[0]!.ToString(), StringComparison.Ordinal);
                    Assert.Contains("附加", actions.Items[1]!.ToString(), StringComparison.Ordinal);

                    // Selecting a state this character does not define falls back to idle and keeps its catalog.
                    ((ComboBox)window.FindName("PreviewState")).SelectedIndex = 1;
                    Assert.Equal(2, actions.Items.Count);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                window?.Close();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
