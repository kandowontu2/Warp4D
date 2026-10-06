using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Warp4D.Emulation;

namespace Warp4D.Profiles;

internal sealed class GameRecognitionProfile
{
    public const int CurrentFormatVersion = 21;

    public int FormatVersion { get; set; } = 6;
    public string Name { get; set; } = "Custom game profile";
    public string RomName { get; set; } = string.Empty;
    public string RomSha256 { get; set; } = string.Empty;
    public int GameplayStateAddress { get; set; } = -1;
    public int GameplayStateValue { get; set; } = 2;
    public bool TransparentBackdrop { get; set; }
    public bool SeparateMetatiles { get; set; }
    // Explicit opt-in: never infer identities from arbitrary screen fragments.
    public bool AllowMaskedLeftEdgeArtwork { get; set; }
    public int CellSize { get; set; } = 16;
    public bool RequireRecognizedScene { get; set; }
    public int MinimumScenePatterns { get; set; } = 2;
    public List<SceneAnchor> SceneAnchors { get; set; } = [];
    public List<Rectangle> FlatRegions { get; set; } = [];
    // Sprite HUDs must not protect the world scenery underneath them.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Rectangle>? FlatSpriteRegions { get; set; }
    internal bool ProtectsSprite(Rectangle bounds) => FlatSpriteRegions?.Any(r=>r.IntersectsWith(bounds))==true;
    // Explicit per-state owners replace spatial sprite guards in that state:
    // an overworld score rectangle must not flatten fortress enemies there.
    internal bool ProtectsSprite(Rectangle bounds,NesFrame frame) =>
        !(FlatSpriteStateAddress is int address && address>=0 && address<frame.Ram.Length &&
          FlatSpriteSlotsByState?.ContainsKey(frame.Ram[address])==true) && ProtectsSprite(bounds);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FlatSpriteStateAddress { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<int,List<int>>? FlatSpriteSlotsByState { get; set; }
    internal bool ProtectsSpriteSlot(NesFrame frame,int index) =>
        FlatSpriteStateAddress is int address && address>=0 && address<frame.Ram.Length &&
        FlatSpriteSlotsByState?.TryGetValue(frame.Ram[address],out var slots)==true && slots.Contains(index);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ConditionalFlatRegion>? ConditionalFlatRegions { get; set; }
    public List<Rectangle> ObjectRegions { get; set; } = [];
    public List<int> PlayerOamIndices { get; set; } = [];
    public string PlayerLabel { get; set; } = "Player";
    // Opt-in for games whose OAM slots vary with enemy/projectile ordering.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayerSpriteTracking? PlayerTracking { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SpriteAssembly>? SpriteAssemblies { get; set; }
    // Explicit tracked-player exception only. Background/HUD sprites and
    // conditional interface guards retain their normal protection.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PlayerOverlaysFlatHud { get; set; }
    internal bool AllowsTrackedPlayer(Rectangle bounds, NesFrame frame) =>
        PlayerOverlaysFlatHud && PlayerTracking is not null
            ? (ObjectRegions.Count == 0 || ObjectRegions.Any(r => r.Contains(bounds))) &&
              !(ConditionalFlatRegions?.Any(r => r.Matches(frame) && r.Bounds.IntersectsWith(bounds)) ?? false)
            : Allows(bounds, frame);
    public bool Protects(Rectangle bounds) => FlatRegions.Any(r => r.IntersectsWith(bounds));
    public bool Allows(Rectangle bounds) => !Protects(bounds) && (ObjectRegions.Count == 0 || ObjectRegions.Any(r => r.Contains(bounds)));
    public bool Allows(Rectangle bounds, NesFrame frame) => Allows(bounds) &&
        !(ConditionalFlatRegions?.Any(r => r.Matches(frame) && r.Bounds.IntersectsWith(bounds)) ?? false);
    public IReadOnlyList<Rectangle> FlatRegionsFor(NesFrame frame) => ConditionalFlatRegions is null
        ? FlatRegions : FlatRegions.Concat(ConditionalFlatRegions.Where(r => r.Matches(frame)).Select(r => r.Bounds)).ToArray();
    public bool IsActive(NesFrame frame)
    {
        // Rendering-disabled loading frames still contain stale PPU tile/OAM
        // memory. A gameplay RAM value alone cannot make that artwork visible.
        if(frame.PpuMask is byte mask && (mask&0x18)==0)return false;
        if(frame.IsUniformPairedFade())return false;
        if (GameplayStateAddress >= 0 && frame.Ram[GameplayStateAddress] != GameplayStateValue) return false;
        // An explicitly protected whole screen is an inactive interface, not
        // a gameplay scene containing independently extracted flat sprites.
        // Keep transitions pixel-exact and avoid duplicate stale actor sheets.
        if (ConditionalFlatRegions?.Any(r => r.Bounds == new Rectangle(0, 0, 256, 240) && r.Matches(frame)) == true)
            return false;
        if (!RequireRecognizedScene) return true;
        if (SceneAnchors.Count > 0) return SceneAnchors.All(a =>
            MetatileSignature.Read(frame,(frame.ScrollX+a.X)/8,(frame.ScrollY+a.Y)/8,8).Key==a.Key &&
            MetatileVisualFingerprint.Read(frame,(frame.ScrollX+a.X)/8,(frame.ScrollY+a.Y)/8,8)==a.Fingerprint);
        HashSet<string> matches = [];
        int step = CellSize / 8;
        for (int y = frame.ScrollY / 8 / step * step; y <= (frame.ScrollY + 239) / 8; y += step)
        for (int x = frame.ScrollX / 8 / step * step; x <= (frame.ScrollX + 255) / 8; x += step)
        {
            if (!Allows(new(x * 8 - frame.ScrollX, y * 8 - frame.ScrollY, CellSize, CellSize), frame)) continue;
            var signature = MetatileSignature.Read(frame, x, y, CellSize);
            if (Match(signature, frame, x, y) is not null &&
                (frame.NativeScreenPixels is null || MetatileVisualFingerprint.IsVisible(frame,x,y,CellSize,AllowMaskedLeftEdgeArtwork))) matches.Add(signature.Key);
            if (matches.Count >= MinimumScenePatterns) return true;
        }
        return false;
    }
    public Dictionary<string, BackgroundObjectRule> BackgroundRules { get; set; } = [];

    public static GameRecognitionProfile Create(string romName, string romSha256) => new()
    {
        Name = $"{Path.GetFileNameWithoutExtension(romName)} objects",
        RomName = Path.GetFileName(romName),
        RomSha256 = romSha256
    };

    public BackgroundObjectRule? Match(
        MetatileSignature signature,
        NesFrame frame,
        int worldTileX,
        int worldTileY)
    {
        if(frame.PpuMask is byte mask && (mask&0x08)==0)return null;
        if (GameplayStateAddress >= 0 && (GameplayStateAddress >= frame.Ram.Length || frame.Ram[GameplayStateAddress] != GameplayStateValue)) return null;
        if (!BackgroundRules.TryGetValue(signature.Key, out BackgroundObjectRule? rule))
        {
            return null;
        }

        // Profiles created by 1.0 only stored tile numbers. Keep those files
        // working; re-selecting a pattern in the editor upgrades it to artwork-
        // aware matching and prevents CHR-bank aliases on menus/title screens.
        if (!rule.HasVisualFingerprint)
        {
            return rule;
        }

        string visibleArtwork = MetatileVisualFingerprint.Read(frame, worldTileX, worldTileY, CellSize);
        if (rule.AcceptsArtwork(visibleArtwork)) return rule;
        if (AllowMaskedLeftEdgeArtwork && CellSize == 16 &&
            MetatileVisualFingerprint.HasVerifiedMaskedLeftHalf(frame,worldTileX,worldTileY))
        {
            string rightHalf = MetatileVisualFingerprint.ReadRightHalf(frame,worldTileX,worldTileY);
            // A masked cell can contain a different hidden tile/palette half.
            // Its declared rule must already exist, and the visible glyph must
            // be known in the same object class; no unknown keys are invented.
            foreach (var known in BackgroundRules.Values)
                if (known.Kind == rule.Kind)
                    foreach (var variant in known.ArtworkVariants)
                        if (variant.RightHalfFingerprint.Length == 64 && variant.RightHalfFingerprint == rightHalf) return rule;
        }
        return null;
    }

    public GameRecognitionProfile Clone()
    {
        GameRecognitionProfile clone = new()
        {
            FormatVersion = FormatVersion,
            Name = Name,
            RomName = RomName,
            RomSha256 = RomSha256,
            GameplayStateAddress = GameplayStateAddress,
            GameplayStateValue = GameplayStateValue,
            TransparentBackdrop = TransparentBackdrop,
            SeparateMetatiles = SeparateMetatiles,
            AllowMaskedLeftEdgeArtwork = AllowMaskedLeftEdgeArtwork,
            CellSize = CellSize, RequireRecognizedScene = RequireRecognizedScene,
            MinimumScenePatterns = MinimumScenePatterns,
            SceneAnchors = SceneAnchors.Select(a=>new SceneAnchor{X=a.X,Y=a.Y,Key=a.Key,Fingerprint=a.Fingerprint}).ToList(),
            FlatRegions = [..FlatRegions], ObjectRegions = [..ObjectRegions], PlayerOamIndices = [..PlayerOamIndices], PlayerLabel = PlayerLabel,
            FlatSpriteRegions = FlatSpriteRegions is null ? null : [..FlatSpriteRegions],
            FlatSpriteStateAddress = FlatSpriteStateAddress,
            FlatSpriteSlotsByState = FlatSpriteSlotsByState?.ToDictionary(p=>p.Key,p=>new List<int>(p.Value)),
            PlayerOverlaysFlatHud = PlayerOverlaysFlatHud,
            SpriteAssemblies = SpriteAssemblies?.Select(a=>a.Clone()).ToList(),
            PlayerTracking = PlayerTracking is null ? null : new PlayerSpriteTracking
                { XAddress=PlayerTracking.XAddress,YAddress=PlayerTracking.YAddress,SpritePalette=PlayerTracking.SpritePalette,MaximumWidth=PlayerTracking.MaximumWidth,MaximumHeight=PlayerTracking.MaximumHeight,PositionTolerance=PlayerTracking.PositionTolerance,XScrollAddress=PlayerTracking.XScrollAddress,YScrollAddress=PlayerTracking.YScrollAddress,
                  BodyAssembly=PlayerTracking.BodyAssembly?.Clone(),VerticalWrap=PlayerTracking.VerticalWrap?.Clone(),PaletteStateAddress=PlayerTracking.PaletteStateAddress,PalettesByState=PlayerTracking.PalettesByState is null ? null : new(PlayerTracking.PalettesByState),PaletteOamByState=PlayerTracking.PaletteOamByState is null ? null : new(PlayerTracking.PaletteOamByState),BodyOamCountByState=PlayerTracking.BodyOamCountByState is null ? null : new(PlayerTracking.BodyOamCountByState),BodyExtraOamIndicesByState=PlayerTracking.BodyExtraOamIndicesByState?.ToDictionary(p=>p.Key,p=>new List<int>(p.Value)),BodyTilesByAnimation=PlayerTracking.BodyTilesByAnimation?.ToDictionary(p=>p.Key,p=>new List<int>(p.Value)) },
            ConditionalFlatRegions = ConditionalFlatRegions?.Select(r => new ConditionalFlatRegion
                { Bounds = r.Bounds, ExpectedRam = new(r.ExpectedRam), ExcludedRam = r.ExcludedRam is null ? null : new(r.ExcludedRam) }).ToList(),
            BackgroundRules = (BackgroundRules ?? []).ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.Clone() ?? new BackgroundObjectRule(),
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
                $"This game profile uses format {FormatVersion}, but this Warp4D build supports format {CurrentFormatVersion}.");
        }
        if(SpriteAssemblies is not null)
        {
            if(SpriteAssemblies.Count is <1 or >64||SpriteAssemblies.Any(a=>a is null))throw new InvalidDataException("Bounded sprite assemblies required.");
            foreach(var assembly in SpriteAssemblies)assembly.Validate();
            FormatVersion=Math.Max(FormatVersion,20);
        }
        FlatSpriteRegions=FlatSpriteRegions?.Select(r=>Rectangle.Intersect(r,new(0,0,256,240))).Where(r=>r.Width>0&&r.Height>0).Take(32).ToList();
        if(FlatSpriteRegions?.Count==0)FlatSpriteRegions=null;
        if(FlatSpriteRegions is not null)FormatVersion=Math.Max(FormatVersion,18);
        if(FlatSpriteSlotsByState is not null || FlatSpriteStateAddress is not null)
        {
            if(FlatSpriteStateAddress is not (>=0 and <2048) || FlatSpriteSlotsByState is not {Count:>0} slots || slots.Count>256 ||
               slots.Any(p=>p.Key is <0 or >255 || p.Value is null || p.Value.Count is <1 or >64 ||
                   p.Value.Any(i=>i is <0 or >63) || p.Value.Distinct().Count()!=p.Value.Count))
                throw new InvalidDataException("Sprite HUD ownership requires a valid native RAM state address and distinct in-bounds OAM slots.");
            foreach(var pair in slots)
                if(pair.Value.Any(i=>PlayerOamIndices?.Contains(i)==true) ||
                   PlayerTracking?.PaletteStateAddress==FlatSpriteStateAddress &&
                   PlayerTracking.PaletteOamByState?.TryGetValue(pair.Key,out int first)==true &&
                   PlayerTracking.BodyOamCountByState?.TryGetValue(pair.Key,out int count)==true &&
                   pair.Value.Any(i=>i>=first&&i<first+count || PlayerTracking.BodyExtraOamIndicesByState?.GetValueOrDefault(pair.Key)?.Contains(i)==true))
                    throw new InvalidDataException("Sprite HUD ownership overlaps declared player body slots.");
            FlatSpriteSlotsByState=slots.ToDictionary(p=>p.Key,p=>new List<int>(p.Value));
            FormatVersion=Math.Max(FormatVersion,19);
        }
        if(PlayerOverlaysFlatHud)
        {
            if(PlayerTracking is null)throw new InvalidDataException("Player/HUD overlap requires explicit native player tracking.");
            FormatVersion=Math.Max(FormatVersion,15);
        }

        // Keep existing exports byte-compatible. State-dependent protection
        // needs format 7; coordinate tracking needs 8, scroll conversion 9,
        // exclusion guards on stale room IDs need 10.
        ConditionalFlatRegions = ConditionalFlatRegions?.Where(r => r is not null && r.ExpectedRam is { Count: > 0 } &&
            r.ExpectedRam.All(e => e.Key is >= 0 and < 2048 && e.Value is >= 0 and <= 255) &&
            (r.ExcludedRam is null || r.ExcludedRam.All(e => e.Key is >= 0 and < 2048 && e.Value is >= 0 and <= 255)))
            .Select(r => new ConditionalFlatRegion { Bounds = Rectangle.Intersect(r.Bounds, new(0,0,256,240)),
                ExpectedRam = new(r.ExpectedRam), ExcludedRam = r.ExcludedRam is {Count:>0} ? new(r.ExcludedRam) : null }).Where(r => r.Bounds.Width > 0 && r.Bounds.Height > 0).Take(32).ToList();
        if (ConditionalFlatRegions?.Count == 0) ConditionalFlatRegions = null;
        FormatVersion = Math.Max(FormatVersion, ConditionalFlatRegions is null ? 6 : 7);
        if(ConditionalFlatRegions?.Any(r=>r.ExcludedRam is not null)==true)FormatVersion=Math.Max(FormatVersion,10);
        if(PlayerTracking is not null)
        {
            if(PlayerTracking.BodyAssembly is {} bodyAssembly)
            {
                bodyAssembly.Validate(allowSingleTile:true);
                if(PlayerTracking.BodyTilesByAnimation is not null||PlayerTracking.PaletteStateAddress is not null||PlayerTracking.BodyOamCountByState is not null||PlayerTracking.BodyExtraOamIndicesByState is not null)
                    throw new InvalidDataException("Complete player layouts cannot be combined with a different sprite owner.");
                FormatVersion=Math.Max(FormatVersion,21);
            }
            if(PlayerTracking.BodyTilesByAnimation is not null)
            {
                if(PlayerTracking.BodyTilesByAnimation is not {} animationFrames||animationFrames.Count==0||animationFrames.Count>256||
                   animationFrames.Any(p=>p.Key is <0 or >255||p.Value is null||p.Value.Count is <1 or >16||p.Value.Any(tile=>tile is <0 or >255))||
                   PlayerTracking.BodyOamCountByState is not null||PlayerTracking.BodyExtraOamIndicesByState is not null||PlayerTracking.PaletteStateAddress is not null||PlayerTracking.PaletteOamByState is not null||PlayerTracking.PalettesByState is not null)
                    throw new InvalidDataException("Animation-owned player bodies require1–16 ordered byte tiles per native frame, without conflicting fixed ownership/palette declarations.");
                FormatVersion=Math.Max(FormatVersion,17);
            }
            if(PlayerTracking.XAddress is <0 or >=2048 || PlayerTracking.YAddress is <0 or >=2048 || PlayerTracking.SpritePalette is <0 or >3)
                throw new InvalidDataException("Player tracking requires valid RAM screen-coordinate addresses and sprite palette 0–3.");
            if(PlayerTracking.XScrollAddress is <0 or >=2048 || PlayerTracking.YScrollAddress is <0 or >=2048)
                throw new InvalidDataException("Player tracking scroll addresses must be valid internal RAM addresses.");
            if(PlayerTracking.VerticalWrap is {} verticalWrap)
            {
                if(PlayerTracking.YScrollAddress is null)throw new InvalidDataException("Vertical nametable wrapping requires a Y scroll address.");
                verticalWrap.Validate();FormatVersion=Math.Max(FormatVersion,16);
            }
            PlayerTracking.MaximumWidth=Math.Clamp(PlayerTracking.MaximumWidth,8,128);
            if(PlayerTracking.PositionTolerance is <0 or >8 || PlayerTracking.PositionTolerance>0 && (PlayerTracking.PaletteOamByState?.Count??0)==0)
                throw new InvalidDataException("Player coordinate tolerance requires an explicit OAM owner and must be0–8 pixels.");
            if(PlayerTracking.PositionTolerance>0)FormatVersion=Math.Max(FormatVersion,13);
            if(PlayerTracking.PaletteStateAddress is int paletteAddress)
            {
                if(paletteAddress is <0 or >=2048||((PlayerTracking.PalettesByState?.Count??0)+(PlayerTracking.PaletteOamByState?.Count??0)==0)||PlayerTracking.PalettesByState?.Any(p=>p.Key is <0 or >255||p.Value is <0 or >3)==true||PlayerTracking.PaletteOamByState?.Any(p=>p.Key is <0 or >255||p.Value is <0 or >63)==true)
                    throw new InvalidDataException("State-dependent player palettes require a valid RAM address, byte states and palettes0–3.");
                if(PlayerTracking.PaletteOamByState?.Keys.Any(key=>PlayerTracking.PalettesByState?.ContainsKey(key)==true)==true)throw new InvalidDataException("A player state cannot specify both a fixed and OAM-owned palette.");
                FormatVersion=Math.Max(FormatVersion,11);
                if(PlayerTracking.PaletteOamByState is not null)FormatVersion=Math.Max(FormatVersion,12);
            }
            else if(PlayerTracking.PalettesByState is not null||PlayerTracking.PaletteOamByState is not null)throw new InvalidDataException("Player palette overrides require a state address.");
            if(PlayerTracking.BodyOamCountByState is {} bodyCounts)
            {
                if(bodyCounts.Count==0||bodyCounts.Any(p=>p.Key is <0 or >255||p.Value is <1 or >16||
                   PlayerTracking.PaletteOamByState?.TryGetValue(p.Key,out int first)!=true||first+p.Value>64))
                    throw new InvalidDataException("Player body ranges require an explicit state/OAM owner and1–16 in-bounds consecutive slots.");
                FormatVersion=Math.Max(FormatVersion,14);
            }
            if(PlayerTracking.BodyExtraOamIndicesByState is {} bodyExtras)
            {
                if(bodyExtras.Count==0||bodyExtras.Any(p=>PlayerTracking.BodyOamCountByState?.TryGetValue(p.Key,out int count)!=true||
                   PlayerTracking.PaletteOamByState?.TryGetValue(p.Key,out int first)!=true||p.Value is null||p.Value.Count==0||count+p.Value.Count>16||
                   p.Value.Distinct().Count()!=p.Value.Count||p.Value.Any(i=>i is <0 or >63||i>=first&&i<first+count)))
                    throw new InvalidDataException("Extra player body slots require a declared base range and distinct nonoverlapping indices withinOAM.");
                FormatVersion=Math.Max(FormatVersion,14);
            }
            PlayerTracking.MaximumHeight=Math.Clamp(PlayerTracking.MaximumHeight,8,128);
            FormatVersion=Math.Max(FormatVersion,PlayerTracking.XScrollAddress is not null || PlayerTracking.YScrollAddress is not null ? 9 : 8);
        }
        CellSize = CellSize == 8 ? 8 : 16;
        MinimumScenePatterns = Math.Clamp(MinimumScenePatterns, 1, 20);
        SceneAnchors = (SceneAnchors ?? []).Where(a=>a.X>=0&&a.X<256&&a.Y>=0&&a.Y<240&&MetatileSignature.IsValidKey(a.Key)&&a.Fingerprint.Length==64).Take(16).ToList();
        FlatRegions = (FlatRegions ?? []).Select(r => Rectangle.Intersect(r, new(0,0,256,240))).Where(r => r.Width > 0 && r.Height > 0).ToList();
        ObjectRegions = (ObjectRegions ?? []).Select(r => Rectangle.Intersect(r, new(0,0,256,240))).Where(r => r.Width > 0 && r.Height > 0).ToList();
        PlayerOamIndices = (PlayerOamIndices ?? []).Where(i => i >= 0 && i < 64).Distinct().ToList();
        GameplayStateAddress = Math.Clamp(GameplayStateAddress, -1, 0x7ff);
        GameplayStateValue = Math.Clamp(GameplayStateValue, 0, 255);
        Name = string.IsNullOrWhiteSpace(Name) ? "Custom game profile" : Name.Trim();
        if (Name.Length > 64) Name = Name[..64];
        RomName = string.IsNullOrWhiteSpace(RomName) ? "Unknown ROM" : Path.GetFileName(RomName.Trim());
        RomSha256 = (RomSha256 ?? string.Empty).Trim().ToUpperInvariant();
        BackgroundRules ??= [];

        Dictionary<string, BackgroundObjectRule> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, BackgroundObjectRule? candidate) in BackgroundRules)
        {
            if (!MetatileSignature.IsValidKey(key))
            {
                continue;
            }
            BackgroundObjectRule rule = candidate ?? new BackgroundObjectRule();
            if (!Enum.TryParse(rule.Kind, ignoreCase: true, out SceneObjectKind kind))
            {
                kind = SceneObjectKind.Terrain;
            }
            rule.Kind = kind.ToString();
            rule.Label = string.IsNullOrWhiteSpace(rule.Label)
                ? ProjectionProfile.DisplayName(kind)
                : rule.Label.Trim();
            if (rule.Label.Length > 48) rule.Label = rule.Label[..48];
            rule.VisualFingerprint = NormalizeFingerprint(rule.VisualFingerprint);
            rule.ArtworkVariants ??= [];
            rule.ArtworkVariants = rule.ArtworkVariants
                .Where(variant => variant is not null && NormalizeFingerprint(variant.Fingerprint).Length == 64)
                .GroupBy(variant => NormalizeFingerprint(variant.Fingerprint), StringComparer.OrdinalIgnoreCase)
                .Select(group => new ArtworkVariant
                {
                    Fingerprint = group.Key,
                    RightHalfFingerprint = group.Select(variant => NormalizeFingerprint(variant.RightHalfFingerprint))
                        .FirstOrDefault(fingerprint => fingerprint.Length == 64) ?? string.Empty,
                    ThumbnailPng = group.Select(variant => variant.ThumbnailPng)
                        .FirstOrDefault(png => !string.IsNullOrEmpty(png)) ?? string.Empty
                }).ToList();
            if (rule.VisualFingerprint.Length == 64 && !rule.ArtworkVariants.Any(variant =>
                variant.Fingerprint.Equals(rule.VisualFingerprint, StringComparison.OrdinalIgnoreCase)))
            {
                rule.ArtworkVariants.Add(new ArtworkVariant { Fingerprint = rule.VisualFingerprint });
            }
            if (rule.ArtworkVariants.Count > 0)
            {
                rule.VisualFingerprint = rule.ArtworkVariants[0].Fingerprint;
            }
            normalized[key.ToUpperInvariant()] = rule;
        }
        BackgroundRules = normalized;
    }

    private static string NormalizeFingerprint(string? fingerprint)
    {
        string normalized = (fingerprint ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
            ? normalized
            : string.Empty;
    }
}

