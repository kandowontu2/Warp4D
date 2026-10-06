using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class SmbWaterFixtureTests
{
    internal static int Run(string input,string output,string game="world")
    {
        Directory.CreateDirectory(output);
        try
        {
            Require(game is "world" or "europe","Explicit supported fixture group required.");
            string[] poses=["water-small-entry","water-small-rise","water-small-release","water-small-left","water-large-entry","water-large-rise"];
            using ArchiveFrameReader frameReader=new();
            using var archive=File.Exists(input)?ZipFile.OpenRead(input):null;
            string? archiveHash=archive is null?null:Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
            Dictionary<string,string> hashes=[];
            if(archive is not null)
            {
                using var indexReader=new StreamReader(archive.GetEntry("__coverage_archive_index.tsv")!.Open());
                while(indexReader.ReadLine() is string row){var fields=row.Split('\t');hashes.Add(fields[0],fields[2]);}
            }
            List<object> cases=[];int previousY=240;
            for(int index=0;index<poses.Length;index++)
            {
                string basename=$"{index:D2}-{poses[index]}.frame.json";
                string key=archive is null?Path.Combine(input,basename):archive.Entries.Single(entry=>entry.FullName.EndsWith("/"+basename,StringComparison.Ordinal)).FullName;
                var frame=frameReader.Read(key,archive is null?null:input,archiveHash,archive is null?null:hashes[key]);
                var ram=frame.Ram;
                Require(ram[0x770]==1&&ram[0x772]==3&&ram[14]==8&&ram[0x74e]==0&&ram[0x704]==1,"Actual active native water/swimming required.");
                Require(ram[0xe7]==0x47&&ram[0xe8]==0xae&&ram[0xe9]==0x71&&ram[0xea]==0xa1,"Native supported-cartridge water-data pointers required; AreaPointer can hold future exit destination.");
                Require(ram[0x754]==(index<4?1:0)&&ram[0x1d]==1&&ram[0xce]<previousY,"Actual rising native swimming poses required.");previousY=ram[0xce];
                var body=SmbNaturalGrowthTests.Save(game,poses[index],frame,output);
                using var scene=new SmbProfile().Build(frame,true);
                using var owned=SmbPlayerOwnershipTests.OwnedImage(frame,out var bodyBounds);
                Require(scene.PlayerOverlaysFlatHud==(bodyBounds.Top<32),"Only actual HUD-crossing player needs overlay ordering.");
                // Exact SMB uses raw nametable HUD plus separate flat OAM
                // objects. No native player pixels are copied into this layer.
                // Require every background HUD pixel unchanged; independently
                // compare non-sprite native HUD pixels below as well.
                for(int y=0;y<32;y++)for(int x=0;x<256;x++)
                {
                    int expected=frame.NametablePixels[0][y*256+x];
                    Require((scene.Background.GetPixel(x,y).ToArgb()&0xffffff)==(expected&0xffffff),$"{poses[index]}: HUD background({x},{y}) changed.");
                }
                using var background=(Bitmap)scene.Background.Clone();
                foreach(var obj in scene.Objects.Where(o=>o.IdentityKey.StartsWith("background:")))
                for(int y=0;y<obj.Image.Height;y++)for(int x=0;x<obj.Image.Width;x++)
                {
                    int sx=x+obj.Bounds.X,sy=y+obj.Bounds.Y;var color=obj.Image.GetPixel(x,y);
                    if(sx>=0&&sx<256&&sy>=0&&sy<240&&color.A!=0)background.SetPixel(sx,sy,color);
                }
                var sprites=scene.Objects.Where(o=>!o.IdentityKey.StartsWith("background:")).Select(o=>o.Bounds).ToArray();
                int eligible=0,matched=0,hud=0,hudMatched=0;
                for(int y=0;y<240;y++)for(int x=8;x<256;x++)
                {
                    if(sprites.Any(bounds=>bounds.Contains(x,y)))continue;
                    bool equal=(background.GetPixel(x,y).ToArgb()&0xffffff)==(frame.NativeScreenPixels![y*256+x]&0xffffff);
                    if(y<32){hud++;if(equal)hudMatched++;}else{eligible++;if(equal)matched++;}
                }
                Require(eligible>40000&&eligible==matched&&hud==hudMatched,"Exact reconstructed native water scenery/HUD required.");
                cases.Add(new{Pose=poses[index],Body=body,ExactRawHudPixels=8192,NativeHudSpritePixelsExcluded=true,EligibleBackgroundPixels=eligible,MatchedBackgroundPixels=matched,HudPixels=hud,MatchedHudPixels=hudMatched,SceneryObjects=scene.Objects.Count(o=>o.IdentityKey.StartsWith("background:"))});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Game=game,Cases=cases,AssistedAreaAndLargeForm=true,NaturalWaterRoute=false,Scope="Six isolated swimming poses/body/native scenery for the explicit fixture group, not entire water course/all forms/physical display."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
