using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Warp4D.Profiles;

namespace Warp4D.Emulation;

internal sealed class NesEmulator : IDisposable
{
    private const string FamiDashSha256 = "FDCC6C107CC64A245CE8F07188CBB50558EF435B908105F4CC88004D205BDA26";
    private const int FamiDashGameStateAddress = 0x049C;
    private const int FamiDashScrollXAddress = 0x04A6;
    private const int FamiDashScrollYAddress = 0x04AA;
    private readonly object _nativeLock = new();
    // MesenCore exports one process-global console, not one console per wrapper.
    // A second InitDll would replace the console while the first Run is active.
    private static readonly object NativeSessionGate = new();
    private static NesEmulator? _activeNativeSession;
    private static readonly object TraceLock = new();
    private static long _traceWriteFailures;
    internal static long TraceWriteFailuresForTest=>Interlocked.Read(ref _traceWriteFailures);
    internal void HoldNativeLockForTest(Action action){lock(_nativeLock)action();}
    private readonly ViewportStabilizer _viewportStabilizer = new();
    private bool _initialized;
    private bool _loaded;
    private bool _debugInitialized;
    private long _sequence;
    private int _inputMask;
    private bool _paused;
    private Thread? _runThread;
    private NesFrame? _scanlineFrame;
    private bool _scanlineMode;
    private IntPtr _notificationListener;
    private MesenApi.NotificationCallback? _notificationCallback;
    private Exception? _captureError;
    private bool _mmc5Capture;
    private NesFrame? _pendingScanlineFrame;
    private int[][]? _hudPixels;
    internal bool MeasureNativeCaptureForTest {get;set;}
    private readonly object _captureMetricsGate=new();
    private NativeCaptureStats _nativeCaptureStats;
    internal NativeCaptureStats NativeCaptureStatsForTest {get{lock(_captureMetricsGate)return _nativeCaptureStats;}}
    internal bool UseNametablePixelReuseForTest {get;set;}=false;
    private NametablePixelSnapshots? _nametablePixelSnapshots;
    internal object NametablePixelReuseStatsForTest=>new{Hits=_nametablePixelSnapshots?.Hits??0,Misses=_nametablePixelSnapshots?.Misses??0,ScratchPixels=_nametablePixelSnapshots?.ScratchPixels??0,RetainedPixels=_nametablePixelSnapshots?.RetainedPixels??0};
    private int[]? _nativeScreen;
    private readonly uint[] _nativeWorkspace = new uint[682*624];
    private int _nativeFrameCounter;
    private bool _smbInjuryFullRate;
    private long _smbInjuryFullRatePairs;
    private long _pairedPlayfieldCopies;
    private long _discardedPlayfieldCopiesAvoided;
    private string? _knownGame;

    public string? RomPath { get; private set; }
    public bool IsLoaded => _loaded;
    public bool IsSmbWorld { get; private set; }
    public bool IsFamiDash { get; private set; }
    public GameRecognitionProfile? BuiltInGameProfile { get; private set; }
    public string RomSha256 { get; private set; } = string.Empty;
    public bool IsPaused => _paused;
    internal int InputMaskForTest => _inputMask;
    internal long SmbInjuryFullRatePairsForTest=>Interlocked.Read(ref _smbInjuryFullRatePairs);
    internal (long Copies, long Avoided) PairedCaptureMetricsForTest =>
        (Interlocked.Read(ref _pairedPlayfieldCopies), Interlocked.Read(ref _discardedPlayfieldCopiesAvoided));
    public bool IsAudioEnabled { get; private set; }
    public string AudioDevices { get; private set; } = string.Empty;

    public void Initialize(IntPtr windowHandle = default, IntPtr viewerHandle = default)
    {
        lock(_nativeLock)
        lock(NativeSessionGate)
        {
            if(_initialized)return;
            if(_activeNativeSession is not null && !ReferenceEquals(_activeNativeSession,this))
                throw new InvalidOperationException("Only one native emulator session can run in this process. Close the existing session before opening another.");
            _activeNativeSession=this;
            bool nativeCreated=false;
            try {InitializeOwned(windowHandle,viewerHandle,ref nativeCreated);}
            catch
            {
                try {if(nativeCreated)MesenApi.Release();}
                finally {_activeNativeSession=null;}
                throw;
            }
        }
    }

