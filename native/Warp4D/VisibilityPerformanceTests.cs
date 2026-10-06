using System.Diagnostics;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class VisibilityPerformanceTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            using var document=JsonDocument.Parse(File.ReadAllText(manifest));
            string[] paths=document.RootElement.EnumerateArray().Select(s=>s.GetProperty("Frame").GetString()!).Distinct().ToArray();
            var benchmarkPaths=document.RootElement.EnumerateArray().Where(s=>!s.TryGetProperty("Negative",out var negative)||!negative.GetBoolean()).Select(s=>s.GetProperty("Frame").GetString()!).ToHashSet();
            int checks=0,positive=0,negative=0;
            NesFrame? benchmark=null;
            string? benchmarkPath=null;
            foreach(string path in paths)
            {
                var frame=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!;
                if(benchmark is null && frame.NativeScreenPixels?.Length==61440 && benchmarkPaths.Contains(path)){benchmark=frame;benchmarkPath=path;}
                foreach(int size in new[]{8,16})foreach(bool masked in new[]{false,true})
                foreach(var point in new[]{new Point(-1,-1),new Point(0,0),new Point(1,3),new Point(4,7),new Point(15,14),new Point(23,19),new Point(30,27),new Point(31,29)})
                    Check(frame,frame.ScrollX/8+point.X,frame.ScrollY/8+point.Y,size,masked);
            }
            if(benchmark is null)throw new InvalidDataException("Native-video fixture required.");
            Random random=new(72491);
            int[][] tables=Enumerable.Range(0,4).Select(_=>Enumerable.Range(0,61440).Select(_=>random.Next(4)).ToArray()).ToArray();
            var synthetic=benchmark with {NametablePixels=tables,ScrollX=0,ScrollY=0,PpuMask=0x08};
            const int targetX=80,targetY=80;
            foreach(int size in new[]{8,16})
            foreach(Point shift in new[]{Point.Empty,new Point(-8,0),new Point(8,0),new Point(0,-8),new Point(0,8),new Point(-1,0),new Point(1,0),new Point(0,-1),new Point(0,1)})
            {
                // Recolored, independently alpha-valued tile: same topology,
                // deliberately located at every boundary of the camera search.
                int[] native=Enumerable.Repeat(unchecked((int)0xff101010),61440).ToArray();
                for(int dy=0;dy<size;dy++)for(int dx=0;dx<size;dx++)
                    native[(targetY+shift.Y+dy)*256+targetX+shift.X+dx]=unchecked((int)0x73000000)|(0x202020+tables[0][(targetY+dy)*256+targetX+dx]*0x151515);
                var shifted=synthetic with {NativeScreenPixels=native};
                Check(shifted,targetX/8,targetY/8,size,false,true);
                native[(targetY+shift.Y)*256+targetX+shift.X]^=0x010101;
                Check(shifted,targetX/8,targetY/8,size,false);
            }
            // Actual left-eight-pixel clipping must retain the opt-in fallback.
            int[] clipped=(int[])tables[0].Clone();
            for(int dy=0;dy<16;dy++)for(int dx=0;dx<8;dx++)clipped[(32+dy)*256+dx]=0;
            var maskFrame=synthetic with {NativeScreenPixels=clipped};
            Check(maskFrame,0,4,16,true,true);
            Check(maskFrame,0,4,16,false);
            Check(maskFrame with {PpuMask=0x0a},0,4,16,true);
            Check(maskFrame with {NativeScreenPixels=null},0,4,16,true,false);
            List<double> oldTimes=[],newTimes=[];List<long> oldBytes=[],newBytes=[];
            const int calls=2000;
            for(int repeat=0;repeat<13;repeat++)
            {
                if(repeat%2==0){Measure(false,oldTimes,oldBytes);Measure(true,newTimes,newBytes);}
                else{Measure(true,newTimes,newBytes);Measure(false,oldTimes,oldBytes);}
                void Measure(bool candidate,List<double> times,List<long> allocations)
                {
                    long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                    int matches=0;
                    for(int i=0;i<calls;i++)
                    {
                        int x=benchmark.ScrollX/8+i%32,y=benchmark.ScrollY/8+i/32%30;
                        if(candidate?MetatileVisualFingerprint.IsVisible(benchmark,x,y,16,true):Legacy(benchmark,x,y,16,true))matches++;
                    }
                    if(repeat>2){times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);allocations.Add(GC.GetAllocatedBytesForCurrentThread()-bytes);}
                    GC.KeepAlive(matches);
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{
                Passed=true,Frames=paths.Length,ExactVisibilityChecks=checks,Positive=positive,Negative=negative,
                CameraOffsetsAndPaletteSubstitution=true,MaskedEdgeOptIn=true,
                Scope="Independent previous hash-search algorithm vs direct canonical matching. Sampled real cells and explicit shifted/masked synthetic fixtures; not full-game coverage or FPS.",
                BenchmarkFrame=benchmarkPath,CallsPerBatch=calls,
                LegacyMedianMs=oldTimes.Order().ElementAt(5),CandidateMedianMs=newTimes.Order().ElementAt(5),
                LegacyMedianAllocatedBytes=oldBytes.Order().ElementAt(5),CandidateMedianAllocatedBytes=newBytes.Order().ElementAt(5)
            },new JsonSerializerOptions{WriteIndented=true}));return 0;
            void Check(NesFrame frame,int x,int y,int size,bool mask,bool? expected=null)
            {
                bool old=Legacy(frame,x,y,size,mask),current=MetatileVisualFingerprint.IsVisible(frame,x,y,size,mask);
                if(old!=current || expected is bool value && current!=value)throw new InvalidDataException($"Visibility mismatch #{checks}: ({x},{y}), size{size}, masked={mask}, legacy={old}, current={current}, expected={expected}.");
                checks++;if(current)positive++;else negative++;
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static bool Legacy(NesFrame frame,int worldX,int worldY,int size,bool masked)
    {
        int x=worldX*8-frame.ScrollX,y=worldY*8-frame.ScrollY;
        string artwork=MetatileVisualFingerprint.Read(frame,worldX,worldY,size);
        if(MetatileVisualFingerprint.ReadScreen(frame,x,y,size)==artwork)return true;
        if(masked&&size==16&&MetatileVisualFingerprint.HasVerifiedMaskedLeftHalf(frame,worldX,worldY))return true;
        for(int delta=1;delta<=8;delta++)
            if(MetatileVisualFingerprint.ReadScreen(frame,x-delta,y,size)==artwork||MetatileVisualFingerprint.ReadScreen(frame,x+delta,y,size)==artwork||MetatileVisualFingerprint.ReadScreen(frame,x,y-delta,size)==artwork||MetatileVisualFingerprint.ReadScreen(frame,x,y+delta,size)==artwork)return true;
        return false;
    }
}
