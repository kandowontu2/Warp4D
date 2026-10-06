using System.Text.Json;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class SurfaceReuseTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        bool motion=InterfaceMotion.Enabled;
        try
        {
            InterfaceMotion.Enabled=true;
            ValidateStorage();
            ValidateOpacity();
            ValidateOrdering();
            var orderStorage=ValidateOrderStorage();
            int accentCases=ValidateAccentStorage();
            int noRomFrames=ValidateNoRomAccents();
            var backgroundPixels=BackgroundPixelCacheTests.Validate();
            ValidateVectorProjection();
            ValidateLighting();
            int descriptorCases=ValidateSheetDescriptors();
            using var scene=LookGalleryForm.CreateSample();
            using WarpRendererControl reference=new(){Size=new(720,600),UseGpu=true,PresentationMode=true,UseBackgroundPixelCacheForTest=false,ReuseMeshStorageForTest=false,UseBatchedProjectionForTest=false,UsePackedLightingForTest=false,UseCachedSheetDescriptorsForTest=false,UseDirectVertexWritesForTest=false,UseCachedTextureInventoryForTest=false,UseReusableOrderStorageForTest=false,UseReusableAccentStorageForTest=false};
            using WarpRendererControl reused=new(){Size=new(720,600),UseGpu=true,PresentationMode=true,UseGeometryMapCacheForTest=true,UseBackgroundPixelCacheForTest=true};
            reference.CreateControl();reused.CreateControl();
            reference.SetScene(scene.Clone());reused.SetScene(scene.Clone());
            using Bitmap a=new(720,600),b=new(720,600);
            List<object> runs=[];
            foreach(var mode in Enum.GetValues<GeometryMode>())
            foreach(bool dynamic in new[]{false,true})
            {
                PresentationSettings settings=new(){Animate=true,Opacity=.63f,Geometry=new(){Mode=mode,Animate=true},CrossSections=5};
                settings.Dimensions.Choreography=dynamic;
                settings.Dimensions.AudioReactive=dynamic;
                settings.Dimensions.AdaptiveQuality=false;
                settings.Effects.Enabled=true;
                reference.ApplySettings(settings.Clone());reused.ApplySettings(settings.Clone());
                long referenceAllocated=0,reusedAllocated=0;
                for(int frame=0;frame<12;frame++)
                {
                    double seconds=frame*.071;
                    // Shrinking/growing geometry, zero/opaque alpha and positional
                    // changes exercise the same storage without clearing the caches.
                    foreach(var renderer in new[]{reference,reused})
                    {
                        renderer.MotionSecondsForTest=seconds;
                        renderer.AudioLevel=frame/12f;
                        PresentationAnimator.Apply(renderer,renderer.Settings,seconds);
                        renderer.ProjectionCycleSeconds=seconds;
                        renderer.GeometryCycleSeconds=seconds;
                        renderer.ProjectionOpacity=frame==4?0:frame==8?1:.63f;
                        renderer.SliceCount=frame<6?5:3;
                        renderer.Size=frame<9?new(720,600):new(704,584);
                    }
                    long start=GC.GetAllocatedBytesForCurrentThread();reference.DrawToBitmap(a,reference.ClientRectangle);
                    if(frame>0)referenceAllocated+=GC.GetAllocatedBytesForCurrentThread()-start;
                    start=GC.GetAllocatedBytesForCurrentThread();reused.DrawToBitmap(b,reused.ClientRectangle);
                    if(frame>0)reusedAllocated+=GC.GetAllocatedBytesForCurrentThread()-start;
                    var pa=ImagePixels.Read(a).Pixels;var pb=ImagePixels.Read(b).Pixels;
                    Require(pa.SequenceEqual(pb),$"Exact pixels: {mode}, dynamic={dynamic}, frame={frame}");
                    Require(reference.SurfaceTriangleCount==reused.SurfaceTriangleCount,"Same triangle count");
                    Require(reference.GpuDrawCalls==reused.GpuDrawCalls,"Same draw calls");
                    Require(reference.RendererStatus.StartsWith("GPU")&&reused.RendererStatus.StartsWith("GPU"),"GPU required");
                }
                string name=$"{mode}-{dynamic}";
                b.Save(Path.Combine(output,name+".png"));
                runs.Add(new{Mode=mode.ToString(),Dynamic=dynamic,ExactFrames=12,ReferenceAllocatedBytes=referenceAllocated,ReusedAllocatedBytes=reusedAllocated});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,BackgroundPixels=backgroundPixels,AccentStorageCases=accentCases,NoRomAccentFrames=noRomFrames,OrderStorage=orderStorage,ExactSheetDescriptorCases=descriptorCases,StorageLifecycle=true,ClonesIndependent=true,StableOrderingMatchesLinq=true,PackedLightingExact=true,LightingTriangles=24576,VectorProjectionBitExact=true,VectorLanes=System.Numerics.Vector<float>.Count,VectorHardwareAccelerated=System.Numerics.Vector.IsHardwareAccelerated,ExactFrames=168,Runs=runs},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{InterfaceMotion.Enabled=motion;}
    }
    private static int ValidateSheetDescriptors()
    {
        using var scene=LookGalleryForm.CreateSample();var original=scene.Objects[0];int checks=0;
        using WarpRendererControl legacy=new(){UseGpu=false,UseCachedSheetDescriptorsForTest=false};
        using WarpRendererControl candidate=new(){UseGpu=false};
        foreach(int slices in Enumerable.Range(2,8))foreach(float depth in new[]{0f,1f})foreach(bool edited in new[]{false,true})
        {
            using SceneObject item=new(){Kind=original.Kind,Label=original.Label,IdentityKey="descriptor-test",Bounds=original.Bounds,Image=(Bitmap)original.Image.Clone(),Accent=original.Accent,Depth=depth,ProjectionEnabled=true};
            PresentationSettings settings=new(){CrossSections=slices,Depth=.6f,Opacity=.7f,RotationSpread=35,Animate=true};
            if(edited)
            {
                settings.EditLayer(item.PresentationKey,"W:+0.5").Enabled=false;
                var w=settings.EditLayer(item.PresentationKey,"W:-1");w.DepthPercent=135;w.Rotation.XW=67;w.UseGlobalOpacity=false;w.OpacityPercent=37;
                var center=settings.EditLayer(item.PresentationKey,"Center");center.DepthPercent=175;center.Rotation.ZW=-35;
            }
            foreach(var renderer in new[]{legacy,candidate}){renderer.ApplySettings(settings.Clone());renderer.ProjectionCycleSeconds=.375;renderer.GeometryCycleSeconds=.375;}
            var a=legacy.LayerTransformsForTest(item);var b=candidate.LayerTransformsForTest(item);
            Require(a.Keys.SequenceEqual(b.Keys),"Exact cached layer keys and order for every slice count");
            foreach(var key in a.Keys)Require(a[key]==b[key],"Exact cached layer transform with independent edits");
            var old=legacy.GeometrySurfacesForTest(item).Triangles;var current=candidate.GeometrySurfacesForTest(item).Triangles;
            Require(old.Count==current.Count,"Descriptor mesh count");
            for(int i=0;i<old.Count;i++)Require(old[i]==current[i],"Descriptor triangle identity");
            checks++;
        }
        return checks;
    }
    private static void ValidateStorage()
    {
        SurfaceGroup group=new();
        SurfaceVertex a=new(new(1,2),.1f,1),b=new(new(3,4),.2f,1),c=new(new(5,6),.3f,1);
        group.BeginUpdate();group.Triangle(a,b,c,null,Color.Red,1,"Old");group.Triangle(c,b,a,null,Color.Blue,.5f);group.EndUpdate();
        var first=group.Triangles[0];var copy=first with{Color=Color.Green};var oldOrder=group.Ordered();
        group.Offset=new(20,30);
        group.BeginUpdate();group.Triangle(c,a,b,new("test",1,1,[unchecked((int)0xffffffff)]),Color.Yellow,.4f,"New");group.EndUpdate();
        Require(group.Triangles.Count==1&&ReferenceEquals(first,group.Triangles[0]),"Reuse and truncate");
        Require(first.A==c&&first.B==a&&first.C==b&&first.Texture?.Key=="test"&&first.Color==Color.Yellow&&first.Opacity==.4f&&first.LayerKey=="New","Every field overwritten");
        Require(copy.A==a&&copy.Color==Color.Green&&copy.LayerKey=="Old","Independent record clone");
        Require(group.Offset==default&&!ReferenceEquals(oldOrder,group.Ordered())&&group.Ordered().Length==1,"Offset and order reset");
        group.BeginUpdate();group.EndUpdate();Require(group.Ordered().Length==0,"Empty rebuild removes old triangles");
        group.BeginUpdate();group.Quad(a,b,c,a,null,Color.White,1);group.EndUpdate();Require(group.Triangles.Count==2,"Grow after empty");
        Require(!group.TextureInventory.Any(),"Empty rebuild releases old textures");
        ImagePixels texture=new("inventory-a",1,1,[1]),sameKey=new("inventory-a",1,1,[2]),other=new("inventory-b",1,1,[3]);
        group.BeginUpdate();
        group.Triangle(a,b,c,texture,Color.White,1);
        group.Triangle(a,b,c,null,Color.White,1);
        group.Triangle(a,b,c,sameKey,Color.White,1);
        group.Triangle(a,b,c,other,Color.White,1);group.EndUpdate();
        Require(group.TextureInventory.SequenceEqual(new[]{texture,other}),"First key wins and texture order retained");
        group.BeginUpdate();group.Triangle(a,b,c,other,Color.White,1);group.EndUpdate();
        Require(group.TextureInventory.SequenceEqual(new[]{other}),"Shrinking rebuild removes obsolete artwork");
        group.Triangle(a,b,c,texture,Color.White,1);
        Require(group.TextureInventory.SequenceEqual(new[]{other,texture}),"Append updates inventory");
        var inventoryClone=group.Triangles[0] with {Texture=texture};
        Require(ReferenceEquals(inventoryClone.Texture,texture)&&ReferenceEquals(group.Triangles[0].Texture,other),"Texture clone remains independent");
    }
    private static int ValidateAccentStorage()
    {
        SurfaceGroup reused=new();
        int frame=0;
        foreach(int count in new[]{300,12,0,257,2,1})
        {
            SurfaceGroup source=new(){Offset=new(frame*3,frame*-2)};
            for(int i=0;i<count;i++)
            {
                SurfaceVertex v=new(new(i,frame),i*.01f,1,.5f,.25f);
                source.Triangle(v,v,v,null,Color.White,i%2==0?1:.3f,i%7==0?"W:+1":"Center");
            }
            ImagePixels mask=new($"accent:{frame}",1,1,[unchecked((int)0xffffffff)]);
            Color color=frame%2==0?Color.Black:Color.Gold;
            var original=SurfaceEffects.Accent(source,mask,color,.2f+frame*.1f,new(7,13),1.1f,new(5,-3));
            var current=SurfaceEffects.Accent(source,mask,color,.2f+frame*.1f,new(7,13),1.1f,new(5,-3),reused);
            Require(ReferenceEquals(current,reused)&&original.Offset==current.Offset,"Accent storage identity and exact offset");
            Require(original.Triangles.SequenceEqual(current.Triangles),"Accent cross-artwork shrink/grow/empty exact fields");
            Require(original.Ordered().SequenceEqual(current.Ordered()),"Accent exact stable order");
            Require(current.OrderTailClearedForTest&&current.TextureInventory.SequenceEqual(original.TextureInventory),"Accent releases removed artwork references");
            Require(current.Triangles.Count<=256,"Accent unchanged triangle cap");
            frame++;
        }
        return frame;
    }
    private static int ValidateNoRomAccents()
    {
        bool motion=InterfaceMotion.Enabled;InterfaceMotion.Enabled=false;
        try
        {
            using WarpRendererControl original=new(){Size=new(720,600),UseGpu=true,UseReusableAccentStorageForTest=false};
            using WarpRendererControl reused=new(){Size=new(720,600),UseGpu=true};
            original.CreateControl();reused.CreateControl();
            using Bitmap a=new(720,600),b=new(720,600);
            for(int i=0;i<6;i++)
            {
                if(i==3){original.ResetRuntime();reused.ResetRuntime();}
                original.DrawToBitmap(a,original.ClientRectangle);reused.DrawToBitmap(b,reused.ClientRectangle);
                Require(ImagePixels.Read(a).Pixels.SequenceEqual(ImagePixels.Read(b).Pixels),"Repeated no-ROM showcase and runtime reset exact accent storage");
            }
            return 6;
        }
        finally{InterfaceMotion.Enabled=motion;}
    }
    private static object ValidateOrderStorage()
    {
        SurfaceGroup reference=new(){UseReusableOrderStorageForTest=false},candidate=new();
        SurfaceVertex v=new(default,.2f,1);
        // Warm maximum topology and all radix buffers before alternating counts.
        void Fill(SurfaceGroup group,int count){group.BeginUpdate();for(int i=0;i<count;i++)group.Triangle(v,v,v,null,Color.White,.5f);group.EndUpdate();group.Ordered();}
        Fill(reference,4096);Fill(candidate,4096);
        long Measure(SurfaceGroup group){long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<256;i++)Fill(group,4095+i%2);return GC.GetAllocatedBytesForCurrentThread()-start;}
        long original=Measure(reference),reused=Measure(candidate);
        Require(reused<original/10,"Counted ordering storage removes repeated array allocation");
        int capacity=candidate.OrderCapacityForTest;
        Fill(candidate,1);Require(candidate.Ordered().Length==1&&candidate.Ordered().Count()==1,"Spare capacity is never enumerated");
        Require(candidate.OrderCapacityForTest==capacity&&candidate.OrderTailClearedForTest,"Shrinking storage releases stale triangles and artwork");
        Fill(candidate,0);Require(candidate.Ordered().Length==0&&candidate.OrderTailClearedForTest,"Empty counted view releases old references");
        Fill(candidate,8193);Require(candidate.Ordered().Length==8193&&candidate.OrderCapacityForTest>=8193,"Growth retains exact count");
        return new {Iterations=256,OriginalAllocatedBytes=original,ReusedAllocatedBytes=reused,ExactCount=true,StaleReferencesCleared=true};
    }
    private static void ValidateOpacity()
    {
        var colors=Enum.GetValues<KnownColor>().Select(Color.FromKnownColor).Append(Color.Empty)
            .Concat(Enumerable.Range(0,256).Select(a=>Color.FromArgb(a,23,45,67))).ToArray();
        float[] opacities=[float.NaN,float.NegativeInfinity,-1,0,.5f,.9989f,.999f,1,2,float.PositiveInfinity];
        SurfaceVertex v=new(default,0,1);
        SurfaceTriangle reused=new(v,v,v,null,Color.Empty,0);
        foreach(var color in colors)
        foreach(float opacity in opacities)
        {
            bool expected=opacity>=.999f&&color.A==255;
            SurfaceTriangle fresh=new(v,v,v,null,color,opacity);
            Require(fresh.Opaque==expected,"Constructor opacity classification");
            reused.Color=color;reused.Opacity=opacity;
            Require(reused.Opaque==expected,"Reused opacity classification");
            var clone=reused with { Color=Color.Transparent,Opacity=.5f };
            Require(!clone.Opaque&&reused.Opaque==expected,"Independent clone opacity classification");
            clone.Opacity=opacity;clone.Color=color;
            Require(clone.Opaque==expected&&clone==fresh,"Reverse setter order and record equality");
        }
    }
    private static void ValidateLighting()
    {
        Random random=new(6243);
        var colors=Enum.GetValues<KnownColor>().Select(Color.FromKnownColor).Append(Color.Empty).ToArray();
        foreach(float strength in new[]{0f,.001f,.25f,.5f,.999f,1f})
        {
            SurfaceGroup reference=new(),candidate=new();
            for(int i=0;i<4096;i++)
            {
                SurfaceVertex Vertex()=>new(new((float)random.NextDouble()*500,(float)random.NextDouble()*500),(float)random.NextDouble(),1);
                var a=Vertex();var b=i%13==0?a:Vertex();var c=i%17==0?a:Vertex();
                Color color=i<colors.Length?colors[i]:Color.FromArgb((int)random.NextInt64(int.MinValue,(long)int.MaxValue+1));
                reference.Triangle(a,b,c,null,color,.5f);candidate.Triangle(a,b,c,null,color,.5f);
            }
            SurfaceEffects.Light(reference,strength,false);SurfaceEffects.Light(candidate,strength);
            for(int i=0;i<reference.Triangles.Count;i++)Require(reference.Triangles[i]==candidate.Triangles[i],$"Exact packed lighting strength={strength}, triangle={i}");
        }
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void ValidateOrdering()
    {
        Random random=new(4271);
        SurfaceGroup group=new();
        foreach(int count in new[]{0,1,2,3,7,16,31,63,64,65,127,128,129,255,256,257,1023,4095,16384,7,0,256,256})
        foreach(int distribution in new[]{0,1,2,3})
        {
            group.BeginUpdate();
            for(int i=0;i<count;i++)
            {
                float depth=i%11==0?float.NaN:i%13==0?float.PositiveInfinity:i%17==0?float.NegativeInfinity:i%19==0?BitConverter.UInt32BitsToSingle(0x80000000):i%23==0?BitConverter.UInt32BitsToSingle((uint)random.NextInt64(0,1L<<32)):random.Next(-5,6)*.1f;
                if(distribution==1)depth=.25f;
                if(distribution==2)depth=(count-i)*.01f;
                if(distribution==3)depth=BitConverter.UInt32BitsToSingle((uint)random.NextInt64(0,1L<<32));
                SurfaceVertex v=new(new(i,0),depth,1);
                group.Triangle(v,v,v,null,i%3==0?Color.White:Color.FromArgb(128,255,255,255),i%5==0?.5f:1,$"{i}");
            }
            group.EndUpdate();
            // Independent original formula: a stale cached classification must fail.
            bool Opaque(SurfaceTriangle t)=>t.Opacity>=.999f&&t.Color.A==255;
            var expected=group.Triangles.Where(Opaque).Concat(group.Triangles.Where(t=>!Opaque(t)).OrderBy(t=>t.Depth)).ToArray();
            var actual=group.Ordered();
            Require(actual.Length==expected.Length,"Ordering count");
            for(int i=0;i<actual.Length;i++)Require(ReferenceEquals(actual[i],expected[i]),$"Stable exact order count={count}, index={i}");
            Require(ReferenceEquals(actual,group.Ordered()),"Unchanged ordering cached");
        }
    }
    private static void ValidateVectorProjection()
    {
        Random random=new(5261);
        foreach(int count in new[]{0,1,3,7,8,9,15,16,17,31,64,127,257})
        for(int pose=0;pose<30;pose++)
        {
            float Next(float range)=>(float)((random.NextDouble()*2-1)*range);
            PreparedRotation4D rotation=new(new(Next(6),Next(6),Next(6),Next(6),Next(6),Next(6)));
            float z=Next(40),w=Next(40),c4=pose%5==0?.4f:40+random.Next(200),c3=pose%7==0?.3f:40+random.Next(200),scale=.1f+(float)random.NextDouble()*5;
            PointF center=new(Next(1000),Next(1000));
            PointF[] points=Enumerable.Range(0,count).Select(_=>new PointF(Next(80),Next(80))).ToArray();
            SurfaceVertex[] vertices=new SurfaceVertex[count];
            rotation.ProjectVertices(points,z,w,c4,c3,center,scale,vertices);
            for(int i=0;i<count;i++)
            {
                var p=FourDMath.Project(new(points[i].X,points[i].Y,z,w),rotation,c4,c3);
                SurfaceVertex expected=new(new(center.X+p.Point.X*scale,center.Y+p.Point.Y*scale),p.CameraDepth/c3,p.Scale4D);
                Require(Bits(vertices[i].Point.X)==Bits(expected.Point.X)&&Bits(vertices[i].Point.Y)==Bits(expected.Point.Y)&&Bits(vertices[i].Depth)==Bits(expected.Depth)&&Bits(vertices[i].Q)==Bits(expected.Q),$"SIMD projection bit identity: count={count}, pose={pose}, point={i}");
            }
        }
        using var sample=LookGalleryForm.CreateSample();
        var item=sample.Objects[0];var geometry=item.PixelGeometry;
        foreach(float half in new[]{8f,9.5f,16f,2.5f,8f})
        {
            var local=geometry.LocalPoints(item.Image.Width,item.Image.Height,half,half*2);
            for(int i=0;i<local.Length;i++)
            {
                var point=geometry.Topology.Points[i];
                Require(Bits(local[i].X)==Bits(-half+(point.X/item.Image.Width)*half*2f)&&Bits(local[i].Y)==Bits(-half*2+(point.Y/item.Image.Height)*(half*2)*2f),"Cached local coordinates exact after size changes");
            }
            Require(ReferenceEquals(local,geometry.LocalPoints(item.Image.Width,item.Image.Height,half,half*2)),"Local topology reused");
        }
        static int Bits(float value)=>BitConverter.SingleToInt32Bits(value);
    }
}
