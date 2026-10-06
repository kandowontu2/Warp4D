# Patched Mesen 0.9.9 core

Source revision: `f3a18bed018fa853627e0e15d02a3f2ba4960222` (upstream `0.9.9`). GPL-3.0; see the upstream source's LICENSE and Warp4D's LICENSE.txt.

`mesen-0.9.9-chr-ram.patch` contains all native changes. It guards three graphics-address lookups with `!_onlyChrRam`. Upstream reserves a zero-length CHR-ROM allocation for RAM-only cartridges, then sets `_chrRomSize` to the RAM size for page banking. That must not make the empty allocation a valid graphics-ROM address range. Nearby RAM/nametable allocations could otherwise be misclassified as ROM; the debugger's ROM counter arrays are empty and native execution can crash.

The patch also adds a synthetic regression diagnostic export and explicitly includes `<chrono>` for current MSVC compatibility. The regression forces an overlapping graphics-ROM pointer and checks all 8,192 graphics-RAM addresses across the three lookups. Unpatched source fails at the first address (0 checks); patched source completes 24,576 checks. No ROM is used by this test.

Build on Windows with Git, PowerShell 7, Visual Studio C++ Build Tools (v143), and a Windows SDK:

```powershell
pwsh -File tools/native-core/build-mesen-core.ps1
```

The script fetches pinned source into a fresh ignored artifact directory, applies the patch, builds the native DLL with a static C++ runtime, and runs the regression. It never automatically replaces the application's bundled DLL. Preserve and verify an old DLL before installing the new library and running packaged emulator/video/profile regressions.
