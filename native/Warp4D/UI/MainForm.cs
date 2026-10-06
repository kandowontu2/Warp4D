using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D.UI;

internal sealed partial class MainForm : Form, IMessageFilter
{
    internal void LoadRomForTest(string path) => LoadRom(path);
    internal void ApplyPresentationForTest(PresentationSettings settings) { ApplyPresentation(settings); SavePresentation(); }
    internal PresentationSettings PresentationForTest => _presentation.Clone();
    internal void ToggleFullscreenForTest() => ToggleFullscreen();
    internal int InputMaskForTest => _emulator.InputMaskForTest;
    internal bool HasSceneForTest => _renderer.SceneObjects.Count > 0;
    internal bool RomLoadedForTest => _emulator.IsLoaded;
    internal NesFrame? LatestFrameForTest => Volatile.Read(ref _latestFrame);
    internal int ProjectedSceneryCountForTest => _renderer.SceneObjects.Count(o => o.SortOrder < 20 && o.ProjectionEnabled);
    // Explicit diagnostic only: hidden test windows cannot assume OS focus.
    // Ordinary instances retain the real foreground-input guard.
    internal bool SimulateInputFocusForTest { get; set; }
    private bool HasInputFocus => ContainsFocus || SimulateInputFocusForTest;
    internal void CycleForTest(bool enabled) { _cycleCheckBox!.Checked = enabled; }
    internal void ResetProjectionCycleTimeForTest()
    {
        _projectionCycleClock.Restart();
        PresentationAnimator.Apply(_renderer,_presentation,0);
        SyncPresentationSliders();
    }
    internal void RandomizeCycleForTest() => _randomizeCycleButton!.PerformClick();
    internal double GeometryTimeForTest => _renderer.GeometryCycleSeconds;
    internal RotationAngles LiveRotationForTest => new() { XY=_renderer.AngleXYDegrees,XZ=_renderer.AngleXZDegrees,XW=_renderer.AngleXWDegrees,YZ=_renderer.AngleYZDegrees,YW=_renderer.AngleYWDegrees,ZW=_renderer.AngleZWDegrees };
    internal string PerformanceForTest => PerformanceMetrics.Summary;
    internal string RendererForTest=>_renderer.RendererStatus;
    internal long GpuPresentedFramesForTest=>_renderer.GpuPresentedFrames;
    internal bool DirectStageShownForTest=>_renderer.DirectGpuStageShown;
    internal Action<PlaybackPaintTiming>? PlaybackObserverForTest { set=>_renderer.PlaybackObserverForTest=value; }
    internal MeshAssemblyStats MeshAssemblyForTest=>_renderer.MeshAssemblyForTest;
    internal bool MeasureNativeCaptureForTest {get=>_emulator.MeasureNativeCaptureForTest;set=>_emulator.MeasureNativeCaptureForTest=value;}
    internal NativeCaptureStats NativeCaptureStatsForTest=>_emulator.NativeCaptureStatsForTest;
    internal bool UseNametablePixelReuseForTest {get=>_emulator.UseNametablePixelReuseForTest;set=>_emulator.UseNametablePixelReuseForTest=value;}
    internal object NametablePixelReuseStatsForTest=>_emulator.NametablePixelReuseStatsForTest;
    internal bool UseBackgroundPixelCacheForTest {get=>_renderer.UseBackgroundPixelCacheForTest;set=>_renderer.UseBackgroundPixelCacheForTest=value;}
    internal object BackgroundPixelCacheStatsForTest {get{var s=_renderer.BackgroundPixelCacheStatsForTest;return new {s.Hits,s.Misses,s.StoredPixels};}}
    internal bool UseReusableAccentStorageForTest {get=>_renderer.UseReusableAccentStorageForTest;set=>_renderer.UseReusableAccentStorageForTest=value;}
    internal long GpuReadbackFramesForTest=>_renderer.GpuReadbackFrames;
    internal bool ReuseCapturedScenesForTest { get; set; } = true;
    internal bool UseDirectVertexWritesForTest {set=>_renderer.UseDirectVertexWritesForTest=value;}
    internal bool UseReusableOrderStorageForTest {get=>_renderer.UseReusableOrderStorageForTest;set=>_renderer.UseReusableOrderStorageForTest=value;}
    internal bool UseCachedTextureInventoryForTest {set=>_renderer.UseCachedTextureInventoryForTest=value;}
    internal bool UseGeometryMapCacheForTest {get=>_renderer.UseGeometryMapCacheForTest;set=>_renderer.UseGeometryMapCacheForTest=value;}
    internal bool UseStreamingVertexBufferForTest {get=>_renderer.UseStreamingVertexBufferForTest;set=>_renderer.UseStreamingVertexBufferForTest=value;}
    internal bool UsedStreamingVertexBufferForTest=>_renderer.UsedStreamingVertexBufferForTest;
    internal object GeometryMapCacheStatsForTest
    {
        get {var stats=_renderer.GeometryMapCacheStatsForTest;return new{stats.Hits,stats.Misses,stats.Entries,stats.Vertices};}
    }
    internal string LastDirectFallbackForTest=>_renderer.LastDirectFallbackForTest;
    internal bool IgnorePointerForTest {set=>_renderer.IgnorePointerForTest=value;}
    internal object RenderingBreakdownForTest=>new
    {
        LastDrawMs=_renderer.LastDrawMilliseconds,
        SceneAssemblyMs=_renderer.SceneAssemblyMilliseconds,
        LightingMs=_renderer.LightingMilliseconds,
        GpuSubmissionMs=_renderer.GpuSubmissionMs,
        OrderingMs=_renderer.GpuOrderingMs,
        VertexPackingMs=_renderer.GpuPackingMs,
        ReadbackMs=_renderer.GpuReadbackMs,
        PresentMs=_renderer.GpuPresentMs,
        DirectStageShown=_renderer.DirectGpuStageShown,
        PresentedFrames=_renderer.GpuPresentedFrames,
        ReadbackFrames=_renderer.GpuReadbackFrames,
        Triangles=_renderer.SurfaceTriangleCount,
        DrawCalls=_renderer.GpuDrawCalls,
        TextureUploads=_renderer.TextureUploads
    };
    internal void PublishSceneForTest(SmbScene scene)=>PublishScene(scene,Volatile.Read(ref _romGeneration),null);
    internal void PublishCapturedSceneForTest(SmbScene scene,NesFrame captured,NesFrame newer)
    {
        Volatile.Write(ref _latestFrame,newer);
        PublishCapturedScene(scene,captured,Volatile.Read(ref _romGeneration));
    }
    internal int[]? DeliveredNativePixelsForTest=>_renderer.NativeScreenPixels;
    internal long PublishedSequenceForTest=>_lastPublishedSequence;
    internal int SceneDeliveryCountForTest{get;private set;}
    // Diagnostic boundary only: let any owned worker finish, then register or
    // detach timing observers and snapshot counters without a partial build.
    internal void CaptureBoundaryForTest(Action action)
    {
        long start=System.Diagnostics.Stopwatch.GetTimestamp();
        while(Interlocked.CompareExchange(ref _captureInProgress,1,0)!=0)
        {
            if(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds>5)throw new TimeoutException("Diagnostic capture boundary did not settle.");
            Thread.Sleep(1);
        }
        try{action();}
        finally{Volatile.Write(ref _captureInProgress,0);}
    }
    internal SmbScene? BuildCapturedFrameForTest(NesFrame frame,int generation)=>BuildCapturedFrame(frame,generation);
    internal int AdvanceCaptureGenerationForTest()=>Interlocked.Increment(ref _romGeneration);
    internal void TogglePauseForTest()=>TogglePause();
    internal (long Requests,long Completed,long Skipped) SceneBuildCountsForTest=>(Interlocked.Read(ref _sceneBuildRequests),Interlocked.Read(ref _sceneBuildCompleted),Interlocked.Read(ref _sceneBuildSkipped));
    internal Action<FrameBuildTiming>? FrameBuildObserverForTest {get;set;}
    internal Action<ProfileBuildTiming>? ProfileBuildObserverForTest {set=>_profile.BuildObserverForTest=value;}
    internal Action<BackgroundBuildTiming>? BackgroundBuildObserverForTest {set=>_profile.BackgroundObserverForTest=value;}
    internal bool UseBulkSpriteDecodingForTest {set=>_profile.UseBulkSpriteDecodingForTest=value;}
    internal bool UseSpriteArtworkPreflightForTest {set=>_profile.UseSpriteArtworkPreflightForTest=value;}
    internal bool UseDirectBackdropCountingForTest {set=>_profile.UseDirectBackdropCountingForTest=value;}
    internal bool UseRunBackdropCountingForTest {set=>_profile.UseRunBackdropCountingForTest=value;}
    internal bool UseBackdropCacheForTest {get=>_profile.UseBackdropCacheForTest;set=>_profile.UseBackdropCacheForTest=value;}
    internal object BackdropCacheStatsForTest=>_profile.BackdropCacheStatsForTest;
    internal bool UseFusedBackdropVisibilityForTest {set=>_profile.UseFusedBackdropVisibilityForTest=value;}
    internal bool UseCombinedArtworkExtractionForTest {set=>_profile.UseCombinedArtworkExtractionForTest=value;}
    internal void SetCaptureProfilesForTest(ProjectionProfile projection,GameRecognitionProfile? game)
    { Volatile.Write(ref _projectionProfile,projection);Volatile.Write(ref _gameProfile,game); }
    internal string? GameProfileNameForTest => _gameProfile?.Name;
    internal void ToggleControlsForTest() => ToggleControls();
    internal bool ControlsVisibleForTest => _controlsVisible;
    internal void ToggleCinematicForTest()=>ToggleCinematic();
    internal bool CinematicForTest=>_cinematic;
    internal void ApplyLookForTest(int index) => ApplyLook(index);
    internal Size StageSizeForTest => _renderer.ClientSize;
    internal void SaveScreenshotForTest(string path) => SaveStageImage(path);
    internal bool LookDockControlsClearForTest
    {
        get
        {
            if(_friendlyTabs.TabPages.Count==4)return _lookCards.All(card=>card.Parent!.ClientRectangle.Contains(card.Bounds))&&_friendlyTabs.ClientSize.Width>=250;
            if (_interfaceMotionCheckBox?.Parent is not Control parent) return false;
            Rectangle bounds = _interfaceMotionCheckBox.Bounds;
            return parent.ClientRectangle.Contains(bounds) && bounds.Height >= _interfaceMotionCheckBox.GetPreferredSize(Size.Empty).Height &&
                parent.Controls.Cast<Control>().Where(c => c != _interfaceMotionCheckBox).All(c => !c.Bounds.IntersectsWith(bounds));
        }
    }
    private readonly HighResolutionTimer _timerResolution = new();
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int LookDockHeight = 200;
    private int InspectorWidth => _friendlyTabs.TabPages.Count==4 ? 326 : 280;
    private static readonly Color WindowColor = Color.FromArgb(8, 11, 17);
    private static readonly Color PanelColor = Color.FromArgb(14, 19, 28);
    private static readonly Color BorderColor = Color.FromArgb(37, 49, 63);
    private static readonly Color TextColor = Color.FromArgb(227, 234, 241);
    private static readonly Color MutedColor = Color.FromArgb(126, 143, 160);
    private static readonly Color Lime = Color.FromArgb(173, 255, 93);

