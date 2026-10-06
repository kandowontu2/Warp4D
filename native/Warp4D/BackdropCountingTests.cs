using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Profiles;

namespace Warp4D;

internal static class BackdropCountingTests
{
    internal static unsafe int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            int cases=0;long pixels=0;
            Random random=new(20261008);
            foreach(Size size in new[]{new Size(1,1),new Size(7,32),new Size(1,33),new Size(19,47),new Size(256,240)})
            foreach(int palette in new[]{1,2,3,16,64,257,61440})
            foreach(bool ties in new[]{false,true})
            {
                using Bitmap bitmap=new(size.Width,size.Height,PixelFormat.Format32bppArgb);
                Fill(bitmap,palette,ties);
                int a=SmbProfile.FindDominantRgb(bitmap,false),b=SmbProfile.FindDominantRgb(bitmap,true),c=SmbProfile.FindDominantRgb(bitmap,runs:true);
                if(a!=b||a!=c)throw new InvalidDataException($"Backdrop mismatch {size}/{palette}/{ties}: {a:X6}/{b:X6}/{c:X6}");
                if(size.Height<=32&&b!=0)throw new InvalidDataException("Empty scan must yield0.");
                if(ties&&palette==61440&&size.Width==256&&b!=8192)throw new InvalidDataException("First-occurrence tie changed.");
                cases++;pixels+=(long)size.Width*Math.Max(0,size.Height-32);
            }
            int runCases=0;
            foreach(Size size in new[]{new Size(1,33),new Size(7,47),new Size(256,240)})
            foreach(int length in new[]{1,2,3,8,256,513,61440})
            {
                using Bitmap bitmap=new(size.Width,size.Height,PixelFormat.Format32bppArgb);
                BitmapData data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                try
                {
                    for(int y=0;y<bitmap.Height;y++)
                    {
                        int* row=(int*)((byte*)data.Scan0+y*data.Stride);
                        for(int x=0;x<bitmap.Width;x++)row[x]=(random.Next(256)<<24)|((y*bitmap.Width+x)/length%3);
                    }
                }
                finally{bitmap.UnlockBits(data);}
                if(SmbProfile.FindDominantRgb(bitmap)!=SmbProfile.FindDominantRgb(bitmap,runs:true))
                    throw new InvalidDataException($"Run/row/alpha counting mismatch {size}/{length}");
                runCases++;pixels+=(long)size.Width*Math.Max(0,size.Height-32);
            }
            using Bitmap benchmark=new(256,240,PixelFormat.Format32bppArgb);Fill(benchmark,64,false);
            List<double> original=[],direct=[];
            for(int iteration=0;iteration<23;iteration++)
            {
                if(iteration%2==0){Measure(false,original);Measure(true,direct);}
                else{Measure(true,direct);Measure(false,original);}
                void Measure(bool candidate,List<double> list)
                {
                    long start=Stopwatch.GetTimestamp();int checksum=0;
                    for(int i=0;i<64;i++)checksum^=SmbProfile.FindDominantRgb(benchmark,candidate);
                    GC.KeepAlive(checksum);
                    if(iteration>=3)list.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases,RunCases=runCases,RunCounterMatchesOriginal=true,ComparedScanPixels=pixels,OriginalMedianMs=original.Order().ElementAt(10),DirectMedianMs=direct.Order().ElementAt(10),OriginalBatches=original,DirectBatches=direct,Scope="Exact backdrop RGB including alpha-ignored, empty HUD-only scans, one-pixel/odd/full sizes, random palette counts1..61440 and deterministic first-occurrence ties;21additional short/long runs crossing rows with changing alpha. Alternating64image scans compare the previously rejected direct counter, not run timings or live FPS."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
            void Fill(Bitmap bitmap,int palette,bool ties)
            {
                BitmapData data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                try
                {
                    for(int y=0;y<bitmap.Height;y++)
                    {
                        int* row=(int*)((byte*)data.Scan0+y*data.Stride);
                        for(int x=0;x<bitmap.Width;x++)row[x]=(random.Next(256)<<24)|((ties?(y*bitmap.Width+x)%palette:random.Next(palette))&0xffffff);
                    }
                }
                finally{bitmap.UnlockBits(data);}
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
