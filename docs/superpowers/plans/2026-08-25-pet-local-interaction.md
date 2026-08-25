# 本机桌宠交互 Implementation Plan

> **状态：✅ 实现、构建、自动回归和发布已完成。** 拖动、缩放、重置、隐藏、托盘恢复、退出和位置保存已落地；最后的视觉手工验收由用户在 Windows 桌面完成。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 让独立运行的 Windows WPF 桌宠支持拖动、缩放、右键菜单、隐藏恢复、状态记忆和正常退出。

**Architecture:** 窗口状态及 JSON 文件访问位于不依赖 WPF 的小类，xUnit 覆盖其边界和错误回退。MainWindow 只把鼠标和菜单事件映射为状态变化；App 持有托盘服务，提供隐藏后显示和退出入口。stdin/stdout 协议保持不变。

**Tech Stack:** .NET 10、C#、WPF、Windows Forms NotifyIcon、xUnit、Node.js 内置测试运行器。

---

## 文件结构

| 文件 | 责任 |
| --- | --- |
| pet-helper/PetWindowState.cs | 坐标和缩放的验证、默认值和显示尺寸。 |
| pet-helper/PetWindowStateStore.cs | 状态 JSON 的安全读写。 |
| pet-helper/PetTrayIcon.cs | 托盘图标及“显示/退出”菜单。 |
| pet-helper/MainWindow.xaml(.cs) | 右键菜单、拖动和窗口状态协调。 |
| pet-helper/App.xaml.cs | 连接窗口、托盘与现有协议。 |
| pet-helper.Tests/PetWindowStateTests.cs | 状态和存储单元测试。 |
| test/packaged-helper.test.mjs | 发布 exe 的 ready/closed 回归测试。 |

### Task 1: 窗口状态模型与读写存储 ✅ 已完成

**Files:**
- Create: pet-helper/PetWindowState.cs
- Create: pet-helper/PetWindowStateStore.cs
- Create: pet-helper.Tests/PetWindowStateTests.cs

- [ ] **Step 1: 写出会失败的状态模型和存储测试**

