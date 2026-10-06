using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

// Explicit offline diagnostic: never opens a cartridge or runs at startup.
internal static class ProfileSceneDiffTests
{
    internal sealed record Sample(string Frame, string PriorProfile, string Profile, string Id="icarus");
    internal static int Run(string manifest, string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var samples = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(manifest))!;
            List<object> results = [];
            foreach (var sample in samples)
            {
                var frame = CartridgeViewport.NormalizeCapture(sample.Id, JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(sample.Frame))!);
                if (frame.CaptureScanline != 96 || frame.Sequence != frame.NativeScreenSequence || frame.NativeScreenPixels?.Length != 61440)
                    throw new InvalidDataException("Paired native fixture required.");
                using var prior = new SmbProfile().Build(frame, false, null, GameRecognitionProfileStore.ReadFromFile(sample.PriorProfile));
                using var current = new SmbProfile().Build(frame, false, null, GameRecognitionProfileStore.ReadFromFile(sample.Profile));
                var keys = prior.Objects.Select(o => o.IdentityKey).ToHashSet();
                var added = current.Objects.Where(o => !keys.Contains(o.IdentityKey)).Select(o =>
                {
                    int visible = 0, nativeMatches = 0;
                    for (int y = 0; y < o.Image.Height; y++)
                    for (int x = 0; x < o.Image.Width; x++)
                        if (o.Image.GetPixel(x, y).A != 0)
                        {
                            visible++;
                            int native = frame.NativeScreenPixels[(o.Bounds.Y + y) * 256 + o.Bounds.X + x];
                            if ((o.Image.GetPixel(x, y).ToArgb() & 0xffffff) == (native & 0xffffff)) nativeMatches++;
                        }
                    bool priorTextureExact=prior.Objects.Any(old=>old.Bounds==o.Bounds&&old.Image.Width==o.Image.Width&&old.Image.Height==o.Image.Height&&
                        Enumerable.Range(0,o.Image.Height).All(y=>Enumerable.Range(0,o.Image.Width).All(x=>old.Image.GetPixel(x,y).ToArgb()==o.Image.GetPixel(x,y).ToArgb())));
                    return new { o.IdentityKey, o.Label, o.Bounds, VisiblePixels = visible, NativeMatchingPixels = nativeMatches,
                        PriorBoundsAndTextureExact=priorTextureExact,o.ProjectionEnabled };
                }).ToArray();
                results.Add(new { sample.Frame, sample.PriorProfile, sample.Profile,
                    PriorProfileSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sample.PriorProfile))),
                    ProfileSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sample.Profile))),
                    PriorObjects = prior.Objects.Count, CurrentObjects = current.Objects.Count,
                    PriorPlayers=prior.Objects.Count(o=>o.Kind==SceneObjectKind.Player&&o.ProjectionEnabled),
                    CurrentPlayers=current.Objects.Count(o=>o.Kind==SceneObjectKind.Player&&o.ProjectionEnabled),Added = added });
            }
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { Passed = true, Samples = results }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), exception.ToString());
            return 1;
        }
    }
}
