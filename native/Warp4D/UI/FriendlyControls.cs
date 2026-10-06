namespace Warp4D.UI;

internal sealed class FriendlyTabs : TabControl
{
    public FriendlyTabs()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        SizeMode = TabSizeMode.Fixed;
        BackColor = Color.FromArgb(14, 19, 28);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        for (int i = 0; i < TabCount; i++)
        {
            Rectangle bounds = GetTabRect(i);
            bool selected = i == SelectedIndex;
            using SolidBrush fill = new(selected ? Color.FromArgb(32, 44, 48) : Color.FromArgb(17, 24, 34));
            e.Graphics.FillRectangle(fill, bounds);
            TextRenderer.DrawText(e.Graphics, TabPages[i].Text, Font, bounds, selected ? Color.FromArgb(173, 255, 93) : Color.FromArgb(177, 194, 207), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected) { using Pen accent = new(Color.FromArgb(173, 255, 93), 2); e.Graphics.DrawLine(accent, bounds.Left + 8, bounds.Bottom - 3, bounds.Right - 8, bounds.Bottom - 3); }
        }
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
}

internal static class FriendlyLooks
{
    public static readonly string[] Names=["Gentle depth","Kaleidoscope","Hypercube","Solid sculpture","Separate layers","Round","Double circles","Slice through","Ribbon","Perspective lens","Unfold"];
    public static readonly string[] Descriptions=["A little depth, without overpowering the game.","A dance of independently rotating copies.","Explore a box with a fourth dimension.","Opaque projections with a sculpted feel.","Pull the layers apart to see their structure.","Wrap the artwork onto a rounded shape.","Bend the artwork around two circular dimensions.","Move a slice through the shape to reveal its inside.","Twist the artwork into a dimensional ribbon.","Exaggerate the distance through the fourth dimension.","Watch flat artwork open into a dimensional shape."];
}

internal sealed class FriendlySlider : Panel
{
    public TrackBar Slider {get;}
    private readonly Label _value;
    private readonly Func<int,string> _format;
    public FriendlySlider(string title,string help,int low,int high,int value,Action<int> changed,Func<int,string>? format=null)
    {
        Width=260;Height=77;Margin=new(0,0,0,8);BackColor=Color.FromArgb(14,19,28);
        _format=format??(v=>v+"%");
        Label label=new(){Text=title,AutoSize=true,Location=new(0,1),Font=new("Segoe UI",10,FontStyle.Bold),ForeColor=Color.FromArgb(227,234,241)};
        _value=new(){AutoSize=false,TextAlign=ContentAlignment.MiddleRight,Location=new(180,1),Size=new(80,20),Font=new("Segoe UI",9),ForeColor=Color.FromArgb(173,255,93)};
        Slider=new(){Minimum=low,Maximum=high,Value=Math.Clamp(value,low,high),TickStyle=TickStyle.None,Location=new(-7,23),Size=new(274,32),BackColor=BackColor,AccessibleName=title,AccessibleDescription=help};
        Label hint=new(){Text=help,Location=new(0,58),Size=new(260,18),Font=new("Segoe UI",8),ForeColor=Color.FromArgb(144,160,177),AutoEllipsis=true};
        Slider.ValueChanged+=(_,_)=>{RefreshValue();changed(Slider.Value);};Controls.Add(label);Controls.Add(_value);Controls.Add(Slider);Controls.Add(hint);RefreshValue();
    }
    private void RefreshValue()=>_value.Text=_format(Slider.Value);
}

internal sealed class FriendlyCallout : Panel
{
    public FriendlyCallout(string title,string description)
    {
        Width=260;Height=85;Margin=new(0,0,0,16);BackColor=Color.FromArgb(21,32,42);
        Controls.Add(new Label{Text=title,Location=new(12,10),Size=new(236,23),Font=new("Segoe UI",11,FontStyle.Bold),ForeColor=Color.FromArgb(227,234,241)});
        Controls.Add(new Label{Text=description,Location=new(12,36),Size=new(236,43),Font=new("Segoe UI",9),ForeColor=Color.FromArgb(155,173,190)});
    }
}
