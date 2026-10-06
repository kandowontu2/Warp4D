using Warp4D.Profiles;

namespace Warp4D.Rendering;

internal static class PresentationAnimator
{
    public static void Apply(WarpRendererControl renderer, PresentationSettings settings, double seconds)
    {
        if (!settings.Animate) return;
        if (settings.Geometry.Animate || settings.ObjectGeometries.Values.Concat(settings.ClassGeometries.Values).Any(g => g.Animate)) renderer.GeometryCycleSeconds = seconds;
        if (settings.DepthAnimation.Enabled) renderer.DepthAmount = settings.DepthAnimation.Sample(seconds, 0.15) / 100f;
        if (settings.CameraAnimation.Enabled) renderer.Perspective = settings.CameraAnimation.Sample(seconds, 1.35) / 100f;
        if (settings.AnimateCrossSections) renderer.SliceCount = 2 + (int)Math.Round((Math.Sin(seconds * 0.37) + 1) * 2);
        if (settings.RotationAnimation.Enabled)
        {
            AnimationChannel channel = settings.RotationAnimation;
            float Axis(string name,double speed,double phase) => channel.Sample(seconds * settings.RotationCycleSpeeds.GetValueOrDefault(name,speed),settings.RotationCyclePhases.GetValueOrDefault(name,phase));
            renderer.AngleXYDegrees = Axis("XY",.7,.4);
            renderer.AngleXWDegrees = Axis("XW",1,1.1);
            renderer.AngleYWDegrees = Axis("YW",.81,2.5);
            renderer.AngleZWDegrees = Axis("ZW",1.23,.8);
            renderer.AngleXZDegrees = Axis("XZ",.55,1.8);
            renderer.AngleYZDegrees = Axis("YZ",.46,2.9);
        }
        if (settings.AnimateLayers) renderer.ProjectionCycleSeconds = seconds;
    }

    internal static void EnableCycle(PresentationSettings settings)
    {
        settings.Animate = true;
        settings.RotationAnimation.Enabled = true;
        if(settings.RotationAnimation.Speed<=0) settings.RotationAnimation.Speed=.1;
        if(settings.RotationAnimation.Minimum==settings.RotationAnimation.Maximum)
        { settings.RotationAnimation.Minimum=-85;settings.RotationAnimation.Maximum=85; }
        foreach(var geometry in settings.ObjectGeometries.Values.Concat(settings.ClassGeometries.Values).Prepend(settings.Geometry))
        if(geometry.Mode!=GeometryMode.Hyperprism)
        { geometry.Animate=true;if(geometry.Speed<=0)geometry.Speed=.12; }
    }

    internal static void RandomizeCycle(PresentationSettings settings,Random random)
    {
        EnableCycle(settings);
        foreach(string axis in new[]{"XY","XZ","XW","YZ","YW","ZW"})
        {
            settings.RotationCyclePhases[axis]=random.NextDouble()*Math.Tau;
            settings.RotationCycleSpeeds[axis]=.4+random.NextDouble()*1.3;
        }
        settings.DepthAnimation.Enabled=settings.CameraAnimation.Enabled=true;
        settings.DepthAnimation.Minimum=random.Next(15,35);settings.DepthAnimation.Maximum=random.Next(75,96);
        settings.DepthAnimation.Speed=.045+random.NextDouble()*.07;
        settings.CameraAnimation.Minimum=random.Next(10,25);settings.CameraAnimation.Maximum=random.Next(60,86);
        settings.CameraAnimation.Speed=.035+random.NextDouble()*.065;
        settings.RotationSpread=random.Next(25,101);settings.CrossSections=random.Next(3,6);
        settings.Geometry.Phase=(float)random.NextDouble();settings.Geometry.Speed=.06+random.NextDouble()*.15;
        // Opacity, object/layer edits, recognition and renderer quality stay authored.
        settings.Normalize();
    }
}
