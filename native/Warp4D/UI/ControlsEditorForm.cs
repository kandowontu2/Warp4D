using Warp4D.Emulation;

namespace Warp4D.UI;

internal sealed class ControlsEditorForm : Form
{
    private readonly InputSettings _working;
    private readonly Dictionary<NesButton, Button> _buttons = [];
    private NesButton? _listening;
    public InputSettings EditedSettings { get; private set; }
    public ControlsEditorForm(InputSettings settings)
    {
        _working = settings.Clone(); EditedSettings = _working;
        Text = "Warp4D — Keyboard and controller"; ClientSize = new Size(450, 540);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; KeyPreview = true;
        StartPosition = FormStartPosition.CenterParent;
        FlowLayoutPanel stack = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        stack.Controls.Add(new Label { Text = "Click a binding, then press its new key. Esc cancels capture.", AutoSize = true, MaximumSize = new Size(405, 50) });
        foreach (NesButton button in Enum.GetValues<NesButton>())
        {
            Panel row = new() { Width = 400, Height = 35 };
            row.Controls.Add(new Label { Text = button.ToString(), Location = new Point(8, 7), Width = 150 });
            Button binding = new() { Text = _working.Keyboard[button.ToString()].ToString(), Location = new Point(180, 0), Width = 210, Height = 32 };
            binding.Click += (_, _) => { _listening = button; RefreshBindings(); binding.Text = "Press a key…"; };
            binding.PreviewKeyDown += (_, e) => { if (_listening is not null) e.IsInputKey = true; };
            _buttons[button] = binding; row.Controls.Add(binding); stack.Controls.Add(row);
        }
        CheckBox controller = new() { Text = "Enable Xbox / XInput controller", Checked = _working.ControllerEnabled, AutoSize = true };
        controller.CheckedChanged += (_, _) => _working.ControllerEnabled = controller.Checked;
        stack.Controls.Add(controller);
        stack.Controls.Add(new Label { Text = "D-pad / left stick: move · A: NES A · B or X: NES B\nStart: Start · Back: Select\nF5: save slot · F8: load slot · F11: fullscreen\nF3 / F4: choose state slot · F2: reset",
            AutoSize = true, MaximumSize = new Size(400, 100) });
        FlowLayoutPanel actions = new() { AutoSize = true };
        Button reset = new() { Text = "Defaults", Width = 100 };
        reset.Click += (_, _) => { _working.Keyboard = InputSettings.Defaults(); _listening = null; RefreshBindings(); };
        Button save = new() { Text = "Save", Width = 100 };
        save.Click += (_, _) =>
        {
            if (_working.Keyboard.Values.Distinct().Count() != _working.Keyboard.Count)
            { MessageBox.Show(this, "Give each NES button its own key.", "Duplicate binding"); return; }
            EditedSettings = _working.Clone(); DialogResult = DialogResult.OK; Close();
        };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 100 };
        actions.Controls.AddRange([reset, save, cancel]); stack.Controls.Add(actions); Controls.Add(stack);
        CancelButton = cancel;
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_listening is NesButton button)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { _listening = null; RefreshBindings(); return true; }
            if (key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or Keys.Menu or Keys.LMenu or Keys.RMenu ||
                (key >= Keys.F1 && key <= Keys.F12)) return true;
            if (key == Keys.ShiftKey) key = ((msg.LParam.ToInt64() >> 16) & 255) == 0x36 ? Keys.RShiftKey : Keys.LShiftKey;
            _working.Keyboard[button.ToString()] = key; _listening = null; RefreshBindings(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private void RefreshBindings() { foreach ((NesButton button, Button binding) in _buttons) binding.Text = _working.Keyboard[button.ToString()].ToString(); }
}
