# Third-party notices

Warp4D uses the native emulator core from **Mesen 0.9.9**.

- Copyright © 2014–2020 Sour
- License: GNU General Public License, version 3
- Corresponding source: <https://github.com/SourMesen/Mesen>
- Component distributed here: `MesenCore.dll` (64-bit Windows)

The patched core is based on upstream revision `f3a18bed018fa853627e0e15d02a3f2ba4960222`. Complete modifications and pinned-source rebuild instructions are in `tools/native-core/`; the complete modified native source is supplied as a release asset. The C++ runtime is statically linked.

Mesen’s archived repository identifies 0.9.9 as the old release line and includes the complete source used to build the core. Warp4D is distributed under the same GPL-3.0 license; see `LICENSE.txt`.

No Nintendo ROM, game code, or copyrighted cartridge image is included. Users must provide their own legally obtained `.nes` file.
