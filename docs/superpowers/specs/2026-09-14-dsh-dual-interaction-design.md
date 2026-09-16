# DSH Web 与桌宠同时处理批准与问卷：开发设计

> 当前实现（v0.2.13 / 协议 v18）以本节和 `2026-09-15-questionnaire-helper.md` 为准：`pet` 支持桌宠批准与分页问卷；`both` 保留原生 Web 回答权，桌宠只读镜像并同步结束状态。下文的统一 Broker、双端抢答及设置重命名仍是设计目标，未宣称完成。保留 `approvalSurface` 字段和默认 `web`，避免改变已有用户的路由选择。实际协议沿用独立 `approval-*` / `question-*` 消息，`*-request` 包含必填 `answerable`，新增 `question-cancel`。所有题目、选项与答复均严格校验；不截断语义，无法完整表示的问卷回退 Web。

## 目标

当 DSH 会话产生权限批准（`approval/asked`）或用户问卷（`ask_user_question`）时，DSH Web 与桌宠都能在同一时间看到待处理提示。两端共享一个 Host 侧请求状态和一次性决策；任一端完成回答后，另一端立即变为已处理，绝不重复提交或产生竞态批准。

范围限定为当前选中的会话。Helper 仍是本地 UI，不联网；模型、工具和最终 DSH 决策仍由 DSH 提供。

## 现状与设计取舍

当前 `ApprovalController` 只在 `approvalSurface === pet` 时接管批准，否则让 DSH Web 处理；协议只有无详情的 `approval-request`。`question` 事件只改变桌宠状态，点击后回到 Harness。新设计废弃单选 surface，改为通知策略：`web`、`pet`、`both`（默认 `both`）。Web 是 DSH 原生 UI，桌宠是镜像入口；Host 只允许一个批准/问卷答复。

## 统一请求模型

新增 Host 内存对象 `InteractionRequest`，不写入设置、日志或文件：

- `requestId`：Host 单调递增、仅本次进程有效。
- `kind`：`approval` 或 `questionnaire`。
- `sessionId`：仅 Host 内部用于会话匹配，不跨 Helper。
- `status`：`pending | resolved | expired | unavailable`。
- `expiresAt`：建议 10 分钟；超时按关闭处理。
- 问卷仅保留经白名单校验的短文本、选项和值类型；禁止 reasoning、工具参数、路径、凭据和原始事件。

`InteractionBroker` 负责创建、广播、仲裁和清理；`ApprovalController` 改为其 approval 适配器，问卷新增 `QuestionnaireController`。两者共享 `PendingInteractionStore`，保证同一会话最多一个可回答请求。

## 协议 v18

升级 TypeScript/C# 两端协议版本并拒绝旧版本。新增消息（字段必须 exact-match，所有文本有长度上限）：

```ts
// Host -> Helper；不含 sessionId、工具参数或敏感详情
{ kind: 'interaction-request', requestId: number,
  interaction: 'approval' | 'questionnaire',
  title: string, prompt?: string,
  options?: readonly { id: string, label: string }[] }
{ kind: 'interaction-resolved', requestId: number,
  interaction: 'approval' | 'questionnaire',
  outcome: 'allowed-once' | 'rejected' | 'answered' | 'cancelled' | 'expired' | 'unavailable' }

// Helper -> Host
{ kind: 'interaction-answer', requestId: number,
  interaction: 'approval' | 'questionnaire',
  outcome?: 'allowed-once' | 'rejected' | 'cancelled',
  answers?: readonly { optionId: string, value?: string }[] }
{ kind: 'open-harness', requestId?: number }
```

`answers` 只允许选项 ID 和受限短文本（单选/多选/短文本三种），不允许 Helper 回传问题原文以外的数据。重复、倒序、未知 ID、过长文本和不匹配的 interaction 一律丢弃。Host 对 Web 侧的回答也走同一 `resolveOnce`，收到首个合法结果后向 Helper 广播 `interaction-resolved`。

