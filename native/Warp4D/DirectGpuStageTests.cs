using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class DirectGpuStageTests
{
    internal static void ShowOwnedWindowForTest(Control window)=>ShowWindow(window.Handle,5);
    internal static int RunPerformance(string output,string framePath,string profilePath,string cartridgeId,bool streamingOnly=false)
    {
        Directory.CreateDirectory(output);
        try
        {
            var frame=CartridgeViewport.NormalizeCapture(cartridgeId,JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(framePath))!);
            var profile=GameRecognitionProfileStore.ReadFromFile(profilePath);
            if(frame.CaptureScanline!=96||frame.Sequence!=frame.NativeScreenSequence||!profile.IsActive(frame))throw new InvalidDataException("Coherent active gameplay required.");
            using var scene=new SmbProfile().Build(frame,false,null,profile);
            int projected=scene.Objects.Count(item=>item.ProjectionEnabled&&item.SortOrder<20);
            if(projected<50)throw new InvalidDataException("Dense scenery required.");
            using Form form=new(){ClientSize=new(1000,780),Location=new(20,20),StartPosition=FormStartPosition.Manual,Text="Warp4D native presentation benchmark"};
            using var renderer=new WarpRendererControl{Dock=DockStyle.Fill,UseGpu=true,EnableStyleTransitions=false};
            renderer.SetScene(scene.Clone());form.Controls.Add(renderer);form.Show();ShowWindow(form.Handle,5);Application.DoEvents();
            List<object> runs=[];
            foreach(int style in new[]{0,5,6})
            {
                var settings=LookCatalog.Create(style,new());settings.Effects.Enabled=false;settings.Dimensions.AdaptiveQuality=false;
                renderer.ApplySettings(settings);
                List<double> legacy=[],direct=[],legacyWall=[],directWall=[],present=[],readback=[];
                int directReadbacks=0;long uploads=0;
                Bitmap? lastLegacy=null,lastDirect=null;
                for(int iteration=0;iteration<23;iteration++)
                {
                    double seconds=iteration/60d;
                    renderer.MotionSecondsForTest=seconds;PresentationAnimator.Apply(renderer,settings,seconds);
                    renderer.ProjectionCycleSeconds=renderer.GeometryCycleSeconds=seconds;
                    if(iteration%2==0){Paint(false);Paint(true);}else{Paint(true);Paint(false);}
                    if(iteration==2)uploads=renderer.TextureUploads;
                    void Paint(bool native)
                    {
                        renderer.DirectGpuPresentation=streamingOnly||native;
                        if(streamingOnly)renderer.UseStreamingVertexBufferForTest=native;
                        long reads=renderer.GpuReadbackFrames,start=Stopwatch.GetTimestamp();
                        renderer.Invalidate();renderer.Update();
                        double wall=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        Require(renderer.RendererStatus.StartsWith("GPU"),"GPU required");
                        if(native||streamingOnly)
                        {
                            Require(renderer.DirectGpuStageShown,$"Direct playback required: {renderer.DirectGpuFailureForTest}");
                            directReadbacks+=(int)(renderer.GpuReadbackFrames-reads);
                            Require(renderer.GpuReadbackMs==0,"No direct playback readback");
                            if(streamingOnly)Require(renderer.UsedStreamingVertexBufferForTest==native,"Observed streaming mode required");
                        }
                        if(iteration<3)return;
                        (native?direct:legacy).Add(renderer.LastDrawMilliseconds);
                        (native?directWall:legacyWall).Add(wall);
                        if(native)present.Add(renderer.GpuPresentMs);else readback.Add(renderer.GpuReadbackMs);
                        if(streamingOnly&&iteration==22)
                        {
                            if(native)lastDirect=renderer.ReadStoredStageForTest();
                            else lastLegacy=renderer.ReadStoredStageForTest();
                        }
                    }
                }
                using var old=streamingOnly?lastLegacy!:renderer.CloneLastReadbackForTest();
                using var current=streamingOnly?lastDirect!:renderer.ReadStoredStageForTest();
                Require(ImagePixels.Read(old).Pixels.SequenceEqual(ImagePixels.Read(current).Pixels),"Exact dense gameplay source pixels");
                Require(directReadbacks==0,"Zero direct playback readbacks");
                current.Save(Path.Combine(output,$"style-{style}-stage.png"));
                runs.Add(new{Style=LookCatalog.Names[style],RenderScale=settings.RenderScale,LegacyMedianPaintMs=Median(legacy),DirectMedianPaintMs=Median(direct),LegacyMedianWallMs=Median(legacyWall),DirectMedianWallMs=Median(directWall),LegacyMedianReadbackMs=Median(readback),DirectMedianPresentMs=Median(present),DirectReadbacks=directReadbacks,ExactSourcePixels=true,Triangles=renderer.SurfaceTriangleCount,DrawCalls=renderer.GpuDrawCalls,SteadyTextureUploads=renderer.TextureUploads-uploads});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Scope=streamingOnly?"Alternating client-memory/streaming-buffer direct GPU presentations in one process, identical poses/detail, 3 warmup/20 measured per style. Two final diagnostic readbacks outside timed paints. Not sustained emulator FPS or physical display.":"Alternating on-screen bitmap/native presentation, identical fixed poses, full authored detail, 3 warmup/20 measured. Includes native show/hide switching; not sustained emulator FPS.",StreamingComparison=streamingOnly,Renderer=renderer.RendererStatus,Frame=framePath,Profile=profilePath,ProjectedScenery=projected,AdaptiveQuality=false,Effects=false,Runs=runs},new JsonSerializerOptions{WriteIndented=true}));form.Close();return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        static double Median(List<double> values)=>values.Order().ElementAt(values.Count/2);
    }

    internal static int Run(string output,bool textureOnly=false,bool visibleDiagnostic=false,bool streaming=false)
    {
        Directory.CreateDirectory(output);
        bool motion=InterfaceMotion.Enabled;
        try
        {
            InterfaceMotion.Enabled=true;
            using var sample=LookGalleryForm.CreateSample();
            using var geometry=new WarpRendererControl{UseGpu=true};
            using var gpu=new OpenGlSurfaceRenderer();
            Require(gpu.CanPresentDirectly,"GPU required for this test");
            File.WriteAllText(Path.Combine(output,"pixel-format.txt"),gpu.PixelFormatForTest);
            int exact=0;
            foreach(var mode in Enum.GetValues<GeometryMode>())
            for(int pose=0;pose<12;pose++)
            {
                var settings=new PresentationSettings{Animate=true,Geometry=new(){Mode=mode,Animate=true},CrossSections=pose<6?5:3,Opacity=pose==4?0:pose==8?1:.63f};
                geometry.ApplySettings(settings);
                geometry.MotionSecondsForTest=pose*.071;
                geometry.ProjectionCycleSeconds=geometry.GeometryCycleSeconds=pose*.071;
                PresentationAnimator.Apply(geometry,settings,pose*.071);
                List<SurfaceGroup> groups=[];
                SurfaceGroup background=new();
                var pixels=ImagePixels.Read(sample.Background);
                background.Quad(new(new(0,0),0,1,0,0),new(new(256,0),0,1,1,0),new(new(256,240),0,1,1,1),new(new(0,240),0,1,0,1),pixels,Color.White,1);
                groups.Add(background);
                foreach(var item in sample.Objects){var group=geometry.GeometrySurfacesForTest(item);SurfaceEffects.Light(group,.65f);groups.Add(group);}
                gpu.UseStreamingVertexBufferForTest=false;
                using Bitmap old=(Bitmap)gpu.Render(groups,new(0,0,256,240),2).Clone();
                int priorCalls=gpu.LastDrawCalls;
                gpu.UseStreamingVertexBufferForTest=streaming;
                using Bitmap current=(Bitmap)gpu.RenderPresentationTextureForTest(groups,new(0,0,256,240),2).Clone();
                if(streaming)Require(gpu.LastUsedStreamingVertexBuffer&&gpu.LastDrawCalls==priorCalls,"Actual streaming buffer and unchanged draw order/runs required");
                var oldPixels=ImagePixels.Read(old).Pixels;var newPixels=ImagePixels.Read(current).Pixels;
                if(!oldPixels.SequenceEqual(newPixels))
                {
                    old.Save(Path.Combine(output,"legacy-failed.png"));current.Save(Path.Combine(output,"texture-failed.png"));
                    File.WriteAllText(Path.Combine(output,"pixel-differences.json"),JsonSerializer.Serialize(Enumerable.Range(0,oldPixels.Length).Where(i=>oldPixels[i]!=newPixels[i]).Take(40).Select(i=>new{X=i%old.Width,Y=i/old.Width,Old=oldPixels[i].ToString("X8"),New=newPixels[i].ToString("X8")})));
                    throw new InvalidOperationException($"Legacy/GPU texture exact pixels: {mode}, pose {pose}");
                }
                exact++;
            }
            if(textureOnly)
            {
                File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new
                {
                    Passed=true,Device=gpu.DeviceName,ExactGpuTextureFrames=exact,StreamingVertexBuffer=streaming,DisplayedStageTested=false,
                    Scope="GPU texture/readback equality only. Does not prove displayed presentation, input, screenshots, recording or overlay behavior. Full display test remains required."
                },new JsonSerializerOptions{WriteIndented=true}));
                return 0;
            }
            using Form form=new(){ClientSize=new(720,600),StartPosition=FormStartPosition.Manual,Location=new(40,40),Text="Warp4D direct GPU regression"};
            using var renderer=new WarpRendererControl{Dock=DockStyle.Fill,UseGpu=true,MotionSecondsForTest=.5,EnableStyleTransitions=false};
            renderer.ApplySettings(new(){Animate=false,Opacity=.63f,Geometry=new(){Mode=GeometryMode.Duocylinder},Dimensions=new(){AdaptiveQuality=false}});
            renderer.SetScene(sample.Clone());form.Controls.Add(renderer);form.Show();
            int offered=0;bool record=false;
            renderer.CleanFrameRendered+=_=>offered++;
            renderer.NeedsCleanFrame=()=>record;
            // The diagnostic process is launched hidden; explicitly reveal its
            // owned test window so WM_PAINT and GL pixel ownership are real.
            ShowWindow(form.Handle,5);Application.DoEvents();
            // Pixel ownership is evaluated while drawing, not when reading.
            // Establish an unobscured owned window before either GPU paint.
            OwnedGpuDisplayInspection.PrepareVisible(form);
            Paint();Paint();
            Require(renderer.DirectGpuStageShown && renderer.GpuPresentedFrames>=2,$"Native child stage presents ordinary playback: {renderer.DirectGpuFailureForTest}, presented={renderer.GpuPresentedFrames}, readback={renderer.GpuReadbackFrames}");
            Require(renderer.GpuReadbackFrames==0 && renderer.GpuReadbackMs==0 && offered==0,"No production readback or recording callback in ordinary playback");
            if(visibleDiagnostic)
            {
                using var front=renderer.ReadDisplayedStageForTest();
                using var stored=renderer.ReadStoredStageForTest();
                using var composed=OwnedGpuDisplayInspection.Capture(form,renderer.DirectGpuStageHandleForTest,front.Size);
                using var visibleFront=renderer.ReadDisplayedStageForTest();
                using var backStage=renderer.ReadDisplayedBackStageForTest();
                var source=ImagePixels.Read(stored).Pixels;
                var actual=ImagePixels.Read(composed).Pixels;
                var glFront=ImagePixels.Read(front).Pixels;
                var visibleGlFront=ImagePixels.Read(visibleFront).Pixels;
                var glBack=ImagePixels.Read(backStage).Pixels;
                bool screenExact=Exact(actual,composed.Size),frontExact=Exact(glFront,front.Size);
                // Independently validate the screen-read path using ordinary
                // parent painting, without the native OpenGL child drawable.
                renderer.DirectGpuPresentation=false;Paint();Application.DoEvents();
                using Bitmap gdiReference=new(renderer.Width,renderer.Height);
                renderer.DrawToBitmap(gdiReference,renderer.ClientRectangle);
                using var gdiScreen=OwnedGpuDisplayInspection.Capture(form,renderer.Handle,gdiReference.Size);
                var gdiExpected=ImagePixels.Read(gdiReference).Pixels;
                var gdiActual=ImagePixels.Read(gdiScreen).Pixels;
                int gdiMatching=Enumerable.Range(0,gdiActual.Length).Count(i=>gdiActual[i]==gdiExpected[i]);
                bool gdiExact=gdiMatching==gdiActual.Length;
                if(gdiExact)gdiScreen.Save(Path.Combine(output,"owned-gdi-reference.png"));
                Rectangle stageBounds=Rectangle.Round(renderer.GameBoundsForTest);
                int gdiStageMatching=0;
                for(int y=stageBounds.Top;y<stageBounds.Bottom;y++)
                    for(int x=stageBounds.Left;x<stageBounds.Right;x++)
                    {
                        int index=y*renderer.Width+x;
                        if(gdiActual[index]==gdiExpected[index])gdiStageMatching++;
                    }
                int gdiStagePixels=stageBounds.Width*stageBounds.Height;
                bool gdiStageExact=gdiStageMatching==gdiStagePixels;
                if(gdiStageExact)
                    using(var knownStage=gdiScreen.Clone(stageBounds,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        knownStage.Save(Path.Combine(output,"owned-gdi-stage.png"));
                File.WriteAllText(Path.Combine(output,"display-diagnostic.json"),JsonSerializer.Serialize(new{
                    Scope="Owned visible screen pixels vs stored GPU source, independently of GL_FRONT. Diagnostic only, not full release verification.",
                    Drawable=renderer.DirectGpuDrawableDiagnosticForTest,
                    Buffer=renderer.DirectGpuBufferDiagnosticForTest,
                    OpenGlBackExact=Exact(glBack,backStage.Size),OpenGlBackMatchingPixels=Matching(glBack,backStage.Size),OpenGlBackAllBlack=glBack.All(p=>p==unchecked((int)0xff000000)),
                    // A single flat color cannot contain unrelated desktop text.
                    FlatScreenColor=actual.Distinct().Take(2).Count()==1?actual[0].ToString("X8"):null,
                    GdiScreenExact=gdiExact,GdiScreenMatchingPixels=gdiMatching,GdiScreenPixels=gdiActual.Length,
                    GdiScreenAllWhite=gdiActual.All(p=>p==unchecked((int)0xffffffff)),
                    GdiStageExact=gdiStageExact,GdiStageMatchingPixels=gdiStageMatching,GdiStagePixels=gdiStagePixels,
                    ScreenExact=screenExact,ScreenMatchingPixels=Matching(actual,composed.Size),ScreenMatchingParentBackdrop=actual.Count(p=>p==renderer.BackColor.ToArgb()),ScreenDistinctColors=actual.Distinct().Count(),ScreenPixels=actual.Length,ScreenAllBlack=actual.All(p=>p==unchecked((int)0xff000000)),SourceAllBlack=source.All(p=>p==unchecked((int)0xff000000)),OpenGlFrontExact=frontExact,VisibleOpenGlFrontExact=Exact(visibleGlFront,visibleFront.Size),OpenGlFrontAllBlack=glFront.All(p=>p==unchecked((int)0xff000000)),VisibleOpenGlFrontAllBlack=visibleGlFront.All(p=>p==unchecked((int)0xff000000)),Width=composed.Width,Height=composed.Height
                },new JsonSerializerOptions{WriteIndented=true}));
                Require(screenExact,"Captured owned GPU stage must be pixel-exact; failing screen pixels are intentionally not saved.");
                composed.Save(Path.Combine(output,"owned-visible-stage.png"));
                stored.Save(Path.Combine(output,"gpu-source.png"));
                front.Save(Path.Combine(output,"gl-front-stage.png"));
                visibleFront.Save(Path.Combine(output,"visible-gl-front-stage.png"));
                form.Close();return 0;
                bool Exact(int[] pixels,Size size)
                    =>Matching(pixels,size)==pixels.Length;
                int Matching(int[] pixels,Size size)
                {
                    int matches=0;
                    for(int y=0;y<size.Height;y++)for(int x=0;x<size.Width;x++)
                    {
                        int sx=Math.Clamp((int)((x+.5)*stored.Width/size.Width),0,stored.Width-1);
                        int sy=stored.Height-1-Math.Clamp((int)((size.Height-y-.5)*stored.Height/size.Height),0,stored.Height-1);
                        if(pixels[y*size.Width+x]==source[sy*stored.Width+sx])matches++;
                    }
                    return matches;
                }
            }
            using(var displayed=renderer.ReadDisplayedStageForTest())
            using(var stored=renderer.ReadStoredStageForTest())
            {
                displayed.Save(Path.Combine(output,"native-stage.png"));stored.Save(Path.Combine(output,"gpu-source.png"));
                var actual=ImagePixels.Read(displayed).Pixels;var source=ImagePixels.Read(stored).Pixels;
                for(int y=0;y<displayed.Height;y++)for(int x=0;x<displayed.Width;x++)
                {
                    int sx=Math.Clamp((int)((x+.5)*stored.Width/displayed.Width),0,stored.Width-1);
                    int sy=stored.Height-1-Math.Clamp((int)((displayed.Height-y-.5)*stored.Height/displayed.Height),0,stored.Height-1);
                    Require(actual[y*displayed.Width+x]==source[sy*stored.Width+sx],$"Displayed nearest-neighbour GPU copy exact at {x},{y}");
                }
            }
            // WM_PRINT uses the old readback path even with the child still shown.
            using Bitmap capture=new(renderer.Width,renderer.Height),reference=new(renderer.Width,renderer.Height);
            renderer.DrawToBitmap(capture,renderer.ClientRectangle);
            Require(renderer.DirectGpuStageShown,"Screenshot does not hide/flash the live child");
            renderer.DirectGpuPresentation=false;
            renderer.DrawToBitmap(reference,renderer.ClientRectangle);
            Require(ImagePixels.Read(capture).Pixels.SequenceEqual(ImagePixels.Read(reference).Pixels),"DrawToBitmap exact with native child present");
            capture.Save(Path.Combine(output,"screenshot.png"));
            renderer.DirectGpuPresentation=true;Paint();
            long before=renderer.GpuReadbackFrames;
            record=true;Paint();Require(renderer.DirectGpuStageShown && offered>0 && renderer.GpuReadbackFrames>before,"Recording returns clean source frames without hiding the GPU stage");
            record=false;Paint();Require(renderer.DirectGpuStageShown,"Recording stop resumes native stage");
            renderer.RevealOriginal=true;renderer.NativeScreenPixels=ImagePixels.Read(sample.Background).Pixels;Paint();
            Require(!renderer.DirectGpuStageShown,"Original view hides projected native stage");
            renderer.RevealOriginal=false;Paint();
            long hoverReadbacks=renderer.GpuReadbackFrames;
            renderer.SetHoveredObjectForTest(sample.Objects[0]);Paint();
            Require(!renderer.DirectGpuStageShown&&renderer.GpuReadbackFrames>hoverReadbacks,"Hover overlay retains bitmap readback");
            renderer.SetHoveredObjectForTest(null);Paint();Require(renderer.DirectGpuStageShown,"Leaving hover resumes direct GPU playback");
            renderer.PaintObjects=true;Paint();Require(!renderer.DirectGpuStageShown,"Profile painting keeps original overlay/picking path");
            renderer.PaintObjects=false;renderer.EditLayers=true;Paint();Require(!renderer.DirectGpuStageShown,"Projection editing keeps overlay path");
            renderer.EditLayers=false;Paint();
            IntPtr stage=renderer.DirectGpuStageHandleForTest;
            Require(stage!=IntPtr.Zero,"Native input target exists");
            float beforeRotation=renderer.AngleXWDegrees;
            SendMessage(stage,0x0201,(IntPtr)1,Pack(12,12));
            SendMessage(stage,0x0200,(IntPtr)1,Pack(32,26));
            SendMessage(stage,0x0202,IntPtr.Zero,Pack(32,26));
            Require(renderer.AngleXWDegrees!=beforeRotation && !renderer.Capture,"Native child forwards drag and releases capture");
            renderer.SelectedObjectKey=sample.Objects[0].PresentationKey;Paint();Require(!renderer.DirectGpuStageShown,"Selected-object hyperframe preserved");
            renderer.SelectedObjectKey=null;
            renderer.UseGpu=false;Paint();Require(!renderer.DirectGpuStageShown,"Software fallback hides child");
            renderer.UseGpu=true;Paint();Require(renderer.DirectGpuStageShown,"GPU stage resumes");
            form.ClientSize=new(704,584);Paint();Require(renderer.DirectGpuStageShown,"Resize preserves native stage");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Device=gpu.DeviceName,ExactGpuTextureFrames=exact,ExactDisplayedNearestNeighbour=true,ZeroReadbackPlayback=true,ExactScreenshot=true,RecordingFallback=true,EditorOverlays=true,OriginalView=true,NativeDragInput=true,Resize=true,SoftwareFallback=true,PresentedFrames=renderer.GpuPresentedFrames,ReadbackFrames=renderer.GpuReadbackFrames},new JsonSerializerOptions{WriteIndented=true}));
            form.Close();return 0;
            void Paint(){renderer.Invalidate();renderer.Update();}
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{InterfaceMotion.Enabled=motion;}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static IntPtr Pack(int x,int y)=>(IntPtr)((y&0xffff)<<16|(x&0xffff));
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window,int command);
}
