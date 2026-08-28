# DSH 状态桥接设计

## 目标

将 DSH `0.1.1-rc.2` 已提交的 Session 事件归约为桌宠的安全可见状态。桌宠只显示固定中文短标签，绝不读取、保存、转发或记录提示词、模型回复、工具参数、路径、凭据或 Session 正文。

## 已验证的 DSH 接口

当前运行的 DSH 位于 `C:\Users\root\AppData\Roaming\npm\node_modules\@deepseek-ai\dsh`，版本为 `0.1.1-rc.2`。插件可注册以下公开观察器：

- `ctx.on('session/event', (session, event) => void)`：事件已提交到 Session 日志后触发；观察器异常被 DSH 隔离。
- `ctx.on('session/disposed', (session) => void)`：已发布 Session 离开存储时触发一次。

适配器只读取 `session.id`、`session.header.delegationDepth`、`event.type`、`event.seq` 及 `turn/end` 的 `reason.kind`。它不会读取 `event.data` 的其他字段。

## 事件事实与归约

`src/companion-reducer.ts` 定义纯函数 `CompanionReducer`。它接受已脱敏的事实，而不是 DSH 的原始 Session 或原始事件：

```ts
type CompanionState =
  | 'idle' | 'thinking' | 'working' | 'waiting'
  | 'success' | 'error' | 'disconnected'

type SessionFact = {
  sessionId: string
  seq: number
  isSubagent: boolean
  kind: 'thinking' | 'working' | 'waiting' | 'success' | 'error' | 'idle'
}

type SessionDisposedFact = { sessionId: string }
```

每个 Session 保存最后接收的序号和当前状态。序号不大于已见序号的事实、无效事实和已释放 Session 的后续事实均被忽略。`session/disposed` 删除该 Session；没有候选 Session 时输出 `idle`。

适配器使用下面的白名单映射：

| DSH 事件 | 事实状态 |
| --- | --- |
| `turn/start`、`step/start`、`assistant/chunk`、`assistant/message`、`step/end` | `thinking` |
| `tool/call`、`tool/code-dispatch-start` | `working` |
| `tool/result`、`tool/code-dispatch`、`approval/decided` | `thinking` |
| `approval/asked` | `waiting` |
| `turn/end` 且 `reason.kind === 'completed'` | `success` |
| `turn/end` 且 `reason.kind === 'error'` 或 `'max-tokens'` | `error` |
| `turn/end` 的 `aborted`、`blocked`、`interrupted` 或未知结束原因 | `idle` |

未知事件一律不产生事实。默认忽略 `session.header.delegationDepth` 大于零的子 Agent；Reducer 配置 `includeSubagents: true` 后才接纳它们。

多 Session 的可见状态从候选状态中按 `waiting > error > working > thinking > success > idle` 选择。并列状态取较大的事件序号；这使结果在同一输入序列下确定。`success` 与 `error` 是短暂状态：Host 在成功状态展示 5 秒、错误状态展示 2.5 秒后，仅当同一展示序号仍是当前输出时发送 `idle`，新的事实会取消旧计时器。

`disconnected` 不来自 Session 事件：它只由协议校验、握手失败或 Host 停止触发。

## 协议 v2

JSON Lines 协议升级到 v2。Host 到 Helper 的状态消息仅包含下列字段：

```json
{"version":2,"kind":"state","state":"working","label":"工作中…","sequence":42}
```

Host 只能使用以下固定映射：`idle` 为空标签、`thinking` 为“思考中…”、`working` 为“工作中…”、`waiting` 为“等待你的操作”、`success` 为“已完成”、`error` 为“发生错误”、`disconnected` 为“未连接”。

`config` 为 `{"version":2,"kind":"config","scale":1,"reducedMotion":false}`，其中 `scale` 只能是 `0.75`、`1`、`1.25` 或 `1.5`。`hello` 与 `shutdown` 没有业务载荷；Helper 到 Host 的 `ready` 与 `closed` 同样仅带版本和种类。

TypeScript 编码器和 C# 解析器都拒绝错误版本、未知 kind、未知状态、错误标签组合、非安全整数序号、超长行和额外业务字段。新 Helper 收到不兼容 Host 消息时先显示 `disconnected`，随后正常退出；新 Host 收到旧 Helper 的 v1 `ready` 时拒绝握手并清理该进程，避免旧界面静默显示为 idle。

## Helper 表现

`MainWindow` 保持透明 PNG 形象，并在其上方显示仅绑定固定标签的气泡。idle 不显示气泡；waiting 使用更醒目的视觉样式。UI 不绑定或展示任何来自 DSH 的自由文本。C# 的纯协议/展示模型验证状态、标签、序号和配置；无效输入切换至 `disconnected`，不会抛出到协议读取循环。

## 错误处理与生命周期

所有 DSH 观察回调包在脱敏错误边界内：只记录事件类别和错误类别，绝不记录 Session id、消息内容或事件载荷。助手进程启动后先发送 v2 `hello`、默认 `config` 和初始 `idle`；插件卸载或 DSH 停止仍沿用 `shutdown` / `closed` 有序关闭。通信失效不创建网络端口，也不阻塞 DSH。

## 测试策略

- TypeScript：先用录制的脱敏 `SessionFact` 测试每一种状态、顺序、重复、已释放事件、顶层优先级、子 Agent 开关和终态计时器的防过期规则。
- TypeScript 协议：测试 v2 的完整编码与所有非法载荷拒绝，并验证 Helper 只收到枚举、固定标签和序号。
- C#：测试 Host 行解析、未知值回退 `disconnected`、固定气泡标签和配置范围；不使用真实 DSH Session。
- 集成：扩展 fake Helper，断言插件观察回调产生的 stdin 行不含自由文本、路径或凭据，并保留有序退出回归。

## 范围

本设计实现状态桥接和静态状态气泡。它不实现 PNG 帧动画、DSH 设置持久化、菜单回传、打开 DSH、崩溃重启退避或任何新的本地端口。
