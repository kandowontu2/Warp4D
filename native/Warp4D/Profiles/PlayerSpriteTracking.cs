using Warp4D.Emulation;
using System.Text.Json.Serialization;

namespace Warp4D.Profiles;

internal sealed class PlayerSpriteTracking
{
    public int XAddress { get; set; } = -1;
    public int YAddress { get; set; } = -1;
    public int SpritePalette { get; set; } = -1;
    public int MaximumWidth { get; set; } = 64;
    public int MaximumHeight { get; set; } = 64;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PositionTolerance { get; set; }

    // Optional room-to-screen conversion for games whose cached screen
    // position is stale during initialization. RAM bytes wrap at256; a
    // declared native nametable context can correct vertical PPU-page seams.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? XScrollAddress { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? YScrollAddress { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VerticalNametableWrap? VerticalWrap { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PaletteStateAddress { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<int,int>? PalettesByState { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<int,int>? PaletteOamByState { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<int,int>? BodyOamCountByState { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<int,List<int>>? BodyExtraOamIndicesByState { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<int,List<int>>? BodyTilesByAnimation { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SpriteAssembly? BodyAssembly { get; set; }

    internal bool TryGetBodySlots(NesFrame frame,out int[] slots)
    {
        slots=[];
        if(BodyAssembly is null||!TryGetScreenPosition(frame,out var anchor))return false;
        foreach(var candidate in BodyAssembly.Match(frame))
        {
            Rectangle? bounds=null;
            foreach(int index in candidate)
            {
                int offset=index*4,delta=((frame.Oam[offset+3]-anchor.X+128)&255)-128;
                Rectangle tile=new(anchor.X+delta,frame.Oam[offset]+1,8,frame.LargeSprites?16:8);
                bounds=bounds is Rectangle prior?Rectangle.Union(prior,tile):tile;
            }
            if(bounds is not Rectangle body||body.Width>MaximumWidth||body.Height>MaximumHeight||!body.Contains(anchor))continue;
            if(slots.Length>0&&!slots.SequenceEqual(candidate)){slots=[];return false;}
            slots=candidate;
        }
        return slots.Length>0;
    }

    internal bool TryGetBodyRange(NesFrame frame,out int first,out int count)
    {
        first=count=0;
        if(BodyTilesByAnimation is {} frames)
        {
            if(frame.Oam.Length<256||!TryGetScreenPosition(frame,out var anchor))return false;
            int match=-1,matchCount=0;
            // Completed OAM can still contain the prior pose after the engine
            // has advanced AnimFrame or entered the F7 blink state. Require a complete declared native pose,
            // not merely a tile/palette near the coordinate. Alias sequences
            // naming the same range are one match; different ranges are ambiguous.
            foreach(var pose in frames.Values)
            for(int start=0;start+pose.Count<=64;start++)
            {
                Rectangle? bounds=null;bool equal=true;
                int palette=frame.Oam[start*4+2]&3;
                for(int tile=0;tile<pose.Count;tile++)
                {
                    int offset=(start+tile)*4;
                    if(frame.Oam[offset+1]!=pose[tile]||(frame.Oam[offset+2]&3)!=palette){equal=false;break;}
                    if(frame.Oam[offset]>=239)continue;
                    int delta=((frame.Oam[offset+3]-anchor.X+128)&255)-128;
                    var rectangle=new Rectangle(anchor.X+delta,frame.Oam[offset]+1,8,frame.LargeSprites?16:8);
                    bounds=bounds is Rectangle prior?Rectangle.Union(prior,rectangle):rectangle;
                }
                if(!equal||bounds is not Rectangle body||body.Width>MaximumWidth||body.Height>MaximumHeight||!body.Contains(anchor))continue;
                if(match>=0&&(match!=start||matchCount!=pose.Count))return false;
                match=start;matchCount=pose.Count;
            }
            if(match<0)return false;
            first=match;count=matchCount;return true;
        }
        if(PaletteStateAddress is not int state||state<0||state>=frame.Ram.Length||
           PaletteOamByState?.TryGetValue(frame.Ram[state],out first)!=true||
           BodyOamCountByState?.TryGetValue(frame.Ram[state],out count)!=true)return false;
        return first>=0&&count is >=1 and <=16&&first+count<=64&&frame.Oam.Length>=(first+count)*4&&
            (BodyExtraOamIndicesByState?.TryGetValue(frame.Ram[state],out var extras)!=true||extras is not null&&extras.All(i=>i>=0&&i<64&&frame.Oam.Length>=(i+1)*4));
    }

