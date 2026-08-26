# 桌宠双窗口 UI 重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将桌宠拆为双独立窗口：人物窗口（形象+状态气泡，双击切换对话窗口、左键拖动联动）与对话窗口（白色半透明、输入+输出合并、可调大小、Ctrl/左键拖动、窄滚动条、位置记忆）。

**Architecture:** 新 `DialogueWindow`（WPF 窗口 + WindowChrome 实现透明可调整大小）持有 `ConversationState` 与对话协议消息；`MainWindow` 移除全部对话 UI 并恢复状态气泡独立显示；`App` 双窗口生命周期与事件转发；`DialogueWindowState` 持久化位置尺寸。

**Tech Stack:** C# WPF、xunit、Node 静态布局断言、JSON Lines v5（不变）。

**Spec:** `docs/superpowers/specs/2026-08-26-dual-window-ui-design.md`

---

## 文件结构

| 文件 | 责任 |
| --- | --- |
| `pet-helper/DialogueWindowState.cs` (新建) | 对话窗口位置+尺寸持久化 |
| `pet-helper/DialogueWindow.xaml` (新建) | 白色半透明、输入+输出合并、历史覆盖层、窄滚动条样式 |
| `pet-helper/DialogueWindow.xaml.cs` (新建) | 状态机、事件（InputSubmitted/HistoryRequested/ClosedToHidden）、拖动、位置记忆 |
| `pet-helper/MainWindow.xaml` (重构) | 仅保留鲸鱼、状态气泡、右键菜单 |
| `pet-helper/MainWindow.xaml.cs` (重构) | 双击切换对话窗口、左键拖动联动、状态气泡独立显示 |
| `pet-helper/App.xaml.cs` (更新) | 双窗口创建与消息路由 |
| `pet-helper.Tests/DialogueWindowStateTests.cs` (新建) | 持久化测试 |
| `test/wpf-layout.test.mjs` (更新) | 双窗口静态断言 |

---

### Task 1: DialogueWindowState 持久化

**Files:**
- Create: `pet-helper/DialogueWindowState.cs`
- Test: `pet-helper.Tests/DialogueWindowStateTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `pet-helper.Tests/DialogueWindowStateTests.cs`：

```csharp
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class DialogueWindowStateTests
{
    [Fact]
    public void Load_returns_default_for_missing_or_malformed_json()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"dialogue-{Guid.NewGuid():N}.json");
        Assert.Equal(DialogueWindowState.Default, new DialogueWindowStateStore(missing).Load());

        var malformed = Path.Combine(Path.GetTempPath(), $"dialogue-{Guid.NewGuid():N}.json");
        File.WriteAllText(malformed, "not json");
        try
        {
            Assert.Equal(DialogueWindowState.Default, new DialogueWindowStateStore(malformed).Load());
        }
        finally
        {
            File.Delete(malformed);
        }
    }

    [Fact]
    public void Save_and_load_round_trip_valid_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dialogue-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "dialogue-window-state.json");
        try
        {
            var store = new DialogueWindowStateStore(path);
            var expected = new DialogueWindowState(320d, 180d, 280d, 360d);

            store.Save(expected);

            Assert.Equal(expected, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Normalize_replaces_out_of_range_values_with_defaults()
    {
        Assert.Equal(DialogueWindowState.Default, DialogueWindowState.Normalize(50000d, 200d, 280d, 360d));
        Assert.Equal(DialogueWindowState.Default, DialogueWindowState.Normalize(100d, 200d, 10d, 360d));
        Assert.Equal(DialogueWindowState.Default, DialogueWindowState.Normalize(100d, 200d, 280d, 5000d));
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`
Expected: FAIL（`DialogueWindowState` 不存在）。

- [ ] **Step 3: 实现**

创建 `pet-helper/DialogueWindowState.cs`：

```csharp
using System.Text.Json;

namespace PetHelper;

public sealed record DialogueWindowState(double? Left, double? Top, double Width, double Height)
{
    public const double DefaultWidth = 280d;
    public const double DefaultHeight = 380d;
    public const double MinWidth = 220d;
    public const double MinHeight = 240d;
    public const double MaxWidth = 800d;
    public const double MaxHeight = 900d;

    public static DialogueWindowState Default { get; } = new(null, null, DefaultWidth, DefaultHeight);

    public static DialogueWindowState Normalize(double? left, double? top, double? width, double? height)
    {
        var validPosition = left is { } validLeft && top is { } validTop
            && double.IsFinite(validLeft) && double.IsFinite(validTop)
            && validLeft is >= -10000d and <= 10000d
            && validTop is >= -10000d and <= 10000d;
        var validSize = width is { } validWidth && height is { } validHeight
            && double.IsFinite(validWidth) && double.IsFinite(validHeight)
            && validWidth is >= MinWidth and <= MaxWidth
            && validHeight is >= MinHeight and <= MaxHeight;

        return validPosition && validSize
            ? new DialogueWindowState(left, top, width!.Value, height!.Value)
            : Default;
    }
}

public sealed class DialogueWindowStateStore
{
    private readonly string path;

    public DialogueWindowStateStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DshPngPet",
            "dialogue-window-state.json");
    }

    public DialogueWindowState Load()
    {
        try
        {
            if (!File.Exists(path)) return DialogueWindowState.Default;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            double? left = TryReadDouble(root, "left");
            double? top = TryReadDouble(root, "top");
            var width = TryReadDouble(root, "width");
            var height = TryReadDouble(root, "height");
            return DialogueWindowState.Normalize(left, top, width, height);
        }
        catch
        {
            return DialogueWindowState.Default;
        }
    }

    public void Save(DialogueWindowState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                left = state.Left,
                top = state.Top,
                width = state.Width,
                height = state.Height,
            }));
        }
        catch
        {
            // Best effort: a failed save never blocks the pet.
        }
    }

    private static double? TryReadDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
