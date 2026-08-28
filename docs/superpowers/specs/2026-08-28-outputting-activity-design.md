# DSH 输出中活动设计

## 目标

让桌宠在 DSH 已开始向用户输出文本时显示固定的“输出中…”气泡，并切换到专用的本地动画键。该功能只处理允许的事件类型和序号，不读取或转发任何模型文本。

## 事件与归约

- 仅用户可见文本适配为脱敏的 `responding`：`assistant/chunk.data.chunk.type === 'text-delta'`，或 `assistant/message.data.message.content` 含 `{ type: 'text' }` 块。`reasoning-delta`、`reasoning`、工具块和未知形状一律保持 `thinking`；不读取任何正文。
- `responding` 清除同一会话的 `thinking` 活动，并在 `turn/end`、`waiting`、`idle`、`success` 或 `error` 前持续显示。
- 新的工具开始事件清除 `responding`，恢复现有工作活动；下一个文本事件会再次进入输出中。
- 多会话优先级为：`waiting > error > working > responding > thinking > success > idle`。输出中不和其他活动组合，避免产生不清晰的三重气泡。

## 协议与 Helper

`active` 的 `activities` 白名单增加单项 `responding`，其唯一合法标签是“输出中…”。`responding` 不可与 `thinking` 或 `working` 组合；原有的 `thinking + working` 仍是唯一的双活动组合。WPF 将该固定载荷映射为 `Responding` 动画键；若没有专用 PNG 帧，清单会安全回退至 `idle`。

## 验收

- 首个助手文本事件显示“输出中…”，且后续 `step/end` 不会覆盖它。
- 新工具调用显示工作状态，下一次助手文本事件再切回输出中。
- TypeScript 和 C# 都拒绝未知、混合或标签不匹配的活动载荷。
- 事件适配器、日志和 JSON Lines 载荷中不包含助手文本。
