# 随机待机动作与同一状态多动作 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让内置默认人物与外置导入人物都能在一个状态下声明多个动作，并让桌宠在待机等状态停留满冷却（默认 30 秒）后随机插播一个附加动作、播完回到主动作。

**Architecture:** 内置清单升到 `formatVersion: 5`（片段新增可选 `label`，状态新增 `extras`）；外置来源清单升到 `characterFormatVersion: 2`（`primary` + `extras`），人物库升到 `libraryFormatVersion: 2`（`frames/<状态>/primary|extra-N/`）。插播调度放在现有 `PetStateAnimationCoordinator` 内（新增附加动作阶段 + 注入式随机源），不引入第二个计时器；「人物形象」窗口新增二级「预览动作」下拉与「添加动作」入口。JSON Lines 协议保持 v17，`src/**` 一行不改。

**Tech Stack:** C# / .NET 10 (`net10.0-windows`, WPF, xUnit)、TypeScript 插件（仅用于跑既有 `npm test` 回归）。

**依据 spec:** `docs/superpowers/specs/2026-09-12-random-idle-extras-design.md`

---

## 文件结构

| 文件 | 职责 | 动作 |
| --- | --- | --- |
| `pet-helper/PetAnimationManifest.cs` | 内置清单解析（新增 v5：`label`、`extras`、隐式循环清单排除 extras、动作目录） | 改 |
| `pet-helper/PetStateAnimationCoordinator.cs` | 播放阶段机（新增附加动作阶段、冷却累加、随机插播、静态心跳、预览指定片段） | 改 |
| `pet-helper/PetAnimationPlayer.cs` | WPF 播放器（暴露动作目录与预览入口，持有 manifest 字段） | 改 |
| `pet-helper/PetActionChoice.cs` | 预览动作条目：显示名 + 是否附加 + 片段 | 建 |
| `pet-helper/CharacterManifest.cs` | 来源清单解析（v1 + v2：`primary`/`extras`/`name`/`extrasCooldownMs`） | 改 |
| `pet-helper/CharacterAssetSource.cs` | 库读取（v1 + v2）与动作目录 | 改 |
| `pet-helper/CharacterLibrary.cs` | 导入写 v2 布局；给已导入人物追加/替换/移除动作与 v1→v2 就地升级 | 改 |
| `pet-helper/GifFrameImporter.cs` | 帧解码与规范化（画布边长由调用方给定） | 改 |
| `pet-helper/CharacterWindow.xaml(.cs)` | 二级「预览动作」下拉；「添加动作…」内联面板 | 改 |
| `pet-helper/Assets/pet-animations.json`、`Assets/Animations/*/animation.json` | 升 v5 并补中文 `label` | 改 |
| `pet-helper.Tests/AnimationManifestTestData.cs` | v5 清单测试数据助手 | 建 |
| `pet-helper.Tests/PetAnimationExtrasManifestTests.cs` | v5 extras 解析与校验 | 建 |
| `pet-helper.Tests/PetExtrasPlaybackTests.cs` | 冷却、随机、打断、心跳 | 建 |
| `pet-helper.Tests/CharacterActionDraftTests.cs` | 追加/替换/移除动作与就地升级 | 建 |
| `pet-helper.Tests/PetActionCatalogTests.cs` | 动作目录 API | 建 |
| `pet-helper.Tests/PetAnimationManifestTests.cs`、`CharacterLibraryTests.cs`、`CharacterWindowTests.cs`、`CharacterPlayerTests.cs` | 既有测试（回归 + 新用例） | 改 |

约定：本仓库所有 C# 测试用 xUnit，测试类放在 `namespace PetHelper.Tests`；提交信息沿用 `feat:` / `fix:` / `docs:` 小写英文风格；每个 Task 结束都提交一次。

---

### Task 1: 内置清单 v5 —— `label` 与版本分流

**Files:**
- Modify: `pet-helper/PetAnimationManifest.cs`
- Create: `pet-helper.Tests/AnimationManifestTestData.cs`
- Modify: `pet-helper.Tests/PetAnimationManifestTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `pet-helper.Tests/AnimationManifestTestData.cs`：

```csharp
using PetHelper;

namespace PetHelper.Tests;

internal static class AnimationManifestTestData
{
    internal static string Root(int version) => $$"""
        {
          "formatVersion": {{version}},
          "actions": {
            "idle": { "manifest": "Animations/idle/animation.json" },
            "thinking": { "manifest": "Animations/thinking/animation.json", "fallback": "idle" },
            "working": { "manifest": "Animations/working/animation.json", "fallback": "idle" },
            "thinking-working": { "manifest": "Animations/thinking-working/animation.json", "fallback": "working" },
            "responding": { "manifest": "Animations/responding/animation.json", "fallback": "idle" },
            "waiting": { "manifest": "Animations/waiting/animation.json", "fallback": "idle" },
            "question": { "manifest": "Animations/question/animation.json", "fallback": "waiting" },
            "success": { "manifest": "Animations/success/animation.json", "fallback": "idle" },
            "error": { "manifest": "Animations/error/animation.json", "fallback": "idle" },
            "disconnected": { "manifest": "Animations/disconnected/animation.json", "fallback": "idle" }
          }
        }
        """;