~~~csharp
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetWindowStateTests
{
    [Fact]
    public void Normalize_keeps_supported_scale_and_position()
    {
        var state = PetWindowState.Normalize(100d, 200d, 1.25d);
        Assert.Equal(100d, state.Left);
        Assert.Equal(200d, state.Top);
        Assert.Equal(1.25d, state.Scale);
        Assert.Equal(275d, state.Width);
        Assert.Equal(275d, state.Height);
    }

    [Theory]
    [InlineData(0.5d)]
    [InlineData(1.1d)]
    [InlineData(2d)]
    public void Normalize_replaces_unsupported_scale_with_default(double scale)
    {
        Assert.Equal(1d, PetWindowState.Normalize(100d, 200d, scale).Scale);
    }

    [Fact]
    public void Load_returns_default_for_malformed_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pet-state-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "not json");
        try
        {
            Assert.Equal(PetWindowState.Default, new PetWindowStateStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_and_load_round_trip_valid_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pet-state-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "window-state.json");
        try
        {
            var store = new PetWindowStateStore(path);
            var expected = PetWindowState.Normalize(320d, 180d, 1.5d);
            store.Save(expected);
            Assert.Equal(expected, store.Load());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
~~~

- [ ] **Step 2: 运行测试，确认因为类型不存在而失败**

Run: dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter FullyQualifiedName~PetWindowStateTests

Expected: FAIL，编译器报告缺少 PetWindowState 和 PetWindowStateStore。

- [ ] **Step 3: 实现最小状态模型和容错 JSON 存储**

创建 pet-helper/PetWindowState.cs：

~~~csharp
namespace PetHelper;

public sealed record PetWindowState(double? Left, double? Top, double Scale)
{
    public const double BaseSize = 220d;
    public static PetWindowState Default { get; } = new(null, null, 1d);
    public double Width => BaseSize * Scale;
    public double Height => BaseSize * Scale;

    public static PetWindowState Normalize(double? left, double? top, double? scale)
    {
        var normalizedScale = scale is 0.75d or 1d or 1.25d or 1.5d ? scale.Value : 1d;
        var validPosition = left is { } validLeft && top is { } validTop
            && double.IsFinite(validLeft) && double.IsFinite(validTop)
            && validLeft is >= -10000d and <= 10000d
            && validTop is >= -10000d and <= 10000d;
        return validPosition
            ? new PetWindowState(left, top, normalizedScale)
            : new PetWindowState(null, null, normalizedScale);
    }
}
~~~

创建 pet-helper/PetWindowStateStore.cs：

~~~csharp
using System.Text.Json;

namespace PetHelper;

public sealed class PetWindowStateStore
{
    private readonly string statePath;

    public PetWindowStateStore(string? statePath = null)
    {
        this.statePath = statePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DshPngPet", "window-state.json");
    }

    public PetWindowState Load()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<StoredWindowState>(File.ReadAllText(statePath));
            return saved is null ? PetWindowState.Default
                : PetWindowState.Normalize(saved.Left, saved.Top, saved.Scale);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return PetWindowState.Default;
        }
    }

    public void Save(PetWindowState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(statePath, JsonSerializer.Serialize(new StoredWindowState(state.Left, state.Top, state.Scale)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record StoredWindowState(double? Left, double? Top, double? Scale);
}
~~~

- [ ] **Step 4: 运行针对性测试，确认通过**

Run: dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter FullyQualifiedName~PetWindowStateTests

Expected: PASS，4 个测试全部通过。

- [ ] **Step 5: 提交状态层**

~~~powershell
git add pet-helper/PetWindowState.cs pet-helper/PetWindowStateStore.cs pet-helper.Tests/PetWindowStateTests.cs
git commit -m "feat: persist pet window state"
~~~

### Task 2: WPF 鼠标与右键菜单交互 ✅ 已完成

**Files:**
- Modify: pet-helper/MainWindow.xaml
- Modify: pet-helper/MainWindow.xaml.cs
- Modify: pet-helper.Tests/PetWindowStateTests.cs

- [ ] **Step 1: 为重置默认状态补充测试**

在 PetWindowStateTests.cs 添加：

~~~csharp
[Fact]
public void Default_has_centered_position_and_100_percent_scale()
{
    Assert.Null(PetWindowState.Default.Left);
    Assert.Null(PetWindowState.Default.Top);
    Assert.Equal(1d, PetWindowState.Default.Scale);
    Assert.Equal(220d, PetWindowState.Default.Width);
}
~~~

- [ ] **Step 2: 运行测试，确认默认约定**

Run: dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter FullyQualifiedName~PetWindowStateTests

Expected: PASS；Task 1 已定义 Default，因此这是新增约定的回归确认。

- [ ] **Step 3: 将右键菜单和左键事件声明到 XAML**

将 pet-helper/MainWindow.xaml 完整替换为：

~~~xml
<Window x:Class="PetHelper.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="220" Height="220" WindowStartupLocation="CenterScreen"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        LocationChanged="Window_LocationChanged">
  <Window.ContextMenu>
    <ContextMenu>
      <MenuItem Header="隐藏" Click="HideMenuItem_Click" />
      <MenuItem Header="缩放">
        <MenuItem Header="75%" Tag="0.75" Click="ScaleMenuItem_Click" />
        <MenuItem Header="100%" Tag="1" Click="ScaleMenuItem_Click" />
        <MenuItem Header="125%" Tag="1.25" Click="ScaleMenuItem_Click" />
        <MenuItem Header="150%" Tag="1.5" Click="ScaleMenuItem_Click" />
      </MenuItem>
      <Separator />
      <MenuItem Header="重置大小与位置" Click="ResetMenuItem_Click" />
      <MenuItem Header="关闭桌宠" Click="CloseMenuItem_Click" />
    </ContextMenu>
  </Window.ContextMenu>
  <Grid MouseLeftButtonDown="Pet_MouseLeftButtonDown">
    <Image Source="Assets/placeholder-a.png" Stretch="Uniform" />
  </Grid>
</Window>
~~~

- [ ] **Step 4: 实现窗口状态恢复、拖动、菜单和保存**

将 pet-helper/MainWindow.xaml.cs 完整替换为：

~~~csharp
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetHelper;

public partial class MainWindow : Window
{
    private readonly PetWindowStateStore stateStore = new();
    private bool restoringState = true;

    public event EventHandler? HiddenToTray;

    public MainWindow()
    {
        InitializeComponent();
        RestoreState();
        restoringState = false;
    }

    private void RestoreState() => ApplyState(stateStore.Load());

    private void ApplyState(PetWindowState state)
    {
        Width = state.Width;
        Height = state.Height;
        if (state.Left is { } left && state.Top is { } top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (IsLoaded)
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Left + (workArea.Width - Width) / 2d;
                Top = workArea.Top + (workArea.Height - Height) / 2d;
            }
        }
    }

    private PetWindowState CurrentState() =>
        PetWindowState.Normalize(Left, Top, Width / PetWindowState.BaseSize);

    private void SaveState()
    {
        if (!restoringState) stateStore.Save(CurrentState());
    }

    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Button == MouseButton.Left) DragMove();
    }

    private void Window_LocationChanged(object? sender, EventArgs e) => SaveState();

    private void ScaleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string scaleText }
            || !double.TryParse(scaleText, CultureInfo.InvariantCulture, out var scale)) return;
        ApplyState(PetWindowState.Normalize(Left, Top, scale));
        SaveState();
    }

    private void ResetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyState(PetWindowState.Default);
        SaveState();
    }

    private void HideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SaveState();
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) => Close();
}
~~~

