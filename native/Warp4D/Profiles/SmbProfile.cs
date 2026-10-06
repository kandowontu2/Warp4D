using System.Drawing;
using System.Drawing.Imaging;
using Warp4D.Emulation;
using Warp4D.Rendering;

namespace Warp4D.Profiles;

internal sealed class SmbProfile
{
    internal bool UseBulkSpriteDecodingForTest {get;set;}=true;
    internal bool UseSpriteArtworkPreflightForTest {get;set;}=false;
    internal bool UseDirectBackdropCountingForTest {get;set;}=false;
    internal bool UseRunBackdropCountingForTest {get;set;}=false;
    internal bool UseBackdropCacheForTest {get;set;}=true;
    private BackdropColorCache? _backdropCache;
    internal object BackdropCacheStatsForTest=>new{Hits=_backdropCache?.Hits??0,Misses=_backdropCache?.Misses??0,StoredPixels=_backdropCache?.StoredPixels??0};
    internal bool UseFusedBackdropVisibilityForTest {get;set;}=true;
    internal bool UseCombinedArtworkExtractionForTest {get;set;}=true;
    internal Action<ProfileBuildTiming>? BuildObserverForTest {get;set;}
    internal Action<BackgroundBuildTiming>? BackgroundObserverForTest {get;set;}
    private const int OperModeAddress = 0x0770;
    private const int OperModeTaskAddress = 0x0772;
    private const int DemoTimerAddress = 0x07A2;
    private static readonly ProjectionProfile DefaultProjectionProfile = ProjectionProfile.CreateDefault();
    private static readonly HashSet<int> BushMetatiles =
    [
        Pack(0x24, 0x24, 0x24, 0x35), Pack(0x36, 0x25, 0x37, 0x25), Pack(0x24, 0x38, 0x24, 0x24)
    ];
    private static readonly HashSet<int> HillMetatiles =
    [
        Pack(0x24, 0x30, 0x30, 0x26), Pack(0x26, 0x26, 0x34, 0x26),
        Pack(0x24, 0x31, 0x24, 0x32), Pack(0x33, 0x26, 0x24, 0x33),
        Pack(0x34, 0x26, 0x26, 0x26), Pack(0x26, 0x26, 0x26, 0x26)
    ];
    private static readonly HashSet<int> TreeMetatiles =
    [
        Pack(0xB8, 0xBA, 0xB9, 0xBB), Pack(0xB8, 0xBC, 0xB9, 0xBD),
        Pack(0xBA, 0xBC, 0xBB, 0xBD), Pack(0x4B, 0x4C, 0x4D, 0x4E),
        Pack(0x4D, 0x4F, 0x4D, 0x4F), Pack(0x4D, 0x4E, 0x50, 0x51)
    ];
    private static readonly HashSet<int> PipeMetatiles =
    [
        Pack(0x60, 0x64, 0x61, 0x65), Pack(0x62, 0x66, 0x63, 0x67),
        Pack(0x68, 0x68, 0x69, 0x69), Pack(0x26, 0x26, 0x6A, 0x6A),
        Pack(0x86, 0x8A, 0x87, 0x8B), Pack(0x88, 0x8C, 0x88, 0x8C),
        Pack(0x89, 0x8D, 0x69, 0x69), Pack(0x8E, 0x91, 0x8F, 0x92),
        Pack(0x26, 0x93, 0x26, 0x93), Pack(0x90, 0x94, 0x69, 0x69)
    ];
    private static readonly HashSet<int> CloudMetatiles =
    [
        Pack(0x24, 0x24, 0x24, 0x35), Pack(0x36, 0x25, 0x37, 0x25),
        Pack(0x24, 0x38, 0x24, 0x24), Pack(0x24, 0x24, 0x39, 0x24),
        Pack(0x3A, 0x24, 0x3B, 0x24), Pack(0x3C, 0x24, 0x24, 0x24)
    ];
    private static readonly HashSet<int> BrickMetatiles =
    [
        Pack(0x45, 0x47, 0x45, 0x47), Pack(0x47, 0x47, 0x47, 0x47)
    ];
    private static readonly HashSet<int> GroundMetatiles =
    [
        Pack(0xB4, 0xB6, 0xB5, 0xB7), Pack(0xB0, 0xB1, 0xB2, 0xB3)
    ];
    private static readonly HashSet<int> SolidMetatiles =
    [
        Pack(0xAB, 0xAC, 0xAD, 0xAE), Pack(0x5D, 0x5E, 0x5D, 0x5E),
        Pack(0x82, 0x83, 0x84, 0x85)
    ];
    private static readonly HashSet<int> CastleMetatiles =
    [
        Pack(0x9D, 0x47, 0x9E, 0x47), Pack(0x47, 0x47, 0x27, 0x27),
        Pack(0x47, 0x47, 0x47, 0x47), Pack(0x27, 0x27, 0x47, 0x47),
        Pack(0xA9, 0x47, 0xAA, 0x47), Pack(0x9B, 0x27, 0x9C, 0x27),
        Pack(0x27, 0x27, 0x27, 0x27)
    ];
    private static readonly HashSet<int> FlagpoleMetatiles =
    [
        Pack(0x24, 0x2F, 0x24, 0x3D), Pack(0xA2, 0xA2, 0xA3, 0xA3)
    ];
    private static readonly HashSet<int> QuestionMetatiles =
    [
        Pack(0x53, 0x55, 0x54, 0x56), Pack(0x57, 0x59, 0x58, 0x5A)
    ];

