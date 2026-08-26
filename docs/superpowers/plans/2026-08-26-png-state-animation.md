# PNG 状态动作 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 WPF Helper 为八种已验证的 DSH 展示情形选择本地 PNG 动作，并在状态持续期间安全循环其帧；未提供的动作素材回退到默认待机。

**Architecture:** Host/Helper JSON Lines 协议保持 v4 不变。Helper 在已验证的状态/固定标签上派生八个 `PetAnimationKey`；嵌入 JSON 清单定义受控帧和回退；纯播放模型决定帧，WPF `DispatcherTimer` 仅负责呈现。

**Tech Stack:** .NET 10、WPF、xUnit、Node.js 22/node:test、PowerShell 单文件发布。

---

## 文件结构

- `pet-helper/PetAnimationKey.cs` — 八种本地动作键。
- `pet-helper/PetDisplayState.cs` — 安全展示状态到动作键的纯映射。
- `pet-helper/Assets/pet-animations.json` — 嵌入式动作清单，初始仅引用默认待机 PNG。
- `pet-helper/PetAnimationManifest.cs` — 清单解析、标识符校验与回退解析。
- `pet-helper/PetAnimationPlayback.cs` — 无 WPF 依赖的帧选择规则。
- `pet-helper/PetAnimationPlayer.cs` — 受控资源加载和 WPF 计时器。
- `pet-helper/MainWindow.xaml(.cs)` — 角色图像绑定和状态/配置接线。
- `pet-helper.Tests/*Animation*Tests.cs` — 清单和播放模型测试。
- `test/wpf-layout.test.mjs`、`test/packaging.test.mjs` — 结构与打包输入回归。

## 动作清单初始内容

创建 `pet-helper/Assets/pet-animations.json`。用户以后只需把透明 PNG 添加至 `pet-helper/Assets/Animations/<动作键>/` 并在此清单按帧序列出；不修改协议或 TypeScript。

```json
{
  "idle": { "frames": ["placeholder-a.png"], "intervalMs": 1000 },
  "thinking": { "frames": [], "intervalMs": 120, "fallback": "idle" },
  "working": { "frames": [], "intervalMs": 100, "fallback": "idle" },
  "thinking-working": { "frames": [], "intervalMs": 100, "fallback": "working" },
  "waiting": { "frames": [], "intervalMs": 160, "fallback": "idle" },
  "success": { "frames": [], "intervalMs": 120, "fallback": "idle" },
  "error": { "frames": [], "intervalMs": 120, "fallback": "idle" },
  "disconnected": { "frames": [], "intervalMs": 350, "fallback": "idle" }
}
```

帧标识符必须是相对于 `Assets/` 的正斜杠 PNG 名称（如 `Animations/working/001.png`），不得包含 `..`、反斜杠、绝对路径、空白段或其他扩展名。加载器只将已验证标识符拼到固定的 `pack://application:,,,/Assets/` 前缀；Host 无法指定资源。

### Task 1: 测试先行定义八种状态到动作键的映射

**Files:**
- Create: `pet-helper/PetAnimationKey.cs`
- Modify: `pet-helper/PetDisplayState.cs`
- Modify: `pet-helper.Tests/PetDisplayStateTests.cs`

- [ ] **Step 1: 写失败的参数化映射测试。**

在 `PetDisplayStateTests.cs` 添加：

```csharp
[Theory]
[InlineData("idle", "", PetAnimationKey.Idle)]
[InlineData("active", "思考中…", PetAnimationKey.Thinking)]
[InlineData("active", "工作中…", PetAnimationKey.Working)]
[InlineData("active", "思考中/工作中", PetAnimationKey.ThinkingWorking)]
[InlineData("waiting", "等待你的操作", PetAnimationKey.Waiting)]
[InlineData("success", "已完成", PetAnimationKey.Success)]
[InlineData("error", "发生错误", PetAnimationKey.Error)]
[InlineData("disconnected", "未连接", PetAnimationKey.Disconnected)]
public void Maps_every_valid_display_state_to_an_animation_key(string state, string label, PetAnimationKey expected)
{
    var activities = state == "active"
        ? label == "思考中/工作中" ? new[] { "thinking", "working" }
        : label == "思考中…" ? new[] { "thinking" } : new[] { "working" }
        : Array.Empty<string>();
    Assert.Equal(expected, PetDisplayState.From(state, activities, label, 1).AnimationKey);
}
```

另添加测试：标签为 `secret` 的 active 状态必须得到 `Disconnected`。

- [ ] **Step 2: 确认测试红色。**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetDisplayStateTests"`

Expected: FAIL，`PetAnimationKey` 与/或 `AnimationKey` 不存在。

- [ ] **Step 3: 仅实现映射。**

创建：

```csharp
namespace PetHelper;
public enum PetAnimationKey { Idle, Thinking, Working, ThinkingWorking, Waiting, Success, Error, Disconnected }
```

在 `PetDisplayState` 添加只读 `AnimationKey`，按已经验证的 `(State, Label)` 精确映射八个值，其余返回 `Disconnected`。不要改 `ProtocolReader`、`ProtocolMessage` 或 `src/protocol.ts`。

