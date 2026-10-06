using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class IcarusChamberTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        List<object> results=[];
        try
        {
            var samples=JsonSerializer.Deserialize<ProfileCoverageTests.Sample[]>(File.ReadAllText(manifest))!;
            foreach(var sample in samples)
            {
                if(sample.Id!="icarus")throw new InvalidDataException("Explicit Kid Icarus fixtures required.");
                var profile=GameRecognitionProfileStore.ReadFromFile(sample.Profile);
                var frame=CartridgeViewport.NormalizeCapture("icarus",JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(sample.Frame))!);
                var regions=profile.ConditionalFlatRegions!.Where(r=>r.ExpectedRam.ContainsKey(59)).ToArray();
                if(profile.FormatVersion<10 || regions.Length!=3 || regions.Any(r=>r.ExcludedRam?.GetValueOrDefault(58,-1)!=0))
                    throw new InvalidOperationException("All three chamber text guards require the native nonzero chamber state.");
                var text=new Rectangle(64,48,16,16);
                if(frame.Ram[160]==2 && frame.Ram[59] is 35 or 37 or 38)
                {
                    bool expectedAllowed=frame.Ram[58]==0;
                    if(profile.Allows(text,frame)!=expectedAllowed)throw new InvalidOperationException(sample.Area+": stale room text protection.");
                }
                var cloned=profile.Clone();
                var imported=JsonSerializer.Deserialize<GameRecognitionProfile>(JsonSerializer.Serialize(profile))!;imported.Normalize();
                if(JsonSerializer.Serialize(cloned.ConditionalFlatRegions)!=JsonSerializer.Serialize(profile.ConditionalFlatRegions) ||
                   JsonSerializer.Serialize(imported.ConditionalFlatRegions)!=JsonSerializer.Serialize(profile.ConditionalFlatRegions))
                    throw new InvalidOperationException("Clone/import lost exclusions.");
                foreach(var region in regions)
                {
                    if(region.Matches(frame with{Ram=[]}))throw new InvalidOperationException("Missing RAM activates guard.");
                    byte[] ram=(byte[])frame.Ram.Clone();ram[160]=2;ram[59]=(byte)region.ExpectedRam[59];
                    ram[58]=0;if(region.Matches(frame with{Ram=ram}))throw new InvalidOperationException("Returned field protected.");
                    ram[58]=8;if(!region.Matches(frame with{Ram=ram}))throw new InvalidOperationException("Native chamber unprotected.");
                    if(region.ExpectedRam[59] is 37 or 38)
                    {
                        // The native price glyphs finish above Y128. The old
                        // Y136 guard also flattened Pit's head during a jump.
                        var shopFrame=frame with{Ram=ram};
                        if(profile.Allows(new Rectangle(112,112,8,8),shopFrame) ||
                           !profile.Allows(new Rectangle(104,128,8,8),shopFrame))
                            throw new InvalidOperationException("Shop prices must stay flat without protecting the item/player sprite row.");
                        // Actual funded item contact reaches the previously
                        // protected blank rows120..127. Keep the glyph rows
                        //112..119 flat, without flattening the owned actor.
                        if(!profile.Allows(new Rectangle(112,120,16,24),shopFrame))
                            throw new InvalidOperationException("Shop purchase-contact player rows must remain projectable.");
                    }
                }
                if(sample.Negative)
                {
                    using var scene=new SmbProfile().Build(frame,false,null,profile);
                    if(scene.Objects.Count!=0 || frame.NativeScreenPixels?.Length!=61440)throw new InvalidOperationException("Blank native transition must stay flat.");
                    for(int y=0;y<240;y++)for(int x=0;x<256;x++)if(scene.Background.GetPixel(x,y).ToArgb()!=frame.NativeScreenPixels[y*256+x])
                        throw new InvalidOperationException("Blank transition pixels changed.");
                }
                results.Add(new{sample.Area,sample.Negative,Room=(int)frame.Ram[59],ChamberState=(int)frame.Ram[58],Passed=true});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=results.Count,Scope="Chamber-state exclusion, clone/import and native blank transition; not scenery coverage or challenge completion.",Results=results},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
