namespace Warp4D.Rendering;

internal readonly record struct MeshAssemblyStats(long NewGroups,long ReusedGroups,long NewTriangles,long VertexArrays,long VertexElements,long BuildAllocatedBytes,long CacheOverflowClears,long ExplicitCacheClears,long AccentGroups,long AccentTriangles,long AccentAllocatedBytes,long BackgroundAllocatedBytes)
{
    internal MeshAssemblyStats Since(MeshAssemblyStats b)=>new(NewGroups-b.NewGroups,ReusedGroups-b.ReusedGroups,NewTriangles-b.NewTriangles,VertexArrays-b.VertexArrays,VertexElements-b.VertexElements,BuildAllocatedBytes-b.BuildAllocatedBytes,CacheOverflowClears-b.CacheOverflowClears,ExplicitCacheClears-b.ExplicitCacheClears,AccentGroups-b.AccentGroups,AccentTriangles-b.AccentTriangles,AccentAllocatedBytes-b.AccentAllocatedBytes,BackgroundAllocatedBytes-b.BackgroundAllocatedBytes);
}
internal readonly record struct FrameBuildTiming(long Sequence,double CaptureMs,double RecognitionMs);
internal readonly record struct ProfileBuildTiming(long Sequence,double ComposeMs,double BackgroundMs,double SpritesMs,double FinalizeMs);
internal readonly record struct BackgroundBuildTiming(long Sequence,double BackdropMs,double ClassificationMs,double GroupingMs,double ArtworkMs,int Groups,int Objects,double MetadataMs=0,double CropMs=0,double TransparencyMs=0,double EraseMs=0,bool CombinedArtwork=false);

internal readonly record struct PlaybackPaintTiming(long Timestamp,double DrawMs,double AssemblyMs,
    double LightingMs,double OrderingMs,double PackingMs,double SubmissionMs,double PresentMs,int Triangles,
    double AtlasMs=0,double DriverMs=0,long DrawAllocatedBytes=0,long AssemblyAllocatedBytes=0);
