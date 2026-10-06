using System.Runtime.InteropServices;

namespace Warp4D.Emulation;

// Read only this process's Windows audio sessions. No microphone, loopback capture,
// device-wide activity, or recording is used for the reactive effects.
internal sealed class SessionAudioMeter : IDisposable
{
    private readonly List<IntPtr> _meters=[];
    private long _nextBind;
    public string Status {get;private set;}="Waiting for Warp4D audio";
    public float Level {get;private set;}
    public float Sample(bool enabled)
    {
        if(!enabled){Level=0;return 0;}
        if(Environment.TickCount64>=_nextBind){Bind();_nextBind=Environment.TickCount64+3000;}
        float peak=0;
        foreach(var meter in _meters)
            if(Method<Peak>(meter,3)(meter,out float value)>=0&&float.IsFinite(value))peak=Math.Max(peak,value);
        float target=Math.Clamp(peak*3,0,1);
        Level=Level+(target-Level)*(target>Level?.55f:.12f);
        return Level;
    }
    private void Bind()
    {
        ReleaseMeters();IntPtr enumerator=IntPtr.Zero,device=IntPtr.Zero,manager=IntPtr.Zero,sessions=IntPtr.Zero;
        try
        {
            Guid clsid=new("BCDE0395-E52F-467C-8E3D-C4579291692E"),iid=new("A95664D2-9614-4F35-A746-DE8DB63617E6");
            Check(CoCreateInstance(ref clsid,IntPtr.Zero,1,ref iid,out enumerator));
            Check(Method<DefaultDevice>(enumerator,4)(enumerator,0,1,out device));
            Guid sessionManager=new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
            Check(Method<Activate>(device,3)(device,ref sessionManager,23,IntPtr.Zero,out manager));
            Check(Method<GetPointer>(manager,5)(manager,out sessions));
            Check(Method<GetCount>(sessions,3)(sessions,out int count));
            for(int i=0;i<count;i++)
            {
                IntPtr session=IntPtr.Zero,control=IntPtr.Zero,meter=IntPtr.Zero;
                try
                {
                    Check(Method<GetSession>(sessions,4)(sessions,i,out session));
                    Guid controlId=new("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
                    if(Marshal.QueryInterface(session,ref controlId,out control)<0)continue;
                    if(Method<ProcessId>(control,14)(control,out uint pid)<0||pid!=Environment.ProcessId)continue;
                    Guid meterId=new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
                    if(Marshal.QueryInterface(session,ref meterId,out meter)>=0){_meters.Add(meter);meter=IntPtr.Zero;}
                }
                finally{Release(meter);Release(control);Release(session);}
            }
            Status=_meters.Count==0?"Waiting for Warp4D audio session":"Warp4D audio · live peak envelope";
        }
        catch(COMException e){Status=$"Audio meter unavailable (0x{e.HResult:X8}) · motion stays neutral";}
        finally{Release(sessions);Release(manager);Release(device);Release(enumerator);}
    }
    private static void Check(int hr){Marshal.ThrowExceptionForHR(hr);}
    private static T Method<T>(IntPtr pointer,int slot) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer),slot*IntPtr.Size));
    private static void Release(IntPtr p){if(p!=IntPtr.Zero)Marshal.Release(p);}
    private void ReleaseMeters(){foreach(var p in _meters)Release(p);_meters.Clear();}
    public void Dispose()=>ReleaseMeters();
    [DllImport("ole32.dll")]private static extern int CoCreateInstance(ref Guid clsid,IntPtr outer,uint context,ref Guid iid,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int DefaultDevice(IntPtr self,int flow,int role,out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Activate(IntPtr self,ref Guid iid,uint context,IntPtr parameters,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetPointer(IntPtr self,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetCount(IntPtr self,out int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetSession(IntPtr self,int index,out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int ProcessId(IntPtr self,out uint id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Peak(IntPtr self,out float value);
}
