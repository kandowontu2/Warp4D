using System.Text.Json.Serialization;

namespace Warp4D.Profiles;

[JsonConverter(typeof(JsonStringEnumConverter<GeometryMode>))]
internal enum GeometryMode { Hyperprism, Hypersphere, Duocylinder, Slicing, Ribbon, PerspectiveLens, Unfolding }

internal sealed class GeometrySettings
{
    public GeometryMode Mode { get; set; }
    public float Amount { get; set; } = .7f;
    public float Phase { get; set; } = .5f;
    public double Speed { get; set; } = .15;
    public bool Animate { get; set; }
    public GeometrySettings Clone() => new() { Mode = Mode, Amount = Amount, Phase = Phase, Speed = Speed, Animate = Animate };
    public void Normalize()
    {
        if (!Enum.IsDefined(Mode)) Mode = GeometryMode.Hyperprism;
        Amount = float.IsFinite(Amount) ? Math.Clamp(Amount, 0, 1) : .7f;
        Phase = float.IsFinite(Phase) ? Math.Clamp(Phase, 0, 1) : .5f;
        Speed = double.IsFinite(Speed) ? Math.Clamp(Speed, 0, 4) : .15;
    }
    public float Sample(double seconds) => Animate ? (float)((Math.Sin(seconds * Math.Tau * Speed + Phase * Math.Tau) + 1) / 2) : Phase;
}

internal static class GeometryCatalog
{
    internal static readonly string[] Names = ["Hyperprism (classic)", "Hypersphere", "Duocylinder", "4D slicing", "W-axis ribbon", "4D perspective lens", "Dimensional unfolding"];
    internal static readonly string[] Descriptions = [
        "Original textured sheets and sprite-shaped side walls.",
        "Sprite UVs wrap onto spherical sections of a 4D ellipsoid. Cross-sections sample its W extent.",
        "Two circular coordinate planes: sampled disk × circle and circle × disk boundary surfaces.",
        "An exact W plane intersects the rotated 4D box's cubical boundary cells, forming a textured 3D solid section. Phase sweeps the plane; transitions use a slab preview.",
        "The sprite itself bends continuously through Z and W. Amount controls twist; phase moves the wave.",
        "A closer W camera exaggerates fourth-dimensional perspective. Amount controls magnification; phase moves the lens.",
        "Phase blends flat artwork into an opened 4D structure. Amount controls how widely it unfolds."
    ];
}
