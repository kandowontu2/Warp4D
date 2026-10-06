using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class HudPlayerLayerTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            int checks=0;void Check(bool ok){if(!ok)throw new InvalidOperationException("HUD/body layer check"+checks);checks++;}
            int raw=unchecked((int)0xff123456),glyph=Color.White.ToArgb();
            byte[] chr=new byte[8192],palette=new byte[32],oam=new byte[256],ram=new byte[2048];
            for(int i=0;i<8;i++)chr[i]=255;
            palette[17]=0x16;for(int i=0;i<64;i++)oam[i*4]=250;
            oam[0]=27;oam[3]=32;ram[20]=36;ram[21]=32;
            using var tile=SmbProfile.DecodeSpriteTile(chr,palette,0,0,0,false);
            int body=tile.GetPixel(0,0).ToArgb();Check(body!=raw && body!=glyph);
            int[] native=Enumerable.Repeat(raw,61440).ToArray();
            for(int y=28;y<36;y++)for(int x=32;x<40;x++)native[y*256+x]=body;
            // A HUD glyph outside the body, and unlike-colored native content
            // inside its bounding rectangle, must not be erased.
            native[33*256+22]=glyph;native[29*256+33]=glyph;
            var frame=new NesFrame{Ram=ram,Chr=chr,Palette=palette,Oam=oam,
                Tiles=Enumerable.Range(0,4).Select(_=>new byte[960]).ToArray(),
                Attributes=Enumerable.Range(0,4).Select(_=>new byte[64]).ToArray(),ScrollSource="Synthetic independent HUD fixture",
                NametablePixels=Enumerable.Range(0,4).Select(_=>Enumerable.Repeat(raw,61440).ToArray()).ToArray(),
                NativeScreenPixels=native,Sequence=1,NativeScreenSequence=1,CaptureScanline=96,PpuMask=24};
            var profile=new GameRecognitionProfile{PlayerLabel="Tracked test body",FlatRegions=[new(16,24,56,24)],
                PlayerTracking=new(){XAddress=20,YAddress=21,SpritePalette=0},PlayerOverlaysFlatHud=true};
            profile.Normalize();Check(profile.FormatVersion==15);
            using var scene=new SmbProfile().Build(frame,false,null,profile);
            var actors=scene.Objects.Where(o=>o.Kind==SceneObjectKind.Player&&o.ProjectionEnabled).ToArray();
            Check(actors.Length==1 && actors[0].Bounds==new Rectangle(32,28,8,8));
            for(int y=0;y<8;y++)for(int x=0;x<8;x++)Check(actors[0].Image.GetPixel(x,y).ToArgb()==body);
            int restored=0;
            for(int y=24;y<48;y++)for(int x=16;x<72;x++)
            {
                bool owned=x>=32&&x<40&&y>=28&&y<36&&native[y*256+x]==body;
                Check(scene.Background.GetPixel(x,y).ToArgb()==(owned?raw:native[y*256+x]));
                if(owned)restored++;
            }
            Check(restored==63);Check(scene.PlayerOverlaysFlatHud);
            using var cloned=scene.Clone();Check(cloned.PlayerOverlaysFlatHud);
            Check(profile.Clone().PlayerOverlaysFlatHud);
            var imported=JsonSerializer.Deserialize<GameRecognitionProfile>(JsonSerializer.Serialize(profile))!;
            imported.Normalize();Check(imported.PlayerOverlaysFlatHud&&imported.FormatVersion==15);
            profile.PlayerOverlaysFlatHud=false;
            using var legacy=new SmbProfile().Build(frame,false,null,profile);
            Check(!legacy.PlayerOverlaysFlatHud&&legacy.Objects.All(o=>o.Kind!=SceneObjectKind.Player));
            Check(legacy.Background.GetPixel(32,28).ToArgb()==body);
            Check(!JsonSerializer.Serialize(new GameRecognitionProfile()).Contains("PlayerOverlaysFlatHud"));
            profile.PlayerOverlaysFlatHud=true;
            profile.ConditionalFlatRegions=[new(){Bounds=new(0,0,256,240),ExpectedRam=new(){[29]=0}}];
            using var inactive=new SmbProfile().Build(frame,false,null,profile);
            Check(inactive.Objects.Count==0 && !inactive.PlayerOverlaysFlatHud);
            for(int y=0;y<240;y++)for(int x=0;x<256;x++)Check(inactive.Background.GetPixel(x,y).ToArgb()==native[y*256+x]);
            try{new GameRecognitionProfile{PlayerOverlaysFlatHud=true}.Normalize();throw new Exception("Missing tracker accepted");}catch(InvalidDataException){checks++;}
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,NativeBodyPixelsRemoved=63,WholeRectangleNotRemoved=true,UnrelatedHudPixelsExact=true,LegacyDefaultUnchanged=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
