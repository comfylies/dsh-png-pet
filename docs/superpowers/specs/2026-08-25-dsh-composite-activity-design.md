# DSH 组合活动气泡设计

## 目标与根因

桌宠当前把每条事件归约为一个单一状态。DSH 对快速工具会依次提交 `tool/call` 和 `tool/result`；后者立即将前者的“工作中…”覆盖成“思考中…”，WPF 在下一次绘制前无法显示前者。本次改为展示同一 Session 的并发活动集合，使工具执行时稳定显示“思考中/工作中”。

本设计仍只使用事件类型、序号、Session 标识和 `turn/end.reason.kind`。不读取、保存、传递或展示工具参数、模型回复、路径、提示词、凭据或 Session 正文。

## 表现与选择规则

每个顶层 Session 维护两项可叠加活动：`thinking` 与 `working`。气泡标签由固定顺序生成：

| 活动集合 | 固定标签 |
| --- | --- |
| `thinking` | `思考中…` |
| `working` | `工作中…` |
| `thinking`、`working` | `思考中/工作中` |

`waiting`、`error`、`success`、`idle` 和 `disconnected` 仍是独占状态，不与活动集合拼接。多个顶层 Session 仍只选择一个展示候选，避免把不相关会话混为一条气泡；选择优先级为 `waiting > error > 含 working 的活动集合 > 仅 thinking 的活动集合 > success > idle`。并列候选取较大事件序号。默认忽略子 Agent 的规则不变。

## 事件归约

- `turn/start`、`step/start`、`assistant/chunk`、`assistant/message`、`step/end` 和 `approval/decided`：标记 `thinking` 活动。
- `tool/call` 与 `tool/code-dispatch-start`：将该 Session 的进行中工具计数加一，并保留已有的 `thinking` 活动。
- `tool/result` 与 `tool/code-dispatch`：将进行中工具计数减一，最小为零；不会清除 `thinking`。
- `approval/asked`：产生独占 `waiting`；`approval/decided` 返回活动展示。
- `turn/end`：清空活动和工具计数，并按既有规则产生 `success`、`error` 或 `idle`。
- `session/disposed`：删除该 Session 的全部记录。

每条事实仍以递增序号校验；重复、倒序、无效、已释放 Session 的事实一律忽略。没有与先前工具开始事件配对的结束事件仅将计数保持在零，不会产生负数或异常。

## 协议 v3

v2 的单一 `state` 与固定 `label` 无法表示组合标签，故 Host 与 Helper 同步升级为 v3。进行中活动使用安全的规范化数组，而非任何自由文本：

```json
{"version":3,"kind":"state","state":"active","activities":["thinking","working"],"label":"思考中/工作中","sequence":42}
```

数组只允许 `thinking` 和 `working`，必须非空、去重，并按上表固定顺序排列；`label` 必须等于 Host/C# 共同的固定映射生成结果。独占状态使用空 `activities`，并继续要求原有固定标签。TypeScript 编码器与 C# 解析器均拒绝不匹配版本、未知活动、非规范顺序、错误标签、额外字段及非法序号。

新 Helper 收到 v2 Host 或不合法的 v3 消息时显示 `disconnected` 后关闭；新 Host 收到 v2 Helper 的 `ready` 时拒绝握手。升级包中的 Host 与 Helper 始终同版本发布。

## 测试与范围

- 先为 Reducer 编写失败测试：工具开始后输出 `thinking + working`，工具结束后保留 `thinking`，并行工具的计数正确，快速连续开始/结束不会丢失组合状态，多会话优先级和释放清理保持正确。
- 为事件适配器、协议编码/解析和 C# 展示模型增加 v3 组合活动及非法载荷测试。
- 保留 Helper 端到端 JSON Lines 回归，验证中文组合标签可显示、进程持续运行且不出现自由文本。

本次不新增“输出中”或动画，不改变 DSH 设置、端口、桌宠资产或其他生命周期行为。未来若加入输出活动，将在新的规格中扩展允许的活动集合和固定标签映射。