- [ ] **Step 5: 运行完整 C# 单元测试**

Run: dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore

Expected: PASS，既有协议测试和新增状态测试全部通过。

- [ ] **Step 6: 提交窗口交互**

~~~powershell
git add pet-helper/MainWindow.xaml pet-helper/MainWindow.xaml.cs pet-helper.Tests/PetWindowStateTests.cs
git commit -m "feat: add pet window interactions"
~~~

### Task 3: 托盘恢复与退出入口 ✅ 已完成

**Files:**
- Create: pet-helper/PetTrayIcon.cs
- Modify: pet-helper/PetHelper.csproj
- Modify: pet-helper/App.xaml.cs

- [ ] **Step 1: 添加 Windows Forms 编译支持，确认当前项目仍能构建**

在 PetHelper.csproj 的属性组添加：

~~~xml
<UseWindowsForms>true</UseWindowsForms>
~~~

Run: dotnet build pet-helper\PetHelper.csproj --no-restore

Expected: PASS；此时尚没有托盘类，不会改变运行时行为。

- [ ] **Step 2: 实现仅含“显示”和“退出”的可释放托盘图标**

创建 pet-helper/PetTrayIcon.cs：

~~~csharp
using System.Drawing;
using System.Windows.Forms;

namespace PetHelper;

public sealed class PetTrayIcon : IDisposable
{
    private readonly NotifyIcon notifyIcon;

    public PetTrayIcon(Action showPet, Action exitPet)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示桌宠", null, (_, _) => showPet());
        menu.Items.Add("退出桌宠", null, (_, _) => exitPet());
        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "DSH PNG 桌宠",
            ContextMenuStrip = menu,
            Visible = false,
        };
        notifyIcon.DoubleClick += (_, _) => showPet();
    }

    public void Show() => notifyIcon.Visible = true;
    public void Dispose() => notifyIcon.Dispose();
}
~~~

- [ ] **Step 3: 将托盘生命周期接到 WPF App**

在 pet-helper/App.xaml.cs 中新增字段和方法：

~~~csharp
private PetTrayIcon? trayIcon;

private void ShowMainWindow()
{
    if (MainWindow is null) return;
    MainWindow.Show();
    MainWindow.Activate();
}

