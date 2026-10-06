using Warp4D.Emulation;

namespace Warp4D.Profiles;

// Complete native tile layouts. No ROM/path dependence or current-pose RAM
// assumption: the published OAM may legitimately contain the preceding pose.
internal sealed class SpriteAssembly
{
    public string Name { get; set; } = "Sprite assembly";
    public Dictionary<int,int> ExpectedRam { get; set; } = [];
    public List<List<SpriteAssemblyTile>> Poses { get; set; } = [];
    public SpriteAssembly Clone() => new() { Name=Name,ExpectedRam=new(ExpectedRam),
        Poses=Poses.Select(p=>p.Select(t=>new SpriteAssemblyTile{Tile=t.Tile,Attributes=t.Attributes,X=t.X,Y=t.Y}).ToList()).ToList() };
    public void Validate(bool allowSingleTile=false)
    {
        if(string.IsNullOrWhiteSpace(Name)||Name.Length>128||ExpectedRam is null||ExpectedRam.Any(p=>p.Key is <0 or >=2048||p.Value is <0 or >255)||
           Poses is not {Count:>0 and <=256}||Poses.Any(p=>p is null||p.Count<(allowSingleTile?1:2)||p.Count>64||
               p.Any(t=>t is null||t.Tile is <0 or >255||t.Attributes is <0 or >255||t.X is <-128 or >127||t.Y is <-128 or >127)||
               p.Select(t=>(t.Tile,t.Attributes,t.X,t.Y)).Distinct().Count()!=p.Count))
            throw new InvalidDataException("Sprite assemblies require bounded complete distinct native tile layouts.");
    }
    internal IEnumerable<int[]> Match(NesFrame frame)
    {
        if(frame.Oam.Length<256||ExpectedRam.Any(p=>p.Key>=frame.Ram.Length||frame.Ram[p.Key]!=p.Value))yield break;
        Dictionary<(int Tile,int Attr,int X,int Y),List<int>> lookup=[];
        for(int i=0;i<64;i++)
        {
            int o=i*4;if(frame.Oam[o]>=239)continue;
            var key=((int)frame.Oam[o+1],(int)frame.Oam[o+2],(int)frame.Oam[o+3],(int)frame.Oam[o]);
            if(!lookup.TryGetValue(key,out var slots))lookup[key]=slots=[];
            slots.Add(i);
        }
        HashSet<string> seen=[];
        foreach(var pose in Poses)
        for(int seed=0;seed<64;seed++)
        {
            int o=seed*4;var first=pose[0];
            if(frame.Oam[o]>=239||frame.Oam[o+1]!=first.Tile||frame.Oam[o+2]!=first.Attributes)continue;
            int x=frame.Oam[o+3]-first.X,y=frame.Oam[o]-first.Y;
            List<int> matched=[];bool complete=true;
            foreach(var tile in pose)
            {
                // A duplicate native tuple is ambiguous: never select whichever
                // actor happened to be earlier in the shuffled OAM list.
                if(!lookup.TryGetValue((tile.Tile,tile.Attributes,(x+tile.X)&255,(y+tile.Y)&255),out var slots)||slots.Count!=1||matched.Contains(slots[0]))
                {complete=false;break;}
                matched.Add(slots[0]);
            }
            if(!complete)continue;
            int[] owned=matched.Order().ToArray();
            if(seen.Add(string.Join(',',owned)))yield return owned;
        }
    }
}

internal sealed class SpriteAssemblyTile
{
    public int Tile { get; set; }
    public int Attributes { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}
