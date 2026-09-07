# Windows 一行安装

提供 PowerShell 5.1 可执行的 install.ps1、release.json 和预编译 tgz。发布地址由发布者配置；未配置时生成可本地验证的发布目录，不输出虚假在线安装地址。最终用户不编译代码、不安装 .NET SDK。

安装器校验 Windows x64、清单版本、精确依赖版本、HTTPS 下载和 SHA-256。复用兼容 Node/DSH；缺失时在 LOCALAPPDATA/DSHPet 下安装专用 Node、DSH 和 pnpm，不更改机器 PATH。已有不兼容 DSH 时安全停止，允许显式 UsePrivateDsh 建立独立 DSH_HOME。凭据由 DSH 首次启动配置，安装器不读取或复制会话、凭据及配置文件。

通过 node 执行 DSH 的 JS CLI，仍由 dsh plugin --profile web 管理插件。DSH 当前通过 Windows shell 转发 pnpm，包路径通过带引号的专用环境变量传入，防止空格和 shell 字符被重新解析。所有包先下载校验，再 add；不先 remove。查询 list --json 的版本验证安装；失败且有匹配的已校验旧包时尝试恢复。安装状态只保存版本、运行环境位置和包哈希。

重复安装同版只修复启动入口；更新不终止用户进程，检测到桌宠运行时要求正常退出。启动入口位于稳定用户目录，使用安装时选定的 Node/DSH、pnpm 和 DSH_HOME。安装成功不自动启动 Host；显示快捷方式及模型配置说明。卸载通过 DSH CLI 仅移除桌宠并删除属于本安装器的快捷方式，保留运行环境与 DSH 数据。

发布清单固定 Node 24.15.0、DSH 0.1.1-rc.2、pnpm 11.23.0（当前本机集成版本），后续组合须验证后更新；Node ZIP 哈希取自 nodejs.org 的 SHASUMS256.txt。npm 依赖依然联网解析，当前不承诺完全离线或依赖闭包逐字节锁定。

测试覆盖清单拒绝、哈希失败、依赖选择、路径含空格/中文、重复安装、安装失败恢复、卸载保留数据与安装器互斥。发布执行 Node、C#、自包含构建及实际打包握手测试。另在隔离 DSH_HOME 验证真实 DSH/pnpm 参数传递，不修改现有 web profile。
