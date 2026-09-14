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
    private readonly Func<string, string, bool> confirm;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? importing;
    private CharacterDraft? draft;
    private CharacterActionDraft? actionDraft;
    private bool actionReplacesPrimary;
    private CharacterAssetSource? previewSource;
    private string? previewCharacterId;
    private string? actionSourcePath;
    private PetAnimationPlayer? preview;
    private bool busy;
    private bool closed;
    private bool refreshingActionOptions;
    private int previewGeneration;

    private sealed record Entry(string? Id, string Label, ImageSource? Thumbnail);
    // One entry per state key of the stored format; the two selectors label them differently.
    private static readonly (string Key, PetAnimationKey AnimationKey, string Name)[] States =
    [
        ("idle", PetAnimationKey.Idle, "待机"), ("thinking", PetAnimationKey.Thinking, "思考"),
        ("working", PetAnimationKey.Working, "工作"), ("thinking-working", PetAnimationKey.ThinkingWorking, "思考与工作"),
        ("responding", PetAnimationKey.Responding, "回复"), ("waiting", PetAnimationKey.Waiting, "等待操作"),
        ("question", PetAnimationKey.Question, "提问"), ("success", PetAnimationKey.Success, "成功"),
        ("error", PetAnimationKey.Error, "错误"), ("disconnected", PetAnimationKey.Disconnected, "未连接"),
    ];
    private sealed record StateChoice(string KeyName, PetAnimationKey Key, string Name) { public override string ToString() => Name; }
    /// <summary>
    /// A state choice of the add-action selector, which spells out what the character already stores
    /// there, e.g. 「待机（主动作 + 1 附加）」.  The plain state name always stays in the text.
    /// </summary>
    private sealed record ActionStateChoice(string KeyName, PetAnimationKey Key, string Name, string Label)
    {
        public override string ToString() => Label;
    }
    /// <summary>One previewable action of the selected state, labelled with the role it plays there.</summary>
    private sealed record ActionChoice(PetActionChoice Choice, int Index)
    {
        public override string ToString() =>
            Choice.IsExtra ? $"{Choice.Label} · 附加" : $"{Choice.Label} · 主动作";
    }

    internal CharacterWindow(CharacterLibrary library, Func<string?, Task<bool>> useCharacter, Func<string?> currentId,
        Func<string, string, bool>? confirm = null)
    {
        InitializeComponent();
        this.library = library; this.useCharacter = useCharacter; this.currentId = currentId;
        // Removing or replacing stored material drops it for good, so every such step asks first.
        // The question itself is injectable, which keeps the answer out of a modal dialog in tests.
        this.confirm = confirm ?? ((text, caption) => MessageBox.Show(this, text, caption,
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        var states = States.Select(state => new StateChoice(state.Key, state.AnimationKey, state.Name)).ToArray();
        PreviewState.ItemsSource = states;
        PreviewState.SelectedIndex = 0;
        // The add-action selector starts from the plain names and gains its counts with a character.
        ActionState.ItemsSource = States
            .Select(state => new ActionStateChoice(state.Key, state.AnimationKey, state.Name, state.Name)).ToArray();
        ActionState.SelectedIndex = 0;
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
            // Staging is only released while nothing is being written into it.
            if (!busy) { draft?.Dispose(); draft = null; DiscardActionDraft(); }
        };
    }

    internal void ShowNotice(string text) => Notice.Text = text;

    internal void ShowOrRestore()
    {
        if (WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(this);
        Show();
        Activate();
    }

    /// <summary>
    /// Releases the staged action together with the facts a save reads from it, so a draft and the
    /// role it was staged with never disagree.
    /// </summary>
    private void DiscardActionDraft()
    {
        actionDraft?.Dispose();
        actionDraft = null;
        actionReplacesPrimary = false;
    }

    /// <summary>
    /// Adding an action needs a stored character to add it to: not the built-in one, which the window
    /// previews with a null source, and not an import that has not been committed yet.
    /// </summary>
    private bool CanAddAction => !busy && previewCharacterId is not null && draft is null;

    private void SetBusy(bool value)
    {
        busy = value;
        AddImageButton.IsEnabled = AddDirectoryButton.IsEnabled = Characters.IsEnabled = !value;
        AddActionButton.IsEnabled = CanAddAction;
        ApplyButton.IsEnabled = DefaultButton.IsEnabled = !value;
        DeleteButton.IsEnabled = !value && draft is null && Characters.SelectedItem is Entry { Id: not null };
        ImportSettings.IsEnabled = !value;
        ActionSettings.IsEnabled = !value;
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
        // A prepared action belongs to the character it was prepared for, so selecting another one
        // leaves the action mode.
        DiscardActionDraft();
        actionSourcePath = null;
        ImportSettings.Visibility = Visibility.Collapsed;
        ActionSettings.Visibility = Visibility.Collapsed;
        var generation = ++previewGeneration;
        try
        {
            var source = entry.Id is null ? null : await Task.Run(() => library.Load(entry.Id), lifetime.Token);
            if (closed || generation != previewGeneration) return;
            ShowPreview(source);
            PreviewTitle.Text = entry.Label;
            ApplyButton.Content = "使用此人物";
            DeleteButton.IsEnabled = entry.Id is not null;
            ShowNotice(entry.Id is null
                ? "内置人物不支持添加动作。GIF 按当前状态循环；缺少状态动作时使用该人物的待机动画。"
                : "GIF 按当前状态循环；缺少状态动作时使用该人物的待机动画。");
        }
        catch (OperationCanceledException) { }
        catch { if (!closed && generation == previewGeneration) ShowNotice("这个人物暂时无法预览，可移除后重新导入。"); }
    }

    internal bool PreviewTimerRunning => preview?.IsTimerRunning == true;

    internal void ShowPreview(CharacterAssetSource? source)
    {
        preview?.Stop();
        PreviewImage.Source = null;
        previewSource = source;
        previewCharacterId = source?.Id;
        AddActionButton.IsEnabled = CanAddAction;
        preview = new PetAnimationPlayer(PreviewImage, source, preview: true);
        preview.AssetFailed += (_, _) => ShowNotice("素材无法播放，请检查后重新导入。");
        AnchorX.Value = source?.Document.StatusAnchor.X ?? 0.5;
        AnchorY.Value = source?.Document.StatusAnchor.Y ?? 0.05;
        Baseline.Value = source?.Document.Baseline ?? 0.976;
        UpdateMarkers();
        // The add-action panel describes the character that is shown: its state names carry the counts
        // of that character, and its roles and extras follow the state the selector holds.
        RefreshActionOptions();
        RebuildActionChoices();
    }

    /// <summary>Lists the actions of the selected state; the primary comes first, extras follow.</summary>
    private void RebuildActionChoices()
    {
        if (closed || PreviewState?.SelectedItem is not StateChoice state || preview is null) return;
        var catalog = preview.ActionCatalog(state.Key);
        PreviewAction.ItemsSource = catalog.Select((choice, index) => new ActionChoice(choice, index)).ToArray();
        // A state with a single action has nothing to choose between, so the dropdown stays inert.
        PreviewAction.IsEnabled = catalog.Count > 1;
        PreviewAction.SelectedIndex = catalog.Count > 0 ? 0 : -1;
        ApplyPreviewAction();
    }

    private void ApplyPreviewAction()
    {
        if (closed || preview is null || PreviewState?.SelectedItem is not StateChoice state) return;
        if (PreviewAction.SelectedItem is not ActionChoice choice) return;
        preview.PreviewAction(state.Key, choice.Index, StaticPreview.IsChecked == true);
    }

    private void PreviewState_Changed(object sender, SelectionChangedEventArgs e) => RebuildActionChoices();
    private void PreviewAction_Changed(object sender, SelectionChangedEventArgs e) => ApplyPreviewAction();
    private void PreviewMotion_Changed(object sender, RoutedEventArgs e) => ApplyPreviewAction();
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
            // The import owns the preview now, so a prepared action is released with its panel.
            DiscardActionDraft();
            actionSourcePath = null;
            ActionSettings.Visibility = Visibility.Collapsed;
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

    private void AddAction_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (previewSource is null || previewCharacterId is null)
        {
            ShowNotice("内置人物不支持添加动作。请先在左侧选择已导入的人物。");
            ActionSettings.Visibility = Visibility.Collapsed;
            return;
        }
        actionSourcePath = null;
        DiscardActionDraft();
        // The two modes write the same preview, so start the action flow from a clean slate.
        draft?.Dispose();
        draft = null;
        ImportSettings.Visibility = Visibility.Collapsed;
        ActionSettings.Visibility = Visibility.Visible;
        ApplyButton.Content = "保存到人物";
        RefreshActionOptions();
        ShowNotice("选择目标状态与动作角色，再点「选择素材…」。附加动作会在冷却后被完整插播一遍，然后回到主动作。");
    }

    private void ActionSettings_Changed(object sender, SelectionChangedEventArgs e) => RefreshActionOptions();

    /// <summary>
    /// Names what the selected character stores in every state, and lists the roles the chosen state
    /// still accepts plus the extras it already stores.
    /// </summary>
    private void RefreshActionOptions()
    {
        // Assigning the selectors below raises SelectionChanged again, which must not re-enter here.
        if (refreshingActionOptions || ActionState.SelectedItem is not ActionStateChoice state) return;
        refreshingActionOptions = true;
        try
        {
            // The state selector names what each state currently has, so its labels follow the
            // character the panel works on rather than staying at whatever was shown before.
            var document = previewSource?.Document;
            var labels = States.Select(entry => DescribeState(entry.Name, entry.Key, document)).ToArray();
            if (!labels.SequenceEqual(ActionState.Items.Cast<ActionStateChoice>().Select(choice => choice.Label)))
            {
                ActionState.ItemsSource = States
                    .Select((entry, index) => new ActionStateChoice(entry.Key, entry.AnimationKey, entry.Name, labels[index]))
                    .ToArray();
                // Rebuilding the list drops the selection, so the state the user chose is put back.
                ActionState.SelectedItem = ActionState.Items.Cast<ActionStateChoice>()
                    .FirstOrDefault(choice => choice.KeyName == state.KeyName);
            }
            // The built-in character stores no states, so there are no roles or extras to describe.
            if (document is null) return;
            var hasState = document.Actions.TryGetValue(state.KeyName, out var stored);
            var primaryFrames = hasState ? stored!.Primary.Frames.Length : 0;
            var extraCount = hasState ? stored!.Extras.Length : 0;
            var roles = new List<string> { primaryFrames == 0 ? "主动作" : "替换主动作" };
            // An extra is interleaved into the primary it returns to, so a state without a primary can
            // only be given one.
            if (primaryFrames > 0 && extraCount < CharacterManifest.MaximumExtrasPerState) roles.Add("附加动作");
            if (!roles.SequenceEqual(ActionRole.Items.Cast<string>()))
            {
                ActionRole.ItemsSource = roles;
                ActionRole.SelectedIndex = primaryFrames == 0 ? 0 : Math.Min(1, roles.Count - 1);
            }
            var names = hasState ? stored!.Extras.Select(extra => extra.Name).ToArray() : [];
            if (!names.SequenceEqual(ActionExtras.Items.Cast<string>())) ActionExtras.ItemsSource = names;
            RemoveActionButton.IsEnabled = extraCount > 0;
            ActionName.IsEnabled = (ActionRole.SelectedItem as string) == "附加动作";
        }
        finally { refreshingActionOptions = false; }
    }

    /// <summary>
    /// Spells out what a state already stores, e.g. 「待机（主动作 + 1 附加）」 or 「思考（无主动作）」;
    /// the plain state name always stays in the text.  Without a stored character there is nothing to
    /// count, so the name stays plain.
    /// </summary>
    private static string DescribeState(string name, string key, StoredCharacter? document)
    {
        if (document is null) return name;
        var hasState = document.Actions.TryGetValue(key, out var state);
        var description = hasState && state!.Primary.Frames.Length > 0 ? "主动作" : "无主动作";
        var extras = hasState ? state!.Extras.Length : 0;
        if (extras > 0) description += $" + {extras} 附加";
        return $"{name}（{description}）";
    }

    /// <summary>The Chinese name of a state key, which is what the user reads in a confirmation.</summary>
    private static string StateName(string key) =>
        States.FirstOrDefault(state => state.Key == key).Name ?? "该状态";

    /// <summary>
    /// True when the picked material is a GIF that decodes to a single frame within the importer's
    /// canvas limits.  One frame carries no display duration of its own and this flow declares none
    /// for GIFs, so such material can never become an extra; the window names that reason instead of
    /// letting the library report it as unreadable material.  The chosen path is only borrowed here.
    /// </summary>
    private static bool IsUnstorableSingleFrameGif(string path)
    {
        try
        {
            var bytes = CharacterFiles.Read(path, 20 * 1024 * 1024);
            if (!GifFrameImporter.IsGif(bytes) || bytes.Length < 10) return false;
            // The logical screen size is judged before frames are counted, as the importer does.
            var width = bytes[6] | bytes[7] << 8;
            var height = bytes[8] | bytes[9] << 8;
            if (width is < 1 or > 2048 || height is < 1 or > 2048) return false;
            using var input = new MemoryStream(bytes, writable: false);
            var decoder = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
            return decoder.Frames.Count == 1;
        }
        catch { return false; }
    }

    private async void PickActionSource_Click(object sender, RoutedEventArgs e)
    {
        if (busy || previewSource is null || previewCharacterId is null) return;
        var dialog = new OpenFileDialog
        {
            Filter = "动作素材 (*.gif;*.png)|*.gif;*.png", Multiselect = false,
            Title = "选择动作素材", CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        await ChooseActionSource(dialog.FileName);
    }

    /// <summary>Stages the material the user picked for the state and role the panel shows.</summary>
    internal async Task ChooseActionSource(string path)
    {
        actionSourcePath = path;
        await PrepareActionDraft();
    }

    /// <summary>Decodes the chosen material into staging and previews it against the stored character.</summary>
    private async Task PrepareActionDraft()
    {
        if (busy || previewCharacterId is null || actionSourcePath is null || ActionState.SelectedItem is not ActionStateChoice state) return;
        var role = ActionRole.SelectedItem as string;
        var asPrimary = role is "主动作" or "替换主动作";
        // The role and the name are read before the busy state disables the panel that holds them,
        // which would otherwise make ActionName.IsEnabled report false.
        var previewName = ActionName.IsEnabled && ActionName.Text.Trim().Length > 0 ? ActionName.Text : "预览";
        ++previewGeneration;
        SetBusy(true);
        importing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancelImportButton.Visibility = Visibility.Visible;
        ShowNotice("正在处理动作素材… 可以取消，当前桌宠继续运行。");
        CharacterActionDraft? next = null;
        try
        {
            next = await Task.Run(() =>
            {
                // A single static GIF cannot become an extra and this flow declares no duration for
                // GIFs, so it is refused here with its own reason rather than staged and reported as
                // unreadable material once the library rejects it.
                if (!asPrimary && IsUnstorableSingleFrameGif(actionSourcePath)) return null;
                return library.PrepareAction(previewCharacterId, actionSourcePath, state.KeyName, asPrimary, importing.Token);
            }, importing.Token);
            if (next is null)
            {
                if (!closed) ShowNotice("单个静态 GIF 没有自身时长，不能作为附加动作：请改用多帧 GIF，或改用 PNG 素材。");
                return;
            }
            if (closed || importing.IsCancellationRequested) { next.Dispose(); return; }
            // The staged source is built before the draft is published, so a rejected name or an
            // unreadable library cannot leave the window holding a draft that was already released.
            var stagedSource = library.PreviewStagedAction(next, previewName);
            DiscardActionDraft();
            actionDraft = next;
            // Only the role selector that offered 替换主动作 can stage a replacement, and the draft
            // keeps that fact: the selector can move before the user saves.
            actionReplacesPrimary = role == "替换主动作";
            preview?.Stop();
            PreviewImage.Source = null;
            previewSource = stagedSource;
            previewCharacterId = next.CharacterId;
            preview = new PetAnimationPlayer(PreviewImage, previewSource, preview: true);
            // Both selectors move to the state the action is being added to, so the dropdown lists the
            // new action and selecting it plays it.
            PreviewState.SelectedItem = PreviewState.Items.Cast<StateChoice>()
                .FirstOrDefault(choice => choice.KeyName == state.KeyName);
            RebuildActionChoices();
            // The staged action is the first catalog entry for a primary and the last one for an extra.
            var staged = asPrimary ? 0 : PreviewAction.Items.Count - 1;
            if (staged >= 0 && staged < PreviewAction.Items.Count) PreviewAction.SelectedIndex = staged;
            PreviewTitle.Text = asPrimary ? "预览新主动作" : "预览新附加动作";
            ShowNotice("确认动作后点「保存到人物」。素材会按该人物现有画布等比缩放、底部对齐，不会放大。");
        }
        catch (OperationCanceledException) { next?.Dispose(); if (!closed) ShowNotice("已取消。"); }
        catch { next?.Dispose(); if (!closed) ShowNotice("动作素材无法使用，请检查格式、画布尺寸与帧数。"); }
        finally
        {
            importing.Dispose(); importing = null;
            if (closed) DiscardActionDraft();
            if (!closed) { CancelImportButton.Visibility = Visibility.Collapsed; SetBusy(false); }
        }
    }

    private async void RemoveAction_Click(object sender, RoutedEventArgs e)
    {
        if (busy || previewCharacterId is null || ActionState.SelectedItem is not ActionStateChoice state) return;
        if (ActionExtras.SelectedItem is not string name) return;
        if (!confirm($"移除附加动作「{name}」？", "移除动作")) return;
        SetBusy(true);
        try
        {
            // Removing renumbers the folders, so a prepared action and the preview of it are both
            // stale as soon as the removal is written.
            DiscardActionDraft();
            preview?.Stop(); PreviewImage.Source = null;
            await Task.Run(() => library.RemoveExtra(previewCharacterId, state.KeyName, name), lifetime.Token);
            SetBusy(false);
            await ReloadCharacter(previewCharacterId);
            ShowNotice("已移除附加动作。");
        }
        catch { if (!closed) ShowNotice("移除失败，请稍后重试。"); }
        finally { if (!closed) SetBusy(false); }
    }

    /// <summary>
    /// Shows the stored character again after its directory was rebuilt.  The running pet is rebuilt
    /// from it as well, even when this window is already closed: the commit moved the frames the live
    /// player reads, and a player that loses them falls back to the default character.  The result is
    /// reported so a caller can say whether the running pet really took the rebuilt character.
    /// </summary>
    private async Task<bool> ReloadCharacter(string id)
    {
        var source = await Task.Run(() => library.Load(id));
        if (!closed)
        {
            // ShowPreview also refreshes the add-action panel for the reloaded character.
            ShowPreview(source);
            PreviewTitle.Text = source.Document.Name;
        }
        return await useCharacter(id);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        // The action role is read before the busy state disables the panel that holds the selectors.
        var extraName = actionDraft is not null && ActionName.IsEnabled ? ActionName.Text : null;
        // The draft, not the role selector that can move after the material was staged, decides
        // whether this save stores an extra - and an extra is stored under the name in the box.
        if (actionDraft is { AsPrimary: false } && ActionName.Text.Trim().Length == 0)
        {
            ShowNotice("附加动作必须有名称：请填写 1–40 个字符的名称后再保存。");
            return;
        }
        // Replacing a primary drops that state's stored frames for good, so it is confirmed first.
        if (actionDraft is { AsPrimary: true } && actionReplacesPrimary &&
            !confirm($"替换「{StateName(actionDraft.StateKey)}」的主动作？该状态原来的主动作帧会被丢弃，无法恢复。", "替换主动作"))
            return;
        var actionAnchor = new PetStatusAnchor(AnchorX.Value, AnchorY.Value);
        var actionBaseline = Baseline.Value;
        SetBusy(true);
        try
        {
            string? id;
            if (actionDraft is not null)
            {
                var pending = actionDraft;
                // Stop the preview and release its staged frames before the commit moves them.
                preview?.Stop(); PreviewImage.Source = null;
                var info = await Task.Run(() => library.CommitAction(pending, extraName, actionAnchor, actionBaseline));
                DiscardActionDraft();
                pending.Dispose();
                ImportSettings.Visibility = Visibility.Collapsed;
                ActionSettings.Visibility = Visibility.Collapsed;
                ApplyButton.Content = "使用此人物";
                SetBusy(false);
                // The action is stored by now, so a running pet that cannot take the rebuilt frames
                // is reported as its own outcome instead of as a successful reload.
                ShowNotice(await ReloadCharacter(info.Id)
                    ? "动作已保存，运行中的桌宠已重新加载这个人物。"
                    : "动作已保存到人物库；运行中的桌宠无法重新加载，请重新选择这个人物。");
                return;
            }
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
            else { draft?.Dispose(); draft = null; DiscardActionDraft(); }
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
        if (!confirm("移除这个人物的本机导入副本？原始素材文件会保留。", "移除人物")) return;
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
