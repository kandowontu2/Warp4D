using System.Numerics;

namespace Warp4D.Rendering;

// Intersect the eight cubical boundary cells of a rotated 4D box with W = plane.
// Each intersection is a genuine face of the resulting 3D polytope, not a clipped XY sheet.
internal static class SolidSection4D
{
    public static List<List<GeometryPoint>> Intersect(float hx, float hy, float hz, float hw, PreparedRotation4D rotation, float plane)
    {
        float[] half = [hx, hy, Math.Max(.01f,hz), Math.Max(.01f,hw)];
        GeometryPoint[] vertices = Enumerable.Range(0,16).Select(i =>
        {
            var p = rotation.Apply(new((i&1)==0?-half[0]:half[0],(i&2)==0?-half[1]:half[1],(i&4)==0?-half[2]:half[2],(i&8)==0?-half[3]:half[3]));
            return new GeometryPoint(p,(i&1)==0?0:1,(i&2)==0?0:1);
        }).ToArray();
        List<List<GeometryPoint>> faces = [];
        for (int fixedAxis=0;fixedAxis<4;fixedAxis++) for(int side=0;side<2;side++)
        {
            List<GeometryPoint> polygon=[];
            for(int i=0;i<16;i++)
            {
                if(((i>>fixedAxis)&1)!=side)continue;
                for(int axis=0;axis<4;axis++)
                {
                    if(axis==fixedAxis || (i&(1<<axis))!=0)continue;
                    GeometryPoint a=vertices[i],b=vertices[i|(1<<axis)];
                    float da=a.Position.W-plane,db=b.Position.W-plane;
                    if(Math.Abs(da)<.00001f)Add(a with { Position=a.Position with {W=plane} });
                    if(Math.Abs(db)<.00001f)Add(b with { Position=b.Position with {W=plane} });
                    if(da*db>=0)continue;
                    float t=da/(da-db);Vector4F p=a.Position,q=b.Position;
                    Add(new(new(p.X+(q.X-p.X)*t,p.Y+(q.Y-p.Y)*t,p.Z+(q.Z-p.Z)*t,plane),a.U+(b.U-a.U)*t,a.V+(b.V-a.V)*t));
                }
            }
            if(polygon.Count<3)continue;
            Vector3 center=polygon.Select(p=>V(p.Position)).Aggregate(Vector3.Zero,(a,b)=>a+b)/polygon.Count;
            Vector3 u=Vector3.Normalize(V(polygon[0].Position)-center),normal=Vector3.Zero;
            for(int j=1;j<polygon.Count&&normal.LengthSquared()<1e-10;j++)normal=Vector3.Cross(u,V(polygon[j].Position)-center);
            if(normal.LengthSquared()<1e-10)continue;
            Vector3 v=Vector3.Normalize(Vector3.Cross(normal,u));
            polygon=polygon.OrderBy(p=>Math.Atan2(Vector3.Dot(V(p.Position)-center,v),Vector3.Dot(V(p.Position)-center,u))).ToList();
            faces.Add(polygon);
            void Add(GeometryPoint p) { if(!polygon.Any(q=>Vector3.DistanceSquared(V(p.Position),V(q.Position))<1e-8f))polygon.Add(p); }
        }
        return faces;
    }
    private static Vector3 V(Vector4F p)=>new(p.X,p.Y,p.Z);
}
