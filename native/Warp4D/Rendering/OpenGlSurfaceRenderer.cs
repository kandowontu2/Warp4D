using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Buffers;

namespace Warp4D.Rendering;

// A private Windows OpenGL context keeps GPU drawing independent of WinForms painting.
// Playback stays GPU-local. Bitmap readback remains for screenshots, recording
// and editor overlays; textures and unchanged sprite geometry stay cached.
internal sealed class OpenGlSurfaceRenderer : IDisposable
{
    private readonly NativeWindow _window = new();
    private IntPtr _dc, _context;
    private int _pixelFormat;
    private uint _presentationTexture;
    private Size _presentationSize;
    private const int AtlasSize=2048;
    private readonly Dictionary<string, AtlasRegion> _textures = [];
    private uint _atlas;
    private int _atlasX, _atlasY, _atlasRowHeight;
    private static readonly ImagePixels White = new("gpu:white",1,1,[unchecked((int)0xffffffff)]);
    private readonly record struct AtlasRegion(int X,int Y,int Width,int Height);
    private Bitmap? _bitmap;
    private int[] _readback = [];
    public string DeviceName { get; private set; } = "Unavailable";
    public long TextureUploads { get; private set; }
    public double LastSubmissionMs {get;private set;}
    public double LastReadbackMs {get;private set;}
    public double LastOrderingMs {get;private set;}
    public double LastPackingMs {get;private set;}
    public double LastAtlasMs {get;private set;}
    public double LastDriverMs {get;private set;}
    public double LastPresentMs {get;private set;}
    public long ReadbackFrames {get;private set;}
    public long PresentedFrames {get;private set;}
    internal bool UseDirectVertexWritesForTest {get;set;}=true;
    internal bool UseCachedTextureInventoryForTest {get;set;}=true;
    internal bool UseStreamingVertexBufferForTest {get;set;}=false;
    internal bool LastUsedStreamingVertexBuffer {get;private set;}
    private uint _vertexBuffer;
    private bool _bufferFunctionsLoaded;
    private GenBuffersDelegate? _genBuffers;
    private BindBufferDelegate? _bindBuffer;
    private BufferDataDelegate? _bufferData;
    private DeleteBuffersDelegate? _deleteBuffers;
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GenBuffersDelegate(int count,out uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void BindBufferDelegate(uint target,uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void BufferDataDelegate(uint target,IntPtr bytes,IntPtr data,uint usage);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DeleteBuffersDelegate(int count,ref uint buffer);
    private readonly record struct BufferDraw(int Group,bool Opaque,int First,int Count);
    public bool CanPresentDirectly => Available;
    internal string PixelFormatForTest {get;private set;}="";
    internal object? PresentedDrawableForTest {get;private set;}
    public int LastDrawCalls {get;private set;}
    public bool Available { get; private set; }
    public OpenGlSurfaceRenderer()
    {
        try
        {
            _window.CreateHandle(new CreateParams { Caption = "Warp4D GPU surface", X = -10000, Y = -10000,
                Width = 2048, Height = 2048, Style = unchecked((int)0x80000000) });
            _dc = GetDC(_window.Handle);
            PixelFormatDescriptor format = new() { Size = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(), Version = 1,
                Flags = 0x25, ColorBits = 32, AlphaBits = 8, DepthBits = 24 };
            int index = ChoosePixelFormat(_dc, ref format);
            if (index == 0 || !SetPixelFormat(_dc, index, ref format)) return;
            _pixelFormat=index;
            _context = wglCreateContext(_dc);
            if (_context == IntPtr.Zero || !wglMakeCurrent(_dc, _context)) return;
            DeviceName = Marshal.PtrToStringAnsi(glGetString(0x1F01)) ?? "Unknown OpenGL device";
            Available = !DeviceName.Contains("GDI Generic", StringComparison.OrdinalIgnoreCase);
            glGetIntegerv(0x0D56,out int depthBits);glGetIntegerv(0x0D52,out int redBits);glGetIntegerv(0x0D55,out int alphaBits);
            PixelFormatForTest=$"Depth={depthBits},Red={redBits},Alpha={alphaBits}";
        }
        catch (Exception exception) when (exception is ExternalException or DllNotFoundException or EntryPointNotFoundException)
        { DeviceName = "Software fallback: " + exception.Message; }
        finally { wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); }
    }

    public Bitmap Render(IReadOnlyList<SurfaceGroup> groups, RectangleF bounds, int renderScale)
        =>RenderCore(groups,bounds,renderScale,null,false)!;

    internal Bitmap RenderPresentationTextureForTest(IReadOnlyList<SurfaceGroup> groups,RectangleF bounds,int renderScale)
        =>RenderCore(groups,bounds,renderScale,null,true)!;

    internal Bitmap? Present(IReadOnlyList<SurfaceGroup> groups,RectangleF bounds,int renderScale,GpuStageWindow target,bool capture=false)
    {
        if (!CanPresentDirectly) throw new InvalidOperationException("Direct GPU presentation is unavailable.");
        if (!target.PixelFormatSet)
        {
            PixelFormatDescriptor format=new(){Size=(ushort)Marshal.SizeOf<PixelFormatDescriptor>(),Version=1,Flags=0x25,ColorBits=32,AlphaBits=8,DepthBits=24};
            if (!SetPixelFormat(target.DeviceContext,_pixelFormat,ref format)) throw new InvalidOperationException("Could not configure the GPU stage pixel format.");
            target.PixelFormatSet=true;
        }
        return RenderCore(groups,bounds,renderScale,target,true,capture);
    }

    private unsafe Bitmap? RenderCore(IReadOnlyList<SurfaceGroup> groups, RectangleF bounds, int renderScale,GpuStageWindow? target,bool copyTexture,bool capture=false)
    {
        int width = Math.Min((int)Math.Ceiling(bounds.Width), 256 * renderScale);
        int height = Math.Max(1, (int)Math.Round(width * bounds.Height / bounds.Width));
        if (width > 2048 || height > 2048 || width < 1 || height < 1) throw new InvalidOperationException("Stage exceeds GPU surface limits.");
        if (!wglMakeCurrent(_dc, _context)) throw new InvalidOperationException("Could not activate GPU renderer.");
        try
        {
            long renderStart=System.Diagnostics.Stopwatch.GetTimestamp();LastDrawCalls=0;LastOrderingMs=LastPackingMs=LastReadbackMs=LastPresentMs=LastAtlasMs=LastDriverMs=0;
            if ((target is null || capture) && _bitmap?.Size != new Size(width, height))
            { _bitmap?.Dispose(); _bitmap = new(width, height, PixelFormat.Format32bppArgb); _readback = new int[width * height]; }
            long atlasStart=System.Diagnostics.Stopwatch.GetTimestamp();
            EnsureAtlas(groups);
            LastAtlasMs=System.Diagnostics.Stopwatch.GetElapsedTime(atlasStart).TotalMilliseconds;
            glViewport(0, 0, width, height);
            // glClear ignores the viewport. Restrict it to the stage instead of
            // clearing the hidden 2048×2048 drawable on every small NES frame.
            glEnable(0x0C11);glScissor(0,0,width,height);
            glMatrixMode(0x1701); glLoadIdentity(); glOrtho(0, bounds.Width, bounds.Height, 0, -1, 1);
            glMatrixMode(0x1700); glLoadIdentity();
            glClearColor(0, 0, 0, 1); glDepthMask(1); glClear(0x4000 | 0x0100);
            glEnable(0x0BE2); glBlendFunc(0x0302, 0x0303);
            glEnable(0x0BC0); glAlphaFunc(0x0204, 0.001f);
            glEnable(0x0B71); glDepthFunc(0x0203);
            glEnableClientState(0x8074); glEnableClientState(0x8076); glEnableClientState(0x8078);
            glEnable(0x0DE1);glBindTexture(0x0DE1,_atlas);
            LastUsedStreamingVertexBuffer=UseStreamingVertexBufferForTest&&EnsureVertexBuffer();
            if(LastUsedStreamingVertexBuffer)DrawStreaming(groups,bounds);
            else for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                SurfaceGroup group = groups[groupIndex];
                // Disjoint depth ranges preserve NES layer priority while allowing
                // one depth-buffer clear for the entire frame, not one per sprite.
                glDepthRange((groups.Count - groupIndex - 1d) / groups.Count, (groups.Count - groupIndex) / (double)groups.Count);
                long orderingStart=System.Diagnostics.Stopwatch.GetTimestamp();
                OrderedSurfaceTriangles triangles = group.Ordered();
                LastOrderingMs+=System.Diagnostics.Stopwatch.GetElapsedTime(orderingStart).TotalMilliseconds;
                GpuVertex[] vertices = ArrayPool<GpuVertex>.Shared.Rent(Math.Max(1, triangles.Length * 3));
                try
                {
                    int index = 0;
                    ImagePixels? lastPixels=null;
                    AtlasRegion region=_textures[White.Key];
                    while (index < triangles.Length)
                    {
                        SurfaceTriangle first = triangles[index];
                        glDepthMask(first.Opaque ? (byte)1 : (byte)0);
                        int count = 0;
                        long packingStart=System.Diagnostics.Stopwatch.GetTimestamp();
                        count=PackRun(triangles,ref index,vertices,bounds,group.Offset,first.Opaque,ref lastPixels,ref region,UseDirectVertexWritesForTest);
                        LastPackingMs+=System.Diagnostics.Stopwatch.GetElapsedTime(packingStart).TotalMilliseconds;
                        long driverStart=System.Diagnostics.Stopwatch.GetTimestamp();
                        fixed (GpuVertex* vertexData = vertices)
                        {
                            glVertexPointer(3, 0x1406, sizeof(GpuVertex), (IntPtr)vertexData);
                            glTexCoordPointer(4, 0x1406, sizeof(GpuVertex), (IntPtr)((byte*)vertexData + 12));
                            glColorPointer(4, 0x1406, sizeof(GpuVertex), (IntPtr)((byte*)vertexData + 28));
                            glDrawArrays(0x0004, 0, count);
                            LastDrawCalls++;
                        }
                        LastDriverMs+=System.Diagnostics.Stopwatch.GetElapsedTime(driverStart).TotalMilliseconds;
                    }
                }
                finally { ArrayPool<GpuVertex>.Shared.Return(vertices); }
            }
            glDepthRange(0, 1);
            glDisableClientState(0x8074); glDisableClientState(0x8076); glDisableClientState(0x8078);
            glReadBuffer(0x0405);
            LastSubmissionMs=System.Diagnostics.Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
            long presentStart=System.Diagnostics.Stopwatch.GetTimestamp();
            if(copyTexture) CopyPresentationTexture(width,height);
            if (target is not null)
            {
                target.ShowStage(true); // window-system pixel ownership rejects blits to hidden drawables
                if(!wglMakeCurrent(target.DeviceContext,_context))throw new InvalidOperationException("Could not activate native GPU stage.");
                DrawPresentationTexture(target.StageSize);
                if (glGetError()!=0 || !SwapBuffers(target.DeviceContext)) throw new InvalidOperationException("Could not present the GPU stage.");
                LastPresentMs=System.Diagnostics.Stopwatch.GetElapsedTime(presentStart).TotalMilliseconds;
                PresentedFrames++;
                if(!capture)return null;
                // Capture the retained source texture, not the window-system
                // front buffer. Keep the same native stage visible throughout.
                if(!wglMakeCurrent(_dc,_context))throw new InvalidOperationException("Could not activate recording source.");
                glBindTexture(0x0DE1,_presentationTexture);
            }
            long readStart=System.Diagnostics.Stopwatch.GetTimestamp();
            fixed (int* buffer = _readback)
            {
                if(copyTexture)glGetTexImage(0x0DE1,0,0x80E1,0x1401,(IntPtr)buffer);
                else glReadPixels(0, 0, width, height, 0x80E1, 0x1401, (IntPtr)buffer);
            }
            if (glGetError() != 0) throw new InvalidOperationException("OpenGL reported a rendering error.");
            // The stage already includes its opaque NES background. Do not blend
            // it a second time against the WinForms canvas using GL's accumulated
            // framebuffer alpha (which is lower than one on translucent sheets).
            for (int index = 0; index < _readback.Length; index++) _readback[index] |= unchecked((int)0xff000000);
            Bitmap bitmap=_bitmap!;
            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                fixed (int* source = _readback)
                    for (int y = 0; y < height; y++)
                        Buffer.MemoryCopy(source + (height - 1 - y) * width, (byte*)data.Scan0 + y * data.Stride, width * 4, width * 4);
            }
            finally { bitmap.UnlockBits(data); }
            LastReadbackMs=System.Diagnostics.Stopwatch.GetElapsedTime(readStart).TotalMilliseconds;
            ReadbackFrames++;
            return _bitmap;
        }
        finally { wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); }
    }

    private void CopyPresentationTexture(int width,int height)
    {
        if(_presentationTexture==0)glGenTextures(1,out _presentationTexture);
        glBindTexture(0x0DE1,_presentationTexture);
        if(_presentationSize!=new Size(width,height))
        {
            glTexImage2D(0x0DE1,0,0x8058,width,height,0,0x80E1,0x1401,IntPtr.Zero);
            glTexParameteri(0x0DE1,0x2801,0x2600);glTexParameteri(0x0DE1,0x2800,0x2600);
            glTexParameteri(0x0DE1,0x2802,0x812F);glTexParameteri(0x0DE1,0x2803,0x812F);
            _presentationSize=new(width,height);
        }
        // Keep the original drawable's precise depth/blend/dither behaviour.
        // A GPU-local copy avoids the tiny colour-rounding changes of alternate
        // framebuffer formats, without ever transferring gameplay pixels to CPU.
        glCopyTexSubImage2D(0x0DE1,0,0,0,0,0,width,height);
    }

    private void DrawPresentationTexture(Size size)
    {
        glViewport(0,0,size.Width,size.Height);glDisable(0x0C11);glDisable(0x0BE2);glDisable(0x0BC0);glDisable(0x0B71);
        glMatrixMode(0x1701);glLoadIdentity();glOrtho(0,size.Width,0,size.Height,-1,1);
        glMatrixMode(0x1700);glLoadIdentity();
        glBindTexture(0x0DE1,_presentationTexture);glColor4f(1,1,1,1);
        glBegin(0x0007);
        glTexCoord2f(0,0);glVertex2f(0,0);glTexCoord2f(1,0);glVertex2f(size.Width,0);
        glTexCoord2f(1,1);glVertex2f(size.Width,size.Height);glTexCoord2f(0,1);glVertex2f(0,size.Height);
        glEnd();
    }

    internal unsafe Bitmap ReadDisplayedStageForTest(GpuStageWindow target)
        =>ReadStageForTest(target);
    internal Bitmap ReadDisplayedBackStageForTest(GpuStageWindow target)=>ReadStageForTest(target,true);

    internal Bitmap ReadStoredStageForTest()=>ReadStageForTest(null);
    internal Bitmap CloneLastReadbackForTest()=>(Bitmap)_bitmap!.Clone();

    private unsafe Bitmap ReadStageForTest(GpuStageWindow? target,bool backBuffer=false)
    {
        if(!wglMakeCurrent(target?.DeviceContext??_dc,_context))throw new InvalidOperationException("Could not inspect displayed GPU stage.");
        try
        {
            if(target is not null)
            {
                glGetIntegerv(0x0C32,out int doubleBuffered);
                glGetIntegerv(0x0C01,out int drawBuffer);
                DescribePixelFormat(target.DeviceContext,_pixelFormat,(uint)Marshal.SizeOf<PixelFormatDescriptor>(),out var actualFormat);
                PresentedDrawableForTest=new{Device=DeviceName,DoubleBuffered=doubleBuffered,DrawBuffer=drawBuffer,
                    CurrentDcIsStage=wglGetCurrentDC()==target.DeviceContext,PixelFormatFlags=actualFormat.Flags,
                    PixelType=actualFormat.PixelType,ColorBits=actualFormat.ColorBits,AlphaBits=actualFormat.AlphaBits,
                    DepthBits=actualFormat.DepthBits,LayerType=actualFormat.LayerType};
            }
            Size size=target?.StageSize??_presentationSize;
            int width=size.Width,height=size.Height;
            int[] pixels=new int[width*height];
            fixed(int* data=pixels)
            {
                if(target is not null){glReadBuffer(backBuffer?0x0405u:0x0404u);glReadPixels(0,0,width,height,0x80E1,0x1401,(IntPtr)data);}
                else{glBindTexture(0x0DE1,_presentationTexture);glGetTexImage(0x0DE1,0,0x80E1,0x1401,(IntPtr)data);}
            }
            if(glGetError()!=0)throw new InvalidOperationException("Could not read displayed GPU stage.");
            Bitmap image=new(width,height,PixelFormat.Format32bppArgb);
            var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try
            {
                for(int i=0;i<pixels.Length;i++)pixels[i]|=unchecked((int)0xff000000);
                fixed(int* data=pixels)for(int y=0;y<height;y++)Buffer.MemoryCopy(data+(height-1-y)*width,(byte*)bits.Scan0+y*bits.Stride,width*4,width*4);
            }
            finally{image.UnlockBits(bits);}
            return image;
        }
        finally{glReadBuffer(0x0405);wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);}
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct GpuVertex { public float X, Y, Z, S, T, R, Q, Red, Green, Blue, Alpha; }
    private bool EnsureVertexBuffer()
    {
        if(!_bufferFunctionsLoaded)
        {
            _bufferFunctionsLoaded=true;
            _genBuffers=LoadBufferFunction<GenBuffersDelegate>("glGenBuffers");
            _bindBuffer=LoadBufferFunction<BindBufferDelegate>("glBindBuffer");
            _bufferData=LoadBufferFunction<BufferDataDelegate>("glBufferData");
            _deleteBuffers=LoadBufferFunction<DeleteBuffersDelegate>("glDeleteBuffers");
        }
        if(_genBuffers is null||_bindBuffer is null||_bufferData is null||_deleteBuffers is null)return false;
        if(_vertexBuffer==0)_genBuffers(1,out _vertexBuffer);
        return _vertexBuffer!=0;
    }
    private static T? LoadBufferFunction<T>(string name) where T:Delegate
    {
        IntPtr address=wglGetProcAddress(name);
        long value=address.ToInt64();
        return value is 0 or 1 or 2 or 3 or -1?null:Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private unsafe void DrawStreaming(IReadOnlyList<SurfaceGroup> groups,RectangleF bounds)
    {
        // One frame upload, but retain every original per-group depth range and
        // opaque/translucent run. No cross-object batching/reordering.
        OrderedSurfaceTriangles[] ordered=new OrderedSurfaceTriangles[groups.Count];
        int total=0;
        long orderingStart=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int g=0;g<groups.Count;g++){ordered[g]=groups[g].Ordered();total=checked(total+ordered[g].Length*3);}
        LastOrderingMs+=System.Diagnostics.Stopwatch.GetElapsedTime(orderingStart).TotalMilliseconds;
        GpuVertex[] vertices=ArrayPool<GpuVertex>.Shared.Rent(Math.Max(1,total));
        List<BufferDraw> draws=new(groups.Count*2);
        try
        {
            int write=0;
            long packingStart=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int g=0;g<groups.Count;g++)
            {
                int index=0;ImagePixels? pixels=null;AtlasRegion region=_textures[White.Key];
                while(index<ordered[g].Length)
                {
                    bool opaque=ordered[g][index].Opaque;
                    int count=PackRun(ordered[g],ref index,vertices,bounds,groups[g].Offset,opaque,ref pixels,ref region,UseDirectVertexWritesForTest,write);
                    draws.Add(new(g,opaque,write,count));write+=count;
                }
            }
            if(write!=total)throw new InvalidOperationException("Streaming vertex count mismatch.");
            LastPackingMs+=System.Diagnostics.Stopwatch.GetElapsedTime(packingStart).TotalMilliseconds;
            long driverStart=System.Diagnostics.Stopwatch.GetTimestamp();
            _bindBuffer!(0x8892,_vertexBuffer);
            fixed(GpuVertex* data=vertices)_bufferData!(0x8892,(IntPtr)checked(total*sizeof(GpuVertex)),(IntPtr)data,0x88E0);
            glVertexPointer(3,0x1406,sizeof(GpuVertex),IntPtr.Zero);
            glTexCoordPointer(4,0x1406,sizeof(GpuVertex),(IntPtr)12);
            glColorPointer(4,0x1406,sizeof(GpuVertex),(IntPtr)28);
            foreach(var draw in draws)
            {
                glDepthRange((groups.Count-draw.Group-1d)/groups.Count,(groups.Count-draw.Group)/(double)groups.Count);
                glDepthMask(draw.Opaque?(byte)1:(byte)0);
                glDrawArrays(0x0004,draw.First,draw.Count);LastDrawCalls++;
            }
            LastDriverMs+=System.Diagnostics.Stopwatch.GetElapsedTime(driverStart).TotalMilliseconds;
        }
        finally{_bindBuffer?.Invoke(0x8892,0);ArrayPool<GpuVertex>.Shared.Return(vertices);}
    }
    private readonly record struct GpuTint(float Red,float Green,float Blue,float Alpha);
    // Diagnostic only: Render/Present retain their original arithmetic.
    private static float[]? _byteColorValues;
    private static float[] ByteColorValues
    {
        get
        {
            if(_byteColorValues is {} existing)return existing;
            float[] values=new float[256];
            for(int i=0;i<values.Length;i++)values[i]=i/255f;
            return Interlocked.CompareExchange(ref _byteColorValues,values,null)??values;
        }
    }
    private unsafe int PackByteColorRun(OrderedSurfaceTriangles triangles,ref int index,GpuVertex[] vertices,RectangleF bounds,PointF offset,
        bool opaque,ref ImagePixels? lastPixels,ref AtlasRegion region)
    {
        if(vertices.Length<checked(triangles.Length*3))throw new ArgumentException("Vertex buffer too small.");
        int count=0;
        fixed(GpuVertex* buffer=vertices)
        fixed(float* colors=ByteColorValues)
        while(index<triangles.Length&&triangles[index].Opaque==opaque)
        {
            var triangle=triangles[index++];var pixels=triangle.Texture??White;
            if(!ReferenceEquals(pixels,lastPixels)){region=_textures[pixels.Key];lastPixels=pixels;}
            int argb=triangle.Color.ToArgb();
            GpuTint tint=new(colors[(argb>>16)&255],colors[(argb>>8)&255],colors[argb&255],colors[(uint)argb>>24]*triangle.Opacity);
            WriteVertex(ref buffer[count++],triangle.A,bounds,offset,region,tint);
            WriteVertex(ref buffer[count++],triangle.B,bounds,offset,region,tint);
            WriteVertex(ref buffer[count++],triangle.C,bounds,offset,region,tint);
        }
        return count;
    }
    private unsafe int PackRun(OrderedSurfaceTriangles triangles,ref int index,GpuVertex[] vertices,RectangleF bounds,PointF offset,
        bool opaque,ref ImagePixels? lastPixels,ref AtlasRegion region,bool direct,int destinationOffset=0)
    {
        int count=0;
        if(direct)
        {
            if(destinationOffset<0||vertices.Length<checked(destinationOffset+(triangles.Length-index)*3))throw new ArgumentException("Vertex buffer too small.");
            fixed(GpuVertex* data=vertices)
            {
            GpuVertex* buffer=data+destinationOffset;
            while(index<triangles.Length&&triangles[index].Opaque==opaque)
            {
                var triangle=triangles[index++];var pixels=triangle.Texture??White;
                if(!ReferenceEquals(pixels,lastPixels)){region=_textures[pixels.Key];lastPixels=pixels;}
                int argb=triangle.Color.ToArgb();
                GpuTint tint=new(((argb>>16)&255)/255f,((argb>>8)&255)/255f,(argb&255)/255f,
                    ((uint)argb>>24)/255f*triangle.Opacity);
                // Capacity is checked once; each run emits at most three
                // vertices per triangle, with no pointer surviving this call.
                WriteVertex(ref buffer[count++],triangle.A,bounds,offset,region,tint);
                WriteVertex(ref buffer[count++],triangle.B,bounds,offset,region,tint);
                WriteVertex(ref buffer[count++],triangle.C,bounds,offset,region,tint);
            }
            }
            return count;
        }
        while(index<triangles.Length&&triangles[index].Opaque==opaque)
        {
            var triangle=triangles[index++];var pixels=triangle.Texture??White;
            if(!ReferenceEquals(pixels,lastPixels)){region=_textures[pixels.Key];lastPixels=pixels;}
            Color color=triangle.Color;
            GpuTint tint=new(color.R/255f,color.G/255f,color.B/255f,color.A/255f*triangle.Opacity);
            vertices[destinationOffset+count++]=Vertex(triangle.A,bounds,offset,region,tint);
            vertices[destinationOffset+count++]=Vertex(triangle.B,bounds,offset,region,tint);
            vertices[destinationOffset+count++]=Vertex(triangle.C,bounds,offset,region,tint);
        }
        return count;
    }
    // Rejected optimization retained only for explicit paired diagnostics.
    // Normal Render/Present never calls this experimental path.
    private unsafe int PackCachedTintRun(OrderedSurfaceTriangles triangles,ref int index,GpuVertex[] vertices,RectangleF bounds,PointF offset,
        bool opaque,ref ImagePixels? lastPixels,ref AtlasRegion region)
    {
        if(vertices.Length<checked(triangles.Length*3))throw new ArgumentException("Vertex buffer too small.");
        int count=0,previousArgb=0,previousOpacity=0;bool haveTint=false;GpuTint tint=default;
        fixed(GpuVertex* buffer=vertices)
        while(index<triangles.Length&&triangles[index].Opaque==opaque)
        {
            var triangle=triangles[index++];var pixels=triangle.Texture??White;
            if(!ReferenceEquals(pixels,lastPixels)){region=_textures[pixels.Key];lastPixels=pixels;}
            int argb=triangle.Color.ToArgb(),opacityBits=BitConverter.SingleToInt32Bits(triangle.Opacity);
            if(!haveTint || argb!=previousArgb || opacityBits!=previousOpacity)
            {
                tint=new(((argb>>16)&255)/255f,((argb>>8)&255)/255f,(argb&255)/255f,
                    ((uint)argb>>24)/255f*triangle.Opacity);
                previousArgb=argb;previousOpacity=opacityBits;haveTint=true;
            }
            WriteVertex(ref buffer[count++],triangle.A,bounds,offset,region,tint);
            WriteVertex(ref buffer[count++],triangle.B,bounds,offset,region,tint);
            WriteVertex(ref buffer[count++],triangle.C,bounds,offset,region,tint);
        }
        return count;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void WriteVertex(ref GpuVertex target,SurfaceVertex v,RectangleF bounds,PointF offset,AtlasRegion region,in GpuTint tint)
    {
        // Preserve every original float operation/order, but write the 44-byte
        // vertex in place rather than returning and copying a large struct.
        target.X=v.Point.X+offset.X-bounds.X;target.Y=v.Point.Y+offset.Y-bounds.Y;target.Z=Math.Clamp(v.Depth,-.99f,.99f);
        target.S=(region.X+1+v.U*region.Width)/AtlasSize*v.Q;
        target.T=(region.Y+1+v.V*region.Height)/AtlasSize*v.Q;
        target.R=0;target.Q=v.Q;target.Red=tint.Red;target.Green=tint.Green;target.Blue=tint.Blue;target.Alpha=tint.Alpha;
    }
    internal unsafe object MeasurePackingForTest(IReadOnlyList<SurfaceGroup> groups,RectangleF bounds,int iterations,bool tintOnly=false,bool byteColorsOnly=false)
    {
        var ordered=groups.Select(g=>g.Ordered()).ToArray();
        int capacity=Math.Max(1,ordered.Max(t=>t.Length)*3);
        var legacy=new GpuVertex[capacity];var direct=new GpuVertex[capacity];
        List<double> oldTimes=new(iterations),newTimes=new(iterations);long compared=0;
        for(int iteration=0;iteration<iterations;iteration++)
        {
            double oldMs,newMs;
            if(iteration%2==0){oldMs=Pack(false,legacy);newMs=Pack(true,direct);}
            else{newMs=Pack(true,direct);oldMs=Pack(false,legacy);}
            if(iteration>=32){oldTimes.Add(oldMs);newTimes.Add(newMs);}
            // Independently compare every opaque/translucent run; the timed
            // loops above do no comparison, sorting, allocation or GPU work.
            for(int g=0;g<groups.Count;g++)
            {
                int a=0,b=0;ImagePixels? ap=null,bp=null;AtlasRegion ar=_textures[White.Key],br=ar;
                while(a<ordered[g].Length)
                {
                    int n=PackRun(ordered[g],ref a,legacy,bounds,groups[g].Offset,ordered[g][a].Opaque,ref ap,ref ar,tintOnly||byteColorsOnly);
                    int m=byteColorsOnly?PackByteColorRun(ordered[g],ref b,direct,bounds,groups[g].Offset,ordered[g][b].Opaque,ref bp,ref br)
                        :tintOnly?PackCachedTintRun(ordered[g],ref b,direct,bounds,groups[g].Offset,ordered[g][b].Opaque,ref bp,ref br)
                        :PackRun(ordered[g],ref b,direct,bounds,groups[g].Offset,ordered[g][b].Opaque,ref bp,ref br,true);
                    if(n!=m||a!=b)throw new InvalidDataException("Packing run count mismatch.");
                    fixed(GpuVertex* pa=legacy,pb=direct)
                    {
                        var sa=new ReadOnlySpan<byte>(pa,n*sizeof(GpuVertex));var sb=new ReadOnlySpan<byte>(pb,n*sizeof(GpuVertex));
                        if(!sa.SequenceEqual(sb))throw new InvalidDataException($"Vertex bytes differ at iteration{iteration}, group{g}.");
                    }
                    compared+=n;
                }
            }
        }
        return new{Exact=true,ComparedVertices=compared,Triangles=ordered.Sum(t=>t.Length),MeasuredIterations=oldTimes.Count,
            LegacyMeanMs=oldTimes.Average(),DirectMeanMs=newTimes.Average(),LegacyMedianMs=oldTimes.Order().ElementAt(oldTimes.Count/2),
            DirectMedianMs=newTimes.Order().ElementAt(newTimes.Count/2),LegacySamples=oldTimes,DirectSamples=newTimes};
        double Pack(bool writeDirect,GpuVertex[] buffer)
        {
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int g=0;g<groups.Count;g++)
            {
                int index=0;ImagePixels? pixels=null;AtlasRegion region=_textures[White.Key];
                while(index<ordered[g].Length)
                    _=writeDirect&&byteColorsOnly?PackByteColorRun(ordered[g],ref index,buffer,bounds,groups[g].Offset,ordered[g][index].Opaque,ref pixels,ref region)
                        :writeDirect&&tintOnly?PackCachedTintRun(ordered[g],ref index,buffer,bounds,groups[g].Offset,ordered[g][index].Opaque,ref pixels,ref region)
                        :PackRun(ordered[g],ref index,buffer,bounds,groups[g].Offset,ordered[g][index].Opaque,ref pixels,ref region,writeDirect||tintOnly||byteColorsOnly);
            }
            return System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static GpuVertex Vertex(SurfaceVertex v, RectangleF bounds, PointF offset,AtlasRegion region,in GpuTint tint)
    {
        return new() { X = v.Point.X + offset.X - bounds.X, Y = v.Point.Y + offset.Y - bounds.Y, Z = Math.Clamp(v.Depth, -0.99f, 0.99f),
            S = (region.X+1+v.U*region.Width)/AtlasSize*v.Q, T = (region.Y+1+v.V*region.Height)/AtlasSize*v.Q, R = 0, Q = v.Q, Red = tint.Red, Green = tint.Green,
            Blue = tint.Blue, Alpha = tint.Alpha };
    }

    private void EnsureAtlas(IReadOnlyList<SurfaceGroup> groups)
    {
        if(_atlas==0)
        {
            glGenTextures(1,out _atlas);glBindTexture(0x0DE1,_atlas);
            glTexImage2D(0x0DE1,0,0x1908,AtlasSize,AtlasSize,0,0x80E1,0x1401,IntPtr.Zero);
        }
        glBindTexture(0x0DE1,_atlas);
        glTexParameteri(0x0DE1, 0x2801, 0x2600); glTexParameteri(0x0DE1, 0x2800, 0x2600);
        glTexParameteri(0x0DE1, 0x2802, 0x812F); glTexParameteri(0x0DE1, 0x2803, 0x812F);
        Dictionary<string,ImagePixels> used=new(){[White.Key]=White};
        foreach(var group in groups)
        {
            if(UseCachedTextureInventoryForTest)
            {
                foreach(var pixels in group.TextureInventory)used.TryAdd(pixels.Key,pixels);
                continue;
            }
            ImagePixels? lastPixels=null;
            foreach(var triangle in group.Triangles)
                if(triangle.Texture is {} pixels && !ReferenceEquals(pixels,lastPixels)){used.TryAdd(pixels.Key,pixels);lastPixels=pixels;}
        }
        if(used.Values.Any(p=>p.Width+2>AtlasSize||p.Height+2>AtlasSize))throw new InvalidOperationException("Artwork exceeds GPU atlas limits.");
        // Reset only before drawing, never halfway through a frame. Duplicate-edge
        // padding prevents neighboring sprite colors bleeding across UV boundaries.
        if(!Fits(used.Values.Where(p=>!_textures.ContainsKey(p.Key)))){_textures.Clear();_atlasX=_atlasY=_atlasRowHeight=0;}
        if(!Fits(used.Values.Where(p=>!_textures.ContainsKey(p.Key))))throw new InvalidOperationException("Current scene exceeds GPU atlas capacity.");
        foreach(var pixels in used.Values)Upload(pixels);
    }
    private bool Fits(IEnumerable<ImagePixels> images)
    {
        int x=_atlasX,y=_atlasY,row=_atlasRowHeight;
        foreach(var p in images){int w=p.Width+2,h=p.Height+2;if(x+w>AtlasSize){x=0;y+=row;row=0;}if(y+h>AtlasSize)return false;x+=w;row=Math.Max(row,h);}return true;
    }
    private unsafe void Upload(ImagePixels pixels)
    {
        if(_textures.ContainsKey(pixels.Key))return;
        int width=pixels.Width+2,height=pixels.Height+2;
        if(_atlasX+width>AtlasSize){_atlasX=0;_atlasY+=_atlasRowHeight;_atlasRowHeight=0;}
        int[] padded=ArrayPool<int>.Shared.Rent(width*height);
        try
        {
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)padded[y*width+x]=pixels.Pixels[Math.Clamp(y-1,0,pixels.Height-1)*pixels.Width+Math.Clamp(x-1,0,pixels.Width-1)];
            fixed(int* data=padded)glTexSubImage2D(0x0DE1,0,_atlasX,_atlasY,width,height,0x80E1,0x1401,(IntPtr)data);
            _textures[pixels.Key]=new(_atlasX,_atlasY,pixels.Width,pixels.Height);_atlasX+=width;_atlasRowHeight=Math.Max(_atlasRowHeight,height);TextureUploads++;
        }
        finally{ArrayPool<int>.Shared.Return(padded);}
    }
    public void Dispose()
    {
        _bitmap?.Dispose();
        if (_context != IntPtr.Zero)
        {
            wglMakeCurrent(_dc, _context);
            if(_vertexBuffer!=0)_deleteBuffers?.Invoke(1,ref _vertexBuffer);
            if(_presentationTexture!=0)glDeleteTextures(1,ref _presentationTexture);
            if(_atlas!=0)glDeleteTextures(1,ref _atlas);
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); wglDeleteContext(_context); _context = IntPtr.Zero;
        }
        if (_dc != IntPtr.Zero) { ReleaseDC(_window.Handle, _dc); _dc = IntPtr.Zero; }
        _window.DestroyHandle();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort Size, Version; public uint Flags;
        public byte PixelType, ColorBits, RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift, AlphaBits, AlphaShift,
            AccumBits, AccumRedBits, AccumGreenBits, AccumBlueBits, AccumAlphaBits, DepthBits, StencilBits, AuxBuffers, LayerType, Reserved;
        public uint LayerMask, VisibleMask, DamageMask;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr dc, ref PixelFormatDescriptor descriptor);
    [DllImport("gdi32.dll")] private static extern int DescribePixelFormat(IntPtr dc,int index,uint size,out PixelFormatDescriptor descriptor);
    [DllImport("gdi32.dll")] private static extern bool SwapBuffers(IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetPixelFormat(IntPtr dc, int index, ref PixelFormatDescriptor descriptor);
    [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr dc);
    [DllImport("opengl32.dll",CharSet=CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetCurrentDC();
    [DllImport("opengl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool wglMakeCurrent(IntPtr dc, IntPtr context);
    [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr context);
    [DllImport("opengl32.dll")] private static extern IntPtr glGetString(uint name);
    [DllImport("opengl32.dll")] private static extern void glGetIntegerv(uint name,out int value);
    [DllImport("opengl32.dll")] private static extern void glViewport(int x, int y, int width, int height);
    [DllImport("opengl32.dll")] private static extern void glScissor(int x,int y,int width,int height);
    [DllImport("opengl32.dll")] private static extern void glMatrixMode(uint mode);
    [DllImport("opengl32.dll")] private static extern void glLoadIdentity();
    [DllImport("opengl32.dll")] private static extern void glOrtho(double left, double right, double bottom, double top, double near, double far);
    [DllImport("opengl32.dll")] private static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] private static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] private static extern void glEnable(uint capability);
    [DllImport("opengl32.dll")] private static extern void glEnableClientState(uint capability);
    [DllImport("opengl32.dll")] private static extern void glDisableClientState(uint capability);
    [DllImport("opengl32.dll")] private static extern void glVertexPointer(int size, uint type, int stride, IntPtr pointer);
    [DllImport("opengl32.dll")] private static extern void glTexCoordPointer(int size, uint type, int stride, IntPtr pointer);
    [DllImport("opengl32.dll")] private static extern void glColorPointer(int size, uint type, int stride, IntPtr pointer);
    [DllImport("opengl32.dll")] private static extern void glDrawArrays(uint mode, int first, int count);
    [DllImport("opengl32.dll")] private static extern void glDisable(uint capability);
    [DllImport("opengl32.dll")] private static extern void glBlendFunc(uint source, uint destination);
    [DllImport("opengl32.dll")] private static extern void glAlphaFunc(uint function, float reference);
    [DllImport("opengl32.dll")] private static extern void glDepthFunc(uint function);
    [DllImport("opengl32.dll")] private static extern void glDepthRange(double near, double far);
    [DllImport("opengl32.dll")] private static extern void glDepthMask(byte enabled);
    [DllImport("opengl32.dll")] private static extern void glReadBuffer(uint buffer);
    [DllImport("opengl32.dll")] private static extern void glDrawBuffer(uint buffer);
    [DllImport("opengl32.dll")] private static extern void glReadPixels(int x, int y, int width, int height, uint format, uint type, IntPtr pixels);
    [DllImport("opengl32.dll")] private static extern uint glGetError();
    [DllImport("opengl32.dll")] private static extern void glGenTextures(int count, out uint texture);
    [DllImport("opengl32.dll")] private static extern void glDeleteTextures(int count, ref uint texture);
    [DllImport("opengl32.dll")] private static extern void glBindTexture(uint target, uint texture);
    [DllImport("opengl32.dll")] private static extern void glTexParameteri(uint target, uint name, int value);
    [DllImport("opengl32.dll")] private static extern void glTexImage2D(uint target, int level, int internalFormat, int width, int height,
        int border, uint format, uint type, IntPtr pixels);
    [DllImport("opengl32.dll")] private static extern void glTexSubImage2D(uint target,int level,int x,int y,int width,int height,uint format,uint type,IntPtr pixels);
    [DllImport("opengl32.dll")] private static extern void glCopyTexSubImage2D(uint target,int level,int xOffset,int yOffset,int x,int y,int width,int height);
    [DllImport("opengl32.dll")] private static extern void glGetTexImage(uint target,int level,uint format,uint type,IntPtr pixels);
    [DllImport("opengl32.dll")] private static extern void glBegin(uint mode);
    [DllImport("opengl32.dll")] private static extern void glEnd();
    [DllImport("opengl32.dll")] private static extern void glColor4f(float red,float green,float blue,float alpha);
    [DllImport("opengl32.dll")] private static extern void glTexCoord2f(float s,float t);
    [DllImport("opengl32.dll")] private static extern void glVertex2f(float x,float y);
}