    // Remaining visible entries in the verified SMB metatile catalog. Keep
    // blank tiles and solid water/lava backdrop flat; classify actual objects.
    // Existing enum kinds preserve user class settings and profile formats.
    private static readonly Dictionary<(byte Palette,int Tiles),BackgroundObjectIdentity> SupplementalMetatiles = new()
    {
        [(0,Pack(0x24,0xc0,0x24,0xc0))]=new(SceneObjectKind.Sprite,"Bridge rail",true),
        [(0,Pack(0x24,0x7f,0x7f,0x24))]=new(SceneObjectKind.Sprite,"Chain",true),
        [(0,Pack(0x6b,0x70,0x2c,0x2d))]=new(SceneObjectKind.Tree,"Mushroom platform"),
        [(0,Pack(0x6c,0x71,0x6d,0x72))]=new(SceneObjectKind.Tree,"Mushroom platform"),
        [(0,Pack(0x6e,0x73,0x6f,0x74))]=new(SceneObjectKind.Tree,"Mushroom platform"),
        [(0,Pack(0xa4,0xe9,0xea,0xeb))]=new(SceneObjectKind.Bush,"Sea plant"),
        [(1,Pack(0xa2,0xa2,0xa3,0xa3))]=new(SceneObjectKind.Sprite,"Rope",true),
        [(1,Pack(0x99,0x24,0x99,0x24))]=new(SceneObjectKind.Sprite,"Rope",true),
        [(1,Pack(0x24,0xa2,0x3e,0x3f))]=new(SceneObjectKind.Sprite,"Pulley",true),
        [(1,Pack(0x5b,0x5c,0x24,0xa3))]=new(SceneObjectKind.Sprite,"Pulley",true),
        [(1,Pack(0x52,0x52,0x52,0x52))]=new(SceneObjectKind.Tree,"Tree"),
        [(1,Pack(0x80,0xa0,0x81,0xa1))]=new(SceneObjectKind.Tree,"Fence"),
        [(1,Pack(0xbe,0xbe,0xbf,0xbf))]=new(SceneObjectKind.Tree,"Tree"),
        [(1,Pack(0x75,0xba,0x76,0xbb))]=new(SceneObjectKind.Tree,"Mushroom platform"),
        [(1,Pack(0xba,0xba,0xbb,0xbb))]=new(SceneObjectKind.Tree,"Mushroom platform"),
        [(1,Pack(0xc1,0x24,0xc1,0x24))]=new(SceneObjectKind.Terrain,"Bridge",true),
        [(1,Pack(0xc6,0xc8,0xc7,0xc9))]=new(SceneObjectKind.Pipe,"Bullet Bill cannon"),
        [(1,Pack(0xca,0xcc,0xcb,0xcd))]=new(SceneObjectKind.Pipe,"Bullet Bill cannon"),
        [(1,Pack(0x2a,0x2a,0x40,0x40))]=new(SceneObjectKind.Pipe,"Bullet Bill cannon"),
        [(1,Pack(0x24,0x47,0x24,0x47))]=new(SceneObjectKind.Brick,"Half brick",true),
        [(1,Pack(0x86,0x8a,0x87,0x8b))]=new(SceneObjectKind.Pipe,"Pipe"),
        [(1,Pack(0x8e,0x91,0x8f,0x92))]=new(SceneObjectKind.Pipe,"Pipe"),
        [(1,Pack(0x24,0x2f,0x24,0x3d))]=new(SceneObjectKind.Flagpole,"Flagpole"),
        [(2,Pack(0x41,0x26,0x41,0x26))]=new(SceneObjectKind.Terrain,"Water / lava surface",true),
        [(2,Pack(0x77,0x79,0x77,0x79))]=new(SceneObjectKind.Terrain,"Castle bridge"),
        [(3,Pack(0xa5,0xa7,0xa6,0xa8))]=new(SceneObjectKind.Item,"Coin",true),
        [(3,Pack(0xc2,0xc4,0xc3,0xc5))]=new(SceneObjectKind.Item,"Underwater coin",true),
        [(3,Pack(0x7b,0x7d,0x7c,0x7e))]=new(SceneObjectKind.Item,"Axe",true)
    };

    private static readonly Dictionary<SceneObjectKind, Color> Accents = new()
    {
        [SceneObjectKind.Bush] = Color.FromArgb(128, 255, 100),
        [SceneObjectKind.Cloud] = Color.FromArgb(210, 235, 255),
        [SceneObjectKind.Hill] = Color.FromArgb(88, 226, 121),
        [SceneObjectKind.Tree] = Color.FromArgb(81, 213, 128),
        [SceneObjectKind.Pipe] = Color.FromArgb(90, 255, 118),
        [SceneObjectKind.QuestionBlock] = Color.FromArgb(255, 208, 56),
        [SceneObjectKind.Brick] = Color.FromArgb(255, 118, 52),
        [SceneObjectKind.Terrain] = Color.FromArgb(214, 98, 45),
        [SceneObjectKind.Castle] = Color.FromArgb(220, 140, 88),
        [SceneObjectKind.Flagpole] = Color.FromArgb(240, 244, 207),
        [SceneObjectKind.Player] = Color.FromArgb(255, 245, 127),
        [SceneObjectKind.Enemy] = Color.FromArgb(255, 91, 97),
        [SceneObjectKind.Item] = Color.FromArgb(105, 226, 255),
        [SceneObjectKind.Sprite] = Color.FromArgb(198, 147, 255)
    };

    private static readonly Dictionary<int, string> EnemyNames = new()
    {
        [0x00] = "Green Koopa", [0x02] = "Buzzy Beetle", [0x03] = "Red Koopa",
        [0x05] = "Hammer Bro", [0x06] = "Goomba", [0x07] = "Bloober",
        [0x08] = "Bullet Bill", [0x0A] = "Cheep-Cheep", [0x0B] = "Red Cheep-Cheep",
        [0x0C] = "Podoboo", [0x0D] = "Piranha Plant", [0x0E] = "Paratroopa",
        [0x0F] = "Red Paratroopa", [0x10] = "Flying Paratroopa", [0x11] = "Lakitu",
        [0x12] = "Spiny", [0x14] = "Flying Cheep-Cheep", [0x15] = "Bowser Flame",
        [0x16] = "Firework", [0x17] = "Bullet/Cheep Frenzy", [0x2D] = "Bowser",
        [0x2E] = "Power-up", [0x2F] = "Vine", [0x30] = "Flag",
        [0x31] = "Star Flag", [0x32] = "Spring"
    };