internal sealed class ConditionalFlatRegion
{
    public Rectangle Bounds { get; set; }
    public Dictionary<int, int> ExpectedRam { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<int, int>? ExcludedRam { get; set; }
    public bool Matches(NesFrame frame) => ExpectedRam.Count > 0 && ExpectedRam.All(e =>
        e.Key >= 0 && e.Key < frame.Ram.Length && frame.Ram[e.Key] == e.Value) &&
        (ExcludedRam?.All(e=>e.Key>=0 && e.Key<frame.Ram.Length && frame.Ram[e.Key]!=e.Value) ?? true);
}

internal sealed class SceneAnchor
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Key { get; set; } = "";
    public string Fingerprint { get; set; } = "";
}

internal sealed class BackgroundObjectRule
{
    public string Kind { get; set; } = SceneObjectKind.Terrain.ToString();
    public string Label { get; set; } = "Object";
    public string VisualFingerprint { get; set; } = string.Empty;
    public List<ArtworkVariant> ArtworkVariants { get; set; } = [];

    [JsonIgnore]
    public SceneObjectKind ObjectKind =>
        Enum.TryParse(Kind, ignoreCase: true, out SceneObjectKind kind) ? kind : SceneObjectKind.Terrain;
    [JsonIgnore]
    public bool HasVisualFingerprint => VisualFingerprint.Length == 64 || ArtworkVariants.Count > 0;

