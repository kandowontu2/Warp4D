using System.Drawing.Imaging;
using System.Security.Cryptography;
using Warp4D.Profiles;

namespace Warp4D.Rendering;

internal sealed record ImagePixels(string Key, int Width, int Height, int[] Pixels)
{
    public static unsafe ImagePixels Read(Bitmap image)
    {
        int[] pixels = new int[image.Width * image.Height];
        BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            fixed (int* destination = pixels)
                for (int y = 0; y < image.Height; y++)
                    Buffer.MemoryCopy((byte*)data.Scan0 + y * data.Stride, destination + y * image.Width, image.Width * 4, image.Width * 4);
        }
        finally { image.UnlockBits(data); }
        // GDI+ clones may discard RGB underneath alpha=0. Those invisible bytes
        // must not turn identical artwork into different cache/texture entries.
        for (int index = 0; index < pixels.Length; index++) if ((pixels[index] & unchecked((int)0xff000000)) == 0) pixels[index] = 0;
        string key = $"{image.Width}x{image.Height}:" + Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan())));
        return new(key, image.Width, image.Height, pixels);
    }
}

internal static class GeometryCache
{
    private const int Capacity = 512;
    private static readonly Dictionary<string, PixelGeometry> Cache = [];
    private static readonly Queue<string> Order = [];
    public static long Hits { get; private set; }
    public static long Misses { get; private set; }
    public static PixelGeometry Get(ImagePixels pixels, Bitmap image)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(pixels.Key, out PixelGeometry? geometry)) { Hits++; return geometry; }
            Misses++;
            geometry = PixelGeometry.FromBitmap(image);
            while (Cache.Count >= Capacity) Cache.Remove(Order.Dequeue());
            Cache[pixels.Key] = geometry; Order.Enqueue(pixels.Key); return geometry;
        }
    }
}