    public SmbScene Build(
        NesFrame frame,
        bool exactProfile,
        ProjectionProfile? projectionProfile = null,
        GameRecognitionProfile? gameProfile = null)
    {
        projectionProfile ??= DefaultProjectionProfile;
        var observer=BuildObserverForTest;
        long timingStart=observer is null?0:System.Diagnostics.Stopwatch.GetTimestamp();
        bool active = (gameProfile?.IsActive(frame) ?? true) &&
            (frame.PpuMask is not byte mask || (mask&0x18)!=0) && !frame.IsUniformPairedFade();
        Bitmap background = ComposeBackground(frame, exactProfile, gameProfile, !active);
        double composeMs=Elapsed();
        List<SceneObject> objects = [];

        bool normalGameplay = ReadRam(frame, OperModeAddress) == 1;
        bool attractDemo = exactProfile && IsAttractDemo(frame);
        bool hasCustomBackgroundRules = gameProfile?.BackgroundRules.Count > 0;
        bool backgroundVisible = frame.PpuMask is not byte backgroundMask || (backgroundMask & 0x08) != 0;
        bool spritesVisible = frame.PpuMask is not byte spriteMask || (spriteMask & 0x10) != 0;
        if (active && backgroundVisible && ((exactProfile && (normalGameplay || attractDemo)) || (!exactProfile && hasCustomBackgroundRules)))
        {
            objects.AddRange(ExtractBackgroundObjects(
                background,
                frame,
                projectionProfile,
                exactProfile,
                gameProfile,BackgroundObserverForTest,UseDirectBackdropCountingForTest,UseFusedBackdropVisibilityForTest,UseCombinedArtworkExtractionForTest,UseRunBackdropCountingForTest,UseBackdropCacheForTest?(_backdropCache??=new()):null));
        }
        double backgroundMs=Elapsed();
        if (active && spritesVisible) objects.AddRange(ExtractSprites(frame, exactProfile, projectionProfile, gameProfile,UseBulkSpriteDecodingForTest,UseSpriteArtworkPreflightForTest));
        double spritesMs=Elapsed();
        bool playerOverlaysHud=active && (exactProfile
            ? objects.Any(o=>o.Kind==SceneObjectKind.Player && o.ProjectionEnabled && o.Bounds.Top<32)
            : gameProfile?.PlayerOverlaysFlatHud==true);
        // Exact SMB already builds a raw nametable HUD plus separate flat
        // OAM objects, so its player is not duplicated in a native HUD copy.
        if(playerOverlaysHud && !exactProfile && frame.NativeScreenPixels is {Length:61440} native)
        {
            // Remove the duplicate native body from the copied HUD layer,
            // never whole actor rectangles or unlike-colored HUD glyphs.
            // Restore the same raw nametable background used elsewhere.
            foreach(var actor in objects.Where(o=>o.Kind==SceneObjectKind.Player && o.ProjectionEnabled))
            for(int py=0;py<actor.Image.Height;py++)for(int px=0;px<actor.Image.Width;px++)
            {
                int x=actor.Bounds.X+px,y=actor.Bounds.Y+py;
                if(x<0||x>=256||y<0||y>=240||!gameProfile!.Protects(new(x,y,1,1)))continue;
                Color pixel=actor.Image.GetPixel(px,py);
                if(pixel.A==0 || (pixel.ToArgb()&0xffffff)!=(native[y*256+x]&0xffffff))continue;
                int wx=(x+frame.ScrollX)&511,wy=Mod(y+frame.ScrollY,480);
                int table=(wy>=240?2:0)+(wx>=256?1:0);
                background.SetPixel(x,y,Color.FromArgb(frame.NametablePixels[table][(wy%240)*256+(wx&255)]|unchecked((int)0xff000000)));
            }
        }

        int world = ReadRam(frame, 0x075F) + 1;
        int level = ReadRam(frame, 0x075C) + 1;
        string location = exactProfile
            ? $"SMB  {world}-{level}{(attractDemo ? "  ·  ATTRACT DEMO" : string.Empty)}"
            : gameProfile?.Name ?? "Generic NES";
        string recognitionProfileName = exactProfile
            ? gameProfile is null ? "SMB BUILT-IN" : $"SMB + {gameProfile.Name}"
            : gameProfile?.Name ?? "GENERIC SPRITES";

        SmbScene scene=new()
        {
            Background = background,
            Objects = objects,
            Location = location,
            ExactProfile = exactProfile,
            FlatRegions = gameProfile?.FlatRegionsFor(frame).ToArray() ?? [],
            RecognitionProfileName = recognitionProfileName,
            ProjectionProfileName = projectionProfile.Name,
            Sequence = frame.Sequence,
            PlayerOverlaysFlatHud = playerOverlaysHud
        };
        observer?.Invoke(new(frame.Sequence,composeMs,backgroundMs,spritesMs,Elapsed()));
        return scene;
        double Elapsed()
        {
            if(observer is null)return 0;
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            double ms=(now-timingStart)*1000d/System.Diagnostics.Stopwatch.Frequency;
            timingStart=now;return ms;
        }
    }

    internal static bool IsAttractDemo(NesFrame frame) =>
        ReadRam(frame, OperModeAddress) == 0 &&
        ReadRam(frame, OperModeTaskAddress) == 3 &&
        ReadRam(frame, DemoTimerAddress) == 0;

