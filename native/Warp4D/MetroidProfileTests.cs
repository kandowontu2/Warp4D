using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class MetroidProfileTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        List<object> results=[];bool trackingChecks=false;
        try
        {
            var samples=JsonSerializer.Deserialize<ProfileCoverageTests.Sample[]>(File.ReadAllText(manifest))!;
            using var expectations=JsonDocument.Parse(File.ReadAllText(manifest));
            using ArchiveFrameReader frameReader=new();
            int sampleIndex=0;
            foreach(var sample in samples)
            {
                var fixture=expectations.RootElement[sampleIndex++];
                if(sample.Id!="metroid")throw new InvalidDataException("Explicit Metroid fixtures required.");
                var frame=CartridgeViewport.NormalizeCapture(sample.Id,frameReader.Read(sample.Frame,sample.Archive,sample.ArchiveSHA256,sample.FrameSHA256));
                if(sample.RequireCoherentNativeFrame && (frame.CaptureScanline!=96||frame.NativeScreenSequence!=frame.Sequence||frame.NativeScreenPixels?.Length!=61440))
                    throw new InvalidDataException(sample.Area+": coherent paired native frame required.");
                foreach(var assertion in sample.ExpectedRam)
                    if(assertion.Key<0||assertion.Key>=frame.Ram.Length||frame.Ram[assertion.Key]!=assertion.Value)
                        throw new InvalidDataException(sample.Area+": native RAM assertion failed at "+assertion.Key);
                var profile=GameRecognitionProfileStore.ReadFromFile(sample.Profile);
                using var scene=new SmbProfile().Build(frame,false,null,profile);
                int reviewedBodyPixels=0,reviewedProjectilePixels=0;
                if(sample.Negative)
                {
                    if(scene.Objects.Count!=0)throw new InvalidOperationException(sample.Area+": native interface must have no projections.");
                    for(int y=0;y<240;y++)for(int x=0;x<256;x++)
                        if(scene.Background.GetPixel(x,y).ToArgb()!=frame.NativeScreenPixels![y*256+x])
                            throw new InvalidOperationException(sample.Area+": native interface changed.");
                }
                else
                {
                    var tracking=profile.PlayerTracking??throw new InvalidOperationException("Screen-coordinate player tracking required.");
                    // Independent formula from native DrawFrame ObjectX/Y
                    // and scroll, not the movement-only $51/$52 cache.
                    int x=(frame.Ram[782]-frame.Ram[253])&255,y=(frame.Ram[781]-frame.Ram[252])&255;
                    // Native IsObjectVisible DFE5/E004 subtracts another16
                    // after byte subtraction for a visible object on the next
                    // vertical nametable. The PPU wraps240 rows, not256.
                    if((frame.Ram[73]&2)==0 && ((frame.Ram[780]^frame.Ram[255])&1)!=0 && frame.Ram[781]<frame.Ram[252])
                        y=(y-16)&255;
                    if(!tracking.TryGetScreenPosition(frame,out var actualAnchor) || actualAnchor!=new Point(x,y))
                        throw new InvalidOperationException(sample.Area+": native anchor expected "+x+","+y+" actual "+actualAnchor.X+","+actualAnchor.Y);
                    bool visibleBody=Enumerable.Range(0,64).Any(i=>(frame.Oam[i*4+2]&3)==0 &&
                        new Rectangle(frame.Oam[i*4+3],frame.Oam[i*4]+1,8,8).Contains(x,y));
                    // Reviewed native entry fixtures explicitly show Samus.
                    // Do not silently waive identity checks for stale caches.
                    if(sample.Area.EndsWith("-entry.frame",StringComparison.Ordinal) && !visibleBody)
                        throw new InvalidOperationException(sample.Area+": reviewed entrance must contain Samus body at native object-minus-scroll coordinates.");
                    var actors=scene.Objects.Where(o=>o.ProjectionEnabled&&o.Kind==SceneObjectKind.Player).ToArray();
                    if(fixture.TryGetProperty("ExpectedPlayerOamIndices",out var bodyIndices))
                        reviewedBodyPixels=ReviewedSpriteBitmapTests.Verify(frame,actors,bodyIndices,sample.Area);
                    if(fixture.TryGetProperty("ExpectedSeparateSpriteOamIndices",out var projectileIndices))
                    {
                        if(projectileIndices.GetArrayLength()==0)throw new InvalidDataException("Nonempty projectile slots required.");
                        int projectileIndex=projectileIndices[0].GetInt32();
                        if(projectileIndex<0||projectileIndex>=64)throw new InvalidDataException("Invalid projectile slot.");
                        var projectileBounds=new Rectangle(frame.Oam[projectileIndex*4+3],frame.Oam[projectileIndex*4]+1,8,frame.LargeSprites?16:8);
                        var projectile=scene.Objects.Where(o=>o.ProjectionEnabled&&o.Kind!=SceneObjectKind.Player&&o.Bounds==projectileBounds).ToArray();
                        reviewedProjectilePixels=ReviewedSpriteBitmapTests.Verify(frame,projectile,projectileIndices,sample.Area+" projectile");
                    }
                    if(fixture.TryGetProperty("ExpectedPlayerCount",out var expectedCount) && actors.Length!=expectedCount.GetInt32())
                        throw new InvalidOperationException(sample.Area+": reviewed native fixture requires "+expectedCount.GetInt32()+" Samus actor(s), actual="+actors.Length);
                    if(actors.Length>1 || (visibleBody&&(actors.Length!=1||actors[0].Label!="Samus"||!actors[0].Bounds.Contains(x,y))))
                        throw new InvalidOperationException(sample.Area+": visible Samus must be one named actor at her native screen coordinates.");
                    if(actors.Any(a=>profile.Protects(a.Bounds) && !profile.PlayerOverlaysFlatHud))throw new InvalidOperationException("HUD must not be a player without explicit tracked-body overlay.");
                    if(scene.PlayerOverlaysFlatHud!=profile.PlayerOverlaysFlatHud || scene.Clone() is not {} sceneClone)
                        throw new InvalidOperationException("Scene lost HUD layer policy.");
                    using(sceneClone){if(sceneClone.PlayerOverlaysFlatHud!=scene.PlayerOverlaysFlatHud)throw new InvalidOperationException("Scene clone lost HUD layer policy.");}
                    if(!trackingChecks)
                    {
                        if(tracking.BodyTilesByAnimation is not null)
                        {
                            if(!tracking.TryGetBodyRange(frame,out int bodyFirst,out int bodyCount))throw new InvalidOperationException("Reviewed player animation range required.");
                            var cloned=profile.Clone();
                            if(JsonSerializer.Serialize(cloned.PlayerTracking?.BodyTilesByAnimation)!=JsonSerializer.Serialize(tracking.BodyTilesByAnimation)||
                               ReferenceEquals(cloned.PlayerTracking!.BodyTilesByAnimation,tracking.BodyTilesByAnimation)||
                               ReferenceEquals(cloned.PlayerTracking.BodyTilesByAnimation![frame.Ram[771]],tracking.BodyTilesByAnimation[frame.Ram[771]]))
                                throw new InvalidOperationException("Animation ownership clone must be independent and exact.");
                            byte[] ambiguous=new byte[256];for(int i=0;i<64;i++)ambiguous[i*4]=244;
                            Array.Copy(frame.Oam,bodyFirst*4,ambiguous,0,bodyCount*4);
                            Array.Copy(frame.Oam,bodyFirst*4,ambiguous,32*4,bodyCount*4);
                            if(tracking.TryGetBodyRange(frame with{Oam=ambiguous},out _,out _))throw new InvalidOperationException("Ambiguous animation ownership must be rejected.");
                            ambiguous[32*4+1]^=1;
                            if(!tracking.TryGetBodyRange(frame with{Oam=ambiguous},out int uniqueFirst,out _)||uniqueFirst!=0)throw new InvalidOperationException("Unique dynamic animation range required.");
                            ambiguous[1]^=1;
                            if(tracking.TryGetBodyRange(frame with{Oam=ambiguous},out _,out _))throw new InvalidOperationException("Wrong tile sequence must not become Samus.");
                            var invalid=profile.Clone();invalid.PlayerTracking!.BodyTilesByAnimation![0]=[256];
                            bool rejected=false;try{invalid.Normalize();}catch(InvalidDataException){rejected=true;}
                            if(!rejected)throw new InvalidOperationException("Invalid animation ownership tile accepted.");
                        }
                        if(profile.Clone().PlayerTracking is not {} clone || clone.XAddress!=782||clone.YAddress!=781||clone.SpritePalette!=0 || clone.XScrollAddress!=253 || clone.YScrollAddress!=252)
                            throw new InvalidOperationException("Cloning must preserve explicit tracking.");
                        byte[] oam=new byte[256];for(int i=0;i<64;i++)oam[i*4]=244;
                        oam[0]=(byte)Math.Clamp(y-4,0,239);oam[3]=(byte)Math.Clamp(x-4,0,248);oam[2]=2;
                        if(tracking.Matches(frame with{Oam=oam},new(x-4,y-4,8,8),[0]))
                            throw new InvalidOperationException("Enemy palette at OAM zero must not be Samus.");
                        if(tracking.Matches(frame with{Ram=[]},new(x-4,y-4,8,8),[0]))
                            throw new InvalidOperationException("Missing coordinates must not match.");
                        byte[] wrapped=(byte[])frame.Ram.Clone();wrapped[782]=3;wrapped[253]=250;wrapped[781]=5;wrapped[252]=252;
                        wrapped[81]=0;wrapped[82]=0;
                        int wrappedExpectedY=9;
                        if(tracking.VerticalWrap is not null && (wrapped[73]&2)==0 && ((wrapped[780]^wrapped[255])&1)!=0)
                            wrappedExpectedY=(wrappedExpectedY-16)&255;
                        if(!tracking.TryGetScreenPosition(frame with{Ram=wrapped},out var wrappedPoint) || wrappedPoint!=new Point(9,wrappedExpectedY))
                            throw new InvalidOperationException("Room-minus-scroll wraps and does not use stale movement caches.");
                        var roundtrip=JsonSerializer.Deserialize<GameRecognitionProfile>(JsonSerializer.Serialize(profile))!;
                        roundtrip.Normalize();
                        if(roundtrip.FormatVersion!=(tracking.BodyTilesByAnimation is not null?17:tracking.VerticalWrap is not null?16:profile.PlayerOverlaysFlatHud?15:9) || roundtrip.PlayerOverlaysFlatHud!=profile.PlayerOverlaysFlatHud || roundtrip.PlayerTracking?.XScrollAddress!=253 || roundtrip.PlayerTracking?.YScrollAddress!=252 ||
                            JsonSerializer.Serialize(roundtrip.PlayerTracking?.BodyTilesByAnimation)!=JsonSerializer.Serialize(tracking.BodyTilesByAnimation)||
                            JsonSerializer.Serialize(roundtrip.PlayerTracking?.VerticalWrap)!=JsonSerializer.Serialize(tracking.VerticalWrap))
                            throw new InvalidOperationException("Save/import must preserve scroll-aware tracking and HUD policy/version.");
                        if(JsonSerializer.Serialize(profile.Clone().PlayerTracking?.VerticalWrap)!=JsonSerializer.Serialize(tracking.VerticalWrap) ||
                            tracking.VerticalWrap is not null && ReferenceEquals(profile.Clone().PlayerTracking?.VerticalWrap,tracking.VerticalWrap))
                            throw new InvalidOperationException("Profile clone must independently preserve native vertical wrapping.");
                        if(profile.Clone().PlayerOverlaysFlatHud!=profile.PlayerOverlaysFlatHud)throw new InvalidOperationException("Profile clone lost HUD policy.");
                        var oldPolicy=profile.Clone();oldPolicy.PlayerOverlaysFlatHud=false;
                        if(oldPolicy.AllowsTrackedPlayer(new(24,24,8,8),frame))throw new InvalidOperationException("Legacy protection must still win without opt-in.");
                        var legacy=new PlayerSpriteTracking{XAddress=81,YAddress=82,SpritePalette=0};
                        if(!legacy.TryGetScreenPosition(frame,out var legacyPoint) || legacyPoint!=new Point(frame.Ram[81],frame.Ram[82]) ||
                            JsonSerializer.Serialize(legacy).Contains("ScrollAddress"))
                            throw new InvalidOperationException("Existing screen-coordinate declarations remain unchanged.");
                        byte[] ram=(byte[])frame.Ram.Clone();ram[29]=1;
                        if(profile.IsActive(frame with{Ram=ram}))throw new InvalidOperationException("Title state must be inactive even with retained gameplay artwork.");
                        trackingChecks=true;
                    }
                }
                results.Add(new{sample.Area,sample.Negative,Actors=scene.Objects.Count(o=>o.ProjectionEnabled&&o.Kind==SceneObjectKind.Player),ReviewedBodyPixels=reviewedBodyPixels,ReviewedProjectilePixels=reviewedProjectilePixels,Passed=true});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=results.Count,TrackingChecks=trackingChecks,Scope="Named Metroid fixtures, dynamic player identity and native interface preservation; not full-game coverage.",Results=results},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception exception){File.WriteAllText(Path.Combine(output,"error.txt"),exception.ToString());return 1;}
    }
}
