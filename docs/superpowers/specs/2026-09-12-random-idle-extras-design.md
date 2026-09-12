# 随机待机动作与同一状态多动作设计

状态：设计已由用户逐节确认（2026-09-12）。尚未实现；JSON Lines 保持 v17，Helper 版本待发布时递增。

## 1. 结论

内置默认人物与外置导入人物都支持**同一状态下的多个动作**：每个状态有一个持续循环的**主动作**，其余为**附加动作**。桌宠停在某状态时，主动作先正常循环；该状态已播放时长达到冷却（默认 30 秒）后，从该状态的附加动作里等概率随机抽一个**完整播一遍**，播完立即回到主动作并重新计时。

这解决当前的两个事实缺口：

- 内置清单 v4 里，没有 `program` 的状态会把该状态全部片段放进 `program.loop` 按顺序轮播，`question` 则用 `enter`/`loop`/`transitions` 描述序列；两者都无法表达"主力动作 + 偶尔插播的小动作"。
- 外置人物每个状态只有一个动作（`frames/<状态>/NNNN.png`），且预览窗口只有一级「预览状态」下拉，无法查看同状态的其它动作。

## 2. 目标与非目标

目标：

1. 内置清单新增 `extras` 声明，保留现有 `enter` / `loop` / `transitions` 语义不变；没有 `extras` 的状态行为与今天逐帧一致。
2. 外置来源清单升级为 `characterFormatVersion: 2`，用 `primary` + `extras` 显式声明；旧 v1 清单继续可用。
3. 人物库升级为 `libraryFormatVersion: 2`，新增 `frames/<状态>/<动作>/` 一级；旧 v1 布局继续可读。
4. 「人物形象」窗口新增二级「预览动作」下拉，并在已导入人物上支持**追加动作**（含把 v1 库就地升级为 v2）。
5. 用用户提供的 GIF 给已导入人物「维维美」加一个待机附加动作，完成真实验收。

非目标（本次不做）：

- 除"等概率随机"以外的抽取策略（权重、不紧邻重复）、跨启动记忆、不依赖冷却的固定周期换动作。
- 每个动作独立的 `statusAnchor`（外置人物继续使用人物级锚点；内置片段本来就自带锚点）。
- 给内置默认人物新增帧素材（详见 3.4）。
- 任何协议、DSH 事件、会话或对话行为变化。

## 3. 清单与文件夹设计

### 3.1 内置默认人物

内置动画的目录层级**不变**：`Animations/<状态>/<动作>/` 本来就是一级一动作，本次只改清单声明。

```text
pet-helper/Assets/pet-animations.json          → formatVersion: 5
pet-helper/Assets/Animations/idle/
  animation.json                               → clips + program.loop（主动作）+ extras（附加动作）
  breathe/001.png … 032.png                    → 现有主动作，二进制不变
  <新动作目录>/001.png …                        → 以后新增附加动作时放在这里
```

`Animations/idle/animation.json`（v5 形态示例，`extras` 与 `label` 均为新增）：

```json
{
  "clips": {
    "breathe": { "frames": ["breathe/001.png", "…"], "frameDurationMs": 125,
                 "playback": "loop", "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "呼吸" },
    "stretch": { "frames": ["stretch/001.png", "…"], "frameDurationMs": 100,
                 "playback": "once", "statusAnchor": { "x": 0.5, "y": 0.11 }, "label": "伸懒腰" }
  },
  "extras": { "clips": ["stretch"], "cooldownMs": 30000 }
}
```

规则：

