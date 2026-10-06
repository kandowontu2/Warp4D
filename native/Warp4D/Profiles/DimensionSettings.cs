using System.Text.Json.Serialization;
using Warp4D.Rendering;

namespace Warp4D.Profiles;

[JsonConverter(typeof(JsonStringEnumConverter<MotionStyle>))]
internal enum MotionStyle { Still, Tumble, Twist, Orbit, Sway }

internal sealed class ObjectMotion
{
    public MotionStyle Style { get; set; }
    public float Strength { get; set; } = .35f;
    public double Speed { get; set; } = .15;
    public void Normalize()
    {
        if (!Enum.IsDefined(Style)) Style = MotionStyle.Still;
        Strength = float.IsFinite(Strength) ? Math.Clamp(Strength, 0, 1) : .35f;
        Speed = double.IsFinite(Speed) ? Math.Clamp(Speed, 0, 2) : .15;
    }
}

internal sealed class DimensionSettings
{
    public bool Choreography { get; set; }
    public Dictionary<string, ObjectMotion> ClassMotions { get; set; } = Defaults();
    public Dictionary<string, ObjectMotion> ObjectMotions { get; set; } = [];
    public bool Trails { get; set; }
    public int TrailLength { get; set; } = 3;
    public float TrailOpacity { get; set; } = .18f;
    public bool AudioReactive { get; set; }
    public float AudioStrength { get; set; } = .3f;
    public bool AdaptiveQuality { get; set; }
    public int TargetFps { get; set; } = 60;
    public void Normalize()
    {
        ClassMotions ??= Defaults(); ObjectMotions ??= [];
        foreach (var dictionary in new[] { ClassMotions, ObjectMotions })
            foreach (string key in dictionary.Keys.ToArray())
                if (dictionary[key] is null) dictionary.Remove(key); else dictionary[key].Normalize();
        TrailLength = Math.Clamp(TrailLength, 1, 6);
        TrailOpacity = float.IsFinite(TrailOpacity) ? Math.Clamp(TrailOpacity, 0, 1) : .18f;
        AudioStrength = float.IsFinite(AudioStrength) ? Math.Clamp(AudioStrength, 0, 1) : .3f;
        TargetFps = Math.Clamp(TargetFps, 20, 120);
    }
    public static Dictionary<string, ObjectMotion> Defaults() => Enum.GetValues<SceneObjectKind>().ToDictionary(k => k.ToString(), k => new ObjectMotion
    {
        Style = k switch { SceneObjectKind.Player or SceneObjectKind.Enemy => MotionStyle.Tumble,
            SceneObjectKind.Item => MotionStyle.Orbit, SceneObjectKind.Pipe => MotionStyle.Twist,
            SceneObjectKind.Bush or SceneObjectKind.Cloud or SceneObjectKind.Tree => MotionStyle.Sway, _ => MotionStyle.Still },
        Strength = k is SceneObjectKind.Player or SceneObjectKind.Item ? .4f : .18f,
        Speed = k is SceneObjectKind.Player or SceneObjectKind.Item ? .2 : .08
    });
}

internal static class DimensionalMotion
{
    public static Rotation4D Offset(ObjectMotion motion, string identity, double seconds)
    {
        uint hash = 2166136261;
        foreach (char c in identity) hash = (hash ^ c) * 16777619;
        float t = (float)(seconds * Math.Tau * motion.Speed) + hash % 6283 / 1000f;
        float a = motion.Strength * 1.1f;
        return motion.Style switch
        {
            MotionStyle.Tumble => new(MathF.Sin(t) * a, MathF.Sin(t * .73f + 1) * a, MathF.Cos(t * 1.17f) * a, 0, 0, 0),
            MotionStyle.Twist => new(0, 0, MathF.Sin(t) * a, MathF.Cos(t * .6f) * a * .25f, 0, 0),
            MotionStyle.Orbit => new(MathF.Cos(t) * a, MathF.Sin(t) * a, 0, 0, 0, MathF.Sin(t * .4f) * a * .25f),
            MotionStyle.Sway => new(MathF.Sin(t) * a * .4f, MathF.Sin(t + 1) * a * .25f, 0, 0, 0, 0),
            _ => default
        };
    }
    public static Rotation4D Add(Rotation4D a, Rotation4D b) => new(a.XW + b.XW, a.YW + b.YW, a.ZW + b.ZW, a.XZ + b.XZ, a.YZ + b.YZ, a.XY + b.XY);
}
