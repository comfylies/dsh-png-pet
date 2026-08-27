# 长时间 PNG 动画片段（Clip）设计

## 目标

把当前“一个动作键对应一组帧，并按 `round(1000 / 帧数)` 无限循环”的模型升级为可播放完整长动画的本地 Clip 模型。一段动画从第一帧连续播到最后一帧，不会每秒重新选帧或重新选动作；动画作者可以明确控制每帧显示时长和片段是播放一次还是循环。

本阶段只提供 Clip 素材模型、清单校验和确定性的单 Clip 播放器。不编排多个 Clip、没有状态进入动画、状态转场或鼠标交互；这些属于后续“状态编排”阶段。

## 安全与协议边界

Clip、帧顺序、播放时长和资源标识符均为 Helper 内嵌清单中的本地数据。DSH 与 Helper 的 JSON Lines 协议保持 v5：不新增字段，不传递 Clip id、素材路径、播放进度或用户交互内容；不创建端口或网络请求。

## 清单模型

作者格式升级为目录化 v3。根 `pet-animations.json` 只保留八个安全展示动作键、它们固定对应的状态目录和 fallback；每个状态目录有自己的 `animation.json`，并只定义该状态的 Clip。根清单中的目录不能任意指定：例如 `idle` 只能读取 `Animations/idle/animation.json`，因此仍可在启动时做完整的回退、数量、帧去重和资源安全校验。

```json
{
  "formatVersion": 3,
  "actions": {
    "idle": { "manifest": "Animations/idle/animation.json" },
    "thinking": {
      "manifest": "Animations/thinking/animation.json",
      "fallback": "idle"
    }
  }
}
```

`Animations/idle/animation.json`：

```json
{
  "clips": {
    "breathe": {
      "frames": [
        "breathe/001.png",
        "breathe/002.png"
      ],
      "frameDurationMs": 160,
      "playback": "loop",
      "statusAnchor": { "x": 0.5, "y": 0.11 }
    }
  }
}
```

`frameDurationMs` 适用于该 Clip 的全部帧。第一版不支持单帧不同的时长：这覆盖正常 PNG 帧动画，并使 `DispatcherTimer` 的时序、资源限制与测试保持简单。需要停顿时，素材作者重复同一视觉帧；以后若确有需求，才增加受限的 `frameDurationsMs` 数组。

### 解析与资源限制

- 仅允许当前八个动作键及 `idle` 无 fallback 的规则；继续检查 fallback 目标存在且无环。
- 每个状态目录内的 Clip 名称为 1–48 个小写 ASCII 字母、数字或连字符；加载后归一化为全局唯一的 `<状态>-<Clip>` 身份。
- 每个 Clip 1–240 帧，`frameDurationMs` 为 16–1,000 毫秒；单段理论时长最多 240 秒。
- 每个动作最多 8 个 Clip，清单合计最多 1,024 帧；每个帧资源标识符全局唯一，保持当前 `.png`、相对路径和无 `..` 的白名单。
- 每个 Clip 都有合法的 `statusAnchor`；所有帧必须使用相同画布和基线。
- `idle` 至少声明一个 Clip。缺失或无法加载的 Clip 被视为不可用；动作选择继续沿 fallback 走，最终失败时安全显示现有静态占位图。

### 与当前及“多变体”设计的兼容

现有无版本顶层动作清单作为 legacy v1 接受：每个含 `frames` 的动作在内存中转换成 id 为 `<action>-default`、`frameDurationMs: 1000 / frameCount`、`playback: loop` 的隐式 Clip。这样当前的 `idle` 四帧仍近似一秒循环，旧包升级不会立刻失效。

此前“同一状态多动画变体”设计中的每个 variant 在目录化 v3 中对应该状态 `animation.json` 内的一个 Clip。v2 单文件清单和无版本 v1 清单继续作为兼容读取格式；v3 是后续素材的唯一作者格式，不同时支持 v3 的 `variants` 字段，避免两套可写格式长期共存。

## 播放状态机

新增无 WPF 依赖的 `PetClipPlayback`，其输入为已解析、已确认资源可用的 `ResolvedClip`：

1. `Start(clip, reducedMotion)` 立即显示第 1 帧，保存 Clip 身份和索引 0。
2. 普通模式下，每个 tick 前进一帧；`loop` 到末帧后回到 0，`once` 到末帧后停留在最后一帧并报告一次 `Completed`。
3. `Start` 收到相同 Clip 身份时不重置索引或 timer；不同 Clip 必从第 1 帧开始。
4. 减少动态效果时没有 tick，固定显示第 1 帧；关闭后从该帧继续播放。单帧 Clip 不启动 timer，`once` 单帧仅报告一次完成。

`PetAnimationPlayer` 仅负责按 Clip 的固定间隔驱动 `DispatcherTimer`、从受控 `pack://` URI 缓存 PNG 并把 `Completed` 交给上层。Clip 播放器不选择状态、不读取文件、不直接操作 WPF。

## 代码边界

| 位置 | 改动 |
| --- | --- |
| `pet-helper/PetAnimationManifest.cs` | 解析 v1/v2/v3、状态目录清单、Clip 定义、资源/数量校验、动作 fallback 和可用 Clip 解析。 |
| `pet-helper/PetClipPlayback.cs` | 新增纯 Clip 时序状态机与一次性完成通知。 |
| `pet-helper/PetAnimationPlayback.cs` | 过渡为兼容门面，或由 Clip 播放器替换；不承担多 Clip 排队。 |
| `pet-helper/PetAnimationPlayer.cs` | 使用 Clip 间隔驱动 timer，转发完成事件，保持静态占位回退。 |
| `pet-helper/Assets/pet-animations.json` | 迁移到 v3 的状态索引；每个 `Assets/Animations/<状态>/animation.json` 定义本状态 Clip。 |

不改 `PetDisplayState.cs`、Node/DSH 状态归约、协议验证、Helper 生命周期或窗口布局。

## 测试与验收

- v1 清单可解析并等价转换；v2 Clip、动作归属、fallback 与所有资源限制均有纯单元测试。
- 长 `once` Clip 逐帧播放、只报告一次完成并停留尾帧；`loop` Clip 完整循环；重复 Start 同一 Clip 不跳回首帧。
- 状态变化选择新 Clip 时立即显示首帧，无法加载时按 fallback 或静态占位回退。
- 减少动态效果没有有效 timer tick；单帧 Clip 不开 timer。
- 保留现有协议、打包、WPF 布局回归，确认没有新增 JSON Lines 字段与网络行为。

完成本阶段后，应用仍可按当前展示状态直接播放一个 Clip；“一个状态内按顺序播多个长片段”留给下一阶段实现。
