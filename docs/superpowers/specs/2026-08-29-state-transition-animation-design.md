# 可复用状态转场动画设计

## 目标

让桌宠的任意本地动画动作都能按需声明“从我离开、前往哪些目标状态时，先播放哪些一次性 Clip”。动画作者以后可以为 `thinking -> responding`、`working -> success`、`error -> idle` 等状态变换补充素材，而无需新增 C# 条件分支、JSON Lines 字段或新的协议状态。

`waiting -> active` 的“收确认牌”是第一项素材应用，详见 `2026-08-29-waiting-approval-transition-design.md`；本设计是它的通用、可复用基础。后者中的 `active` 仅是该具体场景的目标集合，不是引擎的硬编码特例。

## 不变的边界

- Host 继续只发送已验证的 `state`、`activities`、固定 `label` 与 `sequence`；不会下发动画名、转场名、素材路径、用户选择、审批内容或会话信息。
- `PetDisplayState.AnimationKey` 继续把展示状态转换为固定的 `PetAnimationKey`。转场只根据两个本地动作键和嵌入式清单选择。
- `MainWindow` 先更新状态气泡、颜色、位置和无障碍文案，再调用动画播放器。角色可以继续播一个短转场，但真实状态的文字绝不延迟。
- 所有帧仍是受控的嵌入式 PNG；不访问网络、端口或任意文件路径。

## 动作程序和转场路由

每个动作目录可以拥有一个稳定程序和零至多个离场路由：

```text
动作 A 开始
  A.enter[0..n]（每段一次）
  → A.loop[0..m]（完整轮播）

收到动作 B
  若 A 有覆盖 B 的离场路由：
      A.transition(A -> B)[0..k]（每段一次）
      → B.enter[0..n]
      → B.loop[0..m]
  否则：
      立即中断 A，开始 B.enter / B.loop
```

`enter` 表示进入某状态的动作，`loop` 表示该状态长期持续时的稳定循环，`transition` 是离开源状态时播放的动作。转场定义在**源动作**目录中：例如“收牌”属于 `waiting`，而“完成后挥手”属于 `success`。这样所有帧仍限制在其所属状态目录，素材归属清楚且安全。

## v4 清单作者格式

根 `pet-animations.json` 为 `formatVersion: 4`，结构仍是固定的十个动作键、各自固定 `Animations/<action>/animation.json` 路径和 fallback。v1、v2、v3 保持只读兼容；它们被等价视为“无 program、无 transitions 的单 Clip 动作”。

动作清单示例：

```json
{
  "clips": {
    "raise-confirm-card": { "frames": ["raise-confirm-card/001.png"], "frameDurationMs": 83, "playback": "once", "statusAnchor": { "x": 0.5, "y": 0.11 } },
    "hold-confirm-card": { "frames": ["hold-confirm-card/001.png"], "frameDurationMs": 125, "playback": "once", "statusAnchor": { "x": 0.5, "y": 0.11 } },
    "withdraw-confirm-card": { "frames": ["withdraw-confirm-card/001.png"], "frameDurationMs": 83, "playback": "once", "statusAnchor": { "x": 0.5, "y": 0.11 } }
  },
  "program": {
    "enter": ["raise-confirm-card"],
    "loop": ["hold-confirm-card"]
  },
  "transitions": [
    {
      "to": ["thinking", "working", "thinking-working", "responding"],
      "clips": ["withdraw-confirm-card"]
    }
  ]
}
```

`to` 只能使用本地动作名：`idle`、`thinking`、`working`、`thinking-working`、`responding`、`waiting`、`question`、`success`、`error`、`disconnected`。数组用于让多个目标复用同一离场动作；例如等待审批无论下一步是思考、工具工作还是输出，都收回同一块牌。它不接受 `active`、协议字段或任意字符串。

首批落地的是 `question → [thinking, working, thinking-working, responding]`：拿问号牌、举牌循环、收牌后继续处理。其余可按同一格式补充：

| 源 → 目标 | 源动作中的可选离场 Clip | 用途 |
| --- | --- | --- |
| `thinking → responding` | 放下思考手势 | 从构思自然转到输出。 |
| `working → success` | 合上笔记本 / 抬头 | 完成前的收束。 |
| `success → idle` | 收起庆祝手势 | 平稳回到待机。 |
| `error → idle` | 整理情绪 / 轻叹 | 失败提示后恢复。 |
| `question → thinking` | 收起提问牌 | 用户回答后继续处理。 |

并非每个箭头都需要素材；没有路由时，保持当前“立即开始目标动作”的行为。

## 解析与数据模型

`PetAnimationManifest` 将 v4 解析为以下不可变本地模型：

```csharp
public sealed record ResolvedStateProgram(
    PetAnimationKey EffectiveKey,
    ImmutableArray<ResolvedClip> Enter,
    ImmutableArray<ResolvedClip> Loop,
    ImmutableArray<ResolvedTransition> Transitions);

public sealed record ResolvedTransition(
    ImmutableHashSet<PetAnimationKey> Targets,
    ImmutableArray<ResolvedClip> Clips);
```

