using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class SmbPairedAlignmentTests
{
    internal static int Run(string worldRom,string europeRom,string output)
    {
        Directory.CreateDirectory(output);List<object> cases=[];int failures=0;
        try
        {
            foreach(var game in new[]{(Path:worldRom,Name:"world"),(Path:europeRom,Name:"europe")})
            {
                using NesEmulator emulator=new();emulator.Load(game.Path);
                Require(emulator.IsSmbWorld,"Supported SMB required.");
                Thread.Sleep(1000);emulator.SetButton(NesButton.Start,true);Thread.Sleep(120);emulator.SetInputMask(0);Thread.Sleep(1500);
                emulator.SetButton(NesButton.Right,true);emulator.SetButton(NesButton.B,true);
                for(int sample=0;sample<8;sample++)
                {
                    emulator.SetButton(NesButton.A,sample%2==0);Thread.Sleep(350);
                    NesFrame frame=emulator.CaptureFrame()??throw new InvalidOperationException("Missing paired frame.");
                    Require(frame.CaptureScanline==96&&frame.NativeScreenSequence==frame.Sequence,"Coherent paired native video required.");
                    Require(frame.Ram[0x770]==1,"Ordinary gameplay required.");
                    int[] native=frame.NativeScreenPixels??throw new InvalidOperationException("Missing native video.");
                    using var scene=new SmbProfile().Build(frame,true);
                    using var background=(Bitmap)scene.Background.Clone();
                    foreach(var obj in scene.Objects.Where(o=>o.IdentityKey.StartsWith("background:")))
                    for(int py=0;py<obj.Image.Height;py++)for(int px=0;px<obj.Image.Width;px++)
                    {
                        int x=obj.Bounds.X+px,y=obj.Bounds.Y+py;Color color=obj.Image.GetPixel(px,py);
                        if(x>=0&&x<256&&y>=0&&y<240&&color.A!=0)background.SetPixel(x,y,color);
                    }
                    int logicalX=(frame.Ram[0x71a]<<8)|frame.Ram[0x71c];
                    using var logical=SmbProfile.ComposeBackground(frame with{ScrollX=logicalX},true);
                    Rectangle[] actors=scene.Objects.Where(o=>!o.IdentityKey.StartsWith("background:")).Select(o=>o.Bounds).ToArray();
                    int eligible=0,matched=0,hudEligible=0,hudMatched=0,logicalMatched=0;
                    using Bitmap reference=new(256,240);using Bitmap differences=new(256,240);
                    for(int y=0;y<240;y++)for(int x=0;x<256;x++)
                    {
                        int color=native[y*256+x]&0xffffff;reference.SetPixel(x,y,Color.FromArgb(color|unchecked((int)0xff000000)));
                        if(actors.Any(r=>r.Contains(x,y))||x<8)continue;
                        bool same=(background.GetPixel(x,y).ToArgb()&0xffffff)==color;
                        if(y<32){hudEligible++;if(same)hudMatched++;}
                        else{eligible++;if(same)matched++;if((logical.GetPixel(x,y).ToArgb()&0xffffff)==color)logicalMatched++;}
                        differences.SetPixel(x,y,same?Color.Black:Color.Red);
                    }
                    bool passed=eligible>40000&&matched==eligible&&hudMatched==hudEligible;
                    if(!passed)failures++;
                    string key=$"{game.Name}-{sample:00}";
                    reference.Save(Path.Combine(output,key+"-native.png"),ImageFormat.Png);
                    background.Save(Path.Combine(output,key+"-background.png"),ImageFormat.Png);
                    differences.Save(Path.Combine(output,key+"-diff.png"),ImageFormat.Png);
                    if(!passed&&failures<=2)File.WriteAllText(Path.Combine(output,key+"-frame.json"),JsonSerializer.Serialize(frame));
                    cases.Add(new{Game=game.Name,Sample=sample,frame.Sequence,frame.ScrollX,frame.RawScrollX,LogicalX=logicalX,LogicalMatched=logicalMatched,frame.PpuMask,Eligible=eligible,Matched=matched,HudEligible=hudEligible,HudMatched=hudMatched,Passed=passed});
                }
                emulator.SetInputMask(0);
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Frames=cases.Count,Failures=failures,Paired=true,NoRamWrites=true,Scope="First-stage ordinary-input background alignment excluding extracted sprite rectangles and left8 pixels; no actor identity or physical display proof.",Cases=cases},new JsonSerializerOptions{WriteIndented=true}));
            return failures==0?0:1;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());return 1;}
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
