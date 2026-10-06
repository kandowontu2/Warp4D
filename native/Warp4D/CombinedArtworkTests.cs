using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Profiles;

namespace Warp4D;

internal static class CombinedArtworkTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            Random random=new(20261010);int cases=0;long pixels=0;
            using Bitmap seed=new(256,240,PixelFormat.Format32bppArgb);
            for(int y=0;y<240;y++)for(int x=0;x<256;x++)
                seed.SetPixel(x,y,Color.FromArgb((random.Next(256)<<24)|(random.Next(3)==0?0x123456:random.Next(0x1000000))));
            foreach(Rectangle bounds in new[]{new Rectangle(0,0,256,240),new Rectangle(0,0,8,8),new Rectangle(247,231,9,9),new Rectangle(3,7,29,31),new Rectangle(101,94,67,71)})
            foreach(int scrollX in new[]{0,3,255})foreach(int scrollY in new[]{0,7,239})
            foreach(bool transparent in new[]{false,true})foreach(bool sparse in new[]{false,true})
            {
                using Bitmap old=(Bitmap)seed.Clone(),combined=(Bitmap)seed.Clone();
                var tiles=Tiles(bounds,scrollX,scrollY,sparse);
                using Bitmap a=Reference(old,bounds,tiles,scrollX,scrollY,transparent,out bool oldVisible);
                using Bitmap b=SmbProfile.ExtractObjectArtwork(combined,bounds,tiles,scrollX,scrollY,0x123456,transparent,out bool visible);
                if(oldVisible!=visible)throw new InvalidDataException("Visible state differs.");
                Compare(a,b);Compare(old,combined);cases++;
            }
            // Later overlapping objects must see previous erasures identically.
            using(Bitmap old=(Bitmap)seed.Clone())using(Bitmap combined=(Bitmap)seed.Clone())
            {
                for(int i=0;i<128;i++)
                {
                    Rectangle bounds=new(i%15*8,i%13*8,24,24);
                    var tiles=Tiles(bounds,3,7,i%2==0);
                    using Bitmap a=Reference(old,bounds,tiles,3,7,i%3!=0,out bool oldVisible);
                    using Bitmap b=SmbProfile.ExtractObjectArtwork(combined,bounds,tiles,3,7,0x123456,i%3!=0,out bool visible);
                    if(oldVisible!=visible)throw new InvalidDataException("Overlapping visibility differs.");
                    Compare(a,b);cases++;
                }
                Compare(old,combined);
            }
            using(Bitmap empty=new(16,16,PixelFormat.Format32bppArgb))
            {
                var tiles=Tiles(new(0,0,16,16),0,0,false);
                using Bitmap a=Reference(empty,new(0,0,16,16),tiles,0,0,true,out bool visible);
                using Bitmap blank=new(16,16,PixelFormat.Format32bppArgb);
                using Bitmap b=SmbProfile.ExtractObjectArtwork(blank,new(0,0,16,16),tiles,0,0,0x123456,true,out bool candidateVisible);
                if(visible||candidateVisible)throw new InvalidDataException("Blank artwork became visible.");
                Compare(a,b);cases++;
            }
            var batch=Enumerable.Range(0,128).Select(i=>new Rectangle(i%16*16,i/16*16,16,16)).Select(b=>(Bounds:b,Tiles:Tiles(b,3,7,true))).ToArray();
            List<double> oldTimes=[],newTimes=[];
            for(int iteration=0;iteration<23;iteration++)
            {
                if(iteration%2==0){Measure(false,oldTimes);Measure(true,newTimes);}
                else{Measure(true,newTimes);Measure(false,oldTimes);}
                void Measure(bool candidate,List<double> times)
                {
                    long start=Stopwatch.GetTimestamp();
                    for(int scene=0;scene<16;scene++)
                    {
                        using Bitmap background=(Bitmap)seed.Clone();
                        foreach(var item in batch)
                        {
                            using Bitmap crop=candidate?SmbProfile.ExtractObjectArtwork(background,item.Bounds,item.Tiles,3,7,0x123456,true,out _):Reference(background,item.Bounds,item.Tiles,3,7,true,out _);
                        }
                    }
                    if(iteration>=3)times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases,ExactArgbPixels=pixels,OriginalMedianMs=oldTimes.Order().ElementAt(10),CombinedMedianMs=newTimes.Order().ElementAt(10),OriginalBatches=oldTimes,CombinedBatches=newTimes,Scope="Unique disjoint tile masks, sparse/clipped/odd/full bounds, scroll offsets, random alpha/RGB/backdrop, blank and sequential overlapping objects. Original crop+fused transparency+conditional erase versus combined pass; exact crop/background ARGB and visibility. Alternating16scene x128object batches,3warmup/20measured. Not live FPS."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
            void Compare(Bitmap a,Bitmap b)
            {
                for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)
                    if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())throw new InvalidDataException($"ARGB differs at{x},{y} size{a.Size}");
                pixels+=(long)a.Width*a.Height;
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static Bitmap Reference(Bitmap background,Rectangle bounds,List<(int X,int Y)> tiles,int scrollX,int scrollY,bool transparent,out bool visible)
    {
        Bitmap crop=SmbProfile.CropWithTileMask(background,bounds,tiles,scrollX,scrollY);
        visible=transparent?SmbProfile.MakeBackdropTransparent(crop,0x123456,true):SmbProfile.HasVisiblePixel(crop);
        if(!transparent||visible)SmbProfile.EraseObjectFromBackground(background,crop,bounds,0x123456);
        return crop;
    }
    private static List<(int X,int Y)> Tiles(Rectangle bounds,int scrollX,int scrollY,bool sparse)
    {
        List<(int X,int Y)> tiles=[];
        for(int y=(bounds.Top+scrollY)/8;y<=(bounds.Bottom+scrollY)/8;y++)
        for(int x=(bounds.Left+scrollX)/8;x<=(bounds.Right+scrollX)/8;x++)
            if(!sparse||(x+y)%3!=0)tiles.Add((x,y));
        return tiles;
    }
}
