# 本地批量去背景（rembg）

此脚本使用本地 `rembg` 处理图片，不会上传任何素材，也不会覆盖原图。输出是透明背景 PNG，适合导入桌宠动作目录前的素材准备。

## 安装一次

在已安装 Python 3.11+ 的 PowerShell 中运行：

```powershell
.\scripts\install-rembg.ps1
```

首次运行 `rembg` 会下载所选的本地模型；之后所有图片处理均在本机完成。

## 批处理

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\remove-backgrounds.ps1 `
  -InputDirectory .\images\video-frames-20260827-212350 `
  -OutputDirectory .\images\generated\video-frames-transparent
```

默认使用广泛兼容的 `u2net`。如素材主要是人物，可改用：

```powershell
-Model u2net_human_seg
```

先以少量图片试运行并检查轮廓；确认透明通道、画布尺寸和主体边缘正常后，再将结果复制到 `pet-helper/Assets/Animations/<动作键>/` 并更新相应 `animation.json`。不要直接覆盖原始帧。
