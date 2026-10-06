using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class SmokeTest
{
    public static int RunScrollCapture(string romPath, string outputPath)
    {
        string absoluteOutput = Path.GetFullPath(outputPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
            using NesEmulator emulator = new();
            emulator.Load(romPath);
            List<object> phases = [];
            Thread.Sleep(600);
            for (int phase = 0; phase < 6; phase++)
            {
                List<object> samples = [];
                for (int sample = 0; sample < 24; sample++)
                {
                    Thread.Sleep(50);
                    NesFrame frame = emulator.CaptureFrame() ?? throw new InvalidOperationException("No frame was captured.");
                    samples.Add(CaptureViewportSample(frame));
                }
                phases.Add(new { Phase = phase, Samples = samples });
                PulseButton(emulator, NesButton.Start);
            }
            File.WriteAllText(absoluteOutput, JsonSerializer.Serialize(phases, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.ChangeExtension(absoluteOutput, ".error.txt"), exception.ToString());
            return 1;
        }
    }

    private static object CaptureViewportSample(NesFrame frame)
    {
        return new
        {
            frame.ScrollX,
            frame.ScrollY,
            frame.RawScrollX,
            frame.RawScrollY,
            frame.ScrollSource,
            FamiDashGameState = frame.Ram.Length > 0x49C ? frame.Ram[0x49C] : -1,
            FamiDashRamScrollX = frame.Ram.Length > 0x4A9
                ? frame.Ram[0x4A6] | (frame.Ram[0x4A7] << 8) | (frame.Ram[0x4A8] << 16) | (frame.Ram[0x4A9] << 24)
                : 0,
            FamiDashRamScrollY = frame.Ram.Length > 0x4AB
                ? frame.Ram[0x4AA] | (frame.Ram[0x4AB] << 8)
                : 0
        };
    }

    private static void PulseButton(NesEmulator emulator, NesButton button)
    {
        emulator.SetButton(button, true);
        Thread.Sleep(120);
        emulator.SetButton(button, false);
    }

    public static int RunGameProfile(string romPath, string outputPath)
    {
        string absoluteOutput = Path.GetFullPath(outputPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
            using NesEmulator emulator = new();
            emulator.Load(romPath);
            Thread.Sleep(1200);
            NesFrame frame = emulator.CaptureFrame()
                ?? throw new InvalidOperationException("No frame was captured from the custom-profile ROM.");
            int worldTileX = ((frame.ScrollX + 128) / 8) & ~1;
            int worldTileY = ((frame.ScrollY + 128) / 8) & ~1;
            MetatileSignature signature = MetatileSignature.Read(frame, worldTileX, worldTileY);
            GameRecognitionProfile gameProfile = GameRecognitionProfile.Create(
                emulator.RomPath ?? romPath,
                emulator.RomSha256);
            gameProfile.Name = "Non-SMB smoke profile";
            gameProfile.BackgroundRules[signature.Key] = new BackgroundObjectRule
            {
                Kind = SceneObjectKind.Tree.ToString(),
                Label = "Captured custom object",
                VisualFingerprint = MetatileVisualFingerprint.Read(frame, worldTileX, worldTileY)
            };
            gameProfile.Normalize();

            SmbProfile recognizer = new();
            using SmbScene scene = recognizer.Build(
                frame,
                exactProfile: false,
                ProjectionProfile.CreateDefault(),
                gameProfile);
            SceneObject? captured = scene.Objects.FirstOrDefault(item =>
                item.Kind == SceneObjectKind.Tree && item.Label == "Captured custom object");
            if (captured is null || !captured.ProjectionEnabled)
            {
                throw new InvalidOperationException("The non-SMB ROM did not recognize its captured background pattern.");
            }

            File.WriteAllText(absoluteOutput, JsonSerializer.Serialize(new
            {
                NonSmbGameProfileValidated = !emulator.IsSmbWorld,
                Rom = emulator.RomPath,
                emulator.RomSha256,
                CapturedPattern = signature.Key,
                CapturedBounds = captured.Bounds,
                scene.RecognitionProfileName,
                Objects = scene.Objects.GroupBy(item => item.Kind.ToString())
                    .ToDictionary(group => group.Key, group => group.Count())
            }, new JsonSerializerOptions { WriteIndented = true }));
            return emulator.IsSmbWorld ? 1 : 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.ChangeExtension(absoluteOutput, ".error.txt"), exception.ToString());
            return 1;
        }
    }

    public static int RunAttractDemo(string romPath, string outputPath)
    {
        string absoluteOutput = Path.GetFullPath(outputPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
            List<object> states = [];
            (int OperMode, int OperModeTask, int DemoTimer) previous = (-1, -1, -1);

            using NesEmulator emulator = new();
            emulator.Load(romPath);
            DateTime deadline = DateTime.UtcNow.AddSeconds(35);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
                NesFrame? frame = emulator.CaptureFrame();
                if (frame is null || frame.Ram.Length <= 0x07A2)
                {
                    continue;
                }

                (int OperMode, int OperModeTask, int DemoTimer) current =
                    (frame.Ram[0x0770], frame.Ram[0x0772], frame.Ram[0x07A2]);
                if (current != previous)
                {
                    states.Add(new
                    {
                        Seconds = Math.Round((35 - (deadline - DateTime.UtcNow).TotalSeconds), 2),
                        current.OperMode,
                        current.OperModeTask,
                        current.DemoTimer,
                        ScrollX = frame.ScrollX
                    });
                    previous = current;
                }

                if (!SmbProfile.IsAttractDemo(frame))
                {
                    continue;
                }

                SmbProfile profile = new();
                using SmbScene scene = profile.Build(frame, emulator.IsSmbWorld);
                Dictionary<string, int> objects = scene.Objects
                    .GroupBy(item => item.Kind.ToString())
                    .ToDictionary(group => group.Key, group => group.Count());
                int sceneryObjects = scene.Objects.Count(item => item.Kind is not (
                    SceneObjectKind.Player or SceneObjectKind.Enemy or SceneObjectKind.Item or SceneObjectKind.Sprite));
                if (sceneryObjects == 0)
                {
                    throw new InvalidOperationException(
                        "The attract demo was detected, but no background scenery received object geometry.");
                }

                File.WriteAllText(absoluteOutput, JsonSerializer.Serialize(new
                {
                    AttractDemoDetected = true,
                    BackgroundSceneryProjected = true,
                    scene.Location,
                    frame.ScrollX,
                    frame.ScrollY,
                    Objects = objects,
                    StateTransitions = states
                }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            throw new TimeoutException("SMB did not enter its attract demo within 35 seconds.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.ChangeExtension(absoluteOutput, ".error.txt"), exception.ToString());
            return 1;
        }
    }

    public static int Run(string romPath, string outputPath)
    {
        try
        {
            string absoluteOutput = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
            string progressPath = Path.ChangeExtension(absoluteOutput, ".progress.txt");
            File.WriteAllText(progressPath, "start\n");
            FourDMath.Validate();
            ProjectionCycle.Validate();
            ViewportStabilizer.Validate();
            ValidateFamiDashViewport();
            File.AppendAllText(progressPath, "R4 math + projection cycling validated\n");

            using NesEmulator emulator = new();
            File.AppendAllText(progressPath, "loading rom\n");
            emulator.Load(romPath);
            File.AppendAllText(progressPath, "rom loaded\n");
            Thread.Sleep(900);
            emulator.SetButton(NesButton.Start, true);
            Thread.Sleep(120);
            emulator.SetButton(NesButton.Start, false);
            Thread.Sleep(2800);
            emulator.SetButton(NesButton.Right, true);
            emulator.SetButton(NesButton.B, true);
            Thread.Sleep(650);
            PulseJump(emulator);
            Thread.Sleep(700);
            PulseJump(emulator);
            Thread.Sleep(700);
            emulator.SetButton(NesButton.Right, false);
            emulator.SetButton(NesButton.B, false);
            emulator.TogglePause();
            Thread.Sleep(120);
            emulator.TogglePause();
            Thread.Sleep(120);
            File.AppendAllText(progressPath, "capturing frame\n");
            NesFrame frame = emulator.CaptureFrame() ?? throw new InvalidOperationException("No frame was captured.");
            if (emulator.IsSmbWorld &&
                (frame.Ram.Length <= 0x071C ||
                 frame.ScrollX != ((frame.Ram[0x071A] << 8) | frame.Ram[0x071C]) ||
                 frame.ScrollSource != "SMB RAM gameplay viewport"))
            {
                throw new InvalidOperationException("The SMB viewport did not use its stable logical scroll position.");
            }
            File.AppendAllText(progressPath, "frame captured\n");
            SmbProfile profile = new();
            System.Diagnostics.Stopwatch sceneTimer = System.Diagnostics.Stopwatch.StartNew();
            using SmbScene scene = profile.Build(frame, emulator.IsSmbWorld);
            sceneTimer.Stop();
            File.AppendAllText(progressPath, $"scene built: {scene.Objects.Count}\n");

            byte previousOperMode = frame.Ram[0x0770];
            byte previousOperModeTask = frame.Ram[0x0772];
            byte previousDemoTimer = frame.Ram[0x07A2];
            try
            {
                frame.Ram[0x0770] = 0;
                frame.Ram[0x0772] = 3;
                frame.Ram[0x07A2] = 0;
                using SmbScene simulatedDemo = profile.Build(frame, emulator.IsSmbWorld);
                int simulatedScenery = simulatedDemo.Objects.Count(item => item.Kind is not (
                    SceneObjectKind.Player or SceneObjectKind.Enemy or SceneObjectKind.Item or SceneObjectKind.Sprite));
                if (!SmbProfile.IsAttractDemo(frame) || simulatedScenery == 0 ||
                    !simulatedDemo.Location.Contains("ATTRACT DEMO", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("SMB attract-demo scenery was not projected.");
                }
            }
            finally
            {
                frame.Ram[0x0770] = previousOperMode;
                frame.Ram[0x0772] = previousOperModeTask;
                frame.Ram[0x07A2] = previousDemoTimer;
            }

            ProjectionProfile customProfile = ProjectionProfile.CreateDefault("Smoke test custom");
            customProfile.Objects[SceneObjectKind.Player.ToString()].Enabled = false;
            customProfile.Objects[SceneObjectKind.Enemy.ToString()].DepthPercent = 175;
            using SmbScene customScene = profile.Build(frame, emulator.IsSmbWorld, customProfile);
            SceneObject? customPlayer = customScene.Objects.FirstOrDefault(item => item.Kind == SceneObjectKind.Player);
            if (emulator.IsSmbWorld && (customPlayer is null || customPlayer.ProjectionEnabled))
            {
                throw new InvalidOperationException("The profile editor could not keep the player in 2D.");
            }
            SceneObject? customEnemy = customScene.Objects.FirstOrDefault(item => item.Kind == SceneObjectKind.Enemy);
            if (customEnemy is not null &&
                (customEnemy.Depth < 1.80f || customEnemy.Depth > 1.88f || !customEnemy.ProjectionEnabled))
            {
                throw new InvalidOperationException("The profile editor did not apply the enemy depth multiplier.");
            }

            string exportedProfilePath = Path.ChangeExtension(absoluteOutput, ".warp4d.json");
            ProjectionProfileStore.WriteToFile(exportedProfilePath, customProfile);
            ProjectionProfile importedProfile = ProjectionProfileStore.ReadFromFile(exportedProfilePath);
            if (importedProfile.Name != customProfile.Name ||
                importedProfile.RuleFor(SceneObjectKind.Player).Enabled ||
                importedProfile.RuleFor(SceneObjectKind.Enemy).DepthPercent != 175)
            {
                throw new InvalidOperationException("Projection profile JSON did not round-trip correctly.");
            }

            string editorPreviewPath = Path.Combine(
                Path.GetDirectoryName(absoluteOutput)!,
                Path.GetFileNameWithoutExtension(absoluteOutput) + "-profile-editor.png");
            using (ProfileEditorForm editor = new(importedProfile))
            using (Bitmap editorPreview = new(editor.Width, editor.Height, PixelFormat.Format32bppArgb))
            {
                editor.Show();
                Application.DoEvents();
                editor.PerformLayout();
                editor.DrawToBitmap(editorPreview, new Rectangle(Point.Empty, editorPreview.Size));
                editorPreview.Save(editorPreviewPath, ImageFormat.Png);
                editor.Hide();
            }
            File.AppendAllText(progressPath, "profile editor + JSON round-trip validated\n");

            int capturedGameX = 64;
            int capturedGameY = 208;
            int capturedWorldTileX = ((frame.ScrollX + capturedGameX) / 8) & ~1;
            int capturedWorldTileY = ((frame.ScrollY + capturedGameY) / 8) & ~1;
            MetatileSignature capturedSignature = MetatileSignature.Read(frame, capturedWorldTileX, capturedWorldTileY);
            GameRecognitionProfile customGameProfile = GameRecognitionProfile.Create(
                emulator.RomPath ?? "smoke-test.nes",
                emulator.RomSha256);
            customGameProfile.Name = "Smoke test game creator";
            customGameProfile.BackgroundRules[capturedSignature.Key] = new BackgroundObjectRule
            {
                Kind = SceneObjectKind.Terrain.ToString(),
                Label = "Captured ground",
                VisualFingerprint = MetatileVisualFingerprint.Read(
                    frame,
                    capturedWorldTileX,
                    capturedWorldTileY)
            };
            customGameProfile.Normalize();
            using SmbScene genericCustomScene = profile.Build(
                frame,
                exactProfile: false,
                ProjectionProfile.CreateDefault(),
                customGameProfile);
            if (!genericCustomScene.Objects.Any(item =>
                    item.Kind == SceneObjectKind.Terrain &&
                    item.Label == "Captured ground" &&
                    item.ProjectionEnabled) ||
                genericCustomScene.RecognitionProfileName != customGameProfile.Name)
            {
                throw new InvalidOperationException("A user-created game profile did not recognize its captured metatile.");
            }

            string exportedGameProfilePath = Path.ChangeExtension(absoluteOutput, ".warp4d-game.json");
            GameRecognitionProfileStore.WriteToFile(exportedGameProfilePath, customGameProfile);
            GameRecognitionProfile importedGameProfile = GameRecognitionProfileStore.ReadFromFile(exportedGameProfilePath);
            if (importedGameProfile.Match(
                    capturedSignature,
                    frame,
                    capturedWorldTileX,
                    capturedWorldTileY)?.Label != "Captured ground" ||
                importedGameProfile.RomSha256 != emulator.RomSha256)
            {
                throw new InvalidOperationException("Game-recognition profile JSON did not round-trip correctly.");
            }

            int visualWorldX = ((capturedWorldTileX % 64 + 64) % 64) * 8;
            int visualWorldY = ((capturedWorldTileY % 60 + 60) % 60) * 8;
            int visualTable = (visualWorldX >= 256 ? 1 : 0) + (visualWorldY >= 240 ? 2 : 0);
            int visualPixelIndex = (visualWorldY % 240) * 256 + (visualWorldX & 0xFF);
            int originalVisualPixel = frame.NametablePixels[visualTable][visualPixelIndex];
            int secondVisualPixelIndex = visualPixelIndex + 1;
            int originalSecondVisualPixel = frame.NametablePixels[visualTable][secondVisualPixelIndex];
            try
            {
                frame.NametablePixels[visualTable][visualPixelIndex] ^= 0x00010101;
                if (importedGameProfile.Match(
                        capturedSignature,
                        frame,
                        capturedWorldTileX,
                        capturedWorldTileY) is not null)
                {
                    throw new InvalidOperationException("A game-profile rule matched different tile artwork from another graphics bank.");
                }
                BackgroundObjectRule variantRule = importedGameProfile.BackgroundRules[capturedSignature.Key];
                variantRule.CaptureArtwork(MetatileVisualFingerprint.Read(frame, capturedWorldTileX, capturedWorldTileY));
                if (importedGameProfile.Match(capturedSignature, frame, capturedWorldTileX, capturedWorldTileY) is null)
                    throw new InvalidOperationException("Captured alternate artwork did not match its rule.");
                frame.NametablePixels[visualTable][secondVisualPixelIndex] ^= 0x00020202;
                if (importedGameProfile.Match(capturedSignature, frame, capturedWorldTileX, capturedWorldTileY) is not null)
                    throw new InvalidOperationException("Uncaptured artwork matched an animation-variant rule.");
            }
            finally
            {
                frame.NametablePixels[visualTable][visualPixelIndex] = originalVisualPixel;
                frame.NametablePixels[visualTable][secondVisualPixelIndex] = originalSecondVisualPixel;
            }
            if (importedGameProfile.Match(capturedSignature, frame, capturedWorldTileX, capturedWorldTileY) is null ||
                importedGameProfile.BackgroundRules[capturedSignature.Key].ArtworkVariants.Count != 2)
                throw new InvalidOperationException("Adding an artwork variant replaced the original frame.");
            GameRecognitionProfileStore.WriteToFile(exportedGameProfilePath, importedGameProfile);
            importedGameProfile = GameRecognitionProfileStore.ReadFromFile(exportedGameProfilePath);
            if (importedGameProfile.BackgroundRules[capturedSignature.Key].ArtworkVariants.Count != 2)
                throw new InvalidOperationException("Artwork variants did not survive a JSON round trip.");

            string gameEditorPreviewPath = Path.Combine(
                Path.GetDirectoryName(absoluteOutput)!,
                Path.GetFileNameWithoutExtension(absoluteOutput) + "-game-profile-creator.png");
            using (GameProfileEditorForm gameEditor = new(
                       importedGameProfile,
                       frame,
                       exactSmbProfile: false,
                       emulator.RomPath ?? "smoke-test.nes",
                       emulator.RomSha256))
            using (Bitmap gameEditorPreview = new(gameEditor.Width, gameEditor.Height, PixelFormat.Format32bppArgb))
            {
                gameEditor.Show();
                Application.DoEvents();
                gameEditor.SelectGamePixelForTest(capturedGameX, capturedGameY);
                gameEditor.ApplySelectedForTest();
                bool capturedThumbnail = gameEditor.WorkingProfileForTest.BackgroundRules[capturedSignature.Key]
                    .ArtworkVariants.Any(variant => variant.ThumbnailPng.Length > 0);
                if (!capturedThumbnail) throw new InvalidOperationException("Profile capture did not save its thumbnail.");
                gameEditor.UndoEdit();
                if (gameEditor.WorkingProfileForTest.BackgroundRules[capturedSignature.Key].ArtworkVariants
                    .Any(variant => variant.ThumbnailPng.Length > 0))
                    throw new InvalidOperationException("Undo did not restore the pre-capture rule.");
                gameEditor.RedoEdit();
                if (!gameEditor.WorkingProfileForTest.BackgroundRules[capturedSignature.Key].ArtworkVariants
                    .Any(variant => variant.ThumbnailPng.Length > 0))
                    throw new InvalidOperationException("Redo did not restore the captured thumbnail.");
                (int additionalGameX, int additionalGameY) = FindDifferentVisiblePattern(
                    frame,
                    emulator.IsSmbWorld,
                    capturedSignature);
                gameEditor.SelectGamePixelForTest(additionalGameX, additionalGameY, additive: true);
                gameEditor.UpdatePreview();
                if (gameEditor.SelectedCellCountForTest != 2 ||
                    gameEditor.SelectedPatternCountForTest != 2)
                {
                    throw new InvalidOperationException(
                        "The game-profile creator did not preserve an additive multi-cell selection.");
                }
                gameEditor.DragSelectGamePixelsForTest(32, 64, 112, 64);
                if (gameEditor.SelectedCellCountForTest < 5)
                {
                    throw new InvalidOperationException(
                        "The game-profile creator did not continuously select cells across a pointer drag.");
                }
                gameEditor.SelectGamePixelForTest(capturedGameX, capturedGameY);
                gameEditor.SelectGamePixelForTest(additionalGameX, additionalGameY, additive: true);
                Application.DoEvents();
                gameEditor.DrawToBitmap(gameEditorPreview, new Rectangle(Point.Empty, gameEditorPreview.Size));
                gameEditorPreview.Save(gameEditorPreviewPath, ImageFormat.Png);
                gameEditor.Hide();
            }
            File.AppendAllText(progressPath, "game-profile creator + generic recognition validated\n");

            const int sceneBenchmarkFrames = 6;
            System.Diagnostics.Stopwatch repeatedSceneTimer = System.Diagnostics.Stopwatch.StartNew();
            for (int frameIndex = 0; frameIndex < sceneBenchmarkFrames; frameIndex++)
            {
                using SmbScene benchmarkScene = profile.Build(frame, emulator.IsSmbWorld);
            }
            repeatedSceneTimer.Stop();
            double averageSceneBuildMilliseconds =
                repeatedSceneTimer.Elapsed.TotalMilliseconds / sceneBenchmarkFrames;

            using WarpRendererControl renderer = new()
            {
                Size = new Size(1000, 780),
                DepthAmount = 0.72f,
                Perspective = 0.55f,
                ProjectionOpacity = 0.18f,
                SliceCount = 3
            };
            using Bitmap screenshot = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
            renderer.SetScene(CloneScene(scene));
            renderer.CreateControl();
            renderer.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            System.Diagnostics.Stopwatch renderTimer = System.Diagnostics.Stopwatch.StartNew();
            const int benchmarkFrames = 6;
            for (int frameIndex = 0; frameIndex < benchmarkFrames; frameIndex++)
            {
                renderer.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            }
            renderTimer.Stop();
            double averageRenderMilliseconds = renderTimer.Elapsed.TotalMilliseconds / benchmarkFrames;

            using Bitmap farCamera = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
            using Bitmap nearCamera = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
            renderer.Perspective = 0f;
            renderer.DrawToBitmap(farCamera, new Rectangle(Point.Empty, farCamera.Size));
            renderer.Perspective = 1f;
            renderer.DrawToBitmap(nearCamera, new Rectangle(Point.Empty, nearCamera.Size));
            int perspectiveChangedPixels = CountDifferentPixels(farCamera, nearCamera);
            if (perspectiveChangedPixels < 500)
            {
                throw new InvalidOperationException("The 4D camera proximity control did not materially change the projection.");
            }

            using Bitmap clearLayers = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
            using Bitmap opaqueLayers = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
            renderer.Perspective = 0.55f;
            renderer.ProjectionOpacity = 0f;
            renderer.DrawToBitmap(clearLayers, new Rectangle(Point.Empty, clearLayers.Size));
            renderer.ProjectionOpacity = 1f;
            renderer.DrawToBitmap(opaqueLayers, new Rectangle(Point.Empty, opaqueLayers.Size));
            int opacityChangedPixels = CountDifferentPixels(clearLayers, opaqueLayers);
            if (opacityChangedPixels < 500)
            {
                throw new InvalidOperationException("The 4D layer opacity control did not materially change the projection.");
            }
            renderer.ProjectionOpacity = 0.18f;

            using Bitmap sharedProjectionRotation = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
            using Bitmap independentProjectionRotation = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
            renderer.ProjectionCycleSeconds = 0;
            renderer.ProjectionRotationSpreadDegrees = 0;
            renderer.DrawToBitmap(sharedProjectionRotation, new Rectangle(Point.Empty, sharedProjectionRotation.Size));
            renderer.ProjectionRotationSpreadDegrees = 58;
            renderer.DrawToBitmap(independentProjectionRotation, new Rectangle(Point.Empty, independentProjectionRotation.Size));
            int independentProjectionChangedPixels = CountDifferentPixels(
                sharedProjectionRotation,
                independentProjectionRotation);
            if (independentProjectionChangedPixels < 500)
            {
                throw new InvalidOperationException(
                    "Per-projection rotation did not separate the individual projection sheets.");
            }

            using Bitmap projectionCycleStart = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
            using Bitmap projectionCycleLater = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
            renderer.ProjectionCycleSeconds = 0;
            renderer.DrawToBitmap(projectionCycleStart, new Rectangle(Point.Empty, projectionCycleStart.Size));
            renderer.ProjectionCycleSeconds = 3.75;
            renderer.DrawToBitmap(projectionCycleLater, new Rectangle(Point.Empty, projectionCycleLater.Size));
            int independentProjectionCycleChangedPixels = CountDifferentPixels(
                projectionCycleStart,
                projectionCycleLater);
            if (independentProjectionCycleChangedPixels < 500)
            {
                throw new InvalidOperationException(
                    "Automatic cycling did not rotate individual projection sheets independently.");
            }
            renderer.ProjectionCycleSeconds = 0;

            using Bitmap defaultRotation = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
            renderer.ResetCamera();
            renderer.DrawToBitmap(defaultRotation, new Rectangle(Point.Empty, defaultRotation.Size));
            Dictionary<string, int> rotationChangedPixels = new();
            (string Name, Action Change)[] rotationChecks =
            [
                ("XY", () => renderer.AngleXYDegrees += 35),
                ("XZ", () => renderer.AngleXZDegrees += 35),
                ("XW", () => renderer.AngleXWDegrees += 35),
                ("YZ", () => renderer.AngleYZDegrees += 35),
                ("YW", () => renderer.AngleYWDegrees += 35),
                ("ZW", () => renderer.AngleZWDegrees += 35)
            ];
            foreach ((string name, Action change) in rotationChecks)
            {
                renderer.ResetCamera();
                change();
                using Bitmap changedRotation = new(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
                renderer.DrawToBitmap(changedRotation, new Rectangle(Point.Empty, changedRotation.Size));
                int changedPixels = CountDifferentPixels(defaultRotation, changedRotation);
                if (changedPixels < 500)
                {
                    throw new InvalidOperationException($"The {name} rotation control did not materially change the projection.");
                }
                rotationChangedPixels[name] = changedPixels;
            }
            renderer.ResetCamera();
            renderer.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            screenshot.Save(absoluteOutput, ImageFormat.Png);
            File.AppendAllText(progressPath, "screenshot saved\n");

            string metadataPath = Path.ChangeExtension(absoluteOutput, ".json");
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(new
            {
                ExactSmbProfile = emulator.IsSmbWorld,
                ProjectionModel = "R4 rotation -> W perspective divide -> R3 Z perspective divide",
                HyperprismVertices = 16,
                HyperprismEdges = 32,
                RotationPlanes = 6,
                IndependentRotationPerProjection = true,
                IndependentProjectionChangedPixels = independentProjectionChangedPixels,
                IndependentProjectionCycleChangedPixels = independentProjectionCycleChangedPixels,
                RotationChangedPixels = rotationChangedPixels,
                StableSmbViewportValidated = true,
                AutoCycleValidated = true,
                UserProfileEditorValidated = true,
                GameProfileCreatorValidated = true,
                GameProfileMultiSelectionValidated = true,
                GameProfileHistoryValidated = true,
                AnimatedArtworkVariantsValidated = true,
                AttractDemoSceneryValidated = true,
                ProfileObjectClasses = Enum.GetValues<SceneObjectKind>().Length,
                ExportedProfile = exportedProfilePath,
                ProfileEditorPreview = editorPreviewPath,
                ExportedGameProfile = exportedGameProfilePath,
                GameProfileCreatorPreview = gameEditorPreviewPath,
                CapturedMetatile = capturedSignature.Key,
                AutoCycleSample = ProjectionCycle.Sample(4.25),
                SceneBuildMilliseconds = Math.Round(sceneTimer.Elapsed.TotalMilliseconds, 2),
                AverageSceneBuildMilliseconds = Math.Round(averageSceneBuildMilliseconds, 2),
                AverageRenderMilliseconds = Math.Round(averageRenderMilliseconds, 2),
                PerspectiveChangedPixels = perspectiveChangedPixels,
                OpacityChangedPixels = opacityChangedPixels,
                frame.ScrollX,
                frame.ScrollY,
                frame.ScrollSource,
                frame.Sequence,
                Objects = scene.Objects.GroupBy(item => item.Kind.ToString())
                    .ToDictionary(group => group.Key, group => group.Count()),
                Palette3Metatiles = GetPalette3Metatiles(frame),
                scene.Location,
                Screenshot = absoluteOutput
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.AppendAllText(progressPath, "complete\n");
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(Path.ChangeExtension(Path.GetFullPath(outputPath), ".error.txt"), exception.ToString());
            }
            catch
            {
                // Preserve the original failure as the process exit code.
            }
            return 1;
        }
    }

    private static void ValidateFamiDashViewport()
    {
        byte[] ram = new byte[0x800];
        ram[0x49C] = 0x01;
        if (!NesEmulator.TryGetFamiDashViewport(ram, out int menuX, out int menuY) || menuX != 0 || menuY != 0)
        {
            throw new InvalidOperationException("The FamiDash title viewport was not anchored at the origin.");
        }

        ram[0x49C] = 0x02;
        ram[0x4A6] = 0x34;
        ram[0x4A7] = 0x01;
        ram[0x4AA] = 0xEF;
        ram[0x4AB] = 0x02;
        if (!NesEmulator.TryGetFamiDashViewport(ram, out int gameX, out int gameY) ||
            gameX != 0x134 || gameY != 0xEF)
        {
            throw new InvalidOperationException("The FamiDash gameplay viewport did not decode its extended RAM coordinates.");
        }
    }

    private static (int GameX, int GameY) FindDifferentVisiblePattern(
        NesFrame frame,
        bool exactSmbProfile,
        MetatileSignature excluded)
    {
        for (int gameY = exactSmbProfile ? 32 : 0; gameY < 240; gameY += 16)
        for (int gameX = 0; gameX < 256; gameX += 16)
        {
            int worldPixelX = frame.ScrollX + gameX;
            int worldPixelY = frame.ScrollY + gameY;
            int worldTileX = (worldPixelX / 8) & ~1;
            int worldTileY = (worldPixelY / 8) & ~1;
            MetatileSignature candidate = MetatileSignature.Read(frame, worldTileX, worldTileY);
            if (!candidate.Key.Equals(excluded.Key, StringComparison.OrdinalIgnoreCase))
            {
                return (gameX, gameY);
            }
        }

        throw new InvalidOperationException(
            "The captured viewport did not contain two distinct metatile patterns for multi-selection validation.");
    }

    private static SmbScene CloneScene(SmbScene source)
    {
        List<SceneObject> objects = source.Objects.Select(item => new SceneObject
        {
            Kind = item.Kind,
            Label = item.Label,
            IdentityKey = item.IdentityKey,
            Bounds = item.Bounds,
                Image = new Bitmap(item.Image),
                Accent = item.Accent,
                Depth = item.Depth,
                ProjectionEnabled = item.ProjectionEnabled,
                SortOrder = item.SortOrder
        }).ToList();
        return new SmbScene
        {
            Background = new Bitmap(source.Background),
            Objects = objects,
            Location = source.Location,
            ExactProfile = source.ExactProfile,
            RecognitionProfileName = source.RecognitionProfileName,
            ProjectionProfileName = source.ProjectionProfileName,
            Sequence = source.Sequence
        };
    }

    private static void PulseJump(NesEmulator emulator)
    {
        emulator.SetButton(NesButton.A, true);
        Thread.Sleep(140);
        emulator.SetButton(NesButton.A, false);
    }

    private static object[] GetPalette3Metatiles(NesFrame frame)
    {
        List<object> result = [];
        int leftTile = frame.ScrollX / 8;
        int rightTile = (frame.ScrollX + 255) / 8;
        for (int y = 0; y < 30; y += 2)
        for (int x = leftTile & ~1; x <= rightTile; x += 2)
        {
            (byte tl, byte palette) = ReadTile(frame, x, y);
            (byte bl, _) = ReadTile(frame, x, y + 1);
            (byte tr, _) = ReadTile(frame, x + 1, y);
            (byte br, _) = ReadTile(frame, x + 1, y + 1);
            if (palette == 3 || tl is >= 0x53 and <= 0x5A || bl is >= 0x53 and <= 0x5A ||
                tr is >= 0x53 and <= 0x5A || br is >= 0x53 and <= 0x5A)
            {
                result.Add(new { X = x, Y = y, Palette = palette, Tiles = $"{tl:X2} {bl:X2} {tr:X2} {br:X2}" });
            }
        }
        return [.. result];
    }

    private static (byte Tile, byte Palette) ReadTile(NesFrame frame, int x, int y)
    {
        int wrappedX = (x % 64 + 64) % 64;
        int wrappedY = (y % 60 + 60) % 60;
        int table = (wrappedX >= 32 ? 1 : 0) + (wrappedY >= 30 ? 2 : 0);
        int index = (wrappedY % 30) * 32 + wrappedX % 32;
        byte attribute = frame.Attributes[table][index];
        int shift = ((wrappedY & 2) << 1) | (wrappedX & 2);
        return (frame.Tiles[table][index], (byte)((attribute >> shift) & 3));
    }

    private static unsafe int CountDifferentPixels(Bitmap first, Bitmap second)
    {
        Rectangle bounds = new(0, 0, first.Width, first.Height);
        BitmapData firstData = first.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData secondData = second.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int changed = 0;
            for (int y = 0; y < first.Height; y++)
            {
                int* firstRow = (int*)((byte*)firstData.Scan0 + y * firstData.Stride);
                int* secondRow = (int*)((byte*)secondData.Scan0 + y * secondData.Stride);
                for (int x = 0; x < first.Width; x++)
                {
                    if (firstRow[x] != secondRow[x]) changed++;
                }
            }
            return changed;
        }
        finally
        {
            second.UnlockBits(secondData);
            first.UnlockBits(firstData);
        }
    }
}
