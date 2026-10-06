using System.Drawing.Imaging;

namespace Warp4D.Profiles;

// One bounded snapshot, owned by the serialized scene builder. This caches only
// the dominant-color analysis, never artwork, scene objects or native sprites.
internal sealed class BackdropColorCache
{
    private int[] _pixels=[];
    private int _width,_height,_rgb;
    private bool _valid;
    internal long Hits {get;private set;}
    internal long Misses {get;private set;}
    internal int StoredPixels=>_pixels.Length;

    internal unsafe int Find(Bitmap bitmap)
    {
        if(bitmap.Width>256||bitmap.Height>240)return SmbProfile.FindDominantRgb(bitmap);
        if(bitmap.Height<=32)return 0;
        int count=checked(bitmap.Width*(bitmap.Height-32));
        bool same=_valid&&_width==bitmap.Width&&_height==bitmap.Height;
        if(_pixels.Length!=count)_pixels=new int[count];
        var data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            if(same)
                for(int y=32;y<bitmap.Height;y++)
                    if(!new ReadOnlySpan<int>((byte*)data.Scan0+y*data.Stride,bitmap.Width).SequenceEqual(_pixels.AsSpan((y-32)*bitmap.Width,bitmap.Width)))
                    {same=false;break;}
            if(same){Hits++;return _rgb;}
            _valid=false;
            for(int y=32;y<bitmap.Height;y++)
                new ReadOnlySpan<int>((byte*)data.Scan0+y*data.Stride,bitmap.Width).CopyTo(_pixels.AsSpan((y-32)*bitmap.Width,bitmap.Width));
        }
        finally{bitmap.UnlockBits(data);}
        // A changed alpha also forces a harmless miss; equality is deliberately
        // stronger than RGB equality, so no lossy hashes or stale color guesses.
        int rgb=SmbProfile.FindDominantRgb(bitmap);
        _width=bitmap.Width;_height=bitmap.Height;_rgb=rgb;_valid=true;Misses++;
        return rgb;
    }
}
