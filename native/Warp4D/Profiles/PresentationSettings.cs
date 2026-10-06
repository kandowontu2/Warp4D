using System.Text.Json;
using Warp4D.Emulation;

namespace Warp4D.Profiles;

internal sealed class PresentationSettings
{
    public string Name { get; set; } = "Custom";
    public VisualEffectsSettings Effects { get; set; } = new();
    public DimensionSettings Dimensions { get; set; } = new();
    public Dictionary<string, GeometrySettings> ClassGeometries { get; set; } = [];
    public GeometrySettings? BlendSource { get; set; }
    public float BlendAmount { get; set; } = 1;
    public float Depth { get; set; } = 0.72f;
    public float Perspective { get; set; } = 0.55f;
    public float Opacity { get; set; } = 0.18f;
    public int CrossSections { get; set; } = 3;
    public float RotationSpread { get; set; } = 58;
    public int RenderScale { get; set; } = 2;
    public RotationAngles Rotation { get; set; } = new() { XW = 24, YW = -16, ZW = 33, XZ = -9, YZ = 6 };
    public bool Animate { get; set; }
    public float CycleSpeed {get;set;}=1;
    public AnimationChannel DepthAnimation { get; set; } = new() { Minimum = 20, Maximum = 100, Speed = 0.12 };
    public AnimationChannel CameraAnimation { get; set; } = new() { Minimum = 4, Maximum = 100, Speed = 0.09 };
    public AnimationChannel RotationAnimation { get; set; } = new() { Minimum = -85, Maximum = 85, Speed = 0.10 };
    public Dictionary<string, double> RotationCyclePhases { get; set; } = [];
    public Dictionary<string, double> RotationCycleSpeeds { get; set; } = [];
    public bool AnimateCrossSections { get; set; }
    public bool AnimateLayers { get; set; } = true;
    public Dictionary<string, Dictionary<string, LayerSettings>> ObjectLayers { get; set; } = [];
    public GeometrySettings Geometry { get; set; } = new();
    public Dictionary<string, GeometrySettings> ObjectGeometries { get; set; } = [];
    public GeometrySettings GeometryFor(string objectKey) => ObjectGeometries.GetValueOrDefault(objectKey) ?? Geometry;
    public GeometrySettings GeometryFor(SceneObject item) => ObjectGeometries.GetValueOrDefault(item.PresentationKey) ?? ClassGeometries.GetValueOrDefault(item.Kind.ToString()) ?? Geometry;
    public ProjectionProfile ProjectionProfile { get; set; } = ProjectionProfile.CreateDefault();

    public PresentationSettings Clone() => JsonSerializer.Deserialize<PresentationSettings>(JsonSerializer.Serialize(this))!;

    public void Normalize()
    {
        Depth = Math.Clamp(Depth, 0, 1);
        Perspective = Math.Clamp(Perspective, 0, 1);
        Opacity = Math.Clamp(Opacity, 0, 1);
        CrossSections = Math.Clamp(CrossSections, 2, 9);
        RotationSpread = Math.Clamp(RotationSpread, 0, 180);
        RenderScale = Math.Clamp(RenderScale, 1, 4);
        Rotation ??= new();
        CycleSpeed=float.IsFinite(CycleSpeed)?Math.Clamp(CycleSpeed,0,2):1;
        Effects ??= new(); Effects.Normalize();
        Dimensions ??= new(); Dimensions.Normalize(); ClassGeometries ??= [];
        BlendSource?.Normalize(); BlendAmount=float.IsFinite(BlendAmount)?Math.Clamp(BlendAmount,0,1):1;
        foreach (string key in ClassGeometries.Keys.ToArray())
            if (ClassGeometries[key] is null) ClassGeometries.Remove(key); else ClassGeometries[key].Normalize();
        Rotation.Normalize();
        DepthAnimation ??= new(); CameraAnimation ??= new(); RotationAnimation ??= new();
        DepthAnimation.Normalize(0, 100); CameraAnimation.Normalize(0, 100); RotationAnimation.Normalize(-180, 180);
        RotationCyclePhases = NormalizeCycles(RotationCyclePhases, true);
        RotationCycleSpeeds = NormalizeCycles(RotationCycleSpeeds, false);
        ObjectLayers ??= [];
        Geometry ??= new(); Geometry.Normalize(); ObjectGeometries ??= [];
        foreach (string key in ObjectGeometries.Keys.ToArray())
            if (ObjectGeometries[key] is null) ObjectGeometries.Remove(key); else ObjectGeometries[key].Normalize();
        foreach (string objectKey in ObjectLayers.Keys.ToArray())
        {
            Dictionary<string, LayerSettings>? layers = ObjectLayers[objectKey];
            if (layers is null) { ObjectLayers.Remove(objectKey); continue; }
            foreach (string layerKey in layers.Keys.ToArray())
            {
                LayerSettings? layer = layers[layerKey];
                if (layer is null) { layers.Remove(layerKey); continue; }
                layer.Normalize();
            }
        }
        ProjectionProfile ??= ProjectionProfile.CreateDefault();
        ProjectionProfile.Normalize();
    }

