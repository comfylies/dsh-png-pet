using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class CharacterWindowTests
{
    /// <summary>An imported character whose idle state holds a looping primary and one named extra.</summary>
    private static (CharacterLibrary Library, string Id, string Root) ImportCharacterWithAnExtra()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsh-character-window-" + Guid.NewGuid().ToString("N"));
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
        using var draft = library.PrepareDirectory(root, CancellationToken.None);
        var info = library.Commit(draft, "多动作", new(.5, .1), .95);
        return (library, info.Id, root);
    }

    /// <summary>An imported character whose idle state holds a primary and nothing else.</summary>
    private static (CharacterLibrary Library, string Id, string Root) ImportCharacterWithOneAction()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsh-character-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var image = Path.Combine(root, "idle.gif");
        File.WriteAllBytes(image, CharacterLibraryTests.TinyGif());
        var library = new CharacterLibrary(Path.Combine(root, "library-root"));
        using var draft = library.PrepareImage(image, CancellationToken.None);
        var info = library.Commit(draft, "单动作", new(.5, .1), .95);
        return (library, info.Id, root);
    }

    /// <summary>Runs the thread's pending dispatcher work, which is where an awaited window step resumes.</summary>
    private static void DoEvents(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void WaitFor(Dispatcher dispatcher, Func<bool> until, string what)
    {
        var deadline = Environment.TickCount64 + 30_000;
        while (!until())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException($"Timed out waiting for {what}.");
            DoEvents(dispatcher);
            Thread.Sleep(5);
        }
    }

    /// <summary>Waits for a window task whose continuations run on the window's own dispatcher.</summary>
    private static void Complete(Dispatcher dispatcher, Task task)
    {
        WaitFor(dispatcher, () => task.IsCompleted, "the window task");
        task.GetAwaiter().GetResult();
    }

    private static string Notice(CharacterWindow window) => ((TextBlock)window.FindName("Notice")).Text;
    private static string? StateItem(CharacterWindow window, int index) =>
        ((ComboBox)window.FindName("ActionState")).Items[index]?.ToString();
    private static void Click(CharacterWindow window, string name) =>
        ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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

    [Fact]
    public void Adding_an_action_is_refused_for_the_built_in_character()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            CharacterWindow? window = null;
            try
            {
                window = new CharacterWindow(new CharacterLibrary(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
                    _ => Task.FromResult(true), () => null);
                window.ShowPreview(null);
                window.ShowNotice(string.Empty);

                ((Button)window.FindName("AddActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Contains("内置人物不支持添加动作", ((TextBlock)window.FindName("Notice")).Text, StringComparison.Ordinal);
                Assert.Equal(Visibility.Collapsed, ((StackPanel)window.FindName("ActionSettings")).Visibility);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void Adding_an_action_lists_the_states_and_roles_of_an_imported_character()
    {
        Exception? failure = null;
        var root = Path.Combine(Path.GetTempPath(), "dsh-character-window-action-" + Guid.NewGuid().ToString("N"));
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
                using var draft = library.PrepareDirectory(root, CancellationToken.None);
                var info = library.Commit(draft, "多动作", new(.5, .1), .95);
                window = new CharacterWindow(library, _ => Task.FromResult(true), () => info.Id);
                // ShowPreview also records the previewed character id, which AddAction_Click reads.
                window.ShowPreview(library.Load(info.Id));

                ((Button)window.FindName("AddActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(Visibility.Visible, ((StackPanel)window.FindName("ActionSettings")).Visibility);
                Assert.Equal(10, ((ComboBox)window.FindName("ActionState")).Items.Count);
                var roles = ((ComboBox)window.FindName("ActionRole")).Items.Cast<string>().ToArray();
                Assert.Contains("替换主动作", roles);
                Assert.Contains("附加动作", roles);
                var extras = ((ListBox)window.FindName("ActionExtras")).Items.Cast<string>().ToArray();
                Assert.Equal(new[] { "伸懒腰" }, extras);
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

    [Fact]
    public void The_add_action_state_selector_names_what_each_state_already_has()
    {
        Exception? failure = null;
        var (library, id, root) = ImportCharacterWithAnExtra();
        var thread = new Thread(() =>
        {
            CharacterWindow? window = null;
            try
            {
                window = new CharacterWindow(library, _ => Task.FromResult(true), () => id);
                window.ShowPreview(library.Load(id));
                Click(window, "AddActionButton");

                // Spec §5: the state selector states what the state currently has before a role is picked.
                var states = ((ComboBox)window.FindName("ActionState")).Items.Cast<object>()
                    .Select(item => item?.ToString()).ToArray();
                Assert.Equal(10, states.Length);
                Assert.Equal("待机（主动作 + 1 附加）", states[0]);
                Assert.Equal("思考（无主动作）", states[1]);
                Assert.Contains("提问（无主动作）", states);
                // The preview selector keeps the plain state names.
                Assert.Equal("待机", ((ComboBox)window.FindName("PreviewState")).Items[0]?.ToString());

                // The counts follow the stored actions: removing the extra drops it from the label.
                library.RemoveExtra(id, "idle", "伸懒腰");
                window.ShowPreview(library.Load(id));
                Assert.Equal("待机（主动作）", StateItem(window, 0));

                // The built-in character stores no states, so there is nothing to count.
                window.ShowPreview(null);
                Assert.Equal("待机", StateItem(window, 0));
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

    [Fact]
    public void A_single_frame_gif_is_refused_as_an_extra_with_its_own_reason()
    {
        Exception? failure = null;
        var (library, id, root) = ImportCharacterWithAnExtra();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            CharacterWindow? window = null;
            try
            {
                // A one-frame GIF carries no duration of its own, which an extra must declare.
                File.WriteAllBytes(Path.Combine(root, "static.gif"), CharacterLibraryTests.TinyGif());
                window = new CharacterWindow(library, _ => Task.FromResult(true), () => id);
                window.ShowPreview(library.Load(id));
                Click(window, "AddActionButton");
                Assert.Equal("附加动作", ((ComboBox)window.FindName("ActionRole")).SelectedItem as string);
                ((TextBox)window.FindName("ActionName")).Text = "静态动作";

                Complete(dispatcher, window.ChooseActionSource(Path.Combine(root, "static.gif")));

                Assert.Contains("单个静态 GIF", Notice(window));
                Assert.Contains("不能作为附加动作", Notice(window));
                Assert.DoesNotContain("动作素材无法使用", Notice(window));
                // Refused before staging: the state still holds exactly the one stored extra.
                Assert.Single(library.Load(id).Document.Actions["idle"].Extras);
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

    [Fact]
    public void Saving_an_extra_without_a_name_asks_for_the_name()
    {
        Exception? failure = null;
        var (library, id, root) = ImportCharacterWithAnExtra();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            CharacterWindow? window = null;
            try
            {
                File.WriteAllBytes(Path.Combine(root, "second.png"), CharacterLibraryTests.TinyPng());
                window = new CharacterWindow(library, _ => Task.FromResult(true), () => id);
                window.ShowPreview(library.Load(id));
                Click(window, "AddActionButton");
                Complete(dispatcher, window.ChooseActionSource(Path.Combine(root, "second.png")));
                Assert.Contains("确认动作后点", Notice(window));

                // The name box is empty, which is what the extra needs; the save says so and stops.
                Click(window, "ApplyButton");

                Assert.Contains("附加动作必须有名称", Notice(window));
                Assert.DoesNotContain("保存失败", Notice(window));
                Assert.Single(library.Load(id).Document.Actions["idle"].Extras);
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

    [Fact]
    public void A_declined_primary_replacement_saves_nothing()
    {
        Exception? failure = null;
        var (library, id, root) = ImportCharacterWithOneAction();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            CharacterWindow? window = null;
            try
            {
                File.WriteAllBytes(Path.Combine(root, "replacement.png"), CharacterLibraryTests.TinyPng());
                var asked = new List<string>();
                window = new CharacterWindow(library, _ => Task.FromResult(true), () => id,
                    (text, caption) => { asked.Add($"{caption}|{text}"); return false; });
                window.ShowPreview(library.Load(id));
                Click(window, "AddActionButton");
                ((ComboBox)window.FindName("ActionRole")).SelectedIndex = 0;
                Assert.Equal("替换主动作", ((ComboBox)window.FindName("ActionRole")).SelectedItem as string);
                Complete(dispatcher, window.ChooseActionSource(Path.Combine(root, "replacement.png")));

                Click(window, "ApplyButton");

                var question = Assert.Single(asked);
                Assert.Contains("替换「待机」的主动作", question);
                Assert.Contains("无法恢复", question);
                // The staged replacement dropped nothing: the stored primary is still the old one.
                Assert.Equal(new[] { 100 }, library.Load(id)
                    .ResolveProgram(PetAnimationKey.Idle, _ => true).Loop[0].FrameDurationsMs);
                Assert.Contains("确认动作后点", Notice(window));
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

    [Fact]
    public void An_accepted_primary_replacement_is_saved_and_reports_the_reload()
    {
        Exception? failure = null;
        var (library, id, root) = ImportCharacterWithOneAction();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            CharacterWindow? window = null;
            try
            {
                File.WriteAllBytes(Path.Combine(root, "replacement.png"), CharacterLibraryTests.TinyPng());
                var reloaded = new List<string?>();
                window = new CharacterWindow(library, characterId =>
                {
                    reloaded.Add(characterId);
                    return Task.FromResult(true);
                }, () => id, (_, _) => true);
                window.ShowPreview(library.Load(id));
                Click(window, "AddActionButton");
                ((ComboBox)window.FindName("ActionRole")).SelectedIndex = 0;
                Complete(dispatcher, window.ChooseActionSource(Path.Combine(root, "replacement.png")));

                Click(window, "ApplyButton");
                WaitFor(dispatcher, () => Notice(window).Contains("动作已保存", StringComparison.Ordinal), "the save notice");

                Assert.Contains("已重新加载", Notice(window));
                Assert.Equal(new[] { 1500 }, library.Load(id)
                    .ResolveProgram(PetAnimationKey.Idle, _ => true).Loop[0].FrameDurationsMs);
                Assert.Equal(id, Assert.Single(reloaded));
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

    [Fact]
    public void An_action_that_the_running_pet_could_not_reload_is_reported_as_saved()
    {
        Exception? failure = null;
        var (library, id, root) = ImportCharacterWithAnExtra();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            CharacterWindow? window = null;
            try
            {
                File.WriteAllBytes(Path.Combine(root, "second.png"), CharacterLibraryTests.TinyPng());
                window = new CharacterWindow(library, _ => Task.FromResult(false), () => id);
                window.ShowPreview(library.Load(id));
                Click(window, "AddActionButton");
                ((TextBox)window.FindName("ActionName")).Text = "新动作";
                Complete(dispatcher, window.ChooseActionSource(Path.Combine(root, "second.png")));

                Click(window, "ApplyButton");
                WaitFor(dispatcher, () => Notice(window).Contains("动作已保存", StringComparison.Ordinal), "the save notice");

                // The save is committed, so only the reload is reported as unfinished.
                Assert.Contains("无法重新加载", Notice(window));
                Assert.Contains("请重新选择这个人物", Notice(window));
                Assert.DoesNotContain("已重新加载", Notice(window));
                Assert.Equal(2, library.Load(id).Document.Actions["idle"].Extras.Length);
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