    public bool AcceptsArtwork(string fingerprint) =>
        ArtworkVariants.Any(variant => variant.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase)) ||
        VisualFingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase);

    public void CaptureArtwork(string fingerprint, string thumbnailPng = "")
    {
        if (VisualFingerprint.Length == 64 &&
            !ArtworkVariants.Any(variant => variant.Fingerprint.Equals(VisualFingerprint, StringComparison.OrdinalIgnoreCase)))
        {
            ArtworkVariants.Add(new ArtworkVariant { Fingerprint = VisualFingerprint });
        }
        ArtworkVariant? existing = ArtworkVariants.FirstOrDefault(variant =>
            variant.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            ArtworkVariants.Add(new ArtworkVariant { Fingerprint = fingerprint, ThumbnailPng = thumbnailPng });
        }
        else if (!string.IsNullOrEmpty(thumbnailPng))
        {
            existing.ThumbnailPng = thumbnailPng;
        }
        if (VisualFingerprint.Length == 0) VisualFingerprint = fingerprint;
    }

    public BackgroundObjectRule Clone() => new()
    {
        Kind = Kind,
        Label = Label,
        VisualFingerprint = VisualFingerprint,
        ArtworkVariants = (ArtworkVariants ?? []).Where(variant => variant is not null).Select(variant => variant.Clone()).ToList()
    };
}

