using System.Drawing.Drawing2D;

namespace Warp4D.UI;

internal sealed class LayerCard : Button
{
    internal string LayerKey { get; }
    internal bool Chosen { get; set; }
    private readonly Bitmap _art;
    private bool _hover;
    internal LayerCard(string key, Bitmap art)
    {
        LayerKey = key; _art = new(art); Text = key; AccessibleName = "Projection " + key;
        Size = new(84, 85); Margin = new(5, 5, 0, 0); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        MouseEnter += (_, _) => { _hover = true; Invalidate(); }; MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(16, 23, 32));
        using Pen edge = new(Chosen || _hover || Focused ? Color.FromArgb(173, 255, 93) : Color.FromArgb(43, 57, 71));
        e.Graphics.DrawRectangle(edge, 1, 1, Width - 3, Height - 3);
        float scale = Math.Min(48f / _art.Width, 48f / _art.Height);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_art, (Width - _art.Width * scale) / 2, 8, _art.Width * scale, _art.Height * scale);
        using Font font = new("Segoe UI Semibold", 8);
        TextRenderer.DrawText(e.Graphics, LayerKey, font, new Rectangle(2, 62, Width - 4, 20), Chosen ? Color.FromArgb(173, 255, 93) : Color.FromArgb(205, 219, 229), TextFormatFlags.HorizontalCenter);
    }
    protected override void Dispose(bool disposing) { if (disposing) _art.Dispose(); base.Dispose(disposing); }
}
