using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

// Explicit offline regression: independent training pixels, never cartridge
// loading or adding a failing hold-out's fingerprint to a profile.
internal static class MaskedEdgeArtworkTests
{
    internal static int Run(string profileFile,string trainingFile,string holdFile,string output)
    {
        Directory.CreateDirectory(output);
        List<string> checks=[];
        try
        {
            var profile=GameRecognitionProfileStore.ReadFromFile(profileFile);
            var hold=Read(holdFile);
            Require(hold.PpuMask is null,"Original legacy hold-out remains unchanged");
            profile.AllowMaskedLeftEdgeArtwork=true;
            int learned=0;
            var report=JsonDocument.Parse(File.ReadAllText(trainingFile));
            if (!report.RootElement.GetProperty("Passed").GetBoolean()) throw new InvalidDataException("Terminal accepted training report required.");
            var paths=report.RootElement.GetProperty("AcceptedFrames").EnumerateArray()
                .Select(p=>p.GetString()!).Where(p=>p.EndsWith("-train.frame.json",StringComparison.Ordinal)).ToArray();
            report.Dispose();
            Require(paths.Length>0,"Independent designated training captures required");
            foreach(string path in paths)
            {
                var train=Read(path);
                Require(train.PpuMask==24,"Independent training has PPUMASK $18: "+Path.GetFileName(path));
                for(int y=0;y<60;y+=2) for(int x=0;x<64;x+=2)
                {
                    Rectangle bounds=new(x*8-train.ScrollX,y*8-train.ScrollY,16,16);
                    if (!new Rectangle(0,0,256,240).Contains(bounds) || !profile.Allows(bounds)) continue;
                    var key=MetatileSignature.Read(train,x,y).Key;
                    if (!profile.BackgroundRules.TryGetValue(key,out var rule)) continue;
                    string fingerprint=MetatileVisualFingerprint.Read(train,x,y);
                    var variant=rule.ArtworkVariants.FirstOrDefault(v=>v.Fingerprint==fingerprint);
                    if(variant is null) continue;
                    variant.RightHalfFingerprint=MetatileVisualFingerprint.ReadRightHalf(train,x,y);
                    learned++;
                }
            }
            Require(learned>0,"Half evidence learned only from independent training");
            profile.Normalize();
            foreach(int y in new[]{32,34,36,38})
            {
                var key=MetatileSignature.Read(hold,0,y);
                Require(!profile.BackgroundRules[key.Key].AcceptsArtwork(MetatileVisualFingerprint.Read(hold,0,y)),"Original full-cell mismatch retained "+y);
                Require(profile.Match(key,hold,0,y) is not null,"Known visible half accepted "+y+" proof="+MetatileVisualFingerprint.HasVerifiedMaskedLeftHalf(hold,0,y)+" half="+MetatileVisualFingerprint.ReadRightHalf(hold,0,y));
                Require(MetatileVisualFingerprint.IsVisible(hold,0,y,16,true),"Native half visibility proven "+y);
                var disabled=profile.Clone();disabled.AllowMaskedLeftEdgeArtwork=false;
                Require(disabled.Match(key,hold,0,y) is null,"Default full-cell policy rejects "+y);
                Require(profile.Match(key,hold with{PpuMask=26},0,y) is null,"Left-background enabled rejects fallback "+y);
            }
            var signature=MetatileSignature.Read(hold,0,32);
            int screenY=32*8-hold.ScrollY;
            int[] native=(int[])hold.NativeScreenPixels!.Clone();
            native[screenY*256+8]^=0x010101;
            Require(profile.Match(signature,hold with{NativeScreenPixels=native},0,32) is null,"Native right-half mismatch rejected");
            int[][] tables=hold.NametablePixels.Select(p=>(int[])p.Clone()).ToArray();
            native=(int[])hold.NativeScreenPixels.Clone();
            tables[2][16*256+8]^=0x012345;
            native[screenY*256+8]=tables[2][16*256+8];
            Require(profile.Match(signature,hold with{NametablePixels=tables,NativeScreenPixels=native},0,32) is null,"Unlearned right artwork rejected even with native agreement");
            Require(!MetatileVisualFingerprint.HasVerifiedMaskedLeftHalf(hold with{ScrollX=hold.ScrollX-16},0,32),"Fallback restricted to leftmost cell");
            tables=hold.NametablePixels.Select(p=>(int[])p.Clone()).ToArray();
            native=(int[])hold.NativeScreenPixels.Clone();
            for(int y=0;y<16;y++) for(int x=8;x<16;x++)
            {
                tables[2][(16+y)*256+x]=unchecked((int)0xff010101);
                native[(screenY+y)*256+x]=unchecked((int)0xff010101);
            }
            Require(!MetatileVisualFingerprint.HasVerifiedMaskedLeftHalf(hold with{NametablePixels=tables,NativeScreenPixels=native},0,32),"Uniform/blank visible half cannot activate masked matching");
            native=(int[])hold.NativeScreenPixels.Clone();native[screenY*256+1]^=0x010101;
            Require(profile.Match(signature,hold with{NativeScreenPixels=native},0,32) is null,"Nonuniform hidden half rejected");
            string exported=Path.Combine(output,"test-profile.json");
            GameRecognitionProfileStore.WriteToFile(exported,profile);
            var imported=GameRecognitionProfileStore.ReadFromFile(exported);
            Require(imported.AllowMaskedLeftEdgeArtwork && imported.Match(signature,hold,0,32) is not null,"Format-six export/import retains half evidence and policy");
            var invalid=profile.Clone();
            foreach(var rule in invalid.BackgroundRules.Values)
                foreach(var variant in rule.ArtworkVariants) variant.RightHalfFingerprint="not-a-fingerprint";
            invalid.Normalize();
            Require(invalid.Match(signature,hold,0,32) is null,"Invalid half fingerprint sanitized/rejected");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Training=trainingFile,OriginalHold=holdFile,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());return 1;}
        NesFrame Read(string path)=>CartridgeViewport.NormalizeCapture("smb3",JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!);
        void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);checks.Add(message);}
    }
}
