namespace Warp4D.Rendering;

internal sealed class FrameMetric
{
    private readonly Queue<double> _samples = [];
    public void Add(double milliseconds) { lock (_samples) { _samples.Enqueue(milliseconds); if (_samples.Count > 120) _samples.Dequeue(); } }
    public (double Average, double P95, int Samples) Snapshot()
    {
        lock (_samples)
        {
            if (_samples.Count == 0) return (0, 0, 0);
            double[] sorted = _samples.Order().ToArray();
            return (_samples.Average(), sorted[(int)((sorted.Length - 1) * 0.95)], sorted.Length);
        }
    }
}
internal static class PerformanceMetrics
{
    public static FrameMetric Capture { get; } = new();
    public static FrameMetric Recognition { get; } = new();
    public static FrameMetric Drawing { get; } = new();
    public static string Summary => $"Capture {Capture.Snapshot().Average:0.0} ms · recognition {Recognition.Snapshot().Average:0.0} ms · draw {Drawing.Snapshot().Average:0.0} ms";
}
