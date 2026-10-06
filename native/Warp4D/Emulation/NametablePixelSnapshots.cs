namespace Warp4D.Emulation;

// Decode into private scratch arrays, then publish immutable pixel snapshots.
// Native writes must never target an array already held by a previous frame.
internal sealed class NametablePixelSnapshots
{
    private readonly int[]?[] _scratch=new int[4][],_published=new int[4][];
    internal long Hits {get;private set;}
    internal long Misses {get;private set;}
    internal int ScratchPixels=>_scratch.Sum(x=>x?.Length??0);
    internal int RetainedPixels=>_published.Sum(x=>x?.Length??0);
    internal int[] Scratch(int table)=>_scratch[table]??=new int[NesFrame.NametablePixelCount];
    internal int[] Publish(int table)
    {
        int[] current=Scratch(table);
        if(_published[table] is {} old&&current.AsSpan().SequenceEqual(old)){Hits++;return old;}
        Misses++;return _published[table]=(int[])current.Clone();
    }
    internal void Clear(){Array.Clear(_scratch);Array.Clear(_published);}
}