```

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`
Expected: 全部 PASS（含新增 3 个测试）。

- [ ] **Step 5: 提交**

```bash
git add pet-helper/DialogueWindowState.cs pet-helper.Tests/DialogueWindowStateTests.cs
git commit -m "feat: persist dialogue window position and size"
```

---

### Task 2: DialogueWindow 窗口

**Files:**
- Create: `pet-helper/DialogueWindow.xaml`
- Create: `pet-helper/DialogueWindow.xaml.cs`
- Modify: `pet-helper/App.xaml.cs`
- Test: `test/wpf-layout.test.mjs`

- [ ] **Step 1: 写失败布局测试**

在 `test/wpf-layout.test.mjs` 追加：

```js
test('renders the white translucent dialogue window with merged input and output', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /Background="#F0FFFFFF"/)
  assert.match(xaml, /ResizeMode="CanResize"/)
  assert.match(xaml, /x:Name="InputTextBox"/)
  assert.match(xaml, /x:Name="ReplyTextBlock"/)
  assert.match(xaml, /HistoryButton/)
  assert.match(xaml, /x:Name="HistoryPanel"/)
})

test('styles scroll bars narrow without arrow buttons', () => {
  const xaml = readFileSync(new URL('../pet-helper/DialogueWindow.xaml', import.meta.url), 'utf8')

  assert.match(xaml, /ScrollBar/)
  assert.match(xaml, /Thumb/)
  assert.doesNotMatch(xaml, /RepeatButton/)
})

test('removes dialogue bubbles from the pet window and keeps the state bubble independent', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')

  assert.doesNotMatch(xaml, /InputBubble/)
  assert.doesNotMatch(xaml, /ReplyBubble/)
  assert.doesNotMatch(xaml, /HistoryPanel/)
  assert.match(xaml, /x:Name="StateBubble"/)
})

test('keeps the state bubble visible regardless of other windows', () => {
  const code = readFileSync(new URL('../pet-helper/MainWindow.xaml.cs', import.meta.url), 'utf8')

  assert.match(code, /StateBubble\.Visibility\s*=\s*state\.State\s*==\s*"idle"/)
  assert.doesNotMatch(code, /InputBubble\.Visibility\s*==\s*Visibility\.Visible/)
})
```

- [ ] **Step 2: 运行确认失败**

Run: `node --test --test-isolation=none test/wpf-layout.test.mjs`
Expected: 新测试 FAIL（文件不存在）。

- [ ] **Step 3: 创建 DialogueWindow.xaml**

创建 `pet-helper/DialogueWindow.xaml`：