    /// <summary>Parses the ten-action root with a custom idle state manifest; every other state is empty.</summary>
    internal static PetAnimationManifest ParseIdle(int version, string idleStateJson) =>
        PetAnimationManifest.Parse(Root(version),
            path => path == "Animations/idle/animation.json" ? idleStateJson : "{ \"clips\": {} }");
}
```

在 `pet-helper.Tests/PetAnimationManifestTests.cs` 末尾（`}` 之前）加入：

```csharp
    [Fact]
    public void Reads_a_version_five_clip_label()
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png", "breathe/002.png"],
                  "frameDurationMs": 125,
                  "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 },
                  "label": "呼吸"
                }
              }
            }
            """);

        Assert.Equal("呼吸", manifest.Resolve(PetAnimationKey.Idle, _ => true).Label);
    }

    [Fact]
    public void Version_four_still_rejects_a_clip_label()
    {
        Assert.Throws<FormatException>(() => AnimationManifestTestData.ParseIdle(4, """
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png"],
                  "frameDurationMs": 125,
                  "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 },
                  "label": "呼吸"
                }
              }
            }
            """));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"1234567890123\"")]
    [InlineData("\"有\\u0007控制符\"")]
    public void Rejects_an_invalid_version_five_label(string label)
    {
        Assert.Throws<FormatException>(() => AnimationManifestTestData.ParseIdle(5, $$"""
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png"],
                  "frameDurationMs": 125,
                  "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 },
                  "label": {{label}}
                }
              }
            }
            """));
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetAnimationManifestTests"`
Expected: FAIL —— `Reads_a_version_five_clip_label` 抛 `FormatException`（v5 未被识别），`ResolvedClip` 上也没有 `Label` 属性（编译错误）。

- [ ] **Step 3: 最小实现**

`pet-helper/PetAnimationManifest.cs`：

1）`ResolvedClip` 增加可选标签（放在已有 `FrameDurationsMs` 旁边，避免改位置参数）：

```csharp
    public ImmutableArray<int> FrameDurationsMs { get; init; }

    /// <summary>Optional local display name from the manifest; never crosses the JSON Lines protocol.</summary>
    public string? Label { get; init; }
```

2）`Parse` 的版本分流改为：

```csharp
            return version switch
            {
                2 => ParseVersionTwo(document.RootElement),
                3 => ParseVersionThree(document.RootElement, actionManifestReader ?? throw InvalidManifest()),
                4 => ParseStructuredManifest(document.RootElement, actionManifestReader ?? throw InvalidManifest(), 4, versionFive: false),
                5 => ParseStructuredManifest(document.RootElement, actionManifestReader ?? throw InvalidManifest(), 5, versionFive: true),
                _ => throw InvalidManifest(),
            };
```

3）把 `ParseVersionFour(JsonElement root, Func<string, string> actionManifestReader)` 改名为 `ParseStructuredManifest(JsonElement root, Func<string, string> actionManifestReader, int expectedVersion, bool versionFive)`，只改两处：方法签名，以及版本校验那一行

```csharp
                case "formatVersion":
                    if (field.Value.ValueKind != JsonValueKind.Number ||
                        !field.Value.TryGetInt32(out var version) || version != expectedVersion) throw InvalidManifest();
                    break;
```

其内部调用 `ParseVersionFourAction(...)` 时多传一个 `versionFive`，方法签名同步改为：

```csharp
    private static ActionDefinition ParseVersionFourAction(
        string actionName,
        JsonElement element,
        Func<string, string> actionManifestReader,
        ImmutableDictionary<string, ClipDefinition>.Builder clips,
        HashSet<string> allFrames,
        ref int totalFrames,
        bool versionFive)
```

并把 `ParseVersionFourStateManifest(actionName, childJson, clips, allFrames, ref totalFrames)` 调用改为 `ParseStructuredStateManifest(actionName, childJson, clips, allFrames, ref totalFrames, versionFive)`；该方法的 `ParseClip(...)` 调用改为 `ParseClip(clip.Value, allFrames, ref totalFrames, $"Animations/{actionName}/", versionFive)`。

4）`ParseClip` 增加 `label` 支持（新参数带默认值，v2/v3 老调用点不动）：

```csharp
    private static ClipDefinition ParseClip(
        JsonElement element,
        HashSet<string> allFrames,
        ref int totalFrames,
        string? framePrefix = null,
        bool allowLabel = false)
    {
        ImmutableArray<string>? frames = null;
        int? frameDurationMs = null;
        PetClipPlaybackMode? playback = null;
        PetStatusAnchor? statusAnchor = null;
        string? label = null;
        var renderTransform = PetRenderTransform.Identity;
        var seenFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
        {
            if (!seenFields.Add(field.Name)) throw InvalidManifest();
            switch (field.Name)
            {
                case "frames": frames = ParseFrames(field.Value, allFrames, ref totalFrames, framePrefix); break;
                case "frameDurationMs": frameDurationMs = ParseFrameDuration(field.Value); break;
                case "playback": playback = ParsePlayback(field.Value); break;
                case "statusAnchor": statusAnchor = ParseStatusAnchor(field.Value); break;
                case "renderTransform": renderTransform = ParseRenderTransform(field.Value); break;
                case "label": label = ParseLabel(field.Value); break;
                default: throw InvalidManifest();
            }
        }
        return new ClipDefinition(
            frames ?? throw InvalidManifest(),
            frameDurationMs ?? throw InvalidManifest(),
            playback ?? throw InvalidManifest(),
            statusAnchor ?? throw InvalidManifest(),
            renderTransform,
            label);
    }

    private static string ParseLabel(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String) throw InvalidManifest();
        var label = element.GetString()!;
        if (label.Length is < 1 or > 12 || label.Any(char.IsControl)) throw InvalidManifest();
        return label;
    }
```

注意：`label` 字段只有在 v5 才允许，因此 `case "label":` 必须写成

```csharp
                case "label" when allowLabel: label = ParseLabel(field.Value); break;
```

5）`ClipDefinition` 增加 `Label`，并在 `ToResolvedClip` 带出：

```csharp
    private static ResolvedClip ToResolvedClip(PetAnimationKey key, string id, ClipDefinition clip) => new(
        key,
        id,
        clip.Frames,
        clip.FrameDurationMs,
        clip.Playback,
        clip.StatusAnchor,
        clip.RenderTransform)
    {
        Label = clip.Label,
    };
```

```csharp
    private sealed record ClipDefinition(
        ImmutableArray<string> Frames,
        int FrameDurationMs,
        PetClipPlaybackMode Playback,
        PetStatusAnchor StatusAnchor,
        PetRenderTransform RenderTransform,
        string? Label = null);
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetAnimationManifestTests"`
Expected: PASS（既有 v2/v3/v4 用例同时保持通过）。

- [ ] **Step 5: 提交**

```powershell
git add pet-helper/PetAnimationManifest.cs pet-helper.Tests/AnimationManifestTestData.cs pet-helper.Tests/PetAnimationManifestTests.cs
git commit -m "feat: parse version five clip labels"
```

---

### Task 2: 内置清单 v5 —— `extras` 与隐式循环清单

**Files:**
- Modify: `pet-helper/PetAnimationManifest.cs`
- Create: `pet-helper.Tests/PetAnimationExtrasManifestTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `pet-helper.Tests/PetAnimationExtrasManifestTests.cs`：

```csharp
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetAnimationExtrasManifestTests
{
    private const string IdleWithExtras = """
        {
          "clips": {
            "breathe": {
              "frames": ["breathe/001.png", "breathe/002.png"],
              "frameDurationMs": 125, "playback": "loop",
              "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "呼吸"
            },
            "stretch": {
              "frames": ["stretch/001.png", "stretch/002.png"],
              "frameDurationMs": 100, "playback": "once",
              "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "伸懒腰"
            }
          },
          "extras": { "clips": ["stretch"], "cooldownMs": 30000 }
        }
        """;

    [Fact]
    public void Extras_are_resolved_separately_from_the_primary_loop()
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, IdleWithExtras);

        var program = manifest.ResolveProgram(PetAnimationKey.Idle, _ => true);

        Assert.Equal(new[] { "idle-breathe" }, program.Loop.Select(clip => clip.Id));
        Assert.Equal(new[] { "idle-stretch" }, program.Extras.Select(clip => clip.Id));
        Assert.Equal(30000, program.ExtrasCooldownMs);
        Assert.Equal("伸懒腰", program.Extras[0].Label);
    }

    [Fact]
    public void Extras_default_to_thirty_seconds_and_are_absent_by_default()
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png"], "frameDurationMs": 125, "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              }
            }
            """);

        var program = manifest.ResolveProgram(PetAnimationKey.Idle, _ => true);

        Assert.Empty(program.Extras);
        Assert.Equal(30000, program.ExtrasCooldownMs);
        Assert.Equal(PetAnimationKey.Idle, program.EffectiveKey);
    }

    [Fact]
    public void Extras_without_available_frames_are_dropped()
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, IdleWithExtras);

        var program = manifest.ResolveProgram(PetAnimationKey.Idle, frame => frame.StartsWith("breathe/", StringComparison.Ordinal));

        Assert.Empty(program.Extras);
    }

    [Theory]
    // A clip cannot be both the primary loop and an extra.
    [InlineData("\"program\": { \"enter\": [], \"loop\": [\"stretch\"] }, \"extras\": { \"clips\": [\"stretch\"] }")]
    // Extras must be declared as one-shot clips.
    [InlineData("\"extras\": { \"clips\": [\"breathe\"] }")]
    // Duplicate extras are rejected.
    [InlineData("\"extras\": { \"clips\": [\"stretch\", \"stretch\"] }")]
    // Unknown extra clip id.
    [InlineData("\"extras\": { \"clips\": [\"missing\"] }")]
    // Cooldown below the lower bound.
    [InlineData("\"extras\": { \"clips\": [\"stretch\"], \"cooldownMs\": 4000 }")]
    // Cooldown above the upper bound.
    [InlineData("\"extras\": { \"clips\": [\"stretch\"], \"cooldownMs\": 600001 }")]
    // Unknown field inside extras.
    [InlineData("\"extras\": { \"clips\": [\"stretch\"], \"weight\": 2 }")]
    // Empty extras list.
    [InlineData("\"extras\": { \"clips\": [] }")]
    public void Rejects_an_invalid_extras_block(string extrasOrProgram)
    {
        var state = $$"""
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png", "breathe/002.png"], "frameDurationMs": 125, "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                },
                "stretch": {
                  "frames": ["stretch/001.png", "stretch/002.png"], "frameDurationMs": 100, "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              },
              {{extrasOrProgram}}
            }
            """;

        Assert.Throws<FormatException>(() => AnimationManifestTestData.ParseIdle(5, state));
    }

    [Fact]
    public void Rejects_more_than_four_extras()
    {
        var clips = string.Join(",", Enumerable.Range(0, 5).Select(index =>
            $"\"extra{index}\": {{ \"frames\": [\"extra{index}/001.png\"], \"frameDurationMs\": 100, \"playback\": \"once\", \"statusAnchor\": {{ \"x\": 0.5, \"y\": 0.11 }} }}"));
        var ids = string.Join(",", Enumerable.Range(0, 5).Select(index => $"\"extra{index}\""));
        var state = $$"""
            {
              "clips": {
                "breathe": { "frames": ["breathe/001.png", "breathe/002.png"], "frameDurationMs": 125, "playback": "loop", "statusAnchor": { "x": 0.5, "y": 0.11 } },
                {{clips}}
              },
              "extras": { "clips": [{{ids}}] }
            }
            """;

        Assert.Throws<FormatException>(() => AnimationManifestTestData.ParseIdle(5, state));
    }

    [Fact]
    public void Rejects_extras_that_leave_no_primary_action()
    {
        Assert.Throws<FormatException>(() => AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "stretch": {
                  "frames": ["stretch/001.png"], "frameDurationMs": 100, "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              },
              "extras": { "clips": ["stretch"] }
            }
            """));
    }

    [Fact]
    public void Version_four_rejects_extras()
    {
        Assert.Throws<FormatException>(() => AnimationManifestTestData.ParseIdle(4, IdleWithExtras));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetAnimationExtrasManifestTests"`
Expected: FAIL —— `ResolvedStateProgram` 上没有 `Extras` / `ExtrasCooldownMs`（编译错误），`extras` 字段被 v5 解析器当未知字段拒绝。

- [ ] **Step 3: 最小实现**

`pet-helper/PetAnimationManifest.cs`：

1）`ResolvedStateProgram` 增加两个带默认值的属性（保持既有位置参数调用点不变）：

```csharp
public sealed record ResolvedStateProgram(
    PetAnimationKey EffectiveKey,
    ImmutableArray<ResolvedClip> Enter,
    ImmutableArray<ResolvedClip> Loop,
    ImmutableArray<ResolvedTransition> Transitions,
    bool LoopRepeats)
{
    /// <summary>One-shot clips that may be played after the state has looped for its cooldown.</summary>
    public ImmutableArray<ResolvedClip> Extras { get; init; } = ImmutableArray<ResolvedClip>.Empty;

    /// <summary>Milliseconds of primary playback before an extra may be played.</summary>
    public int ExtrasCooldownMs { get; init; } = PetAnimationManifest.DefaultExtrasCooldownMs;
}
```

2）常量与上限：

```csharp
    internal const int DefaultExtrasCooldownMs = 30000;
    private const int MinimumExtrasCooldownMs = 5000;
    private const int MaximumExtrasCooldownMs = 600000;
    private const int MaximumExtrasPerAction = 4;
```

3）`ResolveProgram` 里带出 extras（注意用 fallback 之后的 `current` 作为 key）：

```csharp
            if (!loop.IsEmpty)
            {
                var enter = ResolveClips(current, program.Enter, isFrameAvailable);
                var transitions = (action.Transitions ?? ImmutableArray<TransitionDefinition>.Empty)
                    .Select(transition => new ResolvedTransition(
                        transition.Targets,
                        ResolveClips(current, transition.ClipIds, isFrameAvailable)))
                    .Where(transition => !transition.Clips.IsEmpty)
                    .ToImmutableArray();
                return new ResolvedStateProgram(current, enter, loop, transitions, program.LoopRepeats)
                {
                    Extras = ResolveClips(current, action.Extras, isFrameAvailable),
                    ExtrasCooldownMs = action.ExtrasCooldownMs,
                };
            }
```

4）`ActionDefinition` 增加两个字段：

```csharp
    private sealed record ActionDefinition(
        ImmutableArray<string> ClipIds,
        PetAnimationKey? Fallback,
        ProgramDefinition? Program = null,
        ImmutableArray<TransitionDefinition>? Transitions = null,
        ImmutableArray<string> Extras = default,
        int ExtrasCooldownMs = DefaultExtrasCooldownMs);
```

（`Extras = default` 在未赋值时是 `default(ImmutableArray<string>)`；`ResolveClips` 接受它并按空处理，与现有 `IsDefaultOrEmpty` 风格一致。若编译器对 `ImmutableArray` 默认值报错，改为 `ImmutableArray<string>.Empty`。）

5）`ParseStructuredStateManifest` 增加 `extras` 解析与"减去 extras"的隐式清单。把该方法里现有的 `clipIds`（全部片段）与返回部分改成：

```csharp
            var localIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var clipIds = ImmutableArray.CreateBuilder<string>();
            foreach (var clip in clipsElement.EnumerateObject())
            {
                var id = $"{actionName}-{clip.Name}";
                if (!IsSafeClipId(clip.Name) || !IsSafeClipId(id) ||
                    clip.Value.ValueKind != JsonValueKind.Object ||
                    clipIds.Count >= MaximumClipsPerAction ||
                    !localIds.TryAdd(clip.Name, id) ||
                    !clips.TryAdd(id, ParseClip(
                        clip.Value,
                        allFrames,
                        ref totalFrames,
                        $"Animations/{actionName}/",
                        versionFive)))
                {
                    throw InvalidManifest();
                }
                clipIds.Add(id);
            }

            var program = hasProgram
                ? ParseProgram(programElement, localIds, clips)
                : null;
            ImmutableArray<string> extras = ImmutableArray<string>.Empty;
            var cooldownMs = DefaultExtrasCooldownMs;
            if (hasExtras)
            {
                (extras, cooldownMs) = ParseExtras(extrasElement, localIds, clips, program);
            }
            var loopIds = extras.IsEmpty
                ? clipIds.ToImmutable()
                : clipIds.Where(id => !extras.Contains(id, StringComparer.Ordinal)).ToImmutableArray();
            if (program is null && loopIds.IsEmpty) throw InvalidManifest();
            var transitions = hasTransitions
                ? ParseTransitions(transitionsElement, localIds, clips)
                : ImmutableArray<TransitionDefinition>.Empty;
            return new StateManifestDefinition(
                loopIds,
                program ?? new ProgramDefinition(ImmutableArray<string>.Empty, loopIds, false),
                transitions,
                extras,
                cooldownMs);
```

该方法的字段循环需要认识 `extras`（仅 v5）：

```csharp
                switch (field.Name)
                {
                    case "clips": clipsElement = field.Value; hasClips = true; break;
                    case "program": programElement = field.Value; hasProgram = true; break;
                    case "transitions": transitionsElement = field.Value; hasTransitions = true; break;
                    case "extras" when versionFive: extrasElement = field.Value; hasExtras = true; break;
                    default: throw InvalidManifest();
                }
```

6）新增 `ParseExtras`：

```csharp
    private static (ImmutableArray<string> ClipIds, int CooldownMs) ParseExtras(
        JsonElement element,
        IReadOnlyDictionary<string, string> localIds,
        ImmutableDictionary<string, ClipDefinition>.Builder clips,
        ProgramDefinition? program)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidManifest();
        JsonElement clipsElement = default;
        var hasClips = false;
        var cooldownMs = DefaultExtrasCooldownMs;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw InvalidManifest();
            switch (field.Name)
            {
                case "clips": clipsElement = field.Value; hasClips = true; break;
                case "cooldownMs":
                    if (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetInt32(out cooldownMs) ||
                        cooldownMs < MinimumExtrasCooldownMs || cooldownMs > MaximumExtrasCooldownMs) throw InvalidManifest();
                    break;
                default: throw InvalidManifest();
            }
        }
        if (!hasClips || clipsElement.ValueKind != JsonValueKind.Array) throw InvalidManifest();
        var programClips = program is null
            ? ImmutableArray<string>.Empty
            : program.Enter.AddRange(program.Loop);
        var ids = ImmutableArray.CreateBuilder<string>();
        foreach (var item in clipsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !localIds.TryGetValue(item.GetString() ?? string.Empty, out var id) ||
                ids.Contains(id, StringComparer.Ordinal) ||
                ids.Count >= MaximumExtrasPerAction ||
                clips[id].Playback != PetClipPlaybackMode.Once ||
                programClips.Contains(id, StringComparer.Ordinal))
            {
                throw InvalidManifest();
            }
            ids.Add(id);
        }
        if (ids.Count == 0) throw InvalidManifest();
        return (ids.ToImmutable(), cooldownMs);
    }
```

注意：`transitions` 里的片段也可能与 extras 冲突，但 transition 片段是 `once` 且只在离开状态时播放；真正的冲突只有 `program` 的 `enter`/`loop`，因此上面的 `programClips` 只取这两个。若要与 spec 的措辞严格一致（"不得同时出现在 program（enter/loop/transitions）与 extras 中"），把 `ParseTransitions` 的结果也纳入检查：在 `ParseExtras` 调用前先解析 transitions，并把 `transitions.SelectMany(t => t.ClipIds)` 一并传入 `programClips`。

7）`StateManifestDefinition` 同步扩展：

```csharp
    private sealed record StateManifestDefinition(
        ImmutableArray<string> ClipIds,
        ProgramDefinition Program,
        ImmutableArray<TransitionDefinition> Transitions,
        ImmutableArray<string> Extras,
        int ExtrasCooldownMs);
```

8）`ParseVersionFourAction`（已改名 `ParseStructuredAction` 的一部分）把 `state.Extras` / `state.ExtrasCooldownMs` 传进 `ActionDefinition`：

```csharp
        return new ActionDefinition(state.ClipIds, fallback, state.Program, state.Transitions, state.Extras, state.ExtrasCooldownMs);
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetAnimationExtrasManifestTests"`
Expected: PASS，且 `--filter "FullyQualifiedName~PetAnimationManifestTests|FullyQualifiedName~PetStateAnimationCoordinatorTests"` 也全绿（v4 语义未变）。

- [ ] **Step 5: 提交**

```powershell
git add pet-helper/PetAnimationManifest.cs pet-helper.Tests/PetAnimationExtrasManifestTests.cs
git commit -m "feat: parse version five action extras"
```

---

### Task 3: 冷却与随机插播

**Files:**
- Modify: `pet-helper/PetStateAnimationCoordinator.cs`
- Create: `pet-helper.Tests/PetExtrasPlaybackTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `pet-helper.Tests/PetExtrasPlaybackTests.cs`：

```csharp
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetExtrasPlaybackTests
{
    // Primary: two-frame loop at 100 ms. Extras: two-frame one-shot at 50 ms. Cooldown: 5000 ms.
    internal const string IdleState = """
        {
          "clips": {
            "breathe": {
              "frames": ["breathe/001.png", "breathe/002.png"], "frameDurationMs": 100, "playback": "loop",
              "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "呼吸"
            },
            "stretch": {
              "frames": ["stretch/001.png", "stretch/002.png"], "frameDurationMs": 50, "playback": "once",
              "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "伸懒腰"
            },
            "yawn": {
              "frames": ["yawn/001.png", "yawn/002.png"], "frameDurationMs": 50, "playback": "once",
              "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "打哈欠"
            }
          },
          "extras": { "clips": ["stretch", "yawn"], "cooldownMs": 5000 }
        }
        """;

    internal static PetStateAnimationCoordinator Create(Func<int, int>? nextExtraIndex = null) =>
        new(AnimationManifestTestData.ParseIdle(5, IdleState), _ => true, nextExtraIndex);

    [Fact]
    public void An_extra_starts_only_after_the_cooldown_and_returns_to_the_primary()
    {
        var coordinator = Create(_ => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);

        for (var tick = 0; tick < 49; tick++) coordinator.Advance();
        Assert.StartsWith("Animations/idle/breathe/", coordinator.Frame, StringComparison.Ordinal);

        coordinator.Advance();  // 50th tick: 50 * 100 ms = the 5000 ms cooldown.
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);

        coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/002.png", coordinator.Frame);

        coordinator.Advance();  // The extra finishes and the primary restarts from its first frame.
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);
    }

    [Fact]
    public void The_cooldown_restarts_after_every_extra()
    {
        var coordinator = Create(_ => 1);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        for (var tick = 0; tick < 50; tick++) coordinator.Advance();
        Assert.Equal("Animations/idle/yawn/001.png", coordinator.Frame);

        coordinator.Advance();
        coordinator.Advance();
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);

        for (var tick = 0; tick < 49; tick++) coordinator.Advance();
        Assert.StartsWith("Animations/idle/breathe/", coordinator.Frame, StringComparison.Ordinal);

        coordinator.Advance();
        Assert.Equal("Animations/idle/yawn/001.png", coordinator.Frame);
    }

    [Fact]
    public void States_without_extras_never_interrupt_their_loop()
    {
        var coordinator = new PetStateAnimationCoordinator(AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "breathe": {
                  "frames": ["breathe/001.png", "breathe/002.png"], "frameDurationMs": 100, "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              }
            }
            """), _ => true);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        for (var tick = 0; tick < 200; tick++)
        {
            coordinator.Advance();
            Assert.StartsWith("Animations/idle/breathe/", coordinator.Frame, StringComparison.Ordinal);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetExtrasPlaybackTests"`
Expected: FAIL —— `PetStateAnimationCoordinator` 没有接受第三个参数的构造函数（编译错误）。

- [ ] **Step 3: 最小实现**

`pet-helper/PetStateAnimationCoordinator.cs`：

1）字段、常量与构造函数：

```csharp
    private readonly Func<PetAnimationKey, Func<string, bool>, ResolvedStateProgram> resolveProgram;
    private readonly Func<string, bool> isFrameAvailable;
    private readonly Func<int, int> nextExtraIndex;
    private readonly PetClipPlayback clipPlayback = new();
    private ResolvedStateProgram? currentProgram;
    private ResolvedTransition? currentTransition;
    private ResolvedTransition? transitionAfterEnter;
    private ResolvedClip? currentClip;
    private ResolvedClip? currentExtra;
    private PetAnimationKey requested;
    private AnimationPhase phase;
    private int clipIndex;
    private int extraElapsedMs;
    private bool reducedMotion;
    private bool clipCompleted;
    private bool finished;

    public PetStateAnimationCoordinator(PetAnimationManifest manifest, Func<string, bool> isFrameAvailable)
        : this(manifest.ResolveProgram, isFrameAvailable, null) { }

    internal PetStateAnimationCoordinator(
        Func<PetAnimationKey, Func<string, bool>, ResolvedStateProgram> resolveProgram,
        Func<string, bool> isFrameAvailable,
        Func<int, int>? nextExtraIndex = null)
    {
        this.resolveProgram = resolveProgram;
        this.isFrameAvailable = isFrameAvailable ?? throw new ArgumentNullException(nameof(isFrameAvailable));
        this.nextExtraIndex = nextExtraIndex ?? (count => Random.Shared.Next(count));
        clipPlayback.Completed += (_, _) =>
        {
            clipCompleted = true;
            Completed?.Invoke(this, EventArgs.Empty);
        };
    }
```

2）`Advance` 改为累加冷却并触发插播：

```csharp
    public void Advance()
    {
        if (!IsAnimating) return;
        var elapsedMs = IntervalMs;
        clipPlayback.Advance();
        if (clipCompleted)
        {
            clipCompleted = false;
            if (currentExtra is not null)
            {
                currentExtra = null;
                StartLoop();
                return;
            }
            MoveToNextClip();
            return;
        }
        if (currentExtra is null && phase == AnimationPhase.Looping)
        {
            extraElapsedMs += elapsedMs;
            if (!reducedMotion && CurrentProgram.Extras.Length > 0 && extraElapsedMs >= CurrentProgram.ExtrasCooldownMs)
                StartExtra();
        }
    }

    private void StartExtra()
    {
        var extras = CurrentProgram.Extras;
        currentExtra = extras[nextExtraIndex(extras.Length)];
        phase = AnimationPhase.Looping;
        clipIndex = 0;
        StartClip(currentExtra);
    }
```

3）`StartLoop` 负责"回到主动作并重新计时"，`StartTarget` / `StartTransition` 负责放弃插播：

```csharp
    private void StartTarget(PetAnimationKey target, bool useEnter)
    {
        currentProgram = resolveProgram(target, isFrameAvailable);
        currentTransition = null;
        transitionAfterEnter = null;
        currentExtra = null;
        extraElapsedMs = 0;
        requested = target;
        if (!useEnter || reducedMotion || currentProgram.Enter.IsEmpty)
        {
            StartLoop();
            return;
        }

        phase = AnimationPhase.Entering;
        clipIndex = 0;
        StartClip(currentProgram.Enter[clipIndex]);
    }

    private void StartLoop()
    {
        phase = AnimationPhase.Looping;
        currentExtra = null;
        extraElapsedMs = 0;
        clipIndex = 0;
        StartClip(CurrentProgram.Loop[clipIndex]);
    }

    private void StartTransition(ResolvedTransition route)
    {
        currentTransition = route;
        transitionAfterEnter = null;
        currentExtra = null;
        extraElapsedMs = 0;
        phase = AnimationPhase.Transitioning;
        clipIndex = 0;
        StartClip(route.Clips[clipIndex]);
    }
```

4）静态主动作的心跳（单帧 `loop` 主动作 + 有附加动作时仍要推进计时）：

```csharp
    private const int StaticPrimaryHeartbeatMs = 1000;

    public int IntervalMs => StaticPrimaryNeedsHeartbeat ? StaticPrimaryHeartbeatMs : clipPlayback.FrameDurationMs;

    public bool IsAnimating => !reducedMotion && !finished &&
        (clipCompleted || clipPlayback.IsAnimating || StaticPrimaryNeedsHeartbeat);

    private bool StaticPrimaryNeedsHeartbeat => currentExtra is null &&
        phase == AnimationPhase.Looping &&
        currentProgram is { } program &&
        !program.Extras.IsEmpty &&
        program.Loop[0].Frames.Length == 1;
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetExtrasPlaybackTests|FullyQualifiedName~PetStateAnimationCoordinatorTests"`
Expected: PASS（既有 coordinator 用例不受影响）。

- [ ] **Step 5: 提交**

```powershell
git add pet-helper/PetStateAnimationCoordinator.cs pet-helper.Tests/PetExtrasPlaybackTests.cs
git commit -m "feat: play random extras after an idle cooldown"
```

---

### Task 4: 打断规则、减少动态效果与静态心跳

**Files:**
- Modify: `pet-helper.Tests/AnimationManifestTestData.cs`
- Modify: `pet-helper.Tests/PetExtrasPlaybackTests.cs`

- [ ] **Step 1: 扩展测试数据助手**

`pet-helper.Tests/AnimationManifestTestData.cs` 的 `ParseIdle` 改为委托给一个新的三参重载：

```csharp
    /// <summary>Parses the ten-action root with custom idle and working state manifests.</summary>
    internal static PetAnimationManifest Parse(int version, string idleStateJson, string workingStateJson) =>
        PetAnimationManifest.Parse(Root(version), path => path switch
        {
            "Animations/idle/animation.json" => idleStateJson,
            "Animations/working/animation.json" => workingStateJson,
            _ => "{ \"clips\": {} }",
        });

    /// <summary>Parses the ten-action root with a custom idle state manifest; every other state is empty.</summary>
    internal static PetAnimationManifest ParseIdle(int version, string idleStateJson) =>
        Parse(version, idleStateJson, "{ \"clips\": {} }");
```

- [ ] **Step 2: 写失败测试**

在 `pet-helper.Tests/PetExtrasPlaybackTests.cs` 的类里追加：

```csharp
    private const string WorkingState = """
        {
          "clips": {
            "haul": {
              "frames": ["haul/001.png", "haul/002.png"], "frameDurationMs": 100, "playback": "loop",
              "statusAnchor": { "x": 0.5, "y": 0.11 }
            }
          }
        }
        """;

    private static PetStateAnimationCoordinator CreateWithWorking(Func<int, int>? nextExtraIndex = null) =>
        new(AnimationManifestTestData.Parse(5, IdleState, WorkingState), _ => true, nextExtraIndex);

    [Fact]
    public void Repeated_messages_for_the_same_state_do_not_interrupt_an_extra_or_reset_the_cooldown()
    {
        var coordinator = Create(_ => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        for (var tick = 0; tick < 50; tick++) coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);

        coordinator.Advance();
        coordinator.Advance();
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);

        // The cooldown restarted when the extra returned, so the next extra needs another 50 ticks.
        for (var tick = 0; tick < 49; tick++) coordinator.Advance();
        Assert.StartsWith("Animations/idle/breathe/", coordinator.Frame, StringComparison.Ordinal);
        coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);
    }

    [Fact]
    public void A_real_state_change_abandons_the_extra_and_restarts_the_cooldown()
    {
        var coordinator = CreateWithWorking(_ => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        for (var tick = 0; tick < 50; tick++) coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);

        coordinator.Apply(PetAnimationKey.Working, reducedMotion: false);
        Assert.Equal("Animations/working/haul/001.png", coordinator.Frame);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);
        for (var tick = 0; tick < 49; tick++) coordinator.Advance();
        Assert.StartsWith("Animations/idle/breathe/", coordinator.Frame, StringComparison.Ordinal);
        coordinator.Advance();
        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);
    }

    [Fact]
    public void Reduced_motion_never_plays_an_extra()
    {
        var coordinator = Create(_ => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: true);
        Assert.False(coordinator.IsAnimating);
        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);

        for (var tick = 0; tick < 500; tick++) coordinator.Advance();

        Assert.Equal("Animations/idle/breathe/001.png", coordinator.Frame);
        Assert.False(coordinator.IsAnimating);
    }

    [Fact]
    public void A_one_shot_state_never_reaches_an_extra()
    {
        var coordinator = new PetStateAnimationCoordinator(AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "complete": {
                  "frames": ["complete/001.png", "complete/002.png"], "frameDurationMs": 100, "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                },
                "stretch": {
                  "frames": ["stretch/001.png"], "frameDurationMs": 1500, "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              },
              "extras": { "clips": ["stretch"], "cooldownMs": 5000 }
            }
            """), _ => true, _ => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        for (var tick = 0; tick < 100; tick++) coordinator.Advance();

        Assert.Equal("Animations/idle/complete/002.png", coordinator.Frame);
        Assert.False(coordinator.IsAnimating);
    }

    [Fact]
    public void A_static_primary_keeps_a_one_second_heartbeat_so_extras_still_fire()
    {
        var coordinator = new PetStateAnimationCoordinator(AnimationManifestTestData.ParseIdle(5, """
            {
              "clips": {
                "pose": {
                  "frames": ["pose/001.png"], "frameDurationMs": 100, "playback": "loop",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                },
                "stretch": {
                  "frames": ["stretch/001.png"], "frameDurationMs": 1500, "playback": "once",
                  "statusAnchor": { "x": 0.5, "y": 0.11 }
                }
              },
              "extras": { "clips": ["stretch"], "cooldownMs": 5000 }
            }
            """), _ => true, _ => 0);

        coordinator.Apply(PetAnimationKey.Idle, reducedMotion: false);
        Assert.True(coordinator.IsAnimating);
        Assert.Equal(1000, coordinator.IntervalMs);

        for (var tick = 0; tick < 5; tick++) coordinator.Advance();

        Assert.Equal("Animations/idle/stretch/001.png", coordinator.Frame);
        Assert.Equal(1500, coordinator.IntervalMs);

        coordinator.Advance();
        Assert.Equal("Animations/idle/pose/001.png", coordinator.Frame);
        Assert.Equal(1000, coordinator.IntervalMs);
    }
```

- [ ] **Step 3: 跑测试确认失败/通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetExtrasPlaybackTests"`
Expected: 若 Task 3 的实现已覆盖这些规则则全部 PASS。任何 FAIL 都说明实现缺口，按提示补：常见缺口是"同一状态重复 `Apply` 意外重置冷却"（应保持 `if (nextRequested == requested) return;` 在最前）与"`StartLoop` 未清零 `extraElapsedMs`"。

- [ ] **Step 4: 提交**

```powershell
git add pet-helper.Tests/AnimationManifestTestData.cs pet-helper.Tests/PetExtrasPlaybackTests.cs
git commit -m "test: cover extra interruption and heartbeat rules"
```

---

### Task 5: 来源清单 v2 与人物库 v2 写入

**Files:**
- Modify: `pet-helper/CharacterManifest.cs`
- Modify: `pet-helper/CharacterAssetSource.cs`
- Modify: `pet-helper/CharacterLibrary.cs`
- Modify: `pet-helper.Tests/CharacterLibraryTests.cs`

- [ ] **Step 1: 写失败测试**

在 `pet-helper.Tests/CharacterLibraryTests.cs` 的类里追加（并复用已有的 `TinyGif()`）：

```csharp
    [Fact]
    public void Imports_a_version_two_character_with_extras_into_the_version_two_layout()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), TinyPng());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "extrasCooldownMs":45000,
             "actions":{"idle":{
               "primary":{"type":"gif","file":"idle.gif"},
               "extras":[{"name":"伸懒腰","type":"png","file":"stretch.png","frameDurationMs":1500}]}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));

        using (var draft = library.PrepareDirectory(root, CancellationToken.None))
        {
            var info = library.Commit(draft, "多动作", new(.5, .1), .95);
            var source = library.Load(info.Id);
            var program = source.ResolveProgram(PetAnimationKey.Idle, _ => true);

            Assert.Single(program.Loop);
            Assert.Single(program.Extras);
            Assert.Equal(45000, program.ExtrasCooldownMs);
            Assert.Equal("frames/idle/extra-0/0000.png", program.Extras[0].Frames[0]);
            Assert.Equal(1500, program.Extras[0].FrameDurationsMs[0]);
            Assert.StartsWith("frames/idle/primary/", program.Loop[0].Frames[0], StringComparison.Ordinal);
            var manifestPath = Path.Combine(root, "output", "library", info.Id, "character.json");
            var manifest = File.ReadAllText(manifestPath);
            Assert.Contains("\"libraryFormatVersion\":2", manifest, StringComparison.Ordinal);
            Assert.Contains("\"extrasCooldownMs\":45000", manifest, StringComparison.Ordinal);
            Assert.Contains("伸懒腰", manifest, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Rejects_a_single_frame_png_extra_without_a_declared_duration()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), TinyPng());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{
               "primary":{"type":"gif","file":"idle.gif"},
               "extras":[{"name":"伸懒腰","type":"png","file":"stretch.png"}]}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));

        Assert.Throws<FormatException>(() => library.PrepareDirectory(root, CancellationToken.None));
    }

    [Fact]
    public void Rejects_duplicate_extra_names_in_one_state()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        File.WriteAllBytes(Path.Combine(root, "a.png"), TinyPng());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":2,"name":"多动作","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{
               "primary":{"type":"gif","file":"idle.gif"},
               "extras":[{"name":"伸懒腰","type":"png","file":"a.png","frameDurationMs":1000},
                         {"name":"伸懒腰","type":"png","file":"a.png","frameDurationMs":1000}]}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));

        Assert.Throws<FormatException>(() => library.PrepareDirectory(root, CancellationToken.None));
    }

    [Fact]
    public void Rejects_an_action_object_that_mixes_primary_and_type_forms()
    {
        var json = """
            {"characterFormatVersion":2,"name":"test","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{"type":"gif","file":"idle.gif","primary":{"type":"gif","file":"idle.gif"}}}}
            """;

        Assert.Throws<FormatException>(() => CharacterManifest.Parse(json));
    }

    internal static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterLibraryTests"`
Expected: FAIL —— `characterFormatVersion: 2` 目前被拒绝（`Integer(..., 1, 1)`），`ResolvedStateProgram.Extras` 为空，`CharacterAction` 没有 extras 概念。

- [ ] **Step 3: 实现来源清单 v2**

`pet-helper/CharacterManifest.cs` 把模型与解析改成：

```csharp
internal sealed record CharacterAction(string Type, string[] Files, int FrameDurationMs, bool DurationDeclared);
internal sealed record CharacterExtra(string Name, CharacterAction Action);
internal sealed record CharacterStateActions(CharacterAction Primary, ImmutableArray<CharacterExtra> Extras);

internal sealed record CharacterManifest(string Name, PetStatusAnchor StatusAnchor, double Baseline,
    int ExtrasCooldownMs, Dictionary<string, CharacterStateActions> Actions)
{
    internal const int DefaultExtrasCooldownMs = 30000;
    internal const int MaximumExtrasPerState = 4;
```

`Parse` 全文替换为：

```csharp
    internal static CharacterManifest Parse(string json)
    {
        using var document = ReadJson(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Invalid();
        if (!root.TryGetProperty("characterFormatVersion", out var versionElement)) throw Invalid();
        var version = Integer(versionElement, 1, 2);
        var required = new[] { "characterFormatVersion", "name", "statusAnchor", "baseline", "actions" };
        var allowed = version == 2
            ? required.Append("extrasCooldownMs").ToHashSet(StringComparer.Ordinal)
            : required.ToHashSet(StringComparer.Ordinal);
        var seen = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!seen.IsSubsetOf(allowed) || required.Any(name => !seen.Contains(name))) throw Invalid();
        var cooldownMs = version == 2 && root.TryGetProperty("extrasCooldownMs", out var cooldownElement)
            ? Integer(cooldownElement, 5000, 600000)
            : DefaultExtrasCooldownMs;

        var actions = new Dictionary<string, CharacterStateActions>(StringComparer.Ordinal);
        var value = root.GetProperty("actions");
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        foreach (var entry in value.EnumerateObject())
        {
            if (!Keys.ContainsKey(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object) throw Invalid();
            var hasType = entry.Value.TryGetProperty("type", out _);
            var hasPrimary = entry.Value.TryGetProperty("primary", out _);
            if (hasType == hasPrimary) throw Invalid();
            if (!hasPrimary)
            {
                actions.Add(entry.Name, new(ParseAction(entry.Value, isExtra: false), []));
                continue;
            }

            var fields = entry.Value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            if (!fields.SetEquals(new[] { "primary", "extras" })) throw Invalid();
            var primary = ParseAction(entry.Value.GetProperty("primary"), isExtra: false);
            var extrasElement = entry.Value.GetProperty("extras");
            if (extrasElement.ValueKind != JsonValueKind.Array ||
                extrasElement.GetArrayLength() is < 1 or > MaximumExtrasPerState) throw Invalid();
            var extras = ImmutableArray.CreateBuilder<CharacterExtra>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var extraElement in extrasElement.EnumerateArray())
            {
                if (extraElement.ValueKind != JsonValueKind.Object) throw Invalid();
                var extraFields = extraElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
                if (!extraFields.SetEquals(new[] { "name", "type", "file", "frameDurationMs" }) &&
                    !extraFields.SetEquals(new[] { "name", "type", "file" }) &&
                    !extraFields.SetEquals(new[] { "name", "type", "frames", "frameDurationMs" })) throw Invalid();
                var name = ValidateName(Text(extraElement.GetProperty("name")));
                if (!names.Add(name)) throw Invalid();
                extras.Add(new(name, ParseAction(extraElement, isExtra: true)));
            }
            actions.Add(entry.Name, new(primary, extras.ToImmutable()));
        }
        if (!actions.ContainsKey("idle")) throw Invalid();
        return new(ValidateName(Text(root.GetProperty("name"))), Anchor(root.GetProperty("statusAnchor")),
            Unit(root.GetProperty("baseline")), cooldownMs, actions);
    }

    private static CharacterAction ParseAction(JsonElement element, bool isExtra)
    {
        var type = Text(element.GetProperty("type"));
        if (type is "gif" or "png")
        {
            var expected = isExtra && element.TryGetProperty("frameDurationMs", out _)
                ? new[] { "type", "file", "frameDurationMs" }
                : new[] { "type", "file" };
            Fields(element, expected);
            var files = new[] { AssetReference(Text(element.GetProperty("file")), "." + type) };
            if (!isExtra || !element.TryGetProperty("frameDurationMs", out var durationElement))
                return new(type, files, 100, DurationDeclared: false);
            var declared = Integer(durationElement, 16, 10000);
            return new(type, files, declared, DurationDeclared: true);
        }
        if (type == "png-sequence")
        {
            Fields(element, "type", "frames", "frameDurationMs");
            var frames = element.GetProperty("frames");
            if (frames.ValueKind != JsonValueKind.Array || frames.GetArrayLength() is < 1 or > 240) throw Invalid();
            var files = frames.EnumerateArray().Select(frame => AssetReference(Text(frame), ".png")).ToArray();
            return new(type, files, Integer(element.GetProperty("frameDurationMs"), 16, 1000), DurationDeclared: true);
        }
        throw Invalid();
    }
```

注意 `Fields` 是"精确集合相等"语义：`Fields(element, expected)` 中的 `expected` 必须与 JSON 实际字段完全一致，因此 `png` 附加动作必须写 `frameDurationMs`（Step 1 的 `Rejects_a_single_frame_png_extra_without_a_declared_duration` 依赖这一条），GIF 附加动作可写可不写。

- [ ] **Step 4: 实现库 v2 读取模型与写入**

`pet-helper/CharacterAssetSource.cs`：

```csharp
internal sealed record StoredCharacterClip(string[] Frames, int[] Durations);
internal sealed record StoredCharacterExtra(string Name, string[] Frames, int[] Durations);
internal sealed record StoredCharacterState(StoredCharacterClip Primary, StoredCharacterExtra[] Extras);
internal sealed record StoredCharacter(int LibraryFormatVersion, string Name, PetStatusAnchor StatusAnchor,
    double Baseline, int ExtrasCooldownMs, Dictionary<string, StoredCharacterState> Actions);
```

`CharacterAssetSource` 构造函数改为把 extras 带进 program：

```csharp
        references = document.Actions.Values
            .SelectMany(state => state.Primary.Frames.Concat(state.Extras.SelectMany(extra => extra.Frames)))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (key, value) in document.Actions)
        {
            var animationKey = CharacterManifest.Keys[key];
            var primary = ToClip(animationKey, $"{id}-{key}-primary", value.Primary, document.StatusAnchor);
            var extras = value.Extras
                .Select((extra, index) => ToClip(animationKey, $"{id}-{key}-extra-{index}",
                    new StoredCharacterClip(extra.Frames, extra.Durations), document.StatusAnchor) with { Label = extra.Name })
                .ToImmutableArray();
            programs.Add(animationKey, new(animationKey, [], [primary], [], false)
            {
                Extras = extras,
                ExtrasCooldownMs = document.ExtrasCooldownMs,
            });
        }
    }

    private static ResolvedClip ToClip(PetAnimationKey key, string id, StoredCharacterClip clip, PetStatusAnchor anchor) =>
        new(key, id, clip.Frames.ToImmutableArray(), clip.Durations[0], PetClipPlaybackMode.Loop, anchor)
        {
            FrameDurationsMs = clip.Durations.ToImmutableArray(),
        };
```

`ParseStored` 全文替换为：

```csharp
    internal static StoredCharacter ParseStored(string json)
    {
        using var document = CharacterManifest.ReadJson(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw CharacterManifest.Invalid();
        var required = new[] { "libraryFormatVersion", "name", "statusAnchor", "baseline", "actions" };
        var version = CharacterManifest.Integer(root.GetProperty("libraryFormatVersion"), 1, 2);
        var allowed = version == 2
            ? required.Append("extrasCooldownMs").ToHashSet(StringComparer.Ordinal)
            : required.ToHashSet(StringComparer.Ordinal);
        var seen = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!seen.IsSubsetOf(allowed) || required.Any(name => !seen.Contains(name))) throw CharacterManifest.Invalid();
        var cooldownMs = version == 2 && root.TryGetProperty("extrasCooldownMs", out var cooldownElement)
            ? CharacterManifest.Integer(cooldownElement, 5000, 600000)
            : CharacterManifest.DefaultExtrasCooldownMs;

        var actions = new Dictionary<string, StoredCharacterState>(StringComparer.Ordinal);
        var input = root.GetProperty("actions");
        if (input.ValueKind != JsonValueKind.Object) throw CharacterManifest.Invalid();
        var total = 0;
        foreach (var entry in input.EnumerateObject())
        {
            if (!CharacterManifest.Keys.ContainsKey(entry.Name)) throw CharacterManifest.Invalid();
            if (version == 1)
            {
                actions.Add(entry.Name, new(ParseStoredClip(entry.Value, $"frames/{entry.Name}/", ref total), []));
                continue;
            }

            CharacterManifest.Fields(entry.Value, "primary", "extras");
            var primary = ParseStoredClip(entry.Value.GetProperty("primary"), $"frames/{entry.Name}/primary/", ref total);
            var extrasElement = entry.Value.GetProperty("extras");
            if (extrasElement.ValueKind != JsonValueKind.Array ||
                extrasElement.GetArrayLength() > CharacterManifest.MaximumExtrasPerState) throw CharacterManifest.Invalid();
            var extras = new List<StoredCharacterExtra>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < extrasElement.GetArrayLength(); index++)
            {
                var extraElement = extrasElement[index];
                CharacterManifest.Fields(extraElement, "name", "frames", "durations");
                var name = CharacterManifest.ValidateName(CharacterManifest.Text(extraElement.GetProperty("name")));
                if (!names.Add(name)) throw CharacterManifest.Invalid();
                var clip = ParseStoredClip(extraElement, $"frames/{entry.Name}/extra-{index}/", ref total);
                extras.Add(new(name, clip.Frames, clip.Durations));
            }
            actions.Add(entry.Name, new(primary, extras.ToArray()));
        }
        if (!actions.ContainsKey("idle")) throw CharacterManifest.Invalid();
        return new(version, CharacterManifest.ValidateName(CharacterManifest.Text(root.GetProperty("name"))),
            CharacterManifest.Anchor(root.GetProperty("statusAnchor")), CharacterManifest.Unit(root.GetProperty("baseline")),
            cooldownMs, actions);
    }

    private static StoredCharacterClip ParseStoredClip(JsonElement element, string expectedPrefix, ref int total)
    {
        CharacterManifest.Fields(element, "frames", "durations");
        var frameElements = element.GetProperty("frames");
        var delayElements = element.GetProperty("durations");
        if (frameElements.ValueKind != JsonValueKind.Array || delayElements.ValueKind != JsonValueKind.Array ||
            frameElements.GetArrayLength() is < 1 or > 240 ||
            frameElements.GetArrayLength() != delayElements.GetArrayLength()) throw CharacterManifest.Invalid();
        var frames = frameElements.EnumerateArray().Select(CharacterManifest.Text).ToArray();
        for (var i = 0; i < frames.Length; i++)
            if (frames[i] != $"{expectedPrefix}{i:D4}.png") throw CharacterManifest.Invalid();
        total += frames.Length;
        if (total > 1024) throw CharacterManifest.Invalid();
        return new(frames, delayElements.EnumerateArray().Select(d => CharacterManifest.Integer(d, 16, 10000)).ToArray());
    }
```

先做一处**行为等价**的重构，让规范化目标画布可以由调用方指定（`pet-helper/GifFrameImporter.cs`）：

```csharp
    internal static void Decode(byte[] bytes, bool gif, int pngDelay, CharacterImportBudget budget,
        CancellationToken cancellation, Action<BitmapSource, int> accept) =>
        Decode(bytes, gif, pngDelay, budget, cancellation, canvasSide: null, accept);

    internal static void Decode(byte[] bytes, bool gif, int pngDelay, CharacterImportBudget budget,
        CancellationToken cancellation, int? canvasSide, Action<BitmapSource, int> accept)
```

方法体内两处 `Normalize(...)` 调用改为 `Normalize(canvas, layout.Width, layout.Height, canvasSide)` 与 `Normalize(pixels, width, height, canvasSide)`，并把 `Normalize` 改成：

```csharp
    private static BitmapSource Normalize(byte[] pixels, int width, int height, int? canvasSide)
    {
        var side = Math.Max(width, height);
        var outputSide = canvasSide ?? Math.Min(side, 512);
        if (outputSide is < 1 or > 512) throw CharacterManifest.Invalid();
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var factor = Math.Min(1d, outputSide / (double)side);
        BitmapSource scaled = factor < 1 ? new TransformedBitmap(source, new ScaleTransform(factor, factor)) : source;
        var target = new byte[outputSide * outputSide * 4];
        var small = new byte[scaled.PixelWidth * scaled.PixelHeight * 4];
        scaled.CopyPixels(small, scaled.PixelWidth * 4, 0);
        var left = (outputSide - scaled.PixelWidth) / 2;
        var top = outputSide - scaled.PixelHeight;
        for (var y = 0; y < scaled.PixelHeight; y++)
            Buffer.BlockCopy(small, y * scaled.PixelWidth * 4, target, ((y + top) * outputSide + left) * 4, scaled.PixelWidth * 4);
        var result = BitmapSource.Create(outputSide, outputSide, 96, 96, PixelFormats.Bgra32, null, target, outputSide * 4);
        result.Freeze();
        return result;
    }
```

（不传 `canvasSide` 时 `outputSide = min(side, 512)`、`factor = min(1, outputSide / side)`，与今天逐像素一致，`GifFrameImporterTests` 必须保持全绿。）

然后 `pet-helper/CharacterLibrary.cs` 的 `Prepare` 循环体改为写出 `primary` 与 `extra-N`：

```csharp
            long outputBytes = 0;
            foreach (var (key, state) in manifest.Actions)
            {
                var primary = WriteActionFrames(draft.DirectoryPath, key, "primary", state.Primary, isExtra: false,
                    budget, canvasSide: null, cancellation, ref outputBytes, existingBytes);
                var extras = new List<StoredCharacterExtra>();
                for (var index = 0; index < state.Extras.Length; index++)
                {
                    var clip = WriteActionFrames(draft.DirectoryPath, key, $"extra-{index}", state.Extras[index].Action,
                        isExtra: true, budget, canvasSide: null, cancellation, ref outputBytes, existingBytes);
                    extras.Add(new(state.Extras[index].Name, clip.Frames, clip.Durations));
                }
                actions.Add(key, new(primary, extras.ToArray()));
            }
            var side = Math.Max(budget.Width, budget.Height);
            var anchor = new PetStatusAnchor((side - budget.Width) / (2d * side) + manifest.StatusAnchor.X * budget.Width / side,
                (side - budget.Height + manifest.StatusAnchor.Y * budget.Height) / side);
            var baseline = (side - budget.Height + manifest.Baseline * budget.Height) / side;
            draft.Document = new(2, manifest.Name, anchor, baseline, manifest.ExtrasCooldownMs, actions);
```

并新增 `WriteActionFrames`（把原内层解码循环整体搬进来，加上单帧附加动作校验）：

```csharp
    private static StoredCharacterClip WriteActionFrames(string characterDirectory, string key, string folder,
        CharacterAction action, bool isExtra, CharacterImportBudget budget, int? canvasSide,
        CancellationToken cancellation, ref long outputBytes, long existingBytes)
    {
        var references = new List<string>();
        var delays = new List<int>();
        var folderPath = $"frames/{key}/{folder}";
        Directory.CreateDirectory(CharacterFiles.Child(characterDirectory, folderPath));
        foreach (var file in action.Files)
        {
            cancellation.ThrowIfCancellationRequested();
            GifFrameImporter.Decode(CharacterFiles.Read(file, 20 * 1024 * 1024), action.Type == "gif", action.FrameDurationMs,
                budget, cancellation, canvasSide, (bitmap, delay) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (references.Count >= 240) throw CharacterManifest.Invalid();
                var reference = $"{folderPath}/{references.Count:D4}.png";
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var memory = new MemoryStream();
                encoder.Save(memory);
                var bytes = memory.ToArray();
                outputBytes += bytes.Length;
                if (outputBytes > 256L * 1024 * 1024 || existingBytes + outputBytes + 65536 > LibraryLimit)
                    throw CharacterManifest.Invalid();
                CharacterFiles.WriteNew(CharacterFiles.Child(characterDirectory, reference), bytes);
                references.Add(reference);
                delays.Add(delay);
            });
        }
        if (isExtra && references.Count == 1 && !action.DurationDeclared) throw CharacterManifest.Invalid();
        return new(references.ToArray(), delays.ToArray());
    }
```

注意 `Prepare` 里原来直接调用 `CharacterFiles.Read(file, ...)`；现在读取移进 `WriteActionFrames`，`manifest.Actions` 的键值类型变为 `CharacterStateActions`。

`Commit` 改为直接序列化记录（不再手写匿名字段顺序）：

```csharp
        var document = draft.Document with { Name = CharacterManifest.ValidateName(name), StatusAnchor = anchor, Baseline = baseline };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterLibraryTests"`
Expected: PASS。既有 `Imports_a_copy_and_restores_selection_without_retaining_source_names` 与 `Rejects_tampered_stored_references_and_corrupt_selection` 需同步改断言：v1 用例里的 `characterFormatVersion` 保持不变，但 `Rejects_tampered_stored_references_and_corrupt_selection` 里的路径替换字符串要改成 `frames/idle/primary/0000.png`（新导入写 v2 布局）。

- [ ] **Step 6: 提交**

```powershell
git add pet-helper/CharacterManifest.cs pet-helper/CharacterAssetSource.cs pet-helper/CharacterLibrary.cs pet-helper.Tests/CharacterLibraryTests.cs
git commit -m "feat: store imported characters with primary and extra actions"
```

---

### Task 6: 旧格式兼容（来源 v1 / 库 v1 / 既有断言修正）

**Files:**
- Modify: `pet-helper.Tests/CharacterLibraryTests.cs`
- Modify: `pet-helper.Tests/CharacterPlayerTests.cs`（若引用了旧布局帧路径）
- Modify: `pet-helper.Tests/CharacterClipTimingTests.cs`（同上）

- [ ] **Step 1: 修正既有断言**

`CharacterLibraryTests.Rejects_unknown_fields_duplicate_keys_and_missing_idle` 中

```csharp
        Assert.Throws<FormatException>(() => CharacterManifest.Parse(valid.Replace("Version\":1", "Version\":2")));
```

改为

```csharp
        Assert.Throws<FormatException>(() => CharacterManifest.Parse(valid.Replace("Version\":1", "Version\":3")));
```

（`characterFormatVersion: 2` 现在合法，越界版本仍必须被拒绝。）

- [ ] **Step 2: 写失败测试**

在 `CharacterLibraryTests` 追加：

```csharp
    [Fact]
    public void Reads_a_version_one_library_as_a_primary_only_character()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "input.gif"), TinyGif());
        var location = Path.Combine(root, "output");
        var library = new CharacterLibrary(location);
        using (var draft = library.PrepareImage(input: Path.Combine(root, "input.gif"), CancellationToken.None))
        {
            library.Commit(draft, "旧库", new(.5, .1), .95);
        }

        // Rewrite the freshly imported character as a version one library to emulate an installed v0.2.11 entry.
        var id = library.List()[0].Id;
        var directory = Path.Combine(location, "library", id);
        Directory.CreateDirectory(Path.Combine(directory, "frames", "idle"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory, "frames", "idle", "primary")))
            File.Move(file, Path.Combine(directory, "frames", "idle", Path.GetFileName(file)));
        Directory.Delete(Path.Combine(directory, "frames", "idle", "primary"));
        File.WriteAllText(Path.Combine(directory, "character.json"),
            """{"libraryFormatVersion":1,"name":"旧库","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,"actions":{"idle":{"frames":["frames/idle/0000.png"],"durations":[100]}}}""");

        var source = library.Load(id);
        var program = source.ResolveProgram(PetAnimationKey.Idle, _ => true);

        Assert.Single(program.Loop);
        Assert.Empty(program.Extras);
        Assert.Equal(30000, program.ExtrasCooldownMs);
        Assert.Equal("frames/idle/0000.png", program.Loop[0].Frames[0]);
    }

    [Fact]
    public void Still_imports_a_version_one_source_directory()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "idle.gif"), TinyGif());
        File.WriteAllText(Path.Combine(root, "character.json"), """
            {"characterFormatVersion":1,"name":"旧来源","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,
             "actions":{"idle":{"type":"gif","file":"idle.gif"}}}
            """);
        var library = new CharacterLibrary(Path.Combine(root, "output"));

        using var draft = library.PrepareDirectory(root, CancellationToken.None);
        var info = library.Commit(draft, "旧来源", new(.5, .1), .95);

        Assert.Single(library.Load(info.Id).ResolveProgram(PetAnimationKey.Idle, _ => true).Loop);
    }
```

- [ ] **Step 3: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterLibraryTests|FullyQualifiedName~CharacterPlayerTests|FullyQualifiedName~CharacterClipTimingTests"`
Expected: PASS。若 `CharacterPlayerTests` / `CharacterClipTimingTests` 里硬编码了 `frames/idle/0000.png`，把它们改成 `frames/idle/primary/0000.png`。

- [ ] **Step 4: 提交**

```powershell
git add pet-helper.Tests
git commit -m "test: keep version one characters and sources importable"
```

---

### Task 7: 动作目录与"预览指定动作" API

**Files:**
- Create: `pet-helper/PetActionChoice.cs`
- Modify: `pet-helper/PetAnimationManifest.cs`
- Modify: `pet-helper/CharacterAssetSource.cs`
- Modify: `pet-helper/PetStateAnimationCoordinator.cs`
- Modify: `pet-helper/PetAnimationPlayer.cs`
- Create: `pet-helper.Tests/PetActionCatalogTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `pet-helper.Tests/PetActionCatalogTests.cs`：

```csharp
using System.IO;
using System.Windows.Controls;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class PetActionCatalogTests
{
    [Fact]
    public void The_manifest_catalog_lists_the_primary_loop_before_the_extras()
    {
        var manifest = AnimationManifestTestData.ParseIdle(5, PetExtrasPlaybackTests.IdleState);

        var catalog = manifest.ResolveActions(PetAnimationKey.Idle, _ => true);

        Assert.Equal(new[] { "呼吸", "伸懒腰", "打哈欠" }, catalog.Select(choice => choice.Label));
        Assert.Equal(new[] { false, true, true }, catalog.Select(choice => choice.IsExtra));
    }

    [Fact]
    public void The_external_catalog_lists_the_primary_and_the_named_extras()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsh-action-catalog-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Image? image = null;
            PetAnimationPlayer? player = null;
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
                image = new Image();
                player = new PetAnimationPlayer(image, library.Load(info.Id), preview: true);

                var catalog = player.ActionCatalog(PetAnimationKey.Idle);
                Assert.Equal(new[] { "默认动作", "伸懒腰" }, catalog.Select(choice => choice.Label));
                Assert.Equal(new[] { false, true }, catalog.Select(choice => choice.IsExtra));

                player.PreviewAction(PetAnimationKey.Idle, 1, reducedMotion: false);
                Assert.True(player.IsTimerRunning);
                Assert.Equal("frames/idle/extra-0/0000.png", catalog[1].Clip.Frames[0]);

                // A one-frame preview restarts instead of finishing, so the window keeps showing it.
                player.AdvanceFrame();
                Assert.True(player.IsTimerRunning);
                Assert.NotNull(image.Source);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                player?.Stop();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetActionCatalogTests"`
Expected: FAIL —— `PetActionChoice`、`ResolveActions`、`ActionCatalog`、`PreviewAction` 都不存在（编译错误）。

- [ ] **Step 3: 实现**

新建 `pet-helper/PetActionChoice.cs`：

```csharp
namespace PetHelper;

/// <summary>
/// One previewable action of a single state: its local display name, whether it is a one-shot extra,
/// and the resolved clip.  Display names never leave the Helper.
/// </summary>
public sealed record PetActionChoice(string Label, bool IsExtra, ResolvedClip Clip);
```

`pet-helper/PetAnimationManifest.cs` 增加：

```csharp
    /// <summary>Lists the primary loop clips followed by the one-shot extras of the resolved state.</summary>
    public ImmutableArray<PetActionChoice> ResolveActions(PetAnimationKey requested, Func<string, bool> isFrameAvailable)
    {
        var program = ResolveProgram(requested, isFrameAvailable);
        return program.Loop
            .Select(clip => new PetActionChoice(clip.Label ?? clip.Id, false, clip))
            .Concat(program.Extras.Select(clip => new PetActionChoice(clip.Label ?? clip.Id, true, clip)))
            .ToImmutableArray();
    }
```

`pet-helper/CharacterAssetSource.cs` 增加（并给 extras 片段带上名字标签：在构造函数里把 `ToClip(...) with { Label = extra.Name }`）：

```csharp
    internal ImmutableArray<PetActionChoice> ResolveActions(PetAnimationKey key, Func<string, bool> available)
    {
        var program = ResolveProgram(key, available);
        return program.Loop
            .Select(clip => new PetActionChoice("默认动作", false, clip))
            .Concat(program.Extras.Select(clip => new PetActionChoice(clip.Label ?? "附加动作", true, clip)))
            .ToImmutableArray();
    }
```

`pet-helper/PetStateAnimationCoordinator.cs` 增加预览模式：

```csharp
    private bool previewing;

    /// <summary>Plays one explicit clip on repeat for the character window preview.</summary>
    internal void Preview(ResolvedClip clip, bool reducedMotion)
    {
        ArgumentNullException.ThrowIfNull(clip);
        previewing = true;
        currentProgram = new ResolvedStateProgram(clip.Key, [], [clip], [], false);
        currentTransition = null;
        transitionAfterEnter = null;
        requested = clip.Key;
        this.reducedMotion = reducedMotion;
        currentExtra = null;
        extraElapsedMs = 0;
        finished = false;
        phase = AnimationPhase.Looping;
        clipIndex = 0;
        StartClip(clip);
    }
```

并在 `Advance` 的完成分支最前面插入预览重启：

```csharp
        if (clipCompleted)
        {
            clipCompleted = false;
            if (previewing && currentClip is { } clip)
            {
                StartClip(clip);
                return;
            }
```

`pet-helper/PetAnimationPlayer.cs`：

```csharp
    private readonly PetAnimationManifest? manifest;
```

构造函数里改为先取清单再建播放器：

```csharp
        try
        {
            manifest = characterSource is null ? LoadManifest(manifestStreamReader, manifestReaderFactory) : null;
            playback = manifest is not null
                ? new PetStateAnimationCoordinator(manifest, IsFrameAvailable)
                : new PetStateAnimationCoordinator(characterSource!.ResolveProgram, IsFrameAvailable);
            playback.Completed += Playback_Completed;
        }
        catch (InvalidOperationException)
        {
            ActivateStaticFallback();
        }
```

新增两个成员（放在 `Apply` 之后）：

```csharp
    /// <summary>Lists the previewable actions of a state; the live pet keeps using Apply.</summary>
    internal IReadOnlyList<PetActionChoice> ActionCatalog(PetAnimationKey key)
    {
        try
        {
            if (characterSource is not null) return characterSource.ResolveActions(key, IsFrameAvailable);
            return manifest is null ? [] : manifest.ResolveActions(key, IsFrameAvailable);
        }
        catch { return []; }
    }

    /// <summary>Plays one catalogued action on repeat for the character window preview.</summary>
    internal void PreviewAction(PetAnimationKey key, int index, bool reducedMotion)
    {
        if (playback is null) return;
        var catalog = ActionCatalog(key);
        if (index < 0 || index >= catalog.Count) return;
        try
        {
            playback.Preview(catalog[index].Clip, reducedMotion);
            UpdateImage();
            if (playback.IsAnimating)
            {
                timer.Interval = TimeSpan.FromMilliseconds(playback.IntervalMs);
                timer.Start();
                return;
            }
            timer.Stop();
        }
        catch { ActivateStaticFallback(); }
    }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~PetActionCatalogTests"`
Expected: PASS。

- [ ] **Step 5: 提交**

```powershell
git add pet-helper/PetActionChoice.cs pet-helper/PetAnimationManifest.cs pet-helper/CharacterAssetSource.cs pet-helper/PetStateAnimationCoordinator.cs pet-helper/PetAnimationPlayer.cs pet-helper.Tests/PetActionCatalogTests.cs
git commit -m "feat: expose per-state action catalogs and clip preview"
```

---

### Task 8: 给已导入人物追加动作（含 v1 → v2 就地升级）

**Files:**
- Modify: `pet-helper/GifFrameImporter.cs`
- Modify: `pet-helper/CharacterLibrary.cs`
- Create: `pet-helper.Tests/CharacterActionDraftTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `pet-helper.Tests/CharacterActionDraftTests.cs`：

```csharp
using System.IO;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class CharacterActionDraftTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dsh-action-draft-" + Guid.NewGuid().ToString("N"));

    private (CharacterLibrary Library, string Id) CreateCharacter()
    {
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "source.gif");
        File.WriteAllBytes(input, CharacterLibraryTests.TinyGif());
        var library = new CharacterLibrary(Path.Combine(root, "output"));
        using var draft = library.PrepareImage(input, CancellationToken.None);
        var info = library.Commit(draft, "维维美", new(.5, .1), .95);
        return (library, info.Id);
    }

    private string CharacterDirectory(string id) => Path.Combine(root, "output", "library", id);

    [Fact]
    public void Adding_an_extra_keeps_the_primary_action_and_writes_the_extra_folder()
    {
        var (library, id) = CreateCharacter();
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), CharacterLibraryTests.TinyPng());

        var info = library.AddExtra(id, Path.Combine(root, "stretch.png"), "idle", "伸懒腰", new(.5, .1), .95, CancellationToken.None);

        var program = library.Load(info.Id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Single(program.Loop);
        Assert.Single(program.Extras);
        Assert.Equal("伸懒腰", program.Extras[0].Label);
        Assert.Equal(1500, program.Extras[0].FrameDurationsMs[0]);
        Assert.True(File.Exists(Path.Combine(CharacterDirectory(id), "frames", "idle", "extra-0", "0000.png")));
        Assert.True(File.Exists(Path.Combine(CharacterDirectory(id), "frames", "idle", "primary", "0000.png")));
        Assert.Contains("\"libraryFormatVersion\":2",
            File.ReadAllText(Path.Combine(CharacterDirectory(id), "character.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_an_extra_upgrades_a_version_one_library_in_place()
    {
        var (library, id) = CreateCharacter();
        var directory = CharacterDirectory(id);
        // Emulate an installed v0.2.11 entry: flat frames plus a version one manifest.
        Directory.CreateDirectory(Path.Combine(directory, "frames", "idle"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory, "frames", "idle", "primary")))
            File.Move(file, Path.Combine(directory, "frames", "idle", Path.GetFileName(file)));
        Directory.Delete(Path.Combine(directory, "frames", "idle", "primary"));
        File.WriteAllText(Path.Combine(directory, "character.json"),
            """{"libraryFormatVersion":1,"name":"维维美","statusAnchor":{"x":0.5,"y":0.1},"baseline":0.95,"actions":{"idle":{"frames":["frames/idle/0000.png"],"durations":[100]}}}""");
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), CharacterLibraryTests.TinyPng());

        library.AddExtra(id, Path.Combine(root, "stretch.png"), "idle", "伸懒腰", new(.5, .1), .95, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(directory, "frames", "idle", "0000.png")));
        Assert.True(File.Exists(Path.Combine(directory, "frames", "idle", "primary", "0000.png")));
        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Equal("frames/idle/primary/0000.png", program.Loop[0].Frames[0]);
        Assert.Single(program.Extras);
    }

    [Fact]
    public void A_failed_commit_leaves_the_existing_character_untouched()
    {
        var (library, id) = CreateCharacter();
        File.WriteAllBytes(Path.Combine(root, "stretch.png"), CharacterLibraryTests.TinyPng());
        var before = File.ReadAllText(Path.Combine(CharacterDirectory(id), "character.json"));

        // A duplicate extra name is rejected while committing.
        library.AddExtra(id, Path.Combine(root, "stretch.png"), "idle", "伸懒腰", new(.5, .1), .95, CancellationToken.None);
        Assert.Throws<FormatException>(() =>
            library.AddExtra(id, Path.Combine(root, "stretch.png"), "idle", "伸懒腰", new(.5, .1), .95, CancellationToken.None));

        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Single(program.Extras);
        Assert.True(File.Exists(Path.Combine(CharacterDirectory(id), "character.json")));
        Assert.NotEqual(before, File.ReadAllText(Path.Combine(CharacterDirectory(id), "character.json")));
    }

    [Fact]
    public void Adding_an_extra_never_upscales_beyond_the_existing_canvas()
    {
        var (library, id) = CreateCharacter();
        File.WriteAllBytes(Path.Combine(root, "small.png"), CharacterLibraryTests.TinyPng());
        library.AddExtra(id, Path.Combine(root, "small.png"), "idle", "小动作", new(.5, .1), .95, CancellationToken.None);

        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Equal(1, GifFrameImporterTests.PngSide(
            Path.Combine(CharacterDirectory(id), program.Extras[0].Frames[0])));
        Assert.Equal(1, GifFrameImporterTests.PngSide(
            Path.Combine(CharacterDirectory(id), program.Loop[0].Frames[0])));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
```

`GifFrameImporterTests` 里需要一个读 PNG 边长的助手（若已有同名方法就复用，否则新增）：

```csharp
    internal static int PngSide(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterActionDraftTests"`
Expected: FAIL —— `CharacterLibrary.AddExtra` 不存在（编译错误）。

- [ ] **Step 3: 确认画布参数已就位**

Task 5 已给 `GifFrameImporter.Decode` 加上 `int? canvasSide` 重载，并让 `WriteActionFrames` 接受同名参数。本任务用 `canvasSide` 把新动作规范化到**该人物现有画布边长**（由 `CanvasSide` 从任一现有帧的 PNG 头读出；人物还没有任何帧时按 512 处理），因此不需要再改 `GifFrameImporter`。

- [ ] **Step 4: 实现 `PrepareAction`、`AddExtra` 与 `CommitAction`**

`pet-helper/CharacterLibrary.cs` 新增内部类型：

```csharp
internal sealed class CharacterActionDraft : IDisposable
{
    internal string CharacterId { get; }
    internal string StateKey { get; }
    internal bool AsPrimary { get; }
    internal string Folder { get; }
    internal StoredCharacterClip Clip { get; internal set; } = null!;
    internal string DirectoryPath { get; }
    internal bool Committed { get; set; }
    private readonly string stagingRoot;

    internal CharacterActionDraft(string characterId, string stateKey, bool asPrimary, string folder, string stagingRoot)
    {
        CharacterId = characterId;
        StateKey = stateKey;
        AsPrimary = asPrimary;
        Folder = folder;
        this.stagingRoot = stagingRoot;
        DirectoryPath = CharacterFiles.Child(stagingRoot, "build");
        Directory.CreateDirectory(DirectoryPath);
    }

    public void Dispose()
    {
        if (Committed) return;
        try { CharacterFiles.DeleteTree(stagingRoot, "build"); } catch { /* Cleanup never logs source paths. */ }
    }
}
```

`CharacterLibrary` 新增：

```csharp
    internal CharacterInfo AddExtra(string id, string file, string stateKey, string name, PetStatusAnchor anchor,
        double baseline, CancellationToken cancellation)
    {
        using var draft = PrepareAction(id, file, stateKey, asPrimary: false, cancellation);
        return CommitAction(draft, name, anchor, baseline);
    }

    /// <summary>Decodes one GIF or PNG into the staging directory, normalised to the character's existing canvas.</summary>
    internal CharacterActionDraft PrepareAction(string id, string file, string stateKey, bool asPrimary,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var gate = Lock();
        Id(id);
        if (!CharacterManifest.Keys.ContainsKey(stateKey)) throw CharacterManifest.Invalid();
        var document = Load(id).Document;
        var existing = document.Actions.TryGetValue(stateKey, out var state) ? state : null;
        if (!asPrimary && existing is not null && existing.Extras.Length >= CharacterManifest.MaximumExtrasPerState)
            throw CharacterManifest.Invalid();
        var directory = CharacterFiles.Child(LibraryPath, id);
        var canvasSide = CanvasSide(directory, document);
        var existingBytes = CharacterFiles.Size(root);
        var folder = asPrimary ? "primary" : $"extra-{existing?.Extras.Length ?? 0}";
        var stagingRoot = CharacterFiles.Child(StagingPath, "action-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        var draft = new CharacterActionDraft(id, stateKey, asPrimary, folder, stagingRoot);
        long draftOutputBytes = 0;
        try
        {
            var bytes = CharacterFiles.Read(file, 20 * 1024 * 1024);
            var gif = GifFrameImporter.IsGif(bytes);
            // A single static frame must carry its own display duration; 1500 ms is the dialog default.
            var action = new CharacterAction(gif ? "gif" : "png", [file], gif ? 100 : 1500, DurationDeclared: !gif);
            draft.Clip = WriteActionFrames(draft.DirectoryPath, stateKey, folder, action, isExtra: !asPrimary,
                new CharacterImportBudget(), canvasSide, cancellation, ref draftOutputBytes, existingBytes);
            return draft;
        }
        catch { draft.Dispose(); throw; }
    }
```

`WriteActionFrames` 就是 Task 5 里那个助手，本任务只是把 `canvasSide` 传成该人物现有画布边长、`isExtra` 传成 `!asPrimary`。

`CommitAction`：

```csharp
    internal CharacterInfo CommitAction(CharacterActionDraft draft, string? name, PetStatusAnchor anchor, double baseline)
    {
        using var gate = Lock();
        if (draft.Committed || !anchor.IsWithinArtboard || !double.IsFinite(baseline) || baseline is < 0 or > 1)
            throw CharacterManifest.Invalid();
        var id = Id(draft.CharacterId);
        var document = Load(id).Document;
        var states = new Dictionary<string, StoredCharacterState>(document.Actions, StringComparer.Ordinal);
        var current = states.TryGetValue(draft.StateKey, out var existing)
            ? existing
            : new StoredCharacterState(new StoredCharacterClip([], []), []);
        StoredCharacterState updated;
        if (draft.AsPrimary)
        {
            updated = new(draft.Clip, current.Extras);
        }
        else
        {
            var extraName = CharacterManifest.ValidateName(name ?? throw CharacterManifest.Invalid());
            if (current.Extras.Any(extra => string.Equals(extra.Name, extraName, StringComparison.Ordinal)))
                throw CharacterManifest.Invalid();
            updated = new(current.Primary, [.. current.Extras, new(extraName, draft.Clip.Frames, draft.Clip.Durations)]);
        }
        states[draft.StateKey] = updated;
        var next = document with
        {
            LibraryFormatVersion = 2,
            Name = name is null ? document.Name : CharacterManifest.ValidateName(name),
            StatusAnchor = anchor,
            Baseline = baseline,
            Actions = states,
        };
        CommitDirectory(id, document, next, draft, droppedReferences: []);
        draft.Committed = true;
        return new(id, next.Name);
    }
```

`CommitDirectory` 负责"搬家 + 换目录"：

```csharp
    private void CommitDirectory(string id, StoredCharacter previous, StoredCharacter next,
        CharacterActionDraft draft, IReadOnlySet<string> droppedReferences)
    {
        var target = CharacterFiles.Child(LibraryPath, id);
        var retired = CharacterFiles.Child(StagingPath, "retired-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.Move(target, retired);
            MoveExistingFrames(retired, draft.DirectoryPath, previous, droppedReferences);
            InstallDraftFrames(draft);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, JsonOptions);
            CharacterAssetSource.ParseStored(Encoding.UTF8.GetString(bytes));
            CharacterFiles.WriteNew(CharacterFiles.Child(draft.DirectoryPath, "character.json"), bytes);
            Directory.Move(draft.DirectoryPath, target);
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(retired)) Directory.Move(retired, target);
            throw;
        }
        finally
        {
            if (Directory.Exists(retired)) CharacterFiles.DeleteTree(StagingPath, Path.GetFileName(retired));
        }
    }

    private static void MoveExistingFrames(string retired, string building, StoredCharacter previous,
        IReadOnlySet<string> droppedReferences)
    {
        foreach (var (key, state) in previous.Actions)
        {
            MoveFrames(retired, building, state.Primary.Frames, key, droppedReferences);
            for (var index = 0; index < state.Extras.Length; index++)
                MoveFrames(retired, building, state.Extras[index].Frames, key, droppedReferences);
        }
    }

    private static void MoveFrames(string retired, string building, string[] frames, string key,
        IReadOnlySet<string> droppedReferences)
    {
        foreach (var reference in frames)
        {
            if (droppedReferences.Contains(reference)) continue;
            // Version one keeps frames directly under the state folder; version two adds primary/.
            var upgraded = reference.StartsWith($"frames/{key}/", StringComparison.Ordinal) &&
                !reference[$"frames/{key}/".Length..].Contains('/')
                ? $"frames/{key}/primary/{reference[$"frames/{key}/".Length..]}"
                : reference;
            var source = CharacterFiles.Child(retired, reference);
            var destination = CharacterFiles.Child(building, upgraded);
            if (!File.Exists(source)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, overwrite: false);
        }
    }

    private static void InstallDraftFrames(CharacterActionDraft draft)
    {
        var expected = CharacterFiles.Child(draft.DirectoryPath, $"frames/{draft.StateKey}/{draft.Folder}");
        Directory.CreateDirectory(expected);
        var actual = draft.Clip.Frames;
        for (var index = 0; index < actual.Length; index++)
        {
            var reference = $"frames/{draft.StateKey}/{draft.Folder}/{index:D4}.png";
            if (reference == actual[index]) continue;
            File.Move(CharacterFiles.Child(draft.DirectoryPath, actual[index]),
                CharacterFiles.Child(draft.DirectoryPath, reference));
        }
        draft.Clip = draft.Clip with { Frames = Enumerable.Range(0, actual.Length)
            .Select(index => $"frames/{draft.StateKey}/{draft.Folder}/{index:D4}.png").ToArray() };
    }

    private static int CanvasSide(string directory, StoredCharacter document)
    {
        foreach (var state in document.Actions.Values)
        {
            foreach (var reference in state.Primary.Frames.Concat(state.Extras.SelectMany(extra => extra.Frames)))
                return ReadPngSide(CharacterFiles.Child(directory, reference));
        }
        return 512;
    }

    private static int ReadPngSide(string path)
    {
        var bytes = CharacterFiles.Read(path, 2 * 1024 * 1024);
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw CharacterManifest.Invalid();
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width != height || width is < 1 or > 512) throw CharacterManifest.Invalid();
        return width;
    }
```

需要在 `CharacterLibrary.cs` 顶部 `using System.Buffers.Binary;`；`WriteActionFrames` 里的 `StoredCharacterClip.Frames` 是 `string[]`，因此 `with { Frames = ... }` 可用。

**已在使用中的角色**：`CommitAction` 成功返回后，调用方（窗口）必须立刻重新加载该人物，让运行中的播放器换成新的帧引用——见 Task 11 的 `Apply_Click`。

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterActionDraftTests"`
Expected: PASS。

- [ ] **Step 6: 提交**

```powershell
git add pet-helper/GifFrameImporter.cs pet-helper/CharacterLibrary.cs pet-helper.Tests/CharacterActionDraftTests.cs pet-helper.Tests/GifFrameImporterTests.cs
git commit -m "feat: add extra actions to an imported character"
```

---

### Task 9: 替换主动作与移除附加动作

**Files:**
- Modify: `pet-helper/CharacterLibrary.cs`
- Modify: `pet-helper.Tests/CharacterActionDraftTests.cs`

- [ ] **Step 1: 写失败测试**

在 `CharacterActionDraftTests` 追加：

```csharp
    [Fact]
    public void Replacing_the_primary_action_swaps_the_frames_of_that_state()
    {
        var (library, id) = CreateCharacter();
        var replacement = Path.Combine(root, "replacement.gif");
        File.WriteAllBytes(replacement, GifFrameImporterTests.Gif(1));

        library.ReplacePrimary(id, replacement, "idle", new(.5, .1), .95, CancellationToken.None);

        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Equal(new[] { 40, 250, 100 }, program.Loop[0].FrameDurationsMs);
        Assert.Empty(program.Extras);
        var primaryFolder = Path.Combine(CharacterDirectory(id), "frames", "idle", "primary");
        Assert.Equal(3, Directory.EnumerateFiles(primaryFolder, "*.png").Count());
    }

    [Fact]
    public void Removing_an_extra_renumbers_the_remaining_ones()
    {
        var (library, id) = CreateCharacter();
        File.WriteAllBytes(Path.Combine(root, "a.png"), CharacterLibraryTests.TinyPng());
        library.AddExtra(id, Path.Combine(root, "a.png"), "idle", "第一个", new(.5, .1), .95, CancellationToken.None);
        library.AddExtra(id, Path.Combine(root, "a.png"), "idle", "第二个", new(.5, .1), .95, CancellationToken.None);
        Assert.Equal(2, library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true).Extras.Length);

        library.RemoveExtra(id, "idle", "第一个");

        var program = library.Load(id).ResolveProgram(PetAnimationKey.Idle, _ => true);
        Assert.Single(program.Extras);
        Assert.Equal("第二个", program.Extras[0].Label);
        Assert.StartsWith("frames/idle/extra-0/", program.Extras[0].Frames[0], StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(CharacterDirectory(id), "frames", "idle", "extra-0", "0000.png")));
        Assert.False(Directory.Exists(Path.Combine(CharacterDirectory(id), "frames", "idle", "extra-1")));
    }

    [Fact]
    public void Removing_an_unknown_extra_is_rejected()
    {
        var (library, id) = CreateCharacter();

        Assert.Throws<FormatException>(() => library.RemoveExtra(id, "idle", "不存在"));
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterActionDraftTests"`
Expected: FAIL —— `ReplacePrimary` / `RemoveExtra` 不存在（编译错误）。

- [ ] **Step 3: 实现**

`pet-helper/CharacterLibrary.cs`：

1）把 Task 8 的 `CommitDirectory` / `MoveExistingFrames` / `MoveFrames` 扩展为支持"丢弃旧帧"与"重定位旧帧"：

```csharp
    private void CommitDirectory(string id, StoredCharacter previous, StoredCharacter next,
        CharacterActionDraft draft, IReadOnlySet<string> droppedReferences,
        IReadOnlyDictionary<string, string> relocatedReferences)
    {
        var target = CharacterFiles.Child(LibraryPath, id);
        var retired = CharacterFiles.Child(StagingPath, "retired-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.Move(target, retired);
            MoveExistingFrames(retired, draft.DirectoryPath, previous, droppedReferences, relocatedReferences);
            InstallDraftFrames(draft);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, JsonOptions);
            CharacterAssetSource.ParseStored(Encoding.UTF8.GetString(bytes));
            CharacterFiles.WriteNew(CharacterFiles.Child(draft.DirectoryPath, "character.json"), bytes);
            Directory.Move(draft.DirectoryPath, target);
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(retired)) Directory.Move(retired, target);
            throw;
        }
        finally
        {
            if (Directory.Exists(retired)) CharacterFiles.DeleteTree(StagingPath, Path.GetFileName(retired));
        }
    }

    private static void MoveExistingFrames(string retired, string building, StoredCharacter previous,
        IReadOnlySet<string> droppedReferences, IReadOnlyDictionary<string, string> relocatedReferences)
    {
        foreach (var (key, state) in previous.Actions)
        {
            MoveFrames(retired, building, state.Primary.Frames, key, droppedReferences, relocatedReferences);
            foreach (var extra in state.Extras)
                MoveFrames(retired, building, extra.Frames, key, droppedReferences, relocatedReferences);
        }
    }

    private static void MoveFrames(string retired, string building, string[] frames, string key,
        IReadOnlySet<string> droppedReferences, IReadOnlyDictionary<string, string> relocatedReferences)
    {
        foreach (var reference in frames)
        {
            if (droppedReferences.Contains(reference)) continue;
            var target = relocatedReferences.TryGetValue(reference, out var relocated)
                ? relocated
                : UpgradeReference(reference, key);
            var source = CharacterFiles.Child(retired, reference);
            var destination = CharacterFiles.Child(building, target);
            if (!File.Exists(source)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, overwrite: false);
        }
    }

    private static string UpgradeReference(string reference, string key)
    {
        var prefix = $"frames/{key}/";
        return reference.StartsWith(prefix, StringComparison.Ordinal) &&
            !reference[prefix.Length..].Contains('/')
            ? $"{prefix}primary/{reference[prefix.Length..]}"
            : reference;
    }
```

`CommitAction` 里计算 `droppedReferences`（替换主动作时丢弃旧主动作帧）并传入：

```csharp
        var dropped = draft.AsPrimary && states.TryGetValue(draft.StateKey, out var previousState) &&
            previousState.Primary.Frames.Length > 0
            ? previousState.Primary.Frames.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        states[draft.StateKey] = updated;
        ...
        CommitDirectory(id, document, next, draft, dropped, new Dictionary<string, string>(StringComparer.Ordinal));
```

2）新增"替换主动作"（复用 Task 8 的 `PrepareAction`，只把 `asPrimary` 传 `true`、`folder` 变成 `"primary"`）：

```csharp
    internal CharacterInfo ReplacePrimary(string id, string file, string stateKey, PetStatusAnchor anchor,
        double baseline, CancellationToken cancellation)
    {
        using var draft = PrepareAction(id, file, stateKey, asPrimary: true, cancellation);
        return CommitAction(draft, null, anchor, baseline);
    }
```

`PrepareAction(asPrimary: true)` 的 `folder` 为 `"primary"`；若该状态还没有任何帧（`state.Primary.Frames.Length == 0`）也允许，等于"给缺失状态补一个主动作"。

3）新增"移除附加动作"：

```csharp
    internal void RemoveExtra(string id, string stateKey, string name)
    {
        using var gate = Lock();
        Id(id);
        var document = Load(id).Document;
        if (!document.Actions.TryGetValue(stateKey, out var state) || state.Extras.Length == 0)
            throw CharacterManifest.Invalid();
        var index = Array.FindIndex(state.Extras, extra => string.Equals(extra.Name, name, StringComparison.Ordinal));
        if (index < 0) throw CharacterManifest.Invalid();
        var removed = state.Extras[index];
        var remaining = state.Extras.Where((_, position) => position != index).ToArray();
        var relocated = new Dictionary<string, string>(StringComparer.Ordinal);
        var rebuilt = new List<StoredCharacterExtra>();
        for (var position = 0; position < remaining.Length; position++)
        {
            var extra = remaining[position];
            if (position == index) continue;
            var frames = new string[extra.Frames.Length];
            for (var frame = 0; frame < extra.Frames.Length; frame++)
            {
                var target = $"frames/{stateKey}/extra-{position}/{frame:D4}.png";
                relocated[extra.Frames[frame]] = target;
                frames[frame] = target;
            }
            rebuilt.Add(new(extra.Name, frames, extra.Durations));
        }
        var states = new Dictionary<string, StoredCharacterState>(document.Actions, StringComparer.Ordinal)
        {
            [stateKey] = new(state.Primary, rebuilt.ToArray()),
        };
        var next = document with { Actions = states };
        var dropped = removed.Frames.ToHashSet(StringComparer.Ordinal);
        var draft = new CharacterActionDraft(id, stateKey, asPrimary: false, "unused", CharacterFiles.Child(StagingPath, "action-" + Guid.NewGuid().ToString("N")));
        draft.Clip = new StoredCharacterClip([], []);
        try
        {
            draft.Committed = true;  // There is no draft action to install or clean up.
            CommitDirectory(id, document, next, draft, dropped, relocated);
        }
        finally { draft.Dispose(); }
    }
```

注意两点：`InstallDraftFrames` 对"没有新片段"的 draft 是空操作（`draft.Clip.Frames.Length == 0`），因此 `RemoveExtra` 可以复用它；`draft.Committed = true` 在 `CommitDirectory` **之前**设置，保证 `Dispose` 不删除已经被搬走的目录——因此 `CommitDirectory` 失败时要在 catch 里先删掉 `draft.DirectoryPath`：

```csharp
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(retired)) Directory.Move(retired, target);
            if (Directory.Exists(draft.DirectoryPath)) CharacterFiles.DeleteTree(StagingPath, Path.GetFileName(draft.DirectoryPath));
            throw;
        }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterActionDraftTests|FullyQualifiedName~CharacterLibraryTests"`
Expected: PASS。

- [ ] **Step 5: 提交**

```powershell
git add pet-helper/CharacterLibrary.cs pet-helper.Tests/CharacterActionDraftTests.cs
git commit -m "feat: replace and remove stored character actions"
```

---

### Task 10: 预览动作二级下拉

**Files:**
- Modify: `pet-helper/CharacterWindow.xaml`
- Modify: `pet-helper/CharacterWindow.xaml.cs`
- Modify: `pet-helper.Tests/CharacterWindowTests.cs`

- [ ] **Step 1: 写失败测试**

在 `pet-helper.Tests/CharacterWindowTests.cs` 追加一个新用例（沿用现有的 STA 线程写法）：

```csharp
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
```

并在既有用例 `Character_window_lays_out_import_controls_and_releases_preview_on_close` 里补三行（默认人物只有一个主动作）：

```csharp
                var actions = (ComboBox)window.FindName("PreviewAction");
                Assert.Single(actions.Items);
                Assert.False(actions.IsEnabled);
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterWindowTests"`
Expected: FAIL —— `window.FindName("PreviewAction")` 返回 `null`（`NullReferenceException`）。

- [ ] **Step 3: 实现**

`pet-helper/CharacterWindow.xaml` 的预览控制行改为：

```xml
          <StackPanel Orientation="Horizontal" Margin="0,10,0,8">
            <TextBlock Text="预览状态" VerticalAlignment="Center" Margin="0,0,10,0"/>
            <ComboBox x:Name="PreviewState" Width="120" SelectionChanged="PreviewState_Changed"/>
            <TextBlock Text="预览动作" VerticalAlignment="Center" Margin="14,0,10,0"/>
            <ComboBox x:Name="PreviewAction" Width="160" SelectionChanged="PreviewAction_Changed"/>
            <CheckBox x:Name="StaticPreview" Content="静态预览" Margin="14,0,0,0" VerticalAlignment="Center"
                      Checked="PreviewMotion_Changed" Unchecked="PreviewMotion_Changed"/>
          </StackPanel>
```

`pet-helper/CharacterWindow.xaml.cs`：

```csharp
    private sealed record StateChoice(PetAnimationKey Key, string Name) { public override string ToString() => Name; }
    private sealed record ActionChoice(PetActionChoice Choice, int Index)
    {
        public override string ToString() =>
            Choice.IsExtra ? $"{Choice.Label} · 附加" : $"{Choice.Label} · 主动作";
    }
```

把 `ApplyPreviewState` 拆成"重建动作列表"与"播放所选动作"：

```csharp
    private void PreviewState_Changed(object sender, SelectionChangedEventArgs e) => RebuildActionChoices();

    private void PreviewAction_Changed(object sender, SelectionChangedEventArgs e) => ApplyPreviewAction();

    private void PreviewMotion_Changed(object sender, RoutedEventArgs e) => ApplyPreviewAction();

    private void RebuildActionChoices()
    {
        if (closed || PreviewState?.SelectedItem is not StateChoice state || preview is null) return;
        var catalog = preview.ActionCatalog(state.Key);
        PreviewAction.ItemsSource = catalog.Select((choice, index) => new ActionChoice(choice, index)).ToArray();
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
```

`ShowPreview` 末尾把 `ApplyPreviewState();` 换成：

```csharp
        RebuildActionChoices();
```

并删除旧的 `ApplyPreviewState` 方法。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterWindowTests"`
Expected: PASS。

- [ ] **Step 5: 提交**

```powershell
git add pet-helper/CharacterWindow.xaml pet-helper/CharacterWindow.xaml.cs pet-helper.Tests/CharacterWindowTests.cs
git commit -m "feat: preview every action of a state in the character window"
```

---

### Task 11: 「添加动作…」UI

**Files:**
- Modify: `pet-helper/CharacterWindow.xaml`
- Modify: `pet-helper/CharacterWindow.xaml.cs`
- Modify: `pet-helper/CharacterLibrary.cs`
- Modify: `pet-helper.Tests/CharacterWindowTests.cs`

- [ ] **Step 1: 写失败测试**

在 `CharacterWindowTests` 追加：

```csharp
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
                // ShowPreview also records the previewed character id, which AddAction_Click uses.
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
```

注意：`AddAction_Click` 不能从 `Characters.SelectedItem` 的私有记录里取 `Id`（测试也无法伪造 `Entry`）。因此窗口新增一个字段 `previewCharacterId`，由 `ShowPreview(source)` 与 `Characters_SelectionChanged`（选中已导入人物时）一起维护；`AddAction_Click`、`PickActionSource_Click`、`RemoveAction_Click` 都只读这个字段。`previewSource is null || previewCharacterId is null` 就是"当前预览的是内置人物"，此时按钮只给提示。

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterWindowTests"`
Expected: FAIL —— 找不到 `AddActionButton` / `ActionSettings`（`NullReferenceException`）。

- [ ] **Step 3: 实现 XAML**

`pet-helper/CharacterWindow.xaml` 顶部按钮行加入：

```xml
      <Button x:Name="AddActionButton" Content="添加动作…" Click="AddAction_Click" />
```

右侧预览列在 `ImportSettings` 之前插入：

```xml
          <StackPanel x:Name="ActionSettings" Visibility="Collapsed">
            <TextBlock Text="目标状态" Margin="0,4,0,5"/>
            <ComboBox x:Name="ActionState" Width="180" HorizontalAlignment="Left" SelectionChanged="ActionSettings_Changed"/>
            <TextBlock Text="动作角色" Margin="0,10,0,5"/>
            <ComboBox x:Name="ActionRole" Width="180" HorizontalAlignment="Left" SelectionChanged="ActionSettings_Changed"/>
            <TextBlock Text="动作名称（附加动作必填）" Margin="0,10,0,5"/>
            <TextBox x:Name="ActionName" MaxLength="40"/>
            <Button x:Name="PickActionSourceButton" Content="选择素材…" Click="PickActionSource_Click" Margin="0,10,0,0"/>
            <TextBlock Text="该状态已有的附加动作" Margin="0,12,0,5"/>
            <ListBox x:Name="ActionExtras" Height="80" BorderBrush="#DEE4ED"/>
            <Button x:Name="RemoveActionButton" Content="移除所选附加动作" Click="RemoveAction_Click" Margin="0,8,0,0"/>
          </StackPanel>
```

- [ ] **Step 4: 实现代码**

`pet-helper/CharacterLibrary.cs` 的 `CharacterActionDraft` 增加预览源（窗口需要它来播放草稿动作）：

```csharp
    /// <summary>Builds a read-only source over the draft frames so the window can preview the new action.</summary>
    internal CharacterAssetSource PreviewSource(StoredCharacter current, string previewName)
    {
        var states = new Dictionary<string, StoredCharacterState>(current.Actions, StringComparer.Ordinal);
        var existing = states.TryGetValue(StateKey, out var state)
            ? state
            : new StoredCharacterState(new StoredCharacterClip([], []), []);
        states[StateKey] = AsPrimary
            ? new(Clip, existing.Extras)
            : new(existing.Primary, [.. existing.Extras, new(previewName, Clip.Frames, Clip.Durations)]);
        return new CharacterAssetSource(CharacterId, DirectoryPath, current with { LibraryFormatVersion = 2, Actions = states });
    }
```

`pet-helper/CharacterWindow.xaml.cs`：

1）状态选项带存储键（替换既有的 `StateChoice`）：

```csharp
    private static readonly (string Key, PetAnimationKey AnimationKey, string Name)[] States =
    [
        ("idle", PetAnimationKey.Idle, "待机"), ("thinking", PetAnimationKey.Thinking, "思考"),
        ("working", PetAnimationKey.Working, "工作"), ("thinking-working", PetAnimationKey.ThinkingWorking, "思考与工作"),
        ("responding", PetAnimationKey.Responding, "回复"), ("waiting", PetAnimationKey.Waiting, "等待操作"),
        ("question", PetAnimationKey.Question, "提问"), ("success", PetAnimationKey.Success, "成功"),
        ("error", PetAnimationKey.Error, "错误"), ("disconnected", PetAnimationKey.Disconnected, "未连接"),
    ];

    private sealed record StateChoice(string KeyName, PetAnimationKey Key, string Name) { public override string ToString() => Name; }
```

构造函数里：

```csharp
        PreviewState.ItemsSource = States.Select(state => new StateChoice(state.Key, state.AnimationKey, state.Name)).ToArray();
        PreviewState.SelectedIndex = 0;
        ActionState.ItemsSource = PreviewState.ItemsSource;
        ActionState.SelectedIndex = 0;
```

新增字段：

```csharp
    private CharacterActionDraft? actionDraft;
    private CharacterAssetSource? previewSource;
    private string? previewCharacterId;
    private string? actionSourcePath;
```

`ShowPreview` 里记录源：

```csharp
    internal void ShowPreview(CharacterAssetSource? source)
    {
        preview?.Stop();
        PreviewImage.Source = null;
        previewSource = source;
        previewCharacterId = source?.Id;
        preview = new PetAnimationPlayer(PreviewImage, source, preview: true);
        ...
    }
```

2）新增处理函数：

```csharp
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
        actionDraft?.Dispose();
        actionDraft = null;
        draft?.Dispose();
        draft = null;
        ImportSettings.Visibility = Visibility.Collapsed;
        ActionSettings.Visibility = Visibility.Visible;
        ApplyButton.Content = "保存到人物";
        RefreshActionOptions();
        ShowNotice("选择目标状态与动作角色，再点「选择素材…」。附加动作会在冷却后被完整插播一遍，然后回到主动作。");
    }

    private void ActionSettings_Changed(object sender, SelectionChangedEventArgs e) => RefreshActionOptions();

    private void RefreshActionOptions()
    {
        if (previewSource is null || ActionState.SelectedItem is not StateChoice state) return;
        var document = previewSource.Document;
        var hasState = document.Actions.TryGetValue(state.KeyName, out var stored);
        var primaryFrames = hasState ? stored!.Primary.Frames.Length : 0;
        var extraCount = hasState ? stored!.Extras.Length : 0;
        var roles = new List<string> { primaryFrames == 0 ? "主动作" : "替换主动作" };
        if (extraCount < CharacterManifest.MaximumExtrasPerState) roles.Add("附加动作");
        ActionRole.ItemsSource = roles;
        ActionRole.SelectedIndex = primaryFrames == 0 ? 0 : Math.Min(1, roles.Count - 1);
        ActionExtras.ItemsSource = hasState ? stored!.Extras.Select(extra => extra.Name).ToArray() : [];
        RemoveActionButton.IsEnabled = extraCount > 0;
        ActionName.IsEnabled = (ActionRole.SelectedItem as string) == "附加动作";
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
        actionSourcePath = dialog.FileName;
        await PrepareActionDraft();
    }

    private async Task PrepareActionDraft()
    {
        if (busy || previewCharacterId is null || actionSourcePath is null || ActionState.SelectedItem is not StateChoice state) return;
        var asPrimary = (ActionRole.SelectedItem as string) is "主动作" or "替换主动作";
        ++previewGeneration;
        SetBusy(true);
        importing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancelImportButton.Visibility = Visibility.Visible;
        ShowNotice("正在处理动作素材… 可以取消，当前桌宠继续运行。");
        CharacterActionDraft? next = null;
        try
        {
            next = await Task.Run(() => library.PrepareAction(previewCharacterId, actionSourcePath, state.KeyName, asPrimary, importing.Token), importing.Token);
            if (closed || importing.IsCancellationRequested) { next.Dispose(); return; }
            actionDraft?.Dispose();
            actionDraft = next;
            var name = ActionName.IsEnabled && ActionName.Text.Trim().Length > 0 ? ActionName.Text : "预览";
            preview?.Stop();
            PreviewImage.Source = null;
            previewSource = next.PreviewSource(previewSource!.Document, name);
            previewCharacterId = next.CharacterId;
            preview = new PetAnimationPlayer(PreviewImage, previewSource, preview: true);
            var target = asPrimary ? 0 : previewSource.Document.Actions[state.KeyName].Extras.Length - 1;
            preview.PreviewAction(state.Key, target, StaticPreview.IsChecked == true);
            PreviewTitle.Text = asPrimary ? "预览新主动作" : "预览新附加动作";
            ShowNotice("确认动作后点「保存到人物」。素材会按该人物现有画布等比缩放、底部对齐，不会放大。");
        }
        catch (OperationCanceledException) { next?.Dispose(); if (!closed) ShowNotice("已取消。"); }
        catch { next?.Dispose(); if (!closed) ShowNotice("动作素材无法使用，请检查格式、画布尺寸与帧数。"); }
        finally
        {
            importing.Dispose(); importing = null;
            if (!closed) { CancelImportButton.Visibility = Visibility.Collapsed; SetBusy(false); }
        }
    }

    private async void RemoveAction_Click(object sender, RoutedEventArgs e)
    {
        if (busy || previewCharacterId is null || ActionState.SelectedItem is not StateChoice state) return;
        if (ActionExtras.SelectedItem is not string name) return;
        if (MessageBox.Show(this, $"移除附加动作「{name}」？", "移除动作",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetBusy(true);
        try
        {
            await Task.Run(() => library.RemoveExtra(previewCharacterId, state.KeyName, name), lifetime.Token);
            SetBusy(false);
            await ReloadCharacter(previewCharacterId);
            ShowNotice("已移除附加动作。");
        }
        catch { if (!closed) ShowNotice("移除失败，请稍后重试。"); }
        finally { if (!closed) SetBusy(false); }
    }

    private async Task ReloadCharacter(string id)
    {
        var source = await Task.Run(() => library.Load(id), lifetime.Token);
        if (closed) return;
        ShowPreview(source);
        RefreshActionOptions();
        await useCharacter(id);   // Rebuild the running pet's frame references after a layout change.
    }
```

3）`Apply_Click` 增加动作草稿分支（放在现有 `if (draft is not null)` 之前）：

```csharp
            if (actionDraft is not null)
            {
                var pending = actionDraft;
                var extraName = ActionName.IsEnabled ? ActionName.Text : null;
                var actionAnchor = new PetStatusAnchor(AnchorX.Value, AnchorY.Value);
                var actionBaseline = Baseline.Value;
                preview?.Stop(); PreviewImage.Source = null;
                var info = await Task.Run(() => library.CommitAction(pending, extraName, actionAnchor, actionBaseline));
                actionDraft = null; draft = pending;
                ImportSettings.Visibility = Visibility.Collapsed;
                ActionSettings.Visibility = Visibility.Collapsed;
                ApplyButton.Content = "使用此人物";
                SetBusy(false);
                await ReloadCharacter(info.Id);
                ShowNotice("动作已保存，运行中的桌宠已重新加载这个人物。");
                return;
            }
```

（`draft = pending;` 只是让 `finally` 里的清理路径复用既有逻辑；`CommitAction` 成功后 `pending.Committed == true`，`Dispose` 不会删目录。）

4）`Closed` 处理里补上 `actionDraft?.Dispose();`。

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~CharacterWindowTests"`
Expected: PASS。

- [ ] **Step 6: 提交**

```powershell
git add pet-helper/CharacterWindow.xaml pet-helper/CharacterWindow.xaml.cs pet-helper/CharacterLibrary.cs pet-helper.Tests/CharacterWindowTests.cs
git commit -m "feat: add actions to an imported character from the character window"
```

---

### Task 12: 内置清单升级为 v5 并补中文 label

**Files:**
- Modify: `pet-helper/Assets/pet-animations.json`
- Modify: `pet-helper/Assets/Animations/{idle,thinking,working,responding,success,question}/animation.json`
- Create: `pet-helper.Tests/EmbeddedAnimationManifestTests.cs`

- [ ] **Step 1: 写失败测试**

新建 `pet-helper.Tests/EmbeddedAnimationManifestTests.cs`：

```csharp
using System.IO;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class EmbeddedAnimationManifestTests
{
    [Fact]
    public void The_shipped_manifest_is_version_five_and_labels_its_primary_actions()
    {
        var assembly = typeof(PetAnimationPlayer).Assembly;
        string Read(string name)
        {
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        var manifest = PetAnimationManifest.Parse(
            Read("PetHelper.Assets.pet-animations.json"),
            path => Read("PetHelper.Assets." + path.Replace('/', '.').Replace('-', '_')));

        Assert.Equal(new[] { "呼吸" }, manifest.ResolveActions(PetAnimationKey.Idle, _ => true).Select(choice => choice.Label));
        Assert.Equal(new[] { "思考" }, manifest.ResolveActions(PetAnimationKey.Thinking, _ => true).Select(choice => choice.Label));
        Assert.Equal(new[] { "搬运" }, manifest.ResolveActions(PetAnimationKey.Working, _ => true).Select(choice => choice.Label));
        Assert.Equal(new[] { "打字" }, manifest.ResolveActions(PetAnimationKey.Responding, _ => true).Select(choice => choice.Label));
        Assert.Equal(new[] { "等待" }, manifest.ResolveActions(PetAnimationKey.Question, _ => true).Select(choice => choice.Label));
        Assert.Empty(manifest.ResolveActions(PetAnimationKey.Idle, _ => true).Where(choice => choice.IsExtra));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~EmbeddedAnimationManifestTests"`
Expected: FAIL —— 内置清单仍是 `formatVersion: 4`，`label` 未声明，返回的是 `idle-breathe` 等原始 id。

- [ ] **Step 3: 迁移清单**

`pet-helper/Assets/pet-animations.json` 把 `"formatVersion": 4` 改成 `"formatVersion": 5`。

给下列片段各加一个 `label`（**只加字段，不动任何帧、`frameDurationMs`、`playback`、`statusAnchor`、`renderTransform`**）：

| 文件 | 片段 | `label` |
| --- | --- | --- |
| `Animations/idle/animation.json` | `breathe` | 呼吸 |
| `Animations/thinking/animation.json` | `ponder` | 思考 |
| `Animations/working/animation.json` | `haul` | 搬运 |
| `Animations/responding/animation.json` | `type` | 打字 |
| `Animations/success/animation.json` | `complete` | 完成 |
| `Animations/question/animation.json` | `raise-card` | 举牌 |
| `Animations/question/animation.json` | `hold-card` | 等待 |
| `Animations/question/animation.json` | `withdraw-card` | 收牌 |

`Animations/idle/animation.json` 迁移后应为：

```json
{
  "clips": {
    "breathe": {
      "frames": ["breathe/001.png", "…", "breathe/032.png"],
      "frameDurationMs": 125,
      "playback": "loop",
      "statusAnchor": { "x": 0.5, "y": 0.11 },
      "label": "呼吸"
    }
  }
}
```

本次**不声明任何 `extras`**：内置默认人物还没有附加动作素材（用户提供的 GIF 属于外置人物「维维美」）。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore --filter "FullyQualifiedName~EmbeddedAnimationManifestTests|FullyQualifiedName~PetAnimationManifestTests|FullyQualifiedName~PetAnimationPlayerTests"`
Expected: PASS（`PetAnimationPlayerTests` 会真实加载内嵌 PNG，确认迁移没破坏资源）。

- [ ] **Step 5: 提交**

```powershell
git add pet-helper/Assets pet-helper.Tests/EmbeddedAnimationManifestTests.cs
git commit -m "feat: move the built-in animation manifest to version five"
```

---

### Task 13: 全量验证与发布准备

**Files:**
- Modify: `package.json`、`package-lock.json`（版本号 +1，例如 0.2.11 → 0.2.12）

- [ ] **Step 1: 跑完整验证序列**

```powershell
npm test
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
npm run build:helper
npm run test:package
```

Expected: 四条命令全部成功；`dotnet test` 输出 `Failed: 0`。任何失败都回到对应 Task 修复，不要带病继续。

- [ ] **Step 2: 递增版本**

`package.json` 与 `package-lock.json` 的 `version` 同步改为 `0.2.12`（JSON Lines 仍是 v17，协议未变）。

- [ ] **Step 3: 打包**

先请用户从右键菜单关闭正在运行的桌宠（`runtime/bin/win32-x64/pet-helper.exe` 被占用会导致发布失败；只有用户明确同意才能结束进程），然后：

```powershell
npm run package:release
```

Expected: `dist\packages\dsh-png-pet-0.2.12.tgz` 生成，命令内部已跑过测试与自包含发布。

- [ ] **Step 4: 安装到 DSH 并重启**

```powershell
$latest = Get-ChildItem .\dist\packages\dsh-png-pet-*.tgz | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
New-Item -ItemType Directory -Force C:\dsh-packages | Out-Null
Copy-Item -LiteralPath $latest.FullName -Destination C:\dsh-packages\dsh-png-pet-current.tgz -Force
& C:\Users\root\AppData\Roaming\npm\dsh.cmd plugin --profile web remove dsh-png-pet
& C:\Users\root\AppData\Roaming\npm\dsh.cmd plugin --profile web add C:\dsh-packages\dsh-png-pet-current.tgz
& C:\Users\root\AppData\Roaming\npm\dsh.cmd plugin --profile web list
```

Expected: `plugin list` 显示 `dsh-png-pet@0.2.12`。随后由用户重启 DSH（已运行的 Harness 不会热加载新包）。

- [ ] **Step 5: 手工验收清单**

按顺序逐条确认，任何一条不成立就回到对应 Task：

1. 右键桌宠 →「人物形象…」→ 选中「维维美」→「添加动作…」→ 目标状态「待机」→ 角色「附加动作」→ 名称「坐着卖萌」→「选择素材…」→ 选 `C:\Users\root\Desktop\claudecode project\cb92af57bfbb28842bae8bd9c2fe800c3546941646440726.gif` → 预览能看到新动作在播 → 蓝点/橙线位置合理 →「保存到人物」。
2. 「预览动作」下拉里出现两项（「默认动作 · 主动作」「坐着卖萌 · 附加」），逐个选择都能循环播放；「静态预览」勾选时显示首帧。
3. 关掉人物形象窗口，让桌宠回到待机，静置约 30 秒：新动作插播一次，然后回到原来的待机动作；再等约 30 秒再次插播。
4. 插播过程中让 DSH 进入工作状态：动画立即切到工作动作；工作结束后回到待机并重新计时。
5. 打开设置里的减少动态效果：不再出现插播。
6. 切回「默认人物」：行为与升级前一致（举牌序列、成功动作、各状态动画）。
7. 真实素材检查：`%LOCALAPPDATA%\DshPngPet\Characters\library\617022720d01461682db80a9e35ca53b\` 现在是 `frames/idle/primary/` + `frames/idle/extra-0/`，`character.json` 为 `libraryFormatVersion: 2`，维维美的选择没有丢失。

- [ ] **Step 6: 提交**

```powershell
git add package.json package-lock.json
git commit -m "chore: release 0.2.12"
```

---

## 自审记录（写计划时对照 spec 逐条核对）

| spec 章节 | 落地任务 |
| --- | --- |
| §3.1 内置 v5：`extras`、`label`、隐式循环清单排除 extras、冷却上限、每状态 4 个 | Task 1、2、12 |
| §3.2 外置库 v2：`frames/<状态>/primary|extra-N/`、`extrasCooldownMs`、旧库可读、就地升级 | Task 5、6、8 |
| §3.3 来源 v2：`primary`+`extras`、`name` 必填且不重名、单帧必须声明时长、v1 兼容 | Task 5、6 |
| §3.4 内置本次不加帧素材 | Task 12（不声明 extras、不动 PNG） |
| §4 播放规则 1–10（冷却、随机、播完回主动作、重复消息不打断、切换放弃、减动态效果跳过、静态心跳、无 extras 行为不变） | Task 3、4 |
| §5「添加动作」：仅已导入人物、状态/角色/名称、替换主动作二次确认、移除、按现有画布规范化、就地升级与回滚 | Task 8、9、11 |
| §6 二级「预览动作」下拉、只有主动作时禁用、循环播放、静态预览 | Task 7、10 |
| §7 代码职责表 | Task 1–11 的 Files 列表逐项覆盖；`src/**` 未出现在任何任务中 |
| §8 兼容与安全边界（协议 v17 不变、动作名不出 Helper、配额与白名单沿用） | Task 5（配额与校验）、Task 11（名字只在本机 UI）、Task 13（协议未改动的回归验证） |
| §9 测试与验收 | 每个 Task 的测试步骤 + Task 13 的完整序列与手工清单 |
| §10 验收素材（GIF 规格与归属、维维美当前状态） | Task 13 Step 5 |

已知实现取舍（写在这里避免执行时误判）：

- 「附加动作必须完整播一遍」等于"帧序走完一次"：附加片段声明 `once`，单帧附加按其 `frameDurationMs` 展示。
- 预览里选中的动作是**循环播放**，运行时附加动作是"每冷却播一遍"；提示行会写清这一点。
- 「替换主动作」在同一状态下已有主动作时可用，保存时 UI 会先弹一次确认。
- `RemoveExtra` 会把剩余附加动作目录重编号为 `extra-0…extra-N-1`，以保持"数组下标 = 目录名"的校验规则。
- 就地升级会短暂移动人物目录（毫秒级），保存后窗口立刻 `useCharacter(id)` 重新加载，避免运行中的播放器持有旧帧引用。

---

## 执行方式

计划已写入 `docs/superpowers/plans/2026-09-12-random-idle-extras.md`。两种执行方式：

1. **Subagent-Driven（推荐）**：每个 Task 派一个全新 subagent，任务之间我来审阅，迭代快。
2. **Inline Execution**：在当前会话里按 `superpowers:executing-plans` 分批执行，带检查点。

选择后按对应技能执行；若中途发现 spec 与实现冲突，先回来改 spec 再继续。

