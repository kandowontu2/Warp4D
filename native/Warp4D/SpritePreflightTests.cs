using System.Diagnostics;
using System.Text.Json;
using Warp4D.Profiles;

namespace Warp4D;

internal static class SpritePreflightTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            Random random=new(20261006);byte[] chr=new byte[8192],palette=new byte[32];
            random.NextBytes(chr);random.NextBytes(palette);int cases=0,blank=0,visible=0;
            foreach(bool large in new[]{false,true})foreach(int bank in new[]{0,4096})
            for(int tile=0;tile<256;tile++)foreach(int flip in new[]{0,64,128,192})for(int p=0;p<4;p++)Check(chr,palette,(byte)tile,(byte)(flip|p),bank,large);
            Array.Clear(chr);
            foreach(bool large in new[]{false,true})foreach(int bank in new[]{0,4096})for(int tile=0;tile<256;tile++)Check(chr,palette,(byte)tile,192,bank,large);
            // Single bits exercise every row, bitplane and8x16 bank/pair rule.
            foreach(bool large in new[]{false,true})foreach(int tile in new[]{0,1,254,255})foreach(int bank in new[]{0,4096})
            for(int index=0;index<(large?32:16);index++)for(int bit=0;bit<8;bit++)
            {
                Array.Clear(chr);int offset=large?(tile&1)*4096+(tile&0xfe)*16:bank+tile*16;chr[offset+index]=(byte)(1<<bit);
                Array.Fill(palette,(byte)13); // Opaque black is not transparent.
                Check(chr,palette,(byte)tile,192,bank,large);
            }
            foreach(int size in new[]{0,4095,4096,8191,8192})foreach(int ps in new[]{0,31,32})foreach(bool large in new[]{false,true})foreach(int bank in new[]{0,4096})Check(chr[..size],palette[..ps],255,0,bank,large);
            random.NextBytes(chr);random.NextBytes(palette);
            for(int tile=0;tile<512;tile+=3)Array.Clear(chr,tile*16,16);
            List<double> old=[],preflight=[];int checksOld=0,checksNew=0;
            for(int round=0;round<23;round++)
            {
                if(round%2==0){Measure(false,old);Measure(true,preflight);}else{Measure(true,preflight);Measure(false,old);}
                void Measure(bool optimized,List<double> times)
                {
                    int count=0;long start=Stopwatch.GetTimestamp();
                    for(int i=0;i<4096;i++)
                    {
                        byte tile=(byte)i;int bank=(i>>8)%2*4096;bool large=i%3==0;
                        if(optimized&&!SmbProfile.SpriteTileHasArtwork(chr,palette,tile,bank,large))continue;
                        using Bitmap image=SmbProfile.DecodeSpriteTile(chr,palette,tile,(byte)(i>>8),bank,large);
                        if(optimized||SmbProfile.HasVisiblePixel(image))count++;
                    }
                    if(optimized)checksNew=count;else checksOld=count;
                    if(round>=3)times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
                if(checksOld!=checksNew)throw new InvalidDataException("Benchmark visibility differs.");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases,BlankCases=blank,VisibleCases=visible,OriginalMedianMs=old.Order().ElementAt(10),PreflightMedianMs=preflight.Order().ElementAt(10),VisiblePer4096Batch=checksOld,Scope="Exact preflight visibility vs decoded bitmap alpha, all tile/flips/palettes/banks/modes, blank/single-bit/truncated/opaque-black cases. Alternating4096tile mixed-artwork batches only, not gameplay FPS."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
            void Check(byte[] data,byte[] colors,byte tile,byte attributes,int bank,bool large)
            {
                using Bitmap image=SmbProfile.DecodeSpriteTile(data,colors,tile,attributes,bank,large);
                bool expected=SmbProfile.HasVisiblePixel(image),actual=SmbProfile.SpriteTileHasArtwork(data,colors,tile,bank,large);
                if(expected!=actual)throw new InvalidDataException($"Visibility differs tile{tile}/attr{attributes}/bank{bank}/large{large}/chr{data.Length}/palette{colors.Length}");
                cases++;if(expected)visible++;else blank++;
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