    internal bool OwnsBodySlot(NesFrame frame,int index) =>
        TryGetBodyRange(frame,out int first,out int count)&&
        (index>=first&&index<first+count||BodyExtraOamIndicesByState?.TryGetValue(frame.Ram[PaletteStateAddress!.Value],out var extras)==true&&extras is not null&&extras.Contains(index));

    internal Rectangle UnwrapBodyTile(NesFrame frame, Rectangle bounds,bool rangeVerified=false)
    {
        // Native X/OAM coordinates are bytes. At the horizontal seam one
        // animation half can be at255 while the other is at7. Keep explicitly
        // owned body geometry contiguous around its native anchor, never
        // extending this operation to nearby items or generic sprite clusters.
        if(!rangeVerified&&!TryGetBodyRange(frame,out _,out _)||!TryGetScreenPosition(frame,out var point))return bounds;
        int delta=((bounds.X-point.X+128)&255)-128;
        return new Rectangle(point.X+delta,bounds.Y,bounds.Width,bounds.Height);
    }

    internal bool TryGetPalette(NesFrame frame,out int palette)
    {
        palette=SpritePalette;
        if(BodyAssembly is not null)
        {
            if(!TryGetBodySlots(frame,out var slots))return false;
            palette=frame.Oam[slots[0]*4+2]&3;return true;
        }
        if(BodyTilesByAnimation is not null)
        {
            if(!TryGetBodyRange(frame,out int first,out _))return false;
            palette=frame.Oam[first*4+2]&3;return true;
        }
        if(PaletteStateAddress is not int address)return true;
        if(address<0||address>=frame.Ram.Length)return false;
        if(PaletteOamByState?.TryGetValue(frame.Ram[address],out int index)==true)
        {
            int offset=index*4+2;
            if(index<0||index>=64||offset>=frame.Oam.Length)return false;
            palette=frame.Oam[offset]&3;
            return true;
        }
        if(PalettesByState?.TryGetValue(frame.Ram[address],out int value)==true)palette=value;
        return true;
    }

    internal bool TryGetScreenPosition(NesFrame frame, out Point point)
    {
        point=default;
        if(XAddress<0 || XAddress>=frame.Ram.Length || YAddress<0 || YAddress>=frame.Ram.Length ||
            (XScrollAddress is int sx && (sx<0 || sx>=frame.Ram.Length)) ||
            (YScrollAddress is int sy && (sy<0 || sy>=frame.Ram.Length)))return false;
        int rawY=frame.Ram[YAddress],scrollY=YScrollAddress is int y ? frame.Ram[y] : 0;
        int screenY=(rawY-scrollY)&255;
        if(VerticalWrap is {} wrap)
        {
            if(YScrollAddress is null || !wrap.TryApplies(frame,rawY,scrollY,out bool applies))return false;
            if(applies)screenY=(screenY-16)&255;
        }
        point=new((frame.Ram[XAddress]-(XScrollAddress is int x ? frame.Ram[x] : 0))&255,screenY);
        return true;
    }

    internal bool ContainsPosition(NesFrame frame,Rectangle bounds,Point point)
    {
        // Only a state with an explicit OAM owner may use coordinate slack.
        // Image bounds are never inflated or moved; this is identity matching.
        int tolerance=PaletteStateAddress is int state && state>=0 && state<frame.Ram.Length &&
            PaletteOamByState?.ContainsKey(frame.Ram[state])==true ? PositionTolerance : 0;
        return Rectangle.Inflate(bounds,tolerance,tolerance).Contains(point);
    }

