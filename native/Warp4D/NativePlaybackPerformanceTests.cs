using System.Diagnostics;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class NativePlaybackPerformanceTests
{
    internal static int Run(string output,string rom,string mode,int seconds)
    {
        Directory.CreateDirectory(output);
        string? previous=Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            if(mode is not ("baseline" or "reuse" or "nametable-pixels-baseline" or "nametable-pixels" or "background-pixels-baseline" or "background-pixels" or "accent-storage-baseline" or "accent-storage" or "order-storage-baseline" or "order-storage" or "packing-baseline" or "packing" or "inventory-baseline" or "inventory" or "sprite-baseline" or "sprite" or "backdrop-baseline" or "backdrop" or "visibility-baseline" or "visibility" or "artwork-baseline" or "artwork" or "sprite-preflight-baseline" or "sprite-preflight" or "geometry-cache-baseline" or "geometry-cache" or "streaming-baseline" or "streaming" or "backdrop-run-baseline" or "backdrop-run" or "backdrop-cache-baseline" or "backdrop-cache") || seconds is <5 or >60)
                throw new ArgumentException("Use a supported baseline/reuse/packing/inventory mode and a 5..60 second measurement window.");
            if(BuiltInGameProfiles.Identify(File.ReadAllBytes(rom))?.Id!="icarus")
                throw new ArgumentException("This bounded native-field controller is specific to Kid Icarus.");
            using MainForm main=new(){SimulateInputFocusForTest=true,IgnorePointerForTest=true,ReuseCapturedScenesForTest=mode!="baseline",UseDirectVertexWritesForTest=mode!="packing-baseline",UseCachedTextureInventoryForTest=mode!="inventory-baseline"};
            main.MeasureNativeCaptureForTest=true;
            main.UseBulkSpriteDecodingForTest=mode!="sprite-baseline";
            main.UseSpriteArtworkPreflightForTest=mode=="sprite-preflight";
            main.UseDirectBackdropCountingForTest=mode=="backdrop";
            main.UseRunBackdropCountingForTest=mode=="backdrop-run";
            main.UseBackdropCacheForTest=mode is "backdrop-cache" or "reuse" or "order-storage-baseline" or "order-storage" or "accent-storage-baseline" or "accent-storage" or "background-pixels-baseline" or "background-pixels" or "nametable-pixels-baseline" or "nametable-pixels";
            if(mode is "nametable-pixels" or "nametable-pixels-baseline")main.UseNametablePixelReuseForTest=mode=="nametable-pixels";
            if(mode is "background-pixels" or "background-pixels-baseline")
                main.UseBackgroundPixelCacheForTest=mode=="background-pixels";
            main.UseReusableAccentStorageForTest=mode!="accent-storage-baseline";
            main.UseReusableOrderStorageForTest=mode!="order-storage-baseline";
            main.UseFusedBackdropVisibilityForTest=mode!="visibility-baseline";
            main.UseCombinedArtworkExtractionForTest=mode is not ("artwork-baseline" or "visibility-baseline" or "visibility");
            if(mode is "geometry-cache" or "geometry-cache-baseline")main.UseGeometryMapCacheForTest=mode=="geometry-cache";
            if(mode is "streaming" or "streaming-baseline")main.UseStreamingVertexBufferForTest=mode=="streaming";
            main.Show();DirectGpuStageTests.ShowOwnedWindowForTest(main);Pump(300);
            if(main.RomLoadedForTest||main.FriendlyPagesForTest!=0)throw new InvalidOperationException("Original interface and no autoload required.");
            main.LoadRomForTest(rom);Pump(1600);
            Press(Keys.Enter,NesButton.Start,100,3000);Press(Keys.Enter,NesButton.Start,350,2000);
            if(!IsField(main.LatestFrameForTest))throw new InvalidOperationException("Coherent native gameplay required, not menu/death.");
            if(mode is "geometry-cache" or "geometry-cache-baseline")main.ApplyLookForTest(6);
            main.SaveScreenshotForTest(Path.Combine(output,"warmup.png"));
            if(!main.RendererForTest.StartsWith("GPU"))throw new InvalidOperationException("GPU required.");
            main.CycleForTest(true);Pump(800);
            var settings=main.PresentationForTest;
            if(settings.Dimensions.AdaptiveQuality)throw new InvalidOperationException("No adaptive detail reduction allowed.");
            List<PlaybackPaintTiming> paints=new(seconds*120);
            ConcurrentQueue<FrameBuildTiming> frameBuilds=new();
            ConcurrentQueue<ProfileBuildTiming> profileBuilds=new();
            ConcurrentQueue<BackgroundBuildTiming> backgroundBuilds=new();
            List<object> states=new(seconds*15);
            main.PlaybackObserverForTest=paints.Add;
            var meshBefore=main.MeshAssemblyForTest;
            var nativeCaptureBefore=main.NativeCaptureStatsForTest;
            var buildsBefore=main.SceneBuildCountsForTest;
            main.CaptureBoundaryForTest(()=>
            {
                buildsBefore=main.SceneBuildCountsForTest;
                main.FrameBuildObserverForTest=frameBuilds.Enqueue;
                main.ProfileBuildObserverForTest=profileBuilds.Enqueue;
                main.BackgroundBuildObserverForTest=backgroundBuilds.Enqueue;
            });
            long sequenceBefore=main.PublishedSequenceForTest,readbacksBefore=main.GpuReadbackFramesForTest;
            int deliveredBefore=main.SceneDeliveryCountForTest,xBefore=main.LatestFrameForTest!.Ram[0x723];
            long allocatedBefore=GC.GetTotalAllocatedBytes();int[] gcBefore=[GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)];
            using Process process=Process.GetCurrentProcess();TimeSpan cpuBefore=process.TotalProcessorTime;
            bool cyclePhaseReset=mode is "nametable-pixels" or "nametable-pixels-baseline";
            if(cyclePhaseReset)main.ResetProjectionCycleTimeForTest();
            var rotationBefore=main.LiveRotationForTest;
            SetKey(Keys.Z,NesButton.B,true);bool right=true;SetKey(Keys.Right,NesButton.Right,true);
            long start=Stopwatch.GetTimestamp();int invalid=0,minScenery=int.MaxValue,minX=255,maxX=0;
            // Application.DoEvents can drain a perpetually busy queue for seconds.
            // Use the ordinary native message loop and a controller timer instead.
            using ApplicationContext context=new();
            using System.Windows.Forms.Timer controller=new(){Interval=100};
            int exitCode=1;
            controller.Tick+=(_,_)=>
            {
                double elapsed=Stopwatch.GetElapsedTime(start).TotalSeconds;
                var frame=main.LatestFrameForTest;
                bool field=IsField(frame);if(!field)invalid++;
                int x=frame?.Ram[0x723]??-1,scenery=main.ProjectedSceneryCountForTest;
                minScenery=Math.Min(minScenery,scenery);minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);
                states.Add(new{ElapsedSeconds=elapsed,Field=field,Sequence=frame?.Sequence,PublishedSequence=main.PublishedSequenceForTest,
                    X=x,Mode=frame?.Ram[0xa0],Transition=frame?.Ram[0x130],Death=frame?.Ram[0x3b],Scenery=scenery});
                if(field && (right&&x>=145 || !right&&x<=60))
                {
                    SetKey(right?Keys.Right:Keys.Left,right?NesButton.Right:NesButton.Left,false);
                    right=!right;SetKey(right?Keys.Right:Keys.Left,right?NesButton.Right:NesButton.Left,true);
                }
                if(elapsed>=seconds)
                {
                    controller.Stop();Finish();main.Close();context.ExitThread();
                }
            };
            controller.Start();Application.Run(context);
            return exitCode;
            void Finish()
            {
            main.PlaybackObserverForTest=null;
            FrameBuildTiming[] frameBuildSamples=[];
            ProfileBuildTiming[] profileBuildSamples=[];
            BackgroundBuildTiming[] backgroundBuildSamples=[];
            var buildsAfter=main.SceneBuildCountsForTest;
            main.CaptureBoundaryForTest(()=>
            {
                main.FrameBuildObserverForTest=null;
                main.ProfileBuildObserverForTest=null;
                main.BackgroundBuildObserverForTest=null;
                frameBuildSamples=frameBuilds.ToArray();
                profileBuildSamples=profileBuilds.ToArray();backgroundBuildSamples=backgroundBuilds.ToArray();
                buildsAfter=main.SceneBuildCountsForTest;
            });
            double duration=Stopwatch.GetElapsedTime(start).TotalSeconds;
            process.Refresh();
            double cpuMs=(process.TotalProcessorTime-cpuBefore).TotalMilliseconds;
            long allocated=GC.GetTotalAllocatedBytes()-allocatedBefore;
            int[] collections=[GC.CollectionCount(0)-gcBefore[0],GC.CollectionCount(1)-gcBefore[1],GC.CollectionCount(2)-gcBefore[2]];
            long readbacks=main.GpuReadbackFramesForTest-readbacksBefore;
            var rotationAfter=main.LiveRotationForTest;
            bool cycleAdvanced=rotationAfter.XW!=rotationBefore.XW||rotationAfter.YW!=rotationBefore.YW||rotationAfter.ZW!=rotationBefore.ZW;
            bool passed=invalid==0&&minScenery>=50&&paints.Count>=30&&readbacks==0&&main.DirectStageShownForTest&&
                main.PublishedSequenceForTest>sequenceBefore&&maxX-minX>=30&&cycleAdvanced;
            SetKey(Keys.Z,NesButton.B,false);SetKey(Keys.Right,NesButton.Right,false);SetKey(Keys.Left,NesButton.Left,false);
            double[] intervals=paints.Zip(paints.Skip(1),(a,b)=>(b.Timestamp-a.Timestamp)*1000d/Stopwatch.Frequency).ToArray();
            var result=new{Passed=passed,Mode=mode,CartridgeId="icarus",RomSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rom))),
                OriginalInterface=true,NoRomAutoload=true,SimulatedFocus=true,PointerIsolated=true,SpriteArtworkPreflight=mode=="sprite-preflight",Scope="Bounded first-field keyboard gameplay, ordinary movement/fire, no RAM writes. Real pointer hover/parallax isolated in this diagnostic only; overlays tested separately. Direct SwapBuffers-completed paints, not physical scanout or full-game performance.",
                GeometryMapCacheEnabled=main.UseGeometryMapCacheForTest,GeometryMapCache=main.GeometryMapCacheStatsForTest,
                BackdropCacheRequested=main.UseBackdropCacheForTest,BackdropCache=main.BackdropCacheStatsForTest,
                ReusableOrderStorage=main.UseReusableOrderStorageForTest,
                ReusableAccentStorage=main.UseReusableAccentStorageForTest,
                BackgroundPixelCacheRequested=main.UseBackgroundPixelCacheForTest,BackgroundPixelCache=main.BackgroundPixelCacheStatsForTest,
                NativeCaptureMeasured=main.MeasureNativeCaptureForTest,NativeCapture=main.NativeCaptureStatsForTest.Since(nativeCaptureBefore),
                NametablePixelReuseEnabled=main.UseNametablePixelReuseForTest,NametablePixelReuse=main.NametablePixelReuseStatsForTest,
                StreamingVertexBufferRequested=main.UseStreamingVertexBufferForTest,StreamingVertexBufferObserved=main.UsedStreamingVertexBufferForTest,
                CycleAdvanced=cycleAdvanced,CyclePhaseReset=cyclePhaseReset,RotationBefore=rotationBefore,RotationAfter=rotationAfter,
                Settings=settings,StageSize=main.StageSizeForTest.ToString(),Renderer=main.RendererForTest,RequestedSeconds=seconds,MeasuredSeconds=duration,
                Presentations=paints.Count,PresentationsPerSecond=paints.Count/duration,Intervals=Stats(intervals),
                Draw=Stats(paints.Select(p=>p.DrawMs)),Assembly=Stats(paints.Select(p=>p.AssemblyMs)),Lighting=Stats(paints.Select(p=>p.LightingMs)),
                Ordering=Stats(paints.Select(p=>p.OrderingMs)),Packing=Stats(paints.Select(p=>p.PackingMs)),Submission=Stats(paints.Select(p=>p.SubmissionMs)),Present=Stats(paints.Select(p=>p.PresentMs)),
                Atlas=Stats(paints.Select(p=>p.AtlasMs)),Driver=Stats(paints.Select(p=>p.DriverMs)),
                DrawAllocatedBytes=Stats(paints.Select(p=>(double)p.DrawAllocatedBytes)),AssemblyAllocatedBytes=Stats(paints.Select(p=>(double)p.AssemblyAllocatedBytes)),
                MeshAssembly=main.MeshAssemblyForTest.Since(meshBefore),
                Capture=Stats(frameBuildSamples.Select(p=>p.CaptureMs)),Recognition=Stats(frameBuildSamples.Select(p=>p.RecognitionMs)),
                Triangles=Stats(paints.Select(p=>(double)p.Triangles)),CpuMilliseconds=cpuMs,AllocatedBytes=allocated,GcCollections=collections,
                SceneRequests=buildsAfter.Requests-buildsBefore.Requests,SceneBuilds=buildsAfter.Completed-buildsBefore.Completed,SceneSkips=buildsAfter.Skipped-buildsBefore.Skipped,
                SceneDeliveries=main.SceneDeliveryCountForTest-deliveredBefore,NativeSequenceAdvance=main.PublishedSequenceForTest-sequenceBefore,
                ReadbacksDuringMeasurement=readbacks,LastDirectFallback=main.LastDirectFallbackForTest,InvalidFieldSamples=invalid,MinimumScenery=minScenery,PlayerXBefore=xBefore,MinimumX=minX,MaximumX=maxX,
                StateSamples=states,PaintSamples=paints,FrameBuildSamples=frameBuildSamples,
                ProfileBuildSamples=profileBuildSamples,BackgroundBuildSamples=backgroundBuildSamples};
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            main.SaveScreenshotForTest(Path.Combine(output,"final.png"));exitCode=passed?0:1;
            }

            void SetKey(Keys key,NesButton button,bool held)
            {
                Message message=Message.Create(main.Handle,held?0x100:0x101,(IntPtr)key,IntPtr.Zero);main.PreFilterMessage(ref message);
                if(((main.InputMaskForTest&(int)button)!=0)!=held)throw new InvalidOperationException("Native input mask mismatch.");
            }
            void Press(Keys key,NesButton button,int held,int settle){SetKey(key,button,true);Pump(held);SetKey(key,button,false);Pump(settle);}
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{Environment.SetEnvironmentVariable("WARP4D_HOME",previous);}
    }
    private static bool IsField(NesFrame? f)=>f is not null&&f.Ram[0xa0]==2&&f.Ram[0x130]==0&&f.Ram[0x3b]==0&&f.CaptureScanline==96&&f.NativeScreenSequence==f.Sequence;
    private static object Stats(IEnumerable<double> source)
    {
        double[] s=source.Order().ToArray();return new{Samples=s.Length,Average=s.Length==0?0:s.Average(),Median=Q(.5),P95=Q(.95),Maximum=s.Length==0?0:s[^1]};
        double Q(double q)=>s.Length==0?0:s[(int)Math.Ceiling((s.Length-1)*q)];
    }
    private static void Pump(int ms){long start=Stopwatch.GetTimestamp();while(Stopwatch.GetElapsedTime(start).TotalMilliseconds<ms){Application.DoEvents();Thread.Sleep(2);}}
}