- **主动作 = 该状态解析出的循环程序**：状态声明了 `program` 就用它；没有 `program` 时，隐式循环清单 = **全部片段减去 `extras.clips`**（保持声明顺序）。上例的隐式清单就是 `[breathe]`。
- 现有 v4 校验不变：状态一旦声明 `program`，其 `enter`/`loop`/`transitions` 里的片段仍必须是 `"playback": "once"`（因此 `breathe` 这种 `loop` 片段只能走隐式清单，不能写进 `program.loop`）。
- `extras.clips` 中的片段必须 `"playback": "once"`；同一个片段不得同时出现在 `program`（`enter`/`loop`/`transitions`）与 `extras` 中；减去 extras 后主动作清单不得为空。
- `label` 可选，1–12 字符，不得含控制字符；缺省时 UI 显示片段 id。迁移到 v5 时给现有片段补中文 label（呼吸、思考、搬运、打字、完成、举牌等待、举牌、收牌）。
- `cooldownMs` 可选，默认 30000，取值 5000–600000。
- 每个状态最多 4 个附加动作；片段帧数沿用现有上限（单片段 ≤240 帧、整清单 ≤1024 帧）。
- 根清单 `formatVersion` 升为 5；v1–v4 解析器保留，v4 及更早仍严格拒绝 `extras`/`label`。

### 3.2 外置人物库

```text
%LOCALAPPDATA%\DshPngPet\Characters\library\<32位ID>\
  character.json                       → libraryFormatVersion: 2
  frames/idle/primary/0000.png …       ← 主动作（新增一级目录）
  frames/idle/extra-0/0000.png …       ← 每个附加动作一个目录：extra-0、extra-1、extra-2、extra-3
  frames/thinking/primary/0000.png …
```

库清单 v2：

```json
{
  "libraryFormatVersion": 2,
  "name": "维维美",
  "statusAnchor": { "x": 0.505, "y": 0.191 },
  "baseline": 0.959,
  "extrasCooldownMs": 30000,
  "actions": {
    "idle": {
      "primary": { "frames": ["frames/idle/primary/0000.png", "…"], "durations": [70, "…"] },
      "extras": [ { "name": "坐着卖萌", "frames": ["frames/idle/extra-0/0000.png", "…"], "durations": [50, "…"] } ]
    },
    "thinking": { "primary": { "frames": ["…"], "durations": [70] }, "extras": [] }
  }
}
```

规则：

- v1 布局 `frames/<状态>/NNNN.png` 继续可读，等价于只有 `primary`；**只有用户对该人物执行「添加动作」时才就地升级为 v2**，未动过的人物保持 v1 原样。
- 外置人物的冷却按人物一个值 `extrasCooldownMs`（可选，默认 30000，范围同 3.1）；内置按状态一个值，各自贴合自己的清单形态。
- 每个附加动作必须有 `name`（1–40 字符、不得含控制字符，与人物名同规则），同一状态内不得重名。
- 主动作没有名字，UI 固定显示「默认动作」。
- 每个状态最多 4 个附加动作；帧数沿用现有上限（单动作 ≤240 帧、单人物 ≤1024 帧、单文件 ≤20 MiB、输入 ≤100 MiB、画布 ≤512、库总量 ≤1 GiB）。
- 缺失状态仍然回退到该人物的 `idle`。

### 3.3 来源角色目录（导入用）

```text
我的人物二号/
  character.json
  idle.gif                        ← 主动作素材
  stretch/001.png 002.png         ← 附加动作「伸懒腰」的帧
```

```json
{
  "characterFormatVersion": 2,
  "name": "维维美二号",
  "statusAnchor": { "x": 0.5, "y": 0.12 },
  "baseline": 0.95,
  "extrasCooldownMs": 30000,
  "actions": {
    "idle": {
      "primary": { "type": "gif", "file": "idle.gif" },
      "extras": [
        { "name": "伸懒腰", "type": "png-sequence", "frames": ["stretch/001.png", "stretch/002.png"], "frameDurationMs": 200 }
      ]
    },
    "thinking": { "type": "png", "file": "thinking.png" }
  }
}
```

规则：

