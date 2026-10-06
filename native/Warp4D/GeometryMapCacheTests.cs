using System.Text.Json;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class GeometryMapCacheTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            GeometryMapCache cache=new();long vertices=0;int grids=0;
            foreach(var mode in Enum.GetValues<GeometryMode>())
            foreach(int columns in new[]{1,8,16})foreach(int rows in new[]{1,4,8})
            foreach(float phase in new[]{0f,.125f,.5f,1f,-0f})
            foreach(float z in new[]{0f,-0f,-4f,4f})
            {
                var points=cache.Get(mode,columns,rows,z,3,8,16,4,3,.73f,phase);
                Require(ReferenceEquals(points,cache.Get(mode,columns,rows,z,3,8,16,4,3,.73f,phase)),"Exact key reuses grid");
                for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
                {
                    var expected=Geometry4D.Map(mode,x/(float)columns,y/(float)rows,z,3,8,16,4,3,.73f,phase);
                    Require(Exact(expected,points[y*(columns+1)+x]),"Bit-exact geometry point");vertices++;
                }
                Require(cache.Entries<=GeometryMapCache.MaximumEntries&&cache.Vertices<=GeometryMapCache.MaximumVertices,"Bounded cache");grids++;
            }
            using var scene=LookGalleryForm.CreateSample();var item=scene.Objects[0];
            using WarpRendererControl old=new(){UseGpu=false,UseGeometryMapCacheForTest=false};
            using WarpRendererControl current=new(){UseGpu=false,UseGeometryMapCacheForTest=true};
            int meshes=0;long triangles=0;
            foreach(var source in Enum.GetValues<GeometryMode>())foreach(var target in Enum.GetValues<GeometryMode>())
            foreach(float blend in new[]{0f,.37f,1f})
            {
                PresentationSettings settings=new(){Animate=true,Opacity=.74f,Geometry=new(){Mode=target,Amount=.81f,Phase=.35f},CrossSections=5};
                settings.Dimensions.AdaptiveQuality=false;
                var layer=settings.EditLayer(item.PresentationKey,"W:+1");layer.Rotation.ZW=87;layer.DepthPercent=137;layer.UseGlobalOpacity=false;layer.OpacityPercent=100;
                foreach(var renderer in new[]{old,current})
                {
                    renderer.ApplySettings(settings.Clone());
                    renderer.SetGeometryMorphForTest(new(){Mode=source,Amount=.63f,Phase=.71f},blend);
                    renderer.ProjectionCycleSeconds=.39;renderer.GeometryCycleSeconds=.57;
                }
                var a=old.GeometrySurfacesForTest(item).Triangles;var b=current.GeometrySurfacesForTest(item).Triangles;
                Require(a.Count==b.Count,"Morph triangle count");
                for(int i=0;i<a.Count;i++)
                {
                    Require(a[i]==b[i],"Morph full triangle equality");
                    Require(Exact(a[i].A,b[i].A)&&Exact(a[i].B,b[i].B)&&Exact(a[i].C,b[i].C),"Morph bit-exact vertices");triangles++;
                }
                meshes++;
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,ExactGrids=grids,ExactMappedVertices=vertices,ExactMorphMeshes=meshes,ExactMorphTriangles=triangles,BoundedCache=true,MaximumEntries=GeometryMapCache.MaximumEntries,MaximumVertices=GeometryMapCache.MaximumVertices,Scope="Geometry mapping and full mesh equality including morphs and independent layer edits; not sustained native gameplay or physical display."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static bool Same(float a,float b)=>BitConverter.SingleToUInt32Bits(a)==BitConverter.SingleToUInt32Bits(b);
    private static bool Exact(Vector4F a,Vector4F b)=>Same(a.X,b.X)&&Same(a.Y,b.Y)&&Same(a.Z,b.Z)&&Same(a.W,b.W);
    private static bool Exact(SurfaceVertex a,SurfaceVertex b)=>Same(a.Point.X,b.Point.X)&&Same(a.Point.Y,b.Point.Y)&&Same(a.Depth,b.Depth)&&Same(a.Q,b.Q)&&Same(a.U,b.U)&&Same(a.V,b.V);
    private static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
}
