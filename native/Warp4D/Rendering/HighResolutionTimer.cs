using System.Runtime.InteropServices;

namespace Warp4D.Rendering;

internal sealed class HighResolutionTimer : IDisposable
{
    private bool _active = timeBeginPeriod(1) == 0;
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);
    public void Dispose() { if (_active) { timeEndPeriod(1); _active = false; } }
}
