using System.Drawing.Imaging;

namespace Warp4D.Rendering;

// One immutable decoded image. Never trust a bitmap reference, frame number,
// camera position or hash as evidence that the next publication is unchanged.
internal sealed class BackgroundPixelCache
{
    internal const int MaximumPixels=256*240;
    private ImagePixels? _pixels;
    internal long Hits {get;private set;}
    internal long Misses {get;private set;}
    internal int StoredPixels=>_pixels?.Pixels.Length??0;
    internal void Clear()=>_pixels=null;
    internal ImagePixels Get(Bitmap image)
    {
        bool supported=(long)image.Width*image.Height<=MaximumPixels;
        if(supported && _pixels is {} previous && Matches(previous,image)){Hits++;return previous;}
        Misses++;
        var pixels=ImagePixels.Read(image);
        _pixels=supported?pixels:null;
        return pixels;
    }
    private static unsafe bool Matches(ImagePixels previous,Bitmap image)
    {
        if(previous.Width!=image.Width||previous.Height!=image.Height)return false;
        BitmapData data=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            for(int y=0;y<image.Height;y++)
            {
                ReadOnlySpan<int> row=new((byte*)data.Scan0+y*data.Stride,image.Width);
                ReadOnlySpan<int> expected=previous.Pixels.AsSpan(y*image.Width,image.Width);
                if(row.SequenceEqual(expected))continue;
                // Exactly ImagePixels.Read's alpha-zero normalization; otherwise
                // every ARGB bit must match, including partially transparent RGB.
                for(int x=0;x<row.Length;x++)
                {
                    int pixel=(row[x]&unchecked((int)0xff000000))==0?0:row[x];
                    if(pixel!=expected[x])return false;
                }
            }
            return true;
        }
        finally{image.UnlockBits(data);}
    }
}
