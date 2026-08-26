# DSH 桌宠输入稳定性修复设计

## 目标

修复首次运行时桌宠提交输入导致 DSH Harness 卸载插件、Helper 随之退出的问题；同时恢复 Helper 在缺少 `WINDIR` 的 DSH 子进程环境中的启动兼容性。

## 根因

DSH SettingsProvider 在不存在 `dsh-png-pet` 设置分节时，将 `undefined` 交给注册的 schema。当前 schema 只为对象属性声明默认值，未为根对象声明默认值，因此返回 `undefined`。`DialogueController` 随后读取该对象的 `defaultSessionId` 或 `previewEnabled`，使异步回调拒绝。

`routeHelperMessage()` 用 `void` 启动该 Promise 而没有拒绝处理，因此异常继续传播到 Harness，触发 fatal load failure。Harness 断开 stdin 后，Helper 依照既有协议退出，表现为桌宠闪退。

## 设计

### 设置默认值

`src/dialogue-settings.ts` 的公开 schema 必须对 `undefined` 和空对象产生相同的完整安全默认配置：未选择会话、关闭预览、480 字符预览上限。控制器不需要猜测或重建默认设置。

### 输入失败隔离

`DialogueController.acceptInput()` 必须将其整个输入处理边界内的意外异常转化为对应请求的固定 `rejected` 状态。不得记录或回传输入文本、会话标识、异常正文或 Session 正文。`routeHelperMessage()` 仍以异步方式调度控制器，但必须消费任何残余 Promise 拒绝，保证消息回调不会让 Harness 崩溃。

### Helper 启动环境

`src/helper-process.ts` 恢复在启动 Helper 时由 `SystemRoot` 补齐缺失的 `WINDIR`，且保留已有 `WINDIR` 与其他继承环境变量不变。这只影响 Windows 子进程环境，不增加端口或新通信通道。

## 测试

- 在 TypeScript 设置测试中断言 schema 接收 `undefined` 时返回完整安全默认值。
- 在对话控制器测试中让设置 scope 返回 `undefined`，断言输入获得固定 `rejected` 状态且 Promise 正常完成。
- 在入口测试中让输入控制器返回拒绝 Promise，断言路由不会产生未处理拒绝。
- 恢复 Helper 环境单元测试，验证仅在需要时补齐 `WINDIR`。
- 运行完整 Node 测试、打包测试；在环境允许时运行 .NET Helper 测试。当前机器的 .NET SDK 权限错误需单独说明，不将其视为代码失败。

## 非目标

- 不变更 JSON Lines v4 协议、WPF 输入界面或 DSH Web 设置页面。
- 不读取、记录或输出用户输入、模型回复、会话正文、文件路径、凭据或异常详情。
- 不修改现有的会话选择、预览关联或状态桥接逻辑。
