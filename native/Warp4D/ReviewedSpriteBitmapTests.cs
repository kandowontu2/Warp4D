using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

// Explicit reviewed fixture slots, not inferred from the projected object.
internal static class ReviewedSpriteBitmapTests
{
    internal static int Verify(NesFrame frame,SceneObject[] actors,JsonElement indices,string area)
    {
        int[] owned=indices.EnumerateArray().Select(v=>v.GetInt32()).ToArray();Rectangle? bounds=null;
        if(owned.Length==0||owned.Distinct().Count()!=owned.Length)throw new InvalidDataException("Nonempty unique reviewed body slots required.");
        foreach(int index in owned)
        {
            if(index<0||index>=64||index*4+3>=frame.Oam.Length)throw new InvalidDataException("Invalid reviewed OAM slot.");
            int offset=index*4;if(frame.Oam[offset]>=239)continue;
            var tile=new Rectangle(frame.Oam[offset+3],frame.Oam[offset]+1,8,frame.LargeSprites?16:8);
            bounds=bounds is Rectangle prior?Rectangle.Union(prior,tile):tile;
        }
        if(bounds is not Rectangle body||actors.Length!=1||actors[0].Bounds!=body)
            throw new InvalidOperationException(area+": exact reviewed body bounds required; expected="+bounds+", actual="+string.Join(";",actors.Select(actor=>actor.Bounds.ToString())));
        using Bitmap expected=new(body.Width,body.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(expected))
        {
            graphics.Clear(Color.Transparent);
            foreach(int index in owned.OrderDescending())
            {
                int offset=index*4;if(frame.Oam[offset]>=239)continue;
                using var tile=SmbProfile.DecodeSpriteTile(frame.Chr,frame.Palette,frame.Oam[offset+1],frame.Oam[offset+2],frame.SpritePatternBase,frame.LargeSprites);
                graphics.DrawImageUnscaled(tile,frame.Oam[offset+3]-body.X,frame.Oam[offset]+1-body.Y);
            }
        }
        for(int y=0;y<body.Height;y++)for(int x=0;x<body.Width;x++)
            if(expected.GetPixel(x,y).ToArgb()!=actors[0].Image.GetPixel(x,y).ToArgb())
                throw new InvalidOperationException(area+": projected actor contains missing/foreign native sprite pixels.");
        return body.Width*body.Height;
    }
}
