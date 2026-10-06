using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class FrontendTests
{
    internal static int Run(string outputDirectory)
    {
        string directory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(directory);
        string? previous = Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME", Path.Combine(directory, "isolated-data"));
        Dictionary<string, object> report = [];
        bool priorMotion = InterfaceMotion.Enabled;
        try
        {
            InterfaceMotion.Enabled = false; InterfaceMotion.Save(); InterfaceMotion.Enabled = true; InterfaceMotion.Load();
            Require(!InterfaceMotion.Enabled, "Reduced motion persists independently of game animation");
            using (WarpRendererControl idle = new() { Size = new(720, 600), UseGpu = false })
            {
                idle.CreateControl();
                using Bitmap before = new(720, 600); using Bitmap after = new(720, 600);
                idle.DrawToBitmap(before, idle.ClientRectangle); Pump(); idle.DrawToBitmap(after, idle.ClientRectangle);
                Require(ImagePixels.Read(before).Pixels.SequenceEqual(ImagePixels.Read(after).Pixels), "Reduced-motion welcome is static");
            }
            InterfaceMotion.Enabled = true; InterfaceMotion.Save();
            report["InterfaceMotionPersistsAndFreezesWelcome"] = true;
            using (MainForm main = new())
            {
                main.Show(); Pump();
                Require(!main.RomLoadedForTest, "No ROM autoload");
                Require(!main.ControlsVisibleForTest&&main.FriendlyPagesForTest==0, "Original stage-first interface restored");
                Size expanded = main.StageSizeForTest;
                Save(main, "welcome.png");
                main.ClientSize = new(1040, 720); Pump(); Save(main, "welcome-minimum.png");
                for (int look = 0; look < 5; look++)
                {
                    main.ApplyLookForTest(look); Pump();
                    Require(main.LookDockControlsClearForTest, "Motion checkbox clear of wrapped status and snapshot for look " + look);
                    Save(main, "look-dock-minimum-" + look + ".png");
                }
                report["MotionCheckboxVisibleForAllLookDescriptionsAtMinimumSize"] = true;
                Require(main.LiveStyleButtonsForTest == 11, "All eleven styles directly exposed as buttons");
                for (int index = 0; index < LookCatalog.Names.Length; index++)
                {
                    main.ClickStyleForTest(index); Pump();
                    Require(main.PresentationForTest.Name == LookCatalog.Names[index], "Live style button applies " + index);
                    Require(main.PresentationForTest.Geometry.Mode == (index < 5 ? GeometryMode.Hyperprism : (GeometryMode)(index - 4)), "Style applies correct geometry");
                    Require(main.LookDockControlsClearForTest, "Shape names cannot overlap motion checkbox");
                    Require(main.LiveStyleButtonsVisibleForTest, "All style buttons fit at minimum size");
                }
                Save(main, "main-live-style-buttons.png");
                report["MainWindowAllElevenButtonsApplyLiveAtMinimumSize"] = true;
                main.ClientSize = new(1260, 820); Pump();
                main.ToggleControlsForTest(); Pump();
                Require(main.StageSizeForTest.Width < expanded.Width, "Controls reveal");
                Save(main, "controls.png");
                main.ToggleFullscreenForTest(); Pump();
                main.ToggleFullscreenForTest(); Pump();
                Require(main.ControlsVisibleForTest, "Fullscreen restores visible controls");
                main.ToggleControlsForTest(); Pump();
                main.ToggleFullscreenForTest(); Pump(); main.ToggleFullscreenForTest(); Pump();
                Require(!main.ControlsVisibleForTest && main.StageSizeForTest == expanded, "Fullscreen restores immersive layout");
                PresentationSettings settings = new();
                settings.ProjectionProfile.Name = "User recognition settings";
                settings.EditLayer("object", "W:-1").Rotation.XY = 77;
                settings.RenderScale = 3;
                main.ApplyPresentationForTest(settings);
                for (int i = 0; i < LookCatalog.Names.Length; i++)
                {
                    main.ApplyLookForTest(i);
                    PresentationSettings after = main.PresentationForTest;
                    Require(after.Name == LookCatalog.Names[i], "Look applied");
                    Require(after.LayerFor("object", "W:-1")!.Rotation.XY == 77, "Individual edits preserved");
                    Require(after.RenderScale == 3 && after.ProjectionProfile.Name == settings.ProjectionProfile.Name, "Quality and class settings preserved");
                }
                main.SaveScreenshotForTest(Path.Combine(directory, "snapshot.png"));
                report["ImmersiveShellControlsAndFullscreen"] = true;
                report["AllFiveLooksPreserveAuthoredSettings"] = true;
                main.Close();
            }
            using (LookGalleryForm gallery = new(new(), null))
            {
                gallery.Show(); Pump();
                gallery.SelectLook(0); gallery.HoverForTest(3, true);
                Require(gallery.PreviewNameForTest == LookCatalog.Names[3] && gallery.SelectedSettings.Name == LookCatalog.Names[0], "Hover previews without applying");
                gallery.HoverForTest(3, false);
                Require(gallery.PreviewNameForTest == LookCatalog.Names[0], "Hover leave restores selection");
                report["HoverPreviewDoesNotApplyAndRestores"] = true;
                for (int i = 0; i < 5; i++) { gallery.SelectLook(i); Pump(); Save(gallery, "gallery-" + i + ".png"); }
                for (int i = 5; i < LookCatalog.Names.Length; i++)
                {
                    gallery.SelectLook(i); Pump(); Save(gallery, "gallery-" + i + ".png");
                    Require(gallery.SelectedSettings.Geometry.Mode == (GeometryMode)(i - 4), "New gallery style applies geometry");
                }
                gallery.Close();
                report["LiveGalleryAllElevenLooksRendered"] = true;
            }
            using SmbScene sample = LookGalleryForm.CreateSample();
            using WarpRendererControl renderer = new() { Size = new(720, 600), UseGpu = false };
            renderer.CreateControl(); renderer.SetScene(sample.Clone());
            renderer.ApplySettings(LookCatalog.Create(4, new()));
            using (Bitmap bitmap = new(720, 600)) renderer.DrawToBitmap(bitmap, renderer.ClientRectangle);
            int hitCount = 0; HashSet<string> keys = [];
            Point? dragPoint = null; string? dragLayer = null;
            for (int y = 190; y < 440; y += 3) for (int x = 210; x < 510; x += 3)
                if (renderer.PickProjection(new(x, y)) is { } hit) { hitCount++; keys.Add(hit.LayerKey); if (dragPoint is null && hit.LayerKey.StartsWith("W:")) { dragPoint = new(x, y); dragLayer = hit.LayerKey; } }
            Require(hitCount > 20 && keys.Any(k => k.StartsWith("W:")), "Projected sheets can be picked");
            renderer.EditLayers = true;
            RotationAngles baseBefore = renderer.Settings.Clone().Rotation;
            renderer.DragProjectionForTest(dragPoint!.Value, new(dragPoint.Value.X + 24, dragPoint.Value.Y + 16));
            File.WriteAllText(Path.Combine(directory,"layer-drag.json"),JsonSerializer.Serialize(new{ExpectedObject="sample:cross",ExpectedLayer=dragLayer,Start=dragPoint,SelectedObject=renderer.SelectedObjectKey,SelectedLayer=renderer.SelectedLayerKey,RequestedModifiers="None",ObservedPhysicalModifiers=Control.ModifierKeys.ToString(),GlobalBefore=baseBefore,GlobalAfter=renderer.Settings.Rotation,LayerAfter=renderer.Settings.LayerFor("sample:cross",dragLayer!)},new JsonSerializerOptions{WriteIndented=true}));
            Require(renderer.Settings.LayerFor("sample:cross", dragLayer!)!.Rotation.XW != 0 && renderer.Settings.Rotation.XW == baseBefore.XW, "Dragging modifies only the picked layer");
            report["DirectLayerDragLeavesGlobalRotationUnchanged"] = true;
            LayerSettings center = renderer.Settings.EditLayer(sample.Objects[0].PresentationKey, "Center");
            center.Enabled = false; renderer.ProjectionOpacity = 0;
            Require(renderer.PickProjection(new(360, 312)) is null, "Invisible layers cannot be picked");
            report["AlphaAwareProjectedSheetPicking"] = true; report["PickedLayerKeys"] = keys;
            using (PresentationEditorForm editor = new(new(), sample.Clone()))
            { editor.Show(); editor.SelectProjection("sample:cross", "W:+1"); Pump(); Save(editor, "layers.png"); editor.Close(); }
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(directory, "error.txt"), exception.ToString()); return 1; }
        finally { InterfaceMotion.Enabled = priorMotion; Environment.SetEnvironmentVariable("WARP4D_HOME", previous); }
        void Save(Control control, string name) { using Bitmap image = new(control.Width, control.Height); control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(directory, name), ImageFormat.Png); }
    }
    private static void Pump() { for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(35); } }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
