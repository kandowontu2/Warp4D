using System.Security.Cryptography;

namespace Warp4D.Emulation;

internal static class SmbCartridgeIdentity
{
    // Known, unmodified PRG+CHR data, independent of iNES/NES 2.0 metadata,
    // trainers and trailing dump metadata. Never infer RAM compatibility by name.
    private const string World = "FCB6A0EF3A20C19B356005FBB21DC8009563B1CB5A9AAEBC8E9386B4A8C5912E";
    private const string Europe = "6B3189414053F975BCAC33EB51A1E9991E0D06F42ABEAD7919969417CC26E2AD";
    internal static bool IsSupported(byte[] rom)
    {
        if (rom.Length < 16 || rom[0] != 'N' || rom[1] != 'E' || rom[2] != 'S' || rom[3] != 0x1a || rom[4] != 2 || rom[5] != 1) return false;
        bool nes2 = (rom[7] & 0x0c) == 8;
        int mapper = (rom[6] >> 4) | (rom[7] & 0xf0) | (nes2 ? (rom[8] & 15) << 8 : 0);
        if (mapper != 0 || nes2 && rom[9] != 0) return false;
        int offset = 16 + ((rom[6] & 4) != 0 ? 512 : 0);
        const int payloadLength = 32768 + 8192;
        if (rom.Length < offset + payloadLength) return false;
        string hash = Convert.ToHexString(SHA256.HashData(rom.AsSpan(offset, payloadLength)));
        return hash is World or Europe;
    }
}
