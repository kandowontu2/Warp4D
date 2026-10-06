using System.Drawing.Imaging;
using System.Diagnostics;
using System.Text.Json;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class GeometryTests
{
    internal static int Run(string outputDirectory)
    {
        string directory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(directory);
        string? priorHome = Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME", Path.Combine(directory, "isolated-data"));
        Dictionary<string, object> report = [];
        try
        {
            PresentationSettings old = JsonSerializer.Deserialize<PresentationSettings>("{\"Opacity\":1,\"CrossSections\":5}")!;
            old.Normalize(); Require(old.Geometry.Mode == GeometryMode.Hyperprism, "Legacy presets remain classic");
            for (int i = 0; i < 20; i++)
            {
                float u = i / 19f, v = (i * 7 % 19) / 19f;
                Vector4F p = Geometry4D.Map(GeometryMode.Hypersphere, u, v, 0, 8, 16, 20, 10, 12, .7f, .3f);
                Require(Math.Abs(Sq(p.X / 16) + Sq(p.Y / 20) + Sq(p.Z / 10) + Sq(p.W / 12) - 1) < .0001f, "Hypersphere equation");
                foreach (float z in new[] { 0f, 10f })
                {
                    p = Geometry4D.Map(GeometryMode.Duocylinder, u, v, z, 8, 16, 20, 10, 12, 1, .3f);
                    float xy = Sq(p.X / 16) + Sq(p.Y / 20), zw = Sq(p.Z / 10) + Sq(p.W / 12);
                    Require(xy <= 1.0001f && zw <= 1.0001f && Math.Abs(Math.Max(xy, zw) - 1) < .0001f, "Duocylinder boundary equation");
                }
            }
            List<GeometryPoint> clipped = Geometry4D.ClipSlab([new(new(-1, 0, 0, -2), 0, 0), new(new(1, 0, 0, 2), 1, 0), new(new(0, 1, 0, 0), .5f, 1)], 0, .2f);
            Require(clipped.Count >= 3 && clipped.All(p => Math.Abs(p.Position.W) <= .20001f), "Slice clips rotated W with interpolated UVs");
            report["MathematicalSurfacesAndSlabClipping"] = true;
            using SmbScene sample = LookGalleryForm.CreateSample();
            using WarpRendererControl renderer = new() { Size = new(720, 600), UseGpu = false, PresentationMode = true };
            renderer.CreateControl(); renderer.SetScene(sample.Clone());
            List<object> modes = []; HashSet<string> images = [];
            foreach (GeometryMode mode in Enum.GetValues<GeometryMode>())
            {
                PresentationSettings settings = new() { Depth = 1, Opacity = 1, CrossSections = 5, RotationSpread = 0,
                    Geometry = new() { Mode = mode, Amount = .7f, Phase = .5f } };
                settings.ObjectGeometries["other-object"] = new() { Mode = GeometryMode.Ribbon, Phase = .23f };
                string preset = Path.Combine(directory, mode + ".warp4d-look.json");
                PresentationSettingsStore.WriteToFile(preset, settings);
                PresentationSettings restored = PresentationSettingsStore.ReadFromFile(preset);
                Require(restored.Geometry.Mode == mode && restored.ObjectGeometries["other-object"].Phase == .23f, "Geometry roundtrip");
                renderer.ApplySettings(restored);
                renderer.SelectedObjectKey = renderer.SelectedLayerKey = null;
                renderer.UseGpu = false;
                Stopwatch watch = Stopwatch.StartNew();
                using Bitmap image = new(720, 600); renderer.DrawToBitmap(image, renderer.ClientRectangle);
                double draw = watch.Elapsed.TotalMilliseconds;
                image.Save(Path.Combine(directory, mode + ".png"), ImageFormat.Png); images.Add(ImagePixels.Read(image).Key);
                renderer.UseGpu = true;
                using Bitmap gpuImage = new(720, 600);
                renderer.DrawToBitmap(gpuImage, renderer.ClientRectangle);
                double rgbError = MeanRgbError(ImagePixels.Read(image).Pixels, ImagePixels.Read(gpuImage).Pixels);
                Require(rgbError < 8, "GPU and CPU geometry agree: " + mode);
                Stopwatch gpuClock = Stopwatch.StartNew();
                for (int repeat = 0; repeat < 5; repeat++) renderer.DrawToBitmap(gpuImage, renderer.ClientRectangle);
                double gpuDraw = gpuClock.Elapsed.TotalMilliseconds / 5;
                SurfaceGroup mesh = renderer.GeometrySurfacesForTest(sample.Objects[0]);
                Require(mesh.Triangles.Count > 0 && mesh.Triangles.Count < 4000, "Bounded nonempty geometry");
                Require(mesh.Triangles.All(t => Finite(t.A) && Finite(t.B) && Finite(t.C)), "Finite perspective coordinates");
                Point? pick = null;
                for (int y = 190; y < 450 && pick is null; y += 4) for (int x = 220; x < 510; x += 4)
                    if (renderer.PickProjection(new(x, y)) is not null) { pick = new(x, y); break; }
                Require(pick is not null, "Visible curved sprite can be picked: " + mode);
                renderer.EditLayers = true;
                renderer.DragProjectionForTest(pick!.Value, new(pick.Value.X + 18, pick.Value.Y + 12));
                Require(renderer.Settings.ObjectLayers.Count > 0, "Mesh pick/drag edits real named layer");
                renderer.EditLayers = false;
                modes.Add(new { Mode = mode.ToString(), Triangles = mesh.Triangles.Count, SoftwareDrawMilliseconds = draw, GpuDrawMilliseconds = gpuDraw, MeanRgbError = rgbError, Renderer = renderer.RendererStatus, PickAndDrag = true });
                settings.Geometry.Animate = true; settings.Animate = true; settings.AnimateLayers = false;
                settings.DepthAnimation.Enabled = settings.CameraAnimation.Enabled = settings.RotationAnimation.Enabled = false;
                renderer.ApplySettings(settings); PresentationAnimator.Apply(renderer, settings, .7);
                SurfaceGroup before = renderer.GeometrySurfacesForTest(sample.Objects[0]);
                PresentationAnimator.Apply(renderer, settings, 2.1);
                SurfaceGroup after = renderer.GeometrySurfacesForTest(sample.Objects[0]);
                if (mode != GeometryMode.Hyperprism) Require(!before.Triangles.SequenceEqual(after.Triangles), "Geometry has its own animation");
                Require(renderer.ProjectionOpacity == 1, "Geometry animation never cycles opacity");
            }
            Require(images.Count == 7, "All seven modes produce different artwork projections");
            report["Modes"] = modes; report["PresetRoundtripLegacyCompatibilityAndOpacityLock"] = true;
            PresentationSettings perObject = new() { Geometry = new() { Mode = GeometryMode.Hypersphere } };
            perObject.ObjectGeometries["sample:cross"] = new() { Mode = GeometryMode.Unfolding, Phase = 0 };
            renderer.ApplySettings(perObject);
            SurfaceGroup flat = renderer.GeometrySurfacesForTest(sample.Objects[0]);
            Require(flat.Triangles.All(t => Math.Abs(t.A.Depth) < .0001f && Math.Abs(t.B.Depth) < .0001f && Math.Abs(t.C.Depth) < .0001f), "Unfolding begins at exactly flat artwork");
            Require(perObject.GeometryFor("other").Mode == GeometryMode.Hypersphere && perObject.GeometryFor("sample:cross").Mode == GeometryMode.Unfolding, "Independent per-object geometry");
            using (PresentationEditorForm editor = new(perObject, sample.Clone()))
            {
                editor.Show(); Pump();
                foreach (GeometryMode mode in Enum.GetValues<GeometryMode>()) { editor.SelectGlobalGeometryForTest(mode); Require(editor.GlobalGeometryForTest == mode, "Editor mode selector applies"); }
                editor.EnableGeometryCycleForTest(); Require(editor.GeometryCyclesForTest, "Geometry cycling enables the animation master");
                using Bitmap window = new(editor.Width, editor.Height); editor.DrawToBitmap(window, new(Point.Empty, window.Size)); window.Save(Path.Combine(directory, "editor-global.png"));
                editor.SelectProjection("sample:cross", "Center"); Pump();
                using Bitmap objectWindow = new(editor.Width, editor.Height); editor.DrawToBitmap(objectWindow, new(Point.Empty, objectWindow.Size)); objectWindow.Save(Path.Combine(directory, "editor-object.png"));
                editor.Close();
            }
            using (PresentationEditorForm empty = new(new(), null)) { empty.Show(); Pump(); empty.SelectGlobalGeometryForTest(GeometryMode.Ribbon); empty.Close(); }
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(directory, "error.txt"), exception.ToString()); return 1; }
        finally { Environment.SetEnvironmentVariable("WARP4D_HOME", priorHome); }
    }
    private static float Sq(float n) => n * n;
    private static double MeanRgbError(int[] a, int[] b)
    {
        long sum = 0;
        for (int i = 0; i < a.Length; i++) for (int shift = 0; shift <= 16; shift += 8) sum += Math.Abs(((a[i] >> shift) & 255) - ((b[i] >> shift) & 255));
        return sum / (a.Length * 3d);
    }
    private static bool Finite(SurfaceVertex v) => float.IsFinite(v.Point.X) && float.IsFinite(v.Point.Y) && float.IsFinite(v.Depth) && float.IsFinite(v.Q) && v.Q > 0;
    private static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    private static void Pump() { for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(35); } }
}
