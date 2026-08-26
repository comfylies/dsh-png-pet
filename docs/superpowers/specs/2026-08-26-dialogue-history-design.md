# 桌宠对话历史与最后回复显示设计

## 目标

让桌宠的会话框显示选定会话的对话内容：气泡只显示**最后一条回复**（完整文本，可滚动），输入框顶部提供**历史按钮**展开最近对话历史（可滚动）。同时修复"发送后回复不显示"的既有缺陷。

## 用户可见行为

### 最后回复气泡

窗口内垂直堆叠：输入框在上，最后回复气泡在输入框下方。气泡状态机：

| 状态 | 表现 |
| --- | --- |
| 无任何回复 | 气泡隐藏，不占空间 |
| 消息已发送、回复尚未生成完毕 | 显示"正在生成回复…"（Helper 收到 `input-status: sent/queued` 时进入 pending；收到 `reply` 后替换；收到 `clear-preview` 或会话切换时回到隐藏） |
| 流式生成中（`previewEnabled` 开启） | 显示流式预览（沿用现有 `reply-preview`） |
| 生成完成 | 显示完整最后回复文本；超长气泡内滚动（MaxHeight） |
| 发送被拒或失败 | 气泡隐藏；失败反馈沿用输入框状态行（"未能发送"等） |

`reply`（最终回复）**不依赖** `previewEnabled` 开关；流式预览仍受开关控制。

### 历史面板

输入框标题行新增"历史"按钮（标题行布局：`[历史] 发送到已选会话 [×]`）。点击后打开全窗口覆盖层：

| 情况 | 表现 |
| --- | --- |
| 有历史 | 最近 20 条消息列表，可滚动；用户消息右对齐浅蓝，助手回复左对齐深色 |
| 无历史 | "暂无对话历史"空状态 |
| 历史加载中 | "加载中…" |
| 选定会话不可用 | 历史按钮点击提示"会话不可用"或按钮禁用 |
| 默认会话切换 | 历史与气泡清空并按新会话重载 |

历史面板打开时隐藏输入框、回复气泡与状态气泡；关闭后恢复原显示状态。

### 内容过滤（安全边界）

- 只显示：用户自己的文本消息（`source.kind === 'user'`）与助手最终文本回复（`assistant/message` content 中的 `text` 块）
- 剔除：reasoning/think 块、工具调用与结果、工具参数、文件路径、来源元数据、凭据
- 空文本消息跳过
- 历史条数 ≤ 20；单条文本 ≤ 2000 字符（超出保留尾部）；`reply` 文本 ≤ 8000 字符（超出保留尾部）

## 架构

### 插件端：新模块 `src/dialogue-history.ts`

纯函数提取器：

- 输入：`agent.session.events`（事件日志，来自 `ctx.agents.get(sessionId)?.session.events`；agent 不存在时先 `resume`）
- 遍历事件：
  - `user/message` 且 `data.source.kind === 'user'`：拼接 `data.content` 中 `type === 'text'` 的块
  - `assistant/message`：拼接 `data.message.content` 中 `type === 'text'` 的块（剔除 reasoning）；空文本跳过
  - 其余事件忽略
- 输出：按 seq 排序的 `{ role: 'user' | 'assistant', text }[]`，取最近 20 条

触发：

- `turn/end` 事件：重读日志，发送 `reply`（最后一条回复；若最后一条是用户消息则无回复，气泡回到"生成中/隐藏"判定）
- Helper 点击历史：发送 `request-history` 响应 `conversation-history`
- `sessionUnavailable` / 设置切换：清空并重载

### 协议：v4 → v5（`src/protocol.ts`）

新增消息：

- Helper → Host：`request-history` `{ version, kind, requestId }`
- Host → Helper：`reply` `{ version, kind, requestId, text, completed }`
- Host → Helper：`conversation-history` `{ version, kind, requestId, messages: [{ role, text }] }`

上限调整：

- `maxLineLength`：4096 → 65536（容纳 20 条历史的最坏情况）
- `reply` 文本 ≤ 8000；历史单条 ≤ 2000；角色仅 `user` / `assistant`
- 旧 Helper 收到 v5 时进入 `disconnected`（沿用现有协议版本校验）

### Helper 端

- `ConversationState`：新增 `ReplyText`、`ReplyPending`、`HistoryMessages`、`HistoryPending`、`HistoryUnavailable` 状态，处理 `reply` / `conversation-history` / `request-history` 语义
- `MainWindow`：新增回复气泡（输入框下方，ScrollViewer 滚动）、历史按钮、历史覆盖层面板；全部元素沿用 `LayoutTransform` 缩放（已有机制）
- 互斥逻辑扩展：历史面板打开时隐藏其余气泡；回复气泡显示时隐藏状态气泡（沿用 `UpdateStateBubbleVisibility`）

## 错误处理

- `agent.session` 读取失败：`reply` / `conversation-history` 不发送，Helper 保持上一状态
- 历史请求 requestId 过期（期间会话切换/Helper 关闭）：响应丢弃（沿用现有 requestId 守卫）
- 未知角色、超长文本、多余字段：协议校验拒绝（现有 `assertExactKeys` 风格）
- 发送链路失败（`input-status: rejected`）：回复气泡隐藏，不进入"生成中"

## 测试

- TS：`dialogue-history` 提取器（夹具含 reasoning/tool/空文本事件 → 只输出 user 与最终 text）；协议 v5 校验（新消息、上限、非法角色拒绝）；控制器触发逻辑（turn/end 发 reply、request-history 响应、会话切换清空）
- C#：`ConversationState` 处理 reply/history/空态/加载态；历史面板互斥
- 布局：静态断言（历史按钮、回复气泡、覆盖层存在且参与缩放）
- 集成：发布 Helper 握手与 v5 往返

## 发布

- 版本 0.1.19（插件 + Helper 同步，协议 v5）
- 需更新 `docs/项目需求书.md` 的对话显示边界描述（用户消息与模型最终回复属于用户明确要求的功能；仍不显示 reasoning、工具细节与凭据）

## 非目标

- 不实现会话搜索、分页、完整历史（只取最近 20 条）
- 不显示工具调用细节、reasoning、文件路径或凭据
- 不改变发送链路（`input` / `input-status` 语义不变）