    private readonly NesEmulator _emulator = new();
    private readonly SmbProfile _profile = new();
    private ProjectionProfile _projectionProfile = ProjectionProfileStore.Load();
    private PresentationSettings _presentation = new();
    private readonly System.Windows.Forms.Timer _saveSettingsTimer = new() { Interval = 600 };
    private CheckBox? _cycleCheckBox;
    private Button? _randomizeCycleButton;
    private InputSettings _inputSettings = InputSettingsStore.Load();
    private readonly HashSet<Keys> _heldKeys = [];
    private readonly Label _performanceLabel = MakeLabel("Timings appear after loading a ROM.", 8, MutedColor);
    private readonly Label _controllerLabel = MakeLabel("Controller: disconnected", 8, MutedColor);
    private NumericUpDown? _stateSlot;
    private int _selectedStateSlot = 1;
    private TableLayoutPanel? _rootLayout;
    private Control? _headerPanel;
    private Control? _sidebarPanel;
    private Control? _lookDockPanel;
    private bool _controlsVisible;
    private Button? _controlsButton;
    private readonly List<LookCard> _lookCards = [];
    private CheckBox? _interfaceMotionCheckBox;
    internal int LiveStyleButtonsForTest => _lookCards.Count;
    internal void ClickStyleForTest(int index) => _lookCards[index].PerformClick();
    internal bool LiveStyleButtonsVisibleForTest => _lookCards.All(card => card.Visible && card.Parent!.ClientRectangle.Contains(card.Bounds) && card.Height >= 60);
    private readonly Label _stageStatus = MakeLabel("YOUR NES. ANOTHER DIMENSION.", 8, MutedColor, FontStyle.Bold);
    private bool _fullscreen;
    private bool _cinematic;
    private DimensionStudioForm? _dimensionStudio;
    private readonly SessionAudioMeter _audioMeter=new();
    private CleanVideoRecorder? _recorder;
    private bool _finishingRecording;
    private Task _recordingFinishTask=Task.CompletedTask;
    private CheckBox? _effectsToggle, _smoothToggle;
    private readonly List<TrackBar> _effectsSliders = [];
    private Rectangle _windowedBounds;
    private FormWindowState _windowedState;
    private long _lastDiagnostics;
    private GameRecognitionProfile? _gameProfile;
    private NesFrame? _latestFrame;
    private readonly WarpRendererControl _renderer = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _frameTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer _projectionCycleTimer = new() { Interval = 16 };
    private readonly System.Diagnostics.Stopwatch _projectionCycleClock = new();
    private readonly Label _romLabel = MakeLabel("NO ROM LOADED", 9, TextColor, FontStyle.Bold);
    private readonly Label _profileLabel = MakeLabel("WAITING", 8, MutedColor, FontStyle.Bold);
    private readonly Label _statusLabel = MakeLabel("Open a ROM or drop one onto the window.", 9, MutedColor);
    private readonly Label _objectCountLabel = MakeLabel("0 live objects", 9, MutedColor);
    private readonly Button _pauseButton;
    private TrackBar? _wExtentSlider;
    private TrackBar? _cameraSlider;
    private TrackBar? _opacitySlider;
    private TrackBar? _crossSectionsSlider;
    private TrackBar? _projectionRotationSpreadSlider;
    private TrackBar? _xyRotationSlider;
    private TrackBar? _xwRotationSlider;
    private TrackBar? _ywRotationSlider;
    private TrackBar? _zwRotationSlider;
    private TrackBar? _xzRotationSlider;
    private TrackBar? _yzRotationSlider;
    private bool _batchingProjectionValues;
    private bool _captureFaultShown;
    private int _captureInProgress;
    private int _romGeneration;
    private sealed record PendingScene(SmbScene Scene,int Generation,int[]? NativePixels);
    private PendingScene? _pendingScene;
    private int _sceneDeliveryQueued;
    private int _lastPublishedGeneration=-1;
    private long _lastPublishedSequence=-1;
    private sealed record SceneBuildKey(int Generation,long Sequence,ProjectionProfile Projection,GameRecognitionProfile? Game);
    private SceneBuildKey? _lastSceneBuildKey;
    private long _sceneBuildRequests,_sceneBuildCompleted,_sceneBuildSkipped;
    private volatile bool _closing;