internal sealed class ArtworkVariant
{
    public string Fingerprint { get; set; } = string.Empty;
    public string RightHalfFingerprint { get; set; } = string.Empty;
    public string ThumbnailPng { get; set; } = string.Empty;
    public ArtworkVariant Clone() => new() { Fingerprint = Fingerprint, RightHalfFingerprint = RightHalfFingerprint, ThumbnailPng = ThumbnailPng };
}

internal static class MetatileVisualFingerprint
{
    public static bool IsVisible(NesFrame frame,int worldX,int worldY,int size,bool allowMaskedLeftEdge=false)
    {
        int x=worldX*8-frame.ScrollX,y=worldY*8-frame.ScrollY;
        Span<byte> artwork=stackalloc byte[256];
        FillWorldCanonical(frame,worldX,worldY,size,artwork);
        if(MatchesScreen(frame,x,y,size,artwork)) return true;
        if (allowMaskedLeftEdge && size == 16 && HasVerifiedMaskedLeftHalf(frame,worldX,worldY)) return true;
        // Native video and the nametable snapshot can be one frame apart.
        // Allow camera motion, not an arbitrary match elsewhere on the screen.
        // One-pixel camera motion is common (notably Castlevania). Checking
        // only even offsets made valid scenery switch off on alternate frames.
        for (int delta = 1; delta <= 8; delta++)
            if (MatchesScreen(frame,x-delta,y,size,artwork) || MatchesScreen(frame,x+delta,y,size,artwork) ||
                MatchesScreen(frame,x,y-delta,size,artwork) || MatchesScreen(frame,x,y+delta,size,artwork)) return true;
        return false;
    }
    // Compare the same first-occurrence color topology directly. Hashing every
    // shifted candidate creates strings/digests even when its second pixel is
    // already different. Palette substitutions and the full +/-8 search remain.
    private static bool MatchesScreen(NesFrame frame,int x,int y,int size,ReadOnlySpan<byte> artwork)
    {
        if(frame.NativeScreenPixels?.Length!=256*240 || x<0 || y<0 || x+size>256 || y+size>240)return false;
        Span<int> colors=stackalloc int[256];
        int count=0;
        for(int dy=0;dy<size;dy++)for(int dx=0;dx<size;dx++)
        {
            int rgb=frame.NativeScreenPixels[(y+dy)*256+x+dx]&0xffffff;
            if(ColorId(rgb,colors,ref count)!=artwork[dy*size+dx])return false;
        }
        return true;
    }
    public static bool HasVerifiedMaskedLeftHalf(NesFrame frame,int worldX,int worldY)
    {
        int x=worldX*8-frame.ScrollX,y=worldY*8-frame.ScrollY;
        if (x != 0 || y < 0 || y+16 > 240 || frame.NativeScreenPixels?.Length != 256*240) return false;
        // New snapshots carry the actual playfield PPUMASK. Older snapshots
        // must instead prove the clipping directly, without changing their bytes.
        if (frame.PpuMask is byte mask && (mask & 0x0a) != 0x08) return false;
        int backdrop=frame.NativeScreenPixels[y*256]&0xffffff;
        bool hiddenPixelsDiffer=false;
        int firstRight=frame.NativeScreenPixels[y*256+8]&0xffffff;
        bool rightHasStructure=false;
        for (int dy=0;dy<16;dy++) for (int dx=0;dx<16;dx++)
        {
            int native=frame.NativeScreenPixels[(y+dy)*256+dx]&0xffffff;
            int metadata=WorldRgb(frame,worldX*8+dx,worldY*8+dy);
            if (dx<8)
            {
                if (native!=backdrop) return false;
                hiddenPixelsDiffer |= native!=metadata;
            }
            else
            {
                if (native!=metadata) return false;
                rightHasStructure |= native!=firstRight;
            }
        }
        // A completely matching ordinary black-edged tile needs no fallback.
        return hiddenPixelsDiffer && rightHasStructure;
    }
    public static string ReadRightHalf(NesFrame frame,int worldX,int worldY)
    {
        Span<byte> canonical=stackalloc byte[128];
        Span<int> colors=stackalloc int[256];
        int colorCount=0;
        for (int y=0;y<16;y++) for (int x=0;x<8;x++)
        {
            int rgb=WorldRgb(frame,worldX*8+8+x,worldY*8+y);
            byte id=ColorId(rgb,colors,ref colorCount);
            canonical[y*8+x]=id;
        }
        return HashCanonical(canonical);
    }
    private static int WorldRgb(NesFrame frame,int worldX,int worldY)
    {
        int x=Mod(worldX,512),y=Mod(worldY,480);
        int table=(x>=256?1:0)+(y>=240?2:0);
        return frame.NametablePixels[table][(y%240)*256+(x%256)]&0xffffff;
    }
    public static string ReadScreen(NesFrame frame,int x,int y,int size)
    {
        if(frame.NativeScreenPixels?.Length!=256*240 || x<0 || y<0 || x+size>256 || y+size>240) return "";
        Span<byte> canonical = stackalloc byte[256];
        Span<int> colors=stackalloc int[256];
        int colorCount=0;
        for(int dy=0;dy<size;dy++) for(int dx=0;dx<size;dx++)
        {
            int rgb=frame.NativeScreenPixels[(y+dy)*256+x+dx]&0xffffff;
            byte id=ColorId(rgb,colors,ref colorCount);
            canonical[dy*size+dx]=id;
        }
        return HashCanonical(canonical[..(size*size)]);
    }
    public static string Read(NesFrame frame, int worldTileX, int worldTileY, int size = 16)
    {
        Span<byte> canonicalPixels = stackalloc byte[16 * 16];
        FillWorldCanonical(frame,worldTileX,worldTileY,size,canonicalPixels);
        return HashCanonical(canonicalPixels[..(size * size)]);
    }

