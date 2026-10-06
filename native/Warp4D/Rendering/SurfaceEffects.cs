using Warp4D.Profiles;

namespace Warp4D.Rendering;

internal static class SurfaceEffects
{
    internal static void Light(SurfaceGroup group,float strength,bool packedColor=true)
    {
        group.InvalidateOrder();
        for(int i=0;i<group.Triangles.Count;i++)
        {
            var t=group.Triangles[i];
            float ux=t.B.Point.X-t.A.Point.X,uy=t.B.Point.Y-t.A.Point.Y,uz=(t.B.Depth-t.A.Depth)*100;
            float vx=t.C.Point.X-t.A.Point.X,vy=t.C.Point.Y-t.A.Point.Y,vz=(t.C.Depth-t.A.Depth)*100;
            float nx=uy*vz-uz*vy,ny=uz*vx-ux*vz,nz=ux*vy-uy*vx;
            float norm=MathF.Sqrt(nx*nx+ny*ny+nz*nz);
            float facing=norm<.0001f?1:Math.Abs((nx*-.35f+ny*-.45f+nz*.82f)/norm);
            float brightness=1-strength*(.32f*(1-facing)+.08f);
            Color c=t.Color;
            if(packedColor && brightness>=0 && brightness<=1)
            {
                // Resolve named/system colors once, rather than four channel
                // queries and the four-channel constructor's validation.
                int argb=c.ToArgb();
                int r=(int)(((argb>>16)&255)*brightness);
                int g=(int)(((argb>>8)&255)*brightness);
                int b=(int)((argb&255)*brightness);
                t.Color=Color.FromArgb((argb&unchecked((int)0xff000000))|(r<<16)|(g<<8)|b);
            }
            else t.Color=Color.FromArgb(c.A,(int)(c.R*brightness),(int)(c.G*brightness),(int)(c.B*brightness));
        }
    }
    internal static ImagePixels Halo(ImagePixels source)
    {
        using Bitmap mask=new(source.Width,source.Height);
        for(int y=0;y<source.Height;y++) for(int x=0;x<source.Width;x++)
        {
            int alpha=0;
            for(int dy=-2;dy<=2;dy++) for(int dx=-2;dx<=2;dx++)
            {
                int px=x+dx,py=y+dy;
                if(px<0||py<0||px>=source.Width||py>=source.Height)continue;
                int a=(int)((uint)source.Pixels[py*source.Width+px]>>24);
                alpha=Math.Max(alpha,(int)(a*(1-Math.Sqrt(dx*dx+dy*dy)/3.5)));
            }
            mask.SetPixel(x,y,Color.FromArgb(Math.Clamp(alpha,0,255),255,255,255));
        }
        return ImagePixels.Read(mask);
    }
    internal static SurfaceGroup Accent(SurfaceGroup source,ImagePixels mask,Color color,float opacity,PointF center,float expansion,PointF offset,SurfaceGroup? reusable=null)
    {
        SurfaceGroup result=reusable??new();
        result.BeginUpdate();
        result.Offset=new(source.Offset.X+offset.X,source.Offset.Y+offset.Y);
        SurfaceVertex Scale(SurfaceVertex v)=>v with {Point=new(center.X+(v.Point.X-center.X)*expansion,center.Y+(v.Point.Y-center.Y)*expansion)};
        foreach(var t in source.Triangles.Where(t=>t.LayerKey=="Center").Take(256))
            result.Triangle(Scale(t.A),Scale(t.B),Scale(t.C),mask,color,t.Opacity*opacity,t.LayerKey);
        result.EndUpdate();
        return result;
    }
}
