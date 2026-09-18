# KillWind

Windows-only local editor for offline single-player process inspection. This repository is self-contained under `D:\CodeX\KillWind`.

## MVP scope

- Process listing with PID, path, architecture, memory usage, and start time
- Attach/detach with process-exit protection
- Virtual memory region enumeration
- Int32 exact first scan and next scan
- Safe Int32 read/write through a native Windows bridge
- Freeze service with per-entry intervals
- Address list and JSON profile persistence
- WPF native desktop shell with debugger-style layout and local EXE packaging
- Unknown Initial Value scan with changed/unchanged/increased/decreased filtering
- Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Float, and Double scanning
- Scan history with undo of the previous filter
- A small `MemoryTestGame.exe` used by integration tests

The primary UI is a native WPF desktop executable. The existing Electron shell remains available as a fallback and for compatibility checks; it is not required by the WPF EXE. Core scanning and native process access remain separate from the UI shell.

Screen Edit Mode deliberately does not claim OCR. RPG Maker MV/MZ renders text into a canvas, so Windows UI Automation cannot read the clicked number reliably. The MVP captures the screen point, asks for the visible current/new Int32 values, and writes automatically only for a unique process-memory match; ambiguous matches remain in the candidate list for explicit selection.

## Run

```powershell
npm install
npm test
npm start
```

`npm start` builds and launches the WPF desktop version. The native build currently uses the Windows .NET Framework C# compiler already present on the build machine, so a full .NET SDK is not required for this local build.

To create a portable Windows folder using the cached project runtime, or Electron downloaded by `npm install`:

```powershell
node scripts/build-native.cjs
node scripts/build-wpf.cjs
node scripts/package-wpf.cjs
```

The result is `release/killwind-wpf/KillWind.exe`. Keep the whole `release/killwind-wpf` folder together when moving it to another Windows machine. The Electron fallback can still be packaged with `npm run package:electron`.

`npm run build:native` compiles the small x64 Windows bridge and the test game using the Windows .NET Framework compiler available on the build machine. The bridge uses documented `OpenProcess`, `VirtualQueryEx`, `ReadProcessMemory`, and `WriteProcessMemory` APIs. It is scoped to local process inspection and does not include network, anti-cheat, DRM, or authentication features.
