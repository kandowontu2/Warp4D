using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Profiles;

namespace Warp4D;

internal static class SpriteDecodeTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            Random random=new(20261006);byte[] chr=new byte[8192],palette=new byte[32];
            random.NextBytes(chr);random.NextBytes(palette);
            int cases=0;long pixels=0;
            foreach(bool large in new[]{false,true})
            foreach(int pattern in new[]{0,4096})
            for(int tile=0;tile<256;tile++)
            foreach(int flip in new[]{0,64,128,192})
            for(int pal=0;pal<4;pal++)Check(chr,palette,(byte)tile,(byte)(flip|pal),pattern,large);
            for(int color=0;color<256;color++)
            {
                Array.Fill(palette,(byte)color);
                Check(chr,palette,(byte)color,(byte)color,color%2*4096,color%2==0);
            }
            foreach(int size in new[]{0,4095,4096,8192})
            foreach(int palSize in new[]{0,31,32})
            foreach(bool large in new[]{false,true})
            foreach(int pattern in new[]{0,4096})
                Check(chr[..size],palette[..palSize],255,192,pattern,large);
            Array.Clear(chr);Check(chr,palette,0,0,0,false);Check(chr,palette,1,192,4096,true);
            List<double> original=[],bulk=[];
            random.NextBytes(chr);random.NextBytes(palette);
            for(int iteration=0;iteration<23;iteration++)
            {
                if(iteration%2==0){Measure(false,original);Measure(true,bulk);}
                else{Measure(true,bulk);Measure(false,original);}
                void Measure(bool useBulk,List<double> times)
                {
                    long start=Stopwatch.GetTimestamp();
                    for(int i=0;i<4096;i++)using(SmbProfile.DecodeSpriteTile(chr,palette,(byte)i,(byte)(i>>8),i%2*4096,i%3==0,useBulk)){}
                    if(iteration>=3)times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases,ExactArgbPixels=pixels,Scope="Every tile index, four flips/palettes, both pattern banks and8x8/8x16 modes; all palette bytes, blank/truncated inputs. Alternating4096-tile decode batches only, not live FPS.",OriginalMedianMs=original.Order().ElementAt(10),BulkMedianMs=bulk.Order().ElementAt(10)},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
            void Check(byte[] data,byte[] colors,byte tile,byte attributes,int pattern,bool large)
            {
                using Bitmap a=SmbProfile.DecodeSpriteTile(data,colors,tile,attributes,pattern,large,false);
                using Bitmap b=SmbProfile.DecodeSpriteTile(data,colors,tile,attributes,pattern,large,true);
                if(a.Size!=b.Size)throw new InvalidDataException("Sprite dimensions changed.");
                for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)
                    if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())throw new InvalidDataException($"Sprite mismatch tile{tile}/attr{attributes}/base{pattern}/large{large}/{x},{y}");
                cases++;pixels+=a.Width*a.Height;
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