    private static void FillWorldCanonical(NesFrame frame,int worldTileX,int worldTileY,int size,Span<byte> canonicalPixels)
    {
        Span<int> colors=stackalloc int[256];
        int colorCount=0;

        for (int pixelY = 0; pixelY < size; pixelY++)
        for (int pixelX = 0; pixelX < size; pixelX++)
        {
            int worldPixelX = Mod(worldTileX * 8 + pixelX, 512);
            int worldPixelY = Mod(worldTileY * 8 + pixelY, 480);
            int table = (worldPixelX >= 256 ? 1 : 0) + (worldPixelY >= 240 ? 2 : 0);
            int localX = worldPixelX & 0xFF;
            int localY = worldPixelY % 240;
            int rgb = frame.NametablePixels[table][localY * 256 + localX] & 0x00FFFFFF;
            byte colorId=ColorId(rgb,colors,ref colorCount);
            canonicalPixels[pixelY * size + pixelX] = colorId;
        }

    }

    // NES cells normally contain four palette colors. First-occurrence IDs
    // remain identical to the dictionary implementation, including arbitrary
    // imported captures with up to 256 distinct RGB values. No cross-frame
    // cache: changing artwork, palettes and banks are always read afresh.
    private static byte ColorId(int rgb,Span<int> colors,ref int count)
    {
        for(int i=0;i<count;i++)if(colors[i]==rgb)return (byte)i;
        colors[count]=rgb;
        return (byte)count++;
    }

