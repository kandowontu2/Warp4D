using System.Drawing.Drawing2D;
using Warp4D.Profiles;

namespace Warp4D.Rendering;

internal sealed class WarpRendererControl : Control
{
    private SmbScene? _scene;
    private bool _dragging;
    private Point _lastMouse;
    private RectangleF _gameBounds;
    internal RectangleF GameBoundsForTest => _gameBounds;
    private SceneObject? _hovered;
    private Rotation4D _rotation = Rotation4D.Default;
    private readonly System.Windows.Forms.Timer _welcomeTimer = new() { Interval = 40 };
    private readonly System.Diagnostics.Stopwatch _welcomeClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly System.Diagnostics.Stopwatch _lookPulse = new();
    private readonly System.Diagnostics.Stopwatch _styleClock = new();
    private GeometrySettings? _previousGeometry;
    private Rotation4D _previousRotation;
    private float _previousDepth, _previousPerspective;
    private readonly Dictionary<string,ImagePixels> _haloCache = [];
    private SmbScene? _showcase;
    private ImagePixels? _showcaseBackground;
    private bool _drawingShowcase;
    private PointF _pointer;
    private GeometrySettings? _crossfadeGeometry;
    private float _crossfadeBlend;
    private readonly AdaptiveDetail _adaptive = new();
    private readonly Queue<(Rotation4D Rotation,double Time)> _echoes = new();
    private double _lastEcho;
    private int _trailObjects;
    private bool _buildingEcho;
    private Rotation4D? _echoRotation;
    private double? _echoTime;
    public bool PaintObjects { get; set; }
    public bool RevealOriginal { get; set; }
    public int[]? NativeScreenPixels { get; set; }
    public float AudioLevel { get; set; }
    public double? MotionSecondsForTest { get; set; }
    private double MotionSeconds => UI.InterfaceMotion.Enabled ? _echoTime ?? MotionSecondsForTest ?? Math.Floor(_welcomeClock.Elapsed.TotalSeconds*30)/30 : 0;
    public string DetailStatus => Settings.Dimensions.AdaptiveQuality ? $"Adaptive detail {_adaptive.Level}/3 · draw {_adaptive.AverageMilliseconds:0.0} ms" : "Full authored detail";
    public event Action<SceneObject>? PaintObjectPicked;
    public event Action<Bitmap>? CleanFrameRendered;
    public Func<bool>? NeedsCleanFrame { get; set; }
    public bool DirectGpuPresentation { get; set; } = true;
    private GpuStageWindow? _gpuStage;
    private bool _directGpuFailed;
    internal string? DirectGpuFailureForTest {get;private set;}
    private int _printingDepth;
    public void ResetRuntime() { _echoes.Clear(); _lastEcho=0; NativeScreenPixels=null; AudioLevel=0; ClearSurfaceCache(); _backgroundPixelCache.Clear(); _backgroundPixels=null; }
    public void ApplyCrossfade(PresentationSettings a, PresentationSettings b,float amount,PresentationSettings preserved)
    {
        bool transitions=EnableStyleTransitions;EnableStyleTransitions=false;
        ApplySettings(LookCrossfader.Blend(a,b,amount,preserved));EnableStyleTransitions=transitions;
        RefreshEffects();
    }
    private bool GeometryBlending => Morphing || _crossfadeGeometry is not null;
    private GeometrySettings? SourceGeometry => _crossfadeGeometry ?? _previousGeometry;
    private float GeometryBlend => _crossfadeGeometry is null ? StyleBlend : _crossfadeBlend;
    internal void SetGeometryMorphForTest(GeometrySettings source,float blend)
    { _crossfadeGeometry=source.Clone();_crossfadeBlend=blend; }
    public bool EnableStyleTransitions { get; set; }
    internal float StyleBlend { get { float p=Math.Clamp((float)(_styleClock.Elapsed.TotalSeconds/.65),0,1);return _previousGeometry is null?1:p*p*(3-2*p); } }
    private bool Morphing => _previousGeometry is not null && StyleBlend<1 && UI.InterfaceMotion.Enabled;
    private void ClearSurfaceCache(){if(PlaybackObserverForTest is not null)_meshExplicitClears++;_surfaceCache.Clear();_lastSurfaceKeys.Clear();_animatedSurfaces.Clear();_accentSurfaces.Clear();_accentWriteIndex=0;}
    public void RefreshEffects() { ClearSurfaceCache(); Invalidate(); }
    internal void SignalLookChange() { _lookPulse.Restart(); Invalidate(); }
    private RectangleF _openBounds;
    private OpenGlSurfaceRenderer? _gpu;
    private readonly SoftwareSurfaceRenderer _software = new();
    private ImagePixels? _backgroundPixels;
    private readonly BackgroundPixelCache _backgroundPixelCache=new();
    internal bool UseBackgroundPixelCacheForTest {get;set;}=true;
    internal (long Hits,long Misses,int StoredPixels) BackgroundPixelCacheStatsForTest=>(_backgroundPixelCache.Hits,_backgroundPixelCache.Misses,_backgroundPixelCache.StoredPixels);
    private bool _gpuFailed;
    private readonly Dictionary<SurfaceCacheKey, (SurfaceGroup Group, PointF Position)> _surfaceCache = [];
    private readonly Dictionary<string,SurfaceCacheKey> _lastSurfaceKeys=[];
    private readonly Dictionary<string,SurfaceGroup> _animatedSurfaces=[];
    // Each slot is used once in one completed paint, never shared by two overlays.
    // Rebuild every field; retain storage only, not previous artwork or transforms.
    private readonly List<SurfaceGroup> _accentSurfaces=[];
    private int _accentWriteIndex;
    internal bool UseReusableAccentStorageForTest {get;set;}=true;
    public long SurfaceCacheHits { get; private set; }
    private long _meshNewGroups,_meshReusedGroups,_meshNewTriangles,_meshVertexArrays,_meshVertexElements,_meshBuildBytes,_meshOverflowClears,_meshExplicitClears;
    private long _meshAccentGroups,_meshAccentTriangles,_meshAccentBytes,_meshBackgroundBytes;
    internal MeshAssemblyStats MeshAssemblyForTest=>new(_meshNewGroups,_meshReusedGroups,_meshNewTriangles,_meshVertexArrays,_meshVertexElements,_meshBuildBytes,_meshOverflowClears,_meshExplicitClears,_meshAccentGroups,_meshAccentTriangles,_meshAccentBytes,_meshBackgroundBytes);
    public bool UseGpu { get; set; } = true;
    public string RendererStatus => UseGpu && _gpu is null && !_gpuFailed ? "GPU · awaiting first frame" : UseGpu && _gpu?.Available == true && !_gpuFailed ? "GPU · " + _gpu.DeviceName : "CPU fallback";
    public long TextureUploads => _gpu?.TextureUploads ?? 0;
    internal double GpuSubmissionMs=>_gpu?.LastSubmissionMs??0;
    internal double GpuReadbackMs=>_gpu?.LastReadbackMs??0;
    internal int GpuDrawCalls=>_gpu?.LastDrawCalls??0;
    internal double GpuOrderingMs=>_gpu?.LastOrderingMs??0;
    internal double GpuPackingMs=>_gpu?.LastPackingMs??0;
    internal bool UseCachedSheetDescriptorsForTest {get;set;}=true;
    private static readonly float[] SheetCoordinates=[-1,1,0,-.5f,.5f,-.25f,.25f,-.75f,.75f];
    private static readonly string[] SheetKeys=["W:-1","W:+1","W:0","W:-0.5","W:+0.5","W:-0.25","W:+0.25","W:-0.75","W:+0.75"];
    internal double GpuPresentMs=>_gpu?.LastPresentMs??0;
    internal long GpuPresentedFrames=>_gpu?.PresentedFrames??0;
    internal long GpuReadbackFrames=>_gpu?.ReadbackFrames??0;
    // Null during normal playback; excludes screenshots and physical scanout.
    internal Action<PlaybackPaintTiming>? PlaybackObserverForTest { get; set; }
    internal bool DirectGpuStageShown=>_gpuStage?.Shown==true;
    internal IntPtr DirectGpuStageHandleForTest=>_gpuStage?.Handle??IntPtr.Zero;
    internal object? DirectGpuDrawableDiagnosticForTest=>_gpuStage?.DrawableDiagnosticForTest;
    internal object? DirectGpuBufferDiagnosticForTest=>_gpu?.PresentedDrawableForTest;
    internal Bitmap ReadDisplayedBackStageForTest()=>_gpu!.ReadDisplayedBackStageForTest(_gpuStage!);
    internal Bitmap ReadDisplayedStageForTest()=>_gpu!.ReadDisplayedStageForTest(_gpuStage!);
    internal Bitmap ReadStoredStageForTest()=>_gpu!.ReadStoredStageForTest();
    internal Bitmap CloneLastReadbackForTest()=>_gpu!.CloneLastReadbackForTest();
    public double LastDrawMilliseconds { get; private set; }
    internal double SceneAssemblyMilliseconds { get; private set; }
    internal double LightingMilliseconds { get; private set; }
    internal int SurfaceTriangleCount { get; private set; }
    internal bool ReuseMeshStorageForTest { get; set; } = true;
    internal bool UseGeometryMapCacheForTest { get; set; } = false;
    private GeometryMapCache? _geometryMapCache;
    internal (long Hits,long Misses,int Entries,int Vertices) GeometryMapCacheStatsForTest=>
        _geometryMapCache is {} cache?(cache.Hits,cache.Misses,cache.Entries,cache.Vertices):(0,0,0,0);
    internal bool UseBatchedProjectionForTest { get; set; } = true;
    internal bool UsePackedLightingForTest { get; set; } = true;
    internal bool UseDirectVertexWritesForTest { get; set; } = true;
    internal bool UseReusableOrderStorageForTest {get;set;} = true;
    internal bool UseCachedTextureInventoryForTest { get; set; } = true;
    internal bool UseStreamingVertexBufferForTest {get;set;}=false;
    internal bool UsedStreamingVertexBufferForTest=>_gpu?.LastUsedStreamingVertexBuffer??false;
    internal string LastDirectFallbackForTest {get;private set;}="";
    internal bool IgnorePointerForTest {get;set;}
    internal void SetHoveredObjectForTest(SceneObject? item){_hovered=item;Invalidate();}

