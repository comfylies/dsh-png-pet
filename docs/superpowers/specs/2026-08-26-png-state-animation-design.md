# PNG 状态动作设计

## 目标

让 WPF Helper 根据现有、安全的 DSH 展示状态切换本地 PNG 帧动作。每个动作在该状态持续期间循环播放；未提供动作素材时回退至默认待机动作。插件与 Helper 的 stdin/stdout JSON Lines 边界保持不变，绝不传递素材路径、会话正文或自由文本。

## 范围与非目标

本阶段只实现动作清单、帧播放、状态到动作的选择、资源回退和减少动态效果。角色 PNG 由用户提供；本阶段不生成、替换或移动 PNG 素材。

不改变 DSH 事件归约、状态优先级、终态停留计时器、会话输入、回复预览、设置持久化或 Helper 生命周期。成功与错误状态继续由既有桥接逻辑短暂展示，随后回到 idle。

## 八种动作

现有协议把思考与工作编码为 `active` 状态的规范化 `activities` 数组。因此 Helper 在本地将状态和活动归一化为以下八个动作键：

| Host 展示模型 | 动作键 | 说明 | 缺失回退 |
| --- | --- | --- | --- |
| `idle`, `[]` | `idle` | 默认待机动作，也是唯一必需素材。 | `idle` 首帧 |
| `active`, `[thinking]` | `thinking` | 模型组织或处理结果。 | `idle` |
| `active`, `[working]` | `working` | 正在执行工具、命令、读写或测试。 | `idle` |
| `active`, `[thinking, working]` | `thinking-working` | 同一 Session 同时思考与工作。 | `working`，再 `idle` |
| `waiting`, `[]` | `waiting` | 等待用户确认或输入。 | `idle` |
| `success`, `[]` | `success` | 顶层任务已完成的短暂状态。 | `idle` |
| `error`, `[]` | `error` | 顶层任务失败的短暂状态。 | `idle` |
| `disconnected`, `[]` | `disconnected` | 握手或通信异常的本地提示。 | `idle` |

协议验证仍只接受现有规范状态、活动和固定标签；动作键完全在 Helper 内部派生，不能由 Host 指定。

## 动作清单和资源组织

在 `pet-helper` 内新增一个嵌入式 JSON 动作清单。它是运行时唯一的帧顺序和回退来源，并记录逻辑资源标识符而非可从协议取得的路径。每个动作可定义零或多个有序帧和可选回退动作；帧间隔由 Helper 从实际帧数独立推导，不接受手工配置。

```json
{
  "idle": {
    "frames": ["idle/001.png"]
  },
  "thinking": {
    "frames": ["thinking/001.png", "thinking/002.png"],
    "fallback": "idle"
  },
  "thinking-working": {
    "frames": [],
    "fallback": "working"
  }
}
```

动作帧将作为 WPF 嵌入资源编译；清单中的标识符由受控资源加载器转换为程序集资源 URI。资源加载器拒绝未知清单条目、空白标识符、重复帧和加载失败的帧，不会读取任意文件系统路径。

`idle` 必须至少解析出一帧；无效或缺少 idle 将安全显示当前静态占位图并写入不含路径的错误类别。每个动作允许 1–64 张帧，并以 `round(1000 / 帧数)` 毫秒计算自己的帧间隔；因此 8 张为 125 ms、3 张为 333 ms，单帧不启动计时器。任一非 idle 动作的清单无效、帧为空或帧无法加载时，播放器按 `fallback` 链选择动作；回退链循环、未知动作和最终无帧都归为 `idle`。这允许用户按状态逐步补充素材。

## 播放和切换规则

`MainWindow` 持有一个单独的动作播放器。播放器输入是本地归一化动作键和 `reducedMotion` 配置，输出是当前应绑定给角色 `Image` 的 `BitmapImage`。

1. Helper 启动和尚未收到状态时选择 `idle`。
2. 收到合法状态消息时，先在本地派生动作键并解析其可播放的回退动作。
3. 若解析后的动作与当前动作不同，立即显示新动作的第 1 帧，重置帧索引，并以该动作按实际帧数计算出的帧间隔启动或重设 `DispatcherTimer`。
4. 若解析后的动作相同，不重置帧索引或计时器，避免同一状态的重复消息造成闪动。
5. 每次计时器 tick 令帧索引按模增长并显示下一帧，因此所有已配置的状态动作都会循环。
6. `success` 和 `error` 不在播放器内部延时或自动切换；它们仅响应 Host 后续的 idle 状态，避免与现有 `CompanionBridge` 的终态计时器竞争。
7. 收到无效状态的现有协议层仍产生 `disconnected`；动作选择器随后按 `disconnected` 的回退规则显示。

当 `reducedMotion` 为真时，播放器停止计时器，进入动作或状态变化时只显示解析动作的第 1 帧。切回 false 时从当前帧继续循环；若当前动作仅有一帧，则不启动计时器。

## 代码边界

- `pet-helper/PetAnimationManifest.cs`：解析、验证和解析回退链的纯模型；不依赖 WPF 控件或协议读取器。
- `pet-helper/PetAnimationPlayer.cs`：加载受控嵌入资源、管理帧索引和 `DispatcherTimer`；只接受已验证的本地动作键。
- `pet-helper/PetDisplayState.cs`：增加从已验证展示状态导出八种动作键的纯方法；不改变 Host 协议类型。
- `pet-helper/MainWindow.xaml.cs`：在状态与 config 应用处调用播放器，并在关闭时停止计时器、释放图像引用。
- `pet-helper/PetHelper.csproj`：嵌入动作清单和用户后续放入的 `Assets/Animations/**/*.png`；保留现有 `placeholder-a.png` 作为 idle 的默认单帧资源，直到用户提供新的 idle 帧。
- `pet-helper.Tests/PetAnimationManifestTests.cs` 与 `pet-helper.Tests/PetDisplayStateTests.cs`：覆盖清单、回退、状态映射和减少动态效果可判断的纯逻辑。播放时序使用可注入的 tick 方法或调度器抽象测试，不依赖真实窗口。

不修改 `src/protocol.ts`、`src/companion-reducer.ts`、`src/companion-bridge.ts` 或事件适配器，因为所有动作选择发生在已验证状态到达 Helper 后。

## 测试策略

- 对八种规范展示模型分别断言动作键，并断言 `thinking-working` 只能由按规范排序的双活动组合产生。
- 验证一个多帧动作按帧序循环，状态变化从第 1 帧开始，而重复同一动作不会重置帧索引。
- 验证空帧动作、缺失资源、未知回退、回退循环均安全回退到 `idle`；`thinking-working` 缺失时优先使用 `working`。
- 验证减少动态效果固定首帧，关闭后恢复循环；单帧动作不创建运行中的计时器。
- 保留现有 Node 协议和状态桥接回归，确保动画实现没有增加 JSON Lines 字段、网络端口或不安全载荷。
- 在用户提供 PNG 后，验证所有被引用资源已嵌入、带 alpha 通道，并对两个发布所需的 idle 镜像资源维持现有 SHA-256 一致性要求。

## 验收标准

1. 八种展示情形均能稳定选择正确的本地动作或规定的待机回退。
2. 同一状态的 PNG 帧循环播放；重复状态消息不会闪回首帧；状态变化立即切换首帧。
3. 开启减少动态效果后没有动画 tick，且显示动作首帧。
4. 角色素材、资源标识符和路径从不出现在 JSON Lines、Node 日志或 DSH 事件处理代码中。
5. 没有新端口、网络服务或对 Session 内容的读取。
