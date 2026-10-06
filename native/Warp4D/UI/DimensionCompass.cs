using Warp4D.Rendering;

namespace Warp4D.UI;

internal sealed class DimensionCompass : Control
{
    private readonly WarpRendererControl _renderer;
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=100};
    private Point _last;
    private string? _plane;
    private bool _drag;
    private static readonly string[] Planes=["XY","XZ","XW","YZ","YW","ZW"];
    public DimensionCompass(WarpRendererControl renderer)
    {
        _renderer=renderer;Size=new(246,204);BackColor=Color.FromArgb(10,17,26);ForeColor=Color.FromArgb(173,255,93);
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint,true);
        _timer.Tick+=(_,_)=>{if(Visible)Invalidate();};_timer.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using Font font=new("Segoe UI",8);using Brush ink=new SolidBrush(ForeColor);
        Rotation4D r=_renderer.RotationForCompass;
        PointF center=new(Width/2f,49);
        Color[] colors=[Color.Coral,Color.LightSkyBlue,Color.MediumPurple,Color.GreenYellow];
        for(int axis=0;axis<4;axis++)
        {
            float[] p=new float[4];p[axis]=27;
            var point=FourDMath.Project(new(p[0],p[1],p[2],p[3]),r,100,110).Point;
            using Pen line=new(colors[axis],2);using Brush label=new SolidBrush(colors[axis]);
            PointF end=new(center.X+point.X,center.Y+point.Y);g.DrawLine(line,center,end);g.DrawString("XYZW"[axis].ToString(),font,label,end.X+3,end.Y-7);
        }
        g.DrawString("R⁴  /  DRAG A PLANE",font,ink,8,5);
        for(int i=0;i<6;i++)
        {
            Rectangle rect=new(6+(i%2)*(Width-12)/2,86+(i/2)*28,(Width-16)/2,25);
            using Brush fill=new SolidBrush(_plane==Planes[i]?Color.FromArgb(45,64,63):Color.FromArgb(20,30,42));g.FillRectangle(fill,rect);
            float value=_renderer.PlaneDegrees(Planes[i]);g.DrawString($"{Planes[i]}  {value:0}°",font,ink,rect.X+8,rect.Y+5);
        }
        string locked=(ModifierKeys&(Keys.Control|Keys.Shift)) switch{Keys.Control=>"W LOCKED · XZ / YZ",Keys.Shift=>"X LOCKED · YW / ZW",Keys.Control|Keys.Shift=>"Y LOCKED · XW / ZW",_=>"CTRL: W   SHIFT: X   BOTH: Y"};
        g.DrawString(locked,font,ink,8,177);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
        _plane=null;
        if(e.Y>=86&&e.Y<170){int row=(e.Y-86)/28,col=e.X<Width/2?0:1;_plane=Planes[Math.Clamp(row*2+col,0,5)];}
        _last=e.Location;_drag=true;Capture=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);if(!_drag)return;
        if((ModifierKeys&(Keys.Control|Keys.Shift))!=0||_plane is null)_renderer.DragCompass((e.X-_last.X)*.009f,(e.Y-_last.Y)*.009f,ModifierKeys);
        else _renderer.AdjustPlane(_plane,(e.X-_last.X)-(e.Y-_last.Y));
        _last=e.Location;Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);_drag=false;Capture=false;}
    protected override void Dispose(bool disposing){if(disposing)_timer.Dispose();base.Dispose(disposing);}
}