    private static string HashCanonical(ReadOnlySpan<byte> canonical)
    {
        Span<byte> digest=stackalloc byte[32];
        SHA256.HashData(canonical,digest);
        return Convert.ToHexString(digest);
    }

    private static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;
}

internal readonly record struct MetatileSignature(
    byte Palette,
    byte TopLeft,
    byte BottomLeft,
    byte TopRight,
    byte BottomRight)
{
    public string Key => $"P{Palette:X1}-{TopLeft:X2}{BottomLeft:X2}{TopRight:X2}{BottomRight:X2}";
    public string Description =>
        $"Palette {Palette} · tiles {TopLeft:X2} {TopRight:X2} / {BottomLeft:X2} {BottomRight:X2}";

    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length != 11 ||
            char.ToUpperInvariant(key[0]) != 'P' || key[2] != '-')
        {
            return false;
        }
        return int.TryParse(key.AsSpan(1, 1), System.Globalization.NumberStyles.HexNumber, null, out _) &&
               uint.TryParse(key.AsSpan(3, 8), System.Globalization.NumberStyles.HexNumber, null, out _);
    }

    public static MetatileSignature Read(NesFrame frame, int worldTileX, int worldTileY, int size = 16)
    {
        (byte topLeft, byte palette) = ReadTile(frame, worldTileX, worldTileY);
        if (size == 8) return new(palette, topLeft, topLeft, topLeft, topLeft);
        (byte bottomLeft, _) = ReadTile(frame, worldTileX, worldTileY + 1);
        (byte topRight, _) = ReadTile(frame, worldTileX + 1, worldTileY);
        (byte bottomRight, _) = ReadTile(frame, worldTileX + 1, worldTileY + 1);
        return new MetatileSignature(palette, topLeft, bottomLeft, topRight, bottomRight);
    }

    private static (byte Tile, byte Palette) ReadTile(NesFrame frame, int worldTileX, int worldTileY)
    {
        int wrappedX = Mod(worldTileX, 64);
        int wrappedY = Mod(worldTileY, 60);
        int table = (wrappedX >= 32 ? 1 : 0) + (wrappedY >= 30 ? 2 : 0);
        int tileIndex = (wrappedY % 30) * 32 + wrappedX % 32;
        byte attribute = frame.Attributes[table][tileIndex];
        int shift = ((wrappedY & 0x02) << 1) | (wrappedX & 0x02);
        return (frame.Tiles[table][tileIndex], (byte)((attribute >> shift) & 0x03));
    }

    private static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;
}

