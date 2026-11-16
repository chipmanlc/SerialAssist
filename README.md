# LuckC

A Windows serial port assistant for real-time receive/send, hex/ASCII switching, timestamps, logging, persistent command frames, and a shared send console.

## Build

```powershell
dotnet build SerialAssist.slnx
dotnet run --project SerialAssist.UI
```

## Single-file exe

Run `打包单文件exe.bat`.

## Installer

Run `制作安装包.bat`. The installer is written to `dist\LuckC-Setup.exe`.

Installed builds keep frame data in the app data directory and preserve it during uninstall.