- 状态的取值可以写成 **v1 简写**（直接写动作对象 = 只有主动作），也可以写成 `primary` + `extras`；`characterFormatVersion: 1` 的目录照旧可导入。
- 素材形式沿用 `gif` / `png` / `png-sequence` 三种。附加动作必须带 `name`。
- **单帧动作必须显式声明 `frameDurationMs`**：`png` 形式在附加动作里必填；GIF 若解码后只有一帧、又没声明时长，导入直接拒绝并给固定提示。多帧则使用素材自身逐帧时长（GIF 延迟规则不变：0 或无 = 100 ms，短于 20 ms 取 20 ms）。
- 同一角色的全部输入素材像素尺寸必须一致（现有 `CharacterImportBudget` 规则不变）。
- 配额、白名单、路径校验、规范化（等比缩小至最长边 512、底部对齐补方形透明画布、小图不放大、只存 PNG）全部沿用现有实现。

### 3.4 内置默认人物本次不新增帧素材

用户提供的动作用于外置人物「维维美」，内置默认人物（黑发虎鲸女仆，720×720 帧）本次只做格式与 label 迁移，现有 PNG 一个字节都不改。以后要加内置附加动作时，把透明 PNG 帧放进 `Animations/<状态>/<新动作目录>/` 并在该状态的 `animation.json` 里声明一个 `once` 片段与 `extras` 即可，无需改代码。

## 4. 播放规则

内置与外置人物共用同一套规则，实现在 `PetStateAnimationCoordinator` 内（新增一个「附加动作」阶段），不引入第二个计时器。

1. **进入状态**：完全沿用现有程序（`enter` → `loop` → `transitions`），主动作开始循环，冷却计时清零。
2. **冷却累加**：每次播放推进时累加**本次 tick 的间隔时长**（动画帧时长，或下述 1 秒心跳），仅在主动作循环阶段累加。
3. **插播**：累加值 ≥ `cooldownMs` 且该状态有可用附加动作时，等概率随机抽一个（完全随机：无权重、允许连续抽中同一个；随机源通过构造函数注入，便于确定性测试），把它**完整播一遍**，然后 `StartLoop()` 回到主动作——**不重播 `enter` 剪辑**——冷却清零重新计时。
4. **单帧附加动作**按其声明的 `frameDurationMs` 展示（校验保证该值存在），不会被压缩成 100 ms 一闪而过。
5. **打断优先级**：真实状态变化最高。附加动作播放中被切走即放弃，走原有的过渡/目标逻辑；**同一状态的重复消息不打断附加动作、也不清零冷却**（沿用现有"相同 requested 直接 return"）。
6. **暂停/拖动**：暂停期间帧计时器停止，冷却自然停止累加。
7. **减少动态效果**：不插播任何附加动作（减少动态效果下本来就没有帧推进）。
8. **静态主动作 + 有附加动作**：单帧 `loop` 主动作不会产生帧推进，此时维持 **1 秒心跳 tick** 供冷却计时，否则附加动作永远不会触发。心跳只在"主动作为单帧且该状态有附加动作"时启用。
9. **没有附加动作的状态**：行为与今天逐帧一致（含 `question` 的举牌序列、`success` 的单次播放）。
10. 附加动作素材缺失或解码失败时，沿用现有失败路径：外置人物整角色失败并恢复内置人物，内置片段回退静态占位图并提示。

## 5. 「添加动作」与库就地升级

「人物形象」窗口只对**已导入人物**启用「添加动作…」；选中「默认人物」时按钮禁用并提示"内置人物不支持添加动作"。

流程：选素材（GIF / PNG 文件）→ 状态下拉（10 个状态，标注现状，例如「待机（主动作 + 1 附加）」）→ 主动作 / 附加动作 → 名称（附加动作必填，1–40 字符，同状态内不得重名）→ 沿用现有棋盘格预览与蓝点/橙线标记调整人物级锚点与基线 → 「保存到人物」。

