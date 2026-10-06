using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D.UI;

internal sealed class GameProfileEditorForm : Form
{
    private static readonly Color WindowColor = Color.FromArgb(8, 11, 17);
    private static readonly Color PanelColor = Color.FromArgb(14, 19, 28);
    private static readonly Color BorderColor = Color.FromArgb(37, 49, 63);
    private static readonly Color TextColor = Color.FromArgb(227, 234, 241);
    private static readonly Color MutedColor = Color.FromArgb(126, 143, 160);
    private static readonly Color Lime = Color.FromArgb(173, 255, 93);

    private static readonly SceneObjectKind[] BackgroundKinds =
    [
        SceneObjectKind.Bush,
        SceneObjectKind.Cloud,
        SceneObjectKind.Hill,
        SceneObjectKind.Tree,
        SceneObjectKind.Pipe,
        SceneObjectKind.QuestionBlock,
        SceneObjectKind.Brick,
        SceneObjectKind.Terrain,
        SceneObjectKind.Castle,
        SceneObjectKind.Flagpole
    ];

    private readonly string _romName;
    private readonly string _romSha256;
    private readonly TextBox _nameBox = new();
    private readonly TilePickerControl _picker;
    private readonly Label _signatureLabel = MakeLabel("Click or drag across 16×16 cells in the game snapshot.", 9, MutedColor);
    private readonly ComboBox _kindBox = new();
    private readonly TextBox _labelBox = new();
    private readonly ListBox _rulesList = new();
    private readonly Label _ruleCountLabel = MakeLabel("0 captured patterns", 8, MutedColor, FontStyle.Bold);
    private readonly Button _addButton;
    private readonly WarpRendererControl _preview = new() { Dock = DockStyle.Fill, ShowLabels = true };
    private readonly SmbProfile _previewRecognizer = new();
    private readonly bool _exactSmbProfile;
    private readonly Func<NesFrame?>? _snapshotProvider;
    private readonly GameRecognitionProfile? _builtInProfile;
    private readonly ToolTip _trackingTip = new() { AutoPopDelay = 10000 };
    internal Button? PlayerTrackingUpgradeButtonForTest { get; private set; }
    internal Button? HudProtectionUpgradeButtonForTest { get; private set; }
    internal Button? SpriteAssemblyUpgradeButtonForTest { get; private set; }
    private NesFrame _frame;
    private readonly Stack<GameRecognitionProfile> _undo = [];
    private readonly Stack<GameRecognitionProfile> _redo = [];
    private readonly Dictionary<string, Bitmap> _ruleThumbnails = [];
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 80 };
    private Button? _undoButton;
    private Button? _redoButton;
    private GameRecognitionProfile _workingProfile;

    public GameRecognitionProfile EditedProfile { get; private set; }
    internal bool ShowWithoutActivationForTest { get; set; }
    protected override bool ShowWithoutActivation => ShowWithoutActivationForTest || base.ShowWithoutActivation;

    internal void SelectGamePixelForTest(int gameX, int gameY, bool additive = false) =>
        _picker.SelectGamePixel(gameX, gameY, additive);

    internal void DragSelectGamePixelsForTest(
        int startGameX,
        int startGameY,
        int endGameX,
        int endGameY,
        bool additive = false) =>
        _picker.SelectGameDrag(startGameX, startGameY, endGameX, endGameY, additive);

    internal int SelectedCellCountForTest => _picker.SelectedCellCount;

    internal int SelectedPatternCountForTest => _picker.SelectedPatternCount;
    internal TilePickerControl PickerForTest => _picker;

    public GameProfileEditorForm(
        GameRecognitionProfile profile,
        NesFrame frame,
        bool exactSmbProfile,
        string romName,
        string romSha256,
        Func<NesFrame?>? snapshotProvider = null,
        PresentationSettings? presentation = null,
        GameRecognitionProfile? builtInProfile = null)
    {
        _frame = frame;
        _exactSmbProfile = exactSmbProfile;
        _snapshotProvider = snapshotProvider;
        _builtInProfile = builtInProfile?.Clone();
        if (presentation is not null) _preview.ApplySettings(presentation.Clone());
        _romName = Path.GetFileName(romName);
        _romSha256 = romSha256;
        _workingProfile = profile.Clone();
        _workingProfile.RomName = _romName;
        _workingProfile.RomSha256 = _romSha256;
        EditedProfile = _workingProfile.Clone();

        using Bitmap background = SmbProfile.ComposeBackground(frame, exactSmbProfile);
        _picker = new TilePickerControl(frame, new Bitmap(background), exactSmbProfile)
        {
            Dock = DockStyle.Fill, CellSize = _workingProfile.CellSize
        };
        _picker.SelectionChanged += (_, _) => SelectedTileChanged();

        Text = "Warp4D · Teach it this game";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1480, 860);
        MinimumSize = new Size(1150, 720);
        BackColor = WindowColor;
        ForeColor = TextColor;
        ShowIcon = false;

        _addButton = MakeButton("ADD PATTERN", primary: true);
        _addButton.Width = 139;
        _addButton.Enabled = false;
        _addButton.Click += (_, _) => AddOrUpdateRule();

        Controls.Add(BuildLayout());
        ApplyProfileToControls();
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            UpdatePreview();
        };
        _kindBox.SelectedIndexChanged += (_, _) => SchedulePreview();
        _labelBox.TextChanged += (_, _) => SchedulePreview();
        UpdatePreview();
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = WindowColor,
            Padding = new Padding(22, 18, 22, 16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 61));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildWorkspace(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
        return root;
    }

    private Control BuildHeader()
    {
        Panel panel = new() { Dock = DockStyle.Fill };
        Label title = MakeLabel("Teach Warp4D this game", 16, TextColor, FontStyle.Bold);
        title.Location = new Point(0, 0);
        Label instructions = MakeLabel(
            "1. Select scenery on the left → 2. Choose its type and name → 3. Add it and check the preview.\nDrag to select; Ctrl adds/removes. Shift+drag draws a rectangle. Wheel zooms; middle-drag pans.",
            9,
            MutedColor);
        instructions.Location = new Point(1, 32);
        instructions.MaximumSize = new Size(1000, 44);

        Label rom = MakeLabel($"ROM  ·  {_romName}  ·  {_romSha256[..Math.Min(12, _romSha256.Length)]}", 8, Lime, FontStyle.Bold);
        rom.Location = new Point(1, 78);
        panel.Controls.Add(title);
        panel.Controls.Add(instructions);
        panel.Controls.Add(rom);
        FlowLayoutPanel tools = new()
        {
            Location = new Point(0, 101), AutoSize = true, WrapContents = false
        };
        _undoButton = MakeButton("UNDO", primary: false);
        _undoButton.Click += (_, _) => UndoEdit();
        _redoButton = MakeButton("REDO", primary: false);
        _redoButton.Click += (_, _) => RedoEdit();
        Button refresh = MakeButton("REFRESH", primary: false);
        refresh.Enabled = _snapshotProvider is not null;
        refresh.Click += (_, _) => RefreshSnapshot();
        Button fit = MakeButton("FIT VIEW", primary: false);
        fit.Click += (_, _) => _picker.ResetView();
        CheckBox rectangle = new()
        {
            Text = "Rectangle selection", AutoSize = true, ForeColor = TextColor,
            Margin = new Padding(10, 9, 8, 0)
        };
        rectangle.CheckedChanged += (_, _) => _picker.RectangleSelection = rectangle.Checked;
        tools.Controls.AddRange([_undoButton, _redoButton, refresh, fit, rectangle]);
        if (_builtInProfile is not null && _builtInProfile.CellSize == _workingProfile.CellSize)
        {
            Button upgrade = MakeButton("ADD BUILT-IN RULES", primary: false);
            upgrade.Width = 170;
            upgrade.Click += (_, _) => AddBuiltInRules();
            tools.Controls.Add(upgrade);
        }
        if(_builtInProfile?.PlayerTracking is not null)
        {
            var tracking=MakeButton("UPDATE PLAYER TRACKING",primary:false);
            tracking.Width=220;
            tracking.AccessibleDescription="Restore this game's built-in player coordinates, body ownership, name and HUD policy. Custom scenery and projection settings are unchanged. Undo is available; Save & Apply is still required.";
            _trackingTip.SetToolTip(tracking,tracking.AccessibleDescription);
            tracking.Click+=(_,_)=>UpdateBuiltInPlayerTracking();
            PlayerTrackingUpgradeButtonForTest=tracking;
            tools.Controls.Add(tracking);
        }
        panel.Controls.Add(tools);
        return panel;
    }

    private Control BuildWorkspace()
    {
        TableLayoutPanel workspace = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 385));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Panel pickerPanel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = PanelColor,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 12, 0)
        };
        pickerPanel.Controls.Add(_picker);
        SplitContainer views = new()
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
            BackColor = BorderColor, SplitterWidth = 6, Size = new Size(1000, 600),
            Panel1MinSize = 300, Panel2MinSize = 300, SplitterDistance = 490,
            Margin = new Padding(0, 0, 12, 0)
        };
        views.Panel1.Controls.Add(pickerPanel);
        views.Panel2.Controls.Add(_preview);
        workspace.Controls.Add(views, 0, 0);
        workspace.Controls.Add(BuildRulePanel(), 1, 0);
        return workspace;
    }

    private Control BuildRulePanel()
    {
        // Keep every rule control reachable when the workspace is short (or
        // Windows scales its fonts). A percent-height list must not push the
        // last instructions underneath the fixed footer.
        Panel scrollHost = new()
        {
            Name = "RulePanelScrollHost",
            Dock = DockStyle.Fill,
            BackColor = PanelColor,
            AutoScroll = true,
            AutoScrollMinSize = new Size(0, 580)
        };
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Top,
            Height = 580,
            BackColor = PanelColor,
            Padding = new Padding(16, 14, 16, 12),
            ColumnCount = 1,
            RowCount = 9
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        Panel namePanel = new() { Dock = DockStyle.Fill };
        Label profileName = MakeLabel("PROFILE NAME", 8, Lime, FontStyle.Bold);
        profileName.Location = new Point(0, 0);
        _nameBox.Location = new Point(0, 21);
        _nameBox.Width = 330;
        _nameBox.MaxLength = 64;
        StyleTextBox(_nameBox);
        namePanel.Controls.Add(profileName);
        namePanel.Controls.Add(_nameBox);
        namePanel.SizeChanged += (_, _) => _nameBox.Width = namePanel.ClientSize.Width;
        panel.Controls.Add(namePanel, 0, 0);

        _signatureLabel.Dock = DockStyle.Fill;
        _signatureLabel.AutoSize = false;
        panel.Controls.Add(_signatureLabel, 0, 1);

        Panel kindPanel = new() { Dock = DockStyle.Fill };
        Label kindLabel = MakeLabel("WHAT IS THIS OBJECT?", 8, MutedColor, FontStyle.Bold);
        kindLabel.Location = new Point(0, 0);
        _kindBox.Location = new Point(0, 22);
        _kindBox.Width = 330;
        _kindBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _kindBox.BackColor = Color.FromArgb(19, 26, 37);
        _kindBox.ForeColor = TextColor;
        _kindBox.Font = new Font("Segoe UI", 9.5f);
        foreach (SceneObjectKind kind in BackgroundKinds)
        {
            _kindBox.Items.Add(new KindChoice(kind));
        }
        _kindBox.SelectedIndex = 0;
        _kindBox.SelectedIndexChanged += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_labelBox.Text) && _kindBox.SelectedItem is KindChoice choice)
            {
                _labelBox.Text = ProjectionProfile.DisplayName(choice.Kind);
            }
        };
        kindPanel.Controls.Add(kindLabel);
        kindPanel.Controls.Add(_kindBox);
        kindPanel.SizeChanged += (_, _) => _kindBox.Width = kindPanel.ClientSize.Width;
        panel.Controls.Add(kindPanel, 0, 2);

        Panel labelPanel = new() { Dock = DockStyle.Fill };
        Label labelTitle = MakeLabel("GIVE IT A NAME", 8, MutedColor, FontStyle.Bold);
        labelTitle.Location = new Point(0, 0);
        _labelBox.Location = new Point(0, 22);
        _labelBox.Width = 330;
        _labelBox.MaxLength = 48;
        StyleTextBox(_labelBox);
        labelPanel.Controls.Add(labelTitle);
        labelPanel.Controls.Add(_labelBox);
        labelPanel.SizeChanged += (_, _) => _labelBox.Width = labelPanel.ClientSize.Width;
        panel.Controls.Add(labelPanel, 0, 3);

        FlowLayoutPanel addRow = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _addButton.Width = 120;
        addRow.Controls.Add(_addButton);
        Button remove = MakeButton("DELETE RULE", primary: false);
        remove.Width = 90;
        remove.Font = new Font("Segoe UI Semibold", 7.5f, FontStyle.Bold);
        remove.Click += (_, _) => RemoveSelectedRule();
        addRow.Controls.Add(remove);
        Button clear = MakeButton("CLEAR CELLS", primary: false);
        clear.Width = 90;
        clear.Font = new Font("Segoe UI Semibold", 7.5f, FontStyle.Bold);
        clear.Click += (_, _) => _picker.ClearSelection();
        addRow.Controls.Add(clear);
        panel.Controls.Add(addRow, 0, 4);

        _ruleCountLabel.Dock = DockStyle.Fill;
        panel.Controls.Add(_ruleCountLabel, 0, 5);

        _rulesList.Dock = DockStyle.Fill;
        _rulesList.BackColor = Color.FromArgb(10, 15, 23);
        _rulesList.ForeColor = TextColor;
        _rulesList.BorderStyle = BorderStyle.FixedSingle;
        _rulesList.Font = new Font("Consolas", 8.5f);
        _rulesList.DrawMode = DrawMode.OwnerDrawFixed;
        _rulesList.ItemHeight = 48;
        _rulesList.IntegralHeight = false;
        _rulesList.DrawItem += DrawRuleItem;
        _rulesList.SelectedIndexChanged += (_, _) => SelectedRuleChanged();
        panel.Controls.Add(_rulesList, 0, 6);

        Label help = MakeLabel(
            "Refresh to capture another animation frame. Adding it keeps earlier artwork variants. Ctrl+Z / Ctrl+Y undo / redo.",
            8,
            MutedColor);
        help.Dock = DockStyle.Fill;
        help.AutoSize = false;
        panel.Controls.Add(help, 0, 7);

        Label projectionHelp = MakeLabel(
            "Use PROJECTION in the main window to set per-class 4D depth and 2D/4D toggles.",
            8,
            Lime,
            FontStyle.Bold);
        projectionHelp.Name = "ProjectionHelp";
        projectionHelp.Dock = DockStyle.Fill;
        projectionHelp.AutoSize = false;
        panel.Controls.Add(projectionHelp, 0, 8);
        scrollHost.Controls.Add(panel);
        scrollHost.SizeChanged += (_, _) => panel.Height = Math.Max(580, scrollHost.ClientSize.Height);
        return scrollHost;
    }

    private Control BuildFooter()
    {
        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        FlowLayoutPanel files = new()
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        Button import = MakeButton("IMPORT…", primary: false);
        import.Click += (_, _) => ImportProfile();
        Button export = MakeButton("EXPORT…", primary: false);
        export.Click += (_, _) => ExportProfile();
        files.Controls.Add(import);
        files.Controls.Add(export);
        if(_builtInProfile?.SpriteAssemblies is {Count:>0})
        {
            var shapes=MakeButton("UPDATE SPRITE SHAPES",primary:false);shapes.Width=210;
            shapes.AccessibleDescription="Update built-in sprite grouping so nearby objects stay separate. Only sprite layouts change; custom scenery, player tracking and HUD settings stay unchanged. Undo is available; Save & Apply is required.";
            _trackingTip.SetToolTip(shapes,shapes.AccessibleDescription);
            shapes.Click+=(_,_)=>UpdateBuiltInSpriteAssemblies();
            SpriteAssemblyUpgradeButtonForTest=shapes;files.Controls.Add(shapes);
        }
        if(_builtInProfile?.FlatSpriteRegions is {Count:>0})
        {
            var hud=MakeButton("UPDATE HUD PROTECTION",primary:false);hud.Width=210;
            hud.AccessibleDescription="Update built-in sprite-only HUD regions and state-specific HUD ownership, allowing scenery behind them to project. Other custom regions and rules stay unchanged. Undo is available; Save & Apply is required.";
            _trackingTip.SetToolTip(hud,hud.AccessibleDescription);
            hud.Click+=(_,_)=>UpdateBuiltInHudProtection();HudProtectionUpgradeButtonForTest=hud;files.Controls.Add(hud);
        }
        footer.Controls.Add(files, 0, 0);

        FlowLayoutPanel confirmation = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        Button cancel = MakeButton("CANCEL", primary: false);
        cancel.DialogResult = DialogResult.Cancel;
        Button save = MakeButton("SAVE & APPLY", primary: true);
        save.Width = 128;
        save.Click += (_, _) => SaveAndApply();
        confirmation.Controls.Add(cancel);
        confirmation.Controls.Add(save);
        footer.Controls.Add(confirmation, 1, 0);
        CancelButton = cancel;
        AcceptButton = save;
        return footer;
    }

    private void ApplyProfileToControls()
    {
        if (_picker.CellSize != _workingProfile.CellSize) _picker.ClearSelection();
        _picker.CellSize = _workingProfile.CellSize;
        _nameBox.Text = _workingProfile.Name;
        RefreshRuleList();
        UpdateHistoryButtons();
        SchedulePreview();
    }

    private void SelectedTileChanged()
    {
        IReadOnlyList<TileSelection> selectedCells = _picker.SelectedTiles;
        if (selectedCells.Count == 0)
        {
            _addButton.Enabled = false;
            _addButton.Text = "ADD PATTERN";
            _signatureLabel.Text = $"Click or drag across {_picker.CellSize}×{_picker.CellSize} cells in the game snapshot.";
            SchedulePreview();
            return;
        }

        TileSelection[] patterns = selectedCells
            .GroupBy(selection => selection.Signature.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
        _addButton.Enabled = true;

        if (patterns.Length == 1 && selectedCells.Count == 1)
        {
            MetatileSignature signature = patterns[0].Signature;
            int visibleMatches = _picker.CountVisibleOccurrences(signature);
            _signatureLabel.Text = $"{signature.Key} · {visibleMatches} visible match{(visibleMatches == 1 ? string.Empty : "es")}\n{signature.Description}";
            if (_workingProfile.BackgroundRules.TryGetValue(signature.Key, out BackgroundObjectRule? existing))
            {
                SelectKind(existing.ObjectKind);
                _labelBox.Text = existing.Label;
            }
            else if (_kindBox.SelectedItem is KindChoice choice)
            {
                _labelBox.Text = ProjectionProfile.DisplayName(choice.Kind);
            }
        }
        else
        {
            _signatureLabel.Text =
                $"{selectedCells.Count} cells · {patterns.Length} unique pattern{(patterns.Length == 1 ? string.Empty : "s")} selected\n" +
                "One class and label will be applied to the whole selection.";
        }

        int existingCount = patterns.Count(pattern =>
            _workingProfile.BackgroundRules.ContainsKey(pattern.Signature.Key));
        _addButton.Text = patterns.Length == 1
            ? existingCount == 1 ? "UPDATE PATTERN" : "ADD PATTERN"
            : existingCount == 0
                ? $"ADD {patterns.Length} PATTERNS"
                : existingCount == patterns.Length
                    ? $"UPDATE {patterns.Length} PATTERNS"
                    : $"APPLY TO {patterns.Length}";
        SchedulePreview();
    }

    private void AddOrUpdateRule()
    {
        TileSelection[] patterns = _picker.SelectedTiles
            .GroupBy(selection => selection.Signature.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
        if (patterns.Length == 0 || _kindBox.SelectedItem is not KindChoice choice)
        {
            return;
        }

        (TileSelection Selection, int VisibleMatches)[] commonPatterns = patterns
            .Select(selection => (
                Selection: selection,
                VisibleMatches: _picker.CountVisibleOccurrences(selection.Signature)))
            .Where(result => result.VisibleMatches > 60)
            .ToArray();
        if (commonPatterns.Length > 0)
        {
            int largestMatchCount = commonPatterns.Max(result => result.VisibleMatches);
            DialogResult answer = MessageBox.Show(
                this,
                $"{commonPatterns.Length} selected pattern{(commonPatterns.Length == 1 ? string.Empty : "s")} appear very frequently in this viewport (up to {largestMatchCount} matches). They may be empty sky or broad background fills and could create very large projected objects. Add them anyway?",
                "Very common tile patterns",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        string label = string.IsNullOrWhiteSpace(_labelBox.Text)
            ? ProjectionProfile.DisplayName(choice.Kind)
            : _labelBox.Text.Trim();
        RecordEdit();
        foreach (TileSelection selection in _picker.SelectedTiles)
        {
            if (!_workingProfile.BackgroundRules.TryGetValue(selection.Signature.Key, out BackgroundObjectRule? rule))
                rule = new BackgroundObjectRule();
            rule.Kind = choice.Kind.ToString();
            rule.Label = label;
            rule.CaptureArtwork(selection.VisualFingerprint, _picker.CaptureThumbnail(selection));
            _workingProfile.BackgroundRules[selection.Signature.Key] = rule;
        }
        RefreshRuleList(patterns[^1].Signature.Key);
        SelectedTileChanged();
    }

    private void RemoveSelectedRule()
    {
        if (_rulesList.SelectedItem is not RuleListItem selected)
        {
            return;
        }
        RecordEdit();
        _workingProfile.BackgroundRules.Remove(selected.SignatureKey);
        RefreshRuleList();
        SelectedTileChanged();
    }

    private void SelectedRuleChanged()
    {
        if (_rulesList.SelectedItem is not RuleListItem selected ||
            !_workingProfile.BackgroundRules.TryGetValue(selected.SignatureKey, out BackgroundObjectRule? rule))
        {
            return;
        }
        SelectKind(rule.ObjectKind);
        _labelBox.Text = rule.Label;
    }

    private void SelectKind(SceneObjectKind kind)
    {
        for (int index = 0; index < _kindBox.Items.Count; index++)
        {
            if (_kindBox.Items[index] is KindChoice choice && choice.Kind == kind)
            {
                _kindBox.SelectedIndex = index;
                return;
            }
        }
    }

    private void RefreshRuleList(string? selectKey = null)
    {
        _rulesList.BeginUpdate();
        foreach (Bitmap thumbnail in _ruleThumbnails.Values) thumbnail.Dispose();
        _ruleThumbnails.Clear();
        _rulesList.Items.Clear();
        foreach ((string key, BackgroundObjectRule rule) in _workingProfile.BackgroundRules.OrderBy(pair => pair.Value.Label))
        {
            RuleListItem item = new(key, rule);
            string? png = rule.ArtworkVariants.Select(variant => variant.ThumbnailPng)
                .FirstOrDefault(value => !string.IsNullOrEmpty(value));
            if (png is not null)
            {
                try
                {
                    using MemoryStream stream = new(Convert.FromBase64String(png));
                    using Bitmap image = new(stream);
                    _ruleThumbnails[key] = new Bitmap(image);
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException)
                {
                    // A missing thumbnail does not invalidate recognition artwork.
                }
            }
            int index = _rulesList.Items.Add(item);
            if (key.Equals(selectKey, StringComparison.OrdinalIgnoreCase))
            {
                _rulesList.SelectedIndex = index;
            }
        }
        _rulesList.EndUpdate();
        int count = _workingProfile.BackgroundRules.Count;
        int legacyCount = _workingProfile.BackgroundRules.Values.Count(rule => !rule.HasVisualFingerprint);
        _ruleCountLabel.Text = legacyCount == 0
            ? $"{count} captured pattern{(count == 1 ? string.Empty : "s")}"
            : $"{count} captured · {legacyCount} need visual update";
    }

    private GameRecognitionProfile ReadProfileFromControls()
    {
        GameRecognitionProfile profile = _workingProfile.Clone();
        profile.Name = _nameBox.Text;
        profile.RomName = _romName;
        profile.RomSha256 = _romSha256;
        profile.Normalize();
        return profile;
    }

    private void RecordEdit()
    {
        _workingProfile.Name = _nameBox.Text;
        _undo.Push(_workingProfile.Clone());
        _redo.Clear();
        UpdateHistoryButtons();
    }

    // Opt-in, undoable expansion: retain the user's classifications, settings,
    // name and existing artwork. Saving remains the user's explicit decision.
    internal void AddBuiltInRules()
    {
        if (_builtInProfile is null || _builtInProfile.CellSize != _workingProfile.CellSize) return;
        RecordEdit();
        foreach (var pair in _builtInProfile.BackgroundRules)
        {
            if (!_workingProfile.BackgroundRules.TryGetValue(pair.Key, out var existing))
                _workingProfile.BackgroundRules[pair.Key] = pair.Value.Clone();
            else if (existing.HasVisualFingerprint)
                foreach (var variant in pair.Value.ArtworkVariants)
                    if (!existing.ArtworkVariants.Any(v => v.Fingerprint.Equals(variant.Fingerprint, StringComparison.OrdinalIgnoreCase)))
                        existing.ArtworkVariants.Add(variant.Clone());
        }
        foreach (Rectangle region in _builtInProfile.FlatRegions)
            if (!_workingProfile.FlatRegions.Contains(region)) _workingProfile.FlatRegions.Add(region);
        foreach (var region in _builtInProfile.ConditionalFlatRegions ?? [])
        {
            _workingProfile.ConditionalFlatRegions ??= [];
            if (!_workingProfile.ConditionalFlatRegions.Any(existing => existing.Bounds == region.Bounds &&
                existing.ExpectedRam.Count == region.ExpectedRam.Count && region.ExpectedRam.All(e =>
                    existing.ExpectedRam.TryGetValue(e.Key, out int value) && value == e.Value) &&
                (existing.ExcludedRam?.Count??0)==(region.ExcludedRam?.Count??0) &&
                (region.ExcludedRam?.All(e=>existing.ExcludedRam?.TryGetValue(e.Key,out int value)==true && value==e.Value)??true)))
                _workingProfile.ConditionalFlatRegions.Add(new ConditionalFlatRegion
                    { Bounds = region.Bounds, ExpectedRam = new(region.ExpectedRam), ExcludedRam=region.ExcludedRam is null ? null : new(region.ExcludedRam) });
        }
        _workingProfile.Normalize();
        ApplyProfileToControls();
        SelectedTileChanged();
    }

    internal void UpdateBuiltInHudProtection()
    {
        if(_builtInProfile?.FlatSpriteRegions is not {Count:>0} regions)return;
        RecordEdit();
        _workingProfile.FlatSpriteRegions??=[];
        foreach(var region in regions)
        {
            _workingProfile.FlatRegions.RemoveAll(existing=>existing==region);
            if(!_workingProfile.FlatSpriteRegions.Contains(region))_workingProfile.FlatSpriteRegions.Add(region);
        }
        if(_builtInProfile.FlatSpriteSlotsByState is {} builtInSlots &&
           (_workingProfile.FlatSpriteSlotsByState is null || _workingProfile.FlatSpriteStateAddress==_builtInProfile.FlatSpriteStateAddress))
        {
            _workingProfile.FlatSpriteStateAddress=_builtInProfile.FlatSpriteStateAddress;
            _workingProfile.FlatSpriteSlotsByState??=[];
            foreach(var pair in builtInSlots)
                _workingProfile.FlatSpriteSlotsByState[pair.Key]=(_workingProfile.FlatSpriteSlotsByState.GetValueOrDefault(pair.Key)??[]).Concat(pair.Value).Distinct().ToList();
        }
        _workingProfile.Normalize();ApplyProfileToControls();SelectedTileChanged();
    }

    internal void UpdateBuiltInPlayerTracking()
    {
        if(_builtInProfile?.PlayerTracking is null)return;
        RecordEdit();
        _workingProfile.PlayerTracking=_builtInProfile.Clone().PlayerTracking;
        _workingProfile.PlayerLabel=_builtInProfile.PlayerLabel;
        _workingProfile.PlayerOverlaysFlatHud=_builtInProfile.PlayerOverlaysFlatHud;
        _workingProfile.Normalize();
        ApplyProfileToControls();
        SelectedTileChanged();
        _signatureLabel.Text="Player tracking updated. Custom scenery is unchanged. Save & Apply to keep it, or Undo.";
    }

    internal void UpdateBuiltInSpriteAssemblies()
    {
        if(_builtInProfile?.SpriteAssemblies is not {Count:>0})return;
        RecordEdit();
        _workingProfile.SpriteAssemblies=_builtInProfile.SpriteAssemblies.Select(a=>a.Clone()).ToList();
        _workingProfile.Normalize();
        ApplyProfileToControls();SelectedTileChanged();
        _signatureLabel.Text="Sprite shapes updated. Custom scenery is unchanged. Save & Apply to keep it, or Undo.";
    }

    internal void UndoEdit()
    {
        if (_undo.Count == 0) return;
        _redo.Push(ReadProfileFromControls());
        _workingProfile = _undo.Pop();
        ApplyProfileToControls();
        SelectedTileChanged();
    }

    internal void RedoEdit()
    {
        if (_redo.Count == 0) return;
        _undo.Push(ReadProfileFromControls());
        _workingProfile = _redo.Pop();
        ApplyProfileToControls();
        SelectedTileChanged();
    }

    private void UpdateHistoryButtons()
    {
        if (_undoButton is not null) _undoButton.Enabled = _undo.Count > 0;
        if (_redoButton is not null) _redoButton.Enabled = _redo.Count > 0;
    }

    private void RefreshSnapshot()
    {
        NesFrame? frame = _snapshotProvider?.Invoke();
        if (frame is null) return;
        _frame = frame;
        _picker.SetSnapshot(frame);
        SchedulePreview();
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    internal void UpdatePreview()
    {
        GameRecognitionProfile candidate = ReadProfileFromControls();
        if (_kindBox.SelectedItem is KindChoice choice)
        {
            string label = string.IsNullOrWhiteSpace(_labelBox.Text)
                ? ProjectionProfile.DisplayName(choice.Kind) : _labelBox.Text.Trim();
            foreach (TileSelection selection in _picker.SelectedTiles)
            {
                if (!candidate.BackgroundRules.TryGetValue(selection.Signature.Key, out BackgroundObjectRule? rule))
                    rule = new BackgroundObjectRule();
                rule.Kind = choice.Kind.ToString();
                rule.Label = label;
                rule.CaptureArtwork(selection.VisualFingerprint);
                candidate.BackgroundRules[selection.Signature.Key] = rule;
            }
        }
        _preview.SetScene(_previewRecognizer.Build(_frame, _exactSmbProfile, _preview.Settings.ProjectionProfile, candidate));
    }

    private void DrawRuleItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _rulesList.Items[e.Index] is not RuleListItem item) return;
        e.DrawBackground();
        Rectangle imageBounds = new(e.Bounds.Left + 5, e.Bounds.Top + 6, 36, 36);
        if (_ruleThumbnails.TryGetValue(item.SignatureKey, out Bitmap? thumbnail))
        {
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.DrawImage(thumbnail, imageBounds);
        }
        else
        {
            using Pen border = new(MutedColor);
            e.Graphics.DrawRectangle(border, imageBounds);
        }
        TextRenderer.DrawText(e.Graphics, item.Rule.Label, Font,
            new Rectangle(e.Bounds.Left + 48, e.Bounds.Top + 4, e.Bounds.Width - 52, 21),
            TextColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string detail = $"{item.Rule.ObjectKind} · {item.Rule.ArtworkVariants.Count} artwork · {item.SignatureKey}";
        TextRenderer.DrawText(e.Graphics, detail, _rulesList.Font,
            new Rectangle(e.Bounds.Left + 48, e.Bounds.Top + 25, e.Bounds.Width - 52, 20),
            MutedColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { UndoEdit(); return true; }
        if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
        { RedoEdit(); return true; }
        if (keyData == Keys.Escape && _picker.SelectedCellCount > 0)
        { _picker.ClearSelection(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    internal int RuleCountForTest => _workingProfile.BackgroundRules.Count;
    internal GameRecognitionProfile WorkingProfileForTest => ReadProfileFromControls();
    internal void ApplySelectedForTest() => AddOrUpdateRule();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewTimer.Dispose();
            _trackingTip.Dispose();
            foreach (Bitmap thumbnail in _ruleThumbnails.Values) thumbnail.Dispose();
            _ruleThumbnails.Clear();
        }
        base.Dispose(disposing);
    }

    private void SaveAndApply()
    {
        try
        {
            GameRecognitionProfile profile = ReadProfileFromControls();
            GameRecognitionProfileStore.Save(profile);
            EditedProfile = profile.Clone();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not save game profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportProfile()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Import Warp4D game profile",
            Filter = "Warp4D game profiles (*.warp4d-game.json)|*.warp4d-game.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            GameRecognitionProfile imported = GameRecognitionProfileStore.ReadFromFile(dialog.FileName);
            if (!imported.RomSha256.Equals(_romSha256, StringComparison.OrdinalIgnoreCase))
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "This profile was created for a different ROM. Retarget its pattern rules to the currently loaded ROM?",
                    "Different ROM hash",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }
            imported.RomName = _romName;
            imported.RomSha256 = _romSha256;
            RecordEdit();
            _workingProfile = imported;
            ApplyProfileToControls();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not import game profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportProfile()
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Export Warp4D game profile",
            Filter = "Warp4D game profiles (*.warp4d-game.json)|*.warp4d-game.json|JSON files (*.json)|*.json",
            FileName = SafeFileName(_nameBox.Text) + ".warp4d-game.json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            GameRecognitionProfileStore.WriteToFile(dialog.FileName, ReadProfileFromControls());
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not export game profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string SafeFileName(string name)
    {
        string cleaned = new((string.IsNullOrWhiteSpace(name) ? "game-profile" : name)
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)
            .ToArray());
        cleaned = cleaned.Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "game-profile" : cleaned;
    }

    private static void StyleTextBox(TextBox box)
    {
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = Color.FromArgb(19, 26, 37);
        box.ForeColor = TextColor;
        box.Font = new Font("Segoe UI", 9.5f);
    }

    private static Label MakeLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = color,
        Font = new Font("Segoe UI", size, style),
        BackColor = Color.Transparent,
        Margin = Padding.Empty
    };

    private static Button MakeButton(string text, bool primary)
    {
        Button button = new()
        {
            Text = text,
            Size = new Size(94, 36),
            Margin = new Padding(0, 0, 7, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Lime : Color.FromArgb(19, 26, 37),
            ForeColor = primary ? Color.FromArgb(13, 20, 10) : TextColor,
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseMnemonic = false
        };
        button.FlatAppearance.BorderColor = primary ? Lime : BorderColor;
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private sealed record KindChoice(SceneObjectKind Kind)
    {
        public override string ToString() => ProjectionProfile.DisplayName(Kind);
    }

    private sealed record RuleListItem(string SignatureKey, BackgroundObjectRule Rule)
    {
        public override string ToString() =>
            $"{SignatureKey}  {(Rule.HasVisualFingerprint ? "VISUAL" : "UPDATE"),-6}  {Rule.ObjectKind,-13}  {Rule.Label}";
    }
}

internal sealed class TilePickerControl : Control
{
    private static readonly Color Lime = Color.FromArgb(173, 255, 93);
    private static readonly Color Cyan = Color.FromArgb(91, 220, 255);
    private NesFrame _frame;
    private Bitmap _background;
    private readonly bool _exactSmbProfile;
    private readonly Dictionary<long, TileSelection> _selectedCells = [];
    private readonly HashSet<long> _dragVisited = [];
    private RectangleF _destination;
    private TileSelection? _primarySelection;
    private TileSelection? _hoverSelection;
    private TileSelection? _lastDragSelection;
    private bool _dragging;
    private bool _dragAdds;
    private float _zoom = 1f;
    private PointF _pan;
    private bool _panning;
    private Point _panStart;
    private bool _rectangleDrag;
    private TileSelection _rectangleStart;
    private readonly Dictionary<long, TileSelection> _rectangleBase = [];
    public bool RectangleSelection { get; set; }
    internal int CellSize { get; set; } = 16;
    private int CellStep => CellSize / 8;
    internal float Zoom => _zoom;

    public IReadOnlyList<TileSelection> SelectedTiles => _selectedCells.Values.ToArray();
    public int SelectedCellCount => _selectedCells.Count;
    public int SelectedPatternCount => _selectedCells.Values
        .Select(selection => selection.Signature.Key)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    public event EventHandler? SelectionChanged;

    public TilePickerControl(NesFrame frame, Bitmap background, bool exactSmbProfile)
    {
        _frame = frame;
        _background = background;
        _exactSmbProfile = exactSmbProfile;
        BackColor = Color.FromArgb(5, 8, 12);
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        TabStop = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _background.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.Clear(BackColor);
        float scale = Math.Min((ClientSize.Width - 20) / 256f, (ClientSize.Height - 20) / 240f);
        scale = Math.Max(0.1f, scale) * _zoom;
        _destination = new RectangleF(
            (ClientSize.Width - 256 * scale) / 2f + _pan.X,
            (ClientSize.Height - 240 * scale) / 2f + _pan.Y,
            256 * scale,
            240 * scale);

        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(_background, _destination);
        graphics.SetClip(_destination);

        using Pen grid = new(Color.FromArgb(55, 222, 235, 244), 1f);
        for (int gameX = 0; gameX <= 256; gameX++)
        {
            if (Mod(_frame.ScrollX + gameX, CellSize) == 0)
            {
                float x = _destination.Left + gameX * scale;
                graphics.DrawLine(grid, x, _destination.Top, x, _destination.Bottom);
            }
        }
        for (int gameY = 0; gameY <= 240; gameY++)
        {
            if (Mod(_frame.ScrollY + gameY, CellSize) == 0)
            {
                float y = _destination.Top + gameY * scale;
                graphics.DrawLine(grid, _destination.Left, y, _destination.Right, y);
            }
        }

        if (_hoverSelection is TileSelection hover &&
            !_selectedCells.ContainsKey(CellKey(hover)))
        {
            RectangleF hoveredBounds = CellBounds(hover, scale);
            using Brush hoverFill = new SolidBrush(Color.FromArgb(30, Color.White));
            using Pen hoverEdge = new(Color.FromArgb(155, Color.White), Math.Max(1f, scale * 0.45f));
            graphics.FillRectangle(hoverFill, hoveredBounds);
            graphics.DrawRectangle(
                hoverEdge,
                hoveredBounds.X,
                hoveredBounds.Y,
                hoveredBounds.Width,
                hoveredBounds.Height);
        }

        using Brush selectedFill = new SolidBrush(Color.FromArgb(55, Cyan));
        using Pen selectedEdge = new(Cyan, Math.Max(1.5f, scale * 0.65f));
        using Brush primaryFill = new SolidBrush(Color.FromArgb(65, Lime));
        using Pen primaryEdge = new(Lime, Math.Max(2f, scale * 0.85f));
        foreach (TileSelection selection in _selectedCells.Values)
        {
            bool primary = _primarySelection is TileSelection active &&
                active.WorldTileX == selection.WorldTileX &&
                active.WorldTileY == selection.WorldTileY &&
                active.ScreenAnchored == selection.ScreenAnchored;
            RectangleF bounds = CellBounds(selection, scale);
            graphics.FillRectangle(primary ? primaryFill : selectedFill, bounds);
            graphics.DrawRectangle(
                primary ? primaryEdge : selectedEdge,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height);
        }
        graphics.ResetClip();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Middle)
        {
            _panning = true;
            _panStart = e.Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
            return;
        }
        if (e.Button != MouseButtons.Left || !TryReadSelection(e.Location, out TileSelection selection))
        {
            return;
        }

        bool control = (ModifierKeys & Keys.Control) == Keys.Control;
        long key = CellKey(selection);
        _dragging = true;
        _dragAdds = !control || !_selectedCells.ContainsKey(key);
        _dragVisited.Clear();
        _lastDragSelection = null;
        _rectangleDrag = RectangleSelection || (ModifierKeys & Keys.Shift) != 0;
        _rectangleStart = selection;
        Capture = true;

        if (!control)
        {
            _selectedCells.Clear();
        }
        _rectangleBase.Clear();
        foreach ((long cell, TileSelection value) in _selectedCells) _rectangleBase[cell] = value;
        if (_rectangleDrag) ApplyRectangle(selection);
        else ApplyDragSelectionPath(selection);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            _pan = new PointF(_pan.X + e.X - _panStart.X, _pan.Y + e.Y - _panStart.Y);
            _panStart = e.Location;
            Invalidate();
            return;
        }
        TileSelection? previousHover = _hoverSelection;
        _hoverSelection = TryReadSelection(e.Location, out TileSelection hover) ? hover : null;

        if (_dragging && (e.Button & MouseButtons.Left) != 0 && _hoverSelection is TileSelection selection)
        {
            if (_rectangleDrag) ApplyRectangle(selection);
            else ApplyDragSelectionPath(selection);
        }
        else if (previousHover != _hoverSelection)
        {
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Middle)
        {
            _panning = false;
            Capture = false;
            Cursor = Cursors.Cross;
        }
        if (e.Button == MouseButtons.Left)
        {
            EndDrag();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_dragging && _hoverSelection is not null)
        {
            _hoverSelection = null;
            Invalidate();
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            _panning = false;
            Cursor = Cursors.Cross;
            EndDrag();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            ClearSelection();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void ApplyDragSelectionPath(TileSelection selection)
    {
        if (_lastDragSelection is not TileSelection previous ||
            previous.ScreenAnchored != selection.ScreenAnchored)
        {
            if (ApplyDragSelection(selection))
            {
                PublishSelectionChanged();
            }
            _lastDragSelection = selection;
            return;
        }

        int x0 = previous.WorldTileX / CellStep;
        int y0 = previous.WorldTileY / CellStep;
        int x1 = selection.WorldTileX / CellStep;
        int y1 = selection.WorldTileY / CellStep;
        int deltaX = Math.Abs(x1 - x0);
        int stepX = x0 < x1 ? 1 : -1;
        int deltaY = -Math.Abs(y1 - y0);
        int stepY = y0 < y1 ? 1 : -1;
        int error = deltaX + deltaY;
        bool changed = false;
        while (true)
        {
            changed |= ApplyDragSelection(ReadWorldSelection(x0 * CellStep, y0 * CellStep, selection.ScreenAnchored));
            if (x0 == x1 && y0 == y1)
            {
                break;
            }
            int doubledError = error * 2;
            if (doubledError >= deltaY)
            {
                error += deltaY;
                x0 += stepX;
            }
            if (doubledError <= deltaX)
            {
                error += deltaX;
                y0 += stepY;
            }
        }
        _lastDragSelection = selection;
        if (changed)
        {
            PublishSelectionChanged();
        }
    }

    private void ApplyRectangle(TileSelection selection)
    {
        if (selection.ScreenAnchored != _rectangleStart.ScreenAnchored) return;
        _selectedCells.Clear();
        foreach ((long key, TileSelection cell) in _rectangleBase) _selectedCells[key] = cell;
        int minX = Math.Min(selection.WorldTileX, _rectangleStart.WorldTileX);
        int maxX = Math.Max(selection.WorldTileX, _rectangleStart.WorldTileX);
        int minY = Math.Min(selection.WorldTileY, _rectangleStart.WorldTileY);
        int maxY = Math.Max(selection.WorldTileY, _rectangleStart.WorldTileY);
        for (int y = minY; y <= maxY; y += CellStep)
        for (int x = minX; x <= maxX; x += CellStep)
        {
            TileSelection cell = ReadWorldSelection(x, y, selection.ScreenAnchored);
            if (_dragAdds) _selectedCells[CellKey(cell)] = cell;
            else _selectedCells.Remove(CellKey(cell));
        }
        _primarySelection = _selectedCells.Count == 0 ? null : _selectedCells.Values.Last();
        PublishSelectionChanged();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomAt(e.Location, e.Delta > 0 ? 1.25f : 0.8f);
    }

    internal void ZoomAt(Point cursor, float multiplier)
    {
        float previous = _zoom;
        _zoom = Math.Clamp(_zoom * multiplier, 1f, 8f);
        float ratio = _zoom / previous;
        _pan = new PointF(
            cursor.X - ClientSize.Width / 2f - (cursor.X - ClientSize.Width / 2f - _pan.X) * ratio,
            cursor.Y - ClientSize.Height / 2f - (cursor.Y - ClientSize.Height / 2f - _pan.Y) * ratio);
        Invalidate();
    }

    internal void ResetView()
    {
        _zoom = 1;
        _pan = PointF.Empty;
        Invalidate();
    }

    internal void SetSnapshot(NesFrame frame)
    {
        EndDrag();
        _frame = frame;
        Bitmap old = _background;
        _background = SmbProfile.ComposeBackground(frame, _exactSmbProfile);
        old.Dispose();
        ClearSelection();
        _hoverSelection = null;
        Invalidate();
    }

    internal string CaptureThumbnail(TileSelection selection)
    {
        using Bitmap image = new(CellSize, CellSize, PixelFormat.Format32bppArgb);
        for (int y = 0; y < CellSize; y++)
        for (int x = 0; x < CellSize; x++)
        {
            int worldX = Mod(selection.WorldTileX * 8 + x, 512);
            int worldY = Mod(selection.WorldTileY * 8 + y, 480);
            int table = (worldX >= 256 ? 1 : 0) + (worldY >= 240 ? 2 : 0);
            int color = _frame.NametablePixels[table][(worldY % 240) * 256 + (worldX % 256)];
            image.SetPixel(x, y, Color.FromArgb(color | unchecked((int)0xFF000000)));
        }
        using MemoryStream stream = new();
        image.Save(stream, ImageFormat.Png);
        return Convert.ToBase64String(stream.ToArray());
    }

    private bool ApplyDragSelection(TileSelection selection)
    {
        long key = CellKey(selection);
        if (!_dragVisited.Add(key))
        {
            return false;
        }

        if (_dragAdds)
        {
            _selectedCells[key] = selection;
            _primarySelection = selection;
            return true;
        }
        if (_selectedCells.Remove(key))
        {
            _primarySelection = _selectedCells.Count == 0 ? null : _selectedCells.Values.Last();
            return true;
        }
        return false;
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        _dragVisited.Clear();
        _lastDragSelection = null;
        if (Capture)
        {
            Capture = false;
        }
    }

    internal void SelectGamePixel(int gameX, int gameY, bool additive = false)
    {
        TileSelection selection = ReadSelection(gameX, gameY);
        if (!additive)
        {
            _selectedCells.Clear();
        }
        _selectedCells[CellKey(selection)] = selection;
        _primarySelection = selection;
        PublishSelectionChanged();
    }

    internal void SelectGameDrag(
        int startGameX,
        int startGameY,
        int endGameX,
        int endGameY,
        bool additive = false)
    {
        if (!additive)
        {
            _selectedCells.Clear();
        }
        _dragAdds = true;
        if (RectangleSelection)
        {
            _rectangleStart = ReadSelection(startGameX, startGameY);
            _rectangleBase.Clear();
            foreach ((long cell, TileSelection value) in _selectedCells) _rectangleBase[cell] = value;
            ApplyRectangle(ReadSelection(endGameX, endGameY));
            return;
        }
        _dragVisited.Clear();
        _lastDragSelection = null;
        ApplyDragSelectionPath(ReadSelection(startGameX, startGameY));
        ApplyDragSelectionPath(ReadSelection(endGameX, endGameY));
        _dragVisited.Clear();
        _lastDragSelection = null;
    }

    internal void ClearSelection()
    {
        if (_selectedCells.Count == 0)
        {
            return;
        }
        _selectedCells.Clear();
        _primarySelection = null;
        PublishSelectionChanged();
    }

    private bool TryReadSelection(Point location, out TileSelection selection)
    {
        if (!_destination.Contains(location))
        {
            selection = default;
            return false;
        }

        int gameX = Math.Clamp((int)((location.X - _destination.Left) * 256f / _destination.Width), 0, 255);
        int gameY = Math.Clamp((int)((location.Y - _destination.Top) * 240f / _destination.Height), 0, 239);
        selection = ReadSelection(gameX, gameY);
        return true;
    }

    private TileSelection ReadSelection(int gameX, int gameY)
    {
        gameX = Math.Clamp(gameX, 0, 255);
        gameY = Math.Clamp(gameY, 0, 239);
        bool screenAnchored = _exactSmbProfile && gameY < 32;
        int worldPixelX = screenAnchored ? gameX : _frame.ScrollX + gameX;
        int worldPixelY = screenAnchored ? gameY : _frame.ScrollY + gameY;
        int worldTileX = worldPixelX / 8 / CellStep * CellStep;
        int worldTileY = worldPixelY / 8 / CellStep * CellStep;
        return ReadWorldSelection(worldTileX, worldTileY, screenAnchored);
    }

    private TileSelection ReadWorldSelection(int worldTileX, int worldTileY, bool screenAnchored)
    {
        return new TileSelection(
            worldTileX,
            worldTileY,
            MetatileSignature.Read(_frame, worldTileX, worldTileY, CellSize),
            MetatileVisualFingerprint.Read(
                _frame,
                worldTileX,
                worldTileY, CellSize),
            screenAnchored);
    }

    private void PublishSelectionChanged()
    {
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal int CountVisibleOccurrences(MetatileSignature signature)
    {
        int firstTileX = _frame.ScrollX / 8 / CellStep * CellStep;
        int firstTileY = _frame.ScrollY / 8 / CellStep * CellStep;
        int lastTileX = (_frame.ScrollX + 255) / 8 / CellStep * CellStep;
        int lastTileY = (_frame.ScrollY + 239) / 8 / CellStep * CellStep;
        int count = 0;
        for (int worldTileY = firstTileY; worldTileY <= lastTileY; worldTileY += CellStep)
        for (int worldTileX = firstTileX; worldTileX <= lastTileX; worldTileX += CellStep)
        {
            if (MetatileSignature.Read(_frame, worldTileX, worldTileY, CellSize) == signature) count++;
        }
        return count;
    }

    private RectangleF CellBounds(TileSelection selection, float scale)
    {
        int gameLeft = selection.ScreenAnchored
            ? selection.WorldTileX * 8
            : WrappedDifference(selection.WorldTileX * 8, _frame.ScrollX, 512);
        int gameTop = selection.ScreenAnchored
            ? selection.WorldTileY * 8
            : WrappedDifference(selection.WorldTileY * 8, _frame.ScrollY, 480);
        return new RectangleF(
            _destination.Left + gameLeft * scale,
            _destination.Top + gameTop * scale,
            CellSize * scale,
            CellSize * scale);
    }

    private static long CellKey(TileSelection selection)
    {
        long coordinate = ((long)selection.WorldTileX << 32) | (uint)selection.WorldTileY;
        return selection.ScreenAnchored ? coordinate ^ long.MinValue : coordinate;
    }

    private static int WrappedDifference(int world, int scroll, int modulus)
    {
        int difference = world - scroll;
        while (difference < -16) difference += modulus;
        while (difference >= modulus - 16) difference -= modulus;
        return difference;
    }

    private static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;
}

internal readonly record struct TileSelection(
    int WorldTileX,
    int WorldTileY,
    MetatileSignature Signature,
    string VisualFingerprint,
    bool ScreenAnchored);