- [ ] **Step 4: 确认测试绿色。**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetDisplayStateTests"`

Expected: PASS。

- [ ] **Step 5: 提交。**

```powershell
git add -- pet-helper/PetAnimationKey.cs pet-helper/PetDisplayState.cs pet-helper.Tests/PetDisplayStateTests.cs
git commit -m "feat: map display states to pet animations"
```

### Task 2: 测试先行实现安全动作清单和回退

**Files:**
- Create: `pet-helper/Assets/pet-animations.json`
- Create: `pet-helper/PetAnimationManifest.cs`
- Create: `pet-helper.Tests/PetAnimationManifestTests.cs`
- Modify: `pet-helper/PetHelper.csproj`

- [ ] **Step 1: 写失败的清单测试。**

测试公开 API：

```csharp
public sealed record ResolvedAnimation(PetAnimationKey Key, ImmutableArray<string> Frames, int IntervalMs);
public sealed class PetAnimationManifest
{
    public static PetAnimationManifest Parse(string json);
    public ResolvedAnimation Resolve(PetAnimationKey requested, Func<string, bool> isFrameAvailable);
}
```

至少加入：

```csharp
[Fact]
public void Resolves_missing_composite_action_through_working_before_idle()
{
    var manifest = PetAnimationManifest.Parse("""
      { "idle": { "frames": ["placeholder-a.png"], "intervalMs": 1000 },
        "working": { "frames": ["Animations/working/001.png"], "intervalMs": 100, "fallback": "idle" },
        "thinking-working": { "frames": [], "intervalMs": 100, "fallback": "working" } }
      """);
    Assert.Equal(PetAnimationKey.Working,
        manifest.Resolve(PetAnimationKey.ThinkingWorking, f => f == "Animations/working/001.png").Key);
}
[Theory]
[InlineData("../secret.png")]
[InlineData("Animations\\working\\001.png")]
[InlineData("C:/secret.png")]
[InlineData("Animations/working/001.jpg")]
public void Rejects_unsafe_frame_identifiers(string frame) =>
    Assert.Throws<FormatException>(() => PetAnimationManifest.Parse($$"""{
      "idle": { "frames": ["{{frame}}"], "intervalMs": 1000 } }"""));
```

补足：多帧顺序保持；idle 缺失/无帧；未知回退；回退环；重复帧；间隔不在 16–10,000 毫秒，均为 `FormatException`。

- [ ] **Step 2: 确认红色。**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetAnimationManifestTests"`

Expected: FAIL，模块缺失。

- [ ] **Step 3: 实现严格解析和解析链。**

用 `System.Text.Json` 与 `ImmutableArray` 实现。只允许八个 kebab-case 键和字段 `frames`、`intervalMs`、`fallback`；idle 必须有帧且无 fallback。解析时拒绝所有未知键和不安全帧标识符；`Resolve` 要求某候选的所有帧可用，随后用已访问集合沿 fallback 查询。无可用动作抛 `InvalidOperationException`，不读取文件系统。

在 csproj 的现有 Resource 项中增加：

```xml
<Resource Include="Assets\Animations\**\*.png" />
<EmbeddedResource Include="Assets\pet-animations.json" />
```

若空目录 glob 导致错误，创建 `.gitkeep`，但不添加任何 PNG。

- [ ] **Step 4: 确认绿色。**

Run:
```powershell
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetAnimationManifestTests|FullyQualifiedName~PetDisplayStateTests"
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
```

Expected: PASS。

- [ ] **Step 5: 提交。**

```powershell
git add -- pet-helper/Assets/pet-animations.json pet-helper/PetAnimationManifest.cs pet-helper/PetHelper.csproj pet-helper.Tests/PetAnimationManifestTests.cs
git commit -m "feat: define safe pet animation manifest"
```

### Task 3: 测试先行实现循环、切换和减少动态效果

**Files:**
- Create: `pet-helper/PetAnimationPlayback.cs`
- Create: `pet-helper.Tests/PetAnimationPlaybackTests.cs`

- [ ] **Step 1: 写失败的播放状态机测试。**

测试 `Apply(PetAnimationKey, bool)`、`Advance()`、`Frame`、`Key`、`IntervalMs` 与 `IsAnimating`。构造有两帧的 thinking 动作并断言：进入显示 001，连续 Advance 显示 002、001；同一已解析动作的重复 Apply 保留当前索引；切换到 working 时重置其 001。另断言 reducedMotion 下 Advance 不改变 001、`IsAnimating == false`，重新关闭 reducedMotion 后多帧动作继续 tick；单帧 idle 从不 tick。

- [ ] **Step 2: 确认红色。**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetAnimationPlaybackTests"`

Expected: FAIL，`PetAnimationPlayback` 不存在。

- [ ] **Step 3: 实现最小纯状态机。**

构造函数注入 `PetAnimationManifest` 和 `Func<string, bool>`。按 `Resolve` 的有效 `Key`（非请求 Key）决定是否重置：有效 Key 变化时索引 0；相同且 reducedMotion 未变时保留索引。开启 reducedMotion 时索引 0 并停止动画；关闭时仅在帧数大于一时允许 tick。Advance 使用 `(index + 1) % Frames.Length`。