    public MainForm(bool guidedInterface = false)
    {
        InterfaceMotion.Load();
        NativeTheme.Apply(this);
        Text = "Warp4D — Native NES Object Projector";
        MinimumSize = new Size(1040, 720);
        ClientSize = new Size(1260, 820);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = WindowColor;
        ForeColor = TextColor;
        KeyPreview = true;
        AllowDrop = true;

        _pauseButton = MakeButton("Pause", secondary: true);
        _pauseButton.Click += (_, _) => TogglePause();

        Controls.Add(guidedInterface ? BuildFriendlyLayout() : BuildLayout());
        _renderer.GuidedInterface=guidedInterface;_renderer.ShowLabels=!guidedInterface;

        _presentation.ProjectionProfile = _projectionProfile.Clone();
        _renderer.EnableStyleTransitions = true;
        _renderer.ApplySettings(_presentation);
        _renderer.RotationChanged += (_, _) => { SyncRotationSliders(); SaveManualRotation(); };
        _renderer.OpenRomRequested += OpenRomDialog;
        _renderer.ProjectionPicked += (objectKey, layerKey) => { if(guidedInterface)PickFriendlyObject(objectKey,layerKey);else OpenPresentationEditor(objectKey,layerKey); };
        _renderer.PaintObjectPicked+=item=> { if(guidedInterface)PickFriendlyObject(item.PresentationKey,"Center");else { OpenDimensionStudio();_dimensionStudio?.SelectObject(item); } };
        _renderer.CleanFrameRendered+=stage=>_recorder?.Offer(stage);
        // Keep intervening playback paints on the GPU. The recorder accepts
        // 30 fps, so reading back every faster paint only stalls presentation.
        _renderer.NeedsCleanFrame=()=>_recorder?.WantsFrame==true;
        _renderer.RotationChanged+=(_,_)=>_dimensionStudio?.StopAutoBlend();
        _saveSettingsTimer.Tick += (_, _) => SavePresentation();
        _frameTimer.Tick += (_, _) => UpdateFrame();
        _projectionCycleTimer.Tick += (_, _) => ApplyProjectionCycle(invalidateRenderer: true);
        Application.AddMessageFilter(this);
        Shown += (_, _) => OnFirstShown();
        FormClosing += (_, _) => Shutdown();
        KeyDown += OnGameKeyDown;
        KeyUp += OnGameKeyUp;
        Deactivate += (_, _) => ReleaseAllButtons();
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = WindowColor,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, LookDockHeight));

        _headerPanel = BuildHeader();
        root.Controls.Add(_headerPanel, 0, 0);
        root.SetColumnSpan(_headerPanel, 2);
        root.Controls.Add(_renderer, 0, 1);
        _sidebarPanel = BuildSidebar();
        root.Controls.Add(_sidebarPanel, 1, 1);
        _sidebarPanel.Visible = false;
        _lookDockPanel = BuildLookDock();
        root.Controls.Add(_lookDockPanel, 0, 2);
        root.SetColumnSpan(_lookDockPanel, 2);
        _rootLayout = root;
        return root;
    }

    private Control BuildHeader()
    {
        Panel header = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 14, 21),
            Padding = new Padding(24, 12, 18, 10)
        };

        header.Controls.Add(new DimensionalBrand { Location = new(18, 8), Size = new(258, 48) });

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 0)
        };
        Button open = MakeButton("OPEN ROM", secondary: false);
        open.Width = 108;
        open.Click += (_, _) => OpenRomDialog();
        Button gameProfile = MakeButton("GAME PROFILE", secondary: true);
        gameProfile.Width = 120;
        gameProfile.Click += (_, _) => OpenGameProfileEditor();
        actions.Controls.Add(open);
        actions.Controls.Add(gameProfile);
        Button look = MakeButton("LOOKS", secondary: true);
        look.Width = 78;
        look.Click += (_, _) => OpenLookGallery();
        actions.Controls.Add(look);
        _controlsButton = MakeButton("CONTROLS", true); _controlsButton.Width = 100;
        _controlsButton.Click += (_, _) => ToggleControls(); actions.Controls.Add(_controlsButton);
        Button studio = MakeButton("STUDIO", true); studio.Width = 78;
        studio.Click += (_, _) => OpenDimensionStudio(); actions.Controls.Add(studio);
        actions.Controls.Add(_pauseButton);
        Button present = MakeButton("CINEMA", true); present.Width = 90;
        present.Click += (_, _) => ToggleCinematic(); actions.Controls.Add(present);
        header.Controls.Add(actions);

        header.Paint += (_, e) =>
        {
            using Pen line = new(BorderColor);
            e.Graphics.DrawLine(line, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        return header;
    }

    private Control BuildLookDock()
    {
        TableLayoutPanel dock = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(10, 15, 22), Padding = new(18, 8, 18, 8) };
        dock.ColumnStyles.Add(new(SizeType.Absolute, 174)); dock.ColumnStyles.Add(new(SizeType.Percent, 100));
        TableLayoutPanel intro = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new(0, 2, 8, 0) };
        intro.ColumnStyles.Add(new(SizeType.Percent, 100));
        intro.RowStyles.Add(new(SizeType.Absolute, 22));
        intro.RowStyles.Add(new(SizeType.Percent, 100));
        intro.RowStyles.Add(new(SizeType.AutoSize));
        intro.RowStyles.Add(new(SizeType.Absolute, 26));
        Label title = MakeLabel("CHOOSE A LOOK", 9, Lime, FontStyle.Bold);
        title.Dock = DockStyle.Fill; title.Margin = Padding.Empty; title.TextAlign = ContentAlignment.MiddleLeft;
        _stageStatus.AutoSize = false; _stageStatus.AutoEllipsis = true;
        _stageStatus.MaximumSize = Size.Empty; _stageStatus.Dock = DockStyle.Fill;
        _stageStatus.Margin = Padding.Empty; _stageStatus.TextAlign = ContentAlignment.MiddleLeft;
        intro.Controls.Add(title, 0, 0); intro.Controls.Add(_stageStatus, 0, 1);
        CheckBox motion = new() { Text = "Interface motion", Checked = InterfaceMotion.Enabled, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new(0, 2, 0, 3), ForeColor = TextColor, Font = new("Segoe UI", 8), AccessibleName = "Animate interface effects" };
        _interfaceMotionCheckBox = motion;
        motion.CheckedChanged += (_, _) => { InterfaceMotion.Enabled = motion.Checked; InterfaceMotion.Save(); Invalidate(true); };
        intro.Controls.Add(motion, 0, 2);
        Button capture = MakeButton("SNAPSHOT · F12", true); capture.Dock = DockStyle.Fill; capture.Margin = new(0, 1, 0, 1);
        capture.Click += (_, _) => CaptureScreenshot(); intro.Controls.Add(capture, 0, 3);
        dock.Controls.Add(intro, 0, 0);
        TableLayoutPanel cards = new() { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Margin = Padding.Empty };
        for (int column = 0; column < 6; column++) cards.ColumnStyles.Add(new(SizeType.Percent, 100f / 6));
        cards.RowStyles.Add(new(SizeType.Percent, 50)); cards.RowStyles.Add(new(SizeType.Percent, 50));
        for (int i = 0; i < LookCatalog.Names.Length; i++)
        {
            LookCard card = new(i) { Dock = DockStyle.Fill, Compact = true, Margin = new(3) };
            card.Click += (_, _) => ApplyLook(card.LookIndex); cards.Controls.Add(card, i % 6, i / 6); _lookCards.Add(card);
        }
        Button gallery = MakeButton("PREVIEW\nGALLERY…", true); gallery.Dock = DockStyle.Fill; gallery.Margin = new(3);
        gallery.Click += (_, _) => OpenLookGallery(); cards.Controls.Add(gallery, 5, 1);
        dock.Controls.Add(cards, 1, 0); return dock;
    }

    private void ToggleControls()
    {
        if (_rootLayout is null || _fullscreen) return;
        if(_cinematic) ToggleCinematic();
        _controlsVisible = !_controlsVisible;
        _rootLayout.SuspendLayout();
        _rootLayout.ColumnStyles[1].Width = _controlsVisible ? InspectorWidth : 0;
        _sidebarPanel!.Visible = _controlsVisible;
        _rootLayout.ResumeLayout();
        if (_controlsButton is not null) _controlsButton.Text = _friendlyTabs.TabPages.Count==4 ? (_controlsVisible ? "Hide settings" : "Show settings") : (_controlsVisible ? "HIDE CONTROLS" : "CONTROLS");
        _renderer.Focus();
    }

    private void ApplyLook(int index)
    {
        RememberFriendlyChange();StopFriendlyBlend();
        ApplyPresentation(LookCatalog.Create(index, _presentation)); SavePresentation();
        _stageStatus.Text = _friendlyTabs.TabPages.Count==4 ? FriendlyLooks.Descriptions[index] : LookCatalog.Descriptions[index];
        _renderer.SignalLookChange();
        _renderer.Focus();
    }

    private void OpenLookGallery()
    {
        ReleaseAllButtons();
        using LookGalleryForm gallery = new(_presentation, _renderer.CloneScene());
        if (gallery.ShowDialog(this) == DialogResult.OK) { ApplyPresentation(gallery.SelectedSettings); SavePresentation(); _renderer.SignalLookChange(); }
        _renderer.Focus();
    }

    private void CaptureScreenshot()
    {
        ReleaseAllButtons();
        using SaveFileDialog dialog = new() { Title = "Save a Warp4D snapshot", Filter = "PNG image (*.png)|*.png", FileName = "Warp4D-snapshot.png" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try { SaveStageImage(dialog.FileName); _stageStatus.Text = "SNAPSHOT SAVED"; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
            { MessageBox.Show(this, exception.Message, "Could not save snapshot"); }
        }
        _renderer.Focus();
    }

    private void SaveStageImage(string path)
    {
        bool previous = _renderer.PresentationMode;
        try
        {
            _renderer.PresentationMode = true;
            using Bitmap image = new(_renderer.Width, _renderer.Height);
            _renderer.DrawToBitmap(image, _renderer.ClientRectangle);
            image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        finally { _renderer.PresentationMode = previous; _renderer.Invalidate(); }
    }

    private Control BuildSidebar()
    {
        Panel sidebar = new()
        {
            Dock = DockStyle.Fill,
            BackColor = PanelColor,
            Padding = new Padding(22, 20, 22, 18),
            AutoScroll = true
        };
        sidebar.Paint += (_, e) =>
        {
            using Pen line = new(BorderColor);
            e.Graphics.DrawLine(line, 0, 0, 0, sidebar.Height);
        };

        FlowLayoutPanel stack = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Width = 246
        };

        stack.Controls.Add(MakeSectionTitle("CARTRIDGE"));
        Button projection = MakeButton("OBJECT CLASS PROFILE", true); projection.Width = 246;
        projection.Click += (_, _) => OpenProfileEditor(); stack.Controls.Add(projection);
        Button reset = MakeButton("RESET GAME (F2)", true); reset.Width = 246;
        reset.Click += (_, _) => _emulator.Reset(); stack.Controls.Add(reset);
        _romLabel.MaximumSize = new Size(246, 42);
        _romLabel.AutoEllipsis = true;
        stack.Controls.Add(_romLabel);
        _profileLabel.Margin = new Padding(0, 5, 0, 22);
        stack.Controls.Add(_profileLabel);

        stack.Controls.Add(MakeSectionTitle("R⁴ PROJECTION"));
        Control wExtent = MakeSliderRow("W EXTENT", 0, 100, 72, value =>
        {
            _renderer.DepthAmount = value / 100f;
            if (!_batchingProjectionValues) _presentation.Depth = value / 100f;
            ProjectionSettingChanged();
        });
        _wExtentSlider = wExtent.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(wExtent);

        Control camera = MakeSliderRow("4D CAMERA PROXIMITY", 0, 100, 55, value =>
        {
            _renderer.Perspective = value / 100f;
            if (!_batchingProjectionValues) _presentation.Perspective = value / 100f;
            ProjectionSettingChanged();
        });
        _cameraSlider = camera.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(camera);

        Control opacity = MakeSliderRow("4D LAYER OPACITY", 0, 100, 18, value =>
        {
            _renderer.ProjectionOpacity = value / 100f;
            if (!_batchingProjectionValues) _presentation.Opacity = value / 100f;
            ProjectionSettingChanged();
        });
        _opacitySlider = opacity.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(opacity);

        Control crossSections = MakeSliderRow("W CROSS-SECTIONS", 2, 9, 3, value =>
        {
            _renderer.SliceCount = value;
            if (!_batchingProjectionValues) _presentation.CrossSections = value;
            ProjectionSettingChanged();
        });
        _crossSectionsSlider = crossSections.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(crossSections);

        Control projectionRotationSpread = MakeSliderRow("PER-PROJECTION ROTATION", 0, 180, 58, value =>
        {
            _renderer.ProjectionRotationSpreadDegrees = value;
            if (!_batchingProjectionValues) _presentation.RotationSpread = value;
            ProjectionSettingChanged();
        });
        _projectionRotationSpreadSlider = projectionRotationSpread.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(projectionRotationSpread);

        Label rotationHelp = MakeLabel(
            "ROTATION PLANES\nEach projected sheet gets a distinct 6-plane rotation.\nXW / YW / ZW rotate through W\nXY / XZ / YZ rotate spatial axes",
            8,
            MutedColor,
            FontStyle.Bold);
        rotationHelp.Margin = new Padding(0, 6, 0, 12);
        stack.Controls.Add(rotationHelp);

        Control xyRotation = MakeSliderRow("XY ROTATION", -180, 180, 0, value =>
        {
            _renderer.AngleXYDegrees = value;
            if (!_batchingProjectionValues) _presentation.Rotation.XY = value;
            ProjectionSettingChanged();
        });
        _xyRotationSlider = xyRotation.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(xyRotation);

        Control xwRotation = MakeSliderRow("XW ROTATION", -180, 180, 24, value =>
        {
            _renderer.AngleXWDegrees = value;
            if (!_batchingProjectionValues) _presentation.Rotation.XW = value;
            ProjectionSettingChanged();
        });
        _xwRotationSlider = xwRotation.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(xwRotation);

        Control ywRotation = MakeSliderRow("YW ROTATION", -180, 180, -16, value =>
        {
            _renderer.AngleYWDegrees = value;
            if (!_batchingProjectionValues) _presentation.Rotation.YW = value;
            ProjectionSettingChanged();
        });
        _ywRotationSlider = ywRotation.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(ywRotation);

        Control zwRotation = MakeSliderRow("ZW ROTATION", -180, 180, 33, value =>
        {
            _renderer.AngleZWDegrees = value;
            if (!_batchingProjectionValues) _presentation.Rotation.ZW = value;
            ProjectionSettingChanged();
        });
        _zwRotationSlider = zwRotation.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(zwRotation);

        Control xzRotation = MakeSliderRow("XZ ROTATION", -180, 180, -9, value =>
        {
            _renderer.AngleXZDegrees = value;
            if (!_batchingProjectionValues) _presentation.Rotation.XZ = value;
            ProjectionSettingChanged();
        });
        _xzRotationSlider = xzRotation.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(xzRotation);

        Control yzRotation = MakeSliderRow("YZ ROTATION", -180, 180, 6, value =>
        {
            _renderer.AngleYZDegrees = value;
            if (!_batchingProjectionValues) _presentation.Rotation.YZ = value;
            ProjectionSettingChanged();
        });
        _yzRotationSlider = yzRotation.Controls.OfType<TrackBar>().Single();
        stack.Controls.Add(yzRotation);

        CheckBox autoCycle = new()
        {
            Text = "Auto-cycle geometry and rotations",
            Checked = false,
            AutoSize = true,
            ForeColor = Lime,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 2, 0, 14)
        };
        autoCycle.CheckedChanged += (_, _) => SetProjectionCycling(autoCycle.Checked, userInitiated: !_batchingProjectionValues);
        _cycleCheckBox = autoCycle;
        stack.Controls.Add(autoCycle);

        Button randomizeCycle = MakeButton("RANDOMIZE CYCLE", secondary: true);
        _randomizeCycleButton = randomizeCycle;
        randomizeCycle.Width = 246;
        randomizeCycle.Margin = new Padding(0, 0, 0, 8);
        randomizeCycle.Click += (_, _) => RandomizeCycle();
        stack.Controls.Add(randomizeCycle);

        Button resetCamera = MakeButton("RESET ALL ROTATIONS", secondary: true);
        resetCamera.Width = 246;
        resetCamera.Margin = new Padding(0, 5, 0, 20);
        resetCamera.Click += (_, _) =>
        {
            _renderer.ProjectionCycleSeconds = 0;
            _renderer.ProjectionRotationSpreadDegrees = 58;
            SetSliderValue(_projectionRotationSpreadSlider, 58);
            _renderer.ResetCamera();
        };
        stack.Controls.Add(resetCamera);
        stack.Controls.Add(new Label {
            Text="Drag: XW / YW\nCtrl: Z rotation, W fixed\nShift: X fixed · Ctrl+Shift: Y fixed",
            Width=246, Height=57, ForeColor=MutedColor, Font=new Font("Segoe UI",8), Margin=new Padding(0,0,0,12)
        });

        CheckBox labels = new()
        {
            Text = "Show object labels on hover",
            Checked = true,
            AutoSize = true,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 0, 0, 22)
        };
        labels.CheckedChanged += (_, _) =>
        {
            _renderer.ShowLabels = labels.Checked;
            _renderer.Invalidate();
        };
        stack.Controls.Add(labels);

        stack.Controls.Add(MakeSectionTitle("LIVE SCENE"));
        _objectCountLabel.Margin = new Padding(0, 0, 0, 16);
        stack.Controls.Add(_objectCountLabel);
        stack.Controls.Add(MakeLegend("MARIO / PLAYER", Color.FromArgb(255, 245, 127)));
        stack.Controls.Add(MakeLegend("ENEMIES", Color.FromArgb(255, 91, 97)));
        stack.Controls.Add(MakeLegend("BUSHES / SCENERY", Color.FromArgb(128, 255, 100)));
        stack.Controls.Add(MakeLegend("BLOCKS / STRUCTURES", Color.FromArgb(255, 180, 50)));

        stack.Controls.Add(MakeSectionTitle("VISUAL FINISH"));
        Button dimensions=MakeButton("DIMENSION STUDIO",true);dimensions.Width=246;dimensions.Click+=(_,_)=>OpenDimensionStudio();stack.Controls.Add(dimensions);
        Button layers=MakeButton("PROJECTION LAYER EDITOR",true);layers.Width=246;layers.Click+=(_,_)=>OpenPresentationEditor();stack.Controls.Add(layers);
        stack.Controls.Add(new DimensionCompass(_renderer));
        _effectsToggle=new(){Text="Lighting, glow and shadows",Checked=_presentation.Effects.Enabled,AutoSize=true,ForeColor=Lime,Margin=new(0,0,0,8)};
        _effectsToggle.CheckedChanged+=(_,_)=>{if(_batchingProjectionValues)return;_presentation.Effects.Enabled=_effectsToggle.Checked;_renderer.RefreshEffects();ProjectionSettingChanged();};
        stack.Controls.Add(_effectsToggle);
        foreach(var entry in new[]{("LIGHTING",0),("SELECTIVE GLOW",1),("DEPTH SHADOWS",2)})
        {
            int index=entry.Item2;
            float initial=index==0?_presentation.Effects.Lighting:index==1?_presentation.Effects.Glow:_presentation.Effects.Shadows;
            Control row=MakeSliderRow(entry.Item1,0,100,(int)(initial*100),value=>
            {
                if(_batchingProjectionValues)return;
                if(index==0)_presentation.Effects.Lighting=value/100f;else if(index==1)_presentation.Effects.Glow=value/100f;else _presentation.Effects.Shadows=value/100f;
                _renderer.RefreshEffects();ProjectionSettingChanged();
            });
            _effectsSliders.Add(row.Controls.OfType<TrackBar>().Single());stack.Controls.Add(row);
        }
        _smoothToggle=new(){Text="Smooth style transformations",Checked=true,AutoSize=true,ForeColor=TextColor,Margin=new(0,0,0,10)};
        _smoothToggle.CheckedChanged+=(_,_)=>{if(_batchingProjectionValues)return;_presentation.Effects.SmoothTransitions=_smoothToggle.Checked;ProjectionSettingChanged();};
        stack.Controls.Add(_smoothToggle);
        Button cinema=MakeButton("CINEMA (F10)",true);cinema.Width=246;cinema.Click+=(_,_)=>ToggleCinematic();stack.Controls.Add(cinema);

        stack.Controls.Add(MakeSectionTitle("EMULATOR"));
        Button controls = MakeButton("KEYBOARD / CONTROLLER", true);
        controls.Width = 246;
        controls.Click += (_, _) => OpenControlsEditor();
        stack.Controls.Add(controls);
        _controllerLabel.Margin = new Padding(0, 8, 0, 12);
        stack.Controls.Add(_controllerLabel);
        stack.Controls.Add(MakeSliderRow("VOLUME", 0, 100, _inputSettings.Volume, value =>
        { _inputSettings.Volume = value; _emulator.SetVolume(value); SaveInputSettings(); }));
        Panel states = new() { Width = 246, Height = 78 };
        states.Controls.Add(new Label { Text = "SAVE STATE SLOT", AutoSize = true, Location = new Point(0, 5) });
        _stateSlot = new() { Minimum = 1, Maximum = 10, Value = 1, Location = new Point(170, 2), Width = 75 };
        _stateSlot.ValueChanged += (_, _) => _selectedStateSlot = (int)_stateSlot.Value;
        states.Controls.Add(_stateSlot);
        Button save = MakeButton("SAVE (F5)", true), load = MakeButton("LOAD (F8)", true);
        save.Location = new Point(0, 35); load.Location = new Point(126, 35);
        save.Width = load.Width = 119;
        save.Click += (_, _) => StateAction(save: true); load.Click += (_, _) => StateAction(save: false);
        states.Controls.Add(save); states.Controls.Add(load); stack.Controls.Add(states);
        Button fullscreen = MakeButton("FULLSCREEN (F11)", true); fullscreen.Width = 246;
        fullscreen.Click += (_, _) => ToggleFullscreen(); stack.Controls.Add(fullscreen);
        CheckBox gpu = new() { Text = "Use GPU renderer", Checked = true, AutoSize = true, Margin = new Padding(0, 12, 0, 6) };
        gpu.CheckedChanged += (_, _) => { _renderer.UseGpu = gpu.Checked; _renderer.Invalidate(); };
        stack.Controls.Add(gpu);
        _performanceLabel.MaximumSize = new Size(246, 120);
        _performanceLabel.Margin = new Padding(0, 10, 0, 16);
        stack.Controls.Add(_performanceLabel);

        _statusLabel.MaximumSize = new Size(246, 70);
        _statusLabel.Margin = new Padding(0, 6, 0, 0);
        stack.Controls.Add(_statusLabel);
        sidebar.Controls.Add(stack);
        return sidebar;
    }

    private void SetProjectionCycling(bool enabled, bool userInitiated = false)
    {
        if(enabled && userInitiated) PresentationAnimator.EnableCycle(_presentation);
        _presentation.Animate = enabled;
        _renderer.Settings.Animate = enabled;
        _renderer.Settings.Geometry = _presentation.Geometry.Clone();
        _renderer.Settings.ObjectGeometries = _presentation.ObjectGeometries.ToDictionary(p=>p.Key,p=>p.Value.Clone());
        if (!_batchingProjectionValues) ProjectionSettingChanged();
        if (enabled)
        {
            _projectionCycleClock.Start();
            ApplyProjectionCycle(invalidateRenderer: true);
            if (!_emulator.IsLoaded)
            {
                _projectionCycleTimer.Start();
            }
        }
        else
        {
            _projectionCycleTimer.Stop();
            _projectionCycleClock.Stop();
            _renderer.ApplySettings(_presentation);
            SyncPresentationSliders();
        }
    }

    private void RandomizeCycle()
    {
        PresentationSettings randomized = _presentation.Clone();
        PresentationAnimator.RandomizeCycle(randomized, Random.Shared);
        ApplyPresentation(randomized); SavePresentation();
        _statusLabel.Text = "Cycle randomized: independent axis phases/speeds. Opacity and object edits preserved.";
    }

    private void ApplyProjectionCycle(bool invalidateRenderer)
    {
        double elapsedSeconds = _projectionCycleClock.Elapsed.TotalSeconds*_presentation.CycleSpeed;
        PresentationAnimator.Apply(_renderer, _presentation, elapsedSeconds);
        SyncPresentationSliders();

        // The empty welcome surface has its own slower decorative timer. Cycling
        // a preset with no game objects must not redraw that surface at 60 Hz.
        if (invalidateRenderer && _renderer.SceneObjects.Count > 0)
        {
            _renderer.Invalidate();
        }
    }

    private void ProjectionSettingChanged()
    {
        if (!_batchingProjectionValues)
        {
            _saveSettingsTimer.Stop();
            _saveSettingsTimer.Start();
            _renderer.Invalidate();
        }
    }

    private void SyncRotationSliders()
    {
        _batchingProjectionValues = true;
        try
        {
            SetSliderValue(_xyRotationSlider, (int)Math.Round(_renderer.AngleXYDegrees));
            SetSliderValue(_xwRotationSlider, (int)Math.Round(_renderer.AngleXWDegrees));
            SetSliderValue(_ywRotationSlider, (int)Math.Round(_renderer.AngleYWDegrees));
            SetSliderValue(_zwRotationSlider, (int)Math.Round(_renderer.AngleZWDegrees));
            SetSliderValue(_xzRotationSlider, (int)Math.Round(_renderer.AngleXZDegrees));
            SetSliderValue(_yzRotationSlider, (int)Math.Round(_renderer.AngleYZDegrees));
        }
        finally
        {
            _batchingProjectionValues = false;
        }
    }

    private static void SetSliderValue(TrackBar? slider, int value)
    {
        if (slider is not null && slider.Value != value)
        {
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        }
    }

    private Control MakeSliderRow(string title, int minimum, int maximum, int initial, Action<int> changed)
    {
        Panel panel = new() { Width = 246, Height = 67, Margin = new Padding(0, 0, 0, 5) };
        Label label = MakeLabel(title, 8, MutedColor, FontStyle.Bold);
        label.AutoSize = true;
        label.Location = new Point(0, 0);
        Label valueLabel = MakeLabel(initial.ToString(), 8, TextColor, FontStyle.Bold);
        valueLabel.AutoSize = true;
        valueLabel.Location = new Point(219, 0);
        TrackBar slider = new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = initial,
            TickStyle = TickStyle.None,
            Width = 252,
            Height = 35,
            Location = new Point(-8, 25),
            BackColor = PanelColor
        };
        slider.ValueChanged += (_, _) =>
        {
            valueLabel.Text = slider.Value.ToString();
            valueLabel.Left = panel.Width - valueLabel.PreferredWidth;
            changed(slider.Value);
        };
        panel.Controls.Add(label);
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(slider);
        return panel;
    }

    private static Control MakeLegend(string text, Color color)
    {
        Panel row = new() { Width = 246, Height = 25, Margin = Padding.Empty };
        Panel swatch = new() { BackColor = color, Width = 8, Height = 8, Location = new Point(0, 8) };
        Label label = MakeLabel(text, 8, MutedColor, FontStyle.Bold);
        label.AutoSize = true;
        label.Location = new Point(18, 4);
        row.Controls.Add(swatch);
        row.Controls.Add(label);
        return row;
    }

    private static Label MakeSectionTitle(string text)
    {
        Label label = MakeLabel(text, 8, Lime, FontStyle.Bold);
        label.AutoSize = true;
        label.Margin = new Padding(0, 0, 0, 11);
        return label;
    }

    private static Label MakeLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = color,
        Font = new Font("Segoe UI", size, style),
        BackColor = Color.Transparent,
        Margin = Padding.Empty
    };

    private static Button MakeButton(string text, bool secondary)
    {
        Button button = new()
        {
            Text = text,
            AutoSize = false,
            Size = new Size(text.Length > 8 ? 112 : 84, 36),
            Margin = new Padding(5, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = secondary ? Color.FromArgb(19, 26, 37) : Lime,
            ForeColor = secondary ? TextColor : Color.FromArgb(13, 20, 10),
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
            UseCompatibleTextRendering = true,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.FlatAppearance.BorderColor = secondary ? BorderColor : Lime;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = secondary ? Color.FromArgb(29, 39, 52) : Color.FromArgb(196, 255, 128);
        button.Paint += (_, e) => { if (!button.Enabled) TextRenderer.DrawText(e.Graphics, button.Text, button.Font, Rectangle.Inflate(button.ClientRectangle, -3, -3), Color.FromArgb(134, 149, 166), button.BackColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); };
        return button;
    }

    private void OnFirstShown()
    {
        _renderer.Focus();
    }

    private void OpenRomDialog()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Open an NES ROM",
            Filter = "NES ROMs (*.nes)|*.nes|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadRom(dialog.FileName);
        }
    }

    private void LoadRom(string path)
    {
        StopFriendlyBlend();ClearFriendlySelection();_friendlyUndo=null;
        _=StopRecordingAsync();_dimensionStudio?.Close();_renderer.ResetRuntime();
        SavePresentation();
        Interlocked.Increment(ref _romGeneration);
        try
        {
            _frameTimer.Stop();
            _statusLabel.Text = "Loading native emulator core…";
            _emulator.Load(path, Handle, _renderer.Handle);
            ReleaseAllButtons();
            _emulator.SetVolume(_inputSettings.Volume);
            ApplyPresentation(PresentationSettingsStore.Load(_emulator.RomSha256) ?? new PresentationSettings { ProjectionProfile = ProjectionProfileStore.Load() });
            Volatile.Write(ref _latestFrame, null);
            Volatile.Write(ref _gameProfile, GameRecognitionProfileStore.LoadForRom(_emulator.RomSha256) ?? _emulator.BuiltInGameProfile?.Clone());
            _romLabel.Text = Path.GetFileNameWithoutExtension(path);
            UpdateProfileLabel();
            _statusLabel.Text = _emulator.IsSmbWorld
                ? $"ROM matched. SMB scenery, object RAM, and {AudioStatus()} are active."
                : Volatile.Read(ref _gameProfile) is GameRecognitionProfile custom
                    ? $"ROM runs with {AudioStatus()} and custom game profile '{custom.Name}'."
                    : $"ROM runs with {AudioStatus()}. Create a game profile to recognize background objects.";
            _captureFaultShown = false;
            _pauseButton.Text = "PAUSE";
            _projectionCycleTimer.Stop();
            _frameTimer.Start();
            _renderer.Focus();
        }
        catch (Exception exception)
        {
            if (_projectionCycleClock.IsRunning)
            {
                _projectionCycleTimer.Start();
            }
            _statusLabel.Text = exception.Message;
            MessageBox.Show(this, exception.Message, "Could not load ROM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenProfileEditor()
    {
        ProjectionProfile current = Volatile.Read(ref _projectionProfile);
        using ProfileEditorForm editor = new(current);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            Volatile.Write(ref _projectionProfile, editor.EditedProfile.Clone());
            _presentation.ProjectionProfile = editor.EditedProfile.Clone();
            SavePresentation();
            Interlocked.Increment(ref _romGeneration);
            UpdateProfileLabel();
            _statusLabel.Text = $"Projection profile '{editor.EditedProfile.Name}' saved and applied.";
            QueueFrameCapture();
        }
        _renderer.Focus();
    }

    private void OpenGameProfileEditor()
    {
        if (!_emulator.IsLoaded || string.IsNullOrWhiteSpace(_emulator.RomPath))
        {
            MessageBox.Show(this, "Load an NES ROM before creating its game profile.", "No ROM loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        NesFrame? frame = Volatile.Read(ref _latestFrame);
        if (frame is null)
        {
            QueueFrameCapture();
            MessageBox.Show(this, "The first game snapshot is still being captured. Try again in a moment.", "Snapshot not ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        GameRecognitionProfile current = Volatile.Read(ref _gameProfile)?.Clone()
            ?? GameRecognitionProfile.Create(_emulator.RomPath, _emulator.RomSha256);
        using GameProfileEditorForm editor = new(
            current,
            frame,
            _emulator.IsSmbWorld,
            _emulator.RomPath,
            _emulator.RomSha256,
            () => _emulator.CaptureFrame(),
            _presentation,
            _emulator.BuiltInGameProfile);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            Volatile.Write(ref _gameProfile, editor.EditedProfile.Clone());
            Interlocked.Increment(ref _romGeneration);
            UpdateProfileLabel();
            _statusLabel.Text = $"Game profile '{editor.EditedProfile.Name}' saved and applied with {editor.EditedProfile.BackgroundRules.Count} background patterns.";
            QueueFrameCapture();
        }
        _renderer.Focus();
    }

    private void UpdateProfileLabel()
    {
        ProjectionProfile profile = Volatile.Read(ref _projectionProfile);
        if (!_emulator.IsLoaded)
        {
            _profileLabel.Text = $"USER · {profile.Name.ToUpperInvariant()}";
            _profileLabel.ForeColor = MutedColor;
            return;
        }

        GameRecognitionProfile? gameProfile = Volatile.Read(ref _gameProfile);
        string detector = _emulator.IsSmbWorld
            ? gameProfile is null ? "SMB BUILT-IN DETECTOR" : $"SMB + {gameProfile.Name}"
            : gameProfile?.Name ?? "GENERIC SPRITES ONLY";
        _profileLabel.Text = $"● {detector}\nUSER · {profile.Name.ToUpperInvariant()}";
        _profileLabel.ForeColor = _emulator.IsSmbWorld ? Lime : Color.FromArgb(255, 187, 80);
    }

    private string AudioStatus()
    {
        if (!_emulator.IsAudioEnabled)
        {
            return "audio disabled";
        }

        string firstDevice = _emulator.AudioDevices
            .Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "default output";
        return $"native audio ({firstDevice})";
    }

    private void QueueFrameCapture()
    {
        if (_closing || !_emulator.IsLoaded || Interlocked.Exchange(ref _captureInProgress, 1) != 0)
        {
            return;
        }

        int generation = Volatile.Read(ref _romGeneration);
        _ = Task.Run(() => CaptureFrameInBackground(generation));
    }

    private void CaptureFrameInBackground(int generation)
    {
        SmbScene? scene = null;
        NesFrame? capturedFrame = null;
        Exception? fault = null;
        try
        {
            long captureStart = System.Diagnostics.Stopwatch.GetTimestamp();
            NesFrame? frame = _emulator.CaptureFrame();
            double captureMs=System.Diagnostics.Stopwatch.GetElapsedTime(captureStart).TotalMilliseconds;
            PerformanceMetrics.Capture.Add(captureMs);
            if (frame is null) return;
            if (generation != Volatile.Read(ref _romGeneration)) return;
            capturedFrame = frame;
            Volatile.Write(ref _latestFrame, frame);
            long recognitionStart=System.Diagnostics.Stopwatch.GetTimestamp();
            scene = BuildCapturedFrame(frame,generation);
            // Explicit diagnostics only. The null production observer does not
            // allocate a sample or change capture/build/delivery scheduling.
            if(scene is not null && FrameBuildObserverForTest is {} observer)
                observer(new(frame.Sequence,captureMs,System.Diagnostics.Stopwatch.GetElapsedTime(recognitionStart).TotalMilliseconds));
        }
        catch (Exception exception)
        {
            fault = exception;
        }
        finally
        {
            Volatile.Write(ref _captureInProgress, 0);
        }

        if (scene is not null)
        {
            PublishCapturedScene(scene, capturedFrame!, generation);
        }
        else if (fault is not null)
        {
            PublishCaptureFault(fault, generation);
        }
    }

    private SmbScene? BuildCapturedFrame(NesFrame frame,int generation)
    {
        Interlocked.Increment(ref _sceneBuildRequests);
        if(generation!=Volatile.Read(ref _romGeneration))return null;
        ProjectionProfile projectionProfile=Volatile.Read(ref _projectionProfile);
        GameRecognitionProfile? gameProfile=Volatile.Read(ref _gameProfile);
        // Paired snapshots own immutable arrays and keep their sequence until
        // the next native publication. The 16ms UI tick can request them twice
        // (or continuously while paused); recognition/topology need run once.
        // Unpaired captures retain their existing behavior. Profile references
        // and the ROM/edit generation are part of the key, not just sequence.
        bool coherent=frame.CaptureScanline==96&&frame.NativeScreenSequence==frame.Sequence&&frame.NativeScreenPixels?.Length==61440;
        SceneBuildKey? previous=Volatile.Read(ref _lastSceneBuildKey);
        if(ReuseCapturedScenesForTest&&coherent&&previous?.Generation==generation&&previous.Sequence==frame.Sequence&&
            ReferenceEquals(previous.Projection,projectionProfile)&&ReferenceEquals(previous.Game,gameProfile))
        { Interlocked.Increment(ref _sceneBuildSkipped);return null; }
        long recognitionStart=System.Diagnostics.Stopwatch.GetTimestamp();
        SmbScene scene=_profile.Build(frame,_emulator.IsSmbWorld,projectionProfile,gameProfile);
        try
        {
            // Build cached boundaries on the worker, never in the input/UI tick.
            foreach(SceneObject item in scene.Objects)if(item.ProjectionEnabled)_=item.PixelGeometry.Topology;
        }
        catch{scene.Dispose();throw;}
        // Commit only a completely built scene. Retired workers cannot block
        // the next generation, and an unpaired build clears the reuse key.
        Volatile.Write(ref _lastSceneBuildKey,coherent?new(generation,frame.Sequence,projectionProfile,gameProfile):null);
        Interlocked.Increment(ref _sceneBuildCompleted);
        PerformanceMetrics.Recognition.Add(System.Diagnostics.Stopwatch.GetElapsedTime(recognitionStart).TotalMilliseconds);
        return scene;
    }

    private void PublishCapturedScene(SmbScene scene,NesFrame captured,int generation)
        => PublishScene(scene,generation,captured.NativeScreenPixels);

    private void PublishScene(SmbScene scene, int generation,int[]? nativePixels)
    {
        if (_closing || IsDisposed || !IsHandleCreated)
        {
            scene.Dispose();
            return;
        }

        // The worker has already released its capture slot. A newer capture
        // may now update _latestFrame: carry this scene's exact pixels instead.
        PendingScene next=new(scene,generation,nativePixels);
        while(true)
        {
            var previous=Volatile.Read(ref _pendingScene);
            if(previous?.Generation==generation&&previous.Scene.Sequence>=scene.Sequence){scene.Dispose();return;}
            if(ReferenceEquals(Interlocked.CompareExchange(ref _pendingScene,next,previous),previous)){previous?.Scene.Dispose();break;}
        }
        QueueSceneDelivery();
    }

    private void QueueSceneDelivery()
    {
        if(_closing){Interlocked.Exchange(ref _pendingScene,null)?.Scene.Dispose();return;}
        if(Interlocked.Exchange(ref _sceneDeliveryQueued,1)!=0)return;
        try{BeginInvoke((Action)DeliverNewestScene);}
        catch(InvalidOperationException){Interlocked.Exchange(ref _pendingScene,null)?.Scene.Dispose();Volatile.Write(ref _sceneDeliveryQueued,0);}
    }

    private void DeliverNewestScene()
    {
        try
        {
            PendingScene? pending=Interlocked.Exchange(ref _pendingScene,null);if(pending is null)return;
            SmbScene scene=pending.Scene;
            if(_closing||pending.Generation!=Volatile.Read(ref _romGeneration)||pending.Generation==_lastPublishedGeneration&&scene.Sequence<=_lastPublishedSequence){scene.Dispose();return;}
            _lastPublishedGeneration=pending.Generation;_lastPublishedSequence=scene.Sequence;
            SceneDeliveryCountForTest++;
            int projected=scene.Objects.Count(item=>item.ProjectionEnabled);
            _objectCountLabel.Text=$"{projected}/{scene.Objects.Count} projected · frame {scene.Sequence}";
            _renderer.NativeScreenPixels=pending.NativePixels;_renderer.SetScene(scene);
            if(_friendlyTabs.TabPages.Count==4)_friendlyProfile.Text=scene.RecognitionProfileName;
        }
        finally{Volatile.Write(ref _sceneDeliveryQueued,0);if(Volatile.Read(ref _pendingScene)is not null)QueueSceneDelivery();}
    }

    private void PublishCaptureFault(Exception exception, int generation)
    {
        if (_closing || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke((Action)(() =>
            {
                if (_closing || generation != Volatile.Read(ref _romGeneration)) return;
                _frameTimer.Stop();
                if (_projectionCycleClock.IsRunning)
                {
                    _projectionCycleTimer.Start();
                }
                _statusLabel.Text = $"Capture stopped: {exception.Message}";
                if (!_captureFaultShown)
                {
                    _captureFaultShown = true;
                    MessageBox.Show(this, exception.ToString(), "Native capture error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The window closed before the background capture could report the fault.
        }
    }

    private void UpdateFrame()
    {
        if(_recorder?.AtLimit==true)_=StopRecordingAsync();
        _renderer.AudioLevel=_audioMeter.Sample(_presentation.Dimensions.AudioReactive&&_emulator.IsLoaded);
        if(_friendlyTabs.TabPages.Count==4)UpdateFriendlyStatus();
        PollInput();
        long now = Environment.TickCount64;
        if (now - _lastDiagnostics > 1000)
        {
            _lastDiagnostics = now;
            _performanceLabel.Text = _renderer.RendererStatus + "\n" + PerformanceMetrics.Summary.Replace(" · ", "\n") +
                $"\nGeometry cache: {GeometryCache.Hits} hits / {GeometryCache.Misses} builds\n{_renderer.DetailStatus}";
        }
        if (_projectionCycleClock.IsRunning)
        {
            // Apply all animated values as one batch. SetScene below performs the
            // only renderer invalidation for this tick.
            ApplyProjectionCycle(invalidateRenderer: true);
        }
        QueueFrameCapture();
    }

    private void TogglePause()
    {
        _=StopRecordingAsync();
        bool paused = _emulator.TogglePause();
        _pauseButton.Text = paused ? "Resume" : "Pause";
    }

    public bool PreFilterMessage(ref Message message)
    {
        if(!_closing && ContainsFocus && message.Msg is WmKeyDown or WmKeyUp && ((Keys)(long)message.WParam&Keys.KeyCode)==Keys.F9)
        {_renderer.RevealOriginal=message.Msg==WmKeyDown;_renderer.Invalidate();return true;}
        if (_closing || !_emulator.IsLoaded || !HasInputFocus ||
            message.Msg is not (WmKeyDown or WmKeyUp))
        {
            return false;
        }

        Keys key = (Keys)(long)message.WParam & Keys.KeyCode;
        if (key == Keys.ShiftKey) key = ((message.LParam.ToInt64() >> 16) & 255) == 0x36 ? Keys.RShiftKey : Keys.LShiftKey;
        if (_inputSettings.IsMapped(key) && (message.Msg == WmKeyUp || (ModifierKeys & (Keys.Control | Keys.Alt)) == 0))
        {
            if(message.Msg==WmKeyDown&&ActiveControl is TrackBar or NumericUpDown or TextBoxBase or ComboBox)return false;
            if (message.Msg == WmKeyDown) _heldKeys.Add(key); else _heldKeys.Remove(key);
            PollInput();
            return true;
        }

        if (key == Keys.F2 && message.Msg == WmKeyDown)
        {
            _=StopRecordingAsync();_renderer.ResetRuntime();
            _emulator.Reset();
            return true;
        }

        return false;
    }

    private void OnGameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.P)
        {
            OpenProfileEditor();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.G)
        {
            OpenGameProfileEditor();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.F2)
        {
            _=StopRecordingAsync();_renderer.ResetRuntime();
            _emulator.Reset();
            e.Handled = true;
        }
    }

    private void OnGameKeyUp(object? sender, KeyEventArgs e)
    {
        if (_inputSettings.IsMapped(e.KeyCode))
        {
            _heldKeys.Remove(e.KeyCode);
            PollInput();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void ReleaseAllButtons()
    {
        _renderer.RevealOriginal=false;_renderer.Invalidate();
        _heldKeys.Clear();
        _emulator.SetInputMask(0);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length == 1 &&
            Path.GetExtension(files[0]).Equals(".nes", StringComparison.OrdinalIgnoreCase))
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            LoadRom(files[0]);
        }
    }

    private void Shutdown()
    {
        if (_closing) return;
        SavePresentation();
        _closing = true;
        Interlocked.Exchange(ref _pendingScene,null)?.Scene.Dispose();
        _friendlyTimer.Dispose();_friendlyTips.Dispose();_moreMenu.Dispose();
        _dimensionStudio?.Close();StopRecordingAsync().GetAwaiter().GetResult();_audioMeter.Dispose();
        Interlocked.Increment(ref _romGeneration);
        _frameTimer.Stop();
        _projectionCycleTimer.Stop();
        _projectionCycleClock.Stop();
        _projectionCycleTimer.Dispose();
        _saveSettingsTimer.Dispose();
        _frameTimer.Dispose();
        _timerResolution.Dispose();
        Application.RemoveMessageFilter(this);
        _emulator.Dispose();
    }

    private void OpenPresentationEditor(string? objectKey = null, string? layerKey = null)
    {
        ReleaseAllButtons();
        using PresentationEditorForm editor = new(_presentation, _renderer.CloneScene());
        if (objectKey is not null && layerKey is not null) editor.SelectProjection(objectKey, layerKey);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            ApplyPresentation(editor.EditedSettings);
            SavePresentation();
            Interlocked.Increment(ref _romGeneration);
            QueueFrameCapture();
        }
        _renderer.Focus();
        _renderer.SelectedObjectKey = null; _renderer.SelectedLayerKey = null;
    }

    private void ApplyPresentation(PresentationSettings settings)
    {
        StopFriendlyBlend();
        _dimensionStudio?.StopAutoBlend();
        _projectionCycleClock.Reset();
        _renderer.ProjectionCycleSeconds = 0;
        _renderer.GeometryCycleSeconds = 0;
        _presentation = settings;
        Volatile.Write(ref _projectionProfile, settings.ProjectionProfile.Clone());
        _renderer.ApplySettings(settings);
        _batchingProjectionValues = true;
        if (_cycleCheckBox is not null) _cycleCheckBox.Checked = settings.Animate;
        _batchingProjectionValues = false;
        SetProjectionCycling(settings.Animate);
        SyncPresentationSliders();
        UpdateProfileLabel();
        foreach (LookCard card in _lookCards) { card.Chosen = settings.Name == LookCatalog.Names[card.LookIndex]; card.Invalidate(); }
        SyncFriendlyControls();
    }

    private void SyncPresentationSliders()
    {
        _batchingProjectionValues = true;
        if(_effectsToggle is not null)_effectsToggle.Checked=_presentation.Effects.Enabled;
        if(_smoothToggle is not null)_smoothToggle.Checked=_presentation.Effects.SmoothTransitions;
        for(int i=0;i<_effectsSliders.Count;i++) SetSliderValue(_effectsSliders[i],(int)(100*(i==0?_presentation.Effects.Lighting:i==1?_presentation.Effects.Glow:_presentation.Effects.Shadows)));
        SetSliderValue(_wExtentSlider, (int)Math.Round(_renderer.DepthAmount * 100));
        SetSliderValue(_cameraSlider, (int)Math.Round(_renderer.Perspective * 100));
        SetSliderValue(_opacitySlider, (int)Math.Round(_renderer.ProjectionOpacity * 100));
        SetSliderValue(_crossSectionsSlider, _renderer.SliceCount);
        SetSliderValue(_projectionRotationSpreadSlider, (int)_renderer.ProjectionRotationSpreadDegrees);
        SyncRotationSliders();
        _batchingProjectionValues = false;
    }

    private void SaveManualRotation()
    {
        StopFriendlyBlend();
        _presentation.RotationAnimation.Enabled = false;
        _presentation.Rotation = new RotationAngles { XY = _renderer.AngleXYDegrees, XZ = _renderer.AngleXZDegrees,
            XW = _renderer.AngleXWDegrees, YZ = _renderer.AngleYZDegrees, YW = _renderer.AngleYWDegrees, ZW = _renderer.AngleZWDegrees };
        ProjectionSettingChanged();
    }

    private void SavePresentation()
    {
        _saveSettingsTimer.Stop();
        if (!_emulator.IsLoaded) return;
        try { PresentationSettingsStore.Save(_emulator.RomSha256, _presentation); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { _statusLabel.Text = "Could not save presentation: " + exception.Message; }
    }

    private void PollInput()
    {
        if (!_emulator.IsLoaded || !HasInputFocus)
        { ReleaseAllButtons(); return; }
        (int mask, bool connected) = _inputSettings.ControllerEnabled ? ControllerInput.Poll() : (0, false);
        _controllerLabel.Text = connected ? "Controller: connected" : "Controller: disconnected";
        _emulator.SetInputMask(_inputSettings.Mask(_heldKeys) | mask);
    }

    private void OpenControlsEditor()
    {
        ReleaseAllButtons();
        using ControlsEditorForm editor = new(_inputSettings);
        if (editor.ShowDialog(this) == DialogResult.OK) { _inputSettings = editor.EditedSettings; SaveInputSettings(); }
        _renderer.Focus();
    }
    private void SaveInputSettings()
    {
        try { InputSettingsStore.Save(_inputSettings); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { _statusLabel.Text = "Could not save controls: " + exception.Message; }
    }
    private void StateAction(bool save)
    {
        if (!_emulator.IsLoaded) { _statusLabel.Text = "Load a ROM before using save states."; return; }
        ReleaseAllButtons();
        try
        {
            if (save) _emulator.SaveState(_selectedStateSlot);
            else
            {
                _=StopRecordingAsync();_renderer.ResetRuntime();
                Interlocked.Increment(ref _romGeneration);
                _emulator.LoadState(_selectedStateSlot);
                _latestFrame = null;
                QueueFrameCapture();
            }
            _statusLabel.Text = $"Slot {_selectedStateSlot} {(save ? "saved" : "loaded")}.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        { _statusLabel.Text = "State operation failed: " + exception.Message; }
        _renderer.Focus();
    }
    private void ToggleCinematic()
    {
        if(_rootLayout is null)return;
        if(_fullscreen)ToggleFullscreen();
        _cinematic=!_cinematic;
        _rootLayout.SuspendLayout();
        _headerPanel!.Visible=_lookDockPanel!.Visible=!_cinematic;
        _sidebarPanel!.Visible=!_cinematic&&_controlsVisible;
        _rootLayout.RowStyles[0].Height=_cinematic?0:66;
        _rootLayout.RowStyles[2].Height=_cinematic?0:LookDockHeight;
        _rootLayout.ColumnStyles[1].Width=!_cinematic&&_controlsVisible?InspectorWidth:0;
        _renderer.PresentationMode=_renderer.CinematicHint=_cinematic;
        _rootLayout.ResumeLayout();_renderer.Invalidate();_renderer.Focus();
    }

    private void ToggleFullscreen()
    {
        if (_rootLayout is null) return;
        if (!_fullscreen)
        {
            _windowedBounds = Bounds; _windowedState = WindowState;
            WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.FromControl(this).Bounds;
            _sidebarPanel!.Visible = false;
            _headerPanel!.Visible = false;
            _rootLayout.ColumnStyles[1].Width = 0; _rootLayout.RowStyles[0].Height = 0;
            _lookDockPanel!.Visible = false; _rootLayout.RowStyles[2].Height = 0;
            _renderer.PresentationMode = true;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable; Bounds = _windowedBounds; WindowState = _windowedState;
            _rootLayout.ColumnStyles[1].Width = _controlsVisible&&!_cinematic ? InspectorWidth : 0; _rootLayout.RowStyles[0].Height = _cinematic?0:66;
            _sidebarPanel!.Visible = _controlsVisible&&!_cinematic;
            _headerPanel!.Visible = !_cinematic;
            _lookDockPanel!.Visible = !_cinematic; _rootLayout.RowStyles[2].Height = _cinematic?0:LookDockHeight;
            _renderer.PresentationMode = _cinematic;
        }
        _fullscreen = !_fullscreen;
        _renderer.Focus();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control|Keys.D:if(_friendlyTabs.TabPages.Count==4)ShowFriendlyPage(1);else OpenDimensionStudio();return true;
            case Keys.Control|Keys.F12:ToggleRecording();return true;
            case Keys.F10: ToggleCinematic();return true;
            case Keys.F11: case Keys.Alt | Keys.Enter: ToggleFullscreen(); return true;
            case Keys.F12: CaptureScreenshot(); return true;
            case Keys.Control | Keys.Tab: ToggleControls(); return true;
            case Keys.Escape when _fullscreen: ToggleFullscreen(); return true;
            case Keys.Escape when _cinematic: ToggleCinematic();return true;
            case Keys.F5: StateAction(true); return true;
            case Keys.F8: StateAction(false); return true;
            case Keys.F3: SetStateSlot(_selectedStateSlot == 1 ? 10 : _selectedStateSlot - 1); return true;
            case Keys.F4: SetStateSlot(_selectedStateSlot == 10 ? 1 : _selectedStateSlot + 1); return true;
            case Keys.Control | Keys.O: OpenRomDialog(); return true;
            case Keys.Control | Keys.L: OpenPresentationEditor(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private void SetStateSlot(int slot) { if (_stateSlot is not null) _stateSlot.Value = slot; }

    private void OpenDimensionStudio()
    {
        if(_dimensionStudio is {IsDisposed:false}){_dimensionStudio.Activate();return;}
        ReleaseAllButtons();
        _dimensionStudio=new(_renderer,()=>_presentation,settings=>{ApplyPresentation(settings);SavePresentation();},
            (a,b,t)=>
            {
                var preserved=_presentation;
                _presentation=LookCrossfader.Blend(a,b,t,preserved);
                _renderer.ApplyCrossfade(a,b,t,preserved);
                _projectionCycleTimer.Stop();_projectionCycleClock.Stop();
                _batchingProjectionValues=true;if(_cycleCheckBox is not null)_cycleCheckBox.Checked=false;_batchingProjectionValues=false;
                SyncPresentationSliders();ProjectionSettingChanged();
            },ToggleRecording,OpenGameProfileEditor)
        {AudioStatus=()=>_audioMeter.Status,RecordStatus=()=>_recorder?.Status??(_finishingRecording?"Finishing AVI…":"Not recording")};
        _dimensionStudio.Show(this);
    }
    private void ToggleRecording()
    {
        if(_recorder is not null){_=StopRecordingAsync();return;}
        if(_finishingRecording)return;
        if(!_emulator.IsLoaded){_statusLabel.Text="Load a ROM before recording video.";return;}
        using SaveFileDialog dialog=new(){Filter="Clean gameplay video (*.avi)|*.avi",FileName="Warp4D-gameplay.avi",Title="Record projected gameplay with game audio"};
        ReleaseAllButtons();if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try
        {
            if(_emulator.IsPaused)TogglePause();
            _recorder=new(dialog.FileName);MesenApi.WaveRecord(_recorder.WavePath);
            if(!MesenApi.WaveIsRecording())throw new IOException("Native game audio recording did not start.");
            _statusLabel.Text="Recording clean 512×480 AVI. Ctrl+F12 to finish (20 min / 800 MB video cap).";
            _renderer.Invalidate();
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {_statusLabel.Text="Recording could not start: "+e.Message;_=StopRecordingAsync();}
    }
    private Task StopRecordingAsync()
    {
        var recorder=_recorder;if(recorder is null)return _recordingFinishTask;_recorder=null;_finishingRecording=true;
        if(MesenApi.WaveIsRecording())MesenApi.WaveStop();
        return _recordingFinishTask=FinishRecorderAsync(recorder);
    }
    private async Task FinishRecorderAsync(CleanVideoRecorder recorder)
    {
        string result;
        try{await recorder.FinishAsync().ConfigureAwait(false);result="Clean gameplay video saved.";}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){result="Video finish failed: "+e.Message;}
        finally{recorder.Dispose();_finishingRecording=false;}
        if(!_closing&&!IsDisposed&&IsHandleCreated)
            try{BeginInvoke((Action)(()=>_statusLabel.Text=result));}catch(InvalidOperationException){}
    }

}
