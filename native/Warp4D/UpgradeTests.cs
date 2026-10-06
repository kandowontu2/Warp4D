using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class UpgradeTests
{
    public static int Run(string romPath, string secondRomPath, string outputDirectory)
    {
        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        string? previousHome = Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME", Path.Combine(directory, "isolated-data"));
        Dictionary<string, object> report = [];
        void Progress(string message) => File.AppendAllText(Path.Combine(directory, "progress.txt"), message + Environment.NewLine);
        try
        {
            InputSettings input = new();
            Require(input.Mask(new HashSet<Keys> { Keys.Left, Keys.X }) == ((int)NesButton.Left | (int)NesButton.A), "Keyboard combinations");
            input.Keyboard["A"] = Keys.C;
            Require(input.Mask(new HashSet<Keys> { Keys.X }) == 0 && input.Mask(new HashSet<Keys> { Keys.C }) == (int)NesButton.A, "Key remapping");
            input.Volume = 42; InputSettingsStore.Save(input);
            Require(InputSettingsStore.Load().Keyboard["A"] == Keys.C && InputSettingsStore.Load().Volume == 42, "Control persistence");
            Require(ControllerInput.Map(new() { Buttons = 0x1010, LeftX = -20000 }) == ((int)NesButton.A | (int)NesButton.Start | (int)NesButton.Left), "Controller mapping");
            Require(ControllerInput.Map(new() { LeftX = 11000, LeftY = -1000 }) == 0, "Controller dead zone");
            report["InputMappingAndPersistence"] = true;
            report["PhysicalControllerConnected"] = ControllerInput.Poll().Connected;

            using (NesEmulator emulator = new())
            {
                Progress("native load");
                emulator.Load(romPath); Thread.Sleep(800); emulator.SetButton(NesButton.Start, true); Thread.Sleep(100);
                emulator.SetButton(NesButton.Start, false); Thread.Sleep(2900); emulator.TogglePause(); Thread.Sleep(60);
                Progress("paused capture");
                NesFrame frame = emulator.CaptureFrame()!;
                Progress("save native state");
                emulator.SaveState(1);
                Progress("saved native state");
                NesFrame saved = emulator.CaptureFrame()!;
                emulator.TogglePause(); emulator.SetButton(NesButton.Right, true); Thread.Sleep(450);
                emulator.SetButton(NesButton.Right, false); emulator.TogglePause();
                NesFrame advanced = emulator.CaptureFrame()!;
                Require(!saved.Ram.SequenceEqual(advanced.Ram), "State changed before restoration");
                Progress("load native state");
                emulator.LoadState(1);
                Progress("loaded native state");
                NesFrame restored = emulator.CaptureFrame()!;
                Progress("restored frame captured");
                int changedBytes = saved.Ram.Zip(restored.Ram).Count(pair => pair.First != pair.Second);
                report["SaveStateRamDifferences"] = changedBytes;
                Require(changedBytes == 0, "Native state RAM restoration");
                Require(emulator.IsPaused, "Pause retained across state load");
                report["NativeSaveStateRestored"] = true;
                SmbProfile recognizer = new();
                using SmbScene scene = recognizer.Build(frame, emulator.IsSmbWorld);
                Progress("scene recognized");
                Require(scene.Objects.Count > 0, "Scene objects");
                report["BenchmarkObjectCount"] = scene.Objects.Count;
                SceneObject item = scene.Objects.First(o => o.ProjectionEnabled);
                long initialHits = GeometryCache.Hits;
                _ = item.PixelGeometry;
                using SmbScene clone = scene.Clone();
                _ = clone.Objects.First(o => o.PresentationKey == item.PresentationKey).PixelGeometry;
                Require(GeometryCache.Hits > initialHits, "Unchanged artwork geometry reuse");

                using WarpRendererControl renderer = new() { Size = new(1000, 780) };
                renderer.SetScene(scene.Clone()); renderer.CreateControl();
                Progress("renderer created");
                PresentationSettings settings = new();
                renderer.ApplySettings(settings);
                Dictionary<string, Vector4F> baseline = renderer.LayerTransformsForTest(item);
                LayerSettings edited = settings.EditLayer(item.PresentationKey, "W:-1");
                edited.Rotation.XY = 63; edited.Rotation.ZW = -28; edited.OpacityPercent = 100;
                edited.UseGlobalOpacity = false; edited.DepthPercent = 150; edited.AnimationSpeed = 0.25;
                Dictionary<string, Vector4F> changed = renderer.LayerTransformsForTest(item);
                Require(baseline["W:-1"] != changed["W:-1"] && baseline["W:+1"] == changed["W:+1"], "Layer edit isolation");
                renderer.SliceCount = 9;
                Dictionary<string, Vector4F> expanded = renderer.LayerTransformsForTest(item);
                foreach (string key in changed.Keys) Require(changed[key] == expanded[key], "Stable layer key " + key);
                report["IndependentLayersAndStableCount"] = true;
                LayerSettings center = settings.EditLayer(item.PresentationKey, "Center");
                Vector4F centerBefore = renderer.LayerTransformsForTest(item)["Center"];
                center.DepthPercent = 150;
                Require(renderer.LayerTransformsForTest(item)["Center"] != centerBefore, "Center depth adjustment");
                center.Enabled = false;
                Require(!renderer.LayerTransformsForTest(item).ContainsKey("Center"), "Center can be disabled");
                center.Enabled = true; center.DepthPercent = 100;
                string exported = Path.Combine(directory, "test-preset.warp4d-look.json");
                PresentationSettingsStore.WriteToFile(exported, settings);
                PresentationSettings loaded = PresentationSettingsStore.ReadFromFile(exported);
                Require(loaded.LayerFor(item.PresentationKey, "W:-1")!.Rotation.XY == 63 && loaded.LayerFor(item.PresentationKey, "W:-1")!.DepthPercent == 150, "Layer JSON persistence");
                settings.Animate = true; settings.DepthAnimation.Enabled = false; settings.CameraAnimation.Enabled = false;
                settings.AnimateLayers = false; settings.RotationAnimation.Minimum = -30; settings.RotationAnimation.Maximum = 30;
                renderer.ApplySettings(settings);
                PresentationAnimator.Apply(renderer, settings, 7);
                Require(renderer.DepthAmount == settings.Depth && renderer.Perspective == settings.Perspective && renderer.ProjectionOpacity == settings.Opacity, "Animation locks");
                Require(renderer.AngleXWDegrees >= -30 && renderer.AngleXWDegrees <= 30, "Animation range");
                float before = renderer.AngleXWDegrees;
                settings.RotationAnimation.Speed = 1.7; PresentationAnimator.Apply(renderer, settings, 7);
                Require(Math.Abs(renderer.AngleXWDegrees - before) > 0.5, "Animation speed");
                report["SeparateAnimationChannels"] = true;
                Progress("layer and animation tests passed");
                settings.Animate = false; settings.Opacity = 1; settings.CrossSections = 5;
                renderer.ApplySettings(settings);
                using Bitmap gpuImage = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
                renderer.DrawToBitmap(gpuImage, new Rectangle(Point.Empty, gpuImage.Size));
                Progress("GPU draw completed " + renderer.RendererStatus);
                gpuImage.Save(Path.Combine(directory, "opaque-gpu.png"));
                Progress("GPU screenshot saved");
                report["Renderer"] = renderer.RendererStatus;
                long uploads = renderer.TextureUploads;
                List<double> renderSamples = [];
                for (int i = 0; i < 60; i++)
                {
                    renderer.ProjectionCycleSeconds = i / 60d;
                    Stopwatch timer = Stopwatch.StartNew(); renderer.DrawToBitmap(gpuImage, new Rectangle(Point.Empty, gpuImage.Size));
                    renderSamples.Add(timer.Elapsed.TotalMilliseconds);
                    if (i % 10 == 0) Progress("GPU benchmark frame " + i);
                }
                Require(renderer.TextureUploads == uploads, "GPU texture reuse");
                report["AverageDrawMilliseconds"] = renderSamples.Average();
                report["P95DrawMilliseconds"] = renderSamples.Order().ElementAt(56);
                renderer.UseGpu = false;
                Progress("software fallback begin");
                using Bitmap softwareImage = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
                renderer.DrawToBitmap(softwareImage, new Rectangle(Point.Empty, softwareImage.Size));
                softwareImage.Save(Path.Combine(directory, "software-fallback.png"));
                Progress("software fallback passed");
                report["SoftwareFallbackDrawn"] = true;
                report["GpuSoftwareMeanRgbError"] = MeanRgbError(gpuImage, softwareImage);
                Require(MeanRgbError(gpuImage, softwareImage) < 8, "GPU/software visual agreement");
                renderer.UseGpu = true;
                settings.Opacity = 0.18f; renderer.ApplySettings(settings);
                renderer.DrawToBitmap(gpuImage, new Rectangle(Point.Empty, gpuImage.Size));
                renderer.UseGpu = false; renderer.DrawToBitmap(softwareImage, new Rectangle(Point.Empty, softwareImage.Size));
                Require(MeanRgbError(gpuImage, softwareImage) < 8, "Translucent GPU/software agreement");
                report["TranslucentGpuSoftwareMeanRgbError"] = MeanRgbError(gpuImage, softwareImage);
                renderer.UseGpu = true;
                List<object> geometryBenchmarks = [];
                foreach (GeometryMode mode in Enum.GetValues<GeometryMode>().Where(m => m != GeometryMode.Hyperprism))
                {
                    settings.Geometry = new() { Mode = mode, Amount = .7f, Phase = .5f };
                    settings.Opacity = .5f; renderer.ApplySettings(settings);
                    Stopwatch geometryClock = Stopwatch.StartNew();
                    renderer.DrawToBitmap(gpuImage, new Rectangle(Point.Empty, gpuImage.Size));
                    double firstDraw = geometryClock.Elapsed.TotalMilliseconds;
                    geometryClock.Restart();
                    for (int repeat = 0; repeat < 5; repeat++) renderer.DrawToBitmap(gpuImage, new Rectangle(Point.Empty, gpuImage.Size));
                    double cachedDraw = geometryClock.Elapsed.TotalMilliseconds / 5;
                    settings.Animate = true; settings.Geometry.Animate = true; renderer.ApplySettings(settings);
                    geometryClock.Restart();
                    for (int repeat = 0; repeat < 5; repeat++) { renderer.GeometryCycleSeconds = repeat * .13; renderer.DrawToBitmap(gpuImage, new Rectangle(Point.Empty, gpuImage.Size)); }
                    double animatedDraw = geometryClock.Elapsed.TotalMilliseconds / 5;
                    geometryBenchmarks.Add(new { Mode = mode.ToString(), Objects = scene.Objects.Count, FirstDrawMilliseconds = firstDraw, CachedDrawMilliseconds = cachedDraw, AnimatedDrawMilliseconds = animatedDraw });
                    settings.Animate = false; settings.Geometry.Animate = false; renderer.ApplySettings(settings);
                    renderer.DrawToBitmap(gpuImage, new Rectangle(Point.Empty, gpuImage.Size));
                    gpuImage.Save(Path.Combine(directory, "gameplay-geometry-" + mode + ".png"));
                }
                report["RealGameplayAllSixGeometryModes"] = geometryBenchmarks;
                settings.Geometry = new();
                settings.Opacity = 0; renderer.ApplySettings(settings);
                renderer.DrawToBitmap(gpuImage, new Rectangle(Point.Empty, gpuImage.Size));
                gpuImage.Save(Path.Combine(directory, "individual-opaque-global-zero.png"));

                using PresentationEditorForm editor = new(settings, scene.Clone());
                Progress("presentation editor created");
                editor.Show(); Application.DoEvents();
                editor.SelectLayerForTest("W:-1"); Application.DoEvents();
                SaveWindow(editor, Path.Combine(directory, "layer-editor.png")); editor.Hide();
                using TilePickerControl picker = new(frame, new Bitmap(scene.Background), emulator.IsSmbWorld) { Size = new(600, 600), RectangleSelection = true };
                picker.CreateControl(); picker.SelectGameDrag(40, 80, 88, 112);
                Require(picker.SelectedCellCount == 12, "Rectangle selection area");
                picker.ZoomAt(new(150, 150), 2);
                Require(picker.Zoom == 2 && picker.SelectedCellCount == 12, "Zoom retains selection");
                picker.ResetView(); Require(picker.Zoom == 1, "Fit view");
                picker.SetSnapshot(restored); Require(picker.SelectedCellCount == 0, "Snapshot refresh clears stale cells");
                report["RectangleZoomRefresh"] = true;
                Progress("rectangle zoom tests passed");
                using ControlsEditorForm controls = new(input);
                Progress("controls created");
                controls.Show(); Application.DoEvents(); SaveWindow(controls, Path.Combine(directory, "controls-editor.png")); controls.Hide();
                Progress("controls screenshot saved");
                controls.Dispose(); Progress("controls disposed");
                picker.Dispose(); Progress("picker disposed");
                editor.Dispose(); Progress("presentation disposed");
                renderer.Dispose(); Progress("renderer disposed");
                emulator.Dispose(); Progress("emulator disposed");
            }
            Progress("headless core disposed");

            using (MainForm main = new())
            {
                main.Show(); Application.DoEvents(); main.LoadRomForTest(romPath);
                Progress("main first ROM loaded");
                PresentationSettings first = PresentationSettings.Preset("Fully opaque"); first.Depth = 0.42f;
                first.Geometry.Mode = GeometryMode.Ribbon;
                main.ApplyPresentationForTest(first);
                main.LoadRomForTest(secondRomPath);
                Progress("main second ROM loaded");
                Require(main.PresentationForTest.Opacity == 0.18f, "Second ROM starts independent");
                Require(main.PresentationForTest.Geometry.Mode == GeometryMode.Hyperprism, "Second ROM geometry independent");
                main.ApplyPresentationForTest(PresentationSettings.Preset("Subtle depth"));
                main.LoadRomForTest(romPath);
                Require(main.PresentationForTest.Opacity == 1 && main.PresentationForTest.Depth == 0.42f, "Switch-back preset restored");
                Require(main.PresentationForTest.Geometry.Mode == GeometryMode.Ribbon, "Switch-back geometry restored");
                Rectangle windowed = main.Bounds;
                main.ToggleFullscreenForTest(); Require(main.FormBorderStyle == FormBorderStyle.None, "Fullscreen entry");
                main.ToggleFullscreenForTest(); Require(main.FormBorderStyle == FormBorderStyle.Sizable && main.Bounds == windowed, "Fullscreen restoration");
                Pump(1800);
                Require(main.HasSceneForTest, "Main window publishes captured scene");
                report["LivePerformance"] = main.PerformanceForTest;
                main.Activate(); main.Focus();
                Message key = Message.Create(main.Handle, 0x0100, (IntPtr)Keys.Left, IntPtr.Zero);
                Require(main.PreFilterMessage(ref key), "Gameplay arrow intercepted");
                Require((main.InputMaskForTest & (int)NesButton.Left) != 0, "Left input reaches core");
                using (ControlsEditorForm modal = new(input))
                {
                    modal.Show(main); modal.Activate(); Pump(60);
                    Require(main.InputMaskForTest == 0, "Editor focus releases gameplay input"); modal.Close();
                }
                Application.DoEvents(); SaveWindow(main, Path.Combine(directory, "main-window.png"));
                main.Activate(); main.Focus();
                Message startDown = Message.Create(main.Handle, 0x0100, (IntPtr)Keys.Enter, IntPtr.Zero);
                Message startUp = Message.Create(main.Handle, 0x0101, (IntPtr)Keys.Enter, IntPtr.Zero);
                Require(main.PreFilterMessage(ref startDown), "Start delivered for gameplay preview"); Pump(100);
                main.PreFilterMessage(ref startUp); Pump(3000);
                PresentationSettings retained = main.PresentationForTest;
                main.ApplyLookForTest(1); Pump(500);
                SaveWindow(main, Path.Combine(directory, "gameplay-kaleidoscope.png"));
                main.SaveScreenshotForTest(Path.Combine(directory, "gameplay-clean-snapshot.png"));
                for (int style = 0; style < LookCatalog.Names.Length; style++)
                {
                    main.ClickStyleForTest(style); Pump(140);
                    Require(main.RomLoadedForTest && main.PresentationForTest.Name == LookCatalog.Names[style], "Live button switches style without reloading ROM");
                }
                report["AllElevenButtonsSwitchDuringGameplay"] = true;
                main.ApplyPresentationForTest(retained); main.Close();
            }
            using (MainForm reopened = new())
            {
                reopened.Show(); Application.DoEvents();
                Require(!reopened.RomLoadedForTest, "No ROM autoload on startup");
                reopened.LoadRomForTest(romPath);
                Require(reopened.PresentationForTest.Opacity == 1 && reopened.PresentationForTest.Depth == 0.42f, "Reopened preset persistence");
                Require(reopened.PresentationForTest.Geometry.Mode == GeometryMode.Ribbon, "Reopened geometry persistence");
                reopened.Close();
            }
            report["RomSwitchReopenAndFullscreen"] = true;
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(directory, "error.txt"), exception.ToString());
            File.WriteAllText(Path.Combine(directory, "partial-results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
        finally { Environment.SetEnvironmentVariable("WARP4D_HOME", previousHome); }
    }
    private static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException("Upgrade regression failed: " + name); }
    private static void Pump(int milliseconds) { Stopwatch clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(2); } }
    private static void SaveWindow(Form window, string path)
    {
        using Bitmap bitmap = new(window.Width, window.Height, PixelFormat.Format32bppArgb);
        window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path, ImageFormat.Png);
    }
    private static double MeanRgbError(Bitmap a, Bitmap b)
    {
        long error = 0;
        for (int y = 0; y < a.Height; y += 2) for (int x = 0; x < a.Width; x += 2)
        {
            Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
            error += Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B);
        }
        return error / (a.Width * a.Height / 4d * 3);
    }
}
