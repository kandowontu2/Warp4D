using System.Runtime.InteropServices;

namespace Warp4D.Rendering;

// The stage is a native child drawable, not a second game window. All input is
// forwarded to the existing WinForms control so its picking/axis locks survive.
internal sealed class GpuStageWindow : NativeWindow, IDisposable
{
    private readonly Control _owner;
    private Rectangle _bounds;
    internal IntPtr DeviceContext { get; private set; }
    internal bool PixelFormatSet { get; set; }
    internal Size StageSize => _bounds.Size;
    internal bool Shown { get; private set; }
    internal object DrawableDiagnosticForTest => new
    {
        DeviceContextBelongsToStage = WindowFromDC(DeviceContext) == Handle,
        ClassStyle = GetClassLongPtr(Handle,-26).ToInt64(),
        PixelFormat = GetPixelFormat(DeviceContext),
        StageWidth = StageSize.Width,
        StageHeight = StageSize.Height
    };

    internal GpuStageWindow(Control owner)
    {
        _owner = owner;
        CreateHandle(new CreateParams { Caption = "Warp4D native GPU stage", Parent = owner.Handle,
            Style = 0x40000000 | 0x04000000 | 0x02000000, Width = 1, Height = 1 });
        DeviceContext = GetDC(Handle);
        if (DeviceContext == IntPtr.Zero) throw new InvalidOperationException("Could not create the GPU stage drawable.");
    }

    internal void Position(Rectangle bounds)
    {
        if (_bounds == bounds) return;
        _bounds = bounds;
        if (!SetWindowPos(Handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0014))
            throw new InvalidOperationException("Could not position the GPU stage.");
    }

    internal void ShowStage(bool show)
    {
        if (Shown == show) return;
        ShowWindow(Handle, show ? 8 : 0); // SW_SHOWNA: never steal keyboard focus.
        Shown = show;
    }

    protected override void WndProc(ref Message message)
    {
        // DrawToBitmap paints the stage through the owner's readback path. A
        // native child must not overwrite that image during recursive WM_PRINT.
        if (message.Msg is 0x0317 or 0x0318) return;
        if (message.Msg == 0x0014) { message.Result = (IntPtr)1; return; } // no GDI erase/flicker
        if (message.Msg == 0x000F)
        {
            ValidateRect(Handle, IntPtr.Zero);
            _owner.Invalidate(_bounds);
            return;
        }
        if (message.Msg is >= 0x0200 and <= 0x020E)
        {
            IntPtr location = message.LParam;
            if (message.Msg is not (0x020A or 0x020E)) // wheel coordinates already screen-relative
            {
                long packed = location.ToInt64();
                int x = (short)(packed & 0xffff) + _bounds.X;
                int y = (short)((packed >> 16) & 0xffff) + _bounds.Y;
                location = (IntPtr)((y & 0xffff) << 16 | (x & 0xffff));
            }
            if (message.Msg == 0x0200)
            {
                TrackMouseEventData tracking = new() { Size = (uint)Marshal.SizeOf<TrackMouseEventData>(), Flags = 2, Window = Handle };
                TrackMouseEvent(ref tracking);
            }
            message.Result = SendMessage(_owner.Handle, message.Msg, message.WParam, location);
            return;
        }
        if (message.Msg == 0x02A3) { SendMessage(_owner.Handle, message.Msg, message.WParam, message.LParam); return; }
        if (message.Msg == 0x0020) { SetCursor(_owner.Cursor.Handle); message.Result = (IntPtr)1; return; }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (DeviceContext != IntPtr.Zero) { ReleaseDC(Handle, DeviceContext); DeviceContext = IntPtr.Zero; }
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential)] private struct TrackMouseEventData { public uint Size, Flags; public IntPtr Window; public uint HoverTime; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromDC(IntPtr dc);
    [DllImport("user32.dll",EntryPoint="GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr window,int index);
    [DllImport("gdi32.dll")] private static extern int GetPixelFormat(IntPtr dc);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool ValidateRect(IntPtr window, IntPtr rect);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TrackMouseEventData tracking);
    [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
}
