# 桌宠右键菜单控制设计

## 目标

让用户无需离开桌宠即可选择默认 DSH 会话，并开关模型回复预览；保留现有隐藏、缩放、重置和关闭操作。

## 用户界面

桌宠窗口的右键菜单新增：

- `默认会话` 子菜单：列出 Host 下发的近期可用会话。当前默认会话带勾选标记；点击其他条目立即设为默认会话。
- `显示回复预览`：可勾选项，对应既有 `previewEnabled` 设置。

当会话列表为空时，`默认会话` 子菜单显示并禁用 `暂无可用会话`。当 Host 尚未下发菜单配置时，两个设置项禁用。菜单不显示会话正文、工作目录、模型回复、提示词或任何凭据。

现有的 `隐藏`、`缩放`、`重置大小与位置` 和 `关闭桌宠` 保持原有行为。托盘菜单维持 `显示桌宠` 与 `退出桌宠`，不复制会话选择控件。

## 协议和数据流

JSON Lines v4 增加两类固定、脱敏消息：

1. Host → Helper `menu-config`：携带当前默认会话 ID、`previewEnabled` 和最多限定数量的会话 `{ id, title }` 列表。`id` 只用于后续设置写入，不在窗口中展示；标题为空时 Host 使用固定后备标题。
2. Helper → Host `menu-action`：携带严格枚举的动作：`set-default-session`（带一个已在当前 `menu-config` 中的 ID）或 `set-preview-enabled`（带布尔值）。

Helper 保存最近一次 `menu-config`，仅对其中存在的 ID 产生会话切换动作。Host 收到动作后通过已注册的 DSH 设置 scope 更新 `defaultSessionId` 或 `previewEnabled`。设置 watcher 提交后重新发布 `menu-config` 与已有 `conversation-config`，因此界面状态以 Host 的已提交设置为准。

## 状态与失败处理

- Helper 不自行推测设置成功；点击后保持当前勾选状态，直至下一次 Host `menu-config` 到达。
- Host 忽略 Helper 传来的未知动作、缺失字段、过长标题或不在已发布列表中的会话 ID。
- 设置写入失败不会记录原始错误，也不清空当前默认会话；Host 重新发布最后一次已提交配置。
- Helper 的协议解析继续拒绝多余字段、错误版本、未知 kind 和长度超限的行。

## 架构边界

- `src/protocol.ts` 是 TypeScript 消息契约与边界校验的唯一来源。
- `src/dialogue-controller.ts` 负责将 DSH settings/session 投影为脱敏菜单配置，并处理固定菜单动作。
- `src/index.ts` 负责路由 Helper 菜单动作到控制器。
- `pet-helper/ProtocolMessage.cs`、`ProtocolReader.cs` 和窗口状态类映射对应协议。
- `MainWindow.xaml` 与 `MainWindow.xaml.cs` 只负责渲染和发出固定菜单动作；不读取 DSH 设置或会话数据。

## 测试

- TypeScript：验证菜单配置的字段界限、未知/过期会话动作被拒绝、有效会话和预览动作写入正确设置并重新发布配置。
- C#：验证 Helper 仅渲染最新菜单配置、禁用空列表项、选中状态和预览复选框正确，且未知消息不改变 UI 状态。
- 集成：验证 Helper 生命周期与 JSON Lines 往返；测试中只使用虚构 ID 和标题，不包含会话正文。

## 非目标

- 不实现会话搜索、分页、会话创建、会话删除或会话正文预览。
- 不添加 HTTP、WebSocket 或任何本地端口。
- 不在菜单中显示模型回复内容或修改输入投递逻辑。