    private static Dictionary<string,double> NormalizeCycles(Dictionary<string,double>? values,bool phase) =>
        (values ?? []).Where(p=>new[]{"XY","XZ","XW","YZ","YW","ZW"}.Contains(p.Key)&&double.IsFinite(p.Value))
        .ToDictionary(p=>p.Key,p=>phase ? (p.Value % Math.Tau + Math.Tau) % Math.Tau : Math.Clamp(p.Value,.1,3));

    public LayerSettings? LayerFor(string objectKey, string layerKey) =>
        ObjectLayers.TryGetValue(objectKey, out Dictionary<string, LayerSettings>? layers) &&
        layers.TryGetValue(layerKey, out LayerSettings? layer) ? layer : null;

    public LayerSettings EditLayer(string objectKey, string layerKey)
    {
        if (!ObjectLayers.TryGetValue(objectKey, out Dictionary<string, LayerSettings>? layers))
            ObjectLayers[objectKey] = layers = [];
        if (!layers.TryGetValue(layerKey, out LayerSettings? layer))
            layers[layerKey] = layer = new() { UseGlobalOpacity = layerKey != "Center", UseAutomaticRotation = layerKey != "Center" };
        return layer;
    }

    public static PresentationSettings Preset(string name) => name switch
    {
        "Subtle depth" => new() { Name = name, Depth = 0.35f, Opacity = 0.25f, RotationSpread = 15, CrossSections = 3 },
        "Strong rotation" => new() { Name = name, Depth = 0.90f, Opacity = 0.45f, RotationSpread = 100, CrossSections = 5,
            Animate = true, DepthAnimation = new() { Enabled = false }, CameraAnimation = new() { Enabled = false } },
        "Fully opaque" => new() { Name = name, Opacity = 1f, RotationSpread = 35, CrossSections = 5 },
        _ => new()
    };
}

internal sealed class RotationAngles
{
    public float XY { get; set; }
    public float XZ { get; set; }
    public float XW { get; set; }
    public float YZ { get; set; }
    public float YW { get; set; }
    public float ZW { get; set; }
    public void Normalize()
    {
        XY = Math.Clamp(XY, -180, 180); XZ = Math.Clamp(XZ, -180, 180); XW = Math.Clamp(XW, -180, 180);
        YZ = Math.Clamp(YZ, -180, 180); YW = Math.Clamp(YW, -180, 180); ZW = Math.Clamp(ZW, -180, 180);
    }
}

internal sealed class VisualEffectsSettings
{
    public bool Enabled { get; set; } = true;
    public float Lighting { get; set; } = .4f;
    public float Glow { get; set; } = .35f;
    public float Shadows { get; set; } = .3f;
    public bool SmoothTransitions { get; set; } = true;
    public void Normalize()
    {
        Lighting = float.IsFinite(Lighting) ? Math.Clamp(Lighting,0,1) : .4f;
        Glow = float.IsFinite(Glow) ? Math.Clamp(Glow,0,1) : .35f;
        Shadows = float.IsFinite(Shadows) ? Math.Clamp(Shadows,0,1) : .3f;
    }
}

internal sealed class AnimationChannel
{
    public bool Enabled { get; set; } = true;
    public double Speed { get; set; } = 0.1;
    public float Minimum { get; set; }
    public float Maximum { get; set; } = 100;
    public float Sample(double seconds, double phase = 0) =>
        Minimum + (Maximum - Minimum) * (float)((Math.Sin(seconds * Math.Tau * Speed + phase) + 1) / 2);
    public void Normalize(float low, float high)
    {
        Speed = Math.Clamp(Speed, 0, 4);
        Minimum = Math.Clamp(Minimum, low, high); Maximum = Math.Clamp(Maximum, Minimum, high);
    }
}

internal sealed class LayerSettings
{
    public bool Enabled { get; set; } = true;
    public RotationAngles Rotation { get; set; } = new();
    public bool UseAutomaticRotation { get; set; } = true;
    public bool UseGlobalOpacity { get; set; } = true;
    public int OpacityPercent { get; set; } = 100;
    public int DepthPercent { get; set; } = 100;
    public double AnimationSpeed { get; set; } = 1;
    public void Normalize()
    {
        Rotation ??= new(); Rotation.Normalize();
        OpacityPercent = Math.Clamp(OpacityPercent, 0, 100);
        DepthPercent = Math.Clamp(DepthPercent, 0, 200);
        AnimationSpeed = Math.Clamp(AnimationSpeed, 0, 4);
    }
}

internal static class PresentationSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static string DirectoryPath => Path.Combine(AppPaths.DataDirectory, "presentations");
    private static string FilePath(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid ROM hash.");
        return Path.Combine(DirectoryPath, hash.ToUpperInvariant() + ".json");
    }
    public static PresentationSettings? Load(string hash)
    {
        try { return File.Exists(FilePath(hash)) ? ReadFromFile(FilePath(hash)) : null; }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException) { return null; }
    }
    public static void Save(string hash, PresentationSettings settings) => WriteToFile(FilePath(hash), settings);
    public static PresentationSettings ReadFromFile(string path)
    {
        PresentationSettings settings = JsonSerializer.Deserialize<PresentationSettings>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Invalid presentation preset.");
        settings.Normalize(); return settings;
    }
    public static void WriteToFile(string path, PresentationSettings settings)
    {
        PresentationSettings snapshot = settings.Clone(); snapshot.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
