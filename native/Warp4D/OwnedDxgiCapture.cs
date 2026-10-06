using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Warp4D;

// Diagnostic only. The caller validates exact ownership/visibility first.
// Desktop duplication remains GPU-local; only the owned ROI is copied to a
// staging texture and mapped. Never map/save the whole desktop texture.
internal static class OwnedDxgiCapture
{
    internal static Bitmap Capture(Rectangle ownedRectangle,Action requestOwnedComposition)
    {
        IntPtr factory=IntPtr.Zero,adapter=IntPtr.Zero,output=IntPtr.Zero,output1=IntPtr.Zero,device=IntPtr.Zero,context=IntPtr.Zero,duplication=IntPtr.Zero,resource=IntPtr.Zero,source=IntPtr.Zero,staging=IntPtr.Zero;
        bool acquired=false,mapped=false;
        try
        {
            Guid factoryId=new("770aae78-f26f-4dba-a829-253c83d1b387");Check(CreateDXGIFactory1(ref factoryId,out factory),"CreateDXGIFactory1");
            OutputDescription selected=default;bool found=false;
            for(uint a=0;a<16&&!found;a++)
            {
                Release(ref adapter);int hr=Method<Enumerate>(factory,7)(factory,a,out adapter);if(hr==unchecked((int)0x887a0002))break;Check(hr,"EnumAdapters");
                for(uint o=0;o<16;o++)
                {
                    Release(ref output);hr=Method<Enumerate>(adapter,7)(adapter,o,out output);if(hr==unchecked((int)0x887a0002))break;Check(hr,"EnumOutputs");
                    Check(Method<GetOutputDescription>(output,7)(output,out var desc),"GetOutputDesc");
                    if(desc.Attached!=0&&Rectangle.FromLTRB(desc.Desktop.Left,desc.Desktop.Top,desc.Desktop.Right,desc.Desktop.Bottom).Contains(ownedRectangle))
                    {selected=desc;found=true;break;}
                }
            }
            if(!found)throw new InvalidOperationException("No desktop output wholly contains the owned diagnostic ROI.");
            if(selected.Rotation is not (0 or 1))throw new InvalidOperationException("Rotated desktop not supported by this bounded diagnostic.");
            Check(D3D11CreateDevice(adapter,0,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out device,out _,out context),"D3D11CreateDevice");
            Guid outputId=new("00cddea8-939b-4b83-a340-a685226666cc");Check(Marshal.QueryInterface(output,ref outputId,out output1),"QueryOutput1");
            Check(Method<Duplicate>(output1,22)(output1,device,out duplication),"DuplicateOutput");
            // AcquireNextFrame waits for a composition update after duplication
            // starts. Request only an owned nonclient title change, never input
            // or a mutation of the rendered client pixels.
            requestOwnedComposition();
            Check(Method<Acquire>(duplication,8)(duplication,1000,out _,out resource),"AcquireNextFrame");acquired=true;
            Guid textureId=new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");Check(Marshal.QueryInterface(resource,ref textureId,out source),"QueryTexture2D");
            Method<GetTextureDescription>(source,10)(source,out var sourceDescription);
            if(sourceDescription.Format!=87)throw new InvalidOperationException("Expected BGRA8 desktop duplication texture.");
            uint left=(uint)(ownedRectangle.Left-selected.Desktop.Left),top=(uint)(ownedRectangle.Top-selected.Desktop.Top);
            if(left+ownedRectangle.Width>sourceDescription.Width||top+ownedRectangle.Height>sourceDescription.Height)throw new InvalidOperationException("Owned ROI exceeds native desktop texture.");
            TextureDescription description=new(){Width=(uint)ownedRectangle.Width,Height=(uint)ownedRectangle.Height,MipLevels=1,ArraySize=1,Format=87,SampleCount=1,Usage=3,CpuAccess=0x20000};
            Check(Method<CreateTexture>(device,5)(device,ref description,IntPtr.Zero,out staging),"CreateOwnedStagingTexture");
            Box box=new(){Left=left,Top=top,Right=left+description.Width,Bottom=top+description.Height,Back=1};
            Method<CopyRegion>(context,46)(context,staging,0,0,0,0,source,0,ref box);
            Check(Method<MapTexture>(context,14)(context,staging,0,1,0,out var data),"MapOwnedStagingTexture");mapped=true;
            Bitmap image=new(ownedRectangle.Width,ownedRectangle.Height,PixelFormat.Format32bppArgb);
            try
            {
                var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                try
                {
                    int[] row=new int[ownedRectangle.Width];
                    for(int y=0;y<ownedRectangle.Height;y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Data,checked(y*(int)data.RowPitch)),row,0,row.Length);
                        for(int x=0;x<row.Length;x++)row[x]|=unchecked((int)0xff000000);
                        Marshal.Copy(row,0,IntPtr.Add(bits.Scan0,y*bits.Stride),row.Length);
                    }
                }
                finally{image.UnlockBits(bits);}
                return image;
            }
            catch{image.Dispose();throw;}
        }
        finally
        {
            if(mapped)Method<UnmapTexture>(context,15)(context,staging,0);
            if(acquired)Method<ReleaseFrame>(duplication,14)(duplication);
            Release(ref staging);Release(ref source);Release(ref resource);Release(ref duplication);Release(ref context);Release(ref device);Release(ref output1);Release(ref output);Release(ref adapter);Release(ref factory);
        }
    }
    private static T Method<T>(IntPtr instance,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size));
    private static void Release(ref IntPtr instance){if(instance!=IntPtr.Zero){Marshal.Release(instance);instance=IntPtr.Zero;}}
    private static void Check(int hr,string operation){if(hr<0)throw new InvalidOperationException($"{operation}: HRESULT 0x{hr:X8}.");}
    [StructLayout(LayoutKind.Sequential)]private struct NativeRectangle{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct OutputDescription{[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string DeviceName;public NativeRectangle Desktop;public int Attached;public uint Rotation;public IntPtr Monitor;}
    [StructLayout(LayoutKind.Sequential)]private struct FrameInfo{public long LastPresent,LastMouse;public uint Frames;public int Coalesced,Protected;public int PointerX,PointerY,PointerVisible;public uint MetadataBytes,PointerShapeBytes;}
    [StructLayout(LayoutKind.Sequential)]private struct TextureDescription{public uint Width,Height,MipLevels,ArraySize,Format,SampleCount,SampleQuality,Usage,BindFlags,CpuAccess,MiscFlags;}
    [StructLayout(LayoutKind.Sequential)]private struct Box{public uint Left,Top,Front,Right,Bottom,Back;}
    [StructLayout(LayoutKind.Sequential)]private struct MappedData{public IntPtr Data;public uint RowPitch,DepthPitch;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Enumerate(IntPtr self,uint index,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetOutputDescription(IntPtr self,out OutputDescription result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Duplicate(IntPtr self,IntPtr device,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Acquire(IntPtr self,uint timeout,out FrameInfo info,out IntPtr resource);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int ReleaseFrame(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void GetTextureDescription(IntPtr self,out TextureDescription desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateTexture(IntPtr self,ref TextureDescription desc,IntPtr initialData,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void CopyRegion(IntPtr self,IntPtr destination,uint subresource,uint x,uint y,uint z,IntPtr source,uint sourceSubresource,ref Box box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int MapTexture(IntPtr self,IntPtr resource,uint subresource,uint type,uint flags,out MappedData result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void UnmapTexture(IntPtr self,IntPtr resource,uint subresource);
    [DllImport("dxgi.dll")]private static extern int CreateDXGIFactory1(ref Guid id,out IntPtr factory);
    [DllImport("d3d11.dll")]private static extern int D3D11CreateDevice(IntPtr adapter,uint driverType,IntPtr software,uint flags,IntPtr levels,uint levelCount,uint sdkVersion,out IntPtr device,out uint featureLevel,out IntPtr context);
}