    private void InitializeOwned(IntPtr windowHandle,IntPtr viewerHandle,ref bool nativeCreated)
    {
        if (_initialized)
        {
            return;
        }

        string? overriddenHome = Environment.GetEnvironmentVariable("WARP4D_HOME");
        string home = string.IsNullOrWhiteSpace(overriddenHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Warp4D")
            : Path.GetFullPath(overriddenHome);
        Directory.CreateDirectory(home);

        Trace("InitDll");
        MesenApi.InitDll();
        nativeCreated=true;
        bool canPlayAudio = windowHandle != IntPtr.Zero && viewerHandle != IntPtr.Zero;
        Trace($"InitializeEmu audio={canPlayAudio} window=0x{windowHandle.ToInt64():X} viewer=0x{viewerHandle.ToInt64():X}");
        MesenApi.InitializeEmu(
            home,
            windowHandle,
            viewerHandle,
            noAudio: !canPlayAudio,
            noVideo: true,
            noInput: true);
        // Warp4D owns cartridge selection. Disable Mesen's recent-game snapshot
        // writer: its raw-video pointer is not reliable when native video is off,
        // particularly after a paused state load. No ROM paths are cached by it.
        MesenApi.SetFlags(0x20000UL);

        if (canPlayAudio)
        {
            // Mesen's Windows audio manager is only constructed when both native
            // handles are present. An empty device name selects DirectSound's
            // current default output device.
            MesenApi.SetAudioDevice(string.Empty);
            MesenApi.SetAudioLatency(60);
            MesenApi.SetSampleRate(48_000);
            MesenApi.SetMasterVolume(2.5, 0, ConsoleId.Master);

            IntPtr devicesPointer = MesenApi.GetAudioDevices();
            AudioDevices = devicesPointer == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUTF8(devicesPointer) ?? string.Empty;
            IsAudioEnabled = true;
            Trace($"DirectSound initialized; devices={AudioDevices.Replace("||", ", ")}");
        }
        else
        {
            IsAudioEnabled = false;
            Trace("Audio disabled because the emulator is running headless");
        }

        Trace("SetControllerType");
        MesenApi.SetControllerType(0, ControllerType.StandardController);
        _initialized = true;
        Trace("Initialize complete");
    }

    public void Load(string path, IntPtr windowHandle = default, IntPtr viewerHandle = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string absolutePath = Path.GetFullPath(path);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("The selected ROM does not exist.", absolutePath);
        }

        string extension = Path.GetExtension(absolutePath);
        if (!extension.Equals(".nes", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Warp4D currently accepts iNES .nes ROM files.");
        }

        byte[] header = new byte[16];
        using (FileStream stream = File.OpenRead(absolutePath))
        {
            if (stream.Read(header, 0, header.Length) != header.Length ||
                header[0] != (byte)'N' || header[1] != (byte)'E' || header[2] != (byte)'S' || header[3] != 0x1A)
            {
                throw new InvalidDataException("This file does not have a valid iNES header.");
            }
        }

        lock (_nativeLock)
        {
            Trace("Load lock acquired");
            Initialize(windowHandle, viewerHandle);
            if (_loaded)
            {
                StopCoreLoop();
                ClearScanlineCapture();
                if (_debugInitialized)
                {
                    MesenApi.DebugRelease();
                    _debugInitialized = false;
                }
                _loaded = false;
            }

            byte[] romData = File.ReadAllBytes(absolutePath);
            RomSha256 = Convert.ToHexString(SHA256.HashData(romData));
            IsSmbWorld = SmbCartridgeIdentity.IsSupported(romData);
            IsFamiDash = RomSha256 == FamiDashSha256;
            BuiltInGameProfile = BuiltInFamiDashProfile.Create(romData, RomSha256);
            IsFamiDash |= BuiltInGameProfile is not null;
            Trace("LoadROM");
            MesenApi.LoadROM(absolutePath, string.Empty);
            // SMB's alternating Luigi turn reads controller two. Warp4D has
            // one input mapping, so the exact supported SMB cartridges mirror
            // it to both ports. Never mirror into other games' second player.
            MesenApi.SetControllerType(1,IsSmbWorld?ControllerType.StandardController:ControllerType.None);
            Trace("DebugInitialize");
            MesenApi.DebugInitialize();
            _debugInitialized = true;
            int mapper = (header[6] >> 4) | (header[7] & 0xf0);
            // Only the two payload-verified FamiDash builds use this paired
            // path. The older prototype hash keeps its legacy capture mode.
            _knownGame = BuiltInGameProfile is not null ? "famidash" : BuiltInGameProfiles.Identify(romData)?.Id;
            if(IsSmbWorld) _knownGame="smb";
            BuiltInGameProfile ??= BuiltInGameProfiles.Create(romData, RomSha256);
            // These identified cartridges need tiles, sprite patterns and
            // completed video from the same emulated frame. Contra's base
            // assembly/animation and Mega Man 2's banked animation are also
            // unsafe to audit against stale video.
            _scanlineMode = _knownGame is "famidash" or "smb" ||
                (mapper is 4 or 5 || _knownGame is "castlevania" or "icarus" or "tetris" or "metroid" or "contra" or "megaman2") && !IsFamiDash;
            _mmc5Capture = mapper == 5;
            _scanlineFrame = null; _captureError = null;
            _nativeScreen = null; _nativeFrameCounter = 0;
            lock(_captureMetricsGate)_nativeCaptureStats=default;
            _smbInjuryFullRate=false;Interlocked.Exchange(ref _smbInjuryFullRatePairs,0);
            Interlocked.Exchange(ref _pairedPlayfieldCopies,0);
            Interlocked.Exchange(ref _discardedPlayfieldCopiesAvoided,0);
            if (_scanlineMode || _knownGame is not null)
            {
                _notificationCallback = (type, parameter) =>
                {
                    // Runs synchronously on the emulation thread at a known
                    // playfield scanline. Never acquire _nativeLock here:
                    // Stop() can be waiting for this thread while holding it.
                    if (type != 15 || !_loaded) return;
                    try
                    {
                        switch (parameter.ToInt64())
                        {
                            case 404:
                                // SMB injury blink omits the player on alternate
                                // native frames. Fixed half-rate sampling aliases
                                // that into permanently visible/absent sprites.
                                // Read one RAM byte only for exact supported SMB;
                                // retain every paired phase during injury only.
                                _smbInjuryFullRate=IsSmbWorld&&MesenApi.DebugGetMemoryValue(DebugMemoryType.InternalRam,0x079e)!=0;
                                // Known cartridges publish paired video only
                                // on alternate completed frames (case 407).
                                // Do not copy/decode tiles and sprite patterns
                                // for the intervening frame we already discard.
                                if (_knownGame is not null && !_smbInjuryFullRate && (_nativeFrameCounter & 1) == 0)
                                {
                                    Interlocked.Increment(ref _discardedPlayfieldCopiesAvoided);
                                    break;
                                }
                                if (_knownGame is not null) Interlocked.Increment(ref _pairedPlayfieldCopies);
                                NesFrame frame = CaptureFrameCore(playfieldSnapshot: true);
                                if (_mmc5Capture || _knownGame is not null) _pendingScanlineFrame = frame;
                                else Volatile.Write(ref _scanlineFrame, frame);
                                break;
                            case 405 when _pendingScanlineFrame is not null:
                                // MMC5 selects sprite CHR during cycles 257..320.
                                byte[] chr = GetMemory(DebugMemoryType.PpuMemory, 16384).AsSpan(0, 8192).ToArray();
                                _pendingScanlineFrame = _pendingScanlineFrame with { Chr = chr };
                                if (_knownGame is null)
                                {
                                    Volatile.Write(ref _scanlineFrame, _pendingScanlineFrame);
                                    _pendingScanlineFrame = null;
                                }
                                break;
                            case 406:
                                _hudPixels = CaptureNametables().Pixels;
                                break;
                            case 407:
                                if ((++_nativeFrameCounter & 1) == 0||_smbInjuryFullRate)
                                {
                                    _nativeScreen = ReadNativeVideoForCapture();
                                    if (_pendingScanlineFrame is not null)
                                    {
                                        if(_smbInjuryFullRate)Interlocked.Increment(ref _smbInjuryFullRatePairs);
                                        Volatile.Write(ref _scanlineFrame, _pendingScanlineFrame with
                                        {
                                            NativeScreenPixels = _nativeScreen,
                                            NativeScreenSequence = _pendingScanlineFrame.Sequence
                                        });
                                    }
                                }
                                // Keep the last coherent pair on skipped video
                                // frames. Never publish fresh tiles over old video.
                                _pendingScanlineFrame = null;
                                break;
                        }
                    }
                    catch (Exception e) { _captureError = e; }
                };
                _notificationListener = MesenApi.RegisterNotificationCallback(ConsoleId.Master, _notificationCallback);
                if (_scanlineMode) MesenApi.DebugSetPpuViewerScanlineCycle(404, 96, 0);
                if (_mmc5Capture) MesenApi.DebugSetPpuViewerScanlineCycle(405, 96, 300);
                if (_knownGame is not null) MesenApi.DebugSetPpuViewerScanlineCycle(407, 240, 0);
                else if (_scanlineMode) MesenApi.DebugSetPpuViewerScanlineCycle(406, 216, 0);
            }
            Trace("Input override");
            _inputMask = 0;
            ApplyInputOverride();
            _loaded = true;
            _paused = false;
            _viewportStabilizer.Reset();
            RomPath = absolutePath;
            StartCoreLoop();
        }
    }

    public NesFrame? CaptureFrame()
    {
        lock (_nativeLock)
        {
            if (!_loaded || !_debugInitialized)
            {
                return null;
            }
            if (_captureError is not null) throw new InvalidOperationException("Native scanline capture failed.", _captureError);
            if (_scanlineMode) return Volatile.Read(ref _scanlineFrame);
            return CaptureFrameCore();
        }
    }

    private unsafe NesFrame CaptureFrameCore(bool playfieldSnapshot = false)
    {
        if(!MeasureNativeCaptureForTest)return CaptureFrameCoreUnmeasured(playfieldSnapshot);
        long bytes=GC.GetAllocatedBytesForCurrentThread(),start=System.Diagnostics.Stopwatch.GetTimestamp();
        try{return CaptureFrameCoreUnmeasured(playfieldSnapshot);}
        finally{RecordCaptureStage(0,bytes,start);}
    }

    private unsafe NesFrame CaptureFrameCoreUnmeasured(bool playfieldSnapshot)
    {
            var (pixels, tiles, attributes) = CaptureNametables();

            byte[] oam = GetMemory(DebugMemoryType.SpriteMemory, 256);
            // Read mapped PPU pattern memory, not the first bank of a CHR ROM.
            // MMC3 games change these banks continuously during play.
            byte[] chr = GetMemory(DebugMemoryType.PpuMemory, 16384).AsSpan(0, 8192).ToArray();
            byte[] palette = GetMemory(DebugMemoryType.PaletteMemory, 32);
            byte[] ram = GetMemory(DebugMemoryType.InternalRam, 0x800);
            (int spriteBank, bool largeSprites, byte ppuMask) = MesenApi.ReadSpriteMode();
            uint packedScroll = MesenApi.DebugGetPpuScroll();
            int rawScrollX = (int)(packedScroll & 0xFFFF);
            int rawScrollY = (int)(packedScroll >> 16);
            int scrollX = rawScrollX;
            int scrollY = rawScrollY;
            string scrollSource = "PPU register";

            if (IsSmbWorld && playfieldSnapshot)
            {
                // Committed playfield scroll matches this completed frame.
                // SMB's logical RAM camera can already be 1..3 pixels ahead.
                scrollY=0;
                scrollSource="SMB playfield PPU viewport";
            }
            else if (IsSmbWorld && ram.Length > 0x071C)
            {
                // SMB changes the live PPU scroll to zero while drawing its fixed
                // status bar. Sampling that register asynchronously therefore
                // alternates between the level and an old nametable page. These
                // RAM variables are SMB's stable logical gameplay viewport.
                scrollX = (ram[0x071A] << 8) | ram[0x071C];
                scrollY = 0;
                scrollSource = "SMB RAM gameplay viewport";
            }
            else if (_knownGame == "famidash" && playfieldSnapshot)
            {
                // The game has already advanced its logical camera by 2–3
                // pixels for the next frame. At scanline96 the PPU scroll is
                // the committed viewport that produced the paired video.
                scrollSource = "FamiDash playfield PPU viewport";
            }
            else if (IsFamiDash && TryGetFamiDashViewport(ram, out int famiDashX, out int famiDashY, BuiltInGameProfile is not null ? 2 : 0))
            {
                scrollX = famiDashX;
                scrollY = famiDashY;
                scrollSource = "FamiDash RAM logical viewport";
            }
            else if (_knownGame is "smb2" or "smb3" or "castlevania" or "icarus" && playfieldSnapshot)
            {
                // At the fixed playfield scanline the PPU contains this frame's
                // committed camera; FC/FD may already describe the next frame.
                scrollSource = _knownGame.ToUpperInvariant() + " playfield PPU viewport";
            }
            else if (CartridgeViewport.TryGet(_knownGame, ram, out Point cartridgeViewport))
            {
                scrollX = cartridgeViewport.X; scrollY = cartridgeViewport.Y;
                scrollSource = "Verified cartridge RAM viewport";
            }
            else if (_knownGame is "metroid" or "contra" or "smb3" or "megaman2")
            {
                if (_knownGame == "megaman2") { scrollX = ram[0x1f] + (ram[0x20] & 1)*256; scrollY = ram[0x22] % 240; }
                else if (_knownGame == "smb3") { scrollX = ram[0xfd] + (ram[0x12]&1)*256; scrollY = ram[0xfc] + (ram[0x13]&1)*240; }
                else { scrollX = ram[0xfd] + (ram[0xff]&1)*256; scrollY = ram[0xfc] + ((ram[0xff]&2)!=0 ? 240 : 0); }
                scrollSource = "Verified cartridge RAM viewport";
            }
            else
            {
                (scrollX, scrollY, bool filtered) = _viewportStabilizer.Update(rawScrollX, rawScrollY);
                if (filtered)
                {
                    scrollSource = "stabilized PPU viewport";
                }
            }

            return new NesFrame
            {
                NametablePixels = pixels,
                Tiles = tiles,
                Attributes = attributes,
                Oam = oam,
                Chr = chr,
                Palette = palette,
                Ram = ram,
                ScrollX = scrollX,
                ScrollY = scrollY,
                RawScrollX = rawScrollX,
                RawScrollY = rawScrollY,
                ScrollSource = scrollSource,
                Sequence = ++_sequence,
                CaptureScanline = playfieldSnapshot ? 96 : null,
                SpritePatternBase = spriteBank,
                LargeSprites = largeSprites,
                PpuMask = ppuMask,
                HudNametablePixels = _hudPixels
                ,NativeScreenPixels = _nativeScreen
            };
    }

    private unsafe (int[][] Pixels, byte[][] Tiles, byte[][] Attributes) CaptureNametables()
    {
        if(!MeasureNativeCaptureForTest)return CaptureNametablesUnmeasured();
        long bytes=GC.GetAllocatedBytesForCurrentThread(),start=System.Diagnostics.Stopwatch.GetTimestamp();
        try{return CaptureNametablesUnmeasured();}
        finally{RecordCaptureStage(1,bytes,start);}
    }

    private unsafe (int[][] Pixels, byte[][] Tiles, byte[][] Attributes) CaptureNametablesUnmeasured()
    {
        int[][] pixels = new int[4][]; byte[][] tiles = new byte[4][]; byte[][] attributes = new byte[4][];
        for (int table = 0; table < 4; table++)
        {
            pixels[table] = UseNametablePixelReuseForTest?(_nametablePixelSnapshots??=new()).Scratch(table):new int[NesFrame.NametablePixelCount]; tiles[table] = new byte[960]; attributes[table] = new byte[960];
            fixed (int* p = pixels[table]) fixed (byte* t = tiles[table]) fixed (byte* a = attributes[table])
                MesenApi.DebugGetNametable(table, 0, (IntPtr)p, (IntPtr)t, (IntPtr)a);
            if(UseNametablePixelReuseForTest)pixels[table]=_nametablePixelSnapshots!.Publish(table);
        }
        return (pixels, tiles, attributes);
    }

    private int[] ReadNativeVideoForCapture()
    {
        if(!MeasureNativeCaptureForTest)return MesenApi.ReadNativeScreen(_nativeWorkspace);
        long bytes=GC.GetAllocatedBytesForCurrentThread(),start=System.Diagnostics.Stopwatch.GetTimestamp();
        try{return MesenApi.ReadNativeScreen(_nativeWorkspace);}
        finally{RecordCaptureStage(2,bytes,start);}
    }

    internal (int[][] Pixels,byte[][] Tiles,byte[][] Attributes) ReadPausedNametablesForTest(bool reuse)
    {
        lock(_nativeLock)
        {
            if(!_loaded||!_paused)throw new InvalidOperationException("Owned paused session required");
            bool previous=UseNametablePixelReuseForTest;UseNametablePixelReuseForTest=reuse;
            try{return CaptureNametablesUnmeasured();}
            finally{UseNametablePixelReuseForTest=previous;}
        }
    }

    private void RecordCaptureStage(int stage,long bytes,long start)
    {
        long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes,ticks=System.Diagnostics.Stopwatch.GetTimestamp()-start;
        lock(_captureMetricsGate)
        {
            NativeCaptureStage old=stage switch {0=>_nativeCaptureStats.Frames,1=>_nativeCaptureStats.Nametables,_=>_nativeCaptureStats.Video};
            NativeCaptureStage next=new(old.Calls+1,old.AllocatedBytes+allocated,old.ElapsedTicks+ticks);
            _nativeCaptureStats=stage switch {0=>_nativeCaptureStats with{Frames=next},1=>_nativeCaptureStats with{Nametables=next},_=>_nativeCaptureStats with{Video=next}};
        }
    }

    private void ClearScanlineCapture()
    {
        if (_notificationListener != IntPtr.Zero) MesenApi.UnregisterNotificationCallback(_notificationListener);
        _notificationListener = IntPtr.Zero; _notificationCallback = null;
        _scanlineMode = false; _scanlineFrame = null;
        _pendingScanlineFrame = null; _hudPixels = null;
        _nametablePixelSnapshots=null;
    }

    internal static bool TryGetFamiDashViewport(byte[] ram, out int scrollX, out int scrollY, int layoutOffset = 0)
    {
        scrollX = 0;
        scrollY = 0;
        if (ram.Length <= FamiDashScrollYAddress + layoutOffset + 1)
        {
            return false;
        }

        byte gameState = ram[FamiDashGameStateAddress + layoutOffset];
        if (gameState == 0x02)
        {
            // These builds store a LINEAR vertical camera, not PPU-format Y.
            // Their calculate_ppufmt_scroll_y routine converts it before writing
            // $2005. Nametable composition already uses 240-line pages, so use
            // linear Y directly. Treating its high byte as a 240-line page
            // incorrectly moved the background 16 pixels per 256 pixels.
            scrollX = ((ram[FamiDashScrollXAddress + layoutOffset + 1] & 0x01) << 8) |
                ram[FamiDashScrollXAddress + layoutOffset];
            int extendedY = ram[FamiDashScrollYAddress + layoutOffset] |
                (ram[FamiDashScrollYAddress + layoutOffset + 1] << 8);
            scrollY = layoutOffset == 2 ? extendedY % 480 : ((extendedY >> 8) & 0x01) * 240 + (extendedY & 0xFF);
            return true;
        }

        // The title and level-selection IRQ tables temporarily write parallax
        // scrolls such as X=256 and Y=239. Their logical base viewport is (0,0).
        if (gameState is 0x01 or 0x05 or 0x06)
        {
            return true;
        }

        return false;
    }

    public void SetButton(NesButton button, bool pressed)
    {
        // Test loaded state under the same lock as shutdown. Otherwise an input
        // call can observe true, wait for Dispose, then call a released console.
        lock (_nativeLock)
        {
            int bit = (int)button;
            if(pressed)_inputMask|=bit;else _inputMask&=~bit;
            if(_loaded)ApplyInputOverride();
        }
    }

    public void SetInputMask(int mask)
    {
        lock (_nativeLock)
        {
            if (_inputMask == mask) return;
            _inputMask = mask & 255;
            if (_loaded) ApplyInputOverride();
        }
    }

    private void ApplyInputOverride()
    {
        MesenApi.DebugSetInputOverride(0,_inputMask);
        MesenApi.DebugSetInputOverride(1,IsSmbWorld?_inputMask:0);
    }

    public void SetVolume(int percent)
    {
        lock (_nativeLock)
        { if (_initialized && IsAudioEnabled) MesenApi.SetMasterVolume(Math.Clamp(percent, 0, 100) / 100d * 2.5, 0, ConsoleId.Master); }
    }

    public string StatePath(int slot)
    {
        if (!_loaded) throw new InvalidOperationException("Load a ROM before using save states.");
        if (slot is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(slot));
        return Path.Combine(AppPaths.DataDirectory, "states", RomSha256, $"slot-{slot}.mst");
    }
    public void SaveState(int slot)
    {
        lock (_nativeLock)
        {
            string path = StatePath(slot), temporary = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            MesenApi.SaveStateFile(temporary);
            byte[] state = File.ReadAllBytes(temporary);
            ValidateState(state);
            File.Move(temporary, path, true);
        }
    }
    public void LoadState(int slot)
    {
        lock (_nativeLock)
        {
            string path = StatePath(slot);
            ValidateState(File.ReadAllBytes(path));
            MesenApi.LoadStateFile(path);
            _viewportStabilizer.Reset();
            _inputMask = 0; ApplyInputOverride();
        }
    }
    private static void ValidateState(byte[] state)
    {
        if (state.Length < 64 || state[0] != 'M' || state[1] != 'S' || state[2] != 'T')
            throw new InvalidDataException("This slot is not a valid Mesen save state.");
    }

    public void Reset()
    {
        lock (_nativeLock)
        {
            if (_loaded)
            {
                MesenApi.Reset();
            }
        }
    }

    internal int[] ReadPausedScreenForTest()
    {
        lock(_nativeLock)
        {
            if(!_loaded || !_paused)throw new InvalidOperationException("Native reference capture requires this owned session to be paused.");
            // Diagnostic reference only: do not attach it to scene metadata or
            // claim scanline pairing from an arbitrary paused event snapshot.
            return MesenApi.ReadNativeScreen(_nativeWorkspace);
        }
    }

    public bool TogglePause()
    {
        lock (_nativeLock)
        {
            if (!_loaded)
            {
                return false;
            }
            if (_paused)
            {
                MesenApi.Resume(ConsoleId.Master);
            }
            else
            {
                MesenApi.Pause(ConsoleId.Master);
            }
            _paused = !_paused;
            return _paused;
        }
    }

    private static unsafe byte[] GetMemory(DebugMemoryType type, int fallbackSize)
    {
        int size = MesenApi.DebugGetMemorySize(type);
        if (size <= 0)
        {
            size = fallbackSize;
        }
        size = Math.Min(size, Math.Max(fallbackSize, size));

        byte[] data = new byte[size];
        fixed (byte* pointer = data)
        {
            MesenApi.DebugGetMemoryState(type, (IntPtr)pointer);
        }
        return data;
    }

    private void StartCoreLoop()
    {
        if (_runThread?.IsAlive == true)
        {
            return;
        }

        _runThread = new Thread(() =>
        {
            Trace("Run thread entered");
            MesenApi.Run();
            Trace("Run thread returned");
        })
        {
            IsBackground = true,
            Name = "Mesen emulation core"
        };
        _runThread.Start();
        Trace("Run thread started");
    }

    private void StopCoreLoop()
    {
        if (_runThread is null)
        {
            return;
        }
        if (_paused)
        {
            MesenApi.Resume(ConsoleId.Master);
            _paused = false;
        }
        MesenApi.Stop();
        Trace("Native Stop returned; joining Run thread");
        if (_runThread.IsAlive)
        {
            // Stop has already waited for the native loop; never release the core
            // while a managed Run invocation can still access it.
            _runThread.Join();
        }
        _runThread = null;
        Trace("Run thread joined");
    }

    private static void Trace(string message)
    {
        string? path = Environment.GetEnvironmentVariable("WARP4D_TRACE");
        if (!string.IsNullOrWhiteSpace(path))
        {
            // Startup/stop messages come from both UI and Run threads. Optional
            // diagnostics must never terminate emulation if writes overlap or
            // their destination becomes unavailable.
            lock(TraceLock)
            {
                try { File.AppendAllText(path, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}"); }
                catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { Interlocked.Increment(ref _traceWriteFailures); }
            }
        }
    }

    public void Dispose()
    {
        lock (_nativeLock)
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                if (_loaded)
                {
                    StopCoreLoop();
                    ClearScanlineCapture();
                    _loaded = false;
                }
                if (_debugInitialized)
                {
                    MesenApi.DebugRelease();
                    _debugInitialized = false;
                }
            }
            finally
            {
                lock(NativeSessionGate)
                {
                    MesenApi.Release();
                    _inputMask = 0;
                    _initialized = false;
                    IsAudioEnabled = false;
                    AudioDevices = string.Empty;
                    if(ReferenceEquals(_activeNativeSession,this))_activeNativeSession=null;
                }
            }
        }
    }
}

[Flags]
internal enum NesButton
{
    A = 0x01,
    B = 0x02,
    Select = 0x04,
    Start = 0x08,
    Up = 0x10,
    Down = 0x20,
    Left = 0x40,
    Right = 0x80
}