为兼容 DSH 原生 Web：Web 的批准/问卷回调注册到 `InteractionBroker`，不再由 `approvalSurface` 决定是否接管。若 DSH 没有可注册的问卷回调，则保留 `question` 状态并发送 `open-harness`，桌宠只作为通知入口。

## Web 与桌宠行为

- `both`：Web 显示原生批准/问卷卡；桌宠显示气泡、`waiting`/`question` 动画和“在桌宠回答 / 打开 DSH”按钮。任一端回答后，另一端卡片立即禁用并显示结果。
- `web`：仅 Web 可回答，桌宠仅显示状态和“打开 DSH”。
- `pet`：仅桌宠可回答，Web 收到一个非阻塞镜像提示并可打开桌宠；此模式用于无障碍或 Web 不可用场景。

桌宠关闭、Helper 崩溃、会话切换、Host dispose 或超时都调用 `resolveUnavailable`/`expire`；批准必须 fail-closed。问卷超时返回取消并让 DSH 走其默认中断路径。DSH Web 自己取消时同样广播 resolved，桌宠不得继续显示可操作按钮。

## 状态归约与并发

`waiting` 仍优先于工作状态；有待处理批准时显示 `waiting`，有待处理问卷时显示 `question`。多个会话按现有会话优先级归约，但交互只广播 selected session；非选中会话不弹桌宠卡。请求 ID 与 DSH 事件序号分离，禁止用事件 `seq` 充当答复 ID。

## 设置与迁移

将 `approvalSurface` 重命名为 `interactionSurface: 'web'|'pet'|'both'`，默认 `both`。读取旧设置时：`web -> web`、`pet -> pet`；未知值回退 `both`。Web 设置页提供三个选项，并分别说明“通知”和“可回答”语义。问卷不新增联网或后台调度开关。

## 实施顺序

1. 新增 `pending-interaction-store.ts` 与 broker 单元测试：首答胜出、重复答复、超时、Helper 不可用、会话切换。
2. 升级 `src/protocol.ts` 到 v18；同步 C# `ProtocolMessage`、`ProtocolReader`、序列化和协议测试。
3. 重构 `approval-controller.ts` 接入 broker；增加问卷回调适配和安全投影提取器。
4. 修改 `src/index.ts` 注册 Web 回调并广播；删除基于 `approvalSurface` 的单路短路。
5. Helper 增加 `InteractionState`、批准/问卷卡、键盘可达性、已处理状态和“打开 DSH”按钮；不保存问题或答案。
6. 更新设置 schema、Web 设置页、迁移测试和文档。
7. 运行 `npm test`、`dotnet test pet-helper.Tests\\PetHelper.Tests.csproj --no-restore`、`npm run build:helper`、`npm run test:package`。

## 验收标准

- Web 和桌宠在 300 ms 内都显示同一 requestId 的待处理提示。
- 双端同时点击只产生一个 DSH 决策；第二次点击得到 `already-resolved`/静默忽略。
- 任何断线、超时、未知载荷都不会导致批准；日志和 IPC 中不出现工具参数、路径、凭据或 reasoning。
- 问卷支持受限选项回答；不支持的 DSH 问卷安全回退到 Web。
- 旧 v17 消息被拒绝，v18 两端 exact-match 校验通过。

## 第一阶段实现状态（2026-09-15）

已落地 `approvalSurface: "both"`：当 Web 保持 DSH 原生批准权时，Host 会将无敏感内容的 `approval-request` 镜像发送给桌宠，并在 Web 决策后发送 `approval-resolved`；桌宠按钮在该模式下仍不能改变 Web 决策。现有 `question` 状态已经让问卷同时在 Web 提问、桌宠显示「等你回答…」提示。

尚未开放桌宠直接提交结构化问卷答案；这需要按 `user-questions/request` 的完整题目/选项模型扩展 v18 协议和 WPF 问卷控件，下一阶段按本文的 InteractionBroker 实现。
