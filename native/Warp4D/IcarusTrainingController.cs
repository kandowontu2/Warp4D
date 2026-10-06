namespace Warp4D;

// Explicit capture tooling only. This policy reads native state and issues
// controller buttons; it has no memory-writing or normal-play integration.
internal sealed class IcarusTrainingController
{
    private byte[]? _previous;
    private long? _previousSequence;
    private bool _entryCleared;
    internal (int Mask, int Target, int Hazards) Choose(byte[] ram, byte[] oam, long? sequence=null)
    {
        if(ram.Length!=2048 || oam.Length!=256 || ram[160]!=2 || ram[304]!=2 || ram[59]!=35)
            throw new InvalidDataException("Training feedback requires complete native stage3/room35 state.");
        int x=ram[1827], y=ram[1824];
        if(ram[58]==8) return (64,24,0); // Dialogue: move when unlocked, never decline with B.
        if(ram[58]!=9 || ram[166]==0) return (0,x,0);
        // Leaving the raised doorway is a navigation task, not a dodge. A
        // nearest-safe-position policy otherwise idles on the entry ledge
        // before its lookahead sees the first falling objects.
        _entryCleared|=x<=144 && y>=176;
        if(!_entryCleared && x>144)return (64|2,96,0);
        // Feedback captures can be separated by multiple emulated frames.
        // Velocity belongs to native frame time, not wall-clock poll time.
        float delta=sequence is long current && _previousSequence is long previous
            ? Math.Max(1,current-previous):1;
        List<(float X,float Y,float Vx,float Vy)> hazards=[];
        for(int i=0;i<64;i++)
        {
            // Native Pit owns8..13 and arrows14..15. Hurt flashes change his
            // palette too, so palette0 alone is not a safe self-exclusion.
            if(i is >=8 and <=15)continue;
            int p=i*4, hy=oam[p]+1, hx=oam[p+3]+4;
            // Native blue training objects use non-player palettes. HUD sprites
            // and hidden OAM slots are not collision candidates.
            if(hy<32 || hy>=224 || (oam[p+2]&3)==0)continue;
            float vx=0,vy=2;
            if(_previous is not null && _previous[p]<224 && _previous[p+1]==oam[p+1])
            {
                vx=Math.Clamp((hx-(_previous[p+3]+4))/delta,-4,4);
                vy=Math.Clamp((hy-(_previous[p]+1))/delta,0,8);
            }
            if(hy>y+20 || hy+vy*32<y-24)continue;
            hazards.Add((hx,hy,vx,vy));
        }
        _previous=(byte[])oam.Clone();
        _previousSequence=sequence;
        // Score reachable walking trajectories, not safe destinations that
        // require teleporting through a falling object. The native right
        // stair/exit at X192 trapped earlier attempts; stay on the open floor.
        int direction=0,target=x;float best=float.PositiveInfinity;
        foreach(int candidate in new[]{0,-1,1})
        {
            float risk=candidate==0?0:.1f;
            for(int t=2;t<=32;t+=2)
            {
                float px=Math.Clamp(x+candidate*t,24,176);
                foreach(var h in hazards)
                {
                    float hy=h.Y+h.Vy*t;
                    if(hy<y-24 || hy>y+16)continue;
                    float distance=Math.Abs(px-(h.X+h.Vx*t));
                    risk+=Math.Max(0,22-distance)*(34-t);
                }
            }
            if(risk<best){best=risk;direction=candidate;target=(int)Math.Clamp(x+candidate*32,24,176);}
        }
        int mask=x<24?128:x>176?64:direction<0?64:direction>0?128:0;
        return (mask|2,target,hazards.Count);
    }
}
