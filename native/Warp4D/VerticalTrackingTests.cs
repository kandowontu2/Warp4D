using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class VerticalTrackingTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            int checks=0;void Check(bool ok){if(!ok)throw new InvalidOperationException("Vertical tracking check"+checks);checks++;}
            byte[] ram=new byte[2048];ram[782]=3;ram[253]=250;
            var frame=new NesFrame{Ram=ram,Chr=[],Palette=[],Oam=[],Tiles=[],Attributes=[],NametablePixels=[],ScrollSource="Independent native vertical anchor matrix"};
            var old=new PlayerSpriteTracking{XAddress=782,YAddress=781,XScrollAddress=253,YScrollAddress=252,SpritePalette=0};
            var current=new PlayerSpriteTracking{XAddress=782,YAddress=781,XScrollAddress=253,YScrollAddress=252,SpritePalette=0,
                VerticalWrap=new(){ScrollDirectionAddress=73,ObjectNametableAddress=780,PpuNametableAddress=255}};
            for(int direction=0;direction<4;direction++)for(int objectPage=0;objectPage<2;objectPage++)for(int ppuPage=0;ppuPage<2;ppuPage++)
            for(int rawY=0;rawY<240;rawY++)for(int scrollY=0;scrollY<240;scrollY++)
            {
                ram[73]=(byte)direction;ram[780]=(byte)objectPage;ram[255]=(byte)(0x90|ppuPage);ram[781]=(byte)rawY;ram[252]=(byte)scrollY;
                // Independent6502 arithmetic: SEC/SBC scroll byte, then the
                // different-page vertical branch SBC0F with carryclear.
                int expected=(rawY-scrollY)&255;
                if(direction<2 && objectPage!=ppuPage && rawY<scrollY)expected=(expected-15-1)&255;
                Check(current.TryGetScreenPosition(frame,out var actual)&&actual==new Point(9,expected));
                Check(old.TryGetScreenPosition(frame,out var legacy)&&legacy==new Point(9,(rawY-scrollY)&255));
            }
            var profile=new GameRecognitionProfile{PlayerTracking=current};profile.Normalize();Check(profile.FormatVersion==16);
            var clone=profile.Clone();Check(clone.PlayerTracking?.VerticalWrap is not null&&!ReferenceEquals(current.VerticalWrap,clone.PlayerTracking.VerticalWrap));
            clone.PlayerTracking!.VerticalWrap!.PpuNametableAddress=254;Check(current.VerticalWrap!.PpuNametableAddress==255);
            var imported=JsonSerializer.Deserialize<GameRecognitionProfile>(JsonSerializer.Serialize(profile))!;imported.Normalize();
            Check(JsonSerializer.Serialize(imported.PlayerTracking)==JsonSerializer.Serialize(current));
            Check(!JsonSerializer.Serialize(old).Contains("VerticalWrap"));
            Check(!current.TryGetScreenPosition(frame with{Ram=[]},out _));
            var invalid=profile.Clone();invalid.PlayerTracking!.YScrollAddress=null;bool rejected=false;
            try{invalid.Normalize();}catch(InvalidDataException){rejected=true;}Check(rejected);
            invalid=profile.Clone();invalid.PlayerTracking!.VerticalWrap!.PpuNametableAddress=2048;rejected=false;
            try{invalid.Normalize();}catch(InvalidDataException){rejected=true;}Check(rejected);
            invalid=profile.Clone();invalid.PlayerTracking!.VerticalWrap!.NametableMask=0;rejected=false;
            try{invalid.Normalize();}catch(InvalidDataException){rejected=true;}Check(rejected);
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,Native240LineContexts=921600,
                LegacyByteWrapContexts=921600,HorizontalAndSamePageUnchanged=true,CloneImportMetadataExact=true},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
