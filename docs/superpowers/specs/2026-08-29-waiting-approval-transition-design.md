# 等待审批举牌与收牌转场设计

## 目标

为桌宠的 `waiting`（气泡文案“等待你的操作”）提供一组可编排的本地 PNG 动画：进入时从身后拿出无文字确认牌、随后持续举牌等待；展示状态从 `waiting` 切换到任一 `active` 活动时，先完整播放一次收牌动作，再开始目标活动的动画。

视觉上的收牌不改变 DSH 的真实状态：状态气泡必须在收到新展示状态的同一 UI 调度周期立即变为“思考中…”“工作中…”或“输出中…”。角色图像会独立完成收牌，动画时长不改变真实状态反馈。

> 实施决策更新：首批“举问号牌”素材实际接入 `question`（“等你回答…”）状态，而不是审批 `waiting`。该状态的拿牌、31–75 帧举牌循环和收牌沿用本设计的同一通用机制；`waiting` 保留给后续单独的审批素材。

## 现状与依据

`2026-08-27-state-animation-orchestration-design.md` 已为状态定义了 `enter`、`loop` 两段程序，并明确将“来源状态 → 目标状态”的固定转场留作下一阶段。当前代码尚未实现该文档提出的 `PetStateAnimationCoordinator`：`PetAnimationPlayback` 仍然只会解析一个 `ResolvedClip`，并在动作键改变时立即开始新 Clip。故不能在 `waiting -> active` 时保留收牌尾段。

通用的清单格式、状态机、抢占规则与测试边界由 `2026-08-29-state-transition-animation-design.md` 定义；本文件只保留等待审批举牌/收牌这一首个素材实例。

本设计实现该缺失的编排层，同时只落地一个明确的本地转场类别：`waiting -> active`。它不读取审批内容、用户选择、会话 id 或 DSH 原始事件；所有素材名称、帧序和转场选择均留在 Helper 的嵌入式清单中。

## 用户可见时序

```text
收到 waiting
  拿出确认牌（enter，一次）
  → 举牌、眨眼、轻微呼吸（loop，持续）

收到 thinking / working / thinking-working / responding
  气泡立即更新为真实活动文案
  → 收回确认牌（waiting -> active，一次）
  → 播放目标状态的 enter
  → 播放目标状态的 loop
```

- 举牌循环持续到桌宠**可见状态**不再是 `waiting` 为止；它不是计时动画。
- 转场只在举牌素材实际被解析并显示时触发。若 `waiting` 因缺帧回退为 `idle`，或收牌 Clip 缺帧，则安全地立即开始目标程序。
- 从 `waiting` 转到 `idle`、`success`、`error`、`disconnected` 或 `question` 时不播放收牌，立即中断并开始目标程序；避免错误和断连等高优先级反馈滞后。
- 收牌中再次收到某个 `active` 键时，保留当前收牌 Clip，最终以**最新**的活动键启动。例如原本准备进入 `thinking`、收牌期间收到 `working`，收牌完毕后直接播放 `working`。
- 收牌中收到非 `active` 键时立即取消收牌并显示新状态的首帧。
- 在举牌 `enter` 尚未完成时就收到 `active`，先完成剩余举牌进入序列，再收牌；这样不会从“牌在身后”突变到“收牌”。素材时长不作额外限制，气泡仍立即反映真实状态。

这里的 “`waiting -> active`” 是展示层语义，而非对某一次审批结果的断言。Helper 从现有脱敏展示消息中无法也不应识别具体审批或会话；当全局可见 `waiting` 不再占优、活动状态成为可见状态时，播放“准备继续做事”的收牌动作是正确且无须扩展协议的表现。

## 清单实例：v4

根 `pet-animations.json` 升级为 `formatVersion: 4`，仍只声明固定动作键、固定状态目录与 fallback。v3 继续可读，作为单 Clip 兼容格式；新功能只由 v4 作者格式表达。

每个 `Assets/Animations/<action>/animation.json` 在原有 `clips` 之外可以声明 `program` 和 `transitions`：

