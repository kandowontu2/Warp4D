using System.Runtime.InteropServices;

namespace Warp4D.Emulation;

internal enum DebugMemoryType
{
    CpuMemory = 0,
    PpuMemory = 1,
    PaletteMemory = 2,
    SpriteMemory = 3,
    SecondarySpriteMemory = 4,
    PrgRom = 5,
    ChrRom = 6,
    ChrRam = 7,
    WorkRam = 8,
    SaveRam = 9,
    InternalRam = 10,
    NametableRam = 11
}

internal enum ControllerType
{
    None = 0,
    StandardController = 1
}

internal enum ConsoleId
{
    Master = 0
}

internal static class MesenApi
{
    private const string DllName = "MesenCore.dll";
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint Warp4DTestChrRamAddressClassification();
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EventDisplayOptions { public fixed uint Colors[27]; public fixed byte Flags[29]; }
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] internal static extern uint TakeEventSnapshot(EventDisplayOptions options);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] internal static extern void GetEventViewerOutput(IntPtr buffer, EventDisplayOptions options);
    internal static unsafe int[] ReadNativeScreen(uint[] workspace)
    {
        uint lines = TakeEventSnapshot(default); if (lines > 312 || lines < 240) throw new InvalidDataException("Invalid native frame height.");
        fixed (uint* p = workspace) GetEventViewerOutput((IntPtr)p, default);
        int[] screen = new int[256*240];
        for (int y = 0; y < 240; y++) for (int x = 0; x < 256; x++) screen[y*256+x] = (int)workspace[(y*2+2)*682+x*2+2] | unchecked((int)0xff000000);
        return screen;
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void InitDll();
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SetFlags(ulong flags);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void InitializeEmu(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string homeFolder,
        IntPtr windowHandle,
        IntPtr viewerHandle,
        [MarshalAs(UnmanagedType.I1)] bool noAudio,
        [MarshalAs(UnmanagedType.I1)] bool noVideo,
        [MarshalAs(UnmanagedType.I1)] bool noInput);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Release();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void LoadROM(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string romPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string patchPath);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Run();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Stop();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Pause(ConsoleId consoleId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Resume(ConsoleId consoleId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Reset();

    // Mesen 0.9.9 InteropDLL/ConsoleWrapper.cpp exports UTF-8 paths and void returns.
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SaveStateFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void LoadStateFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DebugInitialize();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DebugRelease();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DebugGetNametable(
        int nametableIndex,
        int displayMode,
        IntPtr frameBuffer,
        IntPtr tileData,
        IntPtr attributeData);

    [DllImport(DllName, EntryPoint = "DebugGetMemoryState", CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint DebugGetMemoryState(DebugMemoryType memoryType, IntPtr destination);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DebugGetMemorySize(DebugMemoryType memoryType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte DebugGetMemoryValue(DebugMemoryType memoryType, uint address);

    // Mesen's debugger API; used only by explicit developer calibration scripts.
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DebugSetMemoryValue(DebugMemoryType memoryType, uint address, byte value);

    [DllImport(DllName, EntryPoint = "DebugGetPpuScroll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint DebugGetPpuScroll();

    // Mesen 0.9.9 DebugState begins with a 32-byte CPU State, followed by
    // PPUControlFlags (sprite bank at +2, large-sprite flag at +6). Reserve
    // space for the complete native state, including mapper/APU arrays.
    // Layout verified against SourMesen/Mesen GUI.NET/InteropEmu.cs.
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DebugGetState(IntPtr destination);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void NotificationCallback(int type, IntPtr parameter);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr RegisterNotificationCallback(ConsoleId consoleId, NotificationCallback callback);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void UnregisterNotificationCallback(IntPtr listener);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DebugSetPpuViewerScanlineCycle(int id, int scanline, int cycle);

    internal static unsafe (int SpriteBank, bool Large, byte Mask) ReadSpriteMode()
    {
        byte* state = stackalloc byte[8192];
        new Span<byte>(state, 8192).Clear();
        DebugGetState((IntPtr)state);
        int bank = state[34] | state[35] << 8;
        if (bank is not (0 or 4096) || state[38] > 1)
            throw new InvalidDataException("Unexpected native PPU state layout.");
        // CPUState=32 bytes, PPUControlFlags=16, PPUStatusFlags=3, then
        // one alignment byte and PPUState.Control/Mask at offsets 52/53.
        // Verify the raw mask against all eight decoded control flags rather
        // than trusting a plausible byte from a mismatched native layout.
        int mask = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            if (state[40 + bit] > 1) throw new InvalidDataException("Unexpected native PPU mask flags.");
            mask |= state[40 + bit] << bit;
        }
        if (state[53] != mask) throw new InvalidDataException("Unexpected native PPU mask layout.");
        return (bank, state[38] != 0, (byte)mask);
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DebugSetInputOverride(int port, int buttonMask);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SetControllerType(int port, ControllerType controllerType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SetMasterVolume(double volume, double volumeReduction, ConsoleId consoleId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SetSampleRate(uint sampleRate);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SetAudioLatency(uint milliseconds);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SetAudioDevice(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string audioDevice);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr GetAudioDevices();
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void WaveRecord([MarshalAs(UnmanagedType.LPUTF8Str)] string filename);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void WaveStop();
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return:MarshalAs(UnmanagedType.I1)] internal static extern bool WaveIsRecording();
}
