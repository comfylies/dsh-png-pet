# DSH PNG 桌宠第一阶段设计

## 目标

交付一个可安装到 DeepSeek Harness（DSH）`web` profile 的 Windows 插件骨架：DSH 加载插件时启动一个自包含的 C# WPF 桌宠 Helper，DSH 停止或插件关闭时 Helper 有序退出。桌宠仅显示 A 风格占位 PNG，不读取模型凭据、不调用网络服务。

## 范围

本阶段实现 npm 插件包、TypeScript 生命周期管理、JSON Lines 协议、.NET 10 Windows x64 自包含 WPF Helper、协议握手和关闭，以及测试与构建脚本。

本阶段不订阅 DSH Session 事件、不进行状态归约、不实现托盘/右键菜单/拖拽位置持久化，也不显示会话或模型内容。这些功能保留到需求书中的后续阶段。

## 组件与职责

`src/index.ts` 是 DSH Host 入口，只负责注册插件生命周期并创建或释放 `HelperProcess`。

`src/protocol.ts` 是协议边界，定义并校验从插件到 Helper 的 `hello`、`config`、`state`、`shutdown` 消息，及从 Helper 到插件的 `ready`、`closed` 消息。所有消息各占一行 JSON，协议版本固定为 `1`。

`src/helper-process.ts` 负责定位随 npm 包发布的 `runtime/bin/win32-x64/pet-helper.exe`、以受控子进程启动它、按行解析 stdout、向 stdin 写入协议消息，并在超时后终止未响应的 Helper。

`pet-helper/` 是 WPF 项目。启动后显示无边框、透明、置顶且不出现在任务栏的窗口，并呈现包内 A 风格占位图。它先输出 `ready`；收到版本正确的 `shutdown` 后输出 `closed` 并正常退出。

## 数据流与生命周期

1. DSH 装载插件，Host 入口创建 `HelperProcess`。
2. 插件启动 `pet-helper.exe`，等待最多五秒的 `ready`。
3. 握手成功后，插件依次发送 `hello`、默认 `config` 和 `idle` `state`，使 Helper 保持占位空闲外观。
4. DSH 关闭、插件禁用或初始化失败时，插件发送 `shutdown`，最多等待两秒的 `closed`；若仍运行才终止子进程。
5. Helper 非预期退出只记录不含会话内容的错误类别；本阶段不自动重启，避免隐藏集成故障。

## 安全边界

插件仅使用 DSH 的插件生命周期接口和父子进程 stdin/stdout。它不读取环境中的 API Key、Token 或 DSH 配置，不开监听端口，不直接发起 HTTP 请求。协议和诊断日志不得承载提示词、回复、工具参数、路径或会话正文。

## 测试策略

TypeScript 单元测试覆盖协议序列化/校验、错误消息拒绝以及 Helper 生命周期的握手、正常关闭和超时清理。C# 单元测试覆盖协议行解析和关闭命令识别。构建验证生成 npm 包，并检查包中含编译后的 JavaScript、Helper 可执行文件和占位资源。

## 验收条件

- Helper 在插件启用后只启动一个实例，并显示 A 风格占位桌宠。
- Helper 在插件停止时在有限时间内退出，不遗留后台进程。
- 无效或未知协议行不会使 Helper 崩溃。
- 发布的 Windows x64 Helper 为 .NET 10 自包含产物，用户无需另装 .NET。
- 代码、日志、协议与包内容中不含模型密钥和会话内容。
