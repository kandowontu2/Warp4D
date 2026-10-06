using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Profiles;

namespace Warp4D;

internal static class BackdropVisibilityTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            Random random=new(20261009);int cases=0;long pixels=0;
            foreach(Size size in new[]{new Size(1,1),new Size(8,16),new Size(16,16),new Size(19,47),new Size(256,240)})
            foreach(int backdrop in new[]{0,0x123456,0xffffff})
            foreach(int mode in new[]{0,1,2,3})
            {
                using Bitmap seed=new(size.Width,size.Height,PixelFormat.Format32bppArgb);
                for(int y=0;y<size.Height;y++)for(int x=0;x<size.Width;x++)
                {
                    int rgb=mode==0?backdrop:mode==1?backdrop^1:mode==2?(x==size.Width-1&&y==size.Height-1?backdrop^1:backdrop):random.Next(0x1000000);
                    int alpha=mode==1?0:mode==3?random.Next(256):255;
                    seed.SetPixel(x,y,Color.FromArgb((alpha<<24)|rgb));
                }
                Check(seed,backdrop);
            }
            for(int alpha=0;alpha<256;alpha++)
            {
                using Bitmap seed=new(8,16,PixelFormat.Format32bppArgb);
                seed.SetPixel(7,15,Color.FromArgb(alpha,12,34,56));Check(seed,0);
            }
            List<object> benchmarks=[];
            foreach(bool late in new[]{false,true})
            {
                using Bitmap seed=new(16,16,PixelFormat.Format32bppArgb);
                seed.SetPixel(late?15:0,late?15:0,Color.Red);
                List<double> baseline=[],fused=[];
                for(int iteration=0;iteration<23;iteration++)
                {
                    if(iteration%2==0){Measure(false,baseline);Measure(true,fused);}
                    else{Measure(true,fused);Measure(false,baseline);}
                    void Measure(bool candidate,List<double> times)
                    {
                        long start=Stopwatch.GetTimestamp();
                        for(int i=0;i<2048;i++)
                        {
                            using Bitmap bitmap=(Bitmap)seed.Clone();
                            bool visible=SmbProfile.MakeBackdropTransparent(bitmap,0,candidate);
                            if(!candidate)visible=SmbProfile.HasVisiblePixel(bitmap);
                            if(!visible)throw new InvalidDataException("Benchmark artwork disappeared.");
                        }
                        if(iteration>=3)times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    }
                }
                benchmarks.Add(new{LateVisiblePixel=late,BaselineMedianMs=baseline.Order().ElementAt(10),FusedMedianMs=fused.Order().ElementAt(10),BaselineBatches=baseline,FusedBatches=fused});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases,ExactArgbPixels=pixels,Benchmarks=benchmarks,Scope="Exact output pixels and visibility bool; all-backdrop/alpha0/last-visible/random pixels, sizes1..256x240, every alpha byte. Alternating2048crop clones+transparency+visibility,3warmup/20measured,early/late visible pixel; not live FPS."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
            void Check(Bitmap seed,int backdrop)
            {
                using Bitmap a=(Bitmap)seed.Clone(),b=(Bitmap)seed.Clone();
                SmbProfile.MakeBackdropTransparent(a,backdrop,false);
                bool visible=SmbProfile.MakeBackdropTransparent(b,backdrop,true);
                if(visible!=SmbProfile.HasVisiblePixel(a))throw new InvalidDataException("Visibility differs.");
                for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)
                    if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())throw new InvalidDataException("Transparency pixels differ.");
                cases++;pixels+=(long)a.Width*a.Height;
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