    internal static Bitmap ComposeBackground(NesFrame frame, bool exactProfile, GameRecognitionProfile? profile = null, bool flatScreen = false)
    {
        Bitmap bitmap = new(NesFrame.ScreenWidth, NesFrame.ScreenHeight, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        unsafe
        {
            int* target = (int*)data.Scan0;
            for (int y = 0; y < NesFrame.ScreenHeight; y++)
            {
                bool smbStatusBar = exactProfile && y < 32;
                int worldY = smbStatusBar ? y : Mod(y + frame.ScrollY, 480);
                int tableY = worldY >= 240 ? 2 : 0;
                int localY = worldY % 240;
                for (int x = 0; x < NesFrame.ScreenWidth; x++)
                {
                    int worldX = smbStatusBar ? x : (x + frame.ScrollX) & 511;
                    int table = tableY + (worldX >= 256 ? 1 : 0);
                    int localX = worldX & 0xFF;
                    int color = frame.NametablePixels[table][localY * 256 + localX];
                    target[y * (data.Stride / 4) + x] = color | unchecked((int)0xFF000000);
                }
            }
            if(frame.NativeScreenPixels is {Length:256*240} native)
            {
                // Copy protected rectangles once, not a LINQ region scan and
                // closure allocation for every one of the 61,440 pixels.
                IReadOnlyList<Rectangle> flat=flatScreen ? [new(0,0,256,240)] : profile?.FlatRegionsFor(frame) ?? [];
                foreach(Rectangle region in flat)
                {
                    Rectangle clipped=Rectangle.Intersect(region,new(0,0,256,240));
                    for(int y=clipped.Top;y<clipped.Bottom;y++)for(int x=clipped.Left;x<clipped.Right;x++)
                        target[y*(data.Stride/4)+x]=native[y*256+x]|unchecked((int)0xFF000000);
                }
            }
        }

        bitmap.UnlockBits(data);
        return bitmap;
    }

    private static IEnumerable<SceneObject> ExtractBackgroundObjects(
        Bitmap background,
        NesFrame frame,
        ProjectionProfile projectionProfile,
        bool exactProfile,
        GameRecognitionProfile? gameProfile,Action<BackgroundBuildTiming>? observer=null,bool directBackdrop=false,bool fusedVisibility=true,bool combinedArtwork=true,bool runBackdrop=false,BackdropColorCache? backdropCache=null)
    {
        long start=observer is null?0:System.Diagnostics.Stopwatch.GetTimestamp();
        Dictionary<(int X, int Y), BackgroundObjectIdentity> classified = [];
        int backdropRgb = backdropCache?.Find(background)??FindDominantRgb(background,directBackdrop,runBackdrop);
        double backdropMs=Elapsed(),groupingMs=0,artworkMs=0;
        double metadataMs=0,cropMs=0,transparencyMs=0,eraseMs=0;
        int groupCount=0,objectCount=0;

        int startTileX = (frame.ScrollX / 8) - 1;
        int startTileY = (frame.ScrollY / 8) - 1;
        int endTileX = ((frame.ScrollX + 255) / 8) + 1;
        int endTileY = ((frame.ScrollY + 239) / 8) + 1;

        int step = (gameProfile?.CellSize ?? 16) / 8;
        int firstMetatileX = startTileX / step * step - step;
        int firstMetatileY = startTileY / step * step - step;
        for (int worldTileY = firstMetatileY; worldTileY <= endTileY; worldTileY += step)
        {
            // Never classify/erase status text as world scenery, including when
            // a streamed nametable happens to contain matching tiles above it.
            if (exactProfile && worldTileY * 8 - frame.ScrollY < 32) continue;
            for (int worldTileX = firstMetatileX; worldTileX <= endTileX; worldTileX += step)
            {
                Rectangle visibleCell = Rectangle.Intersect(new(worldTileX*8-frame.ScrollX,worldTileY*8-frame.ScrollY,step*8,step*8),new(0,0,256,240));
                if (visibleCell.Width<=0 || visibleCell.Height<=0) continue;
                if (gameProfile is not null && !gameProfile.Allows(new(worldTileX*8-frame.ScrollX, worldTileY*8-frame.ScrollY, step*8, step*8), frame)) continue;
                MetatileSignature signature = MetatileSignature.Read(frame, worldTileX, worldTileY, step*8);
                BackgroundObjectIdentity? identity = ClassifyMetatile(
                    signature,
                    exactProfile,
                    gameProfile,
                    frame,
                    worldTileX,
                    worldTileY);
                if (identity is not null)
                {
                    for (int dy=0;dy<step;dy++) for (int dx=0;dx<step;dx++) classified[(worldTileX+dx,worldTileY+dy)] = identity.Value;
                }
            }
        }

        if (exactProfile) ResolveCastleTiles(classified, frame, gameProfile);
        double classificationMs=Elapsed();
        using var groups=GroupTiles(classified, exactProfile || gameProfile?.SeparateMetatiles == true, step).GetEnumerator();
        while(true)
        {
            if(observer is not null)start=System.Diagnostics.Stopwatch.GetTimestamp();
            bool more=groups.MoveNext();groupingMs+=Elapsed();
            if(!more)break;
            List<(int X,int Y)> group=groups.Current;groupCount++;
            BackgroundObjectIdentity identity = classified[group[0]];
            SceneObjectKind kind = identity.Kind;
            int left = group.Min(point => point.X * 8 - frame.ScrollX);
            int top = group.Min(point => point.Y * 8 - frame.ScrollY);
            int right = group.Max(point => point.X * 8 - frame.ScrollX + 8);
            int bottom = group.Max(point => point.Y * 8 - frame.ScrollY + 8);
            Rectangle bounds = Rectangle.Intersect(
                new Rectangle(left, top, right - left, bottom - top),
                new Rectangle(0, 0, 256, 240));

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                metadataMs+=ArtworkElapsed();
                continue;
            }

            metadataMs+=ArtworkElapsed();
            bool transparent=gameProfile?.TransparentBackdrop == true || identity.TransparentBackdrop || kind is SceneObjectKind.Bush or SceneObjectKind.Cloud or SceneObjectKind.Hill or
                SceneObjectKind.Tree or SceneObjectKind.Pipe or SceneObjectKind.Flagpole;
            Bitmap crop;
            if(combinedArtwork)
            {
                crop=ExtractObjectArtwork(background,bounds,group,frame.ScrollX,frame.ScrollY,backdropRgb,transparent,out bool visible);
                cropMs+=ArtworkElapsed();
                if(transparent&&!visible){crop.Dispose();transparencyMs+=ArtworkElapsed();continue;}
            }
            else
            {
                crop = CropWithTileMask(background, bounds, group, frame.ScrollX, frame.ScrollY);
                cropMs+=ArtworkElapsed();
                if(transparent)
                {
                    bool visible=MakeBackdropTransparent(crop, backdropRgb,fusedVisibility);
                    // Empty artwork must not acquire a projection outline.
                    if (!(fusedVisibility?visible:HasVisiblePixel(crop))) { crop.Dispose(); transparencyMs+=ArtworkElapsed(); continue; }
                }
                transparencyMs+=ArtworkElapsed();
                EraseObjectFromBackground(background, crop, bounds, backdropRgb);
                eraseMs+=ArtworkElapsed();
            }
            ObjectProjectionRule rule = projectionProfile.RuleFor(kind);
            SceneObject item=new()
            {
                Kind = kind,
                Label = identity.Label,
                IdentityKey = $"background:{kind}:{group.Min(point => point.X)}:{group.Min(point => point.Y)}",
                Bounds = bounds,
                Image = crop,
                Accent = Accents[kind],
                Depth = DepthFor(kind, bounds) * rule.DepthScale,
                ProjectionEnabled = rule.Enabled,
                SortOrder = kind is SceneObjectKind.Terrain ? -5 : 0
            };
            objectCount++;metadataMs+=ArtworkElapsed();yield return item;
        }
        observer?.Invoke(new(frame.Sequence,backdropMs,classificationMs,groupingMs,artworkMs,groupCount,objectCount,metadataMs,cropMs,transparencyMs,eraseMs,combinedArtwork));
        double ArtworkElapsed(){double ms=Elapsed();artworkMs+=ms;return ms;}
        double Elapsed()
        {
            if(observer is null)return 0;
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            double ms=(now-start)*1000d/System.Diagnostics.Stopwatch.Frequency;start=now;return ms;
        }
    }

    private static BackgroundObjectIdentity? ClassifyMetatile(
        MetatileSignature signature,
        bool exactProfile,
        GameRecognitionProfile? gameProfile,
        NesFrame frame,
        int worldTileX,
        int worldTileY)
    {
        BackgroundObjectRule? customRule = gameProfile?.Match(signature, frame, worldTileX, worldTileY);
        if (customRule is not null)
        {
            return new BackgroundObjectIdentity(customRule.ObjectKind, customRule.Label);
        }

        if (!exactProfile)
        {
            return null;
        }

        int metatile = Pack(signature.TopLeft, signature.BottomLeft, signature.TopRight, signature.BottomRight);
        byte palette = signature.Palette;
        if(SupplementalMetatiles.TryGetValue((palette,metatile),out var supplemental))return supplemental;
        SceneObjectKind? kind = null;
        if (palette == 0 && BushMetatiles.Contains(metatile)) kind = SceneObjectKind.Bush;
        else if (palette == 0 && HillMetatiles.Contains(metatile)) kind = SceneObjectKind.Hill;
        else if (palette == 0 && TreeMetatiles.Contains(metatile)) kind = SceneObjectKind.Tree;
        else if (palette == 0 && PipeMetatiles.Contains(metatile)) kind = SceneObjectKind.Pipe;
        else if (palette == 0 && FlagpoleMetatiles.Contains(metatile)) kind = SceneObjectKind.Flagpole;
        else if (palette == 2 && CloudMetatiles.Contains(metatile)) kind = SceneObjectKind.Cloud;
        else if (palette == 1 && CastleMetatiles.Contains(metatile)) kind = SceneObjectKind.Castle;
        else if (palette == 1 && BrickMetatiles.Contains(metatile)) kind = SceneObjectKind.Brick;
        else if (palette is 1 or 2 && GroundMetatiles.Contains(metatile)) kind = SceneObjectKind.Terrain;
        else if (palette == 1 && SolidMetatiles.Contains(metatile)) return new(SceneObjectKind.Brick, "Solid block");
        else if (palette == 3 && QuestionMetatiles.Contains(metatile)) kind = SceneObjectKind.QuestionBlock;
        return kind is null ? null : new BackgroundObjectIdentity(kind.Value, KindLabel(kind.Value));
    }

