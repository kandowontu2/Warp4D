using System.Drawing.Drawing2D;
using Warp4D.Rendering;

namespace Warp4D.UI;

internal static class InterfaceMotion
{
    internal static bool Enabled { get; set; } = SystemInformation.UIEffectsEnabled;
    private static string SettingsPath => Path.Combine(Emulation.AppPaths.DataDirectory, "interface-motion.json");
    internal static void Load()
    {
        try { Enabled = System.Text.Json.JsonSerializer.Deserialize<bool>(File.ReadAllText(SettingsPath)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { Enabled = SystemInformation.UIEffectsEnabled; }
    }
    internal static void Save()
    {
        try
        {
            Directory.CreateDirectory(Emulation.AppPaths.DataDirectory);
            File.WriteAllText(SettingsPath + ".tmp", System.Text.Json.JsonSerializer.Serialize(Enabled));
            File.Move(SettingsPath + ".tmp", SettingsPath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* Motion still works for this session. */ }
    }
}

// A small native, double-buffered animation; never invalidates the game surface.
internal sealed class DimensionalBrand : Control
{
    internal bool Compact {get;set;}
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    internal DimensionalBrand()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleName = "Warp4D — beyond the third dimension";
        _timer.Tick += (_, _) => { if (Visible && InterfaceMotion.Enabled && FindForm()?.WindowState != FormWindowState.Minimized) Invalidate(); };
        _timer.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(10, 14, 21));
        float t = InterfaceMotion.Enabled ? (float)_clock.Elapsed.TotalSeconds : 0;
        PreparedRotation4D rotation = new(new(.2f, .3f + t * .2f, .5f + t * .13f, .1f, .2f, 0));
        PointF[] points = FourDMath.CreateHyperprism(11, 11, 11, 11).Select(v =>
        { var p = FourDMath.Project(v, rotation, 90, 120); return new PointF(25 + p.Point.X, 24 + p.Point.Y); }).ToArray();
        using Pen glow = new(Color.FromArgb(24, 173, 255, 93), 5);
        using Pen line = new(Color.FromArgb(173, 255, 93), 1);
        for (int a = 0; a < 16; a++) for (int bit = 0; bit < 4; bit++)
        { int b = a ^ (1 << bit); if (b > a) { g.DrawLine(glow, points[a], points[b]); g.DrawLine(line, points[a], points[b]); } }
        using Font title = new("Segoe UI", Compact ? 15 : 17, FontStyle.Bold);
        using Font subtitle = new("Segoe UI", 7.5f, FontStyle.Bold);
        TextRenderer.DrawText(g, "WARP⁴D", title, new Point(53, 0), Color.FromArgb(227, 234, 241));
        TextRenderer.DrawText(g, Compact?"NES, REIMAGINED":"BEYOND THE THIRD DIMENSION", subtitle, new Point(55, Compact ? 25 : 34), Color.FromArgb(126, 143, 160));
    }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