private void ExitFromTray() => Shutdown();
~~~

将 OnStartup 内的窗口创建段替换为：

~~~csharp
var window = new MainWindow();
MainWindow = window;
trayIcon = new PetTrayIcon(
    () => Dispatcher.Invoke(ShowMainWindow),
    () => Dispatcher.Invoke(ExitFromTray));
window.HiddenToTray += (_, _) => trayIcon.Show();
window.Show();
~~~

并在 OnExit 的 base.OnExit(e); 前加入：

~~~csharp
trayIcon?.Dispose();
~~~

- [ ] **Step 4: 构建并运行完整 C# 测试集**

Run: dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore

Expected: PASS，且无编译警告。

- [ ] **Step 5: 提交托盘支持**

~~~powershell
git add pet-helper/PetHelper.csproj pet-helper/PetTrayIcon.cs pet-helper/App.xaml.cs
git commit -m "feat: restore hidden pet from tray"
~~~

### Task 4: 发布回归、文档与手工验收 🟡 自动部分已完成

**Files:**
- Modify: test/packaged-helper.test.mjs
- Modify: README.md
- Generated (ignored): runtime/bin/win32-x64/pet-helper.exe

- [ ] **Step 1: 先把发布 Helper 的关闭握手断言写成失败测试**

将测试名称改为 published Helper completes ready and shutdown handshakes。收到 ready 后写入：

~~~js
child.stdin.write('{"version":1,"kind":"shutdown"}\n')
~~~

将 readline 处理改为等待 closed；测试结束时使用 await once(child, 'exit') 并断言退出码为 0，而不是在 finally 中杀死进程：

~~~js
if (line === '{"version":1,"kind":"closed"}') {
  clearTimeout(timeout)
  resolve()
}
~~~

- [ ] **Step 2: 运行未重建的发布测试，确认变更后的断言有覆盖**

Run: npm run test:package

Expected: PASS 或 FAIL 都必须记录；若当前发布 exe 已实现 close 握手应 PASS，若未实现则 FAIL。无论结果，继续下一步以重建包含 UI 代码的 exe。

- [ ] **Step 3: 发布最新 Helper 并验证协议回归**

Run: npm run build:helper

Expected: PASS，生成 runtime/bin/win32-x64/pet-helper.exe。

Run: npm run test:package

Expected: PASS，发布 exe 先发出 ready，收到 stdin 的 shutdown 后发出 closed 并退出。

- [ ] **Step 4: 更新运行说明**

在 README.md 的开发环境段落后新增：

~~~markdown
## 本机桌宠交互

直接运行 runtime/bin/win32-x64/pet-helper.exe 后：

- 按住角色左键拖动可移动位置；位置会在下次启动时恢复。
- 右键可选择 75%、100%、125% 或 150% 缩放，也可重置大小和位置。
- 右键“隐藏”后，双击通知区域的 DSH PNG 桌宠 图标或选择“显示桌宠”可恢复。
- 右键“关闭桌宠”或托盘“退出桌宠”会彻底结束进程。
~~~

- [ ] **Step 5: 执行项目全量自动验证**

Run: npm test

Expected: PASS，TypeScript 协议和包布局测试全部通过。

Run: dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore

Expected: PASS，协议、窗口状态及存储测试全部通过。

Run: npm run build:helper; npm run test:package

Expected: 两个命令均 PASS，且发布 Helper 的完整 stdin/stdout 生命周期通过。

- [ ] **Step 6: 手工验收发布 exe**

运行 runtime/bin/win32-x64/pet-helper.exe，逐项确认：左键拖动后重启仍在相同位置；右键四个缩放档均改变窗口大小；重置恢复 220 × 220 并居中；隐藏后能从托盘双击或菜单恢复；右键关闭和托盘退出都能结束进程且不留下桌宠窗口。

- [ ] **Step 7: 提交发布回归与使用说明**

~~~powershell
git add test/packaged-helper.test.mjs README.md
git commit -m "test: verify packaged pet shutdown"
~~~
