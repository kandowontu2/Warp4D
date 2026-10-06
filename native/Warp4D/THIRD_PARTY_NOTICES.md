# Third-party notices

Warp4D uses the native emulator core from **Mesen 0.9.9**.

- Copyright © 2014–2020 Sour
- License: GNU General Public License, version 3
- Corresponding source: <https://github.com/SourMesen/Mesen>
- Component distributed here: `MesenCore.dll` (64-bit Windows)

Warp4D's patched core is based on upstream revision `f3a18bed018fa853627e0e15d02a3f2ba4960222` (`0.9.9`). The complete modifications, pinned-source build instructions, and regression test are in `tools/native-core/` in the Warp4D source repository. Changes correct graphics-RAM address classification, add a synthetic test export, and add an explicit standard-library include for current MSVC. The C++ runtime is statically linked.

Mesen’s archived repository identifies 0.9.9 as the old release line and includes the complete source used to build the core. Warp4D is distributed under the same GPL-3.0 license; see `LICENSE.txt`.

No Nintendo ROM, game code, or copyrighted cartridge image is included. Users must provide their own legally obtained `.nes` file.
