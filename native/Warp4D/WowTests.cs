using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class WowTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        string? home=Environment.GetEnvironmentVariable("WARP4D_HOME");
        bool motion=InterfaceMotion.Enabled;
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        try
        {
            InterfaceMotion.Enabled=false;
            using WarpRendererControl renderer=new(){Size=new(900,650),UseGpu=false};renderer.CreateControl();
            using var first=Capture(renderer,"home-classic");
            var shape=LookCatalog.Create(6,new());shape.Animate=false;shape.Geometry.Animate=false;
            renderer.ApplySettings(shape);using var second=Capture(renderer,"home-duocylinder");
            Require(!ImagePixels.Read(first).Pixels.SequenceEqual(ImagePixels.Read(second).Pixels),"Home uses actual chosen geometry");
            float angle=renderer.AngleXWDegrees;renderer.DragProjectionForTest(new(430,200),new(460,220));
            Require(renderer.AngleXWDegrees!=angle,"Interactive home rotation");
            using var dragged=Capture(renderer,"home-dragged");
            using var stable=Capture(renderer,"home-reduced-motion");
            Require(ImagePixels.Read(dragged).Pixels.SequenceEqual(ImagePixels.Read(stable).Pixels),"Reduced-motion home stays still");
            using var sample=LookGalleryForm.CreateSample();renderer.SetScene(sample.Clone());
            renderer.Settings.Effects.Enabled=false;renderer.RefreshEffects();using var plain=Capture(renderer,"effects-off");
            renderer.Settings.Effects.Enabled=true;renderer.RefreshEffects();using var effects=Capture(renderer,"effects-on");
            Require(!ImagePixels.Read(plain).Pixels.SequenceEqual(ImagePixels.Read(effects).Pixels),"Visible lighting/glow/shadow difference");
            float opacity=renderer.ProjectionOpacity;
            InterfaceMotion.Enabled=true;renderer.EnableStyleTransitions=true;
            var target=LookCatalog.Create(5,renderer.Settings);target.Animate=false;target.Geometry.Animate=false;
            renderer.ApplySettings(target);
            Require(renderer.StyleBlend<.1,"Transition begins at source");
            using var start=Capture(renderer,"morph-start");Thread.Sleep(220);
            Require(renderer.StyleBlend is >.1f and <.95f,"Intermediate morph exists");
            using var mid=Capture(renderer,"morph-mid");Thread.Sleep(700);
            using var end=Capture(renderer,"morph-end");
            Require(renderer.StyleBlend==1,"Transition reaches target");
            Require(!ImagePixels.Read(mid).Pixels.SequenceEqual(ImagePixels.Read(end).Pixels),"Geometry visibly morphs");
            Require(renderer.ProjectionOpacity==target.Opacity,"No opacity cycling");
            target.Effects.SmoothTransitions=false;target.Name="Immediate";renderer.ApplySettings(target);
            Require(renderer.StyleBlend==1,"Transition opt-out");
            PresentationSettingsStore.WriteToFile(Path.Combine(output,"effects.warp4d-look.json"),target);
            var reloaded=PresentationSettingsStore.ReadFromFile(Path.Combine(output,"effects.warp4d-look.json"));
            Require(reloaded.Effects.Glow==target.Effects.Glow&&!reloaded.Effects.SmoothTransitions,"Effects preset persistence");
            using(MainForm main=new())
            {
                main.Show();Pump();Save(main,"interface-normal");Size normal=main.StageSizeForTest;
                main.ToggleCinematicForTest();Pump();
                Require(main.CinematicForTest&&main.StageSizeForTest.Height>normal.Height,"Cinema expands stage");Save(main,"cinema");
                main.ToggleFullscreenForTest();Pump();main.ToggleFullscreenForTest();Pump();
                Require(main.CinematicForTest,"Fullscreen preserves cinema");
                main.ToggleCinematicForTest();Pump();
                Require(!main.CinematicForTest&&main.StageSizeForTest==normal,"Cinema restores exact layout");
                main.ClientSize=new(1040,720);Pump();Save(main,"interface-minimum");
                Require(!main.RomLoadedForTest,"Showcase never loads ROM");main.Close();
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{InteractiveNativeShowcase=true,ReducedMotion=true,GeometryMorph=true,EffectsVisible=true,OpacityManual=true,EffectsPersistence=true,CinemaRestoresLayout=true,NoRomAutoload=true},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{InterfaceMotion.Enabled=motion;Environment.SetEnvironmentVariable("WARP4D_HOME",home);}
        Bitmap Capture(Control c,string name){Bitmap b=new(c.Width,c.Height);c.DrawToBitmap(b,c.ClientRectangle);b.Save(Path.Combine(output,name+".png"),ImageFormat.Png);return b;}
        void Save(Control c,string name){using var bitmap=Capture(c,name);}
    }
    private static void Pump(){for(int i=0;i<8;i++){Application.DoEvents();Thread.Sleep(40);}}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