    internal bool Matches(NesFrame frame, Rectangle bounds, IEnumerable<int> oamIndices) =>
        TryGetScreenPosition(frame,out var point) &&
        TryGetPalette(frame,out int palette) &&
        (BodyAssembly is null || TryGetBodySlots(frame,out var ownedSlots)&&ownedSlots.ToHashSet().SetEquals(oamIndices)) &&
        (BodyTilesByAnimation is null || TryGetBodyRange(frame,out int bodyFirst,out int bodyCount)&&
            oamIndices.Any()&&oamIndices.All(index=>index>=bodyFirst&&index<bodyFirst+bodyCount)) &&
        // An animated palette is owned by an explicit native sprite slot.
        // Normally require that slot. A crouching animation can hide the
        // leading slot while explicitly owned lower body slots stay visible.
        // Never substitute nearby same-color sprites or generic clusters.
        (PaletteStateAddress is not int state || PaletteOamByState?.TryGetValue(frame.Ram[state],out int owner)!=true ||
            oamIndices.Contains(owner) || MatchesHiddenOwnerRemainder(frame,owner,oamIndices)) &&
        bounds.Width<=MaximumWidth && bounds.Height<=MaximumHeight &&
        ContainsPosition(frame,bounds,point) &&
        oamIndices.All(index=>index>=0 && index<64 && index*4+2<frame.Oam.Length && (frame.Oam[index*4+2]&3)==palette);

    private bool MatchesHiddenOwnerRemainder(NesFrame frame,int owner,IEnumerable<int> indices) =>
        TryGetBodyRange(frame,out int first,out _)&&first==owner&&frame.Oam[owner*4]>=239&&
        indices.Any()&&indices.All(index=>OwnsBodySlot(frame,index));
}

// Opt-in native nametable context, not a global modulo240 approximation.
// Metroid's visible next-page Y anchor first wraps as a byte, then subtracts16
// to account for the240-line PPU page. Same-page and horizontal anchors retain
// their original byte arithmetic. Other profiles omit this declaration.
internal sealed class VerticalNametableWrap
{
    public int ScrollDirectionAddress { get; set; } = -1;
    public int HorizontalDirectionMask { get; set; } = 2;
    public int ObjectNametableAddress { get; set; } = -1;
    public int PpuNametableAddress { get; set; } = -1;
    public int NametableMask { get; set; } = 1;

    internal void Validate()
    {
        if(ScrollDirectionAddress is <0 or >=2048 || ObjectNametableAddress is <0 or >=2048 || PpuNametableAddress is <0 or >=2048 ||
            HorizontalDirectionMask is <1 or >255 || NametableMask is <1 or >255)
            throw new InvalidDataException("Vertical nametable wrapping requires valid native RAM addresses and nonzero byte masks.");
    }
    internal bool TryApplies(NesFrame frame,int rawY,int scrollY,out bool applies)
    {
        applies=false;
        if(ScrollDirectionAddress<0 || ScrollDirectionAddress>=frame.Ram.Length || ObjectNametableAddress<0 || ObjectNametableAddress>=frame.Ram.Length ||
            PpuNametableAddress<0 || PpuNametableAddress>=frame.Ram.Length || HorizontalDirectionMask is <1 or >255 || NametableMask is <1 or >255)return false;
        applies=(frame.Ram[ScrollDirectionAddress]&HorizontalDirectionMask)==0 &&
            ((frame.Ram[ObjectNametableAddress]^frame.Ram[PpuNametableAddress])&NametableMask)!=0 && rawY<scrollY;
        return true;
    }
    internal VerticalNametableWrap Clone()=>new(){ScrollDirectionAddress=ScrollDirectionAddress,HorizontalDirectionMask=HorizontalDirectionMask,
        ObjectNametableAddress=ObjectNametableAddress,PpuNametableAddress=PpuNametableAddress,NametableMask=NametableMask};
}