- [ ] **Step 4: 确认绿色。**

Run:
```powershell
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetAnimationPlaybackTests"
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
```

Expected: PASS。

- [ ] **Step 5: 提交。**

```powershell
git add -- pet-helper/PetAnimationPlayback.cs pet-helper.Tests/PetAnimationPlaybackTests.cs
git commit -m "feat: add deterministic pet animation playback"
```

### Task 4: 将播放模型接入 WPF Image 和生命周期

**Files:**
- Create: `pet-helper/PetAnimationPlayer.cs`
- Modify: `pet-helper/MainWindow.xaml`
- Modify: `pet-helper/MainWindow.xaml.cs`
- Modify: `test/wpf-layout.test.mjs`

- [ ] **Step 1: 写失败的 XAML 回归。**

```js
test('binds the pet image to the local animation player instead of a fixed source', () => {
  const xaml = readFileSync(new URL('../pet-helper/MainWindow.xaml', import.meta.url), 'utf8')
  assert.match(xaml, /<Image\s+x:Name="PetImage"/)
  assert.doesNotMatch(xaml, /<Image[^>]*Source="Assets\/placeholder-a\.png"/)
})
```

- [ ] **Step 2: 确认红色。**

Run:
```powershell
npm run build
node --test test/wpf-layout.test.mjs
```

Expected: FAIL，Image 尚未命名且仍有固定 Source。

- [ ] **Step 3: 实现受控 WPF 播放器与窗口接线。**

`PetAnimationPlayer` 接收 `Image`，通过固定程序集嵌入资源名读取清单，并只用已验证标识符生成 `pack://application:,,,/Assets/{frame}` URI；在 `BitmapImage` 设置 `OnLoad` 后 Freeze。资源加载失败只让其被视为不可用并走回退，不记录 URI/帧名。

持有单个 `DispatcherTimer`；按 Playback 的 `IntervalMs` tick，Advance 后更新 Image Source；非动画或 Stop 时停止 timer 并解除订阅。XAML 改成：

```xml
<Image x:Name="PetImage" Stretch="Uniform" VerticalAlignment="Bottom" />
```

`MainWindow` 保存最后 `PetDisplayState` 和 `reducedMotion`：构造后创建播放器并应用 idle；`ApplyDisplayState` 在气泡后 `Apply(state.AnimationKey, reducedMotion)`；`ApplyConfig` 更新 reducedMotion 并重新应用最后状态；`Closed` 调用 Stop。成功/错误的时间保持 Host 状态桥接的原规则，播放器不创建终态延时。

- [ ] **Step 4: 确认绿色。**

Run:
```powershell
npm test
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
```

Expected: PASS。

- [ ] **Step 5: 提交。**

```powershell
git add -- pet-helper/PetAnimationPlayer.cs pet-helper/MainWindow.xaml pet-helper/MainWindow.xaml.cs test/wpf-layout.test.mjs
git commit -m "feat: animate pet images by display state"
```

### Task 5: 包含资源输入回归、文档和最终验证

**Files:**
- Modify: `test/packaging.test.mjs`
- Modify: `README.md`

- [ ] **Step 1: 写发布输入回归。**

在 `test/packaging.test.mjs` 添加：

```js
test('source includes a local animation manifest and default idle asset', () => {
  assert.equal(existsSync('pet-helper/Assets/pet-animations.json'), true)
  assert.equal(existsSync('pet-helper/Assets/placeholder-a.png'), true)
})
```

- [ ] **Step 2: 运行并确认。**

Run:
```powershell
npm run build
node --test test/packaging.test.mjs
```

Expected: 若按顺序执行，PASS；它证明动作清单是单文件发布的构建输入。

- [ ] **Step 3: 更新 README。**

说明清单位置、透明 PNG 的动作帧目录、逐状态循环、缺失时回退 idle、减少动态效果固定首帧，以及仓库当前只含默认待机素材。不得改动两份 `placeholder-a.png`、`package.json` 或 `package-lock.json`。

- [ ] **Step 4: 检查锁定进程并做最终验证。**

先执行：

```powershell
Get-Process -Name pet-helper -ErrorAction SilentlyContinue | Select-Object Id, ProcessName
```

若有输出，要求用户先从右键菜单关闭；未经同意不得终止。无进程后执行：

```powershell
npm test
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
npm run build:helper
npm run test:package
npm pack
```

Expected: 每项退出码 0；测试输出不含 API Key、授权头、提示词、模型回复、工具参数、文件路径或 Session 正文。

- [ ] **Step 5: 提交。**

```powershell
git add -- test/packaging.test.mjs README.md
git commit -m "docs: describe pet animation assets"
```

## 计划自检

- 八种动作、组合活动的 working 优先回退、循环、同状态不闪动、状态切换、减少动态效果和单帧策略均有明确测试与任务。
- 所有资源选择仅在 Helper 内部，协议、端口和 Session 隐私边界不变。
- 计划不添加、替换或移动任何 PNG；默认 idle 继续使用现有 `placeholder-a.png`。