- 该状态已有主动作时，主动作选项显示为「**替换主动作**」并二次确认。
- 单帧素材未声明时长 → 直接拒绝并给固定提示。
- 对话框底部列出该状态现有附加动作，可选中**移除**（带确认），避免"能加不能删"。
- 新增动作的素材按**该人物现有画布边长**规范化：等比缩放到画布内、底部对齐、不放大；人物级锚点与基线默认不变，用户可在预览里微调并随保存写回。素材明显小于现有画布时会显得偏小，这是"小图不放大"既有规则的必然结果，UI 提示中说明。
- **就地升级**：目标人物若还是 v1 布局，先在同一卷的 staging 目录内完成结构迁移（`frames/<状态>/NNNN.png` → `frames/<状态>/primary/NNNN.png`）与新增动作写入，全部校验通过后以目录切换提交；失败或取消都不改变现有人物，升级只发生在用户主动操作时。

## 6. 预览 UI

```text
预览状态 [待机 ▾]   预览动作 [呼吸 · 主动作 ▾]    ☐ 静态预览
```

- 「预览动作」列出所选状态的动作：主动作（标「主动作」）与附加动作（标「附加」）；只有主动作时该下拉禁用。
- 选中即**循环播放**该项，便于逐个检查素材；提示行注明"运行时附加动作每 30 秒只播一遍"，预览与运行时的差异一眼可见。
- 「静态预览」勾选时显示该动作首帧。
- 人物列表、缩略图、导入按钮、移除人物、恢复默认等现有交互不变。

## 7. 代码职责

| 位置 | 改动 |
| --- | --- |
| `pet-helper/PetAnimationManifest.cs` | 解析 `formatVersion: 5`：片段新增可选 `label`，状态清单新增 `extras`；没有 `program` 时隐式循环清单改为"全部片段减去 `extras.clips`"；`ResolvedStateProgram` 增加 `Extras` 与 `ExtrasCooldownMs`；提供"某状态可用动作目录"（主动作 / 附加动作 + 显示名）供预览使用。 |
| `pet-helper/PetStateAnimationCoordinator.cs` | 新增附加动作阶段：冷却累加、注入式随机选择、播完回主动作、重复状态不打断、单帧心跳；新增"预览指定动作"入口。 |
| `pet-helper/PetAnimationPlayer.cs` | 暴露动作目录与"预览指定动作"给窗口；心跳间隔沿用 coordinator 报告的间隔，不新增计时器。 |
| `pet-helper/CharacterManifest.cs` | 来源清单接受 1 与 2；v2 支持 `primary` + `extras` + `name` + `extrasCooldownMs`，强制单帧附加动作声明时长。 |
| `pet-helper/CharacterAssetSource.cs` | 库 v1 / v2 解析；`StoredCharacter` 增加附加动作与冷却；`ResolveProgram` 带出附加动作；动作目录与 `name` 只在本机内存与 UI 中使用。 |
| `pet-helper/CharacterLibrary.cs` | 新导入写 v2 布局（`primary` / `extra-N`）；`PrepareAction` 与 `CommitAction` 支持给已导入人物追加、替换、移除动作，并完成 v1→v2 就地升级与失败回滚。 |
| `pet-helper/CharacterWindow.xaml(.cs)` | 二级「预览动作」下拉；「添加动作…」入口与内联面板（状态 / 主动作·附加动作 / 名称 / 移除）。 |
| `pet-helper/Assets/pet-animations.json`、`Assets/Animations/*/animation.json` | 升为 `formatVersion: 5`，给现有片段补中文 `label`；不新增帧文件。 |
| `pet-helper.Tests/*` | 见第 9 节。 |
| `src/**`、`src/protocol.ts` | **不改**：状态、动作名与随机选择全部留在 Helper 内部。 |

## 8. 兼容与安全边界

- JSON Lines 保持 **v17**，不新增字段；动作名、附加动作、随机选择、冷却都不进入协议、设置或日志。
- 内置根清单 v5，v1–v4 继续可解析；来源清单接受 1 与 2；库格式接受 1 与 2。
- 动作名与文件引用沿用既有白名单与配额；新增约束为"每状态最多 4 个附加动作"与"同状态附加动作不得重名"。
- 素材读取仍只限用户主动选择的本地文件与受控人物库；来源路径只短暂存在于 Helper 内存，不进入 DSH、协议、日志或保存记录。
- 人物库内部清单仍不鼓励手工编辑；本设计新增的升级路径由应用内操作完成，不在文档外提供手工迁移步骤。