internal static class GameRecognitionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string DirectoryPath => Path.Combine(AppPaths.DataDirectory, "game-profiles");

    public static GameRecognitionProfile? LoadForRom(string romSha256)
    {
        try
        {
            string path = PathForRom(romSha256);
            return File.Exists(path) ? ReadFromFile(path) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(GameRecognitionProfile profile)
    {
        profile.Normalize();
        if (string.IsNullOrWhiteSpace(profile.RomSha256))
        {
            throw new InvalidDataException("The game profile is not bound to a ROM hash.");
        }
        WriteToFile(PathForRom(profile.RomSha256), profile);
    }

    public static GameRecognitionProfile ReadFromFile(string path)
    {
        string json = File.ReadAllText(Path.GetFullPath(path));
        GameRecognitionProfile profile = JsonSerializer.Deserialize<GameRecognitionProfile>(json, JsonOptions)
            ?? throw new InvalidDataException("The selected file does not contain a Warp4D game profile.");
        profile.Normalize();
        return profile;
    }

    public static void WriteToFile(string path, GameRecognitionProfile profile)
    {
        GameRecognitionProfile normalized = profile.Clone();
        string absolutePath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(absolutePath, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    private static string PathForRom(string romSha256)
    {
        string safeHash = new((romSha256 ?? string.Empty)
            .Where(character => Uri.IsHexDigit(character))
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (safeHash.Length != 64)
        {
            throw new InvalidDataException("The ROM SHA-256 is invalid.");
        }
        return Path.Combine(DirectoryPath, safeHash + ".warp4d-game.json");
    }
}
