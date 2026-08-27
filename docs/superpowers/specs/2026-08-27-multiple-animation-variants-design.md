# 同一状态多动画变体设计

## 结论

当前实现**不能把同一状态的多套动画作为独立变体选择**。一个展示状态只会在 `PetDisplayState` 中映射为一个 `PetAnimationKey`，而 `pet-animations.json` 中每个动作键只有一条 `frames` 序列。把两套帧都填进该序列，只会合并成一段循环动画，不能在状态再次进入时改播另一套动作。

本设计在 Helper 本地为每个现有动作键支持多个具名变体，并在真正进入该状态时轮换选择；不修改 DSH、TypeScript、JSON Lines 协议或任何会话数据边界。

## 目标与边界

目标：同一展示状态（例如 `idle` 或 `thinking`）可以配置多套彼此独立的 PNG 帧序列；状态进入时切换至一套可用变体，状态持续期间只循环该变体。连续到达同一状态的重复消息不得导致重选、闪回第一帧或状态气泡抖动。

不在本阶段提供用户可配置的随机策略、播放权重、定时自动换变体或跨启动记忆；这些会改变产品行为但不是支持多个动作素材的必要条件。状态、活动、标签、资源标识符和文件路径仍不得跨 stdin/stdout JSON Lines 传递。

## 清单格式

每个动作仍保留一个动作级 `fallback`；有帧时使用 `variants` 定义变体。每个变体有在动作内唯一、稳定的 `id`，以及自己的帧和状态气泡锚点。

```json
{
  "idle": {
    "variants": [
      {
        "id": "breathe",
        "frames": [
          "Animations/idle/breathe/001.png",
          "Animations/idle/breathe/002.png"
        ],
        "statusAnchor": { "x": 0.5, "y": 0.11 }
      },
      {
        "id": "look-around",
        "frames": [
          "Animations/idle/look-around/001.png",
          "Animations/idle/look-around/002.png"
        ],
        "statusAnchor": { "x": 0.5, "y": 0.11 }
      }
    ]
  },
  "thinking": {
    "variants": [],
    "fallback": "idle"
  }
}
```

为确保已有包和已有素材清单可直接升级，解析器同时接受当前的旧格式：动作对象包含 `frames` 和 `statusAnchor` 时，将它转换为一个内部 `id: "default"` 的隐式变体。一个动作对象必须**二选一**使用 `frames`（旧格式）或 `variants`（新格式），不能混用。

### 校验规则

- `idle` 必须至少有一个含帧的变体，且不能有 `fallback`；其他动作可以有空变体数组并回退。
- 一个动作最多 8 个变体；每个变体 1–64 帧；一个动作的所有变体合计最多 128 帧。限制用于控制解析、预加载与内存占用。
- `id` 为 1–48 个 ASCII 字符，只允许小写字母、数字和连字符，且同一动作内不能重复。
- 继续使用现有 PNG 资源标识符白名单；同一帧不得在整个清单中重复引用，所有候选帧均须能由受控嵌入资源加载器加载。
- 含帧的每个变体都必须有自己的 `statusAnchor`；因此趴卧、跳跃等头部位置不同的变体不需要改 WPF 布局。
- 保持现有动作级 fallback 的合法键检查、缺失目标检查和环检测。若一个动作没有可用变体，沿 fallback 链继续；最终仍无可用帧时使用当前静态占位图。

## 选择与播放规则

`PetAnimationManifest` 负责安全解析，并为请求动作返回 fallback 链上第一个具有可用变体的动作及其可用变体集合。它不做随机选择，也不依赖 WPF。

`PetAnimationPlayback` 保存“上一次请求的展示动作键”和当前 `(resolved action key, variant id)`。首次应用或请求动作键发生变化时：

1. 解析可用候选变体及 fallback。
2. 若减少动态效果开启，选择候选列表中的第一个变体；否则用动作级 shuffle-bag（洗牌袋）选择一个变体。一个袋子在本轮所有变体各出现一次后才重新洗牌；若有至少两个候选，重洗牌后首项不得与上次播放的变体相同。
3. 若选择结果与当前 `(action, variant)` 相同，保留帧索引和计时器；否则立即显示新变体首帧、重置索引与计时器间隔。

同一请求动作键的重复状态消息直接保留当前变体和帧索引，完全不调用选择器。这样 Host 的正常状态重发不会造成视觉跳变。状态 A 变为 B、再回到 A 时则是一次新的进入，A 可以轮换到下一变体。

如果两个不同请求动作都回退到了相同的单一动作变体，视觉身份未变，播放器继续现有帧序列；这保留当前“相同有效动作不闪动”的行为。变体帧数仍按 `round(1000 / frameCount)` 计算间隔，单帧不启动计时器。

开启减少动态效果后，播放器停止 timer 并显示当前所选变体的第一帧；状态变更时只会选清单顺序中的首个可用变体并显示其首帧。关闭减少动态效果后，保留当前变体，从当前帧继续其正常循环，不因重复状态消息重新抽取变体。

## 代码职责与兼容性

| 位置 | 改动 |
| --- | --- |
| `pet-helper/PetAnimationManifest.cs` | 解析旧/新清单格式，校验变体，并暴露 fallback 后的可用候选集合。 |
| `pet-helper/PetAnimationPlayback.cs` | 保留请求键、变体身份和帧索引；通过可注入的选择器作可测试的 shuffle-bag 选择。 |
| `pet-helper/PetAnimationPlayer.cs` | 根据已选变体加载受控 PNG、设置 timer，并使用变体自身的锚点。 |
| `pet-helper/Assets/pet-animations.json` | 先迁移 `idle` 为一个或多个 `variants`；其余动作可以继续是空数组加 fallback。 |
| `pet-helper.Tests/*Animation*Tests.cs` | 为清单兼容、选择、回退、重复状态与减少动态效果添加回归测试。 |

`PetDisplayState.cs`、`src/protocol.ts`、`src/companion-reducer.ts`、`src/companion-bridge.ts` 均不修改。变体选择是已验证状态抵达 Helper 后的纯本地展示决定，因此协议继续为 v5，也不会泄露素材路径或改变安全边界。

## 测试与验收

- 旧式单 `frames` 动作解析为 `default` 变体，新式 `variants` 动作按顺序解析；混用字段、重复/非法 id、超出数量上限、重复帧、缺 anchor 均被拒绝。
- 多个可用变体在状态重新进入时按洗牌袋轮换，且存在两个以上变体时不会紧邻重复；选择器使用注入的确定性随机源测试，不依赖时间或真实随机数。
- 同一状态的重复 `Apply` 不调用选择器、不重置帧索引；状态切换后新变体从第一帧开始。不同状态回退到同一唯一变体时不闪动。
- 变体素材缺失时跳过该变体；整组不可用时遵循既有 fallback，最终仍安全显示静态占位图。
- 减少动态效果无 timer tick，状态变化选首个可用变体首帧；关闭后恢复当前变体的循环。
- 保留 Node 协议、打包和 WPF 布局回归；确认 JSON Lines 字段、网络端口与日志内容没有新增动画素材信息。

验收时，向同一动作目录添加至少两套透明 PNG（例如 `Animations/idle/breathe/` 与 `Animations/idle/look-around/`），更新清单后验证：启动选择一套、离开再回到 idle 选择另一套、连续 idle 消息不切换，并且状态气泡始终跟随当前变体的 anchor。
