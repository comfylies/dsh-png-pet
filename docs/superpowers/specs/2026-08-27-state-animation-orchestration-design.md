# 状态动画进入与持续编排设计

## 目标

在“长时间 PNG 动画片段（Clip）”能力之上，让每个已验证的 DSH 展示状态拥有独立的进入序列和持续播放序列。状态开始时完整播放 `enter`，随后将 `loop` 中的多个长 Clip 按清单顺序、完整地依次播放；不会每秒重选，也不会因同一状态消息重发而从头开始。

本阶段不实现“来源状态 → 目标状态”的专用转场 Clip，也不实现鼠标交互。状态切换后直接开始目标状态的进入序列；固定状态转场与本地交互将在后续独立设计和实现。

## 前置条件与范围

依赖 `2026-08-27-long-animation-clips-design.md` 所定义的 v2 清单、`ResolvedClip`、`PetClipPlayback` 及其完成通知。所有引用均是清单中的本地 Clip id，不进入 Host/Helper 协议。

DSH 仍只发送当前已验证的 `state`、`activities`、`label` 与 `sequence`。`PetDisplayState.AnimationKey` 继续把它们归一化为八个本地动作键；编排器不接触原始会话事件、输入文本或回复内容。

## 状态程序清单

v2 清单新增 `statePrograms`。每个动作键最多一个程序；未定义时回退至该动作的第一个可用 Clip，再按既有动作 fallback 处理。

```json
{
  "statePrograms": {
    "idle": {
      "enter": [],
      "loop": ["idle-breathe", "idle-look-around", "idle-stretch"]
    },
    "thinking": {
      "enter": ["thinking-start"],
      "loop": ["thinking-focus", "thinking-note"]
    },
    "working": {
      "enter": ["working-start"],
      "loop": ["working-type"]
    }
  }
}
```

规则：

- `enter` 是 0–4 个 Clip 的一次性有序列表；其中的 Clip 必须声明 `playback: "once"`。
- `loop` 是 1–8 个 Clip 的有序列表；列表中的 Clip 以一次性完整片段执行，完成最后一项后回到第一项。为避免嵌套无限循环，`loop` 中的 Clip 也必须声明 `playback: "once"`。
- 专门需要持续呼吸等无缝素材时，作者将其作为一段短 `once` Clip 放入 `loop`；列表回绕就是循环。Clip 级 `playback: "loop"` 只供第一阶段兼容的“单 Clip 直接播放”使用，不能进入状态程序。
- 一个 Clip 只能被同一状态程序引用一次；所有引用都必须属于对应动作的 `actions.<key>.clips`。解析器拒绝跨状态借用素材、未知 id 和重复 id。
- `idle` 程序必须有至少一个 `loop` 项。其余状态可省略程序或因全部 Clip 不可用而回退至动作 fallback；不会阻塞界面。

## 编排状态机

新增纯模型 `PetStateAnimationCoordinator`，持有当前请求动作键、当前阶段（`Entering` 或 `Looping`）、当前列表索引，并驱动 `PetClipPlayback`。

```text
收到新的动作键
  → 取消当前计划
  → 目标状态 enter[0..n]（逐项完整播放）
  → 目标状态 loop[0..m]（逐项完整播放，回绕）

收到相同动作键
  → 保持当前 Clip、阶段、索引和帧，不重新开始
```

更具体的规则：

1. Helper 启动及尚未收到状态时，以 `idle` 程序启动。
2. `Apply(actionKey, reducedMotion)` 的动作键与当前请求键相同，直接更新减少动态效果，不重建队列。
3. 动作键改变时，立即取消当前 Clip 和尚未播放的队列项，再从目标 `enter` 第一项开始；没有可用 `enter` 时直接开始 `loop`。
4. 当前 Clip 报告完成，编排器推进到同阶段下一项；`enter` 结束后进入 `loop` 的第一个可用项；`loop` 结束后回绕。
5. 某个资源此时无法加载则跳过该项；整个 `enter` 都不可用时跳过进入阶段。整个 `loop` 都不可用时，通过动作 fallback 找到一个可用单 Clip；仍失败时显示静态占位图且不启用 timer。
6. 当前状态气泡、标签和窗口位置仍立即根据 DSH 消息更新；动画的进入序列不会延迟真实状态的文字反馈。

本阶段的切换策略是**立即中断**：思考、工作、等待、成功、错误或断连等不同动作键到来时，正在播放的进入或持续 Clip 立即停止，新的状态程序从首项开始。这保证视觉不会落后于 DSH；将来有专用转场时，协调器会把“取消后启动目标程序”替换为“播放 source→target 转场后启动目标程序”。

## 减少动态效果

减少动态效果开启时，协调器不自动推进 `enter` 或 `loop` 队列，也不启动 timer；它选目标程序中第一个可用 `loop` Clip 并显示首帧。选择 loop 而非 enter，确保静态画面代表稳定状态，不会永久停在“正在进入”的姿势。

关闭减少动态效果后，从该代表 Clip 的第一帧以 `Looping` 阶段恢复，不补播此前跳过的 enter 序列。重复状态消息仍不改变队列。

## 代码边界

| 位置 | 改动 |
| --- | --- |
| `pet-helper/PetAnimationManifest.cs` | 解析并验证 `statePrograms`，暴露某个动作的已解析状态程序。 |
| `pet-helper/PetStateAnimationCoordinator.cs` | 新增无 WPF 的队列/阶段/中断状态机。 |
| `pet-helper/PetAnimationPlayer.cs` | 将 Clip 完成通知交给协调器，并仅按协调器指定 Clip 显示图像。 |
| `pet-helper/MainWindow.xaml.cs` | 用 `AnimationKey` 调用协调器；保留气泡、缩放、右键菜单与关闭流程。 |
| `pet-helper.Tests/PetStateAnimationCoordinatorTests.cs` | 新增状态程序、重复消息、立即中断、失败回退及减少动态效果测试。 |

不改 `src/protocol.ts`、`src/companion-reducer.ts`、`src/companion-bridge.ts`、状态优先级和终态返回 idle 的计时逻辑。

## 验收

- `idle` 的三个长片段按照清单次序各自完整播放，再回到第一段；过程中重复 idle 状态不会重置。
- `thinking` 先完整播放 `thinking-start`，再按顺序循环持续片段；切换到 `working` 时立即停止 thinking 并从 `working-start` 开始。
- 缺失素材只跳过相应项；全部缺失仍遵循动作 fallback 和静态占位回退。
- 开启减少动态效果时无 timer tick，显示目标状态 loop 的首帧；关闭后从其 loop 开始恢复。
- 原有协议/打包/布局测试保持通过，且没有新增网络、JSON Lines 载荷或会话内容处理。

本阶段完成后，才进入第三阶段：为特定 `source -> target` 建立固定转场动画；第四阶段再把左键等本地交互作为可被高优先级状态打断的临时程序。
