using System.Security.Cryptography;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

// Offline calibration tool: explicit paths only, never runs at startup.
internal static class ProfileAuthoring
{
    internal sealed class Area
    {
        public Rectangle Bounds { get; set; }
        public string Kind { get; set; } = "Terrain";
        public string Label { get; set; } = "Scenery";
    }
    internal sealed class Recipe
    {
        public string Id { get; set; } = "";
        public string[] Frames { get; set; } = [];
        public string[] Menus { get; set; } = [];
        public GameRecognitionProfile Profile { get; set; } = new();
        public Area[] Areas { get; set; } = [];
        // Areas are local to each scene, not assumed to mean the same thing in
        // every level. The older Frames/Areas recipe remains supported.
        public Dictionary<string, Area[]> FrameAreas { get; set; } = [];
    }
    internal static int Run(string recipes, string output)
    {
        try
        {
            Directory.CreateDirectory(output);
            List<object> report = [];
            foreach (Recipe recipe in JsonSerializer.Deserialize<Recipe[]>(File.ReadAllText(recipes))!)
            {
                var cartridge = BuiltInGameProfiles.Cartridges.Single(c=>c.Id==recipe.Id);
                var profile = recipe.Profile;
                profile.Name = cartridge.Name+" · built-in"; profile.RomName = cartridge.Name+".nes";
                profile.RequireRecognizedScene = true; profile.TransparentBackdrop = true; profile.SeparateMetatiles = true;
                int size = profile.CellSize, step = size/8;
                string blank = Convert.ToHexString(SHA256.HashData(new byte[size*size]));
                foreach(string path in recipe.Frames)
                {
                    NesFrame frame = CartridgeViewport.NormalizeCapture(recipe.Id, JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!);
                    for(int y=frame.ScrollY/8/step*step;y<=(frame.ScrollY+239)/8;y+=step)
                    for(int x=frame.ScrollX/8/step*step;x<=(frame.ScrollX+255)/8;x+=step)
                    {
                        Rectangle bounds = new(x*8-frame.ScrollX,y*8-frame.ScrollY,size,size);
                        if (!profile.Allows(bounds, frame) || !new Rectangle(0,0,256,240).Contains(bounds)) continue;
                        var signature=MetatileSignature.Read(frame,x,y,size);
                        string fingerprint=MetatileVisualFingerprint.Read(frame,x,y,size);
                        if (fingerprint==blank) continue;
                        Area[] areas = recipe.FrameAreas.TryGetValue(path, out var specific) ? specific : recipe.Areas;
                        Area? area=areas.LastOrDefault(a=>a.Bounds.Contains(bounds.X+size/2,bounds.Y+size/2));
                        if(area is null) continue;
                        if(!profile.BackgroundRules.TryGetValue(signature.Key,out var rule))
                            profile.BackgroundRules[signature.Key]=rule=new(){Kind=area.Kind,Label=area.Label};
                        var variant=rule.ArtworkVariants.FirstOrDefault(v=>v.Fingerprint==fingerprint);
                        if (variant is null) { variant=new(){Fingerprint=fingerprint};rule.ArtworkVariants.Add(variant); }
                        if (profile.AllowMaskedLeftEdgeArtwork && size==16)
                            variant.RightHalfFingerprint=MetatileVisualFingerprint.ReadRightHalf(frame,x,y);
                    }
                }
                // Exclude any artwork shared with the supplied negative scenes.
                profile.Normalize();
                foreach(string path in recipe.Menus)
                {
                    NesFrame menu=CartridgeViewport.NormalizeCapture(recipe.Id, JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!);
                    // An explicit cartridge-specific RAM gate can distinguish a
                    // title/menu from gameplay even when they share cloud tiles.
                    // Keep those valid gameplay variants; the negative activation
                    // validation below still checks every supplied menu.
                    int gate=profile.GameplayStateAddress;
                    if(gate>=0 && gate<menu.Ram.Length && menu.Ram[gate]!=profile.GameplayStateValue) continue;
                    for(int y=menu.ScrollY/8/step*step;y<=(menu.ScrollY+239)/8;y+=step)
                    for(int x=menu.ScrollX/8/step*step;x<=(menu.ScrollX+255)/8;x+=step)
                    {
                        if(!profile.Allows(new(x*8-menu.ScrollX,y*8-menu.ScrollY,size,size), menu)) continue;
                        // A menu can leave level artwork in off-screen PPU memory.
                        // Only exclude artwork actually drawn on that menu.
                        if (menu.NativeScreenPixels is not null && !MetatileVisualFingerprint.IsVisible(menu,x,y,size)) continue;
                        string key=MetatileSignature.Read(menu,x,y,size).Key;
                        if(!profile.BackgroundRules.TryGetValue(key,out var rule)) continue;
                        string artwork=MetatileVisualFingerprint.Read(menu,x,y,size);
                        rule.ArtworkVariants.RemoveAll(v=>v.Fingerprint==artwork);
                        if(rule.ArtworkVariants.Count==0) profile.BackgroundRules.Remove(key);
                    }
                }
                profile.Normalize();
                if (recipe.Id=="tetris")
                {
                    NesFrame board=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(recipe.Frames[0]))!;
                    foreach(Point p in new[]{new Point(80,40),new Point(176,208)})
                    {
                        int x=(board.ScrollX+p.X)/8,y=(board.ScrollY+p.Y)/8;
                        profile.SceneAnchors.Add(new(){X=p.X,Y=p.Y,Key=MetatileSignature.Read(board,x,y,8).Key,Fingerprint=MetatileVisualFingerprint.Read(board,x,y,8)});
                    }
                }
                // A matching tile number with different menu artwork is not a match.
                foreach(string path in recipe.Menus)
                {
                    NesFrame menu=CartridgeViewport.NormalizeCapture(recipe.Id, JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!);
                    if(profile.IsActive(menu)) throw new InvalidDataException(recipe.Id+" activates on menu "+path);
                }
                // Dense calibration sets contain hundreds of full PPU/native
                // snapshots. Validate one at a time instead of retaining them all.
                foreach(string path in recipe.Frames)
                {
                    var frame = CartridgeViewport.NormalizeCapture(recipe.Id, JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!);
                    if(!profile.IsActive(frame))
                    {
                        int art=0,visible=0;
                        for(int y=frame.ScrollY/8/step*step;y<=(frame.ScrollY+239)/8;y+=step)
                        for(int x=frame.ScrollX/8/step*step;x<=(frame.ScrollX+255)/8;x+=step)
                        if(profile.Match(MetatileSignature.Read(frame,x,y,size),frame,x,y) is not null)
                        {art++;if(MetatileVisualFingerprint.ReadScreen(frame,x*8-frame.ScrollX,y*8-frame.ScrollY,size)==MetatileVisualFingerprint.Read(frame,x,y,size)) visible++;}
                        throw new InvalidDataException(recipe.Id+" insufficient scene matches in "+path+" at sequence "+frame.Sequence+" scroll "+frame.ScrollY+" artwork "+art+" visible "+visible);
                    }
                    using var scene=new SmbProfile().Build(frame,false,null,profile);
                    if(!scene.Objects.Any(o=>o.SortOrder<20)) throw new InvalidDataException(recipe.Id+" has no scenery objects");
                    var offender=scene.Objects.FirstOrDefault(o=>o.ProjectionEnabled&&scene.FlatRegions.Any(r=>HasPixelsIn(o,r)));
                    if(offender is not null) throw new InvalidDataException(recipe.Id+" projects HUD: "+offender.Label+" "+offender.Bounds+" regions "+string.Join(";",profile.FlatRegions));
                }
                File.WriteAllText(Path.Combine(output,recipe.Id+".json"),JsonSerializer.Serialize(profile,new JsonSerializerOptions{WriteIndented=true}));
                report.Add(new {recipe.Id, Rules=profile.BackgroundRules.Count, Frames=recipe.Frames.Length, Menus=recipe.Menus.Length});
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(recipes))!,"profile-validation.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            if(File.Exists(Path.Combine(output,"error.txt"))) File.Delete(Path.Combine(output,"error.txt"));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static bool HasPixelsIn(SceneObject item, Rectangle region)
    {
        Rectangle overlap=Rectangle.Intersect(item.Bounds,region);
        for(int y=overlap.Top;y<overlap.Bottom;y++) for(int x=overlap.Left;x<overlap.Right;x++)
            if(item.Image.GetPixel(x-item.Bounds.Left,y-item.Bounds.Top).A!=0) return true;
        return false;
    }
}
