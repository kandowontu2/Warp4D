using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class TetrisProfileTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        List<object> results=[];
        bool stateProtection=false;
        try
        {
            var samples=JsonSerializer.Deserialize<ProfileCoverageTests.Sample[]>(File.ReadAllText(manifest))!;
            foreach(var sample in samples)
            {
                if(sample.Id!="tetris")throw new InvalidDataException("Explicit Tetris fixtures required.");
                var frame=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(sample.Frame))!;
                var profile=GameRecognitionProfileStore.ReadFromFile(sample.Profile);
                if(!sample.Negative&&!stateProtection)
                {
                    // Goal-check state 6 is also visited after ordinary B-Type
                    // locks. Protect it only when the remaining lines are zero.
                    byte[] ram=(byte[])frame.Ram.Clone();
                    ram[189]=3;ram[193]=1;ram[104]=6;ram[112]=1;
                    if(!profile.IsActive(frame with{Ram=ram}))throw new InvalidOperationException("Ordinary B-Type goal check must not flicker inactive.");
                    ram[112]=0;
                    if(profile.IsActive(frame with{Ram=ram}))throw new InvalidOperationException("B-Type success must be inactive.");
                    ram[193]=0;ram[104]=10;
                    if(profile.IsActive(frame with{Ram=ram}))throw new InvalidOperationException("Native game-over curtain must be inactive.");
                    stateProtection=true;
                }
                using var scene=new SmbProfile().Build(frame,false,null,profile);
                if(sample.Negative)
                {
                    if(scene.Objects.Count!=0)throw new InvalidOperationException(sample.Area+": inactive interface must have no projections.");
                    for(int y=0;y<240;y++)for(int x=0;x<256;x++)
                        if(scene.Background.GetPixel(x,y).ToArgb()!=frame.NativeScreenPixels![y*256+x])
                            throw new InvalidOperationException(sample.Area+": inactive native screen must remain exact.");
                }
                else
                {
                    Rectangle piece=Rectangle.Empty;
                    for(int index=0;index<4;index++)
                    {
                        int offset=index*4,y=frame.Oam[offset]+1,x=frame.Oam[offset+3];
                        if(y>=240||frame.Oam[offset+1] is not (0x7b or 0x7c or 0x7d))continue;
                        Rectangle tile=new(x,y,8,8);piece=piece.IsEmpty?tile:Rectangle.Union(piece,tile);
                    }
                    var actors=scene.Objects.Where(o=>o.ProjectionEnabled&&o.Kind==SceneObjectKind.Player).ToArray();
                    if(!piece.IsEmpty&&profile.Allows(piece,frame))
                    {
                        if(actors.Length!=1||actors[0].Bounds!=piece||actors[0].Label!="Falling tetromino")
                            throw new InvalidOperationException(sample.Area+": declared piece slots must form one correctly bounded named actor.");
                    }
                    if(scene.Objects.Any(o=>o.ProjectionEnabled&&!new Rectangle(96,48,80,160).Contains(o.Bounds)))
                        throw new InvalidOperationException(sample.Area+": next-piece/statistics/interface must remain flat.");
                }
                results.Add(new{sample.Area,sample.Negative,Actors=scene.Objects.Count(o=>o.ProjectionEnabled&&o.Kind==SceneObjectKind.Player),Passed=true});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Scope="Named fixture actor identity, bounds and interface preservation; not whole-game completion.",StateProtection=stateProtection,Cases=results.Count,Results=results},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception exception){File.WriteAllText(Path.Combine(output,"error.txt"),exception.ToString());return 1;}
    }
}