```xml
<Window x:Class="PetHelper.DialogueWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="280" Height="380" WindowStartupLocation="Manual"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="CanResize"
        LocationChanged="DialogueWindow_LocationChanged">
  <WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight="0" ResizeBorderThickness="6" CornerRadius="12"
                  GlassFrameThickness="0" UseAeroCaptionButtons="False" />
  </WindowChrome.WindowChrome>
  <Window.Resources>
    <Style x:Key="NarrowScrollBar" TargetType="{x:Type ScrollBar}">
      <Setter Property="Width" Value="4" />
      <Setter Property="Background" Value="Transparent" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="{x:Type ScrollBar}">
            <Grid Background="Transparent">
              <Track x:Name="PART_Track" IsDirectionReversed="True">
                <Track.Thumb>
                  <Thumb Background="#66FFFFFF" />
                </Track.Thumb>
              </Track>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="NarrowScrollViewer" TargetType="{x:Type ScrollViewer}">
      <Setter Property="VerticalScrollBarVisibility" Value="Auto" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="{x:Type ScrollViewer}">
            <Grid>
              <ScrollContentPresenter />
              <ScrollBar x:Name="PART_VerticalScrollBar" Style="{StaticResource NarrowScrollBar}"
                         Orientation="Vertical" HorizontalAlignment="Right"
                         Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}" />
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>
  <Grid>
    <Border Background="#F0FFFFFF" CornerRadius="12" BorderBrush="#40000000" BorderThickness="1"
            Margin="1">
      <Grid Margin="10">
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto" />
          <RowDefinition Height="Auto" />
          <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <DockPanel Grid.Row="0" MouseLeftButtonDown="DragHandle_MouseLeftButtonDown">
          <Button DockPanel.Dock="Left" x:Name="HistoryButton" Content="历史"
                  Click="HistoryButton_Click" Padding="6,0" Margin="0,0,6,0" />
          <Button DockPanel.Dock="Right" Content="×" Click="CloseButton_Click"
                  ToolTip="关闭对话" Padding="5,0" />
          <TextBlock Text="对话" Foreground="#CC222222" FontWeight="SemiBold"
                     HorizontalAlignment="Center" VerticalAlignment="Center" />
        </DockPanel>
        <StackPanel Grid.Row="1" Margin="0,8,0,0">
          <TextBox x:Name="InputTextBox" MinHeight="36" MaxHeight="72" AcceptsReturn="True"
                   TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"
                   KeyDown="InputTextBox_KeyDown" Background="#E8FFFFFF"
                   BorderBrush="#40000000" />
          <DockPanel Margin="0,4,0,0">
            <Button DockPanel.Dock="Right" x:Name="SendButton" Content="发送"
                    Padding="10,2" Click="SendButton_Click" />
            <TextBlock x:Name="ConversationStatusLabel" Foreground="#AA000000"
                       VerticalAlignment="Center" TextWrapping="Wrap" />
          </DockPanel>
        </StackPanel>
        <Border Grid.Row="2" Margin="0,8,0,0" Background="#E8FFFFFF" CornerRadius="8"
                BorderBrush="#30000000" BorderThickness="1" Padding="8">
          <ScrollViewer x:Name="ReplyScroll" Style="{StaticResource NarrowScrollViewer}">
            <TextBlock x:Name="ReplyTextBlock" Foreground="#DD000000" TextWrapping="Wrap" />
          </ScrollViewer>
        </Border>
        <Border x:Name="HistoryPanel" Grid.RowSpan="3" Visibility="Collapsed"
                Background="#F2FFFFFF" CornerRadius="10" BorderBrush="#40000000"
                BorderThickness="1" Padding="10" Panel.ZIndex="10">
          <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto" />
              <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <DockPanel Grid.Row="0">
              <TextBlock DockPanel.Dock="Left" Text="对话历史" Foreground="#DD000000" FontWeight="SemiBold" />
              <Button DockPanel.Dock="Right" Content="×" Click="CloseHistoryButton_Click" Padding="5,0" />
              <TextBlock x:Name="HistoryStatus" Foreground="#88000000" Text="加载中…"
                         HorizontalAlignment="Right" VerticalAlignment="Center" />
            </DockPanel>
            <ScrollViewer Grid.Row="1" x:Name="HistoryScroll" Style="{StaticResource NarrowScrollViewer}"
                          Margin="0,6,0,0">
              <StackPanel x:Name="HistoryList" />
            </ScrollViewer>
          </Grid>
        </Border>
      </Grid>
    </Border>
  </Grid>
</Window>
```

- [ ] **Step 4: 创建 DialogueWindow.xaml.cs**

