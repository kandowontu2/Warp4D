namespace Warp4D.Emulation;

internal sealed record NesFrame
{
    public const int ScreenWidth = 256;
    public const int ScreenHeight = 240;
    public const int NametablePixelCount = ScreenWidth * ScreenHeight;

    public required int[][] NametablePixels { get; init; }
    public required byte[][] Tiles { get; init; }
    public required byte[][] Attributes { get; init; }
    public required byte[] Oam { get; init; }
    public required byte[] Chr { get; init; }
    public required byte[] Palette { get; init; }
    public required byte[] Ram { get; init; }
    public int ScrollX { get; init; }
    public int ScrollY { get; init; }
    public int RawScrollX { get; init; }
    public int RawScrollY { get; init; }
    public required string ScrollSource { get; init; }
    public long Sequence { get; init; }
    public int SpritePatternBase { get; init; }
    public bool LargeSprites { get; init; }
    // Optional for older captures. Taken with the same playfield metadata,
    // including the NES's independent left-eight-pixel BG/sprite enables.
    public byte? PpuMask { get; init; }
    public int[][]? HudNametablePixels { get; init; }
    public int[]? NativeScreenPixels { get; init; }
    // Diagnostic provenance: scanline snapshots paired at frame completion.
    public int? CaptureScanline { get; init; }
    public long? NativeScreenSequence { get; init; }
    // Optional explicit-calibration evidence; ordinary emulation does not read it.
    public byte[]? CalibrationWorkRam { get; init; }

    // A uniform completed frame plus a fully collapsed palette has no visible
    // object silhouette. Lighting must not resurrect stale black OAM shapes.
    // Require coherent native evidence; never infer this from an old screenshot
    // or merely a black backdrop (which can contain visible black sprites).
    internal bool IsUniformPairedFade()
    {
        if(CaptureScanline is null || NativeScreenSequence!=Sequence ||
            NativeScreenPixels is not {Length:NametablePixelCount} native || Palette.Length<32)return false;
        int color=native[0]&0xffffff;
        for(int i=0;i<32;i++)
            if((Warp4D.Rendering.NesPalette.Get(Palette[i]).ToArgb()&0xffffff)!=color)return false;
        for(int i=1;i<native.Length;i++)if((native[i]&0xffffff)!=color)return false;
        return true;
    }
}
