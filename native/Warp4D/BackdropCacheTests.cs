using System.Text.Json;
using Warp4D.Profiles;

namespace Warp4D;

internal static class BackdropCacheTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            if(!new SmbProfile().UseBackdropCacheForTest)throw new InvalidDataException("Production backdrop cache must be enabled.");
            BackdropColorCache cache=new();int checks=0;
            Random random=new(20261011);
            foreach(var size in new[]{new Size(1,1),new Size(7,32),new Size(1,33),new Size(7,47),new Size(256,240),new Size(257,240),new Size(256,241),new Size(256,240)})
            {
                using Bitmap image=new(size.Width,size.Height);
                for(int y=0;y<size.Height;y++)for(int x=0;x<size.Width;x++)image.SetPixel(x,y,Color.FromArgb(random.Next(256),random.Next(8),random.Next(8),random.Next(8)));
                Check(image);Check(image);
                using Bitmap clone=(Bitmap)image.Clone();Check(clone);
                // Same bitmap reference, different content must not return stale
                // analysis. This can completely replace the winning RGB color.
                using(var graphics=Graphics.FromImage(image))graphics.Clear(Color.Magenta);
                Check(image);Check(image);
                if(size.Height>32)
                {
                    long hits=cache.Hits,misses=cache.Misses;
                    image.SetPixel(0,0,Color.Blue);Check(image);
                    if(size.Width<=256&&size.Height<=240&&cache.Hits!=hits+1)throw new InvalidDataException("Excluded HUD rows must not invalidate.");
                    image.SetPixel(size.Width-1,size.Height-1,Color.Cyan);Check(image);
                    if(size.Width<=256&&size.Height<=240&&cache.Misses!=misses+1)throw new InvalidDataException("Last scanned pixel must invalidate.");
                    image.SetPixel(0,32,Color.FromArgb(1,255,0,255));Check(image);Check(image);
                }
                if(cache.StoredPixels>256*(240-32))throw new InvalidDataException("Snapshot exceeded bounded NES storage.");
            }
            // A tied dominant result must retain row-major first-occurrence order.
            using(Bitmap ties=new(2,34))
            {
                ties.SetPixel(0,32,Color.Red);ties.SetPixel(1,32,Color.Blue);
                ties.SetPixel(0,33,Color.Blue);ties.SetPixel(1,33,Color.Red);
                Check(ties);Check(ties);
                if(cache.Find(ties)!=(Color.Red.ToArgb()&0xffffff))throw new InvalidDataException("Dominant tie order changed.");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,ProductionEnabled=true,Checks=checks,cache.Hits,cache.Misses,MaximumStoredPixels=256*(240-32),ExactReferenceRgb=true,ReferenceIdentityNotTrusted=true,ExcludedHudRows=true,FinalPixelInvalidates=true,AlphaChangeSafe=true,ResizesAndUnsupportedSizes=true,FirstOccurrenceTies=true},new JsonSerializerOptions{WriteIndented=true}));return 0;
            void Check(Bitmap image){if(cache.Find(image)!=SmbProfile.FindDominantRgb(image))throw new InvalidDataException("Cached dominant RGB differs from fresh reference.");checks++;}
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
