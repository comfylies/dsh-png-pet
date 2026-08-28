# DSH PNG 桌宠：续开发指南

## 项目边界

- 仅支持 Windows 10/11 x64。
- DSH/Node.js 负责插件、Session、模型与设置；C# WPF Helper 是唯一可见界面。
- 插件与 Helper 只能经 stdin/stdout JSON Lines 通信；不得创建 HTTP、WebSocket 或其他本地端口。
- 不读取、保存、输出或传递 API Key、授权头、提示词、工具参数或文件路径。
- 桌宠输入、回复预览、完整回复和按需加载的最近对话历史可仅通过本地 JSON Lines 暂存和显示；它们不得写入日志、文件或设置，也不得发送到网络。

## 当前状态

- 当前包版本为 dsh-png-pet@0.1.25，协议版本为 v5。安装状态不是源码事实；需要时用 `dsh plugin --profile web list` 确认。已运行的 Harness 必须重启才能加载更新。
- 已实现：Helper 生命周期及有序关闭、透明窗口、拖动、位置保存、缩放、右键菜单、托盘恢复/退出、状态气泡、基于真实 DSH Session/Agent 事件的状态归约、多 Session 优先级、目录化 v3 PNG Clip 清单、完整片段播放与减少动态效果回退。
- 已实现：DSH Web“桌宠”设置（默认会话、回复预览及长度）、桌宠输入发送、回复显示/预览，以及按需读取并显示有限的对话历史。
- 未实现：打开 DSH、本次关闭抑制自动重启、Helper 崩溃退避，以及将缩放和减少动态效果持久化到 DSH 设置。

## 关键路径

- src/index.ts：DSH 插件入口和 Helper 生命周期。
- src/protocol.ts：JSON Lines 协议；扩展字段时必须同步 TypeScript、C# 和测试。
- src/helper-process.ts：Helper 子进程启动、握手、发送和关闭。
- src/companion-reducer.ts、src/dsh-event-adapter.ts、src/companion-bridge.ts：DSH 事件适配、状态归约和安全状态桥接。
- src/dialogue-controller.ts、src/dialogue-history.ts、src/dialogue-settings.ts、src/client.tsx：桌宠对话、受限历史、设置和 Web 设置页。
- pet-helper/：WPF Helper。
- pet-helper/Assets/pet-animations.json：v3 状态索引；每个状态目录的 animation.json 声明 Clip。
- pet-helper/Assets/Animations/idle/breathe/：当前 idle 呼吸动画，32 帧、125 ms/帧、循环时长 4 秒。
- pet-helper/Assets/placeholder-a.png：WPF 嵌入的当前形象。
- assets/placeholder-a.png：随 npm 包发布的镜像资源；必须与 WPF 资源保持相同内容。
- scripts/build-helper.ps1：发布自包含 exe 到 runtime/bin/win32-x64/。

## 修改形象

1. 主形象仍须同时替换 pet-helper/Assets/placeholder-a.png 与 assets/placeholder-a.png。
2. 检查两者 SHA-256 相同，并保留 PNG alpha。
3. 动作帧放在 pet-helper/Assets/Animations/<动作键>/<片段键>/，同步更新状态目录的 animation.json 及根 pet-animations.json；缺失动作必须回退 idle。对视频帧先用 scripts/remove-backgrounds.ps1 在本地 rembg 去背景，确认透明 PNG、统一画布和基线后再导入。
4. 递增 package.json 和 package-lock.json 中的版本号。
5. 运行构建、测试、打包并更新 DSH 安装包。

## 验证与发布

~~~powershell
npm test
dotnet test pet-helper.Tests\PetHelper.Tests.csproj --no-restore
npm run build:helper
npm run test:package
npm pack
~~~

从包含空格的工作区路径安装 tgz 会触发 DSH CLI 的路径解析问题。先复制到无空格路径再安装：

~~~powershell
New-Item -ItemType Directory -Force C:\dsh-packages
Copy-Item .\dsh-png-pet-<version>.tgz C:\dsh-packages\
dsh plugin --profile web remove dsh-png-pet
dsh plugin --profile web add C:\dsh-packages\dsh-png-pet-<version>.tgz
dsh plugin --profile web list
~~~

构建前检查是否运行了 runtime/bin/win32-x64/pet-helper.exe；Windows 会锁住该文件，导致复制失败。应让用户从右键菜单关闭桌宠；仅在用户同意时终止该进程。

## 环境注意事项

- 使用 .NET 10 SDK；项目内的 NuGet.Config、Directory.Build.props 和 Directory.Build.targets 用于避开机器级失效的 NuGet fallback 路径，不要删除。
- 当前终端的 PATH 不包含全局 npm bin；调用 DSH CLI 时使用 `C:\\Users\\root\\AppData\\Roaming\\npm\\dsh.cmd`，不要假设裸命令 `dsh` 可用。
- pet-helper.exe 单文件发布必须保留 IncludeNativeLibrariesForSelfExtract=true。
- 运行 DSH 或 Codex 子进程时可能缺少 WINDIR；App.xaml.cs 已从有效的 SystemRoot 补齐该环境变量。
- Git 可能提示无法访问 C:\Users\root\.config\git\ignore；这是环境权限警告，不要为此修改项目文件。
- rembg 首次安装会下载本地模型；scripts/install-rembg.ps1 把依赖放入项目的 .tools/rembg，scripts/remove-backgrounds.ps1 仅写入单独的输出目录，绝不覆盖原图。

## 工作方式

- 修改功能前，先阅读对应设计和计划文档，采用测试先行并保留验证证据。
- 不要覆盖或删除用户已有改动；先检查 git status。
- 二进制资源无法用文本补丁处理时，可在用户明确授权后复制；源代码和文本配置使用 apply_patch。
