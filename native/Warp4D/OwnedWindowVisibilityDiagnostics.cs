using System.Runtime.InteropServices;
using System.Text;

namespace Warp4D;

// Read-only metadata for an explicitly owned diagnostic form. Never inspect
// other application titles, move a desktop, unlock a session or change focus.
internal static class OwnedWindowVisibilityDiagnostics
{
    internal static object Read(Form owned)
    {
        int cloakHr=DwmGetWindowAttribute(owned.Handle,14,out uint cloaked,4);
        IntPtr input=OpenInputDesktop(0,false,1);int inputError=input==IntPtr.Zero?Marshal.GetLastWin32Error():0;
        string? inputName;
        try{inputName=input==IntPtr.Zero?null:Name(input);}finally{if(input!=IntPtr.Zero)CloseDesktop(input);}
        int? virtualDesktopHr=null;bool? currentVirtualDesktop=null;object? manager=null;
        try
        {
            Type? type=Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"));
            if(type is not null)
            {
                manager=Activator.CreateInstance(type);
                if(manager is IVirtualDesktopManager desktop)
                {virtualDesktopHr=desktop.IsWindowOnCurrentVirtualDesktop(owned.Handle,out bool current);if(virtualDesktopHr==0)currentVirtualDesktop=current;}
            }
        }
        catch(COMException ex){virtualDesktopHr=ex.HResult;}
        finally{if(manager is not null&&Marshal.IsComObject(manager))Marshal.FinalReleaseComObject(manager);}
        return new{ManagedVisible=owned.Visible,NativeVisible=IsWindowVisible(owned.Handle),Iconic=IsIconic(owned.Handle),CloakQueryHResult=cloakHr,CloakFlags=cloaked,ThreadDesktopName=Name(GetThreadDesktop(GetCurrentThreadId())),InputDesktopName=inputName,InputDesktopError=inputError,VirtualDesktopQueryHResult=virtualDesktopHr,OnCurrentVirtualDesktop=currentVirtualDesktop};
    }
    private static string? Name(IntPtr handle)
    {StringBuilder text=new(256);return handle!=IntPtr.Zero&&GetUserObjectInformation(handle,2,text,512,out _)?text.ToString():null;}
    [ComImport,Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]int IsWindowOnCurrentVirtualDesktop(IntPtr window,[MarshalAs(UnmanagedType.Bool)]out bool current);
        [PreserveSig]int GetWindowDesktopId(IntPtr window,out Guid id);
        [PreserveSig]int MoveWindowToDesktop(IntPtr window,ref Guid id);
    }
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr window,uint attribute,out uint value,uint size);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")]private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")]private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",EntryPoint="GetUserObjectInformationW",CharSet=CharSet.Unicode)]private static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder name,uint length,out uint needed);
}
