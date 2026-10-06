using System.Drawing.Drawing2D;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D.UI;

// These are presentation presets, never recognition profiles. Applying a look
// preserves the user's game detector, quality preference and individual edits.
internal static class LookCatalog
{
    internal static readonly string[] Names = ["Subtle depth", "Kaleidoscope", "Hypercube", "Solid sculpture", "Exploded layers", "Hypersphere", "Duocylinder", "4D slicing", "W-axis ribbon", "4D perspective lens", "Dimensional unfolding"];
    internal static readonly string[] Descriptions = ["A little beyond the screen", "Independent sheets in motion", "Explore the fourth axis", "Pixel art, sculpted in space", "Reveal every cross-section", "Artwork wrapped into R⁴", "Two circular dimensions", "Move through a W cross-section", "Twist the sprite through W", "Magnify fourth-axis distance", "Flat pixels open into R⁴"];
    internal static PresentationSettings Create(int index, PresentationSettings source)
    {
        PresentationSettings result = source.Clone();
        result.BlendSource=null;result.BlendAmount=1;
        result.Name = Names[index];
        if (index >= 5)
        {
            result.Geometry = new() { Mode = (GeometryMode)(index - 4), Amount = .7f, Phase = .5f, Animate = index is 7 or 8 or 10, Speed = .12 };
            result.Animate = result.Geometry.Animate;
            result.AnimateLayers = false; result.AnimateCrossSections = false;
            result.DepthAnimation.Enabled = result.CameraAnimation.Enabled = result.RotationAnimation.Enabled = false;
            result.Depth = .8f; result.Perspective = .55f; result.Opacity = .6f; result.CrossSections = 3;
            result.RotationSpread = 18;
            return result;
        }
        result.Geometry.Mode = GeometryMode.Hyperprism;
        result.Geometry.Animate = false;
        result.Animate = index is 1 or 2;
        result.AnimateCrossSections = false;
        result.AnimateLayers = true;
        result.DepthAnimation.Enabled = false;
        result.CameraAnimation.Enabled = false;
        result.RotationAnimation.Enabled = index == 2;
        result.Depth = new[] { .35f, .9f, 1f, .85f, 1f }[index];
        result.Perspective = new[] { .35f, .55f, .7f, .45f, .8f }[index];
        result.Opacity = new[] { .25f, .55f, .4f, 1f, .65f }[index];
        result.CrossSections = new[] { 3, 5, 5, 5, 9 }[index];
        result.RotationSpread = new[] { 15f, 110f, 65f, 35f, 135f }[index];
        result.Rotation = new() { XW = 24, YW = -16, ZW = 33, XZ = -9, YZ = 6 };
        return result;
    }
}

