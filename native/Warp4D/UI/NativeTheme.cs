using System.Runtime.InteropServices;

namespace Warp4D.UI;

internal static class NativeTheme
{
    internal static void Apply(Form form)
    {
        form.HandleCreated += (_, _) =>
        {
            int dark = 1;
            // Unsupported Windows versions simply retain their system chrome.
            if (DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int));
        };
    }
    internal static void StyleTabs(TabControl tabs)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed; tabs.ItemSize = new(110, 30);
        tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabs.SelectedIndex;
            using SolidBrush brush = new(selected ? Color.FromArgb(32, 44, 48) : Color.FromArgb(17, 24, 34));
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds,
                selected ? Color.FromArgb(173, 255, 93) : Color.FromArgb(177, 194, 207), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
    }
    internal static void StyleComboBox(ComboBox box)
    {
        box.DrawMode = DrawMode.OwnerDrawFixed;
        box.BackColor = Color.FromArgb(17, 24, 34); box.ForeColor = Color.FromArgb(227, 234, 241);
        box.ItemHeight = box.Font.Height + 6;
        box.DrawItem += (_, e) =>
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using SolidBrush fill = new(selected ? Color.FromArgb(38, 54, 45) : box.BackColor);
            e.Graphics.FillRectangle(fill, e.Bounds);
            string text = e.Index >= 0 && e.Index < box.Items.Count ? box.Items[e.Index]?.ToString() ?? "" : box.Text;
            TextRenderer.DrawText(e.Graphics, text, box.Font, Rectangle.Inflate(e.Bounds, -4, 0), box.ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        };
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
