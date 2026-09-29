# Warp4D

Warp4D is a native Windows NES emulator experiment that gives selected game objects a controllable W-axis while keeping gameplay on one readable 2D screen. It is a WinForms desktop application backed by Mesen’s native NES core—there is no browser, HTML, WebView, Node.js, or local web server at runtime.

This build uses an actual four-coordinate geometry pipeline. Each transparent NES sprite bitmap forms sampled XY sheets in `XY × Z × W`, while merged segments from its exact pixel silhouette form the side walls. Every projected sheet carries its own six-plane rotation transform, with independently phased motion during auto-cycle; the central NES sprite remains the readable anchor. Geometry supports all six rotation planes—XY, XZ, XW, YZ, YW, and ZW—followed by a W-axis perspective divide from R⁴ to R³ and a Z-axis perspective divide from R³ to the monitor. The layers and side surfaces use the sprite’s own pixels and colors—not screen-space offset copies. Hovering an object reveals its 16-vertex, 32-edge diagnostic hyperframe.

The exact **Super Mario Bros. (World)** profile recognizes SHA-256:

`F61548FDF1670CFFEFCC4F0B7BDCDD9EABA0C226E3B74F8666071496988248DE`

For that ROM, Warp4D groups the game’s original 16×16 metatiles into whole bushes, clouds, hills, trees, pipes, bricks, question blocks, castles, and flagpoles. Terrain stays flat as a readable gameplay reference. Mario, enemies, items, and effects are reconstructed from live OAM and CHR data. Object labels use SMB’s own player/enemy RAM slots.

## Run

Download `Warp4D.exe` from the [latest Windows release](https://github.com/kandowontu2/Warp4D/releases/latest), launch it, then choose **Open ROM** or drag a `.nes` file onto the window. Warp4D always starts with no cartridge loaded; it never searches the computer for ROMs or automatically opens one. No ROM is included in this repository, executable, or release.

Controls:

- Arrow keys: move
- Z / X: B / A
- Enter: Start
- Right Shift: Select
- F2: reset
- Ctrl+P: open the projection profile editor
- Ctrl+G: open the game-profile creator for the loaded ROM
- Drag horizontally: XW rotation
- Drag vertically: YW rotation
- Sidebar: W extent, 4D camera perspective, true 0–100% layer opacity, W cross-sections, per-projection rotation spread, and all six base rotation planes (XY, XZ, XW, YZ, YW, ZW)
- Per-projection rotation: controls how far the individual sheets of each 4D object diverge from its base rotation. Set it to 0° for a rigid stack or increase it for independently oriented projections.
- Auto-cycle geometry and rotations: animates W extent, camera proximity, cross-sections, base rotation controls, and every projection sheet at its own phase and rate. Opacity and per-projection spread remain at their manually selected values.
- Projection: enable or disable 4D projection per recognized object class, adjust each class from 25–200% relative depth, apply character/scenery presets, and import or export shareable `.warp4d.json` projection profiles. Disabled classes remain visible as ordinary 2D NES objects.
- Game Profile: create recognition profiles for other games. The creator displays a live 16×16 metatile grid from the loaded ROM. Click a cell for a single selection, drag to paint across cells, or hold Ctrl while clicking/dragging to add or remove cells; one class and label can then be applied to every unique selected pattern at once. Esc or **Clear Cells** clears the selection. Captured pieces with the same label are joined into whole objects at runtime. New and updated rules bind both the tile numbers and visible tile artwork, preventing a graphics-bank swap from misidentifying menu art as gameplay scenery. Profiles can be imported or exported as `.warp4d-game.json` files.

For the exact SMB profile, the viewport is taken from SMB's stable screen-position RAM instead of the PPU register used temporarily for the fixed status bar. This prevents old nametable pages from flickering into the gameplay view.

For other games, a temporal viewport filter rejects one-frame PPU scroll values written for raster splits while preserving normal continuous scrolling and persistent scene transitions. This keeps menus such as FamiDash's title screen anchored instead of jumping between nametable pages.

The exact FamiDash build with SHA-256 `FDCC6C107CC64A245CE8F07188CBB50558EF435B908105F4CC88004D205BDA26` uses its logical RAM viewport directly. Its title and level-select screens stay fixed at their intended origin, while gameplay uses FamiDash's extended horizontal and vertical scroll coordinates.

The SMB title-screen attract demo is recognized as an active level scene, so its bushes, hills, blocks, pipes, and other detected scenery use the same 4D profile as normal gameplay. The static title screen remains 2D until the demo timer actually expires.

Gameplay keys remain routed to the NES while any Warp4D control has focus, including the auto-cycle checkbox and projection sliders.

Game audio plays through the Windows default output device using Mesen's native DirectSound path. Headless `--smoke` runs intentionally disable audio because they have no native window handle.

The active projection profile is saved to `%LOCALAPPDATA%\Warp4D\projection-profile.json` and loaded automatically the next time Warp4D starts.

Game-recognition profiles are bound to the ROM's SHA-256 and stored under `%LOCALAPPDATA%\Warp4D\game-profiles`. Generic NES sprites continue to be detected automatically; captured game-profile rules add background objects for non-SMB games.

## Build

On 64-bit Windows with .NET 8 SDK:

```powershell
.\build.ps1
```

The script publishes a self-contained, single-file `release\Warp4D.exe`. The 64-bit Mesen core is included in this repository and embedded into the application during publishing. The resulting EXE includes the .NET runtime and native core, requires no adjacent DLLs, and does not require .NET to be installed on the destination PC.

## License

Warp4D and its Mesen-linked build are GPL-3.0. See [`LICENSE`](LICENSE) and [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
