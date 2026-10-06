using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class IcarusPlayerTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        List<object> results=[];
        try
        {
            var samples=JsonSerializer.Deserialize<ProfileCoverageTests.Sample[]>(File.ReadAllText(manifest))!;
            using var expectations=JsonDocument.Parse(File.ReadAllText(manifest));
            using var frameReader=new ArchiveFrameReader();
            int sampleIndex=0;
            foreach(var sample in samples)
            {
                if(sample.Id!="icarus")throw new InvalidDataException("Explicit Kid Icarus fixtures required.");
                var frame=CartridgeViewport.NormalizeCapture("icarus",frameReader.Read(sample.Frame,sample.Archive,sample.ArchiveSHA256,sample.FrameSHA256));
                var profile=GameRecognitionProfileStore.ReadFromFile(sample.Profile);
                var tracking=profile.PlayerTracking??throw new InvalidOperationException("Native Pit tracking required.");
                if(sampleIndex==0){VerifyBodyWrapping(frame,tracking);VerifyPartialBody(frame,tracking);}
                if(tracking.XAddress!=1827||tracking.YAddress!=1824||tracking.SpritePalette!=0||tracking.XScrollAddress is not null||tracking.YScrollAddress is not null||profile.PlayerLabel!="Pit")
                    throw new InvalidOperationException("Pit uses native screen object coordinates and palette0.");
                using var scene=new SmbProfile().Build(frame,false,null,profile);
                var actors=scene.Objects.Where(o=>o.Kind==SceneObjectKind.Player&&o.ProjectionEnabled).ToArray();
                var fixture=expectations.RootElement[sampleIndex++];
                if(fixture.TryGetProperty("ExpectedPlayerCount",out var count)&&actors.Length!=count.GetInt32())
                    throw new InvalidOperationException(sample.Area+": visually reviewed fixture requires "+count.GetInt32()+" Pit actor(s), actual="+actors.Length);
                bool wrappedBody=fixture.TryGetProperty("ExpectedPlayerWrappedOamIndices",out var wrappedIndices);
                if(wrappedBody||fixture.TryGetProperty("ExpectedPlayerOamIndices",out _))
                {
                    var ownedIndices=wrappedBody?wrappedIndices:fixture.GetProperty("ExpectedPlayerOamIndices");
                    Rectangle? body=null;
                    foreach(var owned in ownedIndices.EnumerateArray())
                    {
                        int index=owned.GetInt32(),offset=index*4;
                        if(index<0||index>=64||offset+3>=frame.Oam.Length)throw new InvalidDataException("Invalid reviewed player OAM fixture.");
                        if(frame.Oam[offset]>=239)continue;
                        using var tile=SmbProfile.DecodeSpriteTile(frame.Chr,frame.Palette,frame.Oam[offset+1],frame.Oam[offset+2],frame.SpritePatternBase,frame.LargeSprites);
                        bool visible=false;
                        for(int py=0;py<tile.Height;py++)for(int px=0;px<tile.Width;px++)visible|=tile.GetPixel(px,py).A!=0;
                        if(!visible)continue;
                        var bounds=new Rectangle(frame.Oam[offset+3],frame.Oam[offset]+1,8,frame.LargeSprites?16:8);
                        if(wrappedBody)
                        {
                            int anchor=frame.Ram[1827],wrappedX=anchor+((bounds.X-anchor+128)&255)-128;
                            bounds=new Rectangle(wrappedX,bounds.Y,bounds.Width,bounds.Height);
                        }
                        body=body is Rectangle previous?Rectangle.Union(previous,bounds):bounds;
                    }
                    if(body is not Rectangle expectedBody||actors.Length!=1||actors[0].Bounds!=expectedBody)
                        throw new InvalidOperationException(sample.Area+": owned native player bounds must exclude nearby items. "+JsonSerializer.Serialize(new{Expected=body,Actual=actors.Select(a=>a.Bounds)}));
                    using var ownedImage=new Bitmap(expectedBody.Width,expectedBody.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using(var graphics=Graphics.FromImage(ownedImage))
                    {
                        graphics.Clear(Color.Transparent);
                        foreach(int index in ownedIndices.EnumerateArray().Select(v=>v.GetInt32()).OrderByDescending(i=>i))
                        {
                            int offset=index*4;
                            if(frame.Oam[offset]>=239)continue;
                            using var tile=SmbProfile.DecodeSpriteTile(frame.Chr,frame.Palette,frame.Oam[offset+1],frame.Oam[offset+2],frame.SpritePatternBase,frame.LargeSprites);
                            int tileX=frame.Oam[offset+3];
                            if(wrappedBody){int anchor=frame.Ram[1827];tileX=anchor+((tileX-anchor+128)&255)-128;}
                            graphics.DrawImageUnscaled(tile,tileX-expectedBody.X,frame.Oam[offset]+1-expectedBody.Y);
                        }
                    }
                    for(int py=0;py<ownedImage.Height;py++)for(int px=0;px<ownedImage.Width;px++)
                        if(ownedImage.GetPixel(px,py).ToArgb()!=actors[0].Image.GetPixel(px,py).ToArgb())
                            throw new InvalidOperationException(sample.Area+": projected player bitmap includes foreign sprite pixels.");
                }
                int x=frame.Ram[1827],y=frame.Ram[1824];
                int palette=frame.Ram[160] is >=2 and <=8?(frame.Oam[8*4+2]&3):0;
                if(tracking.PaletteStateAddress!=160||tracking.PalettesByState is not null||tracking.PaletteOamByState?.Count!=7||Enumerable.Range(2,7).Any(mode=>tracking.PaletteOamByState.GetValueOrDefault(mode,-1)!=8)||!tracking.TryGetPalette(frame,out int actualPalette)||actualPalette!=palette)
                    throw new InvalidOperationException("Gameplay Pit must follow its native OAM8 palette, including hurt flashes.");
                bool visibleBody=false;
                for(int i=0;i<64;i++)
                {
                    var bounds=new Rectangle(frame.Oam[i*4+3],frame.Oam[i*4]+1,8,frame.LargeSprites?16:8);
                    if(bounds.Top>=240||(frame.Oam[i*4+2]&3)!=palette||!bounds.Contains(x,y)||!profile.Allows(bounds,frame))continue;
                    using var tile=SmbProfile.DecodeSpriteTile(frame.Chr,frame.Palette,frame.Oam[i*4+1],frame.Oam[i*4+2],frame.SpritePatternBase,frame.LargeSprites);
                    for(int py=0;py<tile.Height;py++)for(int px=0;px<tile.Width;px++)visibleBody|=tile.GetPixel(px,py).A!=0;
                }
                if(sample.Negative)
                {
                    if(scene.Objects.Count!=0||frame.NativeScreenPixels?.Length!=61440)throw new InvalidOperationException("Native interface must remain flat.");
                    for(int py=0;py<240;py++)for(int px=0;px<256;px++)
                        if(scene.Background.GetPixel(px,py).ToArgb()!=frame.NativeScreenPixels[py*256+px])throw new InvalidOperationException("Native interface pixels changed.");
                }
                else if(profile.IsActive(frame))
                {
                    if(actors.Length>1||(visibleBody&&actors.Length!=1)||actors.Any(a=>a.Label!="Pit"||!tracking.ContainsPosition(frame,a.Bounds,new(x,y))))
                        throw new InvalidOperationException(sample.Area+": visible Pit must be one named actor at native coordinates. "+JsonSerializer.Serialize(new{x,y,visibleBody,Actors=actors.Select(a=>new{a.Label,a.Bounds}),Nearby=scene.Objects.Where(o=>o.Bounds.Contains(x,y)).Select(o=>new{o.Kind,o.Label,o.Bounds})}));
                }
                var cloned=profile.Clone();
                var imported=JsonSerializer.Deserialize<GameRecognitionProfile>(JsonSerializer.Serialize(profile))!;imported.Normalize();
                foreach(var copy in new[]{cloned,imported})
                    if(copy.PlayerLabel!="Pit"||copy.PlayerTracking?.XAddress!=1827||copy.PlayerTracking.YAddress!=1824||copy.PlayerTracking.SpritePalette!=0||copy.PlayerTracking.PositionTolerance!=2||profile.FormatVersion<14||copy.FormatVersion!=profile.FormatVersion||copy.PlayerTracking.PaletteOamByState?.Count!=7||copy.PlayerTracking.BodyOamCountByState?.Count!=7||Enumerable.Range(2,7).Any(mode=>copy.PlayerTracking.PaletteOamByState.GetValueOrDefault(mode,-1)!=8||copy.PlayerTracking.BodyOamCountByState.GetValueOrDefault(mode,-1)!=6))
                        throw new InvalidOperationException("Clone/import lost Pit identity.");
                // A newer profile is not an identity regression. Require its
                // actual format and sprite-only HUD guards to survive both
                // copying paths, instead of pinning every fixture to format14.
                foreach(var copy in new[]{cloned,imported})
                    if(!Enumerable.SequenceEqual(profile.FlatSpriteRegions??[],copy.FlatSpriteRegions??[]))
                        throw new InvalidOperationException("Clone/import lost sprite-only HUD guards.");
                if(profile.FlatSpriteRegions is {Count:>0})
                {
                    if(ReferenceEquals(cloned.FlatSpriteRegions,profile.FlatSpriteRegions))
                        throw new InvalidOperationException("Clone shares mutable sprite-only HUD guards.");
                    Rectangle originalGuard=profile.FlatSpriteRegions[0];
                    cloned.FlatSpriteRegions![0]=Rectangle.Empty;
                    if(profile.FlatSpriteRegions[0]!=originalGuard)
                        throw new InvalidOperationException("Clone mutates source sprite-only HUD guard.");
                }
                if(ReferenceEquals(cloned.PlayerTracking!.PaletteOamByState,tracking.PaletteOamByState))throw new InvalidOperationException("Clone shares mutable palette overrides.");
                if(ReferenceEquals(cloned.PlayerTracking.BodyOamCountByState,tracking.BodyOamCountByState))throw new InvalidOperationException("Clone shares mutable body ranges.");
                cloned.PlayerTracking.BodyOamCountByState![8]=5;
                if(tracking.BodyOamCountByState![8]!=6)throw new InvalidOperationException("Clone mutates body ownership.");
                foreach(var copy in new[]{profile.Clone(),imported})
                    if(copy.PlayerTracking!.BodyExtraOamIndicesByState?.Count!=1||!copy.PlayerTracking.BodyExtraOamIndicesByState[8].SequenceEqual(new[]{18,19}))
                        throw new InvalidOperationException("Clone/import lost native flying wing slots.");
                var wingClone=profile.Clone();wingClone.PlayerTracking!.BodyExtraOamIndicesByState![8][0]=20;
                if(tracking.BodyExtraOamIndicesByState![8][0]!=18)throw new InvalidOperationException("Clone mutates source flying wing slots.");
                cloned.PlayerTracking.PaletteOamByState![8]=9;
                if(tracking.PaletteOamByState![8]!=8)throw new InvalidOperationException("Clone mutates source palette override.");
                byte[] modes=(byte[])frame.Ram.Clone();
                foreach(byte mode in new byte[]{0,1,2,3,4,5,6,7,8,9,255})
                {
                    modes[160]=mode;
                    if(!tracking.TryGetPalette(frame with{Ram=modes},out int selected)||selected!=(mode is >=2 and <=8?(frame.Oam[8*4+2]&3):0))throw new InvalidOperationException("Wrong-mode palette leakage.");
                }
                if(tracking.TryGetPalette(frame with{Ram=[]},out _))throw new InvalidOperationException("Missing palette state accepted.");
                modes[160]=8;
                if(tracking.TryGetPalette(frame with{Ram=modes,Oam=[]},out _))throw new InvalidOperationException("Missing palette owner accepted.");
                if(tracking.Matches(frame with{Ram=[]},new Rectangle(x-4,y-4,8,8),[0]))throw new InvalidOperationException("Missing RAM identifies Pit.");
                byte[] other=(byte[])frame.Oam.Clone();other[2]=(byte)(palette==2?3:2);
                if(tracking.Matches(frame with{Oam=other},new Rectangle(x-4,y-4,8,8),[0]))throw new InvalidOperationException("Other palette identifies Pit.");
                modes[160]=2;
                if(!tracking.Matches(frame with{Ram=modes},new Rectangle(x+2,y-4,8,8),[8]))throw new InvalidOperationException("Owned two-pixel knockback anchor rejected.");
                if(tracking.Matches(frame with{Ram=modes},new Rectangle(x+3,y-4,8,8),[8]))throw new InvalidOperationException("Coordinate tolerance exceeds two pixels.");
                modes[160]=1;
                if(tracking.ContainsPosition(frame with{Ram=modes},new Rectangle(x+2,y-4,8,8),new(x,y)))throw new InvalidOperationException("Coordinate slack leaks outside owned gameplay states.");
                modes[160]=2;
                byte[] visibleOwner=(byte[])frame.Oam.Clone();visibleOwner[8*4]=0;
                if(tracking.Matches(frame with{Ram=modes,Oam=visibleOwner},new Rectangle(x-4,y-4,8,8),[9,10,11]))throw new InvalidOperationException("Omitting a visible native owner identifies hurt Pit.");
                if(tracking.Matches(frame with{Oam=[]},new Rectangle(x-4,y-4,8,8),[8]))throw new InvalidOperationException("Short OAM identifies Pit.");
                if(sampleIndex==1)
                {
                    foreach(var badExtras in new[]{new List<int>{8},new List<int>{64},new List<int>{18,18}})
                    {
                        var invalidExtras=profile.Clone();invalidExtras.PlayerTracking!.BodyExtraOamIndicesByState![8]=badExtras;
                        try{invalidExtras.Normalize();throw new InvalidOperationException("Invalid/overlapping wing slots accepted.");}catch(InvalidDataException){}
                    }
                    foreach(int invalidBodyCount in new[]{0,17})
                    {
                        var badBody=profile.Clone();badBody.PlayerTracking!.BodyOamCountByState![2]=invalidBodyCount;
                        try{badBody.Normalize();throw new InvalidOperationException("Invalid body range accepted.");}catch(InvalidDataException){}
                    }
                    var overflowBody=profile.Clone();overflowBody.PlayerTracking!.PaletteOamByState![2]=63;
                    try{overflowBody.Normalize();throw new InvalidOperationException("Overflowing body range accepted.");}catch(InvalidDataException){}
                    var ownerlessBody=profile.Clone();ownerlessBody.PlayerTracking!.BodyOamCountByState![1]=6;
                    try{ownerlessBody.Normalize();throw new InvalidOperationException("Body range without native owner accepted.");}catch(InvalidDataException){}
                    modes[160]=2;
                    if(!tracking.TryGetBodyRange(frame with{Ram=modes},out int first,out int bodyCount)||first!=8||bodyCount!=6)
                        throw new InvalidOperationException("Native player body slots changed.");
                    if(tracking.TryGetBodyRange(frame with{Ram=modes,Oam=[]},out _,out _)||tracking.TryGetBodyRange(frame with{Ram=[]},out _,out _))
                        throw new InvalidOperationException("Missing state/OAM activates body ownership.");
                    modes[160]=1;
                    if(tracking.TryGetBodyRange(frame with{Ram=modes},out _,out _))throw new InvalidOperationException("Body ownership leaks to menus.");
                    foreach(int invalid in new[]{-1,9})
                    {
                        var bad=profile.Clone();bad.PlayerTracking!.PositionTolerance=invalid;
                        try{bad.Normalize();throw new InvalidOperationException("Invalid coordinate tolerance accepted.");}catch(InvalidDataException){}
                    }
                    var unowned=profile.Clone();unowned.PlayerTracking!.PaletteOamByState=null;unowned.PlayerTracking.PaletteStateAddress=null;
                    try{unowned.Normalize();throw new InvalidOperationException("Unowned tolerance accepted.");}catch(InvalidDataException){}
                    modes[160]=1;
                    if(tracking.Matches(frame with{Ram=modes,Oam=[]},new Rectangle(x-4,y-4,8,8),[0]))throw new InvalidOperationException("Short OAM accepted in fallback mode.");
                }
                results.Add(new{sample.Area,sample.Negative,X=x,Y=y,VisibleBody=visibleBody,Actors=actors.Length,Width=actors.FirstOrDefault()?.Bounds.Width??0,Height=actors.FirstOrDefault()?.Bounds.Height??0,Passed=true});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=results.Count,Scope="Named native coordinate/palette, clone/import, interface and sampled animation checks; not exhaustive Pit poses or other actor identities.",Results=results},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }

    private static void VerifyBodyWrapping(NesFrame source,PlayerSpriteTracking tracking)
    {
        byte[] ram=(byte[])source.Ram.Clone();ram[160]=2;ram[1827]=2;
        var left=source with{Ram=ram};var tile=new Rectangle(255,61,8,8);
        if(tracking.UnwrapBodyTile(left,tile)!=new Rectangle(-1,61,8,8))throw new InvalidOperationException("Left seam body not unwrapped.");
        ram[1827]=254;
        if(tracking.UnwrapBodyTile(left,new Rectangle(0,61,8,8))!=new Rectangle(256,61,8,8))throw new InvalidOperationException("Right seam body not unwrapped.");
        var generic=new PlayerSpriteTracking{XAddress=1827,YAddress=1824};
        if(generic.UnwrapBodyTile(left,tile)!=tile||tracking.UnwrapBodyTile(left with{Oam=[]},tile)!=tile)
            throw new InvalidOperationException("Unowned/short body wrapped.");
    }

    private static void VerifyPartialBody(NesFrame source,PlayerSpriteTracking tracking)
    {
        byte[] ram=(byte[])source.Ram.Clone(),oam=(byte[])source.Oam.Clone();ram[160]=3;
        int palette=oam[8*4+2]&3;foreach(int index in new[]{10,11,12,13,14})oam[index*4+2]=(byte)palette;
        oam[8*4]=oam[9*4]=248;
        var frame=source with{Ram=ram,Oam=oam};var bounds=new Rectangle(ram[1827]-4,ram[1824]-8,16,16);
        if(!tracking.Matches(frame,bounds,[10,11,12,13]))throw new InvalidOperationException("Explicit crouching body remainder rejected.");
        if(tracking.Matches(frame,bounds,[10,11,14])||tracking.Matches(frame,bounds,[14])||tracking.Matches(frame,bounds,[]))
            throw new InvalidOperationException("Hidden owner admits foreign or empty body.");
        oam[8*4]=120;
        if(tracking.Matches(frame,bounds,[10,11,12,13]))throw new InvalidOperationException("Visible owner omitted.");
        oam[8*4]=248;
        var generic=new PlayerSpriteTracking{XAddress=1827,YAddress=1824,SpritePalette=palette,PaletteStateAddress=160,PaletteOamByState=new(){{3,8}}};
        if(generic.Matches(frame,bounds,[10,11,12,13]))throw new InvalidOperationException("Partial body fallback leaks to unowned tracking.");
    }

}