    private static IEnumerable<List<(int X, int Y)>> GroupTiles(
        Dictionary<(int X, int Y), BackgroundObjectIdentity> classified, bool separateBlocks = false, int cellStep = 2)
    {
        HashSet<(int X, int Y)> remaining = [.. classified.Keys];
        while (remaining.Count > 0)
        {
            (int X, int Y) seed = remaining.First();
            BackgroundObjectIdentity identity = classified[seed];
            Queue<(int X, int Y)> queue = new();
            List<(int X, int Y)> group = [];
            queue.Enqueue(seed);
            remaining.Remove(seed);

            while (queue.Count > 0)
            {
                (int X, int Y) point = queue.Dequeue();
                group.Add(point);
                foreach ((int X, int Y) neighbor in new[]
                {
                    (point.X - 1, point.Y), (point.X + 1, point.Y),
                    (point.X, point.Y - 1), (point.X, point.Y + 1)
                })
                {
                bool individualBlock = separateBlocks && (identity.Kind is SceneObjectKind.Brick or SceneObjectKind.Terrain or SceneObjectKind.QuestionBlock or SceneObjectKind.Enemy or SceneObjectKind.Item);
                    bool sameBlock = !individualBlock || neighbor.X / cellStep == seed.X / cellStep && neighbor.Y / cellStep == seed.Y / cellStep;
                    if (sameBlock && remaining.Contains(neighbor) && classified[neighbor] == identity)
                    {
                        remaining.Remove(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            yield return group;
        }
    }

    private static void ResolveCastleTiles(Dictionary<(int X, int Y), BackgroundObjectIdentity> classified, NesFrame frame, GameRecognitionProfile? custom)
    {
        // Castle masonry shares patterns with ordinary bricks. Only a connected
        // component containing a distinctive castle cap/door is a whole castle.
        foreach (var group in GroupTiles(classified).Where(g => classified[g[0]].Kind == SceneObjectKind.Castle).ToArray())
        {
            bool anchored = group.Any(p =>
            {
                var s = MetatileSignature.Read(frame, p.X & ~1, p.Y & ~1);
                if (custom?.Match(s, frame, p.X & ~1, p.Y & ~1) is not null) return true;
                return s.TopLeft is 0x9d or 0xa9 or 0x9b;
            });
            if (anchored) continue;
            foreach (var p in group)
            {
                var s = MetatileSignature.Read(frame, p.X & ~1, p.Y & ~1);
                int pattern = Pack(s.TopLeft, s.BottomLeft, s.TopRight, s.BottomRight);
                if (BrickMetatiles.Contains(pattern)) classified[p] = new(SceneObjectKind.Brick, "Brick");
                else classified.Remove(p);
            }
        }
    }

    internal static unsafe Bitmap CropWithTileMask(
        Bitmap source,
        Rectangle bounds,
        List<(int X, int Y)> tiles,
        int scrollX,
        int scrollY)
    {
        Bitmap crop = new(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (Graphics clear = Graphics.FromImage(crop))
        {
            clear.Clear(Color.Transparent);
        }

        BitmapData sourceData = source.LockBits(
            new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        BitmapData targetData = crop.LockBits(
            new Rectangle(0, 0, crop.Width, crop.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            foreach ((int tileX, int tileY) in tiles)
            {
                Rectangle sourceCell = Rectangle.Intersect(
                    new Rectangle(tileX * 8 - scrollX, tileY * 8 - scrollY, 8, 8),
                    bounds);
                if (sourceCell.Width <= 0 || sourceCell.Height <= 0)
                {
                    continue;
                }
                for (int row = 0; row < sourceCell.Height; row++)
                {
                    int* sourcePixels = (int*)((byte*)sourceData.Scan0 + (sourceCell.Y + row) * sourceData.Stride) + sourceCell.X;
                    int* targetPixels = (int*)((byte*)targetData.Scan0 + (sourceCell.Y - bounds.Y + row) * targetData.Stride) + sourceCell.X - bounds.X;
                    Buffer.MemoryCopy(sourcePixels, targetPixels, sourceCell.Width * sizeof(int), sourceCell.Width * sizeof(int));
                }
            }
        }
        finally
        {
            source.UnlockBits(sourceData);
            crop.UnlockBits(targetData);
        }
        return crop;
    }

    internal static unsafe bool MakeBackdropTransparent(Bitmap bitmap, int backdropRgb,bool detectVisible=false)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        bool visible=false;
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                int* row = (int*)((byte*)data.Scan0 + y * data.Stride);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if ((row[x] & 0xffffff) == backdropRgb) row[x] = 0;
                    else if(detectVisible && (row[x]&unchecked((int)0xff000000))!=0)visible=true;
                }
            }
        }
        finally { bitmap.UnlockBits(data); }
        return visible;
    }

    // GroupTiles supplies unique, disjoint 8x8 cells. Erasing a copied cell
    // cannot alter a later cell in this object, preserving snapshot semantics.
    internal static unsafe Bitmap ExtractObjectArtwork(Bitmap background,Rectangle bounds,List<(int X,int Y)> tiles,int scrollX,int scrollY,int backdropRgb,bool transparent,out bool visible)
    {
        if(bounds.Width<=0||bounds.Height<=0||!new Rectangle(Point.Empty,background.Size).Contains(bounds))throw new ArgumentOutOfRangeException(nameof(bounds));
        Bitmap crop=new(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb);
        visible=false;
        BitmapData? source=null,target=null;
        try
        {
            try
            {
                source=background.LockBits(new Rectangle(Point.Empty,background.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
                target=crop.LockBits(new Rectangle(Point.Empty,crop.Size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                for(int y=0;y<crop.Height;y++)new Span<int>((byte*)target.Scan0+y*target.Stride,crop.Width).Clear();
                int replacement=unchecked((int)0xff000000)|backdropRgb;
                foreach((int tileX,int tileY) in tiles)
                {
                    Rectangle cell=Rectangle.Intersect(new Rectangle(tileX*8-scrollX,tileY*8-scrollY,8,8),bounds);
                    for(int y=cell.Top;y<cell.Bottom;y++)
                    {
                        int* from=(int*)((byte*)source.Scan0+y*source.Stride)+cell.Left;
                        int* to=(int*)((byte*)target.Scan0+(y-bounds.Y)*target.Stride)+cell.Left-bounds.X;
                        for(int x=0;x<cell.Width;x++)
                        {
                            int pixel=from[x];
                            if(transparent&&(pixel&0xffffff)==backdropRgb)pixel=0;
                            to[x]=pixel;
                            if((pixel&unchecked((int)0xff000000))!=0){visible=true;from[x]=replacement;}
                        }
                    }
                }
            }
            finally
            {
                if(target is not null)crop.UnlockBits(target);
                if(source is not null)background.UnlockBits(source);
            }
            return crop;
        }
        catch{crop.Dispose();throw;}
    }

    internal static unsafe int FindDominantRgb(Bitmap bitmap,bool direct=false,bool runs=false)
    {
        Dictionary<int, int> colors = [];
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            if(runs)
            {
                // Preserve row-major first-occurrence insertion order, including
                // tied maxima and colors with differing alpha. Only dictionary
                // updates are coalesced; every RGB pixel is still examined.
                for(int y=32;y<bitmap.Height;y++)
                {
                    int* row=(int*)((byte*)data.Scan0+y*data.Stride);
                    int x=0;
                    while(x<bitmap.Width)
                    {
                        int rgb=row[x]&0xffffff,start=x++;
                        while(x<bitmap.Width&&(row[x]&0xffffff)==rgb)x++;
                        colors[rgb]=colors.GetValueOrDefault(rgb)+x-start;
                    }
                }
            }
            else if(direct)
            {
                for(int y=32;y<bitmap.Height;y++)
                {
                    int* row=(int*)((byte*)data.Scan0+y*data.Stride);
                    for(int x=0;x<bitmap.Width;x++)
                    {
                        int rgb=row[x]&0xffffff;
                        ref int count=ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(colors,rgb,out _);
                        count++;
                    }
                }
            }
            else
            {
            for (int y = 32; y < bitmap.Height; y++)
            {
                int* row = (int*)((byte*)data.Scan0 + y * data.Stride);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int rgb = row[x] & 0x00FFFFFF;
                    colors[rgb] = colors.GetValueOrDefault(rgb) + 1;
                }
            }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return colors.Count == 0 ? 0 : colors.MaxBy(pair => pair.Value).Key;
    }

    internal static unsafe void EraseObjectFromBackground(
        Bitmap background,
        Bitmap objectImage,
        Rectangle bounds,
        int backdropRgb)
    {
        BitmapData backgroundData = background.LockBits(
            new Rectangle(0, 0, background.Width, background.Height),
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppArgb);
        BitmapData objectData = objectImage.LockBits(
            new Rectangle(0, 0, objectImage.Width, objectImage.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int replacement = unchecked((int)0xFF000000) | backdropRgb;
            for (int y = 0; y < objectImage.Height; y++)
            {
                int* objectRow = (int*)((byte*)objectData.Scan0 + y * objectData.Stride);
                int* backgroundRow = (int*)((byte*)backgroundData.Scan0 + (bounds.Y + y) * backgroundData.Stride) + bounds.X;
                for (int x = 0; x < objectImage.Width; x++)
                {
                    if ((objectRow[x] & unchecked((int)0xFF000000)) != 0)
                    {
                        backgroundRow[x] = replacement;
                    }
                }
            }
        }
        finally
        {
            objectImage.UnlockBits(objectData);
            background.UnlockBits(backgroundData);
        }
    }

    private static IEnumerable<SceneObject> ExtractSprites(
        NesFrame frame,
        bool exactProfile,
        ProjectionProfile projectionProfile, GameRecognitionProfile? gameProfile = null,bool bulk=true,bool preflight=false)
    {
        List<SpriteTile> tiles = [];
        for (int index = 0; index < 64; index++)
        {
            int offset = index * 4;
            int y = frame.Oam[offset] + 1;
            int x = frame.Oam[offset + 3];
            if (y >= 240 || x >= 256)
            {
                continue;
            }

            // NES color index0 is transparent; all other indices are opaque,
            // even if their palette RGB is black. Any set CHR bit therefore
            // proves visible artwork, independent of flips/palette selection.
            // Check the exact decoder slice before allocation, not a bitmap
            // scan afterwards. Keep the previous path for paired diagnostics.
            if(preflight && !SpriteTileHasArtwork(frame.Chr,frame.Palette,
                frame.Oam[offset+1],frame.SpritePatternBase,frame.LargeSprites))continue;

            Bitmap tile = DecodeSpriteTile(
                frame.Chr,
                frame.Palette,
                frame.Oam[offset + 1],
                frame.Oam[offset + 2], frame.SpritePatternBase, frame.LargeSprites,bulk);
            if (preflight || HasVisiblePixel(tile))
            {
                tiles.Add(new SpriteTile(index, new Rectangle(x, y, 8, frame.LargeSprites ? 16 : 8), tile, frame.Oam[offset+2]&3));
            }
            else
            {
                tile.Dispose();
            }
        }

        List<SpriteTile>? ownedBody=null;
        if(exactProfile)
        {
            // Verified SMB reserves OAM slots1..8 for Mario's four two-tile
            // rows. DefaultSprOffsets[0]=$04 and SpriteShuffler skips <$28.
            // Do not merge a touching enemy/effect into the player's body.
            var body=tiles.Where(t=>t.OamIndex is >=1 and <=8).ToList();
            if(body.Count>0)
            {
                ownedBody=body;
                tiles.RemoveAll(t=>t.OamIndex is >=1 and <=8);
            }
        }
        else if(gameProfile?.PlayerTracking is {BodyAssembly:not null} layoutTracking&&layoutTracking.TryGetBodySlots(frame,out var layoutSlots))
        {
            // A complete published layout already verifies native coordinates
            // and ownership; don't rescan every pose for each individual tile.
            var body=tiles.Where(t=>layoutSlots.Contains(t.OamIndex)).ToList();
            if(body.Count==layoutSlots.Length)
            {
                ownedBody=body.Select(t=>t with{Bounds=layoutTracking.UnwrapBodyTile(frame,t.Bounds,rangeVerified:true)}).ToList();
                tiles.RemoveAll(t=>layoutSlots.Contains(t.OamIndex));
            }
        }
        else if(gameProfile?.PlayerTracking is {} bodyTracking&&bodyTracking.TryGetBodyRange(frame,out int first,out int count))
        {
            // The native animation range was verified once above. Rechecking
            // every possible pose for every tile would multiply this scan.
            bool Owns(int index)=>bodyTracking.BodyTilesByAnimation is not null
                ? index>=first&&index<first+count : bodyTracking.OwnsBodySlot(frame,index);
            var body=tiles.Where(t=>Owns(t.OamIndex)).ToList();
            if(body.Count>0)
                body=body.Select(tile=>tile with{Bounds=bodyTracking.UnwrapBodyTile(frame,tile.Bounds,rangeVerified:true)}).ToList();
            if(body.Count>0&&bodyTracking.Matches(frame,body.Select(t=>t.Bounds).Aggregate(Rectangle.Union),body.Select(t=>t.OamIndex)))
            {
                ownedBody=body;
                // Native ownership precedes proximity. Merchandise, arrows or
                // enemies sharing the player's palette remain separate objects.
                tiles.RemoveAll(t=>Owns(t.OamIndex));
            }
        }
        // Separate HUD ownership before proximity clustering. A touching actor
        // must never become part of a flat score/health sprite group.
        List<SpriteTile>? hudTiles=null;
        if(gameProfile is not null && (gameProfile.FlatSpriteRegions is not null || gameProfile.FlatSpriteSlotsByState is not null))
        {
            bool IsHud(SpriteTile tile)=>gameProfile.ProtectsSprite(tile.Bounds,frame)||gameProfile.ProtectsSpriteSlot(frame,tile.OamIndex);
            hudTiles=tiles.Where(IsHud).ToList();
            tiles.RemoveAll(tile=>IsHud(tile));
        }
        List<List<SpriteTile>> assembled=[];
        if(!exactProfile && gameProfile?.SpriteAssemblies is {} assemblies)
        {
            var candidates=assemblies.SelectMany(a=>a.Match(frame)).GroupBy(slots=>string.Join(',',slots)).Select(g=>g.First()).ToArray();
            foreach(var slots in candidates)
            {
                // Conflicting complete layouts are ambiguous, not permission to
                // steal a subset from another actor or a protected HUD/player.
                if(candidates.Any(other=>!ReferenceEquals(other,slots)&&other.Intersect(slots).Any()))continue;
                var body=tiles.Where(t=>slots.Contains(t.OamIndex)).ToList();
                if(body.Count!=slots.Length)continue;
                assembled.Add(body);
                tiles.RemoveAll(t=>slots.Contains(t.OamIndex));
            }
        }
        IEnumerable<List<SpriteTile>> clusters=ClusterSpriteTiles(tiles,separatePalettes:!exactProfile && gameProfile?.PlayerTracking is not null).Concat(assembled);
        if(hudTiles is {Count:>0})clusters=clusters.Concat(ClusterSpriteTiles(hudTiles,separatePalettes:true));
        if(ownedBody is not null)clusters=clusters.Append(ownedBody);
        List<SpriteTile>? trackedPlayer=null;
        if(!exactProfile && gameProfile?.PlayerTracking is {} tracking)
        {
            var candidates=clusters.ToList();
            // At most one actor. Ambiguous overlapping clusters prefer the
            // smaller bounds, not whichever enemy happened to occupy OAM 0.
            trackedPlayer=ownedBody??candidates.Where(c=>tracking.Matches(frame,
                c.Select(t=>t.Bounds).Aggregate(Rectangle.Union),c.Select(t=>t.OamIndex)))
                .OrderBy(c=>{var b=c.Select(t=>t.Bounds).Aggregate(Rectangle.Union);return b.Width*b.Height;}).FirstOrDefault();
            clusters=candidates;
        }
        foreach (List<SpriteTile> cluster in clusters)
        {
            Rectangle bounds = cluster.Select(item => item.Bounds).Aggregate(Rectangle.Union);
            Bitmap image = new(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Transparent);
                foreach (SpriteTile tile in cluster.OrderByDescending(item => item.OamIndex))
                {
                    graphics.DrawImageUnscaled(tile.Image, tile.Bounds.X - bounds.X, tile.Bounds.Y - bounds.Y);
                }
            }

            foreach (SpriteTile tile in cluster)
            {
                tile.Image.Dispose();
            }

            (SceneObjectKind kind, string label) = IdentifySprite(frame, bounds, exactProfile);
            if(exactProfile && ReferenceEquals(cluster,ownedBody))
                (kind,label)=(SceneObjectKind.Player,ReadRam(frame,0x0753)==1?"Luigi":"Mario");
            // Explicit profile-declared actor slots, not guessed identities for
            // unknown cartridges. HUD/region protection still wins below.
            if(!exactProfile && gameProfile is not null &&
                (ReferenceEquals(cluster,trackedPlayer) || cluster.Any(tile=>gameProfile.PlayerOamIndices.Contains(tile.OamIndex))))
                (kind,label)=(SceneObjectKind.Player,gameProfile.PlayerLabel);
            bool verifiedTrackedPlayer=ReferenceEquals(cluster,trackedPlayer);
            bool statusSprite = (exactProfile && bounds.Top < 32 && !ReferenceEquals(cluster,ownedBody)) || gameProfile?.ProtectsSprite(bounds,frame)==true || cluster.Any(tile=>gameProfile?.ProtectsSpriteSlot(frame,tile.OamIndex)==true) || (gameProfile is not null &&
                !(verifiedTrackedPlayer ? gameProfile.AllowsTrackedPlayer(bounds,frame) : gameProfile.Allows(bounds,frame)));
            if (statusSprite) { kind = SceneObjectKind.Sprite; label = exactProfile ? "HUD coin" : "HUD / interface"; }
            // State-owned HUD slots can contain a later OAM update than the
            // already-rendered scanline. Keep only decoded opaque pixels that
            // actually appear in this paired native publication; never draw a
            // stale glyph or recolor it using arbitrary nearby enemy pixels.
            if(gameProfile is not null && cluster.All(tile=>gameProfile.ProtectsSpriteSlot(frame,tile.OamIndex)) &&
               frame.NativeScreenPixels is {Length:61440} nativeHud && frame.CaptureScanline==96 && frame.NativeScreenSequence==frame.Sequence)
                MaskUnpublishedHudPixels(image,bounds,nativeHud);
            ObjectProjectionRule rule = projectionProfile.RuleFor(kind);
            yield return new SceneObject
            {
                Kind = kind,
                Label = label,
                IdentityKey = kind == SceneObjectKind.Player ? "sprite:player" : $"sprite:{kind}:oam:{cluster.Min(tile => tile.OamIndex)}",
                Bounds = bounds,
                Image = image,
                Accent = Accents[kind],
                Depth = (kind == SceneObjectKind.Player ? 1.25f : 1.05f) * rule.DepthScale,
                ProjectionEnabled = rule.Enabled && !statusSprite,
                SortOrder = statusSprite ? 100 : 20
            };
        }
    }

    private static unsafe void MaskUnpublishedHudPixels(Bitmap image,Rectangle bounds,int[] nativeHud)
    {
        var pixels=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        try
        {
            int transparent=Color.Transparent.ToArgb();
            for(int py=0;py<image.Height;py++)
            {
                int* row=(int*)((byte*)pixels.Scan0+py*pixels.Stride);int sy=bounds.Y+py;
                for(int px=0;px<image.Width;px++)
                {
                    int sx=bounds.X+px,argb=row[px];
                    if((uint)argb>>24!=0 && (sx<0||sx>=256||sy<0||sy>=240||(argb&0xffffff)!=(nativeHud[sy*256+sx]&0xffffff)))row[px]=transparent;
                }
            }
        }
        finally{image.UnlockBits(pixels);}
    }

    internal static unsafe Bitmap DecodeSpriteTile(byte[] chr, byte[] palette, byte tileIndex, byte attributes, int patternBase, bool large = false,bool bulk=true)
    {
        int height = large ? 16 : 8;
        Bitmap bitmap = new(8, height, PixelFormat.Format32bppArgb);
        if (chr.Length < 4096 || palette.Length < 32)
        {
            return bitmap;
        }

        bool flipX = (attributes & 0x40) != 0;
        bool flipY = (attributes & 0x80) != 0;
        int paletteOffset = 0x10 + (attributes & 0x03) * 4;
        // In 8x16 mode bit 0 selects the pattern table; the remaining
        // bits select an even/odd tile pair. Vertical flip swaps both halves.
        int tileOffset = large ? (tileIndex & 1) * 4096 + (tileIndex & 0xFE) * 16 : patternBase + tileIndex * 16;
        if (tileOffset + (large ? 32 : 16) > chr.Length) return bitmap;

        if(!bulk)
        {
            // Original per-pixel implementation retained for explicit paired
            // diagnostic comparisons, never the ordinary application default.
            for(int y=0;y<height;y++)
            {
                int sy=flipY?height-1-y:y;
                int row=tileOffset+(sy/8)*16+sy%8;
                for(int x=0;x<8;x++)
                {
                    int bit=flipX?x:7-x;
                    int color=((chr[row]>>bit)&1)|(((chr[row+8]>>bit)&1)<<1);
                    if(color!=0)bitmap.SetPixel(x,y,NesPalette.Get(palette[paletteOffset+color]));
                }
            }
            return bitmap;
        }

        Span<int> colors=stackalloc int[4];colors[0]=0;
        for(int i=1;i<4;i++)colors[i]=NesPalette.Get(palette[paletteOffset+i]).ToArgb();
        BitmapData data=bitmap.LockBits(new Rectangle(0,0,8,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try
        {
        for (int outputY = 0; outputY < height; outputY++)
        {
            int* target=(int*)((byte*)data.Scan0+outputY*data.Stride);
            int sourceY = flipY ? height - 1 - outputY : outputY;
            int rowOffset = tileOffset + (sourceY / 8) * 16 + sourceY % 8;
            byte low = chr[rowOffset];
            byte high = chr[rowOffset + 8];
            for (int outputX = 0; outputX < 8; outputX++)
            {
                int sourceX = flipX ? 7 - outputX : outputX;
                int bit = 7 - sourceX;
                int color = ((low >> bit) & 1) | (((high >> bit) & 1) << 1);
                target[outputX]=colors[color];
            }
        }
        }
        finally{bitmap.UnlockBits(data);}
        return bitmap;
    }

    internal static bool SpriteTileHasArtwork(byte[] chr,byte[] palette,byte tileIndex,int patternBase,bool large)
    {
        if(chr.Length<4096 || palette.Length<32)return false;
        int offset=large?(tileIndex&1)*4096+(tileIndex&0xfe)*16:patternBase+tileIndex*16;
        int bytes=large?32:16;
        if(offset+bytes>chr.Length)return false;
        return chr.AsSpan(offset,bytes).IndexOfAnyExcept((byte)0)>=0;
    }

    private static IEnumerable<List<SpriteTile>> ClusterSpriteTiles(List<SpriteTile> source,bool separatePalettes=false)
    {
        HashSet<SpriteTile> remaining = [.. source];
        while (remaining.Count > 0)
        {
            SpriteTile seed = remaining.First();
            remaining.Remove(seed);
            Queue<SpriteTile> queue = new();
            List<SpriteTile> cluster = [];
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                SpriteTile tile = queue.Dequeue();
                cluster.Add(tile);
                Rectangle reach = Rectangle.Inflate(tile.Bounds, 3, 3);
                foreach (SpriteTile neighbor in remaining.Where(candidate => reach.IntersectsWith(candidate.Bounds) &&
                    (!separatePalettes || candidate.Palette==tile.Palette)).ToArray())
                {
                    remaining.Remove(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
            yield return cluster;
        }
    }

    private static (SceneObjectKind Kind, string Label) IdentifySprite(
        NesFrame frame,
        Rectangle bounds,
        bool exactProfile)
    {
        if (!exactProfile)
        {
            return (SceneObjectKind.Sprite, "Sprite");
        }

        int screenLeft = ReadRam(frame, 0x071A) * 256 + ReadRam(frame, 0x071C);
        for (int slot = 0; slot < 6; slot++)
        {
            if (ReadRam(frame, 0x000F + slot) == 0)
            {
                continue;
            }
            int enemyX = ReadRam(frame, 0x006E + slot) * 256 + ReadRam(frame, 0x0087 + slot) - screenLeft;
            int enemyY = ReadRam(frame, 0x00CF + slot);
            if (!Near(bounds, enemyX, enemyY, 24))
            {
                continue;
            }

            int id = ReadRam(frame, 0x0016 + slot);
            string name = EnemyNames.GetValueOrDefault(id, $"Object {id:X2}");
            SceneObjectKind kind = id is >= 0x2E and <= 0x32 ? SceneObjectKind.Item : SceneObjectKind.Enemy;
            return (kind, name);
        }

        return (SceneObjectKind.Sprite, "Sprite effect");
    }

    private static bool Near(Rectangle bounds, int x, int y, int tolerance)
    {
        Rectangle target = Rectangle.Inflate(bounds, tolerance, tolerance);
        return target.Contains(x, y) || target.Contains(x + 8, y + 8);
    }

    internal static bool HasVisiblePixel(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y).A != 0) return true;
        }
        return false;
    }

    private static float DepthFor(SceneObjectKind kind, Rectangle bounds) => kind switch
    {
        SceneObjectKind.Bush => 0.72f + Math.Min(0.35f, bounds.Width / 180f),
        SceneObjectKind.Cloud => 0.40f,
        SceneObjectKind.Hill => 0.52f,
        SceneObjectKind.Tree => 0.66f,
        SceneObjectKind.Pipe => 0.95f,
        SceneObjectKind.QuestionBlock => 1.12f,
        SceneObjectKind.Brick => 0.88f,
        SceneObjectKind.Terrain => 0.38f,
        SceneObjectKind.Castle => 0.62f,
        SceneObjectKind.Flagpole => 0.75f,
        _ => 0.7f
    };

    private static string KindLabel(SceneObjectKind kind) => kind switch
    {
        SceneObjectKind.QuestionBlock => "Question block",
        _ => kind.ToString()
    };

    private static int ReadRam(NesFrame frame, int address) =>
        address >= 0 && address < frame.Ram.Length ? frame.Ram[address] : 0;

    private static int Pack(int topLeft, int bottomLeft, int topRight, int bottomRight) =>
        (topLeft << 24) | (bottomLeft << 16) | (topRight << 8) | bottomRight;

    private static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;

    private sealed record SpriteTile(int OamIndex, Rectangle Bounds, Bitmap Image, int Palette);
    private readonly record struct BackgroundObjectIdentity(SceneObjectKind Kind, string Label, bool TransparentBackdrop=false);
}
