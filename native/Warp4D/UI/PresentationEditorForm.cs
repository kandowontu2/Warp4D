using System.Diagnostics;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D.UI;

internal sealed class PresentationEditorForm : Form
{
    private PresentationSettings _working;
    private readonly WarpRendererControl _preview = new() { Dock = DockStyle.Fill, EditLayers = true };
    private readonly ComboBox _objectBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350 };
    private readonly ComboBox _layerBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350 };
    private readonly FlowLayoutPanel _layerControls = new() { AutoSize = true, Width = 360,
        FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly FlowLayoutPanel _general = Stack();
    private readonly FlowLayoutPanel _animation = Stack();
    private readonly FlowLayoutPanel _objectGeometryControls = new() { AutoSize = true, Width = 360, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly FlowLayoutPanel _layerStrip = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, BackColor = Color.FromArgb(9, 14, 21) };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HighResolutionTimer _timerResolution = new();
    private TabControl? _tabs;
    public PresentationSettings EditedSettings { get; private set; }

    public PresentationEditorForm(PresentationSettings settings, SmbScene? scene)
    {
        NativeTheme.Apply(this);
        scene ??= LookGalleryForm.CreateSample();
        _working = settings.Clone(); EditedSettings = _working;
        Text = "Warp4D — Presentation and individual layers";
        ClientSize = new Size(1160, 850); MinimumSize = new Size(1000, 740);
        BackColor = Color.FromArgb(14, 19, 28); ForeColor = Color.FromArgb(227, 234, 241);
        Font = new Font("Segoe UI", 9);
        _objectBox.BackColor = _layerBox.BackColor = Color.White;
        _objectBox.ForeColor = _layerBox.ForeColor = Color.Black;
        NativeTheme.StyleComboBox(_objectBox); NativeTheme.StyleComboBox(_layerBox);
        StartPosition = FormStartPosition.CenterParent;
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(_preview, 0, 0);
        root.Controls.Add(_layerStrip, 0, 1);
        TabControl tabs = new() { Dock = DockStyle.Fill };
        NativeTheme.StyleTabs(tabs);
        _tabs = tabs;
        AddTab(tabs, "Look", _general); AddTab(tabs, "Animation", _animation);
        FlowLayoutPanel layerPage = Stack();
        layerPage.Controls.Add(new Label { Text = "OBJECT", AutoSize = true });
        layerPage.Controls.Add(_objectBox);
        layerPage.Controls.Add(_objectGeometryControls);
        layerPage.Controls.Add(new Label { Text = "PROJECTION LAYER", AutoSize = true });
        layerPage.Controls.Add(_layerBox); layerPage.Controls.Add(_layerControls);
        layerPage.Controls.Add(new Label { Text = "Click a visible sheet in the preview, then drag it to rotate that sheet through XW / YW.", AutoSize = true, MaximumSize = new(350, 70), ForeColor = Color.FromArgb(173, 255, 93) });
        AddTab(tabs, "Individual layers", layerPage);
        root.Controls.Add(tabs, 1, 0);
        root.SetRowSpan(tabs, 2);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        Button apply = new() { Text = "Save & apply", Width = 128, Height = 36 };
        apply.Click += (_, _) => { _working.Normalize(); EditedSettings = _working.Clone(); DialogResult = DialogResult.OK; Close(); };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 100, Height = 36 };
        Button export = new() { Text = "Export preset…", Width = 125, Height = 36 };
        export.Click += (_, _) => Export();
        Button import = new() { Text = "Import preset…", Width = 125, Height = 36 };
        import.Click += (_, _) => Import();
        footer.Controls.AddRange([apply, cancel, export, import]);
        root.Controls.Add(footer, 0, 2); root.SetColumnSpan(footer, 2); Controls.Add(root);
        CancelButton = cancel;
        if (scene is not null)
        {
            _preview.SetScene(scene);
            foreach (SceneObject item in scene.Objects) _objectBox.Items.Add(new ObjectChoice(item.PresentationKey, item.Label, item.Bounds));
        }
        string[] keys = ["Center", "Z:-1", "Z:+1", "W:-1", "W:+1", "W:-0.5", "W:+0.5", "W:-0.25", "W:+0.25", "W:-0.75", "W:+0.75"];
        _layerBox.Items.AddRange(keys);
        _objectBox.SelectedIndexChanged += (_, _) => { BuildObjectGeometryControls(); BuildLayerControls(); BuildLayerStrip(); };
        _preview.ProjectionPicked += SelectProjection;
        _preview.LayerEdited += BuildLayerControls;
        _layerBox.SelectedIndexChanged += (_, _) => BuildLayerControls();
        _layerBox.SelectedIndex = 0;
        if (_objectBox.Items.Count > 0) _objectBox.SelectedIndex = 0;
        BuildGeneral(); BuildAnimation(); _preview.ApplySettings(_working);
        _timer.Tick += (_, _) => { PresentationAnimator.Apply(_preview, _working, _clock.Elapsed.TotalSeconds); _preview.Invalidate(); };
        _timer.Start();
    }

    private void BuildGeneral()
    {
        Clear(_general);
        BuildGeometryControls(_general, _working.Geometry);
        foreach (string preset in new[] { "Subtle depth", "Strong rotation", "Fully opaque", "Reset" })
        {
            Button button = new() { Text = preset, Width = 350, Height = 32 };
            button.Click += (_, _) =>
            {
                ProjectionProfile profile = _working.ProjectionProfile;
                PresentationSettings old = _working;
                _working = PresentationSettings.Preset(preset); _working.ProjectionProfile = profile;
                if (preset != "Reset") { _working.Geometry = old.Geometry; _working.ObjectGeometries = old.ObjectGeometries; _working.ObjectLayers = old.ObjectLayers;
                    _working.ClassGeometries=old.ClassGeometries;_working.Dimensions=old.Dimensions;_working.Effects=old.Effects;_working.BlendSource=old.BlendSource;_working.BlendAmount=old.BlendAmount; }
                BuildGeneral(); BuildAnimation(); BuildObjectGeometryControls(); BuildLayerControls(); Changed();
            };
            _general.Controls.Add(button);
        }
        Number(_general, "W extent (%)", 0, 100, (decimal)(_working.Depth * 100), v => _working.Depth = (float)v / 100);
        Number(_general, "Camera proximity (%)", 0, 100, (decimal)(_working.Perspective * 100), v => _working.Perspective = (float)v / 100);
        Number(_general, "Layer opacity (%)", 0, 100, (decimal)(_working.Opacity * 100), v => _working.Opacity = (float)v / 100);
        Number(_general, "Cross-sections", 2, 9, _working.CrossSections, v => _working.CrossSections = (int)v);
        Number(_general, "Independent rotation spread (°)", 0, 180, (decimal)_working.RotationSpread, v => _working.RotationSpread = (float)v);
        Number(_general, "GPU detail (NES pixels ×)", 1, 4, _working.RenderScale, v => _working.RenderScale = (int)v);
        Toggle(_general,"Lighting, glow and shadows",_working.Effects.Enabled,v=>_working.Effects.Enabled=v);
        Number(_general,"Directional lighting (%)",0,100,(decimal)(_working.Effects.Lighting*100),v=>_working.Effects.Lighting=(float)v/100);
        Number(_general,"Selective glow (%)",0,100,(decimal)(_working.Effects.Glow*100),v=>_working.Effects.Glow=(float)v/100);
        Number(_general,"Depth shadows (%)",0,100,(decimal)(_working.Effects.Shadows*100),v=>_working.Effects.Shadows=(float)v/100);
        Toggle(_general,"Smooth style transformations",_working.Effects.SmoothTransitions,v=>_working.Effects.SmoothTransitions=v);
        Angles(_general, _working.Rotation);
    }

    private void BuildAnimation()
    {
        Clear(_animation);
        Toggle(_animation, "Enable animation", _working.Animate, value => _working.Animate = value);
        Channel("Depth", _working.DepthAnimation, 0, 100);
        Channel("Camera", _working.CameraAnimation, 0, 100);
        Channel("Base rotations", _working.RotationAnimation, -180, 180);
        Toggle(_animation, "Animate individual layers", _working.AnimateLayers, value => _working.AnimateLayers = value);
        Toggle(_animation, "Animate cross-section count", _working.AnimateCrossSections, value => _working.AnimateCrossSections = value);
        _animation.Controls.Add(new Label { Text = "Unchecked channels stay at their chosen values. Opacity always stays manual.",
            AutoSize = true, MaximumSize = new Size(350, 80) });
    }

    private void BuildObjectGeometryControls()
    {
        Clear(_objectGeometryControls);
        if (_objectBox.SelectedItem is not ObjectChoice choice) return;
        bool inherited = !_working.ObjectGeometries.ContainsKey(choice.Key);
        SceneObject? selected=_preview.SceneObjects.FirstOrDefault(item=>item.PresentationKey==choice.Key);
        GeometrySettings inheritedGeometry=selected is null?_working.Geometry:_working.GeometryFor(selected);
        Toggle(_objectGeometryControls, "Use class / global geometry", inherited, value =>
        {
            if (value) _working.ObjectGeometries.Remove(choice.Key);
            else _working.ObjectGeometries[choice.Key] = inheritedGeometry.Clone();
            BuildObjectGeometryControls(); Changed();
        });
        if (!inherited) BuildGeometryControls(_objectGeometryControls, _working.ObjectGeometries[choice.Key]);
        else _objectGeometryControls.Controls.Add(new Label { Text = "Geometry: " + GeometryCatalog.Names[(int)inheritedGeometry.Mode] + ". Class overrides: Dimension Studio. Global mode: Look tab. Uncheck above for this object only.", AutoSize = true, MaximumSize = new(350, 80) });
    }

    private void BuildGeometryControls(FlowLayoutPanel panel, GeometrySettings geometry)
    {
        panel.Controls.Add(new Label { Text = "4D GEOMETRY", AutoSize = true, ForeColor = Color.FromArgb(173, 255, 93), Margin = new(0, 12, 0, 4) });
        ComboBox modes = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "4D geometry mode", BackColor = Color.White, ForeColor = Color.Black };
        NativeTheme.StyleComboBox(modes);
        modes.Items.AddRange(GeometryCatalog.Names); modes.SelectedIndex = (int)geometry.Mode;
        Label explanation = new() { Text = GeometryCatalog.Descriptions[(int)geometry.Mode], AutoSize = true, MaximumSize = new(350, 85) };
        modes.SelectedIndexChanged += (_, _) => { geometry.Mode = (GeometryMode)modes.SelectedIndex; explanation.Text = GeometryCatalog.Descriptions[modes.SelectedIndex]; if (ReferenceEquals(geometry, _working.Geometry)) BuildObjectGeometryControls(); Changed(); };
        panel.Controls.Add(modes); panel.Controls.Add(explanation);
        Number(panel, "Geometry strength (%)", 0, 100, (decimal)(geometry.Amount * 100), v => geometry.Amount = (float)v / 100);
        Number(panel, "Phase / slice / unfolding (%)", 0, 100, (decimal)(geometry.Phase * 100), v => geometry.Phase = (float)v / 100);
        Toggle(panel, "Cycle geometry (also enables animation)", geometry.Animate, value =>
        { geometry.Animate = value; if (value) _working.Animate = true; BuildAnimation(); });
        Number(panel, "Geometry speed (cycles/sec)", 0, 4, (decimal)geometry.Speed, v => geometry.Speed = (double)v, 2);
    }

    private void Channel(string name, AnimationChannel channel, int minimum, int maximum)
    {
        Toggle(_animation, "Animate " + name.ToLowerInvariant(), channel.Enabled, value => channel.Enabled = value);
        Number(_animation, name + " speed (cycles/sec)", 0, 4, (decimal)channel.Speed, v => channel.Speed = (double)v, 2);
        Number(_animation, name + " minimum", minimum, maximum, (decimal)channel.Minimum, v => channel.Minimum = (float)v);
        Number(_animation, name + " maximum", minimum, maximum, (decimal)channel.Maximum, v => channel.Maximum = (float)v);
    }

    private void BuildLayerControls()
    {
        Clear(_layerControls);
        if (_objectBox.SelectedItem is not ObjectChoice choice || _layerBox.SelectedItem is not string key) return;
        _preview.SelectedObjectKey = choice.Key;
        _preview.SelectedLayerKey = key;
        LayerSettings layer = _working.EditLayer(choice.Key, key);
        Toggle(_layerControls, "Show this layer", layer.Enabled, value => layer.Enabled = value);
        Toggle(_layerControls, "Add automatic rotation", layer.UseAutomaticRotation, value => layer.UseAutomaticRotation = value);
        Toggle(_layerControls, "Use global opacity", layer.UseGlobalOpacity, value => layer.UseGlobalOpacity = value);
        Number(_layerControls, "Layer opacity (%)", 0, 100, layer.OpacityPercent, v => layer.OpacityPercent = (int)v);
        Number(_layerControls, key == "Center" ? "Center Z offset (100 = neutral)" : "Layer depth (%)", 0, 200, layer.DepthPercent, v => layer.DepthPercent = (int)v);
        Number(_layerControls, "Layer animation speed", 0, 4, (decimal)layer.AnimationSpeed, v => layer.AnimationSpeed = (double)v, 2);
        Angles(_layerControls, layer.Rotation);
        _layerControls.Controls.Add(new Label { Text = "Angles are offsets from the object's base rotation. Increase cross-sections to reveal interior W layers. Layer identities stay fixed when the count changes.",
            AutoSize = true, MaximumSize = new Size(350, 100) });
        Changed();
        foreach (LayerCard card in _layerStrip.Controls.OfType<LayerCard>()) { card.Chosen = card.LayerKey == key; card.Invalidate(); }
    }

    private void BuildLayerStrip()
    {
        Clear(_layerStrip);
        if (_objectBox.SelectedItem is not ObjectChoice choice) return;
        SceneObject? item = _preview.SceneObjects.FirstOrDefault(o => o.PresentationKey == choice.Key);
        if (item is null) return;
        foreach (string key in _layerBox.Items)
        {
            LayerCard card = new(key, item.Image) { Chosen = _layerBox.SelectedItem as string == key };
            card.Click += (_, _) => { _layerBox.SelectedItem = key; _tabs!.SelectedIndex = 2; };
            _layerStrip.Controls.Add(card);
        }
    }

    private void Angles(FlowLayoutPanel panel, RotationAngles angles)
    {
        Number(panel, "XY rotation (°)", -180, 180, (decimal)angles.XY, v => angles.XY = (float)v);
        Number(panel, "XZ rotation (°)", -180, 180, (decimal)angles.XZ, v => angles.XZ = (float)v);
        Number(panel, "XW rotation (°)", -180, 180, (decimal)angles.XW, v => angles.XW = (float)v);
        Number(panel, "YZ rotation (°)", -180, 180, (decimal)angles.YZ, v => angles.YZ = (float)v);
        Number(panel, "YW rotation (°)", -180, 180, (decimal)angles.YW, v => angles.YW = (float)v);
        Number(panel, "ZW rotation (°)", -180, 180, (decimal)angles.ZW, v => angles.ZW = (float)v);
    }

    private void Number(FlowLayoutPanel panel, string label, decimal low, decimal high, decimal value, Action<decimal> set, int decimals = 0)
    {
        Panel row = new() { Width = 350, Height = 34 };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(0, 7) });
        NumericUpDown input = new() { Minimum = low, Maximum = high, Value = Math.Clamp(value, low, high),
            DecimalPlaces = decimals, Increment = decimals > 0 ? 0.05m : 1, Width = 85, Location = new Point(255, 2) };
        input.ValueChanged += (_, _) => { set(input.Value); Changed(); };
        row.Controls.Add(input); panel.Controls.Add(row);
    }
    private void Toggle(FlowLayoutPanel panel, string label, bool value, Action<bool> set)
    {
        CheckBox input = new() { Text = label, Checked = value, AutoSize = true, Margin = new Padding(0, 9, 0, 6) };
        input.CheckedChanged += (_, _) => { set(input.Checked); Changed(); }; panel.Controls.Add(input);
    }
    private void Changed() { _preview.ApplySettings(_working); }
    private static FlowLayoutPanel Stack() => new() { Dock = DockStyle.Fill, AutoScroll = true,
        FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
    private static void Clear(Control panel) { while (panel.Controls.Count > 0) { Control child = panel.Controls[0]; panel.Controls.Remove(child); child.Dispose(); } }
    private static void AddTab(TabControl tabs, string name, Control contents)
    {
        TabPage page = new(name) { BackColor = Color.FromArgb(14, 19, 28), ForeColor = Color.FromArgb(227, 234, 241) };
        page.Controls.Add(contents); tabs.TabPages.Add(page);
    }
    private void Export()
    {
        using SaveFileDialog dialog = new() { Filter = "Warp4D presentation (*.warp4d-look.json)|*.warp4d-look.json", FileName = "presentation.warp4d-look.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) PresentationSettingsStore.WriteToFile(dialog.FileName, _working);
    }
    private void Import()
    {
        using OpenFileDialog dialog = new() { Filter = "Warp4D presentation (*.warp4d-look.json)|*.warp4d-look.json|JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { _working = PresentationSettingsStore.ReadFromFile(dialog.FileName); BuildGeneral(); BuildAnimation(); BuildObjectGeometryControls(); BuildLayerControls(); Changed(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Could not import preset"); }
    }
    internal void SelectProjection(string objectKey, string layerKey)
    {
        foreach (ObjectChoice choice in _objectBox.Items)
            if (choice.Key == objectKey) { _objectBox.SelectedItem = choice; _layerBox.SelectedItem = layerKey; _tabs!.SelectedIndex = 2; break; }
    }
    internal void SelectLayerForTest(string key) { _tabs!.SelectedIndex = 2; _layerBox.SelectedItem = key; }
    internal void SelectGlobalGeometryForTest(GeometryMode mode) => _general.Controls.OfType<ComboBox>().Single(c => c.AccessibleName == "4D geometry mode").SelectedIndex = (int)mode;
    internal void EnableGeometryCycleForTest() => _general.Controls.OfType<CheckBox>().Single(c => c.Text.StartsWith("Cycle geometry")).Checked = true;
    internal GeometryMode GlobalGeometryForTest => _working.Geometry.Mode;
    internal bool GeometryCyclesForTest => _working.Animate && _working.Geometry.Animate;
    protected override void Dispose(bool disposing) { if (disposing) { _timer.Dispose(); _timerResolution.Dispose(); } base.Dispose(disposing); }
    private sealed record ObjectChoice(string Key, string Label, Rectangle Bounds)
    {
        public override string ToString() => $"{Label} · ({Bounds.X}, {Bounds.Y})";
    }
}
