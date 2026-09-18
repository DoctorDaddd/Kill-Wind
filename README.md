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
- Screen Edit Mode: capture the next click in the attached game, scan the entered Int32, and auto-write only when the match is unique
- A small `MemoryTestGame.exe` used by integration tests

The UI is Electron for this first increment because this workspace does not include a .NET SDK. Core logic lives in independent modules and communicates through explicit service classes (`IProcessService`-style contracts in JSDoc) so a WPF/WinUI shell can be introduced later without moving scan logic into the UI.

Screen Edit Mode deliberately does not claim OCR. RPG Maker MV/MZ renders text into a canvas, so Windows UI Automation cannot read the clicked number reliably. The MVP captures the screen point, asks for the visible current/new Int32 values, and writes automatically only for a unique process-memory match; ambiguous matches remain in the candidate list for explicit selection.

## Run

```powershell
npm install
npm test
npm start
```

To create a portable Windows folder using the cached project runtime, or Electron downloaded by `npm install`:

```powershell
node scripts/build-native.cjs
node scripts/package-local.cjs
```

The result is `release/killwind/killwind.exe`. Keep the whole `release/killwind` folder together when moving it to another Windows machine.

`npm run build:native` compiles the small x64 Windows bridge and the test game using the Windows .NET Framework compiler available on the build machine. The bridge uses documented `OpenProcess`, `VirtualQueryEx`, `ReadProcessMemory`, and `WriteProcessMemory` APIs. It is scoped to local process inspection and does not include network, anti-cheat, DRM, or authentication features.
