using Warp4D.Profiles;

namespace Warp4D.Rendering;

internal static class LookCrossfader
{
    public static PresentationSettings Blend(PresentationSettings a, PresentationSettings b, float amount, PresentationSettings preserved)
    {
        float t = Math.Clamp(amount, 0, 1);
        float Mix(float x, float y) => x + (y - x) * t;
        float Angle(float x, float y) => x + ((y - x + 540) % 360 - 180) * t;
        var result = preserved.Clone();
        result.Name = "Crossfade";
        result.Depth = Mix(a.Depth, b.Depth); result.Perspective = Mix(a.Perspective, b.Perspective);
        // Opacity and per-object edits belong to the user, not the automatic blend.
        result.RotationSpread = Mix(a.RotationSpread, b.RotationSpread);
        result.CrossSections = (int)Math.Round(Mix(a.CrossSections, b.CrossSections));
        result.Rotation = new() { XY = Angle(a.Rotation.XY,b.Rotation.XY), XZ = Angle(a.Rotation.XZ,b.Rotation.XZ),
            XW = Angle(a.Rotation.XW,b.Rotation.XW), YZ = Angle(a.Rotation.YZ,b.Rotation.YZ), YW = Angle(a.Rotation.YW,b.Rotation.YW), ZW = Angle(a.Rotation.ZW,b.Rotation.ZW) };
        result.Geometry = b.Geometry.Clone(); result.Geometry.Animate = false;
        result.Geometry.Amount = Mix(a.Geometry.Amount,b.Geometry.Amount); result.Geometry.Phase = Mix(a.Geometry.Phase,b.Geometry.Phase);
        result.BlendSource = t is >0 and <1 ? a.Geometry.Clone() : null; result.BlendAmount=t;
        if(t==0)result.Geometry=a.Geometry.Clone();
        if(t==1)result.Geometry=b.Geometry.Clone();
        result.Effects.Lighting = Mix(a.Effects.Lighting,b.Effects.Lighting);
        result.Effects.Glow = Mix(a.Effects.Glow,b.Effects.Glow); result.Effects.Shadows = Mix(a.Effects.Shadows,b.Effects.Shadows);
        result.Animate = false; result.Normalize(); return result;
    }
}
