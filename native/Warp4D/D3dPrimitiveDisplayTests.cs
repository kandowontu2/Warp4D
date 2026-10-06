using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Warp4D.Rendering;

namespace Warp4D;

// Independent explicit presentation probe, not a replacement production renderer.
internal static class D3dPrimitiveDisplayTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);List<object> cases=[];
        IntPtr device=IntPtr.Zero,context=IntPtr.Zero,swap=IntPtr.Zero,buffer=IntPtr.Zero,view=IntPtr.Zero,staging=IntPtr.Zero;
        using PrimitiveForm form=new(){ClientSize=new(288,216),Location=new(40,40),StartPosition=FormStartPosition.Manual,Text="Warp4D native D3D owned test"};
        try
        {
            form.Show();ShowWindow(form.Handle,5);OwnedGpuDisplayInspection.PrepareVisible(form);
            object visibility=OwnedWindowVisibilityDiagnostics.Read(form);
            using var powerProbe=new OwnedDisplayPowerProbe(form);object displayPower=powerProbe.Observe();
            SwapDescription desc=new(){Width=288,Height=216,RefreshDenominator=1,Format=87,SampleCount=1,Usage=0x20,BufferCount=2,Window=form.Handle,Windowed=1};
            Check(D3D11CreateDeviceAndSwapChain(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,ref desc,out swap,out device,out uint level,out context),"CreateHardwareDeviceAndSwapChain");
            Guid textureId=new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            Check(Method<GetBuffer>(swap,9)(swap,0,ref textureId,out buffer),"GetBackBuffer");
            Check(Method<CreateView>(device,9)(device,buffer,IntPtr.Zero,out view),"CreateRenderTargetView");
            TextureDescription stageDesc=new(){Width=288,Height=216,MipLevels=1,ArraySize=1,Format=87,SampleCount=1,Usage=3,CpuAccess=0x20000};
            Check(Method<CreateTexture>(device,5)(device,ref stageDesc,IntPtr.Zero,out staging),"CreateOwnedReadbackTexture");
            bool gpuExact=true,screenExact=true,presentAccepted=true;
            foreach(var color in new[]{Color.Red,Color.Lime,Color.Blue})
            {
                Method<ClearView>(context,50)(context,view,[color.R/255f,color.G/255f,color.B/255f,1]);
                Method<CopyResource>(context,47)(context,staging,buffer);
                Check(Method<MapTexture>(context,14)(context,staging,0,1,0,out var data),"MapOwnedBackBuffer");
                int matching=0;
                try
                {
                    int[] row=new int[288];for(int y=0;y<216;y++)
                    {Marshal.Copy(IntPtr.Add(data.Data,checked(y*(int)data.RowPitch)),row,0,row.Length);matching+=row.Count(p=>(p|unchecked((int)0xff000000))==color.ToArgb());}
                }
                finally{Method<UnmapTexture>(context,15)(context,staging,0);}
                gpuExact&=matching==288*216;
                int hr=Method<Present>(swap,8)(swap,1,0);Check(hr,"Present");
                presentAccepted&=hr==0;
                using var captured=OwnedGpuDisplayInspection.Capture(form,form.Handle,form.ClientSize);
                using var inner=captured.Clone(new Rectangle(8,8,272,200),PixelFormat.Format32bppArgb);
                var pixels=ImagePixels.Read(inner).Pixels;int screenMatching=pixels.Count(p=>p==color.ToArgb());bool exact=screenMatching==pixels.Length;screenExact&=exact;
                cases.Add(new{Color=color.Name,GpuMatched=matching,GpuPixels=288*216,PresentHResult=hr,ScreenMatched=screenMatching,ScreenPixels=pixels.Length,ScreenExact=exact,FlatScreenColor=pixels.Distinct().Take(2).Count()==1?pixels[0].ToString("X8"):null});
                if(exact)inner.Save(Path.Combine(output,color.Name.ToLowerInvariant()+"-known-visible.png"),ImageFormat.Png);
            }
            form.Hide();using Form control=new(){ClientSize=new(288,216),Location=new(40,40),StartPosition=FormStartPosition.Manual,BackColor=Color.Lime,Text="Warp4D D3D GDI positive control"};
            control.Show();ShowWindow(control.Handle,5);OwnedGpuDisplayInspection.PrepareVisible(control);control.Refresh();
            using var controlCapture=OwnedGpuDisplayInspection.Capture(control,control.Handle,control.ClientSize);
            using var controlInner=controlCapture.Clone(new Rectangle(8,8,272,200),PixelFormat.Format32bppArgb);
            int controlMatched=ImagePixels.Read(controlInner).Pixels.Count(p=>p==Color.Lime.ToArgb());bool controlExact=controlMatched==54400;
            if(controlExact)controlInner.Save(Path.Combine(output,"gdi-known-visible.png"),ImageFormat.Png);
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=gpuExact&&screenExact&&controlExact&&presentAccepted,PresentAccepted=presentAccepted,Visibility=visibility,DisplayPower=displayPower,HardwareDriverRequested=true,FeatureLevel=level,SwapDescriptionBytes=Marshal.SizeOf<SwapDescription>(),NativeWindowsD3D11=true,NoWarpRenderer=true,NoTextures=true,NoRom=true,GdiControlExact=controlExact,GdiControlMatched=controlMatched,GdiControlPixels=54400,Cases=cases,Scope="Independent D3D11 primitive render/presentation/capture isolation, inner272x200; not Warp4D full-stage/recording/input/performance acceptance."},new JsonSerializerOptions{WriteIndented=true}));
            return gpuExact&&screenExact&&controlExact&&presentAccepted?0:1;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());return 1;}
        finally{Release(ref staging);Release(ref view);Release(ref buffer);Release(ref swap);Release(ref context);Release(ref device);form.Close();}
    }
    private static T Method<T>(IntPtr instance,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size));
    private static void Release(ref IntPtr instance){if(instance!=IntPtr.Zero){Marshal.Release(instance);instance=IntPtr.Zero;}}
    private static void Check(int hr,string operation){if(hr<0)throw new InvalidOperationException($"{operation}: HRESULT0x{hr:X8}.");}
    private sealed class PrimitiveForm:Form
    {
        protected override void OnPaint(PaintEventArgs e){}
        protected override void OnPaintBackground(PaintEventArgs e){}
    }
    [StructLayout(LayoutKind.Sequential)]private struct SwapDescription{public uint Width,Height,RefreshNumerator,RefreshDenominator,Format,ScanlineOrdering,Scaling,SampleCount,SampleQuality,Usage,BufferCount;public IntPtr Window;public int Windowed;public uint SwapEffect,Flags;}
    [StructLayout(LayoutKind.Sequential)]private struct TextureDescription{public uint Width,Height,MipLevels,ArraySize,Format,SampleCount,SampleQuality,Usage,BindFlags,CpuAccess,MiscFlags;}
    [StructLayout(LayoutKind.Sequential)]private struct MappedData{public IntPtr Data;public uint RowPitch,DepthPitch;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetBuffer(IntPtr self,uint index,ref Guid id,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Present(IntPtr self,uint interval,uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateView(IntPtr self,IntPtr resource,IntPtr desc,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateTexture(IntPtr self,ref TextureDescription desc,IntPtr data,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void ClearView(IntPtr self,IntPtr view,[MarshalAs(UnmanagedType.LPArray,SizeConst=4)]float[] color);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void CopyResource(IntPtr self,IntPtr destination,IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int MapTexture(IntPtr self,IntPtr resource,uint subresource,uint type,uint flags,out MappedData result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void UnmapTexture(IntPtr self,IntPtr resource,uint subresource);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("d3d11.dll")]private static extern int D3D11CreateDeviceAndSwapChain(IntPtr adapter,uint driverType,IntPtr software,uint flags,IntPtr levels,uint levelCount,uint sdkVersion,ref SwapDescription description,out IntPtr swap,out IntPtr device,out uint level,out IntPtr context);
}
