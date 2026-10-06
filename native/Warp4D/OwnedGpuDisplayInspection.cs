using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Warp4D;

// Explicit diagnostics only. Production playback never reads screen pixels.
// Callers must not save a failing capture: an unexpected overlay could contain
// unrelated desktop content. Save only after exact known-stage equality.
internal static class OwnedGpuDisplayInspection
{
    internal static void PrepareVisible(Form owned)
    {
        owned.TopMost=true;
        // Hidden diagnostic launches can leave the managed property ahead of
        // the native Z order. Raise only this test form, without taking focus.
        if(!SetWindowPos(owned.Handle,new IntPtr(-1),0,0,0,0,0x0013))
            throw new InvalidOperationException("Could not raise the owned diagnostic form without activation.");
        Application.DoEvents();
        Thread.Sleep(100);
        int flush=DwmFlush();
        if(flush!=0)throw new InvalidOperationException($"Desktop composition flush failed: {flush:X8}.");
    }

    internal static Bitmap Capture(Form owned,IntPtr stage,Size expected,bool gpuAware=false)
    {
        PrepareVisible(owned);
        bool ownedChild=stage!=IntPtr.Zero && GetAncestor(stage,2)==owned.Handle;
        bool visible=stage!=IntPtr.Zero && IsWindowVisible(stage);
        bool clientReady=GetClientRect(stage,out var rectangle);
        if(!ownedChild || !visible || !clientReady || !owned.TopMost)
            throw new InvalidOperationException($"Owned stage inspection guard: child={ownedChild}, visible={visible}, client={clientReady}, topmost={owned.TopMost}.");
        Size size=new(rectangle.Right-rectangle.Left,rectangle.Bottom-rectangle.Top);
        NativePoint point=default;
        if(size!=expected || !ClientToScreen(stage,ref point) ||
            !Screen.AllScreens.Any(screen=>screen.Bounds.Contains(new Rectangle(point.X,point.Y,size.Width,size.Height))))
            throw new InvalidOperationException("Owned stage must be wholly visible at its exact drawable size.");
        foreach(Point sample in new[]{new Point(1,1),new Point(size.Width-2,1),new Point(1,size.Height-2),new Point(size.Width-2,size.Height-2),new Point(size.Width/2,size.Height/2)})
        {
            IntPtr hit=WindowFromPoint(new NativePoint{X=point.X+sample.X,Y=point.Y+sample.Y});
            // Hit testing can resolve a transparent native child to its
            // WinForms parent. Both must belong to this exact owned form.
            if(hit==IntPtr.Zero || GetAncestor(hit,2)!=owned.Handle)
            {
                GetWindowRect(owned.Handle,out var rootBounds);
                bool nativeTopmost=(GetWindowLongPtr(owned.Handle,-20).ToInt64()&8)!=0;
                throw new InvalidOperationException($"Owned stage is occluded at {sample.X},{sample.Y} (screen {point.X+sample.X},{point.Y+sample.Y}, root {rootBounds.Left},{rootBounds.Top},{rootBounds.Right},{rootBounds.Bottom}, nativeTopmost={nativeTopmost}); refusing unrelated desktop pixels.");
            }
        }
        if(gpuAware)
        {
            string title=owned.Text;
            try{return OwnedDxgiCapture.Capture(new Rectangle(point.X,point.Y,size.Width,size.Height),()=>
            {owned.Text=title+" [owned capture]";Application.DoEvents();DwmFlush();});}
            finally{owned.Text=title;}
        }
        Bitmap image=new(size.Width,size.Height,PixelFormat.Format32bppArgb);
        try
        {
            using(var graphics=Graphics.FromImage(image))
                graphics.CopyFromScreen(point.X,point.Y,0,0,size,CopyPixelOperation.SourceCopy);
            // A physical display has RGB, not application alpha. Normalize
            // GDI alpha exactly as the existing GL inspection does.
            var bits=image.LockBits(new Rectangle(Point.Empty,size),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
            try
            {
                int[] row=new int[size.Width];
                for(int y=0;y<size.Height;y++)
                {
                    IntPtr address=IntPtr.Add(bits.Scan0,y*bits.Stride);
                    Marshal.Copy(address,row,0,row.Length);
                    for(int x=0;x<row.Length;x++)row[x]|=unchecked((int)0xff000000);
                    Marshal.Copy(row,0,address,row.Length);
                }
            }
            finally{image.UnlockBits(bits);}
            return image;
        }
        catch{image.Dispose();throw;}
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRectangle { public int Left,Top,Right,Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window,out NativeRectangle rectangle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out NativeRectangle rectangle);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window,ref NativePoint point);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
}
