Option Explicit

' 双击此文件会在后台启动 DSH Web Host。桌宠由已安装的 DSH 插件自动拉起。
' 优先使用全局安装的 dsh；未安装时才回退到 npx 的临时运行方式。

Dim shell, fileSystem, appData, dshPath, npxPath, launchCommand

Set shell = CreateObject("WScript.Shell")
Set fileSystem = CreateObject("Scripting.FileSystemObject")
appData = shell.ExpandEnvironmentStrings("%APPDATA%")
dshPath = appData & "\npm\dsh.cmd"
npxPath = appData & "\npm\npx.cmd"

If fileSystem.FileExists(dshPath) Then
  launchCommand = Quote(dshPath) & " web --no-open"
ElseIf fileSystem.FileExists(npxPath) Then
  launchCommand = Quote(npxPath) & " -y @deepseek-ai/dsh web --no-open"
Else
  MsgBox "未找到 DSH。请先安装 Node.js，并运行：" & vbCrLf & _
    "npm install -g @deepseek-ai/dsh", vbExclamation, "无法启动 DSH 桌宠"
  WScript.Quit 1
End If

' 0 = 隐藏控制台；False = 启动后立即返回，DSH 持续运行直到用户退出它。
shell.Run "cmd.exe /d /s /c " & Quote(launchCommand), 0, False

Function Quote(value)
  Quote = Chr(34) & value & Chr(34)
End Function
