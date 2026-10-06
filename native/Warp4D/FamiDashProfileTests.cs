using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class FamiDashProfileTests
{
    internal static int Run(string normalRom, string hugeRom, string outputDirectory)
    {
        string directory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(directory);
        string? previous = Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME", Path.Combine(directory, "isolated-data"));
        List<object> report = [];
        try
        {
            VerifySpriteDecoding();
            byte[] cameraRam = new byte[0x800]; cameraRam[0x49e] = 2;
            foreach (int linear in new[] { 239, 240, 255, 256, 479, 480, 719, 720, 1000 })
            {
                cameraRam[0x4ac] = (byte)linear; cameraRam[0x4ad] = (byte)(linear >> 8);
                Require(NesEmulator.TryGetFamiDashViewport(cameraRam, out _, out int y, 2) && y == linear % 480, "Linear camera boundary " + linear);
            }
            foreach (var entry in new[] { (Path: normalRom, Name: "famidash"), (Path: hugeRom, Name: "huge-man") })
            {
                using NesEmulator emulator = new(); emulator.Load(entry.Path);
                GameRecognitionProfile profile = emulator.BuiltInGameProfile ?? throw new InvalidOperationException("No built-in profile: " + entry.Name);
                GameRecognitionProfileStore.WriteToFile(Path.Combine(directory, entry.Name + ".warp4d-game.json"), profile);
                Require(profile.BackgroundRules.Count > 40, "Broad metatile coverage");
                Thread.Sleep(1800);
                List<object> phases = [];
                int projectedMax = 0, gameplaySamples = 0;
                for (int phase = 0; phase < 11; phase++)
                {
                    NesFrame frame = emulator.CaptureFrame()!;
                    using SmbScene scene = new SmbProfile().Build(frame, false, null, profile);
                    int backgrounds = scene.Objects.Count(o => !o.IdentityKey.StartsWith("sprite:"));
                    int state = frame.Ram[0x49e];
                    if (state == 2) { projectedMax = Math.Max(projectedMax, backgrounds); gameplaySamples++; }
                    else Require(backgrounds == 0, "Menu bank aliases suppressed");
                    phases.Add(new { Phase = phase, State = state, frame.ScrollX, frame.ScrollY, frame.ScrollSource, frame.SpritePatternBase, frame.LargeSprites, PpuControl = frame.Ram[9], LinearY = frame.Ram[0x4ac] | frame.Ram[0x4ad] << 8, Sprites = scene.Objects.Where(o => o.IdentityKey.StartsWith("sprite:")).Select(o => new { o.Bounds }).ToArray(), Backgrounds = backgrounds, Rules = profile.BackgroundRules.Count, Labels = scene.Objects.Select(o => o.Label).Distinct().ToArray() });
                    File.WriteAllText(Path.Combine(directory, entry.Name + "-phases.json"), JsonSerializer.Serialize(phases, new JsonSerializerOptions { WriteIndented = true }));
                    using WarpRendererControl renderer = new() { Size = new(900, 780) };
                    renderer.CreateControl(); renderer.SetScene(scene.Clone());
                    using Bitmap image = new(900, 780); renderer.DrawToBitmap(image, renderer.ClientRectangle);
                    image.Save(Path.Combine(directory, entry.Name + "-" + phase + ".png"), ImageFormat.Png);
                    if (state != 2) { emulator.SetButton(NesButton.Start, true); Thread.Sleep(100); emulator.SetButton(NesButton.Start, false); Thread.Sleep(1100); }
                    else { emulator.SetButton(NesButton.A, true); Thread.Sleep(180); emulator.SetButton(NesButton.A, false); Thread.Sleep(350); }
                    // A fresh save starts with mandatory credits. Do not mistake
                    // that timed introduction for a title/menu navigation failure.
                    for (int wait = 0; emulator.CaptureFrame()!.Ram[0x49e] == 0 && wait < 150; wait++) Thread.Sleep(300);
                }
                Require(gameplaySamples > 0 && projectedMax > 0, "Gameplay scenery recognized for " + entry.Name);
                report.Add(new { Version = entry.Name, BuiltIn = profile.Name, profile.RomSha256, Rules = profile.BackgroundRules.Count, GameplaySamples = gameplaySamples, MaximumRecognizedScenery = projectedMax, MenuAliasesSuppressed = true, BankAwareVariants = true });
            }
            foreach (string path in new[] { normalRom, hugeRom })
            {
                using MainForm main = new(); main.Show(); Application.DoEvents(); main.LoadRomForTest(path);
                Require(main.GameProfileNameForTest?.Contains("built-in") == true, "Main window automatically applies built-in profile");
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
                GameRecognitionProfile custom = BuiltInFamiDashProfile.Create(File.ReadAllBytes(path), hash)!;
                custom.Name = "User-owned FamiDash edits"; GameRecognitionProfileStore.Save(custom);
                main.LoadRomForTest(path);
                Require(main.GameProfileNameForTest == custom.Name, "Saved user profile takes precedence");
                main.Close();
            }
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { AutomaticProfileApplication = true, ExistingUserEditsPreserved = true, Games = report }, new JsonSerializerOptions { WriteIndented = true })); return 0;
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(directory, "error.txt"), exception.ToString()); return 1; }
        finally { Environment.SetEnvironmentVariable("WARP4D_HOME", previous); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void VerifySpriteDecoding()
    {
        byte[] chr = new byte[8192], palette = new byte[32];
        palette[17] = 0x16; palette[18] = 0x2a;
        // Odd index selects bank 1, even tile 2 on top, tile 3 below.
        for (int y = 0; y < 8; y++) { chr[4096 + 32 + y] = 0xff; chr[4096 + 48 + 8 + y] = 0xff; }
        using Bitmap normal = SmbProfile.DecodeSpriteTile(chr, palette, 3, 0, 0, true);
        using Bitmap flipped = SmbProfile.DecodeSpriteTile(chr, palette, 3, 0x80, 0, true);
        Require(normal.Height == 16 && normal.GetPixel(0, 0).A == 255 && normal.GetPixel(0, 15).A == 255, "Both sprite halves visible");
        Require(normal.GetPixel(0, 0) != normal.GetPixel(0, 15) && flipped.GetPixel(0, 0) == normal.GetPixel(0, 15), "Vertical flip swaps halves");
        Array.Clear(chr); chr[4096 + 32] = 0x80;
        using Bitmap horizontal = SmbProfile.DecodeSpriteTile(chr, palette, 3, 0x40, 0, true);
        Require(horizontal.GetPixel(7, 0).A == 255 && horizontal.GetPixel(0, 0).A == 0, "Horizontal sprite flip");
    }
}
