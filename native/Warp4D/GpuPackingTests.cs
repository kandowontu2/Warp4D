using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class GpuPackingTests
{
    internal static int Run(string output,string framePath,string profilePath,string cartridgeId,bool tintOnly=false,bool byteColorsOnly=false)
    {
        Directory.CreateDirectory(output);
        try
        {
            var frame=CartridgeViewport.NormalizeCapture(cartridgeId,JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(framePath))!);
            var profile=GameRecognitionProfileStore.ReadFromFile(profilePath);
            if(frame.CaptureScanline!=96||frame.Sequence!=frame.NativeScreenSequence||!profile.IsActive(frame))throw new InvalidDataException("Coherent active gameplay required.");
            using var scene=new SmbProfile().Build(frame,false,null,profile);
            using var renderer=new WarpRendererControl{UseGpu=false,EnableStyleTransitions=false};
            using var gpu=new OpenGlSurfaceRenderer();if(!gpu.Available)throw new InvalidOperationException("GPU required.");
            List<object> runs=[];
            foreach(int style in new[]{0,5,6})
            {
                var settings=LookCatalog.Create(style,new());settings.Effects.Enabled=false;settings.Dimensions.AdaptiveQuality=false;renderer.ApplySettings(settings);
                foreach(double pose in new[]{.07,.43,.81})
                {
                    renderer.MotionSecondsForTest=pose;PresentationAnimator.Apply(renderer,settings,pose);
                    renderer.ProjectionCycleSeconds=renderer.GeometryCycleSeconds=pose;
                    var groups=scene.Objects.Select(renderer.GeometrySurfacesForTest).ToArray();
                    _=gpu.Render(groups,new RectangleF(0,0,256,240),1); // Atlas initialized outside measurement.
                    runs.Add(new{Style=style,Pose=pose,Packing=gpu.MeasurePackingForTest(groups,new RectangleF(0,0,256,240),96,tintOnly,byteColorsOnly)});
                }
            }
            SurfaceGroup edge=new(){Offset=new(-7.25f,11.75f)};
            foreach(Color color in new[]{Color.Empty,Color.Transparent,Color.Red,Color.FromArgb(137,29,81,231)})
            foreach(float opacity in new[]{0f,BitConverter.UInt32BitsToSingle(0x80000000),.18f,1f,float.NaN,float.PositiveInfinity})
            {
                var a=new SurfaceVertex(new(-3,17),float.NaN,.5f,-1,2);
                var b=new SurfaceVertex(new(18,-9),float.NegativeInfinity,1,0,1);
                var c=new SurfaceVertex(new(5,13),float.PositiveInfinity,2,1,0);
                edge.Triangle(a,b,c,null,color,opacity);edge.Triangle(c,b,a,null,color,opacity);
            }
            if(byteColorsOnly)
                for(int channel=0;channel<256;channel++)
                    edge.Triangle(new(new(-5,3),-.2f,.5f,-1,2),new(new(8,7),.1f,1,0,1),new(new(9,11),.3f,2,1,0),null,
                        Color.FromArgb(channel,channel,255-channel,(channel*71)&255),.37f);
            _=gpu.MeasurePackingForTest([edge],new RectangleF(-2.5f,3.5f,256,240),40,tintOnly,byteColorsOnly);
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,ExactEdgeTriangles=edge.Triangles.Count,Scope=tintOnly?"Alternating direct vertex writes with original per-triangle tint conversion versus exact-bit local tint reuse; every produced byte equal. Packing only, not sustained FPS.":"Alternating vertex packing only; every produced byte equal. No GPU/frame timing or sustained-FPS claim.",Frame=framePath,Profile=profilePath,Device=gpu.DeviceName,Runs=runs},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
