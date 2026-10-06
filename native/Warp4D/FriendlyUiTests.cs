using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Profiles;
using Warp4D.UI;

namespace Warp4D;

internal static class FriendlyUiTests
{
    internal static int Run(string output,string? rom)
    {
        Directory.CreateDirectory(output);string? home=Environment.GetEnvironmentVariable("WARP4D_HOME");bool motion=InterfaceMotion.Enabled;
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        Dictionary<string,object> results=[];
        try
        {
            using MainForm main=new(true);main.Show();Pump(400);
            Require(!main.RomLoadedForTest,"No ROM autoload");Require(main.ControlsVisibleForTest&&main.FriendlyPagesForTest==4,"Four guided sections available by default");
            Require(main.FriendlyGameActionsDisabledForTest,"Unavailable game actions disabled");
            Save(main,"welcome-normal");main.ClientSize=new(1040,720);Pump(200);Save(main,"welcome-minimum");
            Require(main.LiveStyleButtonsVisibleForTest,"All eleven live style buttons fit beside inspector");
            foreach(int i in Enumerable.Range(0,11)){main.ClickStyleForTest(i);Require(main.PresentationForTest.Name==LookCatalog.Names[i],"Style buttons retain correct preset identity");}
            results["GuidedLayoutAndElevenLiveStyles"]=true;
            var settings=new PresentationSettings();settings.ProjectionProfile.Name="User profile";settings.EditLayer("test","W:-1").Rotation.XY=42;
            main.ApplyPresentationForTest(settings);main.FriendlySetKnobForTest("Projection visibility",100);Require(main.PresentationForTest.Opacity==1,"Fully opaque plain-language visibility control");
            main.FriendlyUndoForTest();Require(main.PresentationForTest.Opacity==settings.Opacity,"Undo restores previous visual settings");
            main.FriendlyResetForTest();Require(main.PresentationForTest.ProjectionProfile.Name=="User profile","Reset preserves recognition profile");
            main.FriendlyUndoForTest();Require(main.PresentationForTest.LayerFor("test","W:-1")?.Rotation.XY==42,"Reset can be undone without losing layer edits");results["VisibilityUndoResetAndPreservation"]=true;
            main.FriendlyPageForTest(1);main.FriendlySetMotionForTest("Animate this look",true);Pump(100);main.FriendlySetKnobForTest("Movement speed",0);double frozen=main.GeometryTimeForTest;Pump(160);
            Require(main.PresentationForTest.CycleSpeed==0,"Speed control affects whole visual cycle");
            main.FriendlySetMotionForTest("Objects move independently",true);main.FriendlySetMotionForTest("Leave fading motion trails",true);main.FriendlySetMotionForTest("React to game sound",true);
            Require(main.PresentationForTest.Dimensions.Choreography&&main.PresentationForTest.Dimensions.Trails&&main.PresentationForTest.Dimensions.AudioReactive,"Plain language motion toggles work");
            main.FriendlyOpenFoldForTest("Blend two saved looks");main.FriendlyBlendForTest(400);Require(main.PresentationForTest.BlendAmount==.4f,"Integrated blend control works");
            main.FriendlyAutoBlendForTest(true);Pump(200);Require(main.PresentationForTest.BlendAmount!=.4f,"Integrated auto blend progresses");main.FriendlySetKnobForTest("Projection visibility",63);Pump(150);Require(main.PresentationForTest.Opacity==.63f,"Manual adjustment stops blend and opacity remains manual");
            results["IntegratedMotionAndBlend"]=true;
            for(int tab=0;tab<4;tab++){main.FriendlyPageForTest(tab);Pump(100);Save(main,"page-"+tab+"-minimum");}
            Size before=main.StageSizeForTest;main.ToggleFullscreenForTest();Pump(50);main.ToggleFullscreenForTest();Pump(100);Require(main.ControlsVisibleForTest&&main.StageSizeForTest==before,"Fullscreen restores guided layout");
            main.ToggleCinematicForTest();Pump(100);Require(main.CinematicForTest&&main.StageSizeForTest.Width>before.Width,"Focus view expands game");main.ToggleCinematicForTest();Pump(100);Require(main.StageSizeForTest==before,"Focus view restores all settings");results["FocusAndFullscreenRestoration"]=true;
            if(rom is not null)
            {
                main.LoadRomForTest(rom);Pump(1800);Require(main.HasSceneForTest,"Native game scene arrived");main.FriendlyObjectForTest();Pump(150);
                Require(main.FriendlySelectionActionsEnabledForTest,"Selection enables relevant actions");main.FriendlyShapeForTest(GeometryMode.Ribbon);Require(main.FriendlyObjectShapeForTest==GeometryMode.Ribbon,"Integrated object shape preview");
                string selected=main.FriendlySelectedKeyForTest!;main.FriendlyScopeForTest(true);main.FriendlyShapeForTest(GeometryMode.Duocylinder);Require(main.FriendlyObjectShapeForTest==GeometryMode.Duocylinder&&!main.PresentationForTest.ObjectGeometries.ContainsKey(selected),"Whole class scope applies and removes selected override");
                Save(main,"native-objects");main.FriendlyPageForTest(0);main.FriendlyFocusKnobForTest("Projection visibility");Pump(100);
                Message message=Message.Create(main.Handle,0x100,(IntPtr)Keys.Left,IntPtr.Zero);Require(!main.PreFilterMessage(ref message),"Arrow keys edit a focused slider instead of controlling the game");
                Require(main.InputMaskForTest==0,"No held controller input during value editing");
                main.ApplyLookForTest(0);Pump(100);Message right=Message.Create(main.Handle,0x100,(IntPtr)Keys.Right,IntPtr.Zero);Require(main.PreFilterMessage(ref right)&&main.InputMaskForTest!=0,"Gameplay regains arrow input after style selection");right.Msg=0x101;main.PreFilterMessage(ref right);
                Save(main,"native-style");results["NativePickingClassScopeAndInputFocus"]=true;
            }
            main.Close();File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{Environment.SetEnvironmentVariable("WARP4D_HOME",home);InterfaceMotion.Enabled=motion;}
        void Save(Control c,string name){using Bitmap bitmap=new(c.Width,c.Height);c.DrawToBitmap(bitmap,new(Point.Empty,c.Size));bitmap.Save(Path.Combine(output,name+".png"),ImageFormat.Png);}
    }
    private static void Pump(int milliseconds){long end=Environment.TickCount64+milliseconds;while(Environment.TickCount64<end){Application.DoEvents();Thread.Sleep(10);}}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