    public WarpRendererControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        BackColor = Color.FromArgb(7, 10, 16);
        TabStop = true;
        _welcomeTimer.Tick += (_, _) =>
        {
            if (Visible && UI.InterfaceMotion.Enabled && FindForm()?.WindowState != FormWindowState.Minimized &&
                (_scene is null || Morphing || Settings.Dimensions.Choreography || Settings.Dimensions.AudioReactive || Settings.Dimensions.Trails || !PresentationMode && _lookPulse.IsRunning && _lookPulse.Elapsed.TotalSeconds < 1.2)) Invalidate();
        };
        _welcomeTimer.Start();
    }


    public float DepthAmount { get; set; } = 0.72f;
    public float Perspective { get; set; } = 0.55f;
    public float ProjectionOpacity { get; set; } = 0.18f;
    public int SliceCount { get; set; } = 3;
    public float ProjectionRotationSpreadDegrees { get; set; } = 58f;
    public double ProjectionCycleSeconds { get; set; }
    public double GeometryCycleSeconds { get; set; }
    public PresentationSettings Settings { get; private set; } = new();
    internal IReadOnlyList<SceneObject> SceneObjects => _scene?.Objects ?? [];
    internal SmbScene? CloneScene() => _scene?.Clone();
    public string? SelectedObjectKey { get; set; }
    public string? SelectedLayerKey { get; set; }
    public bool PresentationMode { get; set; }
    public bool GuidedInterface {get;set;}
    public bool CinematicHint { get; set; }
    public event Action? OpenRomRequested;
    public event Action<string>? ObjectPicked;
    public event Action<string, string>? ProjectionPicked;
    public event Action? LayerEdited;
    public bool EditLayers { get; set; }
    private bool _layerDragging;
    private Keys? _dragModifiersForTest;

    public void ApplySettings(PresentationSettings settings)
    {
        _crossfadeGeometry=settings.BlendSource?.Clone();_crossfadeBlend=settings.BlendAmount;
        if(_crossfadeGeometry is null && EnableStyleTransitions && UI.InterfaceMotion.Enabled && settings.Effects.SmoothTransitions && (settings.Name!=Settings.Name || settings.Geometry.Mode!=Settings.Geometry.Mode))
        {
            _previousRotation=EffectiveRotation;_previousGeometry=Settings.Geometry.Clone();
            _previousDepth=DepthAmount;_previousPerspective=Perspective;_styleClock.Restart();
        }
        else { _previousGeometry=null;_styleClock.Reset(); }
        ClearSurfaceCache();
        Settings = settings;
        DepthAmount = settings.Depth; Perspective = settings.Perspective; ProjectionOpacity = settings.Opacity;
        SliceCount = settings.CrossSections; ProjectionRotationSpreadDegrees = settings.RotationSpread;
        AngleXYDegrees = settings.Rotation.XY; AngleXZDegrees = settings.Rotation.XZ; AngleXWDegrees = settings.Rotation.XW;
        AngleYZDegrees = settings.Rotation.YZ; AngleYWDegrees = settings.Rotation.YW; AngleZWDegrees = settings.Rotation.ZW;
        Invalidate();
    }

    public PresentationSettings CaptureSettings()
    {
        Settings.Depth = DepthAmount; Settings.Perspective = Perspective; Settings.Opacity = ProjectionOpacity;
        Settings.CrossSections = SliceCount; Settings.RotationSpread = ProjectionRotationSpreadDegrees;
        Settings.Rotation = new RotationAngles
        {
            XY = AngleXYDegrees, XZ = AngleXZDegrees, XW = AngleXWDegrees,
            YZ = AngleYZDegrees, YW = AngleYWDegrees, ZW = AngleZWDegrees
        };
        return Settings;
    }
    public float AngleXYDegrees
    {
        get => ToDegrees(_rotation.XY);
        set => _rotation = _rotation with { XY = ToRadians(value) };
    }
    public float AngleXWDegrees
    {
        get => ToDegrees(_rotation.XW);
        set => _rotation = _rotation with { XW = ToRadians(value) };
    }
    public float AngleYWDegrees
    {
        get => ToDegrees(_rotation.YW);
        set => _rotation = _rotation with { YW = ToRadians(value) };
    }
    public float AngleZWDegrees
    {
        get => ToDegrees(_rotation.ZW);
        set => _rotation = _rotation with { ZW = ToRadians(value) };
    }
    public float AngleXZDegrees
    {
        get => ToDegrees(_rotation.XZ);
        set => _rotation = _rotation with { XZ = ToRadians(value) };
    }
    public float AngleYZDegrees
    {
        get => ToDegrees(_rotation.YZ);
        set => _rotation = _rotation with { YZ = ToRadians(value) };
    }
    public bool ShowLabels { get; set; } = true;
    public event EventHandler? RotationChanged;
    internal Rotation4D RotationForCompass=>_rotation;
    internal float PlaneDegrees(string plane)=>ToDegrees(plane switch{"XY"=>_rotation.XY,"XZ"=>_rotation.XZ,"XW"=>_rotation.XW,"YZ"=>_rotation.YZ,"YW"=>_rotation.YW,_=>_rotation.ZW});
    internal void AdjustPlane(string plane,float degrees)
    {
        float d=ToRadians(degrees);
        _rotation=plane switch{"XY"=>_rotation with{XY=WrapAngle(_rotation.XY+d)},"XZ"=>_rotation with{XZ=WrapAngle(_rotation.XZ+d)},"XW"=>_rotation with{XW=WrapAngle(_rotation.XW+d)},"YZ"=>_rotation with{YZ=WrapAngle(_rotation.YZ+d)},"YW"=>_rotation with{YW=WrapAngle(_rotation.YW+d)},_=>_rotation with{ZW=WrapAngle(_rotation.ZW+d)}};
        RefreshEffects();RotationChanged?.Invoke(this,EventArgs.Empty);
    }
    internal void DragCompass(float dx,float dy,Keys modifiers){_rotation=DragRotation(_rotation,dx,dy,modifiers);RefreshEffects();RotationChanged?.Invoke(this,EventArgs.Empty);}

    public void SetScene(SmbScene scene)
    {
        SmbScene? old = _scene;
        _scene = scene;
        if(old?.Location!=scene.Location) _echoes.Clear();
        _backgroundPixels = null;
        _hovered = null;
        old?.Dispose();
        Invalidate();
    }

    public void ResetCamera()
    {
        _rotation = Rotation4D.Default;
        Invalidate();
        RotationChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scene?.Dispose();
            _scene = null;
            _gpu?.Dispose();
            _gpuStage?.Dispose();_gpuStage=null;
            _software.Dispose();
            _welcomeTimer.Dispose();
            _showcase?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _gpuStage?.Dispose();_gpuStage=null;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        bool printing=message.Msg is 0x0317 or 0x0318;
        if(printing)_printingDepth++;
        try {base.WndProc(ref message);}
        finally {if(printing)_printingDepth--;}
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _accentWriteIndex=0;
        long drawStart = System.Diagnostics.Stopwatch.GetTimestamp();
        long drawAllocatedStart=PlaybackObserverForTest is null?0:GC.GetAllocatedBytesForCurrentThread();
        long presentedBefore = GpuPresentedFrames;
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        SmbScene? scene = _scene;
        if (scene is null)
        {
            if(_printingDepth==0)_gpuStage?.ShowStage(false);
            DrawEmptyState(graphics);
            if(CinematicHint)
            {
                using Font hint=new("Segoe UI",8);using Brush ink=new SolidBrush(Color.FromArgb(140,162,177));
                graphics.DrawString("F10 / Esc to exit cinema",hint,ink,14,Height-25);
            }
            return;
        }

        float availableWidth = Math.Max(1, ClientSize.Width - (PresentationMode ? 8 : 48));
        float availableHeight = Math.Max(1, ClientSize.Height - (PresentationMode ? 8 : 64));
        float scale = Math.Min(availableWidth / 256f, availableHeight / 240f);
        scale = Math.Max(0.1f, scale);
        float width = 256 * scale;
        float height = 240 * scale;
        _gameBounds = new RectangleF(
            (ClientSize.Width - width) / 2f,
            (ClientSize.Height - height) / 2f + (PresentationMode ? 0 : 12),
            width,
            height);

        if(RevealOriginal && NativeScreenPixels is {Length:61440} native)
        {
            if(_printingDepth==0)_gpuStage?.ShowStage(false);
            using Bitmap original=BitmapFromNative(native);
            graphics.InterpolationMode=InterpolationMode.NearestNeighbor;graphics.DrawImage(original,_gameBounds);
            CleanFrameRendered?.Invoke(original);
            return;
        }
        if(Settings.Dimensions.Trails && UI.InterfaceMotion.Enabled && MotionSeconds-_lastEcho>=.12)
        {
            _echoes.Enqueue((_rotation,MotionSeconds));_lastEcho=MotionSeconds;
            while(_echoes.Count>6)_echoes.Dequeue();
        }
        else if(!Settings.Dimensions.Trails) _echoes.Clear();

        DrawStageShadow(graphics, _gameBounds);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        long assemblyStart=System.Diagnostics.Stopwatch.GetTimestamp();
        long assemblyAllocatedStart=PlaybackObserverForTest is null?0:GC.GetAllocatedBytesForCurrentThread();
        LightingMilliseconds=0;
        List<SurfaceGroup> groups = [];
        _accentWriteIndex=0;
        SurfaceGroup background = new();
        if(_backgroundPixels is null)
        {
            long before=PlaybackObserverForTest is null?0:GC.GetAllocatedBytesForCurrentThread();
            _backgroundPixels=UseBackgroundPixelCacheForTest?_backgroundPixelCache.Get(scene.Background):ImagePixels.Read(scene.Background);
            if(PlaybackObserverForTest is not null)_meshBackgroundBytes+=GC.GetAllocatedBytesForCurrentThread()-before;
        }
        background.Quad(new(new(_gameBounds.Left, _gameBounds.Top), 0, 1, 0, 0),
            new(new(_gameBounds.Right, _gameBounds.Top), 0, 1, 1, 0),
            new(new(_gameBounds.Right, _gameBounds.Bottom), 0, 1, 1, 1),
            new(new(_gameBounds.Left, _gameBounds.Bottom), 0, 1, 0, 1), _backgroundPixels, Color.White, 1);
        groups.Add(background);

        using Region oldClip = graphics.Clip;
        graphics.SetClip(_gameBounds);

        IReadOnlyList<SceneObject> items = scene.Objects
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Bounds.Bottom)
            .ToArray();

        bool hudAdded = false;
        int accentCount = 0;
        _trailObjects=0;
        foreach (SceneObject item in items)
        {
            if(scene.PlayerOverlaysFlatHud && item.Kind==SceneObjectKind.Player && item.ProjectionEnabled)continue;
            if (scene.ExactProfile && item.SortOrder >= 100 && !hudAdded) AddFixedHud();
            AddStyledObject(groups,item,scale,ref accentCount);
        }
        if (scene.ExactProfile && !hudAdded) AddFixedHud();
        foreach (Rectangle r in scene.FlatRegions)
        {
            SurfaceGroup panel = new();
            PointF a = new(_gameBounds.Left+r.Left*scale, _gameBounds.Top+r.Top*scale);
            PointF b = new(_gameBounds.Left+r.Right*scale, _gameBounds.Top+r.Bottom*scale);
            panel.Quad(new(new(a.X,a.Y),0,1,r.Left/256f,r.Top/240f), new(new(b.X,a.Y),0,1,r.Right/256f,r.Top/240f),
                new(new(b.X,b.Y),0,1,r.Right/256f,r.Bottom/240f),new(new(a.X,b.Y),0,1,r.Left/256f,r.Bottom/240f),_backgroundPixels,Color.White,1);
            groups.Add(panel);
        }
        if(scene.PlayerOverlaysFlatHud)
            foreach(var item in items.Where(o=>o.Kind==SceneObjectKind.Player && o.ProjectionEnabled))
                AddStyledObject(groups,item,scale,ref accentCount);
        void AddFixedHud()
        {
            SurfaceGroup hud = new();
            float bottom = _gameBounds.Top + 32 * scale;
            hud.Quad(new(new(_gameBounds.Left, _gameBounds.Top), 0, 1, 0, 0), new(new(_gameBounds.Right, _gameBounds.Top), 0, 1, 1, 0),
                new(new(_gameBounds.Right, bottom), 0, 1, 1, 32f / 240), new(new(_gameBounds.Left, bottom), 0, 1, 0, 32f / 240), _backgroundPixels, Color.White, 1);
            groups.Add(hud); hudAdded = true;
        }
        SceneAssemblyMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(assemblyStart).TotalMilliseconds;
        long assemblyAllocated=PlaybackObserverForTest is null?0:GC.GetAllocatedBytesForCurrentThread()-assemblyAllocatedStart;
        SurfaceTriangleCount=0;
        foreach(var group in groups)SurfaceTriangleCount+=group.Triangles.Count;
        if(!TryPresentGroups(groups,items))
        {
            if(_printingDepth==0)_gpuStage?.ShowStage(false);
            Bitmap stage=RenderGroups(groups,_gameBounds);
            graphics.DrawImage(stage, _gameBounds);
            CleanFrameRendered?.Invoke(stage);
        }
        foreach (SceneObject item in items)
        {
            if (ReferenceEquals(item, _hovered) || item.PresentationKey == SelectedObjectKey)
                DrawHyperframe(graphics, item, CreateContext(item, scale), true);
        }
        graphics.SetClip(oldClip, CombineMode.Replace);

        if (!PresentationMode) { DrawFrameChrome(graphics, scene); if(!GuidedInterface)DrawCompass(graphics); }
        else if(CinematicHint && _gameBounds.Left>125)
        {
            using Font hint=new("Segoe UI",8);using Brush ink=new SolidBrush(Color.FromArgb(140,162,177));
            graphics.DrawString("CINEMA\nF10 / Esc to exit",hint,ink,12,Height-45);
        }
        if (_hovered is not null && ShowLabels)
        {
            DrawObjectLabel(graphics, _hovered, scale);
        }
        LastDrawMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds;
        PerformanceMetrics.Drawing.Add(LastDrawMilliseconds);
        int oldLevel=_adaptive.Level;
        _adaptive.Sample(LastDrawMilliseconds,Settings.Dimensions.AdaptiveQuality,Settings.Dimensions.TargetFps);
        if(oldLevel!=_adaptive.Level)ClearSurfaceCache();
        if(PlaybackObserverForTest is { } observer && DirectGpuStageShown && GpuPresentedFrames>presentedBefore)
            observer(new(System.Diagnostics.Stopwatch.GetTimestamp(),LastDrawMilliseconds,SceneAssemblyMilliseconds,
                LightingMilliseconds,GpuOrderingMs,GpuPackingMs,GpuSubmissionMs,GpuPresentMs,SurfaceTriangleCount,
                _gpu?.LastAtlasMs??0,_gpu?.LastDriverMs??0,GC.GetAllocatedBytesForCurrentThread()-drawAllocatedStart,assemblyAllocated));
    }

    private SurfaceGroup GetSurfaces(SceneObject item, float scale)
    {
        ObjectMotion? objectMotion=Settings.Dimensions.ObjectMotions.GetValueOrDefault(item.PresentationKey) ?? Settings.Dimensions.ClassMotions.GetValueOrDefault(item.Kind.ToString());
        bool dynamicRotation=UI.InterfaceMotion.Enabled&&item.ProjectionEnabled&&(Settings.Dimensions.AudioReactive||Settings.Dimensions.Choreography&&objectMotion is {Style:not MotionStyle.Still,Strength:>0,Speed:>0});
        if(Morphing || dynamicRotation || _drawingShowcase && UI.InterfaceMotion.Enabled)
        {
            _animatedSurfaces.TryGetValue(item.PresentationKey,out var reusable);
            if(!ReuseMeshStorageForTest)reusable=null;
            var moving=BuildSurfaces(item,scale,reusable);
            if(_animatedSurfaces.Count>=256)_animatedSurfaces.Clear();
            _animatedSurfaces[item.PresentationKey]=moving;
            ApplyLighting(item,moving);
            return moving;
        }
        GeometrySettings geometry = Settings.GeometryFor(item);
        double geometryTime = Settings.Animate && geometry.Animate && geometry.Mode != GeometryMode.Hyperprism ? GeometryCycleSeconds : 0;
        SurfaceCacheKey key = new(item.PresentationKey, item.Pixels.Key, item.Bounds.Size, _gameBounds.Size,
            item.ProjectionEnabled, item.Depth, DepthAmount, Perspective, ProjectionOpacity, SliceCount,
            ProjectionRotationSpreadDegrees, ProjectionCycleSeconds, geometryTime, _rotation);
        PointF position = ToScreen(item.Bounds, scale).Location;
        if (!_surfaceCache.TryGetValue(key, out (SurfaceGroup Group, PointF Position) entry))
        {
            // Animated frames cannot reuse an object's previous rotation mesh.
            // Retain only its newest mesh rather than several stale time slices.
            SurfaceGroup? reusable=null;
            if(_lastSurfaceKeys.TryGetValue(item.PresentationKey,out var previousKey) && _surfaceCache.Remove(previousKey,out var previousEntry))reusable=previousEntry.Group;
            if(!ReuseMeshStorageForTest)reusable=null;
            if (_surfaceCache.Count >= 256) { if(PlaybackObserverForTest is not null)_meshOverflowClears++;_surfaceCache.Clear();_lastSurfaceKeys.Clear(); }
            _lastSurfaceKeys[item.PresentationKey]=key;
            var built=BuildSurfaces(item,scale,reusable);
            ApplyLighting(item,built);
            entry = (built, position); _surfaceCache[key] = entry;
        }
        else SurfaceCacheHits++;
        entry.Group.Offset = new(position.X - entry.Position.X, position.Y - entry.Position.Y);
        return entry.Group;
    }

    private void ApplyLighting(SceneObject item,SurfaceGroup group)
    {
        if(!(Settings.Effects.Enabled&&item.ProjectionEnabled&&DepthAmount>.005f))return;
        long start=System.Diagnostics.Stopwatch.GetTimestamp();
        SurfaceEffects.Light(group,Settings.Effects.Lighting,UsePackedLightingForTest);
        LightingMilliseconds+=System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private Rotation4D EffectiveRotation
    {
        get
        {
            Rotation4D r=_echoRotation ?? _rotation;
            if(Morphing)
            {
                float p=StyleBlend;float Angle(float a,float b)=>a+WrapAngle(b-a)*p;
                r=new(Angle(_previousRotation.XW,r.XW),Angle(_previousRotation.YW,r.YW),Angle(_previousRotation.ZW,r.ZW),Angle(_previousRotation.XZ,r.XZ),Angle(_previousRotation.YZ,r.YZ),Angle(_previousRotation.XY,r.XY));
            }
            if(_drawingShowcase && UI.InterfaceMotion.Enabled && !_dragging)
            {
                float t=(float)_welcomeClock.Elapsed.TotalSeconds;
                r=r with{XW=r.XW+MathF.Sin(t*.3f)*.35f+_pointer.X*.12f,YW=r.YW+MathF.Cos(t*.23f)*.28f+_pointer.Y*.12f,ZW=r.ZW+t*.08f};
            }
            return r;
        }
    }

    private void AddStyledObject(List<SurfaceGroup> groups,SceneObject item,float scale,ref int count)
    {
        if(Settings.Dimensions.Trails && UI.InterfaceMotion.Enabled && _adaptive.Level<2 && _trailObjects<8 && item.ProjectionEnabled && item.Image.Width*item.Image.Height<=4096 && item.Kind is SceneObjectKind.Player or SceneObjectKind.Enemy or SceneObjectKind.Item or SceneObjectKind.Sprite)
        {
            _trailObjects++;
            try
            {
                var echoes=_echoes.TakeLast(Settings.Dimensions.TrailLength).ToArray();
                for(int i=0;i<echoes.Length;i++)
                {
                    _echoRotation=echoes[i].Rotation;_echoTime=echoes[i].Time;_buildingEcho=true;
                    SurfaceGroup past=BuildSurfaces(item,scale),echo=new();
                    foreach(var triangle in past.Triangles.Where(t=>t.LayerKey=="Center").Take(256))
                        echo.Triangle(triangle.A,triangle.B,triangle.C,triangle.Texture,triangle.Color,triangle.Opacity*Settings.Dimensions.TrailOpacity*(i+1)/(echoes.Length+1),triangle.LayerKey);
                    groups.Add(echo);
                }
            }
            finally{_echoRotation=null;_echoTime=null;_buildingEcho=false;}
        }
        SurfaceGroup surface=GetSurfaces(item,scale);
        VisualEffectsSettings fx=Settings.Effects;
        bool accent=fx.Enabled && item.ProjectionEnabled && DepthAmount>.005f && count<(_adaptive.Level>=2?2:8) && item.Image.Width*item.Image.Height<=4096 &&
            (item.Kind is SceneObjectKind.Player or SceneObjectKind.Sprite or SceneObjectKind.Enemy or SceneObjectKind.Item || item.Kind==SceneObjectKind.QuestionBlock && count<3 || ReferenceEquals(item,_hovered));
        if(accent && (fx.Glow>.001f||fx.Shadows>.001f))
        {
            count++;
            if(!_haloCache.TryGetValue(item.Pixels.Key,out var mask))
            { if(_haloCache.Count>=32)_haloCache.Clear();_haloCache[item.Pixels.Key]=mask=SurfaceEffects.Halo(item.Pixels); }
            PointF center=CreateContext(item,scale).Center;
            center=new(center.X-surface.Offset.X,center.Y-surface.Offset.Y);
            if(fx.Shadows>.001f) groups.Add(BuildAccent(surface,mask,Color.Black,fx.Shadows*.55f,center,1,new(3*scale,5*scale)));
            float audioGlow=UI.InterfaceMotion.Enabled&&Settings.Dimensions.AudioReactive?1+AudioLevel*Settings.Dimensions.AudioStrength:1;
            if(fx.Glow>.001f && _adaptive.Level<3) groups.Add(BuildAccent(surface,mask,item.Accent,fx.Glow*.4f*audioGlow,center,1.1f,new(0,0)));
        }
        groups.Add(surface);
    }

    private SurfaceGroup BuildAccent(SurfaceGroup source,ImagePixels mask,Color color,float opacity,PointF center,float expansion,PointF offset)
    {
        bool measure=PlaybackObserverForTest is not null;
        long before=measure?GC.GetAllocatedBytesForCurrentThread():0;
        SurfaceGroup? reusable=null;
        if(UseReusableAccentStorageForTest && _accentWriteIndex<16)
        {
            if(_accentWriteIndex==_accentSurfaces.Count)_accentSurfaces.Add(new());
            reusable=_accentSurfaces[_accentWriteIndex++];
        }
        var group=SurfaceEffects.Accent(source,mask,color,opacity,center,expansion,offset,reusable);
        if(measure){_meshAccentGroups++;_meshAccentTriangles+=group.Triangles.Count;_meshAccentBytes+=GC.GetAllocatedBytesForCurrentThread()-before;}
        return group;
    }

    private Bitmap RenderGroups(List<SurfaceGroup> groups,RectangleF bounds)
    {
        if(UseGpu && !_gpuFailed)
        {
            _gpu??=new OpenGlSurfaceRenderer();
            _gpu.UseDirectVertexWritesForTest=UseDirectVertexWritesForTest;
            _gpu.UseCachedTextureInventoryForTest=UseCachedTextureInventoryForTest;
            _gpu.UseStreamingVertexBufferForTest=UseStreamingVertexBufferForTest;
            try {return _gpu.Available?_gpu.Render(groups,bounds,Math.Max(1,Settings.RenderScale-_adaptive.Level)):_software.Render(groups,bounds);}
            catch(InvalidOperationException){_gpuFailed=true;}
        }
        return _software.Render(groups,bounds);
    }

    private bool TryPresentGroups(List<SurfaceGroup> groups,IReadOnlyList<SceneObject> items)
    {
        // Editor overlays and exports retain their existing bitmap path. Capture
        // due recording frames from the source texture without hiding the stage.
        if(!DirectGpuPresentation || !UseGpu || _gpuFailed || _directGpuFailed || _printingDepth>0 || !Visible || !IsHandleCreated ||
            PaintObjects || EditLayers || _hovered is not null || items.Any(item=>item.PresentationKey==SelectedObjectKey))
        {
            if(PlaybackObserverForTest is not null)
                LastDirectFallbackForTest=$"direct={DirectGpuPresentation},gpu={UseGpu},gpuFailed={_gpuFailed},directFailed={_directGpuFailed},printing={_printingDepth},visible={Visible},handle={IsHandleCreated},paint={PaintObjects},edit={EditLayers},hover={_hovered is not null},selection={items.Any(item=>item.PresentationKey==SelectedObjectKey)},recording={CleanFrameRendered is not null}";
            return false;
        }
        try
        {
            _gpu??=new OpenGlSurfaceRenderer();
            _gpu.UseDirectVertexWritesForTest=UseDirectVertexWritesForTest;
            _gpu.UseCachedTextureInventoryForTest=UseCachedTextureInventoryForTest;
            _gpu.UseStreamingVertexBufferForTest=UseStreamingVertexBufferForTest;
            if(!_gpu.CanPresentDirectly)return false;
            _gpuStage??=new GpuStageWindow(this);
            _gpuStage.Position(Rectangle.Round(_gameBounds));
            bool capture=CleanFrameRendered is not null && (NeedsCleanFrame?.Invoke()??true);
            Bitmap? stage=_gpu.Present(groups,_gameBounds,Math.Max(1,Settings.RenderScale-_adaptive.Level),_gpuStage,capture);
            _gpuStage.ShowStage(true);
            if(stage is not null)CleanFrameRendered?.Invoke(stage);
            return true;
        }
        catch(Exception exception) when(exception is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        { _directGpuFailed=true;DirectGpuFailureForTest=exception.ToString();_gpuStage?.ShowStage(false);return false; }
    }

    private SurfaceGroup BuildSurfaces(SceneObject item,float scale,SurfaceGroup? reusable=null)
    {
        bool measure=PlaybackObserverForTest is not null;
        long allocatedStart=measure?GC.GetAllocatedBytesForCurrentThread():0;
        SurfaceGroup group=reusable??new();
        long trianglesBefore=group.TrianglesAllocatedForTest,arraysBefore=group.VertexArraysAllocatedForTest,elementsBefore=group.VertexElementsAllocatedForTest;
        group.UseReusableOrderStorageForTest=UseReusableOrderStorageForTest;
        group.BeginUpdate();
        SurfaceGroup built=BuildSurfacesCore(item,scale,group);
        group.EndUpdate();
        if(measure)
        {
            if(reusable is null)_meshNewGroups++;else _meshReusedGroups++;
            _meshNewTriangles+=group.TrianglesAllocatedForTest-trianglesBefore;
            _meshVertexArrays+=group.VertexArraysAllocatedForTest-arraysBefore;
            _meshVertexElements+=group.VertexElementsAllocatedForTest-elementsBefore;
            _meshBuildBytes+=GC.GetAllocatedBytesForCurrentThread()-allocatedStart;
        }
        return built;
    }

    private SurfaceGroup BuildSurfacesCore(SceneObject item, float scale,SurfaceGroup group)
    {
        if (!item.ProjectionEnabled || DepthAmount <= 0.005f)
        {
            RectangleF rectangle = ToScreen(item.Bounds, scale);
            group.Quad(new(new(rectangle.Left, rectangle.Top), 0, 1, 0, 0), new(new(rectangle.Right, rectangle.Top), 0, 1, 1, 0),
                new(new(rectangle.Right, rectangle.Bottom), 0, 1, 1, 1), new(new(rectangle.Left, rectangle.Bottom), 0, 1, 0, 1), item.Pixels, Color.White, 1);
            return group;
        }
        ProjectionContext context = CreateContext(item, scale);
        List<Sheet4D> sheets = CreateSheets(context);
        GeometrySettings geometry = Settings.GeometryFor(item);
        if(geometry.Mode==GeometryMode.Slicing && !GeometryBlending)return BuildSolidSection(item,context,geometry);
        if (geometry.Mode != GeometryMode.Hyperprism || GeometryBlending && !HasGeometryOverride(item)) return BuildGeometrySurfaces(item, context, sheets, geometry,group);
        foreach (Sheet4D sheet in sheets)
        {
            float alpha = sheet.Opacity ?? (sheet.IsOriginal ? 1 : ProjectionOpacity);
            group.Quad(Vertex(-context.HalfX, -context.HalfY, sheet, 0, 0), Vertex(context.HalfX, -context.HalfY, sheet, 1, 0),
                Vertex(context.HalfX, context.HalfY, sheet, 1, 1), Vertex(-context.HalfX, context.HalfY, sheet, 0, 1), item.Pixels, Color.White, alpha, sheet.Key);
        }
        Sheet4D[] wSheets = sheets.Where(s => s.Key == "Center" || s.Key.StartsWith("W:")).OrderBy(s => s.W).ToArray();
        Sheet4D[] zSheets = sheets.Where(s => s.Key == "Center" || s.Key.StartsWith("Z:")).OrderBy(s => s.Z).ToArray();
        var topology=item.PixelGeometry.Topology;
        PointF[] localPoints=item.PixelGeometry.LocalPoints(item.Image.Width,item.Image.Height,context.HalfX,context.HalfY);
        Dictionary<string,SurfaceVertex[]> edgeVertices=[];
        Connect(wSheets); Connect(zSheets);
        return group;

        SurfaceVertex Vertex(float x, float y, Sheet4D sheet, float u = 0, float v = 0)
        {
            Projected4D p = FourDMath.Project(new(x, y, sheet.Z, sheet.W), sheet.Rotation, context.Camera4D, context.Camera3D);
            return new(new(context.Center.X + p.Point.X * scale, context.Center.Y + p.Point.Y * scale),
                p.CameraDepth / context.Camera3D, p.Scale4D, u, v);
        }
        void Connect(Sheet4D[] neighbors)
        {
            for (int index = 1; index < neighbors.Length; index++)
            {
                Sheet4D a = neighbors[index - 1], b = neighbors[index];
                float alpha = Math.Min(a.Opacity ?? (a.IsOriginal ? 1 : ProjectionOpacity), b.Opacity ?? (b.IsOriginal ? 1 : ProjectionOpacity));
                if (alpha <= 0.001f) continue;
                SurfaceVertex[] va=EdgeVertices(a),vb=EdgeVertices(b);
                for(int e=0;e<topology.Edges.Length;e++)
                {
                    var edge=topology.Edges[e];
                    group.Quad(va[edge.Start],va[edge.End],vb[edge.End],vb[edge.Start],null,topology.Colors[e],alpha);
                }
            }
        }
        SurfaceVertex[] EdgeVertices(Sheet4D sheet)
        {
            if(edgeVertices.TryGetValue(sheet.Key,out var vertices))return vertices;
            vertices=group.VertexBuffer(sheet.Key,topology.Points.Length);
            if(UseBatchedProjectionForTest)
                sheet.Rotation.ProjectVertices(localPoints,sheet.Z,sheet.W,context.Camera4D,context.Camera3D,context.Center,scale,vertices);
            else for(int i=0;i<vertices.Length;i++)
            {
                var p=topology.Points[i];vertices[i]=Vertex(PixelToLocalX(p.X,item.Image.Width,context),PixelToLocalY(p.Y,item.Image.Height,context),sheet);
            }
            edgeVertices[sheet.Key]=vertices;return vertices;
        }
    }

    private SurfaceGroup BuildGeometrySurfaces(SceneObject item, ProjectionContext context, List<Sheet4D> sheets, GeometrySettings geometry,SurfaceGroup group)
    {
        float phase = Settings.Animate ? geometry.Sample(GeometryCycleSeconds) : geometry.Phase;
        // Bounded tessellation independent of sprite size / per-pixel contours.
        int columns = geometry.Mode is GeometryMode.Hypersphere or GeometryMode.Duocylinder ? Math.Clamp(item.Image.Width / 2, 8, 16) : 8;
        int rows = geometry.Mode is GeometryMode.Hypersphere or GeometryMode.Duocylinder ? Math.Clamp(item.Image.Height / 4, 4, 8) : 4;
        // Linear modes need just one exact quad. Small NES tiles need less curved
        // tessellation than large artwork, keeping dense terrain scenes affordable.
        if (geometry.Mode is GeometryMode.PerspectiveLens or GeometryMode.Slicing or GeometryMode.Unfolding) { columns = 1; rows = 1; }
        bool morph=GeometryBlending && !HasGeometryOverride(item);
        float morphBlend=GeometryBlend;
        if(morph) { columns=item.Image.Width<=16?4:8;rows=item.Image.Height<=16?2:4; }
        if(_adaptive.Level>=1){columns=Math.Max(1,columns/2);rows=Math.Max(1,rows/2);}
        foreach (Sheet4D sheet in sheets)
        {
            if (geometry.Mode is GeometryMode.Hypersphere or GeometryMode.Ribbon && sheet.Key.StartsWith("Z:")) continue;
            float alpha = sheet.Opacity ?? (sheet.IsOriginal ? 1 : ProjectionOpacity);
            if (alpha <= .001f) continue;
            if (geometry.Mode == GeometryMode.Unfolding && phase < .001f && !sheet.IsOriginal) continue;
            if(geometry.Mode!=GeometryMode.Slicing)
            {
                // Adjacent cells share vertices. Map/project each grid point once,
                // instead of rebuilding four corners and two polygon lists per cell.
                int stride=columns+1;
                Vector4F[]? mapped=UseGeometryMapCacheForTest?(_geometryMapCache??=new()).Get(geometry.Mode,columns,rows,sheet.Z,sheet.W,
                    context.HalfX,context.HalfY,context.HalfZ,context.HalfW,geometry.Amount,phase):null;
                Vector4F[]? previousMapped=UseGeometryMapCacheForTest&&morph&&SourceGeometry is {} sourceGeometry?
                    _geometryMapCache!.Get(sourceGeometry.Mode,columns,rows,sheet.Z,sheet.W,context.HalfX,context.HalfY,
                        context.HalfZ,context.HalfW,sourceGeometry.Amount,sourceGeometry.Phase):null;
                SurfaceVertex[] grid=System.Buffers.ArrayPool<SurfaceVertex>.Shared.Rent(stride*(rows+1));
                try
                {
                    if(mapped is null)
                        for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)grid[y*stride+x]=Project(Point(x/(float)columns,y/(float)rows));
                    else for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
                    {
                        float u=x/(float)columns,v=y/(float)rows;
                        Vector4F p=Transform(mapped[y*stride+x],geometry.Mode,phase);
                        if(previousMapped is not null && SourceGeometry is {} prior)
                        {
                            Vector4F old=Transform(previousMapped[y*stride+x],prior.Mode,prior.Phase);
                            p=new(old.X+(p.X-old.X)*morphBlend,old.Y+(p.Y-old.Y)*morphBlend,
                                old.Z+(p.Z-old.Z)*morphBlend,old.W+(p.W-old.W)*morphBlend);
                        }
                        grid[y*stride+x]=Project(new(p,u,v));
                    }
                    for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)
                    {
                        int a=y*stride+x;
                        group.Quad(grid[a],grid[a+1],grid[a+stride+1],grid[a+stride],item.Pixels,Color.White,alpha,sheet.Key);
                    }
                }
                finally{System.Buffers.ArrayPool<SurfaceVertex>.Shared.Return(grid);}
                continue;
            }
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                GeometryPoint a = Point(x / (float)columns, y / (float)rows), b = Point((x + 1f) / columns, y / (float)rows),
                    c = Point((x + 1f) / columns, (y + 1f) / rows), d = Point(x / (float)columns, (y + 1f) / rows);
                Emit([a, b, c]); Emit([a, c, d]);
            }
            GeometryPoint Point(float u, float v)
            {
                Vector4F p = Geometry4D.Map(geometry.Mode, u, v, sheet.Z, sheet.W, context.HalfX, context.HalfY,
                    context.HalfZ, context.HalfW, geometry.Amount, phase);
                p=Transform(p,geometry.Mode,phase);
                if(morph && SourceGeometry is { } previous)
                {
                    Vector4F old=Geometry4D.Map(previous.Mode,u,v,sheet.Z,sheet.W,context.HalfX,context.HalfY,context.HalfZ,context.HalfW,previous.Amount,previous.Phase);
                    old=Transform(old,previous.Mode,previous.Phase);
                    p=new(old.X+(p.X-old.X)*morphBlend,old.Y+(p.Y-old.Y)*morphBlend,old.Z+(p.Z-old.Z)*morphBlend,old.W+(p.W-old.W)*morphBlend);
                }
                return new(p, u, v);
            }
            Vector4F Transform(Vector4F p,GeometryMode mode,float unfolding)
            {
                if (mode == GeometryMode.Unfolding)
                {
                    Vector4F rotated = sheet.Rotation.Apply(p); float blend = unfolding;
                    p = new(p.X + (rotated.X - p.X) * blend, p.Y + (rotated.Y - p.Y) * blend,
                        p.Z + (rotated.Z - p.Z) * blend, p.W + (rotated.W - p.W) * blend);
                }
                else p = sheet.Rotation.Apply(p);
                return p;
            }
            void Emit(List<GeometryPoint> polygon)
            {
                if (geometry.Mode == GeometryMode.Slicing)
                {
                    float extent = MathF.Sqrt(context.HalfX * context.HalfX + context.HalfY * context.HalfY + context.HalfZ * context.HalfZ + context.HalfW * context.HalfW);
                    polygon = Geometry4D.ClipSlab(polygon, (phase * 2 - 1) * extent, Math.Max(.05f, extent * (.015f + geometry.Amount * .22f)));
                }
                if (polygon.Count < 3) return;
                SurfaceVertex first = Project(polygon[0]);
                for (int i = 1; i + 1 < polygon.Count; i++)
                    group.Triangle(first, Project(polygon[i]), Project(polygon[i + 1]), item.Pixels, Color.White, alpha, sheet.Key);
            }
            SurfaceVertex Project(GeometryPoint p)
            {
                float camera = context.Camera4D;
                float factor=geometry.Mode==GeometryMode.PerspectiveLens?1-geometry.Amount*(.3f+phase*.4f):1;
                if(morph && SourceGeometry is {} source)
                {
                    float oldFactor=source.Mode==GeometryMode.PerspectiveLens?1-source.Amount*(.3f+source.Phase*.4f):1;
                    factor=oldFactor+(factor-oldFactor)*morphBlend;
                }
                camera*=factor;
                Projected4D projected = FourDMath.ProjectRotated(p.Position, camera, context.Camera3D);
                return new(new(context.Center.X + projected.Point.X * context.ScreenScale, context.Center.Y + projected.Point.Y * context.ScreenScale),
                    projected.CameraDepth / context.Camera3D, projected.Scale4D, p.U, p.V);
            }
        }
        return group;
    }

    internal SurfaceGroup GeometrySurfacesForTest(SceneObject item) => BuildSurfaces(item, 1);
    internal SurfaceGroup RebuildSurfacesForTest(SceneObject item,SurfaceGroup? reusable)=>BuildSurfaces(item,1,reusable);

    private bool HasGeometryOverride(SceneObject item)=>Settings.ObjectGeometries.ContainsKey(item.PresentationKey)||Settings.ClassGeometries.ContainsKey(item.Kind.ToString());
    private SurfaceGroup BuildSolidSection(SceneObject item,ProjectionContext context,GeometrySettings geometry)
    {
        LayerSettings? centerLayer=Settings.LayerFor(item.PresentationKey,"Center");
        if(centerLayer?.Enabled==false)return new();
        Sheet4D centerSheet=NewSheet(context,new("Center",0,0,true,false));
        PreparedRotation4D sectionRotation=centerSheet.Rotation;
        float phase=Settings.Animate?geometry.Sample(GeometryCycleSeconds):geometry.Phase;
        float extent=0;
        for(int i=0;i<16;i++)
        {
            var p=sectionRotation.Apply(new((i&1)==0?-context.HalfX:context.HalfX,(i&2)==0?-context.HalfY:context.HalfY,(i&4)==0?-context.HalfZ:context.HalfZ,(i&8)==0?-context.HalfW:context.HalfW));
            extent=Math.Max(extent,Math.Abs(p.W));
        }
        var faces=SolidSection4D.Intersect(context.HalfX,context.HalfY,context.HalfZ,context.HalfW,sectionRotation,(phase*2-1)*extent);
        SurfaceGroup result=new();
        foreach(var face in faces)
            for(int i=1;i+1<face.Count;i++)result.Triangle(Project(face[0]),Project(face[i]),Project(face[i+1]),item.Pixels,Color.White,centerSheet.Opacity??1,"Center");
        return result;
        SurfaceVertex Project(GeometryPoint p)
        {
            var projected=FourDMath.ProjectRotated(p.Position,context.Camera4D,context.Camera3D);
            return new(new(context.Center.X+projected.Point.X*context.ScreenScale,context.Center.Y+projected.Point.Y*context.ScreenScale),projected.CameraDepth/context.Camera3D,projected.Scale4D,Math.Clamp(p.U,0,.99999f),Math.Clamp(p.V,0,.99999f));
        }
    }
    private static unsafe Bitmap BitmapFromNative(int[] pixels)
    {
        Bitmap bitmap=new(256,240,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data=bitmap.LockBits(new(0,0,256,240),System.Drawing.Imaging.ImageLockMode.WriteOnly,bitmap.PixelFormat);
        try {fixed(int* source=pixels)for(int y=0;y<240;y++)Buffer.MemoryCopy(source+y*256,(byte*)data.Scan0+y*data.Stride,1024,1024);}
        finally{bitmap.UnlockBits(data);}return bitmap;
    }

    internal Dictionary<string, Vector4F> LayerTransformsForTest(SceneObject item) => CreateSheets(CreateContext(item, 1))
        .ToDictionary(sheet => sheet.Key, sheet => sheet.Rotation.Apply(new(3, 5, sheet.Z, sheet.W)));

    private ProjectionContext CreateContext(SceneObject item, float screenScale)
    {
        float minimumDimension = Math.Max(6f, Math.Min(item.Bounds.Width, item.Bounds.Height));
        float maximumDimension = Math.Max(item.Bounds.Width, item.Bounds.Height);
        float strength = Math.Clamp(Morphing ? _previousDepth+(DepthAmount-_previousDepth)*StyleBlend : DepthAmount, 0, 1.35f) * item.Depth;
        float halfZ = minimumDimension * 0.30f * strength;
        float halfW = minimumDimension * 0.62f * strength;

        // A higher perspective value moves both cameras closer to the object.
        float perspective = Math.Clamp(Morphing ? _previousPerspective+(Perspective-_previousPerspective)*StyleBlend : Perspective, 0, 1);
        float camera4D = maximumDimension * (5.2f - perspective * 3.5f) + halfW * 1.15f;
        float camera3D = maximumDimension * (7.0f - perspective * 4.2f) + halfZ * 1.25f;
        PointF center = new(
            _gameBounds.X + (item.Bounds.Left + item.Bounds.Width / 2f) * screenScale,
            _gameBounds.Y + (item.Bounds.Top + item.Bounds.Height / 2f) * screenScale);
        float rotationStrength = Math.Clamp(item.Depth, 0.22f, 1.25f);
        Rotation4D renderRotation = EffectiveRotation;
        if(Settings.Dimensions.Choreography && UI.InterfaceMotion.Enabled)
        {
            ObjectMotion? motion=Settings.Dimensions.ObjectMotions.GetValueOrDefault(item.PresentationKey) ?? Settings.Dimensions.ClassMotions.GetValueOrDefault(item.Kind.ToString());
            if(motion is not null)renderRotation=DimensionalMotion.Add(renderRotation,DimensionalMotion.Offset(motion,item.PresentationKey,MotionSeconds*Settings.CycleSpeed));
        }
        if(Settings.Dimensions.AudioReactive && UI.InterfaceMotion.Enabled)
            renderRotation=renderRotation with {XW=renderRotation.XW+Math.Clamp(AudioLevel,0,1)*Settings.Dimensions.AudioStrength*.35f,ZW=renderRotation.ZW+Math.Clamp(AudioLevel,0,1)*Settings.Dimensions.AudioStrength*.2f};
        Rotation4D objectRotation = new(
            XW: renderRotation.XW * rotationStrength,
            YW: renderRotation.YW * rotationStrength,
            ZW: renderRotation.ZW * rotationStrength,
            XZ: renderRotation.XZ * rotationStrength,
            YZ: renderRotation.YZ * rotationStrength,
            XY: renderRotation.XY * rotationStrength);

        return new ProjectionContext(
            item.Bounds.Width / 2f,
            item.Bounds.Height / 2f,
            halfZ,
            halfW,
            camera4D,
            camera3D,
            center,
            screenScale,
            item.PresentationKey,
            objectRotation,
            new PreparedRotation4D(objectRotation));
    }

    private List<Sheet4D> CreateSheets(ProjectionContext context)
    {
        List<SheetDescriptor> descriptors = UseCachedSheetDescriptorsForTest?new(12):[];

        // The two Z extrema expose the third spatial axis. W extrema and optional
        // interior samples below expose the fourth without drawing all four
        // redundant Z/W corner combinations for every object.
        descriptors.Add(new SheetDescriptor("Z:-1", -context.HalfZ, 0, IsOriginal: false, IsBoundary: true));
        descriptors.Add(new SheetDescriptor("Z:+1", context.HalfZ, 0, IsOriginal: false, IsBoundary: true));

        // Interior W cross-sections expose how a 3D slice changes across the fourth axis.
        int slices = Math.Clamp(SliceCount-(_adaptive.Level>=3?2:0), 2, 9);
        float[] coordinates = UseCachedSheetDescriptorsForTest?SheetCoordinates:[-1, 1, 0, -0.5f, 0.5f, -0.25f, 0.25f, -0.75f, 0.75f];
        for (int index = 0; index < slices; index++)
        {
            float coordinate = coordinates[index];
            float w = coordinate * context.HalfW;
            if (Math.Abs(w) < context.HalfW * 0.08f)
            {
                continue;
            }
            string key = UseCachedSheetDescriptorsForTest?SheetKeys[index]:"W:" + coordinate.ToString("+0.##;-0.##;0", System.Globalization.CultureInfo.InvariantCulture);
            descriptors.Add(new SheetDescriptor(key, 0, w, IsOriginal: false, IsBoundary: false));
        }

        // The actual NES pixels occupy the central XY cross-section of the 4D prism.
        descriptors.Add(new SheetDescriptor("Center", 0, 0, IsOriginal: true, IsBoundary: false));

        List<Sheet4D> sheets = new(descriptors.Count);
        for (int index = 0; index < descriptors.Count; index++)
        {
            SheetDescriptor descriptor = descriptors[index];
            if(_buildingEcho&&!descriptor.IsOriginal)continue;
            LayerSettings? layer = Settings.LayerFor(context.ObjectKey, descriptor.Key);
            if (layer?.Enabled == false) continue;
            if (layer is not null)
            {
                descriptor = descriptor with { Z = descriptor.Z * layer.DepthPercent / 100f, W = descriptor.W * layer.DepthPercent / 100f };
                if (descriptor.IsOriginal) descriptor = descriptor with { Z = context.HalfZ * (layer.DepthPercent - 100) / 100f };
            }
            sheets.Add(NewSheet(context, descriptor));
        }
        return sheets;
    }

    private Sheet4D NewSheet(
        ProjectionContext context,
        SheetDescriptor descriptor)
    {
        LayerSettings? layer = Settings.LayerFor(context.ObjectKey, descriptor.Key);
        Rotation4D angles = (descriptor.IsOriginal && layer is null) || layer?.UseAutomaticRotation == false
            ? context.BaseRotation : CreateProjectionRotation(context, descriptor);
        if (layer is not null)
        {
            RotationAngles edit = layer.Rotation;
            angles = new Rotation4D(angles.XW + ToRadians(edit.XW), angles.YW + ToRadians(edit.YW),
                angles.ZW + ToRadians(edit.ZW), angles.XZ + ToRadians(edit.XZ),
                angles.YZ + ToRadians(edit.YZ), angles.XY + ToRadians(edit.XY));
        }
        PreparedRotation4D rotation = new(angles);
        return new Sheet4D(
            descriptor.Key,
            descriptor.Z,
            descriptor.W,
            descriptor.IsOriginal,
            rotation,
            layer is null ? null : layer.UseGlobalOpacity ? ProjectionOpacity : layer.OpacityPercent / 100f);
    }

    private Rotation4D CreateProjectionRotation(
        ProjectionContext context,
        SheetDescriptor descriptor)
    {
        float spread = ToRadians(Math.Clamp(ProjectionRotationSpreadDegrees, 0f, 180f));
        if (spread <= 0.0001f)
        {
            return context.BaseRotation;
        }

        float normalizedZ = context.HalfZ <= 0.0001f ? 0f : descriptor.Z / context.HalfZ;
        float normalizedW = context.HalfW <= 0.0001f ? 0f : descriptor.W / context.HalfW;
        float phase = (normalizedZ * 1.73f) + (normalizedW * 2.41f) + (descriptor.IsBoundary ? 0.83f : 2.19f);
        // Deterministic object phase, independent of render order and layer count.
        uint seed = 2166136261;
        foreach (char character in context.ObjectKey) seed = (seed ^ character) * 16777619;
        phase += (seed % 1000) * 0.00628f;
        LayerSettings? layer = Settings.LayerFor(context.ObjectKey, descriptor.Key);
        float time = (float)(ProjectionCycleSeconds * (layer?.AnimationSpeed ?? 1));
        float speed = 0.38f + Math.Abs(normalizedW) * 0.213f + (normalizedZ + normalizedW) * 0.041f;

        // Each visible projection sheet has its own transform. The distinct
        // phases and rates keep sheets from moving as one rigid stack when the
        // automatic rotation cycle is enabled.
        return new Rotation4D(
            context.BaseRotation.XW + spread * 0.70f * MathF.Sin(phase + time * speed),
            context.BaseRotation.YW + spread * 0.62f * MathF.Sin(phase * 1.37f - time * (speed + 0.13f) + 1.11f),
            context.BaseRotation.ZW + spread * 0.76f * MathF.Cos(phase * 1.83f + time * (speed + 0.23f) + 0.47f),
            context.BaseRotation.XZ + spread * 0.43f * MathF.Sin(phase * 2.17f - time * (speed + 0.31f) + 2.03f),
            context.BaseRotation.YZ + spread * 0.39f * MathF.Cos(phase * 2.53f + time * (speed + 0.19f) + 0.83f),
            context.BaseRotation.XY + spread * 0.31f * MathF.Sin(phase * 2.89f - time * (speed + 0.27f) + 2.71f));
    }

    private void DrawHyperframe(Graphics graphics, SceneObject item, ProjectionContext context, bool hovered)
    {
        if (!hovered)
        {
            return;
        }

        if (item.PresentationKey == SelectedObjectKey && SelectedLayerKey is not null)
        {
            if (Settings.GeometryFor(item.PresentationKey).Mode != GeometryMode.Hyperprism)
            {
                using Pen meshEdge = new(Color.FromArgb(130, 255, 230, 85), 1);
                SurfaceGroup mesh = GetSurfaces(item, context.ScreenScale);
                foreach (SurfaceTriangle triangle in mesh.Triangles.Where(t => t.LayerKey == SelectedLayerKey))
                    graphics.DrawPolygon(meshEdge, new[] { triangle.A.Point, triangle.B.Point, triangle.C.Point }
                        .Select(p => new PointF(p.X + mesh.Offset.X, p.Y + mesh.Offset.Y)).ToArray());
                return;
            }
            Sheet4D? selected = CreateSheets(context).Where(sheet => sheet.Key == SelectedLayerKey).Select(sheet => (Sheet4D?)sheet).FirstOrDefault();
            if (selected is Sheet4D sheet)
            {
                PointF[] corners = [
                    ProjectToScreen(new(-context.HalfX, -context.HalfY, sheet.Z, sheet.W), context, sheet.Rotation),
                    ProjectToScreen(new(context.HalfX, -context.HalfY, sheet.Z, sheet.W), context, sheet.Rotation),
                    ProjectToScreen(new(context.HalfX, context.HalfY, sheet.Z, sheet.W), context, sheet.Rotation),
                    ProjectToScreen(new(-context.HalfX, context.HalfY, sheet.Z, sheet.W), context, sheet.Rotation) ];
                using Pen selectedEdge = new(Color.FromArgb(255, 230, 85), 2) { DashStyle = DashStyle.Dash };
                graphics.DrawPolygon(selectedEdge, corners);
            }
            return;
        }

        Vector4F[] vertices4D = FourDMath.CreateHyperprism(
            context.HalfX,
            context.HalfY,
            context.HalfZ,
            context.HalfW);
        PointF[] projected = vertices4D
            .Select(vertex => ProjectToScreen(vertex, context, context.BasePreparedRotation))
            .ToArray();

        foreach ((int start, int end, int axis) in FourDMath.HyperprismEdges())
        {
            int alpha = axis == 3 ? 205 : 125;
            float width = axis == 3
                ? Math.Max(1f, context.ScreenScale * 0.42f)
                : Math.Max(0.7f, context.ScreenScale * 0.25f);
            Color color = axis == 3
                ? Color.FromArgb(alpha, item.Accent)
                : Color.FromArgb(alpha, 205, 221, 237);
            using Pen edge = new(color, width);
            graphics.DrawLine(edge, projected[start], projected[end]);
        }

        if (hovered)
        {
            float radius = Math.Max(1.5f, context.ScreenScale * 0.7f);
            for (int index = 0; index < projected.Length; index++)
            {
                bool positiveW = (index & 0b1000) != 0;
                Color color = positiveW ? item.Accent : Color.FromArgb(210, 229, 240);
                using Brush point = new SolidBrush(color);
                graphics.FillEllipse(
                    point,
                    projected[index].X - radius,
                    projected[index].Y - radius,
                    radius * 2,
                    radius * 2);
            }
        }
    }

    private PointF ProjectToScreen(
        Vector4F point,
        ProjectionContext context,
        PreparedRotation4D rotation)
    {
        Projected4D projected = FourDMath.Project(point, rotation, context.Camera4D, context.Camera3D);
        return new PointF(
            context.Center.X + projected.Point.X * context.ScreenScale,
            context.Center.Y + projected.Point.Y * context.ScreenScale);
    }

    private RectangleF ToScreen(Rectangle bounds, float scale) => new(
        _gameBounds.X + bounds.X * scale,
        _gameBounds.Y + bounds.Y * scale,
        bounds.Width * scale,
        bounds.Height * scale);

    private static float PixelToLocalX(float pixelX, int imageWidth, ProjectionContext context) =>
        -context.HalfX + (pixelX / imageWidth) * context.HalfX * 2f;

    private static float PixelToLocalY(float pixelY, int imageHeight, ProjectionContext context) =>
        -context.HalfY + (pixelY / imageHeight) * context.HalfY * 2f;

    private static void DrawStageShadow(Graphics graphics, RectangleF stage)
    {
        for (int index = 8; index >= 1; index--)
        {
            using Pen pen = new(Color.FromArgb(8, 105, 255, 189), index * 2f);
            graphics.DrawRectangle(pen, stage.X, stage.Y, stage.Width, stage.Height);
        }
    }

    private void DrawFrameChrome(Graphics graphics, SmbScene scene)
    {
        using Pen border = new(Color.FromArgb(77, 112, 137), 1f);
        graphics.DrawRectangle(border, _gameBounds.X - 1, _gameBounds.Y - 1, _gameBounds.Width + 2, _gameBounds.Height + 2);
        // Architectural corner accents and a short look-change pulse, entirely outside the NES image.
        float pulse = UI.InterfaceMotion.Enabled && _lookPulse.IsRunning ? Math.Max(0, 1 - (float)_lookPulse.Elapsed.TotalSeconds / 1.2f) : 0;
        using Pen accent = new(Color.FromArgb((int)(130 + pulse * 125), 173, 255, 93), 2 + pulse * 2);
        RectangleF rim = RectangleF.Inflate(_gameBounds, 5 + pulse * 5, 5 + pulse * 5);
        float length = 18 + pulse * 12;
        foreach (PointF corner in new[] { new PointF(rim.Left, rim.Top), new PointF(rim.Right, rim.Top), new PointF(rim.Left, rim.Bottom), new PointF(rim.Right, rim.Bottom) })
        {
            float dx = corner.X == rim.Left ? length : -length, dy = corner.Y == rim.Top ? length : -length;
            graphics.DrawLine(accent, corner, new(corner.X + dx, corner.Y));
            graphics.DrawLine(accent, corner, new(corner.X, corner.Y + dy));
        }

        using Font labelFont = new("Segoe UI Semibold", 9f);
        using Brush dim = new SolidBrush(Color.FromArgb(126, 144, 162));
        using Brush bright = new SolidBrush(scene.ExactProfile ? Color.FromArgb(160, 255, 112) : Color.FromArgb(255, 190, 87));
        string profile = GuidedInterface ? "Drag artwork to rotate · Hold F9 for original graphics" : $"{scene.RecognitionProfileName.ToUpperInvariant()} · {scene.ProjectionProfileName.ToUpperInvariant()} · R⁴ LIVE";
        using StringFormat labelFormat = new() { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        graphics.DrawString(profile, labelFont, bright, new RectangleF(_gameBounds.Left, _gameBounds.Top - 40, _gameBounds.Width, 20), labelFormat);
        string location = GuidedInterface ? scene.Location : $"{scene.Location}  ·  XW {AngleXWDegrees:0}°  YW {AngleYWDegrees:0}°  ZW {AngleZWDegrees:0}°";
        graphics.DrawString(location, labelFont, dim, new RectangleF(_gameBounds.Left, _gameBounds.Top - 21, _gameBounds.Width, 20), labelFormat);
    }

    private void DrawObjectLabel(Graphics graphics, SceneObject item, float scale)
    {
        RectangleF objectBounds = ToScreen(item.Bounds, scale);
        using Font font = new("Segoe UI Semibold", 9f);
        string text = item.ProjectionEnabled
            ? $"{item.Label}   XY × Z × W   W {item.Depth:0.00}"
            : $"{item.Label}   2D · disabled by profile";
        SizeF size = graphics.MeasureString(text, font);
        RectangleF bubble = new(
            Math.Clamp(objectBounds.Left, 8, Math.Max(8, ClientSize.Width - size.Width - 22)),
            Math.Max(8, objectBounds.Top - size.Height - 14),
            size.Width + 14,
            size.Height + 8);
        using GraphicsPath path = RoundedRect(bubble, 5);
        using Brush fill = new SolidBrush(Color.FromArgb(228, 13, 18, 27));
        using Pen edge = new(Color.FromArgb(190, item.Accent), 1f);
        using Brush textBrush = new SolidBrush(Color.FromArgb(236, 241, 246));
        graphics.FillPath(fill, path);
        graphics.DrawPath(edge, path);
        graphics.DrawString(text, font, textBrush, bubble.X + 7, bubble.Y + 4);
    }

    private void DrawEmptyState(Graphics graphics)
    {
        using LinearGradientBrush atmosphere = new(ClientRectangle, Color.FromArgb(9, 16, 25), Color.FromArgb(5, 8, 13), 65);
        graphics.FillRectangle(atmosphere, ClientRectangle);
        float cx = ClientSize.Width / 2f, cy = ClientSize.Height / 2f - 80;
        using Pen grid = new(Color.FromArgb(15, 55, 83, 98));
        for (int x = 0; x < Width; x += 44) graphics.DrawLine(grid, x, 0, x, Height);
        for (int y = 0; y < Height; y += 44) graphics.DrawLine(grid, 0, y, Width, y);
        float t = UI.InterfaceMotion.Enabled ? (float)_welcomeClock.Elapsed.TotalSeconds : 0;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Orbital trails make the empty stage feel dimensional without shipping any ROM artwork.
        for (int orbit = 0; orbit < 3; orbit++)
        {
            float radius = 96 + orbit * 27;
            using Pen track = new(Color.FromArgb(26, 87, 179, 204), 1);
            graphics.DrawEllipse(track, cx - radius, cy - radius * .37f, radius * 2, radius * .74f);
            for (int particle = 0; particle < 8; particle++)
            {
                float angle = t * (.13f + orbit * .04f) * (orbit == 1 ? -1 : 1) + particle * MathF.PI / 4 + orbit;
                float x = cx + MathF.Cos(angle) * radius, y = cy + MathF.Sin(angle) * radius * .37f;
                using SolidBrush mote = new(Color.FromArgb(85 + (int)(45 * (1 + MathF.Sin(angle))), orbit == 1 ? 173 : 91, orbit == 1 ? 255 : 187, orbit == 1 ? 93 : 223));
                graphics.FillEllipse(mote, x - 1.5f, y - 1.5f, 3, 3);
            }
        }
        using Pen horizon = new(Color.FromArgb(19, 76, 158, 190));
        float floorY = Math.Min(Height - 16, cy + 250);
        for (int rail = -6; rail <= 6; rail++) graphics.DrawLine(horizon, cx + rail * 16, floorY, cx + rail * 145, Height);
        for (int row = 0; row < 5; row++)
        { float y = floorY + (Height - floorY) * row * row / 25f; graphics.DrawLine(horizon, 0, y, Width, y); }
        DrawHomeShowcase(graphics,cx,cy);
        bool compact=Height<500;
        using Font title = new("Segoe UI Semibold", compact?20f:26f);
        using Font copy = new("Segoe UI", 10f);
        using Brush bright = new SolidBrush(Color.FromArgb(225, 234, 242));
        using Brush dim = new SolidBrush(Color.FromArgb(125, 143, 160));
        string heading = GuidedInterface?"Play your NES in a new dimension.":"Your NES. Another dimension.";
        string body = GuidedInterface?"Open a game or drop a .nes file here.\nThen choose a style below — you can change it live.":"Drag the live sculpture. Choose any style below.\nDrop your NES ROM here to transform the real game.";
        SizeF titleSize = graphics.MeasureString(heading, title);
        SizeF bodySize = graphics.MeasureString(body, copy);
        float centerY = cy + Math.Min(140,Height*.25f)+24;
        graphics.DrawString(heading, title, bright, (ClientSize.Width - titleSize.Width) / 2f, centerY);
        graphics.DrawString(body, copy, dim, (ClientSize.Width - bodySize.Width) / 2f, centerY + (compact?42:52));
        _openBounds = new(cx - 85, centerY + (compact?92:108), 170, 38);
        using Brush action = new SolidBrush(Color.FromArgb(173, 255, 93));
        using GraphicsPath path = RoundedRect(_openBounds, 8); graphics.FillPath(action, path);
        using Brush ink = new SolidBrush(Color.FromArgb(9, 15, 20));
        using StringFormat centered = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(GuidedInterface?"Open game":"OPEN ROM", copy, ink, _openBounds, centered);
        graphics.DrawString("NATIVE · SELF-CONTAINED · NO ROM INCLUDED", copy, dim, new RectangleF(0, centerY + (compact?132:158), Width, 24), centered);
    }

    private void DrawHomeShowcase(Graphics graphics,float cx,float cy)
    {
        _showcase ??= CreateHomeShowcase();
        float height=Math.Min(Height*.5f,280),width=height*256/240;
        _gameBounds=new(cx-width/2,cy-height/2,width,height);
        ClearSurfaceCache();
        using Brush panel=new SolidBrush(Color.FromArgb(100,8,18,27));
        RectangleF card=new(cx-Math.Min(Width*.42f,230),cy-height/2-25,Math.Min(Width*.84f,460),height+38);
        using GraphicsPath shape=RoundedRect(card,16);graphics.FillPath(panel,shape);
        using Pen frame=new(Color.FromArgb(85,67,181,210));graphics.DrawPath(frame,shape);
        double prior=GeometryCycleSeconds;
        _drawingShowcase=true;
        if(UI.InterfaceMotion.Enabled)GeometryCycleSeconds=_welcomeClock.Elapsed.TotalSeconds;
        try
        {
            List<SurfaceGroup> groups=[];SurfaceGroup backdrop=new();
            _showcaseBackground??=ImagePixels.Read(_showcase.Background);
            backdrop.Quad(new(new(_gameBounds.Left,_gameBounds.Top),0,1,0,0),new(new(_gameBounds.Right,_gameBounds.Top),0,1,1,0),new(new(_gameBounds.Right,_gameBounds.Bottom),0,1,1,1),new(new(_gameBounds.Left,_gameBounds.Bottom),0,1,0,1),_showcaseBackground,Color.White,1);
            groups.Add(backdrop);int accents=0;
            foreach(var item in _showcase.Objects)AddStyledObject(groups,item,width/256,ref accents);
            graphics.InterpolationMode=InterpolationMode.NearestNeighbor;
            graphics.DrawImage(RenderGroups(groups,_gameBounds),_gameBounds);
            if(!UseGpu||_gpuFailed||_gpu?.Available!=true)_welcomeTimer.Interval=100;
        }
        finally{_drawingShowcase=false;GeometryCycleSeconds=prior;}
        using Font label=new("Segoe UI Semibold",8);
        using Brush cyan=new SolidBrush(Color.FromArgb(106,220,237));
        graphics.DrawString(GuidedInterface ? "LIVE PREVIEW · NO GAME REQUIRED" : "LIVE R⁴ / "+GeometryCatalog.Names[(int)Settings.Geometry.Mode].ToUpperInvariant(),label,cyan,card.Left+14,card.Top+8);
    }

    private static SmbScene CreateHomeShowcase()
    {
        Bitmap bg=new(256,240);using(Graphics g=Graphics.FromImage(bg))g.Clear(Color.FromArgb(8,15,22));
        Bitmap art=new(40,40);using(Graphics g=Graphics.FromImage(art))
        {
            g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.None;
            using Brush cyan=new SolidBrush(Color.FromArgb(77,217,240));
            using Brush lime=new SolidBrush(Color.FromArgb(173,255,93));
            using Brush violet=new SolidBrush(Color.FromArgb(184,112,255));
            g.FillPolygon(violet,new[]{new Point(20,1),new Point(38,10),new Point(38,30),new Point(20,39),new Point(2,30),new Point(2,10)});
            g.FillPolygon(cyan,new[]{new Point(20,4),new Point(35,12),new Point(20,21),new Point(5,12)});
            g.FillPolygon(lime,new[]{new Point(5,15),new Point(18,23),new Point(18,34),new Point(5,27)});
            g.FillPolygon(Brushes.White,new[]{new Point(22,23),new Point(35,15),new Point(35,27),new Point(22,34)});
        }
        return new(){Background=bg,Objects=[new(){Image=art,Bounds=new(64,48,128,128),Label="Original dimensional glyph",Kind=SceneObjectKind.Player,Accent=Color.FromArgb(87,220,239),Depth=1,ProjectionEnabled=true,IdentityKey="showcase:glyph"}],Location="",ExactProfile=false,RecognitionProfileName="ORIGINAL SHOWCASE / NO ROM",ProjectionProfileName="LIVE LAB"};
    }

    private void DrawCompass(Graphics graphics)
    {
        // Keep the compass beside, never over, the playable NES image.
        if (ClientSize.Width - _gameBounds.Right < 108) return;
        PointF center = new(ClientSize.Width - 62, ClientSize.Height - 74);
        using Font font = new("Segoe UI Semibold", 8);
        Vector4F[] axes = [new(30, 0, 0, 0), new(0, -30, 0, 0), new(0, 0, 30, 0), new(0, 0, 0, 30)];
        Color[] colors = [Color.FromArgb(236, 144, 127), Color.FromArgb(127, 191, 245), Color.FromArgb(216, 166, 244), Color.FromArgb(173, 255, 93)];
        for (int i = 0; i < 4; i++)
        {
            var p = FourDMath.Project(axes[i], _rotation, 90, 105);
            PointF end = new(center.X + p.Point.X, center.Y + p.Point.Y);
            using Pen pen = new(colors[i], 1.6f); using Brush brush = new SolidBrush(colors[i]);
            graphics.DrawLine(pen, center, end); graphics.FillEllipse(brush, end.X - 2, end.Y - 2, 4, 4);
            graphics.DrawString("XYZW"[i].ToString(), font, brush, end.X + 4, end.Y - 8);
        }
        using Brush dim = new SolidBrush(Color.FromArgb(125, 143, 160));
        graphics.DrawString("R⁴ • DRAG", font, dim, center.X - 35, center.Y + 43);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if(PaintObjects && e.Button==MouseButtons.Left && HitTest(e.Location) is {} painted)
        {PaintObjectPicked?.Invoke(painted);return;}
        if (_scene is null && e.Button == MouseButtons.Left && _openBounds.Contains(e.Location)) { OpenRomRequested?.Invoke(); return; }
        if ((e.Button == MouseButtons.Right || EditLayers && e.Button == MouseButtons.Left) && PickProjection(e.Location) is { } picked)
        {
            SelectedObjectKey = picked.ObjectKey; SelectedLayerKey = picked.LayerKey;
            ProjectionPicked?.Invoke(picked.ObjectKey, picked.LayerKey);
            ObjectPicked?.Invoke(picked.ObjectKey);
            if (EditLayers && e.Button == MouseButtons.Left)
            { _dragging = _layerDragging = true; _lastMouse = e.Location; Capture = true; Cursor = Cursors.SizeAll; }
            Invalidate(); return;
        }
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _layerDragging = false; Capture = true;
            _lastMouse = e.Location;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if(IgnorePointerForTest)return;
        _pointer=new(Math.Clamp((e.X-Width/2f)/Math.Max(1,Width/2f),-1,1),Math.Clamp((e.Y-Height/2f)/Math.Max(1,Height/2f),-1,1));
        if (_dragging)
        {
            float deltaX = (e.X - _lastMouse.X) * 0.009f;
            float deltaY = (e.Y - _lastMouse.Y) * 0.009f;
            if (_layerDragging && SelectedObjectKey is string objectKey && SelectedLayerKey is string layerKey)
            {
                LayerSettings layer = Settings.EditLayer(objectKey, layerKey);
                RotationAngles a = layer.Rotation;
                Rotation4D changed = DragRotation(new(ToRadians(a.XW),ToRadians(a.YW),ToRadians(a.ZW),ToRadians(a.XZ),ToRadians(a.YZ),ToRadians(a.XY)),deltaX,deltaY,_dragModifiersForTest??ModifierKeys);
                layer.Rotation = new(){XW=ToDegrees(changed.XW),YW=ToDegrees(changed.YW),ZW=ToDegrees(changed.ZW),XZ=ToDegrees(changed.XZ),YZ=ToDegrees(changed.YZ),XY=ToDegrees(changed.XY)};
                ClearSurfaceCache(); _lastMouse = e.Location; Invalidate(); return;
            }
            _rotation = DragRotation(_rotation,deltaX,deltaY,_dragModifiersForTest??ModifierKeys);
            _lastMouse = e.Location;
            Invalidate();
            RotationChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        SceneObject? previous = _hovered;
        _hovered = HitTest(e.Location);
        if (!ReferenceEquals(previous, _hovered))
        {
            Invalidate();
        }
    }

    internal static Rotation4D DragRotation(Rotation4D rotation,float dx,float dy,Keys modifiers)
    {
        bool ctrl=(modifiers & Keys.Control)!=0,shift=(modifiers & Keys.Shift)!=0;
        if(ctrl && shift) return rotation with {XW=WrapAngle(rotation.XW+dx),ZW=WrapAngle(rotation.ZW+dy)};
        if(ctrl) return rotation with {XZ=WrapAngle(rotation.XZ+dx),YZ=WrapAngle(rotation.YZ+dy)};
        if(shift) return rotation with {YW=WrapAngle(rotation.YW+dx),ZW=WrapAngle(rotation.ZW+dy)};
        return rotation with {XW=WrapAngle(rotation.XW+dx),YW=WrapAngle(rotation.YW+dy)};
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_layerDragging) LayerEdited?.Invoke();
        _layerDragging = false; Capture = false;
        _dragging = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        // Crossing between the native stage and its bitmap-overlay fallback is
        // not actually leaving the game. Avoid show/hide hover oscillation.
        if(_gameBounds.Contains(PointToClient(MousePosition)))return;
        if (!_dragging)
        {
            _hovered = null;
            Invalidate();
        }
    }

    private SceneObject? HitTest(Point point)
    {
        SmbScene? scene = _scene;
        if (scene is null || !_gameBounds.Contains(point)) return null;
        float scale = _gameBounds.Width / 256f;
        Point gamePoint = new(
            (int)((point.X - _gameBounds.X) / scale),
            (int)((point.Y - _gameBounds.Y) / scale));
        if (scene.FlatRegions.Any(r=>r.Contains(gamePoint))) return null;
        return scene.Objects
            .OrderByDescending(item => item.SortOrder)
            .ThenByDescending(item => item.Bounds.Bottom)
            .FirstOrDefault(item => item.Bounds.Contains(gamePoint));
    }

    internal (string ObjectKey, string LayerKey)? PickProjection(PointF point)
    {
        if (_scene is null || !_gameBounds.Contains(point)) return null;
        if (_scene.ExactProfile && point.Y < _gameBounds.Top + _gameBounds.Height * 32 / 240f) return null;
        float scale = _gameBounds.Width / 256f;
        Point gamePoint = new((int)((point.X-_gameBounds.Left)/scale),(int)((point.Y-_gameBounds.Top)/scale));
        if (_scene.FlatRegions.Any(r=>r.Contains(gamePoint))) return null;
        foreach (SceneObject item in _scene.Objects.OrderByDescending(o => o.SortOrder).ThenByDescending(o => o.Bounds.Bottom))
        {
            if (!item.ProjectionEnabled || DepthAmount <= .005f) continue;
            if (Settings.GeometryFor(item.PresentationKey).Mode != GeometryMode.Hyperprism)
            {
                SurfaceTriangle? hit = null; float depth = float.NegativeInfinity;
                SurfaceGroup mesh = GetSurfaces(item, scale);
                PointF localPoint = new(point.X - mesh.Offset.X, point.Y - mesh.Offset.Y);
                foreach (SurfaceTriangle triangle in mesh.Triangles)
                    if (TryPickTriangle(triangle, localPoint, out float candidate) && candidate > depth) { hit = triangle; depth = candidate; }
                if (hit is not null) return (item.PresentationKey, hit.LayerKey);
                continue;
            }
            ProjectionContext context = CreateContext(item, scale);
            string? best = null; float bestDepth = float.NegativeInfinity;
            foreach (Sheet4D sheet in CreateSheets(context))
            {
                if ((sheet.Opacity ?? (sheet.IsOriginal ? 1 : ProjectionOpacity)) <= .001f) continue;
                var a = Vertex(-context.HalfX, -context.HalfY, 0, 0);
                var b = Vertex(context.HalfX, -context.HalfY, 1, 0);
                var c = Vertex(context.HalfX, context.HalfY, 1, 1);
                var d = Vertex(-context.HalfX, context.HalfY, 0, 1);
                Check(a, b, c); Check(a, c, d);
                SurfaceVertex Vertex(float x, float y, float u, float v)
                {
                    Projected4D p = FourDMath.Project(new(x, y, sheet.Z, sheet.W), sheet.Rotation, context.Camera4D, context.Camera3D);
                    return new(new(context.Center.X + p.Point.X * scale, context.Center.Y + p.Point.Y * scale), p.CameraDepth / context.Camera3D, p.Scale4D, u, v);
                }
                void Check(SurfaceVertex p, SurfaceVertex q, SurfaceVertex r)
                {
                    float denominator = (q.Point.Y - r.Point.Y) * (p.Point.X - r.Point.X) + (r.Point.X - q.Point.X) * (p.Point.Y - r.Point.Y);
                    if (Math.Abs(denominator) < .0001f) return;
                    float x = ((q.Point.Y - r.Point.Y) * (point.X - r.Point.X) + (r.Point.X - q.Point.X) * (point.Y - r.Point.Y)) / denominator;
                    float y = ((r.Point.Y - p.Point.Y) * (point.X - r.Point.X) + (p.Point.X - r.Point.X) * (point.Y - r.Point.Y)) / denominator;
                    float z = 1 - x - y;
                    if (x < 0 || y < 0 || z < 0) return;
                    float depth = p.Depth * x + q.Depth * y + r.Depth * z;
                    if (depth <= bestDepth) return;
                    float w = p.Q * x + q.Q * y + r.Q * z;
                    float u = (p.U * p.Q * x + q.U * q.Q * y + r.U * r.Q * z) / w;
                    float v = (p.V * p.Q * x + q.V * q.Q * y + r.V * r.Q * z) / w;
                    int ix = Math.Clamp((int)(u * item.Pixels.Width), 0, item.Pixels.Width - 1);
                    int iy = Math.Clamp((int)(v * item.Pixels.Height), 0, item.Pixels.Height - 1);
                    if ((item.Pixels.Pixels[iy * item.Pixels.Width + ix] & unchecked((int)0xff000000)) == 0) return;
                    best = sheet.Key; bestDepth = depth;
                }
            }
            if (best is not null) return (item.PresentationKey, best);
        }
        return null;
    }
    private static bool TryPickTriangle(SurfaceTriangle triangle, PointF point, out float depth)
    {
        depth = float.NegativeInfinity;
        if (triangle.Opacity <= .001f || triangle.Texture is not ImagePixels texture) return false;
        SurfaceVertex a = triangle.A, b = triangle.B, c = triangle.C;
        float denominator = (b.Point.Y - c.Point.Y) * (a.Point.X - c.Point.X) + (c.Point.X - b.Point.X) * (a.Point.Y - c.Point.Y);
        if (Math.Abs(denominator) < .0001f) return false;
        float x = ((b.Point.Y - c.Point.Y) * (point.X - c.Point.X) + (c.Point.X - b.Point.X) * (point.Y - c.Point.Y)) / denominator;
        float y = ((c.Point.Y - a.Point.Y) * (point.X - c.Point.X) + (a.Point.X - c.Point.X) * (point.Y - c.Point.Y)) / denominator;
        float z = 1 - x - y; if (x < 0 || y < 0 || z < 0) return false;
        float q = x * a.Q + y * b.Q + z * c.Q; if (q <= 0) return false;
        float u = (a.U * a.Q * x + b.U * b.Q * y + c.U * c.Q * z) / q;
        float v = (a.V * a.Q * x + b.V * b.Q * y + c.V * c.Q * z) / q;
        int ix = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1), iy = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
        if ((texture.Pixels[iy * texture.Width + ix] & unchecked((int)0xff000000)) == 0) return false;
        depth = x * a.Depth + y * b.Depth + z * c.Depth; return true;
    }
    internal void DragProjectionForTest(Point start, Point end,Keys modifiers=Keys.None)
    {
        var previous=_dragModifiersForTest;_dragModifiersForTest=modifiers;
        try
        {
            OnMouseDown(new(MouseButtons.Left, 1, start.X, start.Y, 0));
            OnMouseMove(new(MouseButtons.Left, 0, end.X, end.Y, 0));
            OnMouseUp(new(MouseButtons.Left, 1, end.X, end.Y, 0));
        }
        finally{_dragModifiersForTest=previous;}
    }

    private static float WrapAngle(float angle)
    {
        while (angle > MathF.PI) angle -= MathF.Tau;
        while (angle < -MathF.PI) angle += MathF.Tau;
        return angle;
    }

    private static float ToDegrees(float radians) => radians * 180f / MathF.PI;

    private static float ToRadians(float degrees) => degrees * MathF.PI / 180f;

    private static GraphicsPath RoundedRect(RectangleF rectangle, float radius)
    {
        GraphicsPath path = new();
        float diameter = radius * 2;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private readonly record struct ProjectionContext(
        float HalfX,
        float HalfY,
        float HalfZ,
        float HalfW,
        float Camera4D,
        float Camera3D,
        PointF Center,
        float ScreenScale,
        string ObjectKey,
        Rotation4D BaseRotation,
        PreparedRotation4D BasePreparedRotation);
    private readonly record struct SurfaceCacheKey(string ObjectKey, string Artwork, Size ObjectSize, SizeF StageSize,
        bool Enabled, float ObjectDepth, float Extent, float Camera, float Opacity, int Count, float Spread, double Time, double GeometryTime, Rotation4D Rotation);

    private readonly record struct Sheet4D(
        string Key,
        float Z,
        float W,
        bool IsOriginal,
        PreparedRotation4D Rotation,
        float? Opacity);

    private readonly record struct SheetDescriptor(
        string Key,
        float Z,
        float W,
        bool IsOriginal,
        bool IsBoundary);
}