创建 `pet-helper/DialogueWindow.xaml.cs`：

```csharp
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PetHelper;

public partial class DialogueWindow : Window
{
    private readonly DialogueWindowStateStore stateStore = new();
    private readonly ConversationState conversationState = new(previewEnabled: false, previewMaxChars: 480);
    private long nextRequestId;
    private long historyRequestId;
    private bool restoringState = true;

    public event EventHandler<InputSubmittedEventArgs>? InputSubmitted;
    public event EventHandler<HistoryRequestedEventArgs>? HistoryRequested;
    public event EventHandler? HiddenToTray;

    public DialogueWindow()
    {
        InitializeComponent();
        RestoreState();
        restoringState = false;
    }

    public void ApplyConversationMessage(ProtocolMessage message)
    {
        conversationState.Apply(message);
        ConversationStatusLabel.Text = conversationState.StatusText;
        ReplyTextBlock.Text = conversationState.ReplyPending && conversationState.ReplyText.Length == 0
            ? "正在生成回复…"
            : conversationState.ReplyText;
        if (message is HistoryMessage)
        {
            RenderHistory(conversationState);
        }
    }

    public void CloseToHidden()
    {
        SaveState();
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    public void SaveState()
    {
        if (restoringState) return;
        stateStore.Save(new DialogueWindowState(Left, Top, Width, Height));
    }

    private void RestoreState()
    {
        var state = stateStore.Load();
        Width = state.Width;
        Height = state.Height;
        if (state.Left is { } left && state.Top is { } top)
        {
            Left = left;
            Top = top;
        }
    }

    private void SubmitInput()
    {
        var text = InputTextBox.Text.Trim();
        if (text.Length is 0 or > 2000) return;

        nextRequestId++;
        conversationState.BeginInput(nextRequestId);
        InputTextBox.Clear();
        ConversationStatusLabel.Text = "正在发送…";
        InputSubmitted?.Invoke(this, new InputSubmittedEventArgs(nextRequestId, text));
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            SubmitInput();
            e.Handled = true;
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => SubmitInput();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseToHidden();

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !IsInteractiveTarget(e.OriginalSource as DependencyObject))
        {
            DragMove();
        }
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        historyRequestId++;
        HistoryRequested?.Invoke(this, new HistoryRequestedEventArgs(historyRequestId));
        HistoryPanel.Visibility = Visibility.Visible;
        HistoryStatus.Text = "加载中…";
        HistoryStatus.Visibility = Visibility.Visible;
        HistoryScroll.Visibility = Visibility.Collapsed;
        HistoryList.Children.Clear();
    }

    private void CloseHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryPanel.Visibility = Visibility.Collapsed;
    }

    private void RenderHistory(ConversationState state)
    {
        HistoryList.Children.Clear();
        HistoryScroll.Visibility = Visibility.Collapsed;
        HistoryStatus.Visibility = Visibility.Visible;
        if (!state.HistoryAvailable)
        {
            HistoryStatus.Text = "会话不可用";
            return;
        }
        if (state.HistoryMessages.Length == 0)
        {
            HistoryStatus.Text = "暂无对话历史";
            return;
        }

        HistoryStatus.Visibility = Visibility.Collapsed;
        HistoryScroll.Visibility = Visibility.Visible;
        foreach (var item in state.HistoryMessages)
        {
            var isUser = item.Role == "user";
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 2, 0, 2),
                MaxWidth = 190,
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, isUser ? (byte)46 : (byte)64, isUser ? (byte)92 : (byte)68, isUser ? (byte)122 : (byte)84)),
            };
            bubble.Child = new TextBlock
            {
                Text = item.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.White,
            };
            HistoryList.Children.Add(bubble);
        }
    }

    private static bool IsInteractiveTarget(DependencyObject? target)
    {
        while (target is not null)
        {
            if (target is TextBox or Button or ScrollViewer) return true;
            target = VisualTreeHelper.GetParent(target);
        }
        return false;
    }

    private void DialogueWindow_LocationChanged(object? sender, EventArgs e) => SaveState();
}
```

- [ ] **Step 5: 更新 App.xaml.cs 双窗口路由**

`pet-helper/App.xaml.cs` 修改：