```json
{
  "clips": {
    "raise-confirm-card": {
      "frames": ["raise-confirm-card/001.png"],
      "frameDurationMs": 83,
      "playback": "once",
      "statusAnchor": { "x": 0.5, "y": 0.11 }
    },
    "hold-confirm-card": {
      "frames": ["hold-confirm-card/001.png"],
      "frameDurationMs": 125,
      "playback": "once",
      "statusAnchor": { "x": 0.5, "y": 0.11 }
    },
    "withdraw-confirm-card": {
      "frames": ["withdraw-confirm-card/001.png"],
      "frameDurationMs": 83,
      "playback": "once",
      "statusAnchor": { "x": 0.5, "y": 0.11 }
    }
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

为避免把审批动作写成引擎特例，实际清单采用通用设计中的目标动作数组：`["thinking", "working", "thinking-working", "responding"]`。它们共同复用 `withdraw-confirm-card`；不新增 `active` 这样的特殊选择器。

### 校验规则

- `program`、`transitions` 均可缺省；缺省时对应动作按当前规则解析其第一个可用 Clip。
- `program.enter` 为 0–4 个不重复 Clip id，`program.loop` 为 1–8 个不重复 Clip id。两组内的引用都必须属于本动作目录，且引用 Clip 必须为 `once`。
- 每条 `transitions` 路由的 `clips` 为 1–4 个不重复 Clip id，均属于该源动作目录且为 `once`。同一 Clip 可以在一个程序和一个转场中复用，便于作者在“举牌未完即确认”的场景复用连续素材；同一列表内不能重复。
- `waiting` 的举牌与收牌时长不作额外限制；气泡仍立即反映真实活动。每个单独 Clip 继续遵守通用清单的帧数和帧间隔限制。
- 每个 Clip 的帧资源仍全局唯一、局限于自身 `Animations/<action>/` 目录，并沿用既有 1–240 帧、16–1,000 ms、统一画布/基线和安全路径校验。
- 解析或资源可用性检查失败时，跳过不可用 Clip；空 `enter` 直接进入 loop，空 transition 立即进入目标程序。所有 fallback 链继续由动作键解析，不允许跨动作读取帧。

## 代码结构

| 位置 | 责任与改动 |
| --- | --- |
| `pet-helper/PetAnimationManifest.cs` | 增加 v4 解析；以 `ResolvedStateProgram` 暴露“实际解析到的动作键、enter、loop、按目标动作查询的 transition”。保留 `Resolve` 作为 v1–v3 和旧测试的兼容入口。 |
| `pet-helper/PetStateAnimationCoordinator.cs` | 新增纯 C# 编排状态机。持有“当前可见程序、当前播放阶段、队列索引、挂起目标键”；选择转场、处理中断与 Clip 完成，不依赖 WPF、文件或协议。 |
| `pet-helper/PetAnimationPlayback.cs` | 退役为兼容门面或内部替换为 Coordinator + `PetClipPlayback`；不再承担“动作改变就立即换 Clip”的策略。 |
| `pet-helper/PetAnimationPlayer.cs` | 仅负责受控资源可用性、按 Coordinator 当前 Clip 解码/缓存帧、`DispatcherTimer` 和完成通知。每个 Clip 完成后请求 Coordinator 的下一 Clip，不自行决定状态转场。 |
| `pet-helper/MainWindow.xaml.cs` | 仍先更新 `lastDisplayState`、状态气泡颜色/文案与位置，再调用 `animationPlayer.Apply(AnimationKey, reducedMotion)`；不接触审批、会话或素材逻辑。 |
| `pet-helper.Tests/PetStateAnimationCoordinatorTests.cs` | 覆盖时序与抢占规则；不启动 WPF。 |

建议的纯模型接口：

```csharp
public sealed class PetStateAnimationCoordinator
{
    public ResolvedClip CurrentClip { get; }
    public PetStatusAnchor StatusAnchor { get; }
    public bool IsAnimating { get; }

    public void Apply(PetAnimationKey requested, bool reducedMotion);
    public void Advance(); // 由 PetAnimationPlayer 的 DispatcherTimer 驱动
}
```

内部阶段为 `Entering`、`Looping`、`Transitioning`。`Apply` 总是先记录最新 `requested`；是否能保持当前帧、继续举牌进入、开始收牌或立即中断，由阶段和 `ResolvedStateProgram.EffectiveKey` 决定。`PetClipPlayback` 继续是唯一的逐帧和 `once` 完成事实来源，避免同时存在两个帧索引或两个 timer。

## 减少动态效果与状态锚点

- `reducedMotion=true` 时不播放举牌、等待循环或收牌；状态变化立即选择目标状态稳定 loop 的第一帧且不启动 timer。这与现有“减少动态效果”的承诺一致。
- 关闭减少动态效果时，从目标状态 loop 的第一帧恢复，不补播先前被跳过的举牌或收牌。
- 气泡位置继续依据**正在展示的 Clip** 的 `statusAnchor` 计算。收牌尚未结束时，使用收牌 Clip 锚点；开始目标程序首帧时切换目标锚点。气泡文案不等待这个切换。

## 测试与验收

1. `waiting` 依次播放 `raise`，再循环 `hold`；重复 `waiting` 不从首帧重启。
2. `waiting` 的 `hold` 中请求 `thinking` 时，立即保留/开始 `withdraw`，其完成后才开始 `thinking` 的 enter/loop；标签更新不属于 Coordinator，不受等待影响。
3. 收牌期间 `thinking -> working`，收牌完整播放一次，最终进入 `working`，不会先闪出 `thinking`。
4. 举牌 enter 期间进入活动，剩余 enter 后接 withdraw；期间到 `error` 或 `disconnected` 则立即中断并进入对应首帧。
5. 缺少举牌、举牌回退 idle、缺收牌、无目标程序、资源不可用和 fallback 链均不抛异常；最终安全显示目标动作或 idle。
6. v1、v2、v3 清单与现有 idle/thinking/responding/success 素材保持可解析、可播放；v4 拒绝跨目录引用、未知目标动作、loop Clip 和重复列表项。
7. `reducedMotion` 下没有 timer tick，且不播放任何 transition；现有协议、JSON Lines 载荷、Node 状态归约和网络边界均不变。

## 非目标

- 本次不生成或导入视频/PNG 帧；举牌、保持和收牌素材需经视觉验收后单独加入。
- 不为 `question`、`success`、`error` 或 `disconnected` 立即添加转场；Coordinator 只提供通用能力。
- 不修改 DSH 协议、事件适配、状态优先级或传递会话/审批详情。
