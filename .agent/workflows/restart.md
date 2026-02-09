---
description: 终止当前进程、构建并重启应用 (Terminate current process, build, and restart application)
---

// turbo-all
1. 运行以下 PowerShell 命令进行自动重启：

```powershell
Stop-Process -Name RefScrn -Force -ErrorAction SilentlyContinue; dotnet build; Start-Process "bin\Debug\net8.0-windows10.0.19041.0\RefScrn.exe"
```
