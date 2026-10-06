namespace Warp4D.Emulation;

// Only called for payload-identified cartridges, never filename guesses.
internal static class CartridgeViewport
{
    internal static bool TryGet(string? id, byte[] ram, out Point viewport)
    {
        viewport = Point.Empty;
        if (ram.Length < 0x101) return false;
        if (id == "smb2" && ram.Length > 0x4c5)
        {
            // Original NMI_AfterBackgroundAttributesUpdate commits FF to
            // PPUCTRL, then FD and (FC + BackgroundYOffset) to PPUSCROLL.
            // Asynchronous PPU reads can instead see the drawing-buffer scroll.
            // Preserve the byte-sized shake offset and 240-line nametable pages.
            int y = (ram[0xfc] + ram[0x4c5]) & 0xff;
            viewport = new(ram[0xfd] + (ram[0xff] & 1) * 256,
                y % 240 + ((ram[0xff] & 2) != 0 ? 240 : 0));
            return true;
        }
        if (id == "castlevania")
        {
            // Original game's UpdateScreenScrolling: FD is scroll X; FF is
            // its PPUCTRL shadow, including nametable selection.
            viewport = new(ram[0xfd] + (ram[0xff] & 1) * 256, 0);
            return true;
        }
        if (id == "icarus")
        {
            // Original EBC9..EBF4 writes FE/FD to PPUSCROLL and saves the
            // committed page bits in the 0100 PPUCTRL shadow. Use that shadow,
            // not 1A/1B which can be ahead during level setup. Vertical pages
            // are 240 lines, not 256; temporary interface writes don't replace it.
            viewport = new(ram[0xfe] + (ram[0x100] & 1) * 256,
                ram[0xfd] % 240 + ((ram[0x100] & 2) != 0 ? 240 : 0));
            return true;
        }
        return false;
    }

    // Replay old diagnostic captures through the same verified camera mapping.
    internal static NesFrame NormalizeCapture(string id, NesFrame frame) =>
        // Coherent split-HUD snapshots contain the committed PPU camera from
        // this emulated frame. RAM mirrors can already describe the next frame.
        // Normalize older coherent SMB3 diagnostics without rewriting evidence.
        id is "smb2" or "smb3" or "castlevania" or "icarus" && frame.CaptureScanline == 96 && frame.NativeScreenSequence == frame.Sequence
            ? frame with { ScrollX = frame.RawScrollX, ScrollY = frame.RawScrollY, ScrollSource = "Committed playfield PPU viewport" }
            : TryGet(id, frame.Ram, out Point viewport)
            ? frame with { ScrollX = viewport.X, ScrollY = viewport.Y, ScrollSource = "Verified cartridge RAM viewport" }
            : frame;
}