```csharp
        var window = new MainWindow();
        MainWindow = window;
        var dialogue = new DialogueWindow();
        window.InputSubmitted += (_, input) => WriteInput(input);
        dialogue.InputSubmitted += (_, input) => WriteInput(input);
        window.HistoryRequested += (_, request) => WriteHistoryRequest(request);
        dialogue.HistoryRequested += (_, request) => WriteHistoryRequest(request);
        window.AttachDialogueWindow(dialogue);
        var tray = new PetTrayIcon(
            () => Dispatcher.Invoke(ShowMainWindow),
            () => Dispatcher.Invoke(ExitFromTray));
        trayIcon = tray;
        window.HiddenToTray += (_, _) => tray.Show();
        dialogue.HiddenToTray += (_, _) => tray.Show();
        window.Show();
```

`ReadProtocolLoop` 中 conversation 消息改路由到 dialogue：

```csharp
                    case ConversationConfigMessage or InputStatusMessage or ReplyPreviewMessage or ClearPreviewMessage or ReplyMessage or HistoryMessage:
                        await Dispatcher.InvokeAsync(() => dialogue.ApplyConversationMessage(message));
                        continue;
```

`ExitFromTray` 关闭两个窗口前保存对话状态：

```csharp
    private void ExitFromTray()
    {
        if (dialogue is { IsVisible: true }) dialogue.SaveState();
        Shutdown();
    }
```

（`dialogue` 保存为字段。）

- [ ] **Step 6: 运行测试确认通过**

Run: `node --test --test-isolation=none test/wpf-layout.test.mjs`
Run: `dotnet build pet-helper\PetHelper.csproj --no-restore`
Expected: 布局测试 FAIL（MainWindow 断言尚未满足，Task 3 处理）；构建 PASS（DialogueWindow 存在）。

- [ ] **Step 7: 提交**

```bash
git add pet-helper/DialogueWindow.xaml pet-helper/DialogueWindow.xaml.cs pet-helper/App.xaml.cs test/wpf-layout.test.mjs
git commit -m "feat: add standalone dialogue window"
```

---

### Task 3: MainWindow 重构

**Files:**
- Modify: `pet-helper/MainWindow.xaml`
- Modify: `pet-helper/MainWindow.xaml.cs`

- [ ] **Step 1: 重构 MainWindow.xaml**

`pet-helper/MainWindow.xaml` 的 Grid 内容替换为（移除 DialogueStack/InputBubble/ReplyBubble/PreviewBubble/HistoryPanel）：

```xml
  <Grid MouseLeftButtonDown="Pet_MouseLeftButtonDown">
    <Image x:Name="PetImage" Stretch="Uniform" VerticalAlignment="Bottom" Margin="0,20,0,0" />
    <Border x:Name="StateBubble" Visibility="Collapsed" Background="#E62B4B5F"
            CornerRadius="12" Padding="12,6" HorizontalAlignment="Center"
            VerticalAlignment="Top" Margin="10,106,10,0" Panel.ZIndex="1">
      <TextBlock x:Name="StateLabel" Foreground="White" FontWeight="SemiBold" TextWrapping="Wrap" />
    </Border>
  </Grid>
```

（右键菜单保留不变。）

- [ ] **Step 2: 重构 MainWindow.xaml.cs**

删除字段/事件/方法：`InputBubbleMargin`、`StateBubbleMargin`、`HistoryPanelMargin`、`conversationState`、`nextRequestId`、`historyRequestId`、`InputSubmitted`、`HistoryRequested`、`ApplyConversationMessage`、`ShowInputBubble`、`SubmitInput`、`InputTextBox_KeyDown`、`SendButton_Click`、`CloseInputButton_Click`、`HistoryButton_Click`、`CloseHistoryButton_Click`、`RenderHistory`、`ScaleMargin`、`UpdateStateBubbleVisibility`。

保留：`ApplyDisplayState`（状态气泡按状态独立显示）、`ApplyConfig`、`ApplyState`（缩放，仅窗口尺寸与 `StateBubble.LayoutTransform`）、`CurrentState`、`SaveState`、`Pet_MouseLeftButtonDown`（双击切换 + 左键拖动联动）、右键菜单处理、`Window_LocationChanged`（联动对话窗口）。

新增字段与方法：

