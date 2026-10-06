using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class CastlevaniaProfileTests
{
    internal static int Run(string manifest, string output)
    {
        Directory.CreateDirectory(output);
        List<object> results=[];
        try
        {
            var samples=JsonSerializer.Deserialize<ProfileCoverageTests.Sample[]>(File.ReadAllText(manifest))!;
            using var expectations=JsonDocument.Parse(File.ReadAllText(manifest));
            int sampleIndex=0;
            using ArchiveFrameReader frameReader=new();
            foreach(var sample in samples)
            {
                if(sample.Id!="castlevania")throw new InvalidDataException("Explicit Castlevania fixtures required.");
                var frame=CartridgeViewport.NormalizeCapture(sample.Id,frameReader.Read(sample.Frame,sample.Archive,sample.ArchiveSHA256,sample.FrameSHA256));
                if(sample.RequireCoherentNativeFrame&&(frame.CaptureScanline!=96||frame.Sequence!=frame.NativeScreenSequence||frame.NativeScreenPixels?.Length!=61440))
                    throw new InvalidDataException(sample.Area+": coherent native frame required.");
                foreach(var expected in sample.ExpectedRam)
                    if(expected.Key<0||expected.Key>=frame.Ram.Length||frame.Ram[expected.Key]!=expected.Value)
                        throw new InvalidDataException(sample.Area+": native state assertion mismatch.");
                var profile=GameRecognitionProfileStore.ReadFromFile(sample.Profile);
                var tracking=profile.PlayerTracking??throw new InvalidOperationException("Native Simon object-coordinate tracking required.");
                if(tracking.XAddress!=908 || tracking.YAddress!=852 || tracking.SpritePalette!=0 || tracking.XScrollAddress is not null || tracking.YScrollAddress is not null)
                    throw new InvalidOperationException("Simon uses native screen object coordinates, not world/camera subtraction.");
                using var scene=new SmbProfile().Build(frame,false,null,profile);
                var actors=scene.Objects.Where(o=>o.Kind==SceneObjectKind.Player && o.ProjectionEnabled).ToArray();
                var fixture=expectations.RootElement[sampleIndex++];
                if(fixture.TryGetProperty("ExpectedPlayerCount",out var expectedCount) && actors.Length!=expectedCount.GetInt32())
                    throw new InvalidOperationException(sample.Area+": reviewed fixture player count mismatch.");
                if(fixture.TryGetProperty("ExpectedPlayerOamIndices",out var indices))
                    VerifyReviewedBody(frame,actors,indices,sample.Area);
                if(tracking.BodyAssembly is not null && fixture.TryGetProperty("ExpectedPlayerOamIndices",out var layoutIndices))
                    VerifyLayoutGuards(frame,profile,layoutIndices,sample.Area);
                bool separateSpriteChecked=false;
                if(fixture.TryGetProperty("ExpectedSeparateSpriteOamIndices",out var weaponIndices))
                {
                    int[] slots=weaponIndices.EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                    if(slots.Length==0 || slots.Any(i=>i<0 || i>=64))
                        throw new InvalidDataException("Reviewed separate sprite slots required.");
                    Rectangle bounds=Rectangle.Empty;
                    foreach(int slot in slots)
                    {
                        int offset=slot*4;
                        if(frame.Oam[offset]>=239)throw new InvalidDataException("Reviewed weapon must be visible.");
                        Rectangle tile=new(frame.Oam[offset+3],frame.Oam[offset]+1,8,frame.LargeSprites?16:8);
                        bounds=bounds.IsEmpty?tile:Rectangle.Union(bounds,tile);
                    }
                    var separate=scene.Objects.Where(o=>o.Kind!=SceneObjectKind.Player && o.ProjectionEnabled && o.Bounds==bounds).ToArray();
                    if(separate.Length!=1)
                        throw new InvalidOperationException(sample.Area+": exact separate sprite bounds required; expected="+bounds+
                            "; overlapping="+JsonSerializer.Serialize(scene.Objects.Where(o=>o.ProjectionEnabled && o.Bounds.IntersectsWith(bounds))
                                .Select(o=>new{o.Kind,o.Label,o.Bounds,o.IdentityKey})));
                    // Reconstruct the reviewed native OAM image independently;
                    // bounds alone could accept a merged/missing weapon shape.
                    VerifyReviewedBody(frame,separate,weaponIndices,sample.Area+": separate weapon");
                    separateSpriteChecked=true;
                }
                int x=frame.Ram[0x38c], y=frame.Ram[0x354];
                int height=frame.LargeSprites?16:8;
                bool visibleBody=Enumerable.Range(0,64).Any(i=>(frame.Oam[i*4+2]&3)==0 && frame.Oam[i*4]<239 &&
                    new Rectangle(frame.Oam[i*4+3],frame.Oam[i*4]+1,8,height).Contains(x,y));
                if(sample.Negative)
                {
                    if(scene.Objects.Count!=0)throw new InvalidOperationException(sample.Area+": native interface must have no projected objects.");
                    if(frame.NativeScreenPixels?.Length!=61440)throw new InvalidOperationException("Native interface pixels required.");
                    for(int py=0;py<240;py++)for(int px=0;px<256;px++)
                        if(scene.Background.GetPixel(px,py).ToArgb()!=frame.NativeScreenPixels[py*256+px])
                            throw new InvalidOperationException(sample.Area+": native interface pixels changed.");
                }
                else if(profile.IsActive(frame))
                {
                    // The object center can lie in a gap between body tiles in
                    // attack poses. A center-tile hit proves presence; no hit
                    // does not prove absence. Interfaces are tested separately.
                    if(actors.Length>1 || (visibleBody && actors.Length!=1) ||
                        actors.Any(a=>a.Label!="Simon Belmont" || !a.Bounds.Contains(x,y)))
                        throw new InvalidOperationException(sample.Area+": visible Simon must be one named player at native object coordinates. Nearby: "+
                            JsonSerializer.Serialize(scene.Objects.Where(o=>o.Bounds.Contains(x,y)).Select(o=>new{o.Kind,o.Label,o.Bounds})));
                    if(actors.Any(a=>!profile.AllowsTrackedPlayer(a.Bounds,frame)))throw new InvalidOperationException("Protected interface must not become Simon.");
                }
                var clone=profile.Clone().PlayerTracking!;
                if(clone.XAddress!=908 || clone.YAddress!=852 || clone.SpritePalette!=0)throw new InvalidOperationException("Clone lost Simon tracking.");
                var roundtrip=JsonSerializer.Deserialize<GameRecognitionProfile>(JsonSerializer.Serialize(profile))!;
                roundtrip.Normalize();
                if(roundtrip.FormatVersion!=(profile.PlayerTracking?.BodyAssembly is not null?21:profile.SpriteAssemblies is not null?20:profile.PlayerOverlaysFlatHud?15:8) || roundtrip.PlayerOverlaysFlatHud!=profile.PlayerOverlaysFlatHud || roundtrip.PlayerLabel!="Simon Belmont" || roundtrip.PlayerTracking?.XAddress!=908 || roundtrip.PlayerTracking?.YAddress!=852)
                    throw new InvalidOperationException("Save/import lost Simon identity.");
                if(tracking.Matches(frame with{Ram=[]},new Rectangle(0,0,8,8),[0]))throw new InvalidOperationException("Missing RAM must not identify Simon.");
                byte[] enemy=(byte[])frame.Oam.Clone();enemy[2]=3;
                if(tracking.Matches(frame with{Oam=enemy},new Rectangle(x-4,y-4,8,8),[0]))throw new InvalidOperationException("Other palette at OAM0 must not identify Simon.");
                results.Add(new{sample.Area,sample.Negative,CenterTilePresent=visibleBody,X=x,Y=y,WhipLength=(int)frame.Ram[112],Action=(int)frame.Ram[1132],Pose=(int)frame.Ram[1216],Actors=actors.Length,ActorWidth=actors.FirstOrDefault()?.Bounds.Width??0,ActorHeight=actors.FirstOrDefault()?.Bounds.Height??0,SeparateSpriteChecked=separateSpriteChecked,Passed=true});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=results.Count,ArchiveCount=frameReader.ArchiveCount,ArchivedReads=frameReader.ArchivedReads,Scope="Named native fixtures plus tracking/clone/import/negative checks, not exhaustive Simon animations or full-game identity.",Results=results},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static void VerifyLayoutGuards(NesFrame frame,GameRecognitionProfile profile,JsonElement indices,string area)
    {
        var tracking=profile.PlayerTracking!;
        int[] expected=indices.EnumerateArray().Select(v=>v.GetInt32()).Order().ToArray();
        void Require(NesFrame variant,int[] slots)
        {
            if(!tracking.TryGetBodySlots(variant,out var actual)||!actual.SequenceEqual(slots))
                throw new InvalidOperationException(area+": complete layout ownership guard failed.");
        }
        void Reject(NesFrame variant)
        {
            if(tracking.TryGetBodySlots(variant,out _))throw new InvalidOperationException(area+": incomplete/ambiguous layout claimed.");
        }
        Require(frame,expected);
        byte[] shuffled=new byte[256];
        for(int i=0;i<64;i++)Array.Copy(frame.Oam,i*4,shuffled,(63-i)*4,4);
        Require(frame with{Oam=shuffled},expected.Select(i=>63-i).Order().ToArray());
        for(int palette=0;palette<4;palette++)
        {
            byte[] colors=(byte[])frame.Oam.Clone();
            foreach(int slot in expected)colors[slot*4+2]=(byte)((colors[slot*4+2]&252)|palette);
            Require(frame with{Oam=colors},expected);
            if(!tracking.TryGetPalette(frame with{Oam=colors},out int ownedPalette)||ownedPalette!=palette)
                throw new InvalidOperationException(area+": palette was not read from published owner.");
        }
        byte[] missing=(byte[])frame.Oam.Clone();missing[expected[0]*4]=244;
        Reject(frame with{Oam=missing});
        byte[] mixed=(byte[])frame.Oam.Clone();mixed[expected[0]*4+2]^=1;
        Reject(frame with{Oam=mixed});
        int[] unused=Enumerable.Range(0,64).Where(i=>frame.Oam[i*4]>=239).Take(expected.Length).ToArray();
        if(unused.Length!=expected.Length)throw new InvalidOperationException("Guard requires unused native slots.");
        byte[] duplicate=(byte[])frame.Oam.Clone();Array.Copy(frame.Oam,expected[0]*4,duplicate,unused[0]*4,4);
        Reject(frame with{Oam=duplicate});
        byte[] competing=(byte[])frame.Oam.Clone();
        for(int i=0;i<expected.Length;i++)
        {
            Array.Copy(frame.Oam,expected[i]*4,competing,unused[i]*4,4);
            competing[unused[i]*4+3]++;
        }
        Reject(frame with{Oam=competing});
        Reject(frame with{Ram=[]});
        byte[] displaced=(byte[])frame.Ram.Clone();displaced[tracking.XAddress]+=100;
        Reject(frame with{Ram=displaced});
        var clone=profile.Clone();clone.PlayerTracking!.BodyAssembly!.Poses[0][0].Tile^=1;
        if(clone.PlayerTracking.BodyAssembly.Poses[0][0].Tile==tracking.BodyAssembly!.Poses[0][0].Tile)
            throw new InvalidOperationException("Clone shares player layout.");
        var imported=JsonSerializer.Deserialize<GameRecognitionProfile>(JsonSerializer.Serialize(profile))!;imported.Normalize();
        if(imported.FormatVersion!=21||!imported.PlayerTracking!.TryGetBodySlots(frame,out var saved)||!saved.SequenceEqual(expected))
            throw new InvalidOperationException("Import lost complete player ownership.");
    }
    private static void VerifyReviewedBody(NesFrame frame,SceneObject[] actors,JsonElement indices,string area)
    {
        int[] owned=indices.EnumerateArray().Select(v=>v.GetInt32()).ToArray();Rectangle? bounds=null;
        if(owned.Length==0||owned.Distinct().Count()!=owned.Length)throw new InvalidDataException("Nonempty unique reviewed body slots required.");
        foreach(int index in owned)
        {
            if(index<0||index>=64||index*4+3>=frame.Oam.Length)throw new InvalidDataException("Invalid reviewed OAM slot.");
            int offset=index*4;if(frame.Oam[offset]>=239)continue;
            var tile=new Rectangle(frame.Oam[offset+3],frame.Oam[offset]+1,8,frame.LargeSprites?16:8);
            bounds=bounds is Rectangle prior?Rectangle.Union(prior,tile):tile;
        }
        if(bounds is not Rectangle body||actors.Length!=1||actors[0].Bounds!=body)
            throw new InvalidOperationException(area+": exact reviewed body bounds required.");
        using Bitmap expected=new(body.Width,body.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(expected))
        {
            graphics.Clear(Color.Transparent);
            foreach(int index in owned.OrderDescending())
            {
                int offset=index*4;if(frame.Oam[offset]>=239)continue;
                using var tile=SmbProfile.DecodeSpriteTile(frame.Chr,frame.Palette,frame.Oam[offset+1],frame.Oam[offset+2],frame.SpritePatternBase,frame.LargeSprites);
                graphics.DrawImageUnscaled(tile,frame.Oam[offset+3]-body.X,frame.Oam[offset]+1-body.Y);
            }
        }
        for(int y=0;y<body.Height;y++)for(int x=0;x<body.Width;x++)
            if(expected.GetPixel(x,y).ToArgb()!=actors[0].Image.GetPixel(x,y).ToArgb())
                throw new InvalidOperationException(area+": projected player contains missing/foreign native pixels.");
    }
}