internal sealed class LookCard : Button
{
    internal int LookIndex { get; }
    internal bool Chosen { get; set; }
    internal bool Compact { get; set; }
    private bool _hover;
    private float _energy;
    private readonly System.Windows.Forms.Timer _feedback = new() { Interval = 25 };
    private readonly System.Diagnostics.Stopwatch _motionClock = System.Diagnostics.Stopwatch.StartNew();
    private Bitmap? _thumbnail;
    internal void SetThumbnail(Bitmap bitmap) { _thumbnail?.Dispose(); _thumbnail = new(bitmap); Invalidate(); }
    internal void HoverForTest(bool enter) { if (enter) OnMouseEnter(EventArgs.Empty); else OnMouseLeave(EventArgs.Empty); }
    public LookCard(int index)
    {
        LookIndex = index;
        Text = LookCatalog.Names[index]; AccessibleName = Text;
        Size = new(174, 104); Margin = new(5); Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat; TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        MouseEnter += (_, _) => { _hover = true; _feedback.Start(); Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; _feedback.Start(); Invalidate(); };
        _feedback.Tick += (_, _) =>
        {
            float target = _hover ? 1 : 0;
            _energy += (target - _energy) * .24f;
            if (Math.Abs(target - _energy) < .01f) { _energy = target; if (!_hover || !InterfaceMotion.Enabled) _feedback.Stop(); }
            if (Visible) Invalidate(); else _feedback.Stop();
        };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(9, 13, 20));
        using SolidBrush fill = new(Color.FromArgb(_hover || Chosen ? 26 : 17, _hover || Chosen ? 38 : 25, _hover || Chosen ? 43 : 35));
        using Pen edge = new(Chosen || _hover || Focused ? Color.FromArgb(173, 255, 93) : Color.FromArgb(40, 54, 68));
        Rectangle r = new(1, 1, Width - 3, Height - 3);
        g.FillRectangle(fill, r); g.DrawRectangle(edge, r);
        float energy = InterfaceMotion.Enabled ? _energy : (_hover ? 1 : 0);
        if (energy > .01f || Chosen)
        {
            using Pen halo = new(Color.FromArgb((int)(22 + energy * 30), 173, 255, 93), 4);
            g.DrawRectangle(halo, 4, 4, Width - 9, Height - 9);
            using SolidBrush active = new(Color.FromArgb(173, 255, 93));
            g.FillRectangle(active, 7, Height - 3, (Width - 14) * (Chosen ? 1 : energy), 2);
        }
        if (_thumbnail is not null)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_thumbnail, new Rectangle(6, 5, Width - 12, Height - 46));
        }
        else
        {
        GraphicsState iconState = g.Save();
        if (Compact) { g.TranslateTransform(Width / 4f, 0); g.ScaleTransform(.5f, .5f); }
        float cx = Width / 2f, cy = Compact ? 27 : Height > 85 ? 34 : 25;
        if (LookIndex is 1 or 4)
        {
            for (int layer = -2; layer <= 2; layer++)
            {
                float offset = layer * (LookIndex == 4 ? 10 : 5);
                PointF[] sheet = [new(cx - 15 + offset, cy - 11), new(cx + 7 + offset, cy - 17), new(cx + 16 + offset, cy + 10), new(cx - 6 + offset, cy + 16)];
                using SolidBrush film = new(Color.FromArgb(22, 109, 182, 239));
                using Pen outline = new(Color.FromArgb(90, layer % 2 == 0 ? Color.FromArgb(109, 182, 239) : Color.FromArgb(206, 153, 243)));
                g.FillPolygon(film, sheet); g.DrawPolygon(outline, sheet);
            }
        }
        if (LookIndex == 3)
        {
            using SolidBrush sculpture = new(Color.FromArgb(65, 173, 255, 93));
            g.FillPolygon(sculpture, new PointF[] { new(cx - 17, cy - 12), new(cx + 9, cy - 17), new(cx + 19, cy + 13), new(cx - 7, cy + 18) });
        }
        float turn = InterfaceMotion.Enabled && _hover ? (float)_motionClock.Elapsed.TotalSeconds * .35f : 0;
        Rotation4D rotation = new(.4f + LookIndex * .19f, -.25f + turn, .6f + turn * .6f, .1f, .15f, .05f);
        Vector4F[] vertices = FourDMath.CreateHyperprism(13, 13, 10, 11);
        PointF[] points = vertices.Select(v => { var p = FourDMath.Project(v, new PreparedRotation4D(rotation), 80, 95); return new PointF(cx + p.Point.X, cy + p.Point.Y); }).ToArray();
        using Pen geometry = new(Color.FromArgb(Chosen || _hover ? 220 : 150, 173, 255, 93), 1.1f);
        for (int a = 0; a < 16; a++) for (int bit = 0; bit < 4; bit++)
        { int b = a ^ (1 << bit); if (b > a) g.DrawLine(geometry, points[a], points[b]); }
        g.Restore(iconState);
        }
        if (Compact)
        {
            using Font compactTitle = new("Segoe UI Semibold", 8f);
            string label = Text.Replace("4D perspective lens", "4D perspective\nlens").Replace("Dimensional unfolding", "Dimensional\nunfolding");
            TextRenderer.DrawText(g, label, compactTitle, new Rectangle(5, Height - 35, Width - 10, 32), Color.FromArgb(229, 237, 242),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return;
        }
        using Font title = new("Segoe UI Semibold", 9f);
        TextRenderer.DrawText(g, Text, title, new Rectangle(6, Height - (Height > 85 ? 38 : 25), Width - 12, 22), Color.FromArgb(229, 237, 242), TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        if (Height > 85)
        {
            using Font copy = new("Segoe UI", 7.5f);
            TextRenderer.DrawText(g, LookCatalog.Descriptions[LookIndex], copy, new Rectangle(5, Height - 19, Width - 10, 16), Color.FromArgb(132, 150, 163), TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) { _feedback.Dispose(); _thumbnail?.Dispose(); } base.Dispose(disposing); }
}

internal sealed class LookGalleryForm : Form
{
    private readonly WarpRendererControl _preview = new() { Dock = DockStyle.Fill, ShowLabels = false };
    private readonly PresentationSettings _source;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly LookCard[] _cards;
    private readonly Label _caption = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(141, 159, 172), Font = new("Segoe UI", 10) };
    private int _selected;
    internal PresentationSettings SelectedSettings => LookCatalog.Create(_selected, _source);
    internal string PreviewNameForTest => _preview.Settings.Name;
    internal void HoverForTest(int index, bool enter) => _cards[index].HoverForTest(enter);
    internal LookGalleryForm(PresentationSettings source, SmbScene? scene)
    {
        NativeTheme.Apply(this);
        _source = source.Clone(); Text = "Warp4D · Looks";
        ClientSize = new(1060, 840); MinimumSize = new(1000, 760); StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(9, 13, 20); ForeColor = Color.White;
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new(18) };
        layout.RowStyles.Add(new(SizeType.Absolute, 50)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 28)); layout.RowStyles.Add(new(SizeType.Absolute, 246)); layout.RowStyles.Add(new(SizeType.Absolute, 46));
        layout.Controls.Add(new Label { Text = "FIND YOUR FOURTH DIMENSION", Font = new("Segoe UI Semibold", 17), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(_preview, 0, 1); layout.Controls.Add(_caption, 0, 2);
        TableLayoutPanel cards = new() { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2 };
        for (int column = 0; column < 6; column++) cards.ColumnStyles.Add(new(SizeType.Percent, 100f / 6));
        for (int row = 0; row < 2; row++) cards.RowStyles.Add(new(SizeType.Percent, 50));
        _cards = Enumerable.Range(0, LookCatalog.Names.Length).Select(i => new LookCard(i) { Dock = DockStyle.Fill }).ToArray();
        foreach (LookCard card in _cards)
        {
            card.Click += (_, _) => SelectLook(card.LookIndex);
            card.MouseEnter += (_, _) => PreviewLook(card.LookIndex);
            card.MouseLeave += (_, _) => PreviewLook(_selected);
            cards.Controls.Add(card, card.LookIndex % 6, card.LookIndex / 6);
        }
        layout.Controls.Add(cards, 0, 3);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        Button apply = new() { Text = "APPLY LOOK", Width = 140, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(173, 255, 93), ForeColor = Color.FromArgb(9, 13, 20) };
        apply.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Button cancel = new() { Text = "Cancel", Width = 100, Height = 36, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
        footer.Controls.Add(apply); footer.Controls.Add(cancel); CancelButton = cancel; AcceptButton = apply;
        layout.Controls.Add(footer, 0, 4); Controls.Add(layout);
        // Empty previews use an explicitly labeled original sample, not a ROM.
        _preview.EnableStyleTransitions=true;
        _preview.SetScene(scene ?? CreateSample()); SelectLook(0);
        _timer.Tick += (_, _) => { PresentationAnimator.Apply(_preview, _preview.Settings, _clock.Elapsed.TotalSeconds); _preview.Invalidate(); };
        Shown += (_, _) => { BuildThumbnails(); _timer.Start(); };
    }
    internal void SelectLook(int index)
    {
        _selected = index;
        foreach (LookCard card in _cards) { card.Chosen = card.LookIndex == index; card.Invalidate(); }
        PreviewLook(index);
    }
    private void PreviewLook(int index)
    {
        _preview.ApplySettings(LookCatalog.Create(index, _source)); _clock.Restart();
        _caption.Text = LookCatalog.Names[index] + "  /  " + LookCatalog.Descriptions[index] + "  ·  Hover to preview. Click to choose.";
    }
    private void BuildThumbnails()
    {
        using WarpRendererControl renderer = new() { Size = new(174, 94), PresentationMode = true, UseGpu = _preview.UseGpu };
        renderer.CreateControl();
        for (int index = 0; index < _cards.Length; index++)
        {
            SmbScene? clone = _preview.CloneScene(); if (clone is null) return;
            renderer.SetScene(clone);
            PresentationSettings look = LookCatalog.Create(index, _source); look.RenderScale = 1;
            // A moving slice can be completely outside the object at t=2.
            // Thumbnails show a representative midpoint, while the live preview cycles.
            if (index >= 5) { look.Geometry.Animate = false; look.Geometry.Phase = index == 10 ? .75f : .5f; }
            renderer.ApplySettings(look); PresentationAnimator.Apply(renderer, look, 2);
            using Bitmap image = new(renderer.Width, renderer.Height); renderer.DrawToBitmap(image, renderer.ClientRectangle);
            _cards[index].SetThumbnail(image);
        }
    }
    internal static SmbScene CreateSample()
    {
        Bitmap background = new(256, 240); using (Graphics g = Graphics.FromImage(background)) g.Clear(Color.FromArgb(16, 24, 35));
        Bitmap art = new(32, 32); using (Graphics g = Graphics.FromImage(art))
        { g.Clear(Color.Transparent); using SolidBrush brush = new(Color.FromArgb(173, 255, 93)); g.FillRectangle(brush, 8, 0, 16, 32); g.FillRectangle(brush, 0, 8, 32, 16); g.FillRectangle(Brushes.White, 12, 12, 8, 8); }
        return new SmbScene { Background = background, Objects = [new SceneObject { Image = art, Bounds = new(96, 84, 64, 64), Label = "Original sample", Kind = SceneObjectKind.Player, Accent = Color.FromArgb(173, 255, 93), Depth = 1, ProjectionEnabled = true, IdentityKey = "sample:cross" }], RecognitionProfileName = "ORIGINAL SAMPLE · NO ROM", ProjectionProfileName = "LOOK PREVIEW", Location = "", ExactProfile = true };
    }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
