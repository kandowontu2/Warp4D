using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;

namespace Warp4D;

// Isolated candidate only: production scenery extraction remains unchanged.
internal static class BackgroundLockTests
{
    internal static int Run(string output,bool sharedOnly=false)
    {
        Directory.CreateDirectory(output);
        try
        {
            using Bitmap seed = new(256,240,PixelFormat.Format32bppArgb);
            Random random = new(20261007);
            for(int y=0;y<240;y++)for(int x=0;x<256;x++)
                seed.SetPixel(x,y,Color.FromArgb(random.Next(4)==0?0:255,random.Next(256),random.Next(256),random.Next(256)));
            int cases=0;long pixels=0;
            foreach(var bounds in new[]{new Rectangle(0,0,256,240),new Rectangle(0,0,8,8),new Rectangle(248,232,8,8),new Rectangle(3,7,19,25),new Rectangle(101,93,71,67)})
            foreach(int scrollX in new[]{0,3,255})foreach(int scrollY in new[]{0,7,239})
            {
                using Bitmap original=(Bitmap)seed.Clone(),regional=(Bitmap)seed.Clone();
                using Bitmap a=Process(original,bounds,scrollX,scrollY,false);
                using Bitmap b=Process(regional,bounds,scrollX,scrollY,true);
                Compare(a,b);Compare(original,regional);cases++;
            }
            if(sharedOnly)
            {
                using Bitmap original=(Bitmap)seed.Clone(),shared=(Bitmap)seed.Clone();
                BitmapData locked=shared.LockBits(new Rectangle(Point.Empty,shared.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
                try
                {
                    // Sequential overlap matters: later crops must see earlier erasure.
                    for(int i=0;i<96;i++)
                    {
                        Rectangle bounds=new(i%30*8,i%28*8,16,16);
                        using Bitmap a=Process(original,bounds,3,7,false);
                        using Bitmap b=Process(shared,bounds,3,7,false,locked);
                        Compare(a,b);cases++;
                    }
                }
                finally{shared.UnlockBits(locked);}
                Compare(original,shared);
            }
            List<double> full=[],region=[];
            for(int iteration=0;iteration<23;iteration++)
            {
                if(iteration%2==0){Measure(false,full);Measure(true,region);}
                else{Measure(true,region);Measure(false,full);}
                void Measure(bool regional,List<double> times)
                {
                    using Bitmap background=(Bitmap)seed.Clone();
                    long start=Stopwatch.GetTimestamp();
                    BitmapData? shared=sharedOnly&&regional?background.LockBits(new Rectangle(Point.Empty,background.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb):null;
                    try
                    {
                        for(int i=0;i<2048;i++)using(Process(background,new Rectangle(i%30*8,i%28*8,16,16),3,7,sharedOnly?false:regional,shared)){}
                    }
                    finally{if(shared is not null)background.UnlockBits(shared);}
                    if(iteration>=3)times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases,ExactArgbPixels=pixels,FullLockMedianMs=full.Order().ElementAt(10),CandidateMedianMs=region.Order().ElementAt(10),Candidate=sharedOnly?"One shared background lock":"Regional background locks",FullLockBatches=full,CandidateBatches=region,Scope="Isolated equivalent crop/masked erase on deterministic random pixels: clipped/sparse tile masks, transparent pixels, edge/full/odd bounds and scroll offsets; shared variant additionally checks sequential overlapping erasure. Alternating 2048-object batches, 3 warmup/20 measured. Not live scenes/FPS; production unchanged."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
            void Compare(Bitmap a,Bitmap b)
            {
                for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)
                    if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())throw new InvalidDataException($"Different ARGB at {x},{y}");
                pixels+=a.Width*a.Height;
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }

    private static unsafe Bitmap Process(Bitmap background,Rectangle bounds,int scrollX,int scrollY,bool regional,BitmapData? shared=null)
    {
        Bitmap crop=new(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb);
        using(Graphics clear=Graphics.FromImage(crop))clear.Clear(Color.Transparent);
        Rectangle locked=regional?bounds:new Rectangle(Point.Empty,background.Size);
        BitmapData source=shared??background.LockBits(locked,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        BitmapData target=crop.LockBits(new Rectangle(Point.Empty,crop.Size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try
        {
            for(int tileY=(bounds.Top+scrollY)/8;tileY<=(bounds.Bottom+scrollY)/8;tileY++)
            for(int tileX=(bounds.Left+scrollX)/8;tileX<=(bounds.Right+scrollX)/8;tileX++)
            {
                if((tileX+tileY)%3==0)continue;
                Rectangle cell=Rectangle.Intersect(new Rectangle(tileX*8-scrollX,tileY*8-scrollY,8,8),bounds);
                if(cell.Width<=0||cell.Height<=0)continue;
                for(int row=0;row<cell.Height;row++)
                {
                    int* from=(int*)((byte*)source.Scan0+(cell.Y-locked.Y+row)*source.Stride)+cell.X-locked.X;
                    int* to=(int*)((byte*)target.Scan0+(cell.Y-bounds.Y+row)*target.Stride)+cell.X-bounds.X;
                    Buffer.MemoryCopy(from,to,cell.Width*sizeof(int),cell.Width*sizeof(int));
                }
            }
        }
        finally{crop.UnlockBits(target);if(shared is null)background.UnlockBits(source);}
        BitmapData destination=shared??background.LockBits(locked,ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        BitmapData mask=crop.LockBits(new Rectangle(Point.Empty,crop.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            for(int y=0;y<crop.Height;y++)
            {
                int* from=(int*)((byte*)mask.Scan0+y*mask.Stride);
                int* to=(int*)((byte*)destination.Scan0+(bounds.Y-locked.Y+y)*destination.Stride)+bounds.X-locked.X;
                for(int x=0;x<crop.Width;x++)if((from[x]&unchecked((int)0xff000000))!=0)to[x]=unchecked((int)0xff123456);
            }
        }
        finally{crop.UnlockBits(mask);if(shared is null)background.UnlockBits(destination);}
        return crop;
    }
}
