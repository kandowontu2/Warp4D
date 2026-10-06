using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class FingerprintPerformanceTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            using var document=JsonDocument.Parse(File.ReadAllText(manifest));
            var paths=document.RootElement.EnumerateArray().Select(s=>s.GetProperty("Frame").GetString()!).Distinct().ToArray();
            int comparisons=0;
            NesFrame? benchmark=null;
            foreach(string path in paths)
            {
                var frame=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!;
                benchmark??=frame;
                Check(frame);
            }
            // Imported captures may contain more colors than native NES video.
            // Exercise all256 IDs, alpha stripping, wrap and absent video too.
            foreach(int colors in new[]{1,4,16,64,256})
            {
                int[][] tables=Enumerable.Range(0,4).Select(t=>Enumerable.Range(0,61440).Select(i=>unchecked((int)((uint)(i%256)<<24))|((i/256*16+i%256+t*31)%colors)).ToArray()).ToArray();
                var synthetic=benchmark! with { NametablePixels=tables,NativeScreenPixels=tables[0],ScrollX=511,ScrollY=479 };
                Check(synthetic);Check(synthetic with {NativeScreenPixels=null});
            }
            List<double> oldTimes=[],newTimes=[];
            List<long> oldBytes=[],newBytes=[];
            const int iterations=2000;
            for(int repeat=0;repeat<13;repeat++)
            {
                if(repeat%2==0){Measure(false,oldTimes,oldBytes);Measure(true,newTimes,newBytes);}
                else{Measure(true,newTimes,newBytes);Measure(false,oldTimes,oldBytes);}
                void Measure(bool candidate,List<double> times,List<long> allocation)
                {
                    long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                    for(int i=0;i<iterations;i++)
                    {
                        int x=i%32,y=i/32%30;
                        string value=candidate?MetatileVisualFingerprint.Read(benchmark!,x,y):Legacy(benchmark!,x,y,16,false,false);
                        if(value.Length!=64)throw new InvalidDataException("Invalid benchmark hash.");
                    }
                    if(repeat>2){times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);allocation.Add(GC.GetAllocatedBytesForCurrentThread()-bytes);}
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{
                Passed=true,Frames=paths.Length,ExactFingerprintComparisons=comparisons,
                Scope="Independent dictionary baseline vs stack color IDs: world/screen/right-half, 8/16px, wrapped/offscreen/missing video and 1..256 colors. Alternating isolated world-hash microbenchmark, not whole-frame/FPS.",
                BenchmarkFrame=paths[0],CallsPerBatch=iterations,
                LegacyMedianMs=oldTimes.Order().ElementAt(5),CandidateMedianMs=newTimes.Order().ElementAt(5),
                LegacyMedianAllocatedBytes=oldBytes.Order().ElementAt(5),CandidateMedianAllocatedBytes=newBytes.Order().ElementAt(5)
            },new JsonSerializerOptions{WriteIndented=true}));
            return 0;
            void Check(NesFrame frame)
            {
                foreach(int size in new[]{8,16})
                foreach(var p in new[]{new Point(-1,-1),new Point(0,0),new Point(1,3),new Point(15,14),new Point(31,29),new Point(32,30),new Point(63,59),new Point(64,60)})
                {
                    Equal(MetatileVisualFingerprint.Read(frame,p.X,p.Y,size),Legacy(frame,p.X,p.Y,size,false,false));
                    Equal(MetatileVisualFingerprint.ReadScreen(frame,p.X*8,p.Y*8,size),Legacy(frame,p.X*8,p.Y*8,size,true,false));
                    if(size==16)Equal(MetatileVisualFingerprint.ReadRightHalf(frame,p.X,p.Y),Legacy(frame,p.X,p.Y,size,false,true));
                }
            }
            void Equal(string candidate,string reference)
            {
                if(candidate!=reference)throw new InvalidDataException($"Fingerprint mismatch at comparison {comparisons}.");
                comparisons++;
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }

    // Independent copy of the pre-optimization dictionary canonicalization.
    private static string Legacy(NesFrame frame,int x,int y,int size,bool screen,bool right)
    {
        if(screen&&(frame.NativeScreenPixels?.Length!=61440||x<0||y<0||x+size>256||y+size>240))return "";
        Span<byte> canonical=stackalloc byte[256];
        Dictionary<int,byte> ids=[];
        int width=right?8:size;
        for(int dy=0;dy<size;dy++)for(int dx=0;dx<width;dx++)
        {
            int rgb;
            if(screen)rgb=frame.NativeScreenPixels![(y+dy)*256+x+dx]&0xffffff;
            else
            {
                int xx=((x*8+dx+(right?8:0))%512+512)%512,yy=((y*8+dy)%480+480)%480;
                rgb=frame.NametablePixels[(xx>=256?1:0)+(yy>=240?2:0)][(yy%240)*256+(xx%256)]&0xffffff;
            }
            if(!ids.TryGetValue(rgb,out byte id)){id=(byte)ids.Count;ids[rgb]=id;}
            canonical[dy*width+dx]=id;
        }
        return Convert.ToHexString(SHA256.HashData(canonical[..(size*width)]));
    }
}
