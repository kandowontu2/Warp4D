using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D;

internal static class SmbRegressionTests
{
    internal static int Run(string worldRom, string europeRom, string outputDirectory)
    {
        string directory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(directory);
        string? previous = Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME", Path.Combine(directory, "isolated-data"));
        List<object> report = [];
        try
        {
            byte[] data = File.ReadAllBytes(worldRom);
            Require(SmbCartridgeIdentity.IsSupported(data), "World identity");
            byte[] altered = (byte[])data.Clone(); altered[7] = 0; Array.Clear(altered, 8, 8);
            Require(SmbCartridgeIdentity.IsSupported(altered), "Legacy iNES header");
            byte[] padded = new byte[altered.Length + 128]; altered.CopyTo(padded, 0);
            Require(SmbCartridgeIdentity.IsSupported(padded), "Trailing metadata");
            byte[] trainer = new byte[altered.Length + 512]; altered.AsSpan(0, 16).CopyTo(trainer); trainer[6] |= 4;
            altered.AsSpan(16).CopyTo(trainer.AsSpan(528));
            Require(SmbCartridgeIdentity.IsSupported(trainer), "Trainer offset");
            altered[16] ^= 1; Require(!SmbCartridgeIdentity.IsSupported(altered), "Modified game rejected");
            altered = (byte[])data.Clone(); altered[6] |= 0x10;
            Require(!SmbCartridgeIdentity.IsSupported(altered), "Wrong mapper rejected");
            Require(!SmbCartridgeIdentity.IsSupported(data[..100]), "Truncation rejected");
            foreach (var entry in new[] { (Path: worldRom, Name: "world"), (Path: europeRom, Name: "europe") })
            {
                using NesEmulator emulator = new(); emulator.Load(entry.Path);
                Require(emulator.IsSmbWorld, "Built-in recognition " + entry.Name);
                Thread.Sleep(900); emulator.SetButton(NesButton.Start, true); Thread.Sleep(120); emulator.SetButton(NesButton.Start, false); Thread.Sleep(3000);
                emulator.SetButton(NesButton.Right, true); emulator.SetButton(NesButton.B, true);
                for (int step = 0; step < 28; step++)
                { emulator.SetButton(NesButton.A, step % 2 == 0); Thread.Sleep(250); if (emulator.CaptureFrame()!.ScrollX >= 235) break; }
                emulator.SetInputMask(0); emulator.TogglePause(); Thread.Sleep(80);
                NesFrame frame = emulator.CaptureFrame()!;
                Require(frame.ScrollX > 0 && frame.ScrollX == frame.RawScrollX && frame.CaptureScanline==96 && frame.NativeScreenSequence==frame.Sequence, "Paired committed scrolling " + entry.Name);
                using Bitmap composed = SmbProfile.ComposeBackground(frame, true);
                using Bitmap shifted = SmbProfile.ComposeBackground(WithScroll(frame, frame.ScrollX + 137), true);
                Require(Differences(composed, shifted, new(0, 0, 256, 32)) == 0, "HUD independent of world scroll");
                Require(Differences(composed, shifted, new(0, 32, 256, 208)) > 100, "World actually scrolls");
                using SmbScene scene = new SmbProfile().Build(frame, true);
                Require(scene.Objects.Any(o => o.Kind == SceneObjectKind.Pipe && o.ProjectionEnabled), "Pipe recognized");
                Require(scene.Objects.Any(o => o.Kind is SceneObjectKind.Bush or SceneObjectKind.Hill or SceneObjectKind.Cloud), "Scenery recognized");
                Require(scene.Objects.Where(o => o.Label == "HUD coin").All(o => !o.ProjectionEnabled), "HUD coin flat");
                Require(scene.Objects.Any(o => o.Kind == SceneObjectKind.Terrain && o.ProjectionEnabled), "Ground blocks projected");
                Require(scene.Objects.Where(o => o.Kind is SceneObjectKind.Brick or SceneObjectKind.Terrain or SceneObjectKind.QuestionBlock).All(o => o.Bounds.Width <= 16 && o.Bounds.Height <= 16), "Independent metatile blocks");
                using WarpRendererControl renderer = new() { Size = new(820, 740), UseGpu = false };
                renderer.CreateControl(); renderer.SetScene(scene.Clone());
                renderer.DepthAmount = 0;
                using Bitmap flat = new(820, 740); renderer.DrawToBitmap(flat, renderer.ClientRectangle);
                renderer.DepthAmount = 1; renderer.ProjectionOpacity = 1; renderer.SliceCount = 9;
                renderer.AngleXWDegrees = 130; renderer.AngleYWDegrees = 100; renderer.AngleZWDegrees = 110;
                using Bitmap rotated = new(820, 740); renderer.DrawToBitmap(rotated, renderer.ClientRectangle);
                RectangleF bounds = renderer.GameBoundsForTest;
                Rectangle hud = Rectangle.FromLTRB((int)Math.Ceiling(bounds.Left) + 2, (int)Math.Ceiling(bounds.Top) + 2, (int)Math.Floor(bounds.Right) - 2, (int)Math.Floor(bounds.Top + bounds.Height * 32 / 240f) - 2);
                Require(Differences(flat, rotated, hud) == 0, "HUD protected from projected scenery");
                rotated.Save(Path.Combine(directory, entry.Name + "-projected.png"), ImageFormat.Png);
                composed.Save(Path.Combine(directory, entry.Name + "-scroll.png"), ImageFormat.Png);
                report.Add(new { Version = entry.Name, BuiltIn = emulator.IsSmbWorld, frame.ScrollX, frame.ScrollSource,
                    HudFixed = true, HudProjectionProtected = true, BlocksIndependent = true,
                    Objects = scene.Objects.Select(o => new { o.Label, Kind = o.Kind.ToString(), o.Bounds, o.ProjectionEnabled }).ToArray() });
            }
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { CartridgeIdentityTests = true, Games = report }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(directory, "error.txt"), exception.ToString()); return 1; }
        finally { Environment.SetEnvironmentVariable("WARP4D_HOME", previous); }
    }
    private static NesFrame WithScroll(NesFrame f, int scroll) => new() { NametablePixels = f.NametablePixels, Tiles = f.Tiles, Attributes = f.Attributes, Oam = f.Oam, Chr = f.Chr, Palette = f.Palette, Ram = f.Ram, ScrollX = scroll, ScrollY = f.ScrollY, ScrollSource = f.ScrollSource };
    private static int Differences(Bitmap a, Bitmap b, Rectangle rectangle)
    {
        int count = 0; for (int y = rectangle.Top; y < rectangle.Bottom; y++) for (int x = rectangle.Left; x < rectangle.Right; x++) if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) count++; return count;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
