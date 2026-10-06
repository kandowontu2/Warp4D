using Warp4D.Profiles;

namespace Warp4D.UI;

internal sealed class ProfileEditorForm : Form
{
    private static readonly Color WindowColor = Color.FromArgb(8, 11, 17);
    private static readonly Color PanelColor = Color.FromArgb(14, 19, 28);
    private static readonly Color BorderColor = Color.FromArgb(37, 49, 63);
    private static readonly Color TextColor = Color.FromArgb(227, 234, 241);
    private static readonly Color MutedColor = Color.FromArgb(126, 143, 160);
    private static readonly Color Lime = Color.FromArgb(173, 255, 93);

    private readonly TextBox _nameBox = new();
    private readonly Dictionary<SceneObjectKind, RuleControls> _ruleControls = [];
    private ProjectionProfile _workingProfile;

    public ProjectionProfile EditedProfile { get; private set; }

    public ProfileEditorForm(ProjectionProfile activeProfile)
    {
        _workingProfile = activeProfile.Clone();
        EditedProfile = activeProfile.Clone();

        Text = "Warp4D · Which objects become 4D?";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(780, 760);
        MinimumSize = new Size(700, 620);
        BackColor = WindowColor;
        ForeColor = TextColor;
        ShowIcon = false;
        MaximizeBox = false;

        Controls.Add(BuildLayout());
        ApplyProfileToControls(_workingProfile);
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = WindowColor,
            Padding = new Padding(24, 20, 24, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 73));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        Panel heading = new() { Dock = DockStyle.Fill };
        Label title = MakeLabel("Which objects become 4D?", 16, TextColor, FontStyle.Bold);
        title.Location = new Point(0, 0);
        Label explanation = MakeLabel(
            "Turn object types on or off, then adjust how much depth they have.\nObjects you turn off keep their original 2D artwork.",
            9,
            MutedColor);
        explanation.Location = new Point(1, 32);
        heading.Controls.Add(title);
        heading.Controls.Add(explanation);
        root.Controls.Add(heading, 0, 0);

        Panel namePanel = new() { Dock = DockStyle.Fill };
        Label nameLabel = MakeLabel("PROFILE NAME", 8, Lime, FontStyle.Bold);
        nameLabel.Location = new Point(0, 0);
        _nameBox.Location = new Point(0, 23);
        _nameBox.Width = 360;
        _nameBox.MaxLength = 48;
        _nameBox.BorderStyle = BorderStyle.FixedSingle;
        _nameBox.BackColor = Color.FromArgb(19, 26, 37);
        _nameBox.ForeColor = TextColor;
        _nameBox.Font = new Font("Segoe UI", 10f);
        namePanel.Controls.Add(nameLabel);
        namePanel.Controls.Add(_nameBox);
        root.Controls.Add(namePanel, 0, 1);

        FlowLayoutPanel presets = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        presets.Controls.Add(MakeSmallButton("ALL 4D", (_, _) => ApplyPreset(Preset.All)));
        presets.Controls.Add(MakeSmallButton("CHARACTERS", (_, _) => ApplyPreset(Preset.Characters)));
        presets.Controls.Add(MakeSmallButton("SCENERY", (_, _) => ApplyPreset(Preset.Scenery)));
        presets.Controls.Add(MakeSmallButton("RESET DEPTHS", (_, _) => ResetDepths()));
        root.Controls.Add(presets, 0, 2);

