using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Warp4D;

// Diagnostic read-only subscription. Never wakes the monitor or changes power
// settings, execution state, input, window focus, or any user application.
internal sealed class OwnedDisplayPowerProbe:NativeWindow,IDisposable
{
    private static readonly Guid ConsoleSetting=new("6fe69556-704a-47a0-8f24-c28d936fda47");
    private static readonly Guid SessionSetting=new("2b84c20e-ad23-4ddf-93db-05ffbd7efca5");
    private readonly List<object> _events=[];
    private IntPtr _consoleNotification,_sessionNotification;
    private int? _consoleState,_sessionState;
    private readonly int _consoleError,_sessionError;
    internal OwnedDisplayPowerProbe(Form owned)
    {
        AssignHandle(owned.Handle);
        Guid console=ConsoleSetting,session=SessionSetting;
        _consoleNotification=RegisterPowerSettingNotification(Handle,ref console,0);
        if(_consoleNotification==IntPtr.Zero)_consoleError=Marshal.GetLastWin32Error();
        _sessionNotification=RegisterPowerSettingNotification(Handle,ref session,0);
        if(_sessionNotification==IntPtr.Zero)_sessionError=Marshal.GetLastWin32Error();
    }
    internal object Observe()
    {
        Stopwatch time=Stopwatch.StartNew();
        while(time.ElapsedMilliseconds<1500&&(_consoleState is null||_sessionState is null)){Application.DoEvents();Thread.Sleep(20);}
        return new{ConsoleNotificationRegistered=_consoleNotification!=IntPtr.Zero,SessionNotificationRegistered=_sessionNotification!=IntPtr.Zero,ConsoleRegistrationError=_consoleError,SessionRegistrationError=_sessionError,ConsoleDisplayState=_consoleState,SessionDisplayState=_sessionState,StateMeanings="0=off,1=on,2=dim;null=no notification observed",Events=_events};
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==0x218&&message.WParam.ToInt64()==0x8013&&message.LParam!=IntPtr.Zero)
        {
            Guid setting=Marshal.PtrToStructure<Guid>(message.LParam);int length=Marshal.ReadInt32(message.LParam,16);
            if(length==4&&(setting==ConsoleSetting||setting==SessionSetting))
            {
                int state=Marshal.ReadInt32(message.LParam,20);
                if(setting==ConsoleSetting)_consoleState=state;else _sessionState=state;
                _events.Add(new{Setting=setting==ConsoleSetting?"console":"session",State=state,TimeUtc=DateTime.UtcNow});
            }
        }
        base.WndProc(ref message);
    }
    public void Dispose()
    {
        if(_consoleNotification!=IntPtr.Zero){UnregisterPowerSettingNotification(_consoleNotification);_consoleNotification=IntPtr.Zero;}
        if(_sessionNotification!=IntPtr.Zero){UnregisterPowerSettingNotification(_sessionNotification);_sessionNotification=IntPtr.Zero;}
        ReleaseHandle();
    }
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr RegisterPowerSettingNotification(IntPtr window,ref Guid setting,uint flags);
    [DllImport("user32.dll")]private static extern bool UnregisterPowerSettingNotification(IntPtr notification);
}
