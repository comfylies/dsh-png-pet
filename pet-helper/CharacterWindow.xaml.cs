using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PetHelper;

public partial class CharacterWindow : Window
{
    private readonly CharacterLibrary library;
    private readonly Func<string?, Task<bool>> useCharacter;
    private readonly Func<string?> currentId;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? importing;
    private CharacterDraft? draft;
    private PetAnimationPlayer? preview;
    private bool busy;
    private bool closed;
    private int previewGeneration;

    private sealed record Entry(string? Id, string Label, ImageSource? Thumbnail);
    private sealed record StateChoice(PetAnimationKey Key, string Name) { public override string ToString() => Name; }

    internal CharacterWindow(CharacterLibrary library, Func<string?, Task<bool>> useCharacter, Func<string?> currentId)
    {
        InitializeComponent();
        this.library = library; this.useCharacter = useCharacter; this.currentId = currentId;
        PreviewState.ItemsSource = new[] {
            new StateChoice(PetAnimationKey.Idle, "待机"), new(PetAnimationKey.Thinking, "思考"),
            new(PetAnimationKey.Working, "工作"), new(PetAnimationKey.ThinkingWorking, "思考与工作"),
            new(PetAnimationKey.Responding, "回复"), new(PetAnimationKey.Waiting, "等待操作"),
            new(PetAnimationKey.Question, "提问"), new(PetAnimationKey.Success, "成功"),
            new(PetAnimationKey.Error, "错误"), new(PetAnimationKey.Disconnected, "未连接") };
        PreviewState.SelectedIndex = 0;
        Loaded += async (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            MinHeight = Math.Min(MinHeight, work.Height);
            Height = Math.Min(Height, work.Height);
            Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
            await Refresh(currentId());
        };
        Closed += (_, _) =>
        {
            closed = true; previewGeneration++; lifetime.Cancel(); importing?.Cancel();
            preview?.Stop(); PreviewImage.Source = null;
            if (!busy) { draft?.Dispose(); draft = null; }
        };
    }

    internal void ShowNotice(string text) => Notice.Text = text;

    private void SetBusy(bool value)
    {
        busy = value;
        AddImageButton.IsEnabled = AddDirectoryButton.IsEnabled = Characters.IsEnabled = !value;
        ApplyButton.IsEnabled = DefaultButton.IsEnabled = !value;
        DeleteButton.IsEnabled = !value && draft is null && Characters.SelectedItem is Entry { Id: not null };
        ImportSettings.IsEnabled = !value;
    }

    private async Task Refresh(string? selected)
    {
        try
        {
            var entries = await Task.Run(() => library.List().Select(info =>
            {
                ImageSource? thumbnail = null;
                try
                {
                    var source = library.Load(info.Id);
                    var frame = source.ResolveProgram(PetAnimationKey.Idle, _ => true).Loop[0].Frames[0];
                    thumbnail = Thumbnail(source.ReadFrame(frame));
                }
                catch { }
                return new Entry(info.Id, info.Name + (info.Id == currentId() ? " · 当前" : ""), thumbnail);
            }).ToList(), lifetime.Token);
            if (closed) return;
            var defaultThumbnail = new BitmapImage();
            defaultThumbnail.BeginInit();
            defaultThumbnail.CacheOption = BitmapCacheOption.OnLoad;
            defaultThumbnail.DecodePixelWidth = 64;
            defaultThumbnail.UriSource = new Uri("pack://application:,,,/pet-helper;component/Assets/placeholder-a.png");
            defaultThumbnail.EndInit(); defaultThumbnail.Freeze();
            entries.Insert(0, new(null, currentId() is null ? "默认人物 · 当前" : "默认人物", defaultThumbnail));
            Characters.ItemsSource = entries;
            Characters.SelectedItem = entries.FirstOrDefault(e => e.Id == selected) ?? entries[0];
        }
        catch (OperationCanceledException) { }
        catch { if (!closed) ShowNotice("人物库暂时无法读取，请稍后重试。"); }
    }

    private static BitmapImage Thumbnail(byte[] bytes)
    {
        // Use the same bounded, signature-checked loader as playback before creating a thumbnail.
        return PetAnimationPlayer.LoadExternalBitmap(bytes, 64);
    }

