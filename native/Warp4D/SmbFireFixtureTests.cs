using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

// Explicit diagnostic fixtures only; never applies setup or loads a ROM.
internal static class SmbFireFixtureTests
{
    internal static int Run(string input, string output, string game = "world")
    {
        Directory.CreateDirectory(output);
        try
        {
            Require(game is "world" or "europe", "Explicit supported fixture group required.");
            string[] poses = ["active-small", "assisted-fire-standing", "fire-right", "release-right", "face-left", "fire-left", "airborne-fire"];
            // Frozen native OAM observations, independent of projected objects.
            // Shuffling can leave OAM on the preceding RAM-offset phase.
            int[][] shots = game == "world" ? [[], [], [10], [10], [32], [32,33], [32,33]] : [[], [], [50], [32], [32], [32,33], [50]];
            using ArchiveFrameReader frameReader = new();
            using var archive = File.Exists(input) ? ZipFile.OpenRead(input) : null;
            string? archiveHash = archive is null ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
            Dictionary<string,string> indexHashes = [];
            if (archive is not null)
            {
                using var index = new StreamReader(archive.GetEntry("__coverage_archive_index.tsv")!.Open());
                while (index.ReadLine() is string row) { var fields = row.Split('\t'); indexHashes.Add(fields[0],fields[2]); }
            }
            List<object> results = [];
            int projectilePixels = 0;
            long previous = -1;
            for (int index = 0; index < poses.Length; index++)
            {
                string file = Path.Combine(input, $"{index:D2}-{poses[index]}.frame.json");
                string frameKey = archive is null ? file : archive.Entries.Single(entry => entry.FullName.EndsWith($"/{index:D2}-{poses[index]}.frame.json",StringComparison.Ordinal)).FullName;
                var frame = frameReader.Read(frameKey,archive is null ? null : input,archiveHash,archive is null ? null : indexHashes[frameKey]);
                Require(frame.Sequence > previous && frame.NativeScreenSequence == frame.Sequence && frame.CaptureScanline == 96, "Ordered paired native captures required.");
                previous = frame.Sequence;
                Require(frame.Ram[0x770] == 1 && frame.Ram[0x772] == 3 && frame.Ram[0x0e] == 8 && frame.Ram[0x6e4] == 4, "Active SMB gameplay and reserved body slots required.");
                Require(frame.Ram[0x754] == (index == 0 ? 1 : 0) && frame.Ram[0x756] == (index == 0 ? 0 : 2), "Actual native small/fire form required.");
                if (index == 2) Require(frame.Ram[0x33] == 1 && frame.Ram[0x24] != 0, "Native right-facing fireball state required.");
                if (index == 5) Require(frame.Ram[0x33] == 2 && frame.Ram[0x24] != 0 && frame.Ram[0x25] != 0, "Left-facing shot must coexist with first fireball.");
                if (index == 6) Require(frame.Ram[0x1d] == 1 && frame.Ram[0xce] < 176, "Native airborne fire form required.");
                using var scene = new SmbProfile().Build(frame,true);
                foreach (int slot in shots[index])
                {
                    int offset = slot * 4;
                    Require(frame.Oam[offset] < 239 && frame.Oam[offset+1] is 0x64 or 0x65 && (frame.Oam[offset+2]&3)==2, "Reviewed fireball tile/palette must exist.");
                    Rectangle bounds = new(frame.Oam[offset+3],frame.Oam[offset]+1,8,8);
                    var candidates = scene.Objects.Where(actor => actor.Kind != SceneObjectKind.Player && actor.Bounds == bounds).ToArray();
                    using var slotsJson = JsonDocument.Parse($"[{slot}]");
                    projectilePixels += ReviewedSpriteBitmapTests.Verify(frame,candidates,slotsJson.RootElement,poses[index]+"/fireball"+slot);
                }
                results.Add(SmbNaturalGrowthTests.Save(game, poses[index], frame, output));
            }
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
            {
                Passed = true, Cases = results.Count, ExactReservedBody = true,
                AssistedFormSetup = true, NaturallyEarnedFireFlower = false,
                Game = game, ExactSeparatelyProjectedFireballPixels = projectilePixels,
                Scope = "Seven isolated first-stage native small/fire poses, exact body bitmap/native colors and frozen fireball tile bitmap separation. Not enemy-adjacent/explosion/projectile semantic-label coverage, swimming/full-game or physical display.",
                Results = results
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString());
            return 1;
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
