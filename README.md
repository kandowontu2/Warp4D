# Warp4D

A native Windows NES emulator that gives game objects a fourth spatial dimension while keeping gameplay on one screen.

[Download the latest release](https://github.com/kandowontu2/Warp4D/releases)

## Getting started

Download the Windows x64 ZIP, extract it, and run **Warp4D.exe**. Use **Open ROM** or drag in your own `.nes` file. No browser, separate .NET installation, included ROM, or automatic ROM loading is required.

## What's new in the 1.2 preview

- GPU-rendered object projections and reusable background/overlay storage.
- Eleven live style buttons, six rotation planes, independent projection rotations, auto-cycle, randomization, and adjustable opacity.
- A visual game-profile editor with additive selection, drag selection, import/export, Undo/Redo, and explicit Save & Apply.
- Expanded profiles for Super Mario Bros., FamiDash, FamiDash Huge Man, Metroid, Contra, Tetris, Mega Man 2, Super Mario Bros. 2 USA, Super Mario Bros. 3, Kid Icarus, Castlevania, and Celeste Mario's Zap & Dash Deluxe.
- Improved sprite ownership, HUD protection, SMB swimming/fire forms, Castlevania player/bat layouts, and Contra base scenery.
- Clean gameplay recording and customizable keyboard/controller input.

Profile coverage varies by game and scene; this remains a preview.

## Controls and editing

Use the on-screen controls to choose your preferred look. Press **Ctrl+G** to edit a game's recognition profile, **Ctrl+L** for presentation settings, **Ctrl+O** to open a ROM, **F11** for fullscreen, and **F12** for a screenshot.

Saved custom profiles take precedence over built-in profiles. Export a backup before replacing a custom profile. Castlevania's **UPDATE PLAYER TRACKING** and **UPDATE SPRITE SHAPES** controls update their respective layouts while preserving custom scenery; click **SAVE & APPLY** when ready. Updated player-layout profiles require this preview or a compatible newer reader.

## Build from source

Install the .NET 8 SDK on Windows, then run:

```powershell
./build.ps1
```

The self-contained executable is written to `release/Warp4D.exe`. The repository includes the native Mesen core. Its pinned upstream source, complete patch, and rebuild instructions are in [tools/native-core](tools/native-core/README.md); the complete modified native source is also supplied with the release.

## License

GPL-3.0. See [LICENSE](LICENSE) and [third-party notices](native/Warp4D/THIRD_PARTY_NOTICES.md). No ROMs are distributed.
