namespace Warp4D.Rendering;

internal sealed class AdaptiveDetail
{
    private double _average;
    private int _samples, _cooldown;
    public int Level { get; private set; }
    public double AverageMilliseconds => _average;
    public void Sample(double milliseconds, bool enabled, int fps)
    {
        if (!enabled) { Level = 0; _samples = _cooldown = 0; _average = 0; return; }
        _average = _samples++ == 0 ? milliseconds : _average * .92 + milliseconds * .08;
        if (_samples < 30 || _cooldown-- > 0) return;
        double budget = 1000d / fps;
        if (_average > budget * 1.12 && Level < 3) { Level++; _cooldown = 90; }
        else if (_average < budget * .65 && Level > 0) { Level--; _cooldown = 180; }
    }
}