## 9. 测试与验收

C# 单元测试（`dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`）：

- v5 清单解析：接受 `extras` / `label`；未知字段、非 `once` 的附加片段、同时出现在 `program` 与 `extras` 的片段、重复片段、超 4 个附加动作、`cooldownMs` 越界、非法 label、减去 extras 后主动作清单为空均被拒绝；有 `extras` 且无 `program` 时隐式循环清单正确排除附加片段；显式 `program.loop` 里的非 `once` 片段照旧被拒绝；v1–v4 回归不变；无 `extras` 的 v5 状态与 v4 行为等价。
- 冷却与随机（注入确定性随机源并逐帧推进）：未满 `cooldownMs` 不插播；满了插播一次且完整播放；播完回到主动作并重新计时；允许连续抽中同一个附加动作；单帧附加动作按其 `frameDurationMs` 展示。
- 打断规则：同一状态的重复 `Apply` 不打断附加动作、不清零冷却；状态切换放弃附加动作；减少动态效果下不插播；拖动暂停期间冷却不推进；单帧主动作 + 有附加动作时心跳维持计时。
- 外置人物：v1 库读作 primary-only；v2 库解析（含 `name`、`extrasCooldownMs`）；v1→v2 就地升级后目录结构与清单正确，失败路径不改变原人物；单帧附加动作缺 `frameDurationMs` 被拒；同状态重名被拒。
- 预览动作目录：状态 → 动作列表映射（主动作排第一，附加动作带「附加」标注；该状态只有主动作时列表只含一项且下拉禁用）。

手工验收（真机）：

1. 在「人物形象」里给「维维美」用「添加动作」加入用户提供的 GIF 作为**待机附加动作**并命名；二级下拉能逐个播放检查；保存后桌宠继续使用维维美。
2. 桌宠回到待机后静置约 30 秒，看到新动作插播一次，随后回到原待机动作；再等约 30 秒再次插播。
3. 附加动作播放中触发真实工作状态，动画立即切到工作；结束后回待机并重新计时。
4. 开启减少动态效果时没有插播。
5. 切回「默认人物」后行为与本次改动前一致（含举牌序列与成功动作）。

需要跑的完整验证序列：`npm test`、`dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore`、`npm run build:helper`、`npm run test:package`。因为 Helper 与内嵌清单都变了，收尾要按 AGENTS.md 递增版本、`npm run package:release`、重装插件并重启 DSH。

## 10. 验收素材（用户提供）

| 项 | 值 |
| --- | --- |
| 文件 | `C:\Users\root\Desktop\claudecode project\cb92af57bfbb28842bae8bd9c2fe800c3546941646440726.gif` |
| SHA-256 | `7F061BC91F01907D4A82E8EEBBFC93C5EB396BC0451FE84F62F98677E3509AE2` |
| 规格 | 1200×1300、45 帧、每帧 50 ms（整段 2.25 s）、索引色带透明通道、17 537 337 字节 |
| 归属 | 外置人物「维维美」的 `idle` 附加动作（**不是**内置默认人物的素材） |

「维维美」当前状态（库 ID `617022720d01461682db80a9e35ca53b`，v1 布局）：`idle` 14 帧 × 70 ms，画布 326×326，锚点 `(0.505, 0.191)`，基线 `0.959`。新动作会等比缩放进这个 326 画布（约 301×326、底部对齐），加入后该人物共 59 帧，仍在全部配额之内。来源 GIF 位于仓库之外，不会提交进项目；它只通过「添加动作」导入本机人物库。

## 11. 未纳入本次范围

- 每个状态各自的冷却时长（外置人物当前按人物一个值）、权重与"不紧邻重复"策略。
- 每个动作独立的 `statusAnchor`（外置人物继续用人物级锚点）。
- 内置默认人物的新增帧素材与 `extras` 声明。
- 主动作的"回退到内置人物"以外的更多故障策略、附加动作的跨启动统计或记忆。
