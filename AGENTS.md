# DSH PNG 桌宠：续开发指南

## 项目边界

- 仅支持 Windows 10/11 x64。
- DSH/Node.js 负责插件、Session、模型与设置；C# WPF Helper 是唯一可见界面。
- 插件与 Helper 只能经 stdin/stdout JSON Lines 通信；不得创建 HTTP、WebSocket 或其他本地端口。
- 不读取、保存、输出或传递 API Key、授权头、提示词、模型回复、工具参数、文件路径或 Session 正文。

## 当前状态

- 包版本为 dsh-png-pet@0.1.1。
- DSH web profile 已安装 dsh-png-pet@0.1.1；已运行的 Harness 必须重启才能加载更新。
- 已实现：Helper 生命周期、透明窗口、拖动、位置保存、缩放、右键菜单、托盘恢复/退出、透明鲸主题形象。
- 未实现：真实 DSH 事件订阅和状态归约、状态气泡/动画、DSH 设置同步、打开 DSH、本次关闭抑制重启和崩溃退避。
- 下一阶段设计见 docs/下一阶段-DSH状态桥接.md。

## 关键路径

- src/index.ts：DSH 插件入口和 Helper 生命周期。
- src/protocol.ts：JSON Lines 协议；扩展字段时必须同步 TypeScript、C# 和测试。
- src/helper-process.ts：Helper 子进程启动、握手、发送和关闭。
- pet-helper/：WPF Helper。
- pet-helper/Assets/placeholder-a.png：WPF 嵌入的当前形象。
- assets/placeholder-a.png：随 npm 包发布的镜像资源；必须与 WPF 资源保持相同内容。
- scripts/build-helper.ps1：发布自包含 exe 到 runtime/bin/win32-x64/。

## 修改形象

1. 同时替换 pet-helper/Assets/placeholder-a.png 与 assets/placeholder-a.png。
2. 检查两者 SHA-256 相同，并保留 PNG alpha。
3. 递增 package.json 和 package-lock.json 中的版本号。
4. 运行构建、测试、打包并更新 DSH 安装包。

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
- pet-helper.exe 单文件发布必须保留 IncludeNativeLibrariesForSelfExtract=true。
- 运行 DSH 或 Codex 子进程时可能缺少 WINDIR；App.xaml.cs 已从有效的 SystemRoot 补齐该环境变量。
- Git 可能提示无法访问 C:\Users\root\.config\git\ignore；这是环境权限警告，不要为此修改项目文件。

## 工作方式

- 修改功能前，先阅读对应设计和计划文档，采用测试先行并保留验证证据。
- 不要覆盖或删除用户已有改动；先检查 git status。
- 二进制资源无法用文本补丁处理时，可在用户明确授权后复制；源代码和文本配置使用 apply_patch。
