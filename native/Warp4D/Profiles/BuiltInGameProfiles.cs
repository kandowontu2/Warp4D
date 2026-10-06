using System.Security.Cryptography;
using System.Text.Json;

namespace Warp4D.Profiles;

internal static class BuiltInGameProfiles
{
    internal sealed record Cartridge(string Id, string Name, int Mapper, string Payload);
    internal static readonly Cartridge[] Cartridges = [
        new("metroid", "Metroid", 1, "649DB8035018F2512CCEA70ACA6606C3B3A6988CD9ED43953B38DC5103DEC7BB"),
        new("contra", "Contra", 2, "D41E28B1A33B3B6768E7C39C9FDFB1FDA4B49940542D14085911FABD399E1CA9"),
        new("tetris", "Tetris (Nintendo USA)", 1, "2AE5FB18A1BF841077E3872BA05060F030EA0BFC573994B2F8FE2FB570DC7853"),
        new("megaman2", "Mega Man 2", 1, "1E588D435E75D80C5C0B578B4FA8D196F2CF4346C11C9A7B7E435D768828AD01"),
        new("smb2", "Super Mario Bros. 2 USA", 4, "CBA920F9394733C82253685D7783F26A3033BA58A94623E9ABF7892329B969B9"),
        new("smb3", "Super Mario Bros. 3", 4, "D77D17D34AF24871D7CE1160CCD3330555835C8E940B7100E095AC38973D927A"),
        new("icarus", "Kid Icarus", 1, "56F1FE3A7881B2E9D69CD33A0971B2F26247E964C3C7DD4A6019715425FF2256"),
        new("castlevania", "Castlevania", 2, "A35E846379FF252594ACE83DA2A1A1CB0692717B931055D1F6603812F18AD5CD"),
        new("celeste", "Celeste Mario's Zap & Dash Deluxe", 5, "3ECA553C38ECD9AD374D6AF0D2DED9F97B6D8442A273AD583201699C9B70C321")
    ];
    internal static Cartridge? Identify(byte[] rom)
    {
        if (rom.Length < 16 || !rom.AsSpan(0,4).SequenceEqual("NES\u001a"u8)) return null;
        bool nes2 = (rom[7] & 12) == 8;
        if (nes2 && rom[9] != 0) return null;
        int mapper = (rom[6] >> 4) | (rom[7] & 240) | (nes2 ? (rom[8] & 15) << 8 : 0);
        int offset = 16 + ((rom[6] & 4) != 0 ? 512 : 0), length = rom[4] * 16384 + rom[5] * 8192;
        if (length == 0 || offset + length > rom.Length) return null;
        string hash = Convert.ToHexString(SHA256.HashData(rom.AsSpan(offset, length)));
        return Cartridges.FirstOrDefault(c => c.Mapper == mapper && c.Payload == hash);
    }
    internal static GameRecognitionProfile? Create(byte[] rom, string fileHash)
    {
        Cartridge? cartridge = Identify(rom); if (cartridge is null) return null;
        using Stream? resource = typeof(BuiltInGameProfiles).Assembly.GetManifestResourceStream("Warp4D.Profiles.Data." + cartridge.Id + ".json");
        if (resource is null) return null;
        GameRecognitionProfile profile = JsonSerializer.Deserialize<GameRecognitionProfile>(resource)!;
        profile.Name = cartridge.Name + " · built-in"; profile.RomName = cartridge.Name + ".nes";
        profile.RomSha256 = fileHash; profile.Normalize(); return profile;
    }
}