        Panel scrollHost = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = PanelColor,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 8)
        };
        scrollHost.Paint += (_, e) =>
        {
            using Pen pen = new(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, scrollHost.ClientSize.Width - 1, scrollHost.ClientSize.Height - 1);
        };
        scrollHost.Controls.Add(BuildRuleTable());
        root.Controls.Add(scrollHost, 0, 3);

        root.Controls.Add(BuildFooter(), 0, 4);
        return root;
    }

    private Control BuildRuleTable()
    {
        SceneObjectKind[] kinds = Enum.GetValues<SceneObjectKind>();
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = kinds.Length + 1,
            BackColor = PanelColor,
            Padding = new Padding(14, 8, 14, 12)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 202));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        table.Controls.Add(MakeHeader("4D"), 0, 0);
        table.Controls.Add(MakeHeader("OBJECT CLASS"), 1, 0);
        table.Controls.Add(MakeHeader("RELATIVE DEPTH"), 2, 0);
        table.Controls.Add(MakeHeader("%"), 3, 0);

        int row = 1;
        foreach (SceneObjectKind kind in kinds)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            CheckBox enabled = new()
            {
                Text = "ON",
                AutoSize = true,
                ForeColor = Lime,
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                Anchor = AnchorStyles.Left,
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 0, 0, 0)
            };
            Label objectName = MakeLabel(ProjectionProfile.DisplayName(kind), 9, TextColor, FontStyle.Bold);
            objectName.Anchor = AnchorStyles.Left;
            objectName.AutoEllipsis = true;
            objectName.Dock = DockStyle.Fill;
            objectName.TextAlign = ContentAlignment.MiddleLeft;
            TrackBar depth = new()
            {
                Minimum = ProjectionProfile.MinimumDepthPercent,
                Maximum = ProjectionProfile.MaximumDepthPercent,
                TickStyle = TickStyle.None,
                Dock = DockStyle.Fill,
                Height = 34,
                BackColor = PanelColor,
                Margin = new Padding(0, 5, 4, 0)
            };
            Label value = MakeLabel("100%", 9, TextColor, FontStyle.Bold);
            value.Dock = DockStyle.Fill;
            value.TextAlign = ContentAlignment.MiddleRight;
            depth.ValueChanged += (_, _) => value.Text = $"{depth.Value}%";
            void UpdateEnabledAppearance()
            {
                depth.Enabled = enabled.Checked;
                value.ForeColor = enabled.Checked ? TextColor : MutedColor;
                objectName.ForeColor = enabled.Checked ? TextColor : MutedColor;
                enabled.Text = enabled.Checked ? "ON" : "2D";
                enabled.ForeColor = enabled.Checked ? Lime : MutedColor;
            }
            enabled.CheckedChanged += (_, _) => UpdateEnabledAppearance();

            table.Controls.Add(enabled, 0, row);
            table.Controls.Add(objectName, 1, row);
            table.Controls.Add(depth, 2, row);
            table.Controls.Add(value, 3, row);
            _ruleControls[kind] = new RuleControls(enabled, depth, UpdateEnabledAppearance);
            row++;
        }
        return table;
    }

    private Control BuildFooter()
    {
        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        FlowLayoutPanel fileActions = new()
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        fileActions.Controls.Add(MakeSmallButton("IMPORT…", (_, _) => ImportProfile()));
        fileActions.Controls.Add(MakeSmallButton("EXPORT…", (_, _) => ExportProfile()));
        footer.Controls.Add(fileActions, 0, 0);

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
        save.Width = 126;
        save.Click += (_, _) => SaveAndApply();
        confirmation.Controls.Add(cancel);
        confirmation.Controls.Add(save);
        footer.Controls.Add(confirmation, 1, 0);
        CancelButton = cancel;
        AcceptButton = save;
        return footer;
    }

    private void ApplyProfileToControls(ProjectionProfile profile)
    {
        _nameBox.Text = profile.Name;
        foreach ((SceneObjectKind kind, RuleControls controls) in _ruleControls)
        {
            ObjectProjectionRule rule = profile.RuleFor(kind);
            controls.Depth.Value = Math.Clamp(
                rule.DepthPercent,
                controls.Depth.Minimum,
                controls.Depth.Maximum);
            controls.Enabled.Checked = rule.Enabled;
            controls.UpdateEnabledAppearance();
        }
    }

    private ProjectionProfile ReadProfileFromControls()
    {
        ProjectionProfile profile = ProjectionProfile.CreateDefault(_nameBox.Text);
        foreach ((SceneObjectKind kind, RuleControls controls) in _ruleControls)
        {
            ObjectProjectionRule rule = profile.Objects[kind.ToString()];
            rule.Enabled = controls.Enabled.Checked;
            rule.DepthPercent = controls.Depth.Value;
        }
        profile.Normalize();
        return profile;
    }

    private void ApplyPreset(Preset preset)
    {
        HashSet<SceneObjectKind> characters =
        [
            SceneObjectKind.Player,
            SceneObjectKind.Enemy,
            SceneObjectKind.Item,
            SceneObjectKind.Sprite
        ];
        foreach ((SceneObjectKind kind, RuleControls controls) in _ruleControls)
        {
            controls.Enabled.Checked = preset switch
            {
                Preset.All => true,
                Preset.Characters => characters.Contains(kind),
                Preset.Scenery => !characters.Contains(kind),
                _ => controls.Enabled.Checked
            };
        }
    }

    private void ResetDepths()
    {
        foreach (RuleControls controls in _ruleControls.Values)
        {
            controls.Depth.Value = 100;
        }
    }

    private void ImportProfile()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Import Warp4D projection profile",
            Filter = "Warp4D profiles (*.warp4d.json)|*.warp4d.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _workingProfile = ProjectionProfileStore.ReadFromFile(dialog.FileName);
            ApplyProfileToControls(_workingProfile);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not import profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportProfile()
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Export Warp4D projection profile",
            Filter = "Warp4D profiles (*.warp4d.json)|*.warp4d.json|JSON files (*.json)|*.json",
            FileName = SafeFileName(_nameBox.Text) + ".warp4d.json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ProjectionProfileStore.WriteToFile(dialog.FileName, ReadProfileFromControls());
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not export profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveAndApply()
    {
        try
        {
            ProjectionProfile profile = ReadProfileFromControls();
            ProjectionProfileStore.Save(profile);
            EditedProfile = profile.Clone();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not save profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string SafeFileName(string name)
    {
        string cleaned = new((string.IsNullOrWhiteSpace(name) ? "projection-profile" : name)
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)
            .ToArray());
        cleaned = cleaned.Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "projection-profile" : cleaned;
    }

    private static Label MakeHeader(string text)
    {
        Label label = MakeLabel(text, 8, MutedColor, FontStyle.Bold);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        return label;
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

    private static Button MakeSmallButton(string text, EventHandler clicked)
    {
        Button button = MakeButton(text, primary: false);
        button.Width = Math.Max(92, text.Length * 9 + 26);
        button.Height = 31;
        button.Click += clicked;
        return button;
    }

    private static Button MakeButton(string text, bool primary)
    {
        Button button = new()
        {
            Text = text,
            Size = new Size(92, 36),
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

    private sealed record RuleControls(CheckBox Enabled, TrackBar Depth, Action UpdateEnabledAppearance);

    private enum Preset
    {
        All,
        Characters,
        Scenery
    }
}
