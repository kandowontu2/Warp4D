using System.Drawing;
using System.Numerics;

namespace Warp4D.Rendering;

internal readonly record struct Vector4F(float X, float Y, float Z, float W)
{
    public float this[int axis] => axis switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        3 => W,
        _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };
}

internal readonly record struct Projected4D(PointF Point, float CameraDepth, float RotatedW, float Scale4D);

internal readonly record struct Rotation4D(
    float XW,
    float YW,
    float ZW,
    float XZ,
    float YZ,
    float XY)
{
    public static Rotation4D Default => new(
        XW: 0.42f,
        YW: -0.28f,
        ZW: 0.58f,
        XZ: -0.16f,
        YZ: 0.10f,
        XY: 0f);
}

internal readonly struct PreparedRotation4D
{
    private readonly float _xwCos;
    private readonly float _xwSin;
    private readonly float _ywCos;
    private readonly float _ywSin;
    private readonly float _zwCos;
    private readonly float _zwSin;
    private readonly float _xzCos;
    private readonly float _xzSin;
    private readonly float _yzCos;
    private readonly float _yzSin;
    private readonly float _xyCos;
    private readonly float _xySin;

    public PreparedRotation4D(Rotation4D rotation)
    {
        (_xwSin, _xwCos) = MathF.SinCos(rotation.XW);
        (_ywSin, _ywCos) = MathF.SinCos(rotation.YW);
        (_zwSin, _zwCos) = MathF.SinCos(rotation.ZW);
        (_xzSin, _xzCos) = MathF.SinCos(rotation.XZ);
        (_yzSin, _yzCos) = MathF.SinCos(rotation.YZ);
        (_xySin, _xyCos) = MathF.SinCos(rotation.XY);
    }

    public Vector4F Apply(Vector4F input)
    {
        float x = input.X;
        float y = input.Y;
        float z = input.Z;
        float w = input.W;

        RotatePlane(ref x, ref y, _xyCos, _xySin);
        RotatePlane(ref x, ref w, _xwCos, _xwSin);
        RotatePlane(ref y, ref w, _ywCos, _ywSin);
        RotatePlane(ref z, ref w, _zwCos, _zwSin);
        RotatePlane(ref x, ref z, _xzCos, _xzSin);
        RotatePlane(ref y, ref z, _yzCos, _yzSin);
        return new Vector4F(x, y, z, w);
    }

    private static void RotatePlane(ref float first, ref float second, float cosine, float sine)
    {
        float originalFirst = first;
        float originalSecond = second;
        first = originalFirst * cosine - originalSecond * sine;
        second = originalFirst * sine + originalSecond * cosine;
    }

    internal void ProjectVertices(ReadOnlySpan<PointF> points,float z,float w,float camera4,float camera3,PointF center,float scale,Span<SurfaceVertex> vertices)
    {
        if(vertices.Length<points.Length)throw new ArgumentException("Projection destination too short.");
        int index=0,lanes=Vector<float>.Count;
        if(Vector.IsHardwareAccelerated && camera4>0 && camera3>0 && points.Length>=lanes)
        {
            Span<float> xs=stackalloc float[lanes],ys=stackalloc float[lanes];
            Span<float> depths=stackalloc float[lanes],qs=stackalloc float[lanes];
            Vector<float> c4=new(camera4),c3=new(camera3),min4=new(camera4*.16f),min3=new(camera3*.16f);
            Vector<float> cx=new(center.X),cy=new(center.Y),screenScale=new(scale);
            for(;index<=points.Length-lanes;index+=lanes)
            {
                for(int lane=0;lane<lanes;lane++){xs[lane]=points[index+lane].X;ys[lane]=points[index+lane].Y;}
                Vector<float> x=new(xs),y=new(ys),zz=new(z),ww=new(w);
                RotateVectorPlane(ref x,ref y,_xyCos,_xySin);
                RotateVectorPlane(ref x,ref ww,_xwCos,_xwSin);
                RotateVectorPlane(ref y,ref ww,_ywCos,_ywSin);
                RotateVectorPlane(ref zz,ref ww,_zwCos,_zwSin);
                RotateVectorPlane(ref x,ref zz,_xzCos,_xzSin);
                RotateVectorPlane(ref y,ref zz,_yzCos,_yzSin);
                Vector<float> q4=c4/Vector.Max(min4,c4-ww);
                Vector<float> x3=x*q4,y3=y*q4,z3=zz*q4;
                Vector<float> q3=c3/Vector.Max(min3,c3-z3);
                (cx+(x3*q3)*screenScale).CopyTo(xs);
                (cy+(y3*q3)*screenScale).CopyTo(ys);
                (z3/c3).CopyTo(depths);(q4*q3).CopyTo(qs);
                for(int lane=0;lane<lanes;lane++)vertices[index+lane]=new(new(xs[lane],ys[lane]),depths[lane],qs[lane]);
            }
        }
        for(;index<points.Length;index++)
        {
            var p=FourDMath.Project(new(points[index].X,points[index].Y,z,w),this,camera4,camera3);
            vertices[index]=new(new(center.X+p.Point.X*scale,center.Y+p.Point.Y*scale),p.CameraDepth/camera3,p.Scale4D);
        }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void RotateVectorPlane(ref Vector<float> first,ref Vector<float> second,float cosine,float sine)
    {
        var a=first;var b=second;
        first=a*new Vector<float>(cosine)-b*new Vector<float>(sine);
        second=a*new Vector<float>(sine)+b*new Vector<float>(cosine);
    }
}

internal static class FourDMath
{
    public static Vector4F Rotate(Vector4F input, Rotation4D rotation) =>
        new PreparedRotation4D(rotation).Apply(input);

    public static Projected4D Project(
        Vector4F input,
        Rotation4D rotation,
        float fourDimensionalCamera,
        float threeDimensionalCamera) =>
        Project(input, new PreparedRotation4D(rotation), fourDimensionalCamera, threeDimensionalCamera);

    public static Projected4D Project(
        Vector4F input,
        PreparedRotation4D rotation,
        float fourDimensionalCamera,
        float threeDimensionalCamera)
    {
        return ProjectRotated(rotation.Apply(input),fourDimensionalCamera,threeDimensionalCamera);
    }

    public static Projected4D ProjectRotated(Vector4F rotated,float fourDimensionalCamera,float threeDimensionalCamera)
    {
        // First perspective divide: R4 -> R3, with the camera on the +W axis.
        float denominator4D = Math.Max(fourDimensionalCamera * 0.16f, fourDimensionalCamera - rotated.W);
        float scale4D = fourDimensionalCamera / denominator4D;
        float x3 = rotated.X * scale4D;
        float y3 = rotated.Y * scale4D;
        float z3 = rotated.Z * scale4D;

        // Second perspective divide: R3 -> the 2D monitor, camera on +Z.
        float denominator3D = Math.Max(threeDimensionalCamera * 0.16f, threeDimensionalCamera - z3);
        float scale3D = threeDimensionalCamera / denominator3D;
        return new Projected4D(
            new PointF(x3 * scale3D, y3 * scale3D),
            z3,
            rotated.W,
            scale4D * scale3D);
    }

    public static Vector4F[] CreateHyperprism(float halfX, float halfY, float halfZ, float halfW)
    {
        Vector4F[] vertices = new Vector4F[16];
        for (int bits = 0; bits < vertices.Length; bits++)
        {
            vertices[bits] = new Vector4F(
                (bits & 0b0001) == 0 ? -halfX : halfX,
                (bits & 0b0010) == 0 ? -halfY : halfY,
                (bits & 0b0100) == 0 ? -halfZ : halfZ,
                (bits & 0b1000) == 0 ? -halfW : halfW);
        }
        return vertices;
    }

    public static IEnumerable<(int Start, int End, int Axis)> HyperprismEdges()
    {
        for (int vertex = 0; vertex < 16; vertex++)
        {
            for (int axis = 0; axis < 4; axis++)
            {
                int neighbor = vertex ^ (1 << axis);
                if (vertex < neighbor)
                {
                    yield return (vertex, neighbor, axis);
                }
            }
        }
    }

    public static void Validate()
    {
        Vector4F untouched = Rotate(new Vector4F(2, 3, 4, 5), default);
        if (Math.Abs(untouched.X - 2) > 0.0001f ||
            Math.Abs(untouched.Y - 3) > 0.0001f ||
            Math.Abs(untouched.Z - 4) > 0.0001f ||
            Math.Abs(untouched.W - 5) > 0.0001f)
        {
            throw new InvalidOperationException("The identity R4 rotation is invalid.");
        }

        Vector4F quarterTurn = Rotate(
            new Vector4F(1, 0, 0, 0),
            new Rotation4D(XW: MathF.PI / 2, YW: 0, ZW: 0, XZ: 0, YZ: 0, XY: 0));
        if (Math.Abs(quarterTurn.X) > 0.0001f || Math.Abs(quarterTurn.W - 1) > 0.0001f)
        {
            throw new InvalidOperationException("The XW plane rotation is invalid.");
        }

        Vector4F xyQuarterTurn = Rotate(
            new Vector4F(1, 0, 0, 0),
            new Rotation4D(XW: 0, YW: 0, ZW: 0, XZ: 0, YZ: 0, XY: MathF.PI / 2));
        if (Math.Abs(xyQuarterTurn.X) > 0.0001f || Math.Abs(xyQuarterTurn.Y - 1) > 0.0001f)
        {
            throw new InvalidOperationException("The XY plane rotation is invalid.");
        }

        if (HyperprismEdges().Count() != 32)
        {
            throw new InvalidOperationException("A 4D hyperprism must expose 32 edges.");
        }
    }

}