```csharp
    private DialogueWindow? dialogueWindow;
    private bool dragging;

    public void AttachDialogueWindow(DialogueWindow window)
    {
        dialogueWindow = window;
    }

    public void ToggleDialogueWindow()
    {
        if (dialogueWindow is null) return;
        if (dialogueWindow.IsVisible)
        {
            dialogueWindow.CloseToHidden();
        }
        else
        {
            dialogueWindow.Show();
            dialogueWindow.Activate();
            dialogueWindow.FocusInput();
        }
    }
```

`ApplyDisplayState` 改为：

```csharp
    public void ApplyDisplayState(PetDisplayState state)
    {
        lastDisplayState = state;
        StateLabel.Text = state.Label;
        StateBubble.Visibility = state.State == "idle" ? Visibility.Collapsed : Visibility.Visible;
        StateBubble.Background = state.State == "waiting"
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 142, 74, 29))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 43, 75, 95));
        animationPlayer.Apply(state.AnimationKey, reducedMotion);
    }
```

`ApplyState` 简化为（只缩放窗口与状态气泡）：

```csharp
    private void ApplyState(PetWindowState state)
    {
        Width = state.Width;
        Height = state.Height;
        StateBubble.LayoutTransform = new ScaleTransform(state.Scale, state.Scale);

        if (state.Left is { } left && state.Top is { } top)
        {
            if (!IsLoaded) WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
            return;
        }

        if (!IsLoaded)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2d;
            Top = workArea.Top + (workArea.Height - Height) / 2d;
        }
    }
```

`Pet_MouseLeftButtonDown` 改为（双击切换、单击拖动联动）：

```csharp
    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleDialogueWindow();
            return;
        }
        if (e.ClickCount == 1)
        {
            dragging = true;
            try
            {
                DragMove();
            }
            finally
            {
                dragging = false;
            }
        }
    }
```

`Window_LocationChanged` 改为（拖动时联动对话窗口）：

```csharp
    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        SaveState();
        if (dragging && dialogueWindow is { IsVisible: true })
        {
            dialogueWindow.Left = Left + Width + 8;
            dialogueWindow.Top = Top;
        }
    }
```

`pet-helper/DialogueWindow.xaml.cs` 增加 `FocusInput`：

```csharp
    public void FocusInput()
    {
        Show();
        Activate();
        InputTextBox.Focus();
    }
```

- [ ] **Step 3: 运行布局测试与 C# 测试**

Run: `node --test --test-isolation=none test/wpf-layout.test.mjs`
Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`
Expected: 全部 PASS。

- [ ] **Step 4: 提交**

```bash
git add pet-helper/MainWindow.xaml pet-helper/MainWindow.xaml.cs pet-helper/DialogueWindow.xaml.cs
git commit -m "refactor: split pet and dialogue windows with linked dragging"
```

---

### Task 4: 构建验证与发布

**Files:**
- Modify: `package.json`、`package-lock.json`（0.1.22）

- [ ] **Step 1: 全量 Node 测试**

Run: `npm run build; node --test --test-isolation=none test/*.test.mjs`
Expected: 全部 PASS（spawn 相关文件用 full-access 运行）。

- [ ] **Step 2: C# 测试与发布握手**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`（full-access）
Run: `npm run build:helper`
Run: `node --test --test-isolation=none test/packaged-helper.test.mjs test/packaging.test.mjs`（full-access）
Expected: 全部 PASS。

- [ ] **Step 3: 升版本并打包**

版本 `0.1.22`，`npm pack`，复制到 `C:\dsh-packages\`。

- [ ] **Step 4: 安装**

用户退出后：

```powershell
dsh plugin --profile web remove dsh-png-pet
dsh plugin --profile web add C:\dsh-packages\dsh-png-pet-0.1.22.tgz
```

- [ ] **Step 5: 手工验收清单**

- 双击人物 → 对话窗口出现（输入+输出同现）；点关闭 → 整个对话窗口消失。
- 拖动人物 → 对话窗口跟随（相对位置不变）；Ctrl/左键拖动对话窗口 → 单独移动。
- 对话窗口边框拖动调整大小；滚动条窄且无箭头。
- 工作状态气泡在人物窗口正常显示（不再消失）。
- 发送消息 → "正在生成回复…" → 最终回复；历史面板正常。
- 重启后对话窗口位置/尺寸恢复。
- 75% 缩放：人物窗口缩放正常，对话窗口独立。

- [ ] **Step 6: 提交**

```bash
git add package.json package-lock.json
git commit -m "chore: release 0.1.22 with dual-window UI"
```
