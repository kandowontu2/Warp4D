using System.Drawing.Imaging;

namespace Warp4D.Rendering;

// Q is reciprocal homogeneous depth, including both the W and Z perspective divides.
internal readonly record struct SurfaceVertex(PointF Point, float Depth, float Q, float U = 0, float V = 0);
internal sealed record SurfaceTriangle(SurfaceVertex A, SurfaceVertex B, SurfaceVertex C, ImagePixels? Texture, Color Color, float Opacity, string LayerKey = "Center")
{
    public SurfaceVertex A { get; set; } = A;
    public SurfaceVertex B { get; set; } = B;
    public SurfaceVertex C { get; set; } = C;
    public ImagePixels? Texture { get => _texture; init => _texture=value; }
    // Only a group's rebuild can mutate the texture of an existing triangle.
    internal void SetTexture(ImagePixels? texture) => _texture = texture;
    private ImagePixels? _texture = Texture;
    public Color Color { get; set; } = Color;
    public float Opacity { get; set; } = Opacity;
    public string LayerKey { get; set; } = LayerKey;
    public float Depth => (A.Depth + B.Depth + C.Depth) / 3;
    public bool Opaque => Opacity >= 0.999f && Color.A == 255;
}
// Counted view: spare capacity is never part of drawing, packing or enumeration.
internal sealed class OrderedSurfaceTriangles(SurfaceTriangle[] storage, int count) : IReadOnlyList<SurfaceTriangle>
{
    public int Count => count;
    public int Length => count;
    public SurfaceTriangle this[int index] => (uint)index < (uint)count ? storage[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public IEnumerator<SurfaceTriangle> GetEnumerator() { for(int i=0;i<count;i++)yield return storage[i]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
internal sealed class SurfaceGroup
{
    private readonly List<SurfaceTriangle> _triangles=[];
    public IReadOnlyList<SurfaceTriangle> Triangles { get; }
    private readonly Dictionary<string,ImagePixels> _textureInventory=[];
    private ImagePixels? _lastTexture;
    public SurfaceGroup() => Triangles=_triangles.AsReadOnly();
    internal IEnumerable<ImagePixels> TextureInventory => _textureInventory.Values;
    private void IncludeTexture(ImagePixels? texture)
    {
        if(texture is not null && !ReferenceEquals(texture,_lastTexture))
            _textureInventory.TryAdd(texture.Key,texture);
        _lastTexture=texture;
    }
    private OrderedSurfaceTriangles? _ordered;
    internal bool UseReusableOrderStorageForTest {get;set;} = true;
    internal int OrderCapacityForTest => _orderStorage.Length;
    internal bool OrderTailClearedForTest => _orderStorage.Skip(Triangles.Count).All(t=>t is null) && _orderScratch.All(t=>t is null);
    private SurfaceTriangle[] _orderStorage=[];
    private ulong[] _sortKeys=[];
    private uint[] _depthKeys=[],_depthScratch=[];
    private SurfaceTriangle[] _orderScratch=[];
    private readonly Dictionary<string,SurfaceVertex[]> _vertexBuffers=[];
    internal long TrianglesAllocatedForTest {get;private set;}
    internal long VertexArraysAllocatedForTest {get;private set;}
    internal long VertexElementsAllocatedForTest {get;private set;}
    internal SurfaceVertex[] VertexBuffer(string key,int count)
    {
        if(!_vertexBuffers.TryGetValue(key,out var vertices)||vertices.Length!=count)
        {
            _vertexBuffers[key]=vertices=new SurfaceVertex[count];
            VertexArraysAllocatedForTest++;VertexElementsAllocatedForTest+=count;
        }
        return vertices;
    }
    private int _writeIndex;
    private bool _updating;
    // A renderer-owned mesh may reuse its triangle storage between completed paints.
    // Every field is overwritten; ordering and offsets must never survive a rebuild.
    public void BeginUpdate() { _updating=true;_writeIndex=0;_ordered=null;Offset=default;_textureInventory.Clear();_lastTexture=null; }
    public void EndUpdate()
    {
        if(_writeIndex<Triangles.Count)_triangles.RemoveRange(_writeIndex,Triangles.Count-_writeIndex);
        if(_orderStorage.Length>Triangles.Count)Array.Clear(_orderStorage,Triangles.Count,_orderStorage.Length-Triangles.Count);
        _updating=false;
    }
    public void InvalidateOrder() => _ordered=null;
    public PointF Offset { get; set; }
    public void Triangle(SurfaceVertex a,SurfaceVertex b,SurfaceVertex c,ImagePixels? texture,Color color,float opacity,string layerKey="Center")
    {
        _ordered=null;
        if(_updating && _writeIndex<Triangles.Count)
        {
            var triangle=Triangles[_writeIndex++];
            triangle.A=a;triangle.B=b;triangle.C=c;triangle.SetTexture(texture);
            triangle.Color=color;triangle.Opacity=opacity;triangle.LayerKey=layerKey;
        }
        else { _triangles.Add(new(a,b,c,texture,color,opacity,layerKey));TrianglesAllocatedForTest++;if(_updating)_writeIndex++; }
        IncludeTexture(texture);
    }
    public void Quad(SurfaceVertex a, SurfaceVertex b, SurfaceVertex c, SurfaceVertex d, ImagePixels? texture, Color color, float opacity, string layerKey = "Center")
    {
        if (opacity <= 0.001f) return;
        Triangle(a,b,c,texture,color,opacity,layerKey);
        Triangle(a,c,d,texture,color,opacity,layerKey);
    }
    public OrderedSurfaceTriangles Ordered(bool singlePass = true)
    {
        if(_ordered is not null)return _ordered;
        int count=Triangles.Count,opaque=0;
        if(UseReusableOrderStorageForTest)
        {
            if(_orderStorage.Length<count)_orderStorage=new SurfaceTriangle[Math.Max(count,Math.Max(16,_orderStorage.Length*2))];
        }
        else if(_orderStorage.Length!=count)_orderStorage=new SurfaceTriangle[count];
        int transparent;
        if(singlePass)
        {
            if(_orderScratch.Length<count)_orderScratch=new SurfaceTriangle[count];
            transparent=0;
            // Partition once, retaining source order in both buckets. The scratch
            // bucket is copied before it is reused by the transparent radix sort.
            foreach(var triangle in Triangles)
                if(triangle.Opaque)_orderStorage[opaque++]=triangle;
                else _orderScratch[transparent++]=triangle;
            Array.Copy(_orderScratch,0,_orderStorage,opaque,transparent);
        }
        else
        {
            // Original partition retained for paired diagnostic measurements.
            foreach(var triangle in Triangles)if(triangle.Opaque)_orderStorage[opaque++]=triangle;
            int write=opaque;
            foreach(var triangle in Triangles)if(!triangle.Opaque)_orderStorage[write++]=triangle;
            transparent=count-opaque;
        }
        if(transparent>64)SortRadix(opaque,count);
        else if(transparent>1)
        {
            if(_sortKeys.Length<count)_sortKeys=new ulong[count];
            for(int i=opaque;i<count;i++)
            {
                float depth=_orderStorage[i].Depth;
                uint bits=BitConverter.SingleToUInt32Bits(depth);
                // IEEE magnitude/sign bits give ascending float order after
                // flipping positives/inverting negatives. CompareTo puts every
                // NaN first and treats signed zero alike; canonicalize those.
                uint order=float.IsNaN(depth)?0:depth==0?0x80000000u:
                    (bits&0x80000000u)!=0?~bits:bits^0x80000000u;
                // The original ordinal makes every key unique. Even an unstable
                // primitive sort then preserves LINQ's equal-depth ordering.
                _sortKeys[i]=((ulong)order<<32)|(uint)i;
            }
            Array.Sort(_sortKeys,_orderStorage,opaque,transparent);
        }
        if(transparent<=64)Array.Clear(_orderScratch);
        return _ordered=new(_orderStorage,count);
    }
    private void SortRadix(int opaque,int count)
    {
        if(_depthKeys.Length<count)
        {
            _depthKeys=new uint[count];_depthScratch=new uint[count];_orderScratch=new SurfaceTriangle[count];
        }
        Span<int> histograms=stackalloc int[1024];histograms.Clear();
        for(int i=opaque;i<count;i++)
        {
            float depth=_orderStorage[i].Depth;
            uint bits=BitConverter.SingleToUInt32Bits(depth);
            uint key=float.IsNaN(depth)?0:depth==0?0x80000000u:(bits&0x80000000u)!=0?~bits:bits^0x80000000u;
            _depthKeys[i]=key;
            histograms[(int)(key&255)]++;
            histograms[256+(int)((key>>8)&255)]++;
            histograms[512+(int)((key>>16)&255)]++;
            histograms[768+(int)(key>>24)]++;
        }
        var source=_orderStorage;var destination=_orderScratch;
        var keys=_depthKeys;var nextKeys=_depthScratch;
        int length=count-opaque;
        for(int pass=0;pass<4;pass++)
        {
            Span<int> buckets=histograms.Slice(pass*256,256);
            if(buckets.IndexOf(length)>=0)continue; // One occupied byte bucket: already ordered.
            int offset=opaque;
            for(int bucket=0;bucket<256;bucket++)
            {
                int amount=buckets[bucket];buckets[bucket]=offset;offset+=amount;
            }
            int shift=pass*8;
            // Forward scatter makes every pass stable. Equal float depths retain
            // their input order without sorting a separate 32-bit ordinal.
            for(int i=opaque;i<count;i++)
            {
                uint key=keys[i];int target=buckets[(int)((key>>shift)&255)]++;
                destination[target]=source[i];nextKeys[target]=key;
            }
            (source,destination)=(destination,source);(keys,nextKeys)=(nextKeys,keys);
        }
        if(!ReferenceEquals(source,_orderStorage))Array.Copy(source,opaque,_orderStorage,opaque,length);
        Array.Clear(_orderScratch); // Do not retain old artwork through scratch references.
    }
}

internal sealed class SoftwareSurfaceRenderer : IDisposable
{
    private Bitmap? _bitmap;
    private int[] _pixels = [];
    private float[] _depth = [];
    public unsafe Bitmap Render(IReadOnlyList<SurfaceGroup> groups, RectangleF bounds)
    {
        int width = Math.Max(1, (int)Math.Ceiling(bounds.Width)), height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
        if (_bitmap?.Size != new Size(width, height))
        {
            _bitmap?.Dispose(); _bitmap = new(width, height, PixelFormat.Format32bppArgb);
            _pixels = new int[width * height]; _depth = new float[_pixels.Length];
        }
        Array.Fill(_pixels, unchecked((int)0xff000000));
        foreach (SurfaceGroup group in groups)
        {
            Array.Fill(_depth, float.NegativeInfinity);
            foreach (SurfaceTriangle triangle in group.Ordered()) Draw(triangle, bounds, width, height, group.Offset);
        }
        BitmapData data = _bitmap.LockBits(new Rectangle(Point.Empty, _bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            fixed (int* source = _pixels)
                for (int y = 0; y < height; y++)
                    Buffer.MemoryCopy(source + y * width, (byte*)data.Scan0 + y * data.Stride, width * 4, width * 4);
        }
        finally { _bitmap.UnlockBits(data); }
        return _bitmap;
    }
    private void Draw(SurfaceTriangle t, RectangleF bounds, int width, int height, PointF translation)
    {
        PointF a = new(t.A.Point.X + translation.X - bounds.X, t.A.Point.Y + translation.Y - bounds.Y),
            b = new(t.B.Point.X + translation.X - bounds.X, t.B.Point.Y + translation.Y - bounds.Y),
            c = new(t.C.Point.X + translation.X - bounds.X, t.C.Point.Y + translation.Y - bounds.Y);
        float determinant = Edge(a, b, c);
        if (Math.Abs(determinant) < 0.0001f) return;
        int left = Math.Clamp((int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, width - 1),
            right = Math.Clamp((int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, width - 1),
            top = Math.Clamp((int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, height - 1),
            bottom = Math.Clamp((int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, height - 1);
        for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
        {
            PointF p = new(x + 0.5f, y + 0.5f);
            float wa = Edge(b, c, p) / determinant, wb = Edge(c, a, p) / determinant, wc = 1 - wa - wb;
            if (wa < 0 || wb < 0 || wc < 0 ||
                (wa == 0 && !OwnsEdge(b, c, determinant)) || (wb == 0 && !OwnsEdge(c, a, determinant)) ||
                (wc == 0 && !OwnsEdge(a, b, determinant))) continue;
            int offset = y * width + x;
            float depth = wa * t.A.Depth + wb * t.B.Depth + wc * t.C.Depth;
            if (depth < _depth[offset] - 0.00001f) continue;
            int argb = t.Color.ToArgb();
            if (t.Texture is ImagePixels texture)
            {
                float q = wa * t.A.Q + wb * t.B.Q + wc * t.C.Q;
                if (q <= 0) continue;
                float u = (wa * t.A.U * t.A.Q + wb * t.B.U * t.B.Q + wc * t.C.U * t.C.Q) / q;
                float v = (wa * t.A.V * t.A.Q + wb * t.B.V * t.B.Q + wc * t.C.V * t.C.Q) / q;
                argb = texture.Pixels[Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1) * texture.Width +
                    Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1)];
                if(t.Color.ToArgb()!=unchecked((int)0xffffffff))
                {
                    int tintedAlpha=(int)((uint)argb>>24)*t.Color.A/255;
                    argb=(tintedAlpha<<24)|(((argb>>16)&255)*t.Color.R/255<<16)|(((argb>>8)&255)*t.Color.G/255<<8)|((argb&255)*t.Color.B/255);
                }
            }
            int alpha = (int)((uint)argb >> 24);
            float opacity = alpha / 255f * t.Opacity;
            if (opacity <= 0.001f) continue;
            if (t.Opaque && alpha == 255) { _pixels[offset] = argb; _depth[offset] = depth; }
            else
            {
                int existing = _pixels[offset];
                int r = (int)(((argb >> 16) & 255) * opacity + ((existing >> 16) & 255) * (1 - opacity));
                int g = (int)(((argb >> 8) & 255) * opacity + ((existing >> 8) & 255) * (1 - opacity));
                int bl = (int)((argb & 255) * opacity + (existing & 255) * (1 - opacity));
                _pixels[offset] = unchecked((int)0xff000000) | r << 16 | g << 8 | bl;
            }
        }
    }
    private static float Edge(PointF a, PointF b, PointF p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
    private static bool OwnsEdge(PointF a, PointF b, float orientation)
    {
        if (orientation < 0) (a, b) = (b, a);
        return b.Y < a.Y || (b.Y == a.Y && b.X > a.X);
    }
    public void Dispose() => _bitmap?.Dispose();
}
