using System.Text.Json;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class CycleInteractionTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        string? previous=Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        try
        {
            using(MainForm main=new())
            {
                main.Show();Pump();
                for(int i=0;i<LookCatalog.Names.Length;i++)
                {
                    main.ApplyLookForTest(i);main.CycleForTest(false);main.CycleForTest(true);
                    float before=main.LiveRotationForTest.XW;double time=main.GeometryTimeForTest;
                    Pump();
                    Require(main.PresentationForTest.RotationAnimation.Enabled&&main.LiveRotationForTest.XW!=before,"Checkbox starts rotations: "+LookCatalog.Names[i]);
                    if(i>=5) Require(main.PresentationForTest.Geometry.Animate&&main.GeometryTimeForTest>time,"Checkbox starts shape animation");
                }
                float opacity=main.PresentationForTest.Opacity;
                main.ToggleControlsForTest();Pump();main.RandomizeCycleForTest();
                var settings=main.PresentationForTest;
                Require(settings.Animate&&settings.RotationCyclePhases.Count==6&&settings.RotationCycleSpeeds.Values.Distinct().Count()==6,"Live randomize button starts independent axes");
                Require(settings.Opacity==opacity,"Randomize preserves opacity");
                main.Close();
            }
            using WarpRendererControl renderer=new();
            var randomized=LookCatalog.Create(6,new());randomized.Opacity=.83f;
            randomized.EditLayer("object","W:-1").Rotation.XY=72;
            PresentationAnimator.RandomizeCycle(randomized,new Random(42));
            renderer.ApplySettings(randomized);
            PresentationAnimator.Apply(renderer,randomized,0);
            float[] first=Angles(renderer);
            Require(first.Distinct().Count()==6,"Distinct random slider angles");
            PresentationAnimator.Apply(renderer,randomized,1.5);
            Require(first.Zip(Angles(renderer)).All(p=>p.First!=p.Second),"All six axes continue moving");
            Require(renderer.ProjectionOpacity==.83f&&randomized.LayerFor("object","W:-1")!.Rotation.XY==72,"Manual opacity/layer edits preserved");
            string preset=Path.Combine(output,"randomized.warp4d-look.json");
            PresentationSettingsStore.WriteToFile(preset,randomized);
            var reopened=PresentationSettingsStore.ReadFromFile(preset);
            Require(reopened.RotationCyclePhases.SequenceEqual(randomized.RotationCyclePhases)&&reopened.RotationCycleSpeeds.SequenceEqual(randomized.RotationCycleSpeeds),"Random cycle settings round-trip");
            Rotation4D zero=default;
            foreach(var item in new[]{(Keys.None,0),(Keys.Control,3),(Keys.Shift,0),(Keys.Control|Keys.Shift,1)})
            {
                var changed=WarpRendererControl.DragRotation(zero,.2f,.3f,item.Item1);
                if(item.Item1==Keys.None) Require(changed.XW!=0&&changed.YW!=0&&changed.XZ==0,"Default W drag");
                else
                {
                    Require(changed!=zero,"Modifier drag moves");
                    Vector4F axis=item.Item2 switch {0=>new(1,0,0,0),1=>new(0,1,0,0),_=>new(0,0,0,1)};
                    Require(new PreparedRotation4D(changed).Apply(axis)==axis,"Locked axis unchanged: "+item.Item1);
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{AllElevenCheckboxCycles=true,RandomizeButton=true,IndependentAxes=true,OpacityPreserved=true,LayerEditsPreserved=true,PresetRoundtrip=true,ModifierAxisLocks=true},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{Environment.SetEnvironmentVariable("WARP4D_HOME",previous);}
    }
    private static float[] Angles(WarpRendererControl r)=>[r.AngleXYDegrees,r.AngleXZDegrees,r.AngleXWDegrees,r.AngleYZDegrees,r.AngleYWDegrees,r.AngleZWDegrees];
    private static void Pump(){for(int i=0;i<8;i++){Application.DoEvents();Thread.Sleep(40);}}
    private static void Require(bool condition,string label){if(!condition)throw new InvalidOperationException(label);}
}
