using System.Diagnostics;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class GpuPerformanceTests
{
    internal static int RunLighting(string output,string framePath,string profilePath,string cartridgeId)
    {
        Directory.CreateDirectory(output);
        try
        {
            var frame=CartridgeViewport.NormalizeCapture(cartridgeId,JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(framePath))!);
            var profile=GameRecognitionProfileStore.ReadFromFile(profilePath);
            if(frame.CaptureScanline!=96||frame.Sequence!=frame.NativeScreenSequence||!profile.IsActive(frame))throw new InvalidDataException("Coherent active gameplay required.");
            using var scene=new SmbProfile().Build(frame,false,null,profile);
            using var renderer=new WarpRendererControl{UseGpu=false,EnableStyleTransitions=false};
            List<object> runs=[];
            foreach(int style in new[]{0,5,6})
            {
                var settings=LookCatalog.Create(style,new());settings.Effects.Enabled=false;settings.Dimensions.AdaptiveQuality=false;renderer.ApplySettings(settings);
                List<double> legacy=[],candidate=[];
                for(int iteration=0;iteration<23;iteration++)
                {
                    renderer.MotionSecondsForTest=iteration/60d;PresentationAnimator.Apply(renderer,settings,iteration/60d);
                    renderer.ProjectionCycleSeconds=renderer.GeometryCycleSeconds=iteration/60d;
                    var old=scene.Objects.Select(renderer.GeometrySurfacesForTest).ToArray();
                    var current=scene.Objects.Select(renderer.GeometrySurfacesForTest).ToArray();
                    if(iteration%2==0){Measure(old,false,legacy);Measure(current,true,candidate);}else{Measure(current,true,candidate);Measure(old,false,legacy);}
                    for(int g=0;g<old.Length;g++)
                    {
                        if(old[g].Triangles.Count!=current[g].Triangles.Count)throw new InvalidDataException("Lighting count mismatch.");
                        for(int t=0;t<old[g].Triangles.Count;t++)if(old[g].Triangles[t]!=current[g].Triangles[t])throw new InvalidDataException($"Lighting mismatch style={style}, pose={iteration}, group={g}, triangle={t}.");
                    }
                    void Measure(SurfaceGroup[] groups,bool packed,List<double> times)
                    {
                        long start=Stopwatch.GetTimestamp();foreach(var group in groups)SurfaceEffects.Light(group,.65f,packed);
                        if(iteration>=3)times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    }
                }
                runs.Add(new{Style=LookCatalog.Names[style],ExactLighting=true,LegacyMedianMs=legacy.Order().ElementAt(10),CandidateMedianMs=candidate.Order().ElementAt(10)});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Scope="Alternating CPU lighting only, identical poses/strength; mesh preparation outside timed region. Not whole-frame timing.",Frame=framePath,Profile=profilePath,Runs=runs},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    internal static int RunAssembly(string output,string framePath,string profilePath,string cartridgeId,bool descriptorsOnly=false,bool geometryCacheOnly=false)
    {
        Directory.CreateDirectory(output);
        try
        {
            var frame=CartridgeViewport.NormalizeCapture(cartridgeId,JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(framePath))!);
            var profile=GameRecognitionProfileStore.ReadFromFile(profilePath);
            if(frame.CaptureScanline!=96||frame.Sequence!=frame.NativeScreenSequence||!profile.IsActive(frame))throw new InvalidDataException("Coherent active gameplay required.");
            using var scene=new SmbProfile().Build(frame,false,null,profile);
            using WarpRendererControl legacy=new(){UseGpu=false,EnableStyleTransitions=false,UseBatchedProjectionForTest=descriptorsOnly,UseCachedSheetDescriptorsForTest=!descriptorsOnly};
            using WarpRendererControl optimized=new(){UseGpu=false,EnableStyleTransitions=false};
            if(geometryCacheOnly)
            {
                legacy.UseBatchedProjectionForTest=true;
                legacy.UseCachedSheetDescriptorsForTest=true;
                legacy.UseGeometryMapCacheForTest=false;
                optimized.UseGeometryMapCacheForTest=true;
            }
            List<object> runs=[];
            foreach(int style in new[]{0,5,6})
            {
                var settings=LookCatalog.Create(style,new());settings.Effects.Enabled=false;settings.Dimensions.AdaptiveQuality=false;
                legacy.ApplySettings(settings.Clone());optimized.ApplySettings(settings.Clone());
                SurfaceGroup?[] oldGroups=new SurfaceGroup?[scene.Objects.Count],newGroups=new SurfaceGroup?[scene.Objects.Count];
                List<double> oldTimes=[],newTimes=[];List<long> oldAllocation=[],newAllocation=[];
                for(int iteration=0;iteration<23;iteration++)
                {
                    foreach(var renderer in new[]{legacy,optimized})
                    {
                        renderer.MotionSecondsForTest=iteration/60d;
                        PresentationAnimator.Apply(renderer,renderer.Settings,iteration/60d);
                        renderer.ProjectionCycleSeconds=renderer.GeometryCycleSeconds=iteration/60d;
                    }
                    if(iteration%2==0){Build(legacy,oldGroups,oldTimes,oldAllocation);Build(optimized,newGroups,newTimes,newAllocation);}
                    else{Build(optimized,newGroups,newTimes,newAllocation);Build(legacy,oldGroups,oldTimes,oldAllocation);}
                    for(int group=0;group<oldGroups.Length;group++)
                    {
                        var a=oldGroups[group]!.Triangles;var b=newGroups[group]!.Triangles;
                        if(a.Count!=b.Count)throw new InvalidDataException("Assembly triangle count mismatch.");
                        for(int i=0;i<a.Count;i++)if(a[i]!=b[i])throw new InvalidDataException($"Assembly mismatch style {style}, iteration {iteration}, group {group}, triangle {i}.");
                    }
                    void Build(WarpRendererControl renderer,SurfaceGroup?[] groups,List<double> times,List<long> allocation)
                    {
                        long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                        for(int i=0;i<groups.Length;i++)groups[i]=renderer.RebuildSurfacesForTest(scene.Objects[i],groups[i]);
                        if(iteration>=3){times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);allocation.Add(GC.GetAllocatedBytesForCurrentThread()-bytes);}
                    }
                }
                runs.Add(new{Style=LookCatalog.Names[style],Groups=oldGroups.Length,Triangles=oldGroups.Sum(g=>g!.Triangles.Count),ExactMeshes=true,LegacyMedianMs=oldTimes.Order().ElementAt(10),OptimizedMedianMs=newTimes.Order().ElementAt(10),LegacyMedianAllocatedBytes=oldAllocation.Order().ElementAt(10),OptimizedMedianAllocatedBytes=newAllocation.Order().ElementAt(10)});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Scope=geometryCacheOnly?"Alternating original mapping and bounded bit-key unrotated geometry cache; identical full-detail meshes/layer edits. CPU mesh only, not total frame.":descriptorsOnly?"Alternating original formatted sheet descriptors and cached immutable keys/coordinates; identical SIMD projection and mesh storage. CPU mesh only, not total frame.":"Alternating scalar bitmap-coordinate queries and cached SIMD projection, both retaining triangle/vertex storage; CPU mesh only, not total frame.",Frame=framePath,Profile=profilePath,VectorLanes=System.Numerics.Vector<float>.Count,HardwareAccelerated=System.Numerics.Vector.IsHardwareAccelerated,Runs=runs},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    internal static int RunOrdering(string output,string framePath,string profilePath,string cartridgeId)
    {
        Directory.CreateDirectory(output);
        try
        {
            var frame=CartridgeViewport.NormalizeCapture(cartridgeId,JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(framePath))!);
            var profile=GameRecognitionProfileStore.ReadFromFile(profilePath);
            if(frame.CaptureScanline!=96||frame.Sequence!=frame.NativeScreenSequence||!profile.IsActive(frame))throw new InvalidDataException("Coherent active gameplay required.");
            using var scene=new SmbProfile().Build(frame,false,null,profile);
            using var renderer=new WarpRendererControl{UseGpu=false,EnableStyleTransitions=false};
            List<object> runs=[];
            foreach(int style in new[]{0,5,6})
            {
                var settings=LookCatalog.Create(style,new());settings.Effects.Enabled=false;settings.Dimensions.AdaptiveQuality=false;
                renderer.ApplySettings(settings);renderer.MotionSecondsForTest=22/60d;
                PresentationAnimator.Apply(renderer,settings,22/60d);renderer.ProjectionCycleSeconds=renderer.GeometryCycleSeconds=22/60d;
                var groups=scene.Objects.Select(renderer.GeometrySurfacesForTest).ToArray();
                List<double> legacy=[],candidate=[];List<long> oldAllocation=[],newAllocation=[];
                for(int iteration=0;iteration<23;iteration++)
                {
                    SurfaceTriangle[][] expected=[],actual=[];
                    if(iteration%2==0){Legacy();Candidate();}else{Candidate();Legacy();}
                    for(int group=0;group<groups.Length;group++)
                    {
                        if(expected[group].Length!=actual[group].Length)throw new InvalidDataException("Ordering count mismatch.");
                        for(int index=0;index<expected[group].Length;index++)if(!ReferenceEquals(expected[group][index],actual[group][index]))throw new InvalidDataException($"Order mismatch style {style}, group {group}, triangle {index}.");
                    }
                    void Legacy()
                    {
                        foreach(var group in groups)group.InvalidateOrder();
                        long allocated=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                        expected=groups.Select(g=>g.Ordered(false).ToArray()).ToArray();
                        if(iteration>=3){legacy.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);oldAllocation.Add(GC.GetAllocatedBytesForCurrentThread()-allocated);}
                    }
                    void Candidate()
                    {
                        foreach(var group in groups)group.InvalidateOrder();
                        long allocated=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                        actual=groups.Select(g=>g.Ordered().ToArray()).ToArray();
                        if(iteration>=3){candidate.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);newAllocation.Add(GC.GetAllocatedBytesForCurrentThread()-allocated);}
                    }
                }
                runs.Add(new{Style=LookCatalog.Names[style],Groups=groups.Length,Triangles=groups.Sum(g=>g.Triangles.Count),ExactOrdering=true,LegacyMedianMs=legacy.Order().ElementAt(10),CandidateMedianMs=candidate.Order().ElementAt(10),LegacyMedianAllocatedBytes=oldAllocation.Order().ElementAt(10),CandidateMedianAllocatedBytes=newAllocation.Order().ElementAt(10)});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Scope="Isolated CPU ordering, alternating original two-pass partition and single-pass partition with identical radix sorting and snapshot copies; not total frame timing.",Frame=framePath,Profile=profilePath,Runs=runs},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    internal static int RunFrame(string output,string framePath,string profilePath,string cartridgeId,string? baseline,bool streaming=false)
    {
        Directory.CreateDirectory(output);
        try
        {
            if(!BuiltInGameProfiles.Cartridges.Any(c=>c.Id==cartridgeId))throw new InvalidDataException("Known capture identity required.");
            var frame=CartridgeViewport.NormalizeCapture(cartridgeId,JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(framePath))!);
            var profile=GameRecognitionProfileStore.ReadFromFile(profilePath);
            if(frame.CaptureScanline!=96||frame.Sequence!=frame.NativeScreenSequence||!profile.IsActive(frame))
                throw new InvalidDataException("Coherent active gameplay capture required; menus are not performance evidence.");
            using var scene=new SmbProfile().Build(frame,false,null,profile);
            int projected=scene.Objects.Count(o=>o.ProjectionEnabled&&o.SortOrder<20);
            if(projected<50)throw new InvalidDataException("Dense scenery capture required.");
            using var renderer=new WarpRendererControl{Size=new(1000,780),UseGpu=true,EnableStyleTransitions=false,UseStreamingVertexBufferForTest=streaming};
            renderer.CreateControl();renderer.SetScene(scene.Clone());
            using Bitmap image=new(renderer.Width,renderer.Height);
            List<object> runs=[];
            foreach(int style in new[]{0,5,6})
            {
                var settings=LookCatalog.Create(style,new());
                settings.Dimensions.AdaptiveQuality=false;settings.Effects.Enabled=false;
                renderer.ApplySettings(settings);
                for(int i=0;i<3;i++)Draw(i);
                long uploads=renderer.TextureUploads;
                List<double> draw=[],submission=[],readback=[],other=[],assembly=[],ordering=[],packing=[];List<int> calls=[],triangles=[];
                for(int i=0;i<20;i++)
                {
                    Draw(i+3);draw.Add(renderer.LastDrawMilliseconds);submission.Add(renderer.GpuSubmissionMs);
                    readback.Add(renderer.GpuReadbackMs);calls.Add(renderer.GpuDrawCalls);
                    assembly.Add(renderer.SceneAssemblyMilliseconds);triangles.Add(renderer.SurfaceTriangleCount);
                    ordering.Add(renderer.GpuOrderingMs);packing.Add(renderer.GpuPackingMs);
                    other.Add(Math.Max(0,renderer.LastDrawMilliseconds-renderer.GpuSubmissionMs-renderer.GpuReadbackMs));
                }
                if(!renderer.RendererStatus.StartsWith("GPU"))throw new InvalidOperationException("GPU required.");
                if(streaming&&!renderer.UsedStreamingVertexBufferForTest)throw new InvalidOperationException("Streaming vertex buffer required.");
                string screenshot=Path.Combine(output,"style-"+style+".png");image.Save(screenshot);
                File.WriteAllText(Path.Combine(output,"style-"+style+"-metrics.json"),JsonSerializer.Serialize(new{MedianDrawMs=draw.Order().ElementAt(10),MedianSubmissionMs=submission.Order().ElementAt(10),MedianReadbackMs=readback.Order().ElementAt(10),MedianOtherMs=other.Order().ElementAt(10),DrawCalls=calls.Max()},new JsonSerializerOptions{WriteIndented=true}));
                int? different=null;
                if(baseline is not null)
                {
                    using Bitmap reference=new(Path.Combine(baseline,"style-"+style+".png"));
                    different=PixelDifferences(reference,image);
                    if(different!=0)throw new InvalidDataException($"Style {style}: {different} pixels differ from the preserved baseline.");
                }
                runs.Add(new {Style=LookCatalog.Names[style],StyleIndex=style,RenderScale=settings.RenderScale,
                    MedianDrawMs=draw.Order().ElementAt(10),P95DrawMs=draw.Order().ElementAt(18),
                    MedianSubmissionMs=submission.Order().ElementAt(10),MedianReadbackMs=readback.Order().ElementAt(10),
                    MedianOtherMs=other.Order().ElementAt(10),MedianSceneAssemblyMs=assembly.Order().ElementAt(10),MedianOrderingMs=ordering.Order().ElementAt(10),MedianPackingMs=packing.Order().ElementAt(10),SurfaceTriangles=triangles.Max(),DrawCalls=calls.Max(),SteadyTextureUploads=renderer.TextureUploads-uploads,
                    DifferentPixels=different});
                void Draw(int i){renderer.MotionSecondsForTest=i/60d;PresentationAnimator.Apply(renderer,settings,i/60d);renderer.ProjectionCycleSeconds=i/60d;renderer.GeometryCycleSeconds=i/60d;renderer.DrawToBitmap(image,renderer.ClientRectangle);}
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Renderer=renderer.RendererStatus,
                Frame=framePath,FrameSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(framePath))),
                Profile=profilePath,ProfileSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(profilePath))),
                frame.Sequence,Objects=scene.Objects.Count,ProjectedScenery=projected,StreamingVertexBuffer=streaming,AdaptiveQuality=false,Effects=false,Runs=runs},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        static int PixelDifferences(Bitmap a,Bitmap b)
        {
            if(a.Size!=b.Size)throw new InvalidDataException("Baseline image size changed.");
            var rect=new Rectangle(Point.Empty,a.Size);var ad=a.LockBits(rect,System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bd=b.LockBits(rect,System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int[] ar=new int[a.Width],br=new int[b.Width];int changed=0;
                for(int y=0;y<a.Height;y++){System.Runtime.InteropServices.Marshal.Copy(ad.Scan0+y*ad.Stride,ar,0,ar.Length);System.Runtime.InteropServices.Marshal.Copy(bd.Scan0+y*bd.Stride,br,0,br.Length);for(int x=0;x<a.Width;x++)if(ar[x]!=br[x])changed++;}
                return changed;
            }
            finally{a.UnlockBits(ad);b.UnlockBits(bd);}
        }
    }
    internal static int RunLive(string output,string rom,bool verifySceneReuse=false)
    {
        Directory.CreateDirectory(output);string? previous=Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        try
        {
            using MainForm main=new(){SimulateInputFocusForTest=true};main.Show();DirectGpuStageTests.ShowOwnedWindowForTest(main);Pump(300);
            if(main.RomLoadedForTest||main.FriendlyPagesForTest!=0)throw new InvalidOperationException("Original interface, no ROM autoload.");
            main.LoadRomForTest(rom);Pump(1600);main.Activate();main.Focus();
            Message down=Message.Create(main.Handle,0x100,(IntPtr)Keys.Enter,IntPtr.Zero);
            Message up=Message.Create(main.Handle,0x101,(IntPtr)Keys.Enter,IntPtr.Zero);
            main.PreFilterMessage(ref down);
            if((main.InputMaskForTest&(int)NesButton.Start)==0)throw new InvalidOperationException("Synthetic Start input reaches core.");
            Pump(100);main.PreFilterMessage(ref up);Pump(3000);
            string? cartridgeId = BuiltInGameProfiles.Identify(File.ReadAllBytes(rom))?.Id;
            if (cartridgeId == "smb2")
            {
                // SMB2 uses A, not Start, to leave character selection.
                // Isolated defaults bind A to X; test actual gameplay, not a menu.
                down=Message.Create(main.Handle,0x100,(IntPtr)Keys.X,IntPtr.Zero);
                up=Message.Create(main.Handle,0x101,(IntPtr)Keys.X,IntPtr.Zero);
                main.PreFilterMessage(ref down);
                if((main.InputMaskForTest&(int)NesButton.A)==0)throw new InvalidOperationException("Synthetic character-select input reaches core.");
                Pump(150);main.PreFilterMessage(ref up);Pump(10000);
            }
            else if (cartridgeId == "smb3")
            {
                Press(Keys.Enter, NesButton.Start, 350, 2000);
                Press(Keys.Enter, NesButton.Start, 350, 2000);
                Press(Keys.Right, NesButton.Right, 650, 750);
                Press(Keys.Up, NesButton.Up, 650, 750);
                Press(Keys.X, NesButton.A, 350, 1000);
                var ready=Stopwatch.StartNew();
                while (ready.ElapsedMilliseconds<6000 &&
                    (main.LatestFrameForTest?.Ram[1832] != 3 || main.ProjectedSceneryCountForTest == 0)) Pump(100);
                var gameplay = main.LatestFrameForTest;
                if (gameplay is null || gameplay.Ram[1832] != 3 || gameplay.Ram[1802] != 1 || gameplay.Ram[1831] != 0 ||
                    gameplay.CaptureScanline != 96 || gameplay.NativeScreenSequence != gameplay.Sequence ||
                    main.ProjectedSceneryCountForTest == 0)
                    throw new InvalidOperationException("SMB3 native level state, coherent video and projected scenery required; menus do not count.");
            }
            else if (cartridgeId == "icarus")
            {
                Press(Keys.Enter, NesButton.Start, 350, 2000);
                var gameplay = main.LatestFrameForTest;
                if (gameplay is null || gameplay.Ram[0xa0] != 2 || gameplay.Ram[0x130] != 0 || gameplay.Ram[0x3b] != 0 ||
                    gameplay.CaptureScanline != 96 || gameplay.NativeScreenSequence != gameplay.Sequence ||
                    main.ProjectedSceneryCountForTest == 0)
                    throw new InvalidOperationException("Kid Icarus native field state, coherent video and projected scenery required; menus do not count.");
            }
            else if (cartridgeId == "celeste")
            {
                // Warning -> language notice -> main menu -> ordinary New Game.
                // Do not confuse the playable Archive hub with the main course.
                Press(Keys.Enter,NesButton.Start,350,1000);
                Press(Keys.Enter,NesButton.Start,350,3500);
                var gameplay=main.LatestFrameForTest;
                if(gameplay is null||gameplay.Ram[14]!=8||gameplay.Ram[0x740]!=1||
                    gameplay.CaptureScanline!=96||gameplay.NativeScreenSequence!=gameplay.Sequence||main.ProjectedSceneryCountForTest==0)
                    throw new InvalidOperationException("Celeste native main-course state, coherent video and projected scenery required; menus/hub do not count.");
            }
            // Hidden automated windows need an explicit paint to initialize WGL;
            // an unpainted renderer reports fallback before a context exists.
            main.SaveScreenshotForTest(Path.Combine(output,"warmup.png"));
            if(!main.HasSceneForTest||!main.RendererForTest.StartsWith("GPU"))
            {
                using Bitmap failed=new(main.Width,main.Height);main.DrawToBitmap(failed,main.ClientRectangle);failed.Save(Path.Combine(output,"failed-live.png"));
                throw new InvalidOperationException($"Live native GPU scene required: objects={main.HasSceneForTest}, sequence={main.PublishedSequenceForTest}, renderer={main.RendererForTest}, metrics={main.PerformanceForTest}");
            }
            long before=main.PublishedSequenceForTest;long presentedBefore=main.GpuPresentedFramesForTest;main.CycleForTest(true);Pump(800);
            if(main.PublishedSequenceForTest<=before)throw new InvalidOperationException("Native frames must keep advancing during cycling.");
            bool requireMovement = cartridgeId is "smb3" or "icarus" or "celeste";
            int? playerXBefore=requireMovement ? PlayerX() : null;
            down=Message.Create(main.Handle,0x100,(IntPtr)Keys.Right,IntPtr.Zero);main.PreFilterMessage(ref down);
            if((main.InputMaskForTest&(int)NesButton.Right)==0)throw new InvalidOperationException("Gameplay input reaches core.");
            Pump(requireMovement ? 600 : 200);up=Message.Create(main.Handle,0x101,(IntPtr)Keys.Right,IntPtr.Zero);main.PreFilterMessage(ref up);
            int? playerXAfter=requireMovement ? PlayerX() : null;
            if (cartridgeId=="smb3" && (main.LatestFrameForTest?.Ram[1832]!=3 || playerXAfter<=playerXBefore))
                throw new InvalidOperationException("SMB3 player must actually move right in native gameplay.");
            if (cartridgeId=="icarus" && (main.LatestFrameForTest?.Ram[0xa0]!=2 || main.LatestFrameForTest?.Ram[0x3b]!=0 || playerXAfter<=playerXBefore))
                throw new InvalidOperationException("Kid Icarus player must actually move right in native gameplay.");
            if(cartridgeId=="celeste" && (main.LatestFrameForTest?.Ram[14]!=8||main.LatestFrameForTest?.Ram[0x740]!=1||playerXAfter<=playerXBefore))
                throw new InvalidOperationException("Celeste player must actually move right in the native main course.");
            if(!main.DirectStageShownForTest||main.GpuPresentedFramesForTest<=presentedBefore)throw new InvalidOperationException("Live gameplay must advance on the native GPU stage.");
            object playbackPaint=main.RenderingBreakdownForTest;
            object? pausedReuse=null;
            if(verifySceneReuse)
            {
                var coherent=main.LatestFrameForTest;
                if(coherent?.CaptureScanline!=96||coherent.NativeScreenSequence!=coherent.Sequence)
                    throw new InvalidOperationException("Live reuse diagnostic requires coherent native frames.");
                main.TogglePauseForTest();Pump(250);
                var countsBefore=main.SceneBuildCountsForTest;
                long pausedSequence=main.PublishedSequenceForTest,pausedGpu=main.GpuPresentedFramesForTest;
                Pump(400);
                var countsAfter=main.SceneBuildCountsForTest;
                if(countsAfter.Completed!=countsBefore.Completed||countsAfter.Skipped<=countsBefore.Skipped||main.PublishedSequenceForTest!=pausedSequence)
                    throw new InvalidOperationException("Paused coherent frame must skip redundant builds.");
                if(main.GpuPresentedFramesForTest<=pausedGpu)throw new InvalidOperationException("GPU cycling must continue without scene rebuilds.");
                pausedReuse=new{Requests=countsAfter.Requests-countsBefore.Requests,Completed=countsAfter.Completed-countsBefore.Completed,Skipped=countsAfter.Skipped-countsBefore.Skipped,GpuPresentations=main.GpuPresentedFramesForTest-pausedGpu,Milliseconds=400};
                main.TogglePauseForTest();Pump(500);
                if(main.PublishedSequenceForTest<=pausedSequence)throw new InvalidOperationException("Native scene updates must resume after paused reuse.");
            }
            main.SaveScreenshotForTest(Path.Combine(output,"gameplay.png"));
            using(Bitmap image=new(main.Width,main.Height)){main.DrawToBitmap(image,main.ClientRectangle);image.Save(Path.Combine(output,"restored-interface.png"));}
            var buildCounts=main.SceneBuildCountsForTest;
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{CartridgeId=cartridgeId,OriginalInterface=true,NoRomAutoload=true,GpuNativeGameplay=true,NativeGpuPresentation=true,FrameQueueAdvances=true,MovementInput=true,PlayerXBefore=playerXBefore,PlayerXAfter=playerXAfter,SimulatedInputFocus=true,ProjectedScenery=main.ProjectedSceneryCountForTest,Renderer=main.RendererForTest,Performance=main.PerformanceForTest,SceneBuildRequests=buildCounts.Requests,SceneBuildCompleted=buildCounts.Completed,SceneBuildSkipped=buildCounts.Skipped,PausedSceneReuse=pausedReuse,PlaybackPaint=playbackPaint,LastPaint=main.RenderingBreakdownForTest},new JsonSerializerOptions{WriteIndented=true}));
            main.Close();return 0;
            void Press(Keys key, NesButton button, int held, int settle)
            {
                Message pressed=Message.Create(main.Handle,0x100,(IntPtr)key,IntPtr.Zero);
                main.PreFilterMessage(ref pressed);
                if ((main.InputMaskForTest & (int)button) == 0) throw new InvalidOperationException("Synthetic navigation input reaches core.");
                Pump(held);
                Message released=Message.Create(main.Handle,0x101,(IntPtr)key,IntPtr.Zero);
                main.PreFilterMessage(ref released);Pump(settle);
            }
            int PlayerX()
            {
                var frame=main.LatestFrameForTest ?? throw new InvalidOperationException("Native player frame required.");
                if (cartridgeId == "icarus") return frame.Ram[0x723];
                if (cartridgeId == "celeste") return frame.Ram[0x7a];
                return (frame.Ram[117]<<8)|frame.Ram[144];
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{Environment.SetEnvironmentVariable("WARP4D_HOME",previous);}
        static void Pump(int ms){Stopwatch clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(2);}}
    }
    internal static int Run(string output, string rom)
    {
        Directory.CreateDirectory(output);
        string? previous = Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME", Path.Combine(Path.GetFullPath(output), "isolated-data"));
        try
        {
            using NesEmulator emulator = new();
            emulator.Load(rom); Thread.Sleep(800); emulator.SetButton(NesButton.Start, true); Thread.Sleep(100);
            emulator.SetButton(NesButton.Start, false); Thread.Sleep(2900); emulator.TogglePause(); Thread.Sleep(60);
            using SmbScene scene = new SmbProfile().Build(emulator.CaptureFrame()!, emulator.IsSmbWorld);
            using WarpRendererControl renderer = new() { Size = new(1000, 780), UseGpu = true };
            renderer.CreateControl(); renderer.SetScene(scene.Clone());
            using Bitmap image = new(renderer.Width, renderer.Height);
            List<object> runs = [];
            foreach (int style in new[] { 0, 5, 6 })
            {
                PresentationSettings settings = LookCatalog.Create(style, new());
                settings.Dimensions.AdaptiveQuality = false; settings.Effects.Enabled = false;
                renderer.ApplySettings(settings);
                for (int i = 0; i < 3; i++) Draw(i);
                long uploads = renderer.TextureUploads;
                List<double> samples = [];
                for (int i = 0; i < 30; i++) { long start = Stopwatch.GetTimestamp(); Draw(i + 3); samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
                if (!renderer.RendererStatus.StartsWith("GPU")) throw new InvalidOperationException(renderer.RendererStatus);
                image.Save(Path.Combine(output, "style-" + style + ".png"));
                runs.Add(new { Style = LookCatalog.Names[style], AverageMs = samples.Average(), MedianMs = samples.Order().ElementAt(15), P95Ms = samples.Order().ElementAt(28), SteadyTextureUploads = renderer.TextureUploads - uploads, renderer.GpuSubmissionMs, renderer.GpuReadbackMs, renderer.GpuDrawCalls });
                void Draw(int i) { PresentationAnimator.Apply(renderer, settings, i / 60d); renderer.ProjectionCycleSeconds = i / 60d; renderer.GeometryCycleSeconds = i / 60d; renderer.DrawToBitmap(image, renderer.ClientRectangle); }
            }
            using(MainForm main=new())
            {
                main.Show();Application.DoEvents();
                if(main.FriendlyPagesForTest!=0||main.ControlsVisibleForTest)throw new InvalidOperationException("Original interface must be restored.");
                Task.Run(()=>
                {
                    for(int i=1;i<=80;i++)main.PublishSceneForTest(TestScene(i));
                }).GetAwaiter().GetResult();
                Application.DoEvents();
                if(main.PublishedSequenceForTest!=80||main.SceneDeliveryCountForTest!=1)throw new InvalidOperationException("Only the newest completed scene should be delivered after a busy UI frame.");
                main.PublishSceneForTest(TestScene(79));Application.DoEvents();
                if(main.PublishedSequenceForTest!=80)throw new InvalidOperationException("Older frames must not replace the current scene.");
                main.Close();
                SmbScene TestScene(long sequence)=>new(){Background=new Bitmap(scene.Background),Objects=[],Sequence=sequence,Location="Queue test",ExactProfile=false,RecognitionProfileName="Test",ProjectionProfileName="Test"};
            }
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { Renderer = renderer.RendererStatus, Objects = scene.Objects.Count, Runs = runs, OriginalInterfaceRestored=true, LatestFrameQueueBounded=true, OldFramesRejected=true }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch(Exception e) { File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString()); return 1; }
        finally { Environment.SetEnvironmentVariable("WARP4D_HOME", previous); }
    }
}
