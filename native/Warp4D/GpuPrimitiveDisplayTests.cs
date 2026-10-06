using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Warp4D.Rendering;

namespace Warp4D;

// Explicit bounded diagnostic. No cartridge, renderer, texture or hidden
// drawable. Never save failing screen pixels or alter system/driver settings.
internal static class GpuPrimitiveDisplayTests
{
    internal static int RunDxgiGdiControl(string output)
    {
        Directory.CreateDirectory(output);
        using Form form=new(){ClientSize=new(288,216),Location=new(40,40),StartPosition=FormStartPosition.Manual,BackColor=Color.Lime,Text="Warp4D DXGI GDI positive control"};
        try
        {
            form.Show();ShowWindow(form.Handle,5);OwnedGpuDisplayInspection.PrepareVisible(form);form.Refresh();
            using var captured=OwnedGpuDisplayInspection.Capture(form,form.Handle,form.ClientSize,gpuAware:true);
            using var inner=captured.Clone(new Rectangle(8,8,272,200),PixelFormat.Format32bppArgb);
            var pixels=ImagePixels.Read(inner).Pixels;int matched=pixels.Count(p=>p==Color.Lime.ToArgb());bool exact=matched==pixels.Length;
            if(exact)inner.Save(Path.Combine(output,"known-gdi-control.png"),ImageFormat.Png);
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=exact,Scope="Independent ordinary GDI control captured through DXGI owned ROI, not OpenGL acceptance.",Matching=matched,Pixels=pixels.Length},new JsonSerializerOptions{WriteIndented=true}));
            return exact?0:1;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());return 1;}
        finally{form.Close();}
    }
    internal static int Run(string output,bool gpuAware=false)
    {
        Directory.CreateDirectory(output);
        List<object> cases=[];bool gpuExact=true;IntPtr dc=IntPtr.Zero,context=IntPtr.Zero;
        using PrimitiveForm form=new(){ClientSize=new(288,216),Location=new(40,40),StartPosition=FormStartPosition.Manual,Text="Warp4D owned primitive GPU test"};
        try
        {
            form.Show();ShowWindow(form.Handle,5);OwnedGpuDisplayInspection.PrepareVisible(form);
            dc=GetDC(form.Handle);Require(dc!=IntPtr.Zero,"Owned window DC required.");
            PixelFormatDescriptor requested=new(){Size=(ushort)Marshal.SizeOf<PixelFormatDescriptor>(),Version=1,Flags=0x25,ColorBits=32,DepthBits=24};
            int format=ChoosePixelFormat(dc,ref requested);
            Require(format>0&&SetPixelFormat(dc,format,ref requested),"Fresh owned drawable format required.");
            context=wglCreateContext(dc);Require(context!=IntPtr.Zero&&wglMakeCurrent(dc,context),"Fresh context on the owned visible window required.");
            string device=Marshal.PtrToStringAnsi(glGetString(0x1f01))??"Unknown";
            glGetIntegerv(0x0c32,out int doubleBuffered);
            DescribePixelFormat(dc,format,(uint)Marshal.SizeOf<PixelFormatDescriptor>(),out var actual);
            foreach(var color in new[]{Color.Red,Color.Lime,Color.Blue})
            {
                Require(wglMakeCurrent(dc,context),"Primitive context current required.");
                glViewport(0,0,form.ClientSize.Width,form.ClientSize.Height);glDrawBuffer(0x0405);
                glClearColor(color.R/255f,color.G/255f,color.B/255f,1);glClear(0x4000);glFinish();
                Require(glGetError()==0,"Primitive clear must succeed.");
                int before=ReadColor(0x0405);
                bool swapped=SwapBuffers(dc);glFinish();Require(swapped&&glGetError()==0,"Primitive swap must succeed.");
                int front=ReadColor(0x0404),back=ReadColor(0x0405);
                wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);
                using Bitmap captured=OwnedGpuDisplayInspection.Capture(form,form.Handle,form.ClientSize,gpuAware);
                // Windows11 rounded outer corners are not a drawable-stage
                // control. Keep whole-client counts, inspect an explicit inner
                // rectangle, and never save unknown corner/desktop pixels.
                using Bitmap inspected=captured.Clone(new Rectangle(8,8,captured.Width-16,captured.Height-16),PixelFormat.Format32bppArgb);
                var pixels=ImagePixels.Read(inspected).Pixels;int expected=color.ToArgb();int matching=pixels.Count(p=>p==expected);
                bool exact=matching==pixels.Length;
                gpuExact&=exact;
                cases.Add(new{Color=color.Name,Expected=expected.ToString("X8"),GpuBeforeSwap=before.ToString("X8"),GpuFrontAfterSwap=front.ToString("X8"),GpuBackAfterSwap=back.ToString("X8"),WholeClientMatching=ImagePixels.Read(captured).Pixels.Count(p=>p==expected),WholeClientPixels=captured.Width*captured.Height,ScreenMatching=matching,ScreenPixels=pixels.Length,ScreenExact=exact,FlatScreenColor=pixels.Distinct().Take(2).Count()==1?pixels[0].ToString("X8"):null});
                if(exact)inspected.Save(Path.Combine(output,color.Name.ToLowerInvariant()+"-known-visible.png"),ImageFormat.Png);
            }
            // Independent ordinary GDI positive control, same size/location,
            // but never mixed into the pixel-formatted OpenGL drawable.
            form.Hide();using Form gdi=new(){ClientSize=new(288,216),Location=new(40,40),StartPosition=FormStartPosition.Manual,BackColor=Color.Lime,Text="Warp4D owned GDI control"};
            gdi.Show();ShowWindow(gdi.Handle,5);OwnedGpuDisplayInspection.PrepareVisible(gdi);gdi.Refresh();
            using Bitmap control=OwnedGpuDisplayInspection.Capture(gdi,gdi.Handle,gdi.ClientSize,gpuAware);
            using Bitmap innerControl=control.Clone(new Rectangle(8,8,control.Width-16,control.Height-16),PixelFormat.Format32bppArgb);
            var controlPixels=ImagePixels.Read(innerControl).Pixels;int controlMatched=controlPixels.Count(p=>p==Color.Lime.ToArgb());
            bool controlExact=controlMatched==controlPixels.Length;
            if(controlExact)innerControl.Save(Path.Combine(output,"gdi-known-visible.png"),ImageFormat.Png);
            bool passed=gpuExact&&controlExact&&!device.Contains("GDI Generic",StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=passed,Capture=gpuAware?"DXGI owned ROI":"GDI owned ROI",Device=device,PixelFormat=format,Flags=actual.Flags,DoubleBuffered=doubleBuffered,OwnDcClass=(GetClassLongPtr(form.Handle,-26).ToInt64()&32)!=0,FreshVisibleWindowContext=true,NoWarpRenderer=true,NoTextures=true,NoHiddenDrawable=true,InspectionMargin=8,GdiWholeClientMatched=ImagePixels.Read(control).Pixels.Count(p=>p==Color.Lime.ToArgb()),GdiWholeClientPixels=control.Width*control.Height,GdiControlExact=controlExact,GdiControlMatched=controlMatched,GdiControlPixels=controlPixels.Length,Cases=cases,Scope="Primitive inner272x200 display/capture isolation only; rounded outer corners reported separately. Does not replace complete Warp4D rendering/display/input/recording gates."},new JsonSerializerOptions{WriteIndented=true}));
            return passed?0:1;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());return 1;}
        finally
        {
            wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);if(context!=IntPtr.Zero)wglDeleteContext(context);
            if(dc!=IntPtr.Zero)ReleaseDC(form.Handle,dc);form.Close();
        }
    }
    private static unsafe int ReadColor(uint buffer)
    {
        int pixel=0;glReadBuffer(buffer);glReadPixels(0,0,1,1,0x80e1,0x1401,(IntPtr)(&pixel));
        Require(glGetError()==0,"Primitive readback must succeed.");return pixel|unchecked((int)0xff000000);
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private sealed class PrimitiveForm:Form
    {
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ClassStyle|=32;p.Style|=0x06000000;return p;}}
        protected override void OnPaint(PaintEventArgs e){}
        protected override void OnPaintBackground(PaintEventArgs e){}
    }
    [StructLayout(LayoutKind.Sequential)]private struct PixelFormatDescriptor
    {
        public ushort Size,Version;public uint Flags;
        public byte PixelType,ColorBits,RedBits,RedShift,GreenBits,GreenShift,BlueBits,BlueShift,AlphaBits,AlphaShift,AccumBits,AccumRedBits,AccumGreenBits,AccumBlueBits,AccumAlphaBits,DepthBits,StencilBits,AuxBuffers,LayerType,Reserved;
        public uint LayerMask,VisibleMask,DamageMask;
    }
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll",EntryPoint="GetClassLongPtrW")]private static extern IntPtr GetClassLongPtr(IntPtr window,int index);
    [DllImport("gdi32.dll")]private static extern int ChoosePixelFormat(IntPtr dc,ref PixelFormatDescriptor descriptor);
    [DllImport("gdi32.dll")]private static extern int DescribePixelFormat(IntPtr dc,int index,uint size,out PixelFormatDescriptor descriptor);
    [DllImport("gdi32.dll")]private static extern bool SetPixelFormat(IntPtr dc,int index,ref PixelFormatDescriptor descriptor);
    [DllImport("gdi32.dll")]private static extern bool SwapBuffers(IntPtr dc);
    [DllImport("opengl32.dll")]private static extern IntPtr wglCreateContext(IntPtr dc);
    [DllImport("opengl32.dll")]private static extern bool wglMakeCurrent(IntPtr dc,IntPtr context);
    [DllImport("opengl32.dll")]private static extern bool wglDeleteContext(IntPtr context);
    [DllImport("opengl32.dll")]private static extern IntPtr glGetString(uint name);
    [DllImport("opengl32.dll")]private static extern void glGetIntegerv(uint name,out int value);
    [DllImport("opengl32.dll")]private static extern void glViewport(int x,int y,int width,int height);
    [DllImport("opengl32.dll")]private static extern void glClearColor(float r,float g,float b,float a);
    [DllImport("opengl32.dll")]private static extern void glClear(uint mask);
    [DllImport("opengl32.dll")]private static extern void glFinish();
    [DllImport("opengl32.dll")]private static extern void glDrawBuffer(uint buffer);
    [DllImport("opengl32.dll")]private static extern void glReadBuffer(uint buffer);
    [DllImport("opengl32.dll")]private static extern void glReadPixels(int x,int y,int width,int height,uint format,uint type,IntPtr data);
    [DllImport("opengl32.dll")]private static extern uint glGetError();
}
