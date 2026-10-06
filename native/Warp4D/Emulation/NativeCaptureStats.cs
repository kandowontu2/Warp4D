namespace Warp4D.Emulation;

// Diagnostic totals measured on the thread doing the native callback work.
// Nametables is a subset of Frames: never add their allocations together.
internal readonly record struct NativeCaptureStage(long Calls,long AllocatedBytes,long ElapsedTicks)
{
    public double Milliseconds=>ElapsedTicks*1000d/System.Diagnostics.Stopwatch.Frequency;
    internal NativeCaptureStage Since(NativeCaptureStage before)=>new(Calls-before.Calls,AllocatedBytes-before.AllocatedBytes,ElapsedTicks-before.ElapsedTicks);
}
internal readonly record struct NativeCaptureStats(NativeCaptureStage Frames,NativeCaptureStage Nametables,NativeCaptureStage Video)
{
    internal NativeCaptureStats Since(NativeCaptureStats before)=>new(Frames.Since(before.Frames),Nametables.Since(before.Nametables),Video.Since(before.Video));
}
