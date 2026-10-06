using System.Text.Json;
using Warp4D.Emulation;

namespace Warp4D.Profiles;

internal sealed class ProjectionProfile
{
    public const int CurrentFormatVersion = 1;
    public const int MinimumDepthPercent = 25;
    public const int MaximumDepthPercent = 200;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Name { get; set; } = "Default projection";
    public Dictionary<string, ObjectProjectionRule> Objects { get; set; } = [];

    public static ProjectionProfile CreateDefault(string name = "Default projection")
    {
        ProjectionProfile profile = new() { Name = name };
        profile.Normalize();
        return profile;
    }

    public ObjectProjectionRule RuleFor(SceneObjectKind kind)
    {
        string key = kind.ToString();
        if (Objects.TryGetValue(key, out ObjectProjectionRule? rule) && rule is not null)
        {
            return rule;
        }

        // Unknown or newly added object kinds use safe defaults without mutating
        // a profile that may currently be read by the capture thread.
        return new ObjectProjectionRule();
    }

    public ProjectionProfile Clone()
    {
        ProjectionProfile clone = new()
        {
            FormatVersion = FormatVersion,
            Name = Name,
            Objects = (Objects ?? []).ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.Clone() ?? new ObjectProjectionRule(),
                StringComparer.OrdinalIgnoreCase)
        };
        clone.Normalize();
        return clone;
    }

    public void Normalize()
    {
        if (FormatVersion > CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"This profile uses format {FormatVersion}, but this Warp4D build supports format {CurrentFormatVersion}.");
        }

        FormatVersion = CurrentFormatVersion;
        Name = string.IsNullOrWhiteSpace(Name) ? "Custom projection" : Name.Trim();
        if (Name.Length > 48)
        {
            Name = Name[..48];
        }

        Objects ??= [];
        Dictionary<string, ObjectProjectionRule> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach (SceneObjectKind kind in Enum.GetValues<SceneObjectKind>())
        {
            ObjectProjectionRule rule = Objects.TryGetValue(kind.ToString(), out ObjectProjectionRule? existing)
                ? existing ?? new ObjectProjectionRule()
                : new ObjectProjectionRule();
            rule.DepthPercent = Math.Clamp(rule.DepthPercent, MinimumDepthPercent, MaximumDepthPercent);
            normalized[kind.ToString()] = rule;
        }
        Objects = normalized;
    }

    public static string DisplayName(SceneObjectKind kind) => kind switch
    {
        SceneObjectKind.Bush => "Bushes",
        SceneObjectKind.Cloud => "Clouds",
        SceneObjectKind.Hill => "Hills",
        SceneObjectKind.Tree => "Trees",
        SceneObjectKind.Pipe => "Pipes",
        SceneObjectKind.QuestionBlock => "Question blocks",
        SceneObjectKind.Brick => "Bricks",
        SceneObjectKind.Terrain => "Terrain",
        SceneObjectKind.Castle => "Castles",
        SceneObjectKind.Flagpole => "Flagpoles",
        SceneObjectKind.Player => "Mario / player",
        SceneObjectKind.Enemy => "Enemies",
        SceneObjectKind.Item => "Items / power-ups",
        SceneObjectKind.Sprite => "Other sprites / effects",
        _ => kind.ToString()
    };
}

internal sealed class ObjectProjectionRule
{
    public bool Enabled { get; set; } = true;
    public int DepthPercent { get; set; } = 100;
    public float DepthScale => DepthPercent / 100f;

    public ObjectProjectionRule Clone() => new()
    {
        Enabled = Enabled,
        DepthPercent = DepthPercent
    };
}

internal static class ProjectionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "projection-profile.json");

    public static ProjectionProfile Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? ReadFromFile(FilePath)
                : ProjectionProfile.CreateDefault();
        }
        catch
        {
            // A damaged or future-format profile should never prevent the emulator
            // from opening. The editor can overwrite it with a valid profile.
            return ProjectionProfile.CreateDefault("Recovered default");
        }
    }

    public static void Save(ProjectionProfile profile) => WriteToFile(FilePath, profile);

    public static ProjectionProfile ReadFromFile(string path)
    {
        string json = File.ReadAllText(Path.GetFullPath(path));
        ProjectionProfile profile = JsonSerializer.Deserialize<ProjectionProfile>(json, JsonOptions)
            ?? throw new InvalidDataException("The selected file does not contain a Warp4D projection profile.");
        profile.Normalize();
        return profile;
    }

    public static void WriteToFile(string path, ProjectionProfile profile)
    {
        ProjectionProfile normalized = profile.Clone();
        string absolutePath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(absolutePath, JsonSerializer.Serialize(normalized, JsonOptions));
    }
}