- `EffectiveKey` 是通过资源可用性和 fallback 实际解析到的动作键，而不是仅仅请求的键；只有实际显示了源动作时，才允许播放其离场转场。
- 每次解析都使用现有受控 `isFrameAvailable` 回调。缺帧 Clip 被跳过；空 program 或空 route 不会阻塞目标状态。
- `Resolve` 保留为旧播放器和现有 v1–v3 测试的兼容方法；新增 `ResolveProgram` 与 `TryResolveTransition` 供编排器使用。

校验规则：

1. `program.enter` 允许 0–4 个不重复 `once` Clip；`program.loop` 必须为 1–8 个不重复 `once` Clip。省略 `program` 时，动作的第一个可用 Clip 是兼容 loop。
2. `transitions` 允许 0–8 条路由；每条 `to` 为 1–10 个动作名，`clips` 为 1–4 个不重复 `once` Clip，且所有 Clip 属于源动作目录。
3. 同一源动作的一个目标键最多被一条路由覆盖，拒绝含糊路由；同一 Clip 可在程序和路由中复用，便于连续动作素材。
4. 保持现有帧路径、帧数、间隔、总资源数、fallback 无环与状态锚点校验；不放开跨目录帧引用。
5. 每段 Clip 继续受现有 240 帧和 16–1,000 ms/帧限制，但 `enter` 与 transition 队列总时长不额外设限。状态气泡始终立即更新；动画作者应自行确保长转场仍能自然表达目标状态。

## 编排器状态机

新增纯模型 `PetStateAnimationCoordinator`，替代 `PetAnimationPlayback` 的“动作一变立即换 Clip”策略。它持有当前解析程序、当前 Clip、阶段、索引和最新请求目标：

```text
Apply(target)
  target 与当前请求相同：保持帧、阶段和队列
  当前程序存在 route 覆盖 target：进入 Transitioning，播放该 route
  否则：取消当前队列，进入 target 的 Entering / Looping

Clip 完成
  Entering：继续 enter；结束后进入 loop
  Looping：继续下一个 loop Clip，末项回绕
  Transitioning：按最新请求解析目标程序，进入其 enter / loop
```

中断规则是通用且确定的：

- 正在播放离场 route 时，若最新目标仍被**同一条 route**覆盖，route 完整播放一次，完成后进入最新目标；例如 `waiting → thinking` 后又变为 `working`，只收一次牌，最终开始工作。
- 若最新目标不被当前 route 覆盖，立即取消 route，重新按“当前可见源动作 → 最新目标”查找路由；找不到就立即进入目标程序。这样 `waiting → active` 的收牌不会压过 `error` 或 `disconnected`，也允许未来作者为不同目标提供不同离场动作。
- 在源状态 `enter` 中发生离开时，若存在 route，完成尚余 enter 后再播放 route；作者必须让 enter 在 1 秒内完成。若新目标再次改变且不匹配该 route，按上一条规则立即抢占。
- 程序或 route 的资源无法解析时，视为不存在，立即按 fallback 后的目标程序继续；不能因为动画缺失卡住展示。

减少动态效果时，Coordinator 不进入 enter、loop 或 transition 的播放队列，直接显示目标程序稳定 loop 的首帧并停止 timer；关闭后从该首帧恢复，不补播跳过的动作。

## 集成职责

| 组件 | 职责 |
| --- | --- |
| `PetAnimationManifest` | 解析 v4、校验并解析本地程序/路由。 |
| `PetStateAnimationCoordinator` | 所有队列、路由选择、抢占和完成后的推进；无 WPF 依赖。 |
| `PetClipPlayback` | 单一 Clip 的帧索引、间隔和 once 完成事实来源。 |
| `PetAnimationPlayer` | 受控资源探测与解码缓存、`DispatcherTimer`、把 tick/完成转交 Coordinator。 |
| `MainWindow` | 立即应用展示状态和气泡，再请求新动画键；不判断转场。 |

## 测试与验收

- 通过一个自定义 v4 清单测试任意 `A → B` 路由、无路由的直接切换、同一路由内目标更新、不同路由抢占和缺帧回退。
- 用 `waiting → [thinking, working, thinking-working, responding]` 验证收牌只播放一次，且最后进入最新活动动画。
- 用 `working → success` 与 `success → idle` 的最小假素材证明引擎不含任何 `waiting` 专用判断。
- 验证所有 v1–v3 清单、现有动画资源与协议测试保持通过。
- 验证 `reducedMotion` 不启动 timer，`error` / `disconnected` 能立即打断普通转场，且气泡更新不依赖 Clip 完成。

## 非目标

本设计只提供代码和清单机制；每一条具体转场仍需要独立的动画分镜、生成提示词、绿幕/透明化处理、帧检查和视觉验收后才加入资源目录。
