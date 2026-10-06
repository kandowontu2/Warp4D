using Warp4D.Profiles;

namespace Warp4D.Rendering;

internal readonly record struct GeometryPoint(Vector4F Position, float U, float V);

internal static class Geometry4D
{
    internal static List<List<GeometryPoint>> SolidSection(float hx,float hy,float hz,float hw,Rotation4D rotation,float plane)=>
        SolidSection4D.Intersect(hx,hy,hz,hw,new PreparedRotation4D(rotation),plane);
    // UVs stay attached to the actual sprite surface; no generated artwork or framebuffer warps.
    internal static Vector4F Map(GeometryMode mode, float u, float v, float z, float w,
        float hx, float hy, float hz, float hw, float amount, float phase)
    {
        float x = (u * 2 - 1) * hx, y = (v * 2 - 1) * hy;
        float nw = hw <= .0001f ? 0 : Math.Clamp(w / hw, -1, 1);
        float nz = hz <= .0001f ? 0 : Math.Clamp(z / hz, -1, 1);
        switch (mode)
        {
            case GeometryMode.Hypersphere:
            {
                // x²/hx² + y²/hy² + z²/hz² + w²/hw² = 1.
                float level = nw * (.25f + amount * .7f), radius = MathF.Sqrt(1 - level * level);
                float latitude = (v - .5f) * MathF.PI, longitude = (u + phase) * MathF.Tau;
                return new(hx * radius * MathF.Cos(latitude) * MathF.Cos(longitude),
                    hy * radius * MathF.Sin(latitude), hz * radius * MathF.Cos(latitude) * MathF.Sin(longitude), hw * level);
            }
            case GeometryMode.Duocylinder:
            {
                // Sample both boundary families of D² × D², not merely a torus in R³.
                float theta = u * MathF.Tau, radius = v;
                float phi = (Math.Abs(z) > .0001f ? nz * .5f + phase : nw * .8f + phase) * MathF.PI;
                float curvature = .25f + amount * .75f;
                return Math.Abs(z) > .0001f
                    ? new(hx * MathF.Cos(phi), hy * MathF.Sin(phi), hz * curvature * radius * MathF.Cos(theta), hw * curvature * radius * MathF.Sin(theta))
                    : new(hx * radius * MathF.Cos(theta), hy * radius * MathF.Sin(theta), hz * curvature * MathF.Cos(phi), hw * curvature * MathF.Sin(phi));
            }
            case GeometryMode.Ribbon:
            {
                float angle = (u - .5f) * MathF.Tau * (1 + amount * 2) + phase * MathF.Tau;
                float twist = angle * amount;
                return new(x, y * MathF.Cos(twist), z + y * MathF.Sin(twist), w + hw * amount * MathF.Sin(angle));
            }
            case GeometryMode.Unfolding:
            {
                float opened = phase * (1 + amount);
                // phase=0 is precisely the original XY sprite, regardless of selected rotations.
                return new(x, y, z * opened, w * opened);
            }
            default: return new(x, y, z, w);
        }
    }

    internal static List<GeometryPoint> ClipSlab(IEnumerable<GeometryPoint> polygon, float center, float thickness)
    {
        return Clip(Clip(polygon.ToList(), center - thickness, true), center + thickness, false);
        static List<GeometryPoint> Clip(List<GeometryPoint> input, float plane, bool greater)
        {
            List<GeometryPoint> result = [];
            if (input.Count == 0) return result;
            GeometryPoint previous = input[^1]; bool previousInside = greater ? previous.Position.W >= plane : previous.Position.W <= plane;
            foreach (GeometryPoint current in input)
            {
                bool inside = greater ? current.Position.W >= plane : current.Position.W <= plane;
                if (inside != previousInside)
                {
                    float t = (plane - previous.Position.W) / (current.Position.W - previous.Position.W);
                    Vector4F a = previous.Position, b = current.Position;
                    result.Add(new(new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, plane),
                        previous.U + (current.U - previous.U) * t, previous.V + (current.V - previous.V) * t));
                }
                if (inside) result.Add(current);
                previous = current; previousInside = inside;
            }
            return result;
        }
    }
}