    private async void Characters_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (closed || busy || Characters.SelectedItem is not Entry entry) return;
        preview?.Stop(); PreviewImage.Source = null;
        draft?.Dispose(); draft = null;
        ImportSettings.Visibility = Visibility.Collapsed;
        var generation = ++previewGeneration;
        try
        {
            var source = entry.Id is null ? null : await Task.Run(() => library.Load(entry.Id), lifetime.Token);
            if (closed || generation != previewGeneration) return;
            ShowPreview(source);
            PreviewTitle.Text = entry.Label;
            ApplyButton.Content = "使用此人物";
            DeleteButton.IsEnabled = entry.Id is not null;
            ShowNotice("GIF 按当前状态循环；缺少状态动作时使用该人物的待机动画。");
        }
        catch (OperationCanceledException) { }
        catch { if (!closed && generation == previewGeneration) ShowNotice("这个人物暂时无法预览，可移除后重新导入。"); }
    }

    internal bool PreviewTimerRunning => preview?.IsTimerRunning == true;

    internal void ShowPreview(CharacterAssetSource? source)
    {
        preview?.Stop();
        PreviewImage.Source = null;
        preview = new PetAnimationPlayer(PreviewImage, source, preview: true);
        preview.AssetFailed += (_, _) => ShowNotice("素材无法播放，请检查后重新导入。");
        AnchorX.Value = source?.Document.StatusAnchor.X ?? 0.5;
        AnchorY.Value = source?.Document.StatusAnchor.Y ?? 0.05;
        Baseline.Value = source?.Document.Baseline ?? 0.976;
        UpdateMarkers();
        ApplyPreviewState();
    }

    private void ApplyPreviewState()
    {
        if (PreviewState?.SelectedItem is StateChoice state)
            preview?.Apply(state.Key, StaticPreview.IsChecked == true);
    }

    private void PreviewState_Changed(object sender, SelectionChangedEventArgs e) => ApplyPreviewState();
    private void PreviewMotion_Changed(object sender, RoutedEventArgs e) => ApplyPreviewState();
    private void Anchor_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateMarkers();
    private void UpdateMarkers()
    {
        if (HeadMarker is null || FootMarker is null || AnchorY is null || Baseline is null) return;
        Canvas.SetLeft(HeadMarker, AnchorX.Value * 240 - 5);
        Canvas.SetTop(HeadMarker, AnchorY.Value * 240 - 5);
        FootMarker.Y1 = FootMarker.Y2 = Baseline.Value * 240;
    }

    private async void AddImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "人物图片 (*.gif;*.png)|*.gif;*.png", Multiselect = false,
            Title = "选择人物图片", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await Import(token => library.PrepareImage(dialog.FileName, token));
    }

    private async void AddDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择含 character.json 的角色目录", Multiselect = false };
        if (dialog.ShowDialog(this) == true) await Import(token => library.PrepareDirectory(dialog.FolderName, token));
    }

    private async Task Import(Func<CancellationToken, CharacterDraft> prepare)
    {
        if (busy) return;
        ++previewGeneration;
        SetBusy(true);
        importing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancelImportButton.Visibility = Visibility.Visible;
        ShowNotice("正在处理人物素材… 可以取消，当前桌宠继续运行。");
        CharacterDraft? next = null;
        try
        {
            next = await Task.Run(() => prepare(importing.Token), importing.Token);
            if (closed || importing.IsCancellationRequested) { next.Dispose(); return; }
            preview?.Stop();
            draft?.Dispose(); draft = next;
            ShowPreview(draft.Source);
            CharacterName.Text = draft.Document.Name;
            PreviewTitle.Text = "预览新人物";
            ImportSettings.Visibility = Visibility.Visible;
            ApplyButton.Content = "导入并使用";
            ShowNotice("调整名称、蓝色头顶点与橙色脚底线后导入。GIF 循环播放，极短帧至少显示 20 ms；背景原样保留。");
        }
        catch (OperationCanceledException) { next?.Dispose(); if (!closed) ShowNotice("已取消导入。"); }
        catch { next?.Dispose(); if (!closed) ShowNotice("导入失败。请检查素材格式、画布尺寸和帧数；人物库正被其他窗口使用时请稍后重试。"); }
        finally
        {
            importing.Dispose(); importing = null;
            if (closed) { draft?.Dispose(); draft = null; }
            if (!closed) { CancelImportButton.Visibility = Visibility.Collapsed; SetBusy(false); }
        }
    }

    private void CancelImport_Click(object sender, RoutedEventArgs e) => importing?.Cancel();

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            string? id;
            if (draft is not null)
            {
                var name = CharacterName.Text;
                var anchor = new PetStatusAnchor(AnchorX.Value, AnchorY.Value);
                var baseline = Baseline.Value;
                var pending = draft;
                // Stop preview and release staging frames before moving the directory.
                preview?.Stop(); PreviewImage.Source = null;
                var info = await Task.Run(() => library.Commit(pending, name, anchor, baseline));
                draft = null; id = info.Id;
            }
            else id = (Characters.SelectedItem as Entry)?.Id;
            if (closed) return;
            if (!await useCharacter(id)) { ShowNotice("切换失败，已保留当前人物。请稍后重试。"); return; }
            ImportSettings.Visibility = Visibility.Collapsed;
            ApplyButton.Content = "使用此人物";
            SetBusy(false);
            await Refresh(id);
            ShowNotice("已使用此人物，下次启动会恢复选择。");
        }
        catch { if (!closed) ShowNotice("保存失败，请检查名称或稍后重试。当前人物未改变。"); }
        finally
        {
            if (!closed) SetBusy(false);
            else { draft?.Dispose(); draft = null; }
        }
    }

    private async void Default_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            if (await useCharacter(null))
            {
                preview?.Stop(); draft?.Dispose(); draft = null;
                SetBusy(false); await Refresh(null);
                ShowNotice("已恢复默认人物。");
            }
            else ShowNotice("暂时无法恢复默认，请稍后重试。");
        }
        finally { if (!closed) SetBusy(false); }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (busy || draft is not null || Characters.SelectedItem is not Entry { Id: not null } entry) return;
        if (MessageBox.Show(this, "移除这个人物的本机导入副本？原始素材文件会保留。", "移除人物",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetBusy(true);
        try
        {
            if (currentId() == entry.Id && !await useCharacter(null)) { ShowNotice("请先恢复默认人物后再移除。"); return; }
            preview?.Stop(); PreviewImage.Source = null;
            await Task.Run(() => library.Delete(entry.Id), lifetime.Token);
            SetBusy(false); await Refresh(currentId());
            ShowNotice("已移除导入副本。");
        }
        catch { if (!closed) ShowNotice("移除失败，请稍后重试。"); }
        finally { if (!closed) SetBusy(false); }
    }
}
