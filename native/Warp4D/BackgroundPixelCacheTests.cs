using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class BackgroundPixelCacheTests
{
    internal static object Validate()
    {
        using WarpRendererControl defaults=new();
        BackgroundPixelCache cache=new();
        List<(ImagePixels Image,int[] Original)> retained=[];
        int checks=0;
        ImagePixels Check(Bitmap image)
        {
            var expected=ImagePixels.Read(image);
            var actual=cache.Get(image);
            Require(actual.Key==expected.Key&&actual.Width==expected.Width&&actual.Height==expected.Height&&actual.Pixels.SequenceEqual(expected.Pixels),"Every normalized ARGB and key match fresh decode");
            foreach(var old in retained)Require(old.Image.Pixels.SequenceEqual(old.Original),"Retained texture pixels remain immutable");
            retained.Add((actual,(int[])actual.Pixels.Clone()));
            Require(cache.StoredPixels<=BackgroundPixelCache.MaximumPixels,"Bounded single NES image");
            checks++;return actual;
        }
        foreach(var size in new[]{new Size(1,1),new Size(7,33),new Size(256,240),new Size(257,240),new Size(256,241),new Size(240,256)})
        {
            using Bitmap image=new(size.Width,size.Height);
            using(var g=Graphics.FromImage(image))g.Clear(Color.DarkBlue);
            var initial=Check(image);var repeat=Check(image);
            bool supported=(long)size.Width*size.Height<=BackgroundPixelCache.MaximumPixels;
            Require(ReferenceEquals(initial,repeat)==supported,"Only bounded exact content is reused");
            using Bitmap clone=(Bitmap)image.Clone();
            Require(ReferenceEquals(repeat,Check(clone))==supported,"Clone content, not bitmap identity");
            image.SetPixel(0,0,Color.Gold);
            Require(!ReferenceEquals(repeat,Check(image)),"First HUD pixel invalidates same bitmap");
            image.SetPixel(size.Width-1,size.Height-1,Color.Cyan);Check(image);
            image.SetPixel(0,0,Color.DarkBlue);Check(image);
            using(var g=Graphics.FromImage(image))g.Clear(Color.Magenta);
            Check(image);Check(image);
            using(var g=Graphics.FromImage(image))g.Clear(Color.DarkBlue);
            Check(image);Check(image);
            cache.Clear();Require(cache.StoredPixels==0,"Clear releases snapshot");
            Require(!ReferenceEquals(initial,Check(image)),"Reset must decode again");
        }
        using(Bitmap alpha=new(3,2))
        {
            Write(alpha,0,0,0x00112233);var before=Check(alpha);
            Write(alpha,0,0,0x00445566);
            Require(ReferenceEquals(before,Check(alpha)),"Hidden RGB alpha-zero normalization");
            Write(alpha,2,1,0x01778899);var visible=Check(alpha);
            Require(!ReferenceEquals(before,visible),"Alpha-one visible RGB invalidates");
            Write(alpha,2,1,0x01778898);
            Require(!ReferenceEquals(visible,Check(alpha)),"Partially transparent RGB cannot be ignored");
        }
        using(Bitmap rgb=new(7,5,PixelFormat.Format24bppRgb))
        {
            using(var g=Graphics.FromImage(rgb))g.Clear(Color.Orange);
            var first=Check(rgb);Require(ReferenceEquals(first,Check(rgb)),"24-bit converted rows reuse");
            rgb.SetPixel(6,4,Color.Black);Require(!ReferenceEquals(first,Check(rgb)),"Padded 24-bit last pixel invalidates");
        }
        IntPtr memory=Marshal.AllocHGlobal(8*4*4);
        try
        {
            using Bitmap inverted=new(8,4,-32,PixelFormat.Format32bppArgb,memory+96);
            using(var g=Graphics.FromImage(inverted))g.Clear(Color.Green);
            inverted.SetPixel(7,3,Color.Purple);var first=Check(inverted);
            Require(first.Pixels[31]==Color.Purple.ToArgb(),"Negative-stride bottom-right orientation");
            Require(ReferenceEquals(first,Check(inverted)),"Negative stride exact reuse");
            inverted.SetPixel(0,0,Color.Red);Require(!ReferenceEquals(first,Check(inverted)),"Negative stride first pixel invalidates");
        }
        finally{Marshal.FreeHGlobal(memory);}
        using Bitmap full=new(256,240);
        using(var g=Graphics.FromImage(full))g.Clear(Color.SkyBlue);
        cache.Get(full);
        long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<128;i++)cache.Get(full);
        long reused=GC.GetAllocatedBytesForCurrentThread()-start;
        start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<128;i++)ImagePixels.Read(full);
        long original=GC.GetAllocatedBytesForCurrentThread()-start;
        Require(reused<original/100,"Exact hits eliminate decoded pixel arrays and hashes");
        int publicationFrames=ValidatePublications();
        return new {Checks=checks,cache.Hits,cache.Misses,MaximumStoredPixels=BackgroundPixelCache.MaximumPixels,OriginalAllocatedBytes=original,ReusedAllocatedBytes=reused,AllocationIterations=128,ExactGpuPublicationFrames=publicationFrames,IncludesHud=true,OldTextureArraysImmutable=true,ProductionEnabled=defaults.UseBackgroundPixelCacheForTest};
    }

    private static int ValidatePublications()
    {
        bool motion=InterfaceMotion.Enabled;InterfaceMotion.Enabled=false;
        try
        {
            using var sample=LookGalleryForm.CreateSample();
            using WarpRendererControl reference=new(){Size=new(720,600),UseGpu=true,PresentationMode=true,UseBackgroundPixelCacheForTest=false};
            using WarpRendererControl candidate=new(){Size=new(720,600),UseGpu=true,PresentationMode=true,UseBackgroundPixelCacheForTest=true};
            reference.CreateControl();candidate.CreateControl();
            using Bitmap a=new(720,600),b=new(720,600);
            for(int i=0;i<12;i++)
            {
                using var next=sample.Clone();
                if(i is 2 or 3)next.Background.SetPixel(0,0,Color.Gold);
                if(i is 4 or 5)next.Background.SetPixel(next.Background.Width-1,next.Background.Height-1,Color.Cyan);
                if(i is 6 or 7)using(var g=Graphics.FromImage(next.Background))g.Clear(Color.DarkRed);
                if(i==10){reference.ResetRuntime();candidate.ResetRuntime();}
                reference.SetScene(next.Clone());candidate.SetScene(next.Clone());
                reference.DrawToBitmap(a,reference.ClientRectangle);candidate.DrawToBitmap(b,candidate.ClientRectangle);
                Require(ImagePixels.Read(a).Pixels.SequenceEqual(ImagePixels.Read(b).Pixels),$"GPU consecutive publication exact pixels, frame {i}");
                Require(reference.RendererStatus.StartsWith("GPU")&&candidate.RendererStatus.StartsWith("GPU"),"GPU publication path required");
                Require(reference.SurfaceTriangleCount==candidate.SurfaceTriangleCount&&reference.GpuDrawCalls==candidate.GpuDrawCalls,"No geometry or draw reduction");
            }
            var stats=candidate.BackgroundPixelCacheStatsForTest;
            Require(stats.Hits>=5&&stats.Misses>=5,"Consecutive identical publications hit; changes and reset miss");
            return 12;
        }
        finally{InterfaceMotion.Enabled=motion;}
    }

    private static unsafe void Write(Bitmap image,int x,int y,uint argb)
    {
        var data=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try{((uint*)((byte*)data.Scan0+y*data.Stride))[x]=argb;}
        finally{image.UnlockBits(data);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
}
