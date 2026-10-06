using Warp4D.Profiles;

namespace Warp4D.Rendering;

// Renderer-owned, bounded cache of unrotated geometry only. Positions, camera,
// independent layer rotations and opacity are still evaluated on every rebuild.
internal sealed class GeometryMapCache
{
    private readonly record struct Key(GeometryMode Mode,int Columns,int Rows,uint Z,uint W,
        uint Hx,uint Hy,uint Hz,uint Hw,uint Amount,uint Phase);
    private readonly Dictionary<Key,Vector4F[]> _grids=[];
    internal int Entries=>_grids.Count;
    internal int Vertices {get;private set;}
    internal long Hits {get;private set;}
    internal long Misses {get;private set;}
    internal const int MaximumEntries=128,MaximumVertices=16384;
    internal Vector4F[] Get(GeometryMode mode,int columns,int rows,float z,float w,
        float hx,float hy,float hz,float hw,float amount,float phase)
    {
        if(columns is <1 or >32 || rows is <1 or >32)throw new ArgumentOutOfRangeException(nameof(columns));
        // Float bit keys preserve signed zero and NaN payload distinctions.
        Key key=new(mode,columns,rows,Bits(z),Bits(w),Bits(hx),Bits(hy),Bits(hz),Bits(hw),Bits(amount),Bits(phase));
        if(_grids.TryGetValue(key,out var existing)){Hits++;return existing;}
        int count=(columns+1)*(rows+1);
        if(_grids.Count>=MaximumEntries || Vertices+count>MaximumVertices){_grids.Clear();Vertices=0;}
        Vector4F[] grid=new Vector4F[count];
        for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
            grid[y*(columns+1)+x]=Geometry4D.Map(mode,x/(float)columns,y/(float)rows,z,w,hx,hy,hz,hw,amount,phase);
        _grids.Add(key,grid);Vertices+=count;Misses++;return grid;
    }
    private static uint Bits(float value)=>BitConverter.SingleToUInt32Bits(value);
}
