using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D;

internal static class SmbTwoPlayerTests
{
    internal static int Run(string worldRom,string europeRom,string output)
    {
        Directory.CreateDirectory(output);List<object> poses=[];int failures=0;
        try
        {
            foreach(var game in new[]{(Path:worldRom,Name:"world"),(Path:europeRom,Name:"europe")})
            {
                using NesEmulator emulator=new();emulator.Load(game.Path);
                Require(emulator.IsSmbWorld,"Exact SMB identity required.");
                Thread.Sleep(1000);emulator.SetButton(NesButton.Select,true);Thread.Sleep(120);emulator.SetInputMask(0);Thread.Sleep(150);
                emulator.SetButton(NesButton.Start,true);Thread.Sleep(120);emulator.SetInputMask(0);
                Stopwatch time=Stopwatch.StartNew();bool mario=false,luigi=false;List<object> states=[];
                while(time.Elapsed.TotalSeconds<35&&!luigi)
                {
                    Thread.Sleep(60);var frame=emulator.CaptureFrame();if(frame is null)continue;
                    states.Add(new{Seconds=time.Elapsed.TotalSeconds,Mode=frame.Ram[0x770],Task=frame.Ram[0x772],Engine=frame.Ram[0x0e],Player=frame.Ram[0x753],NumberOfPlayers=frame.Ram[0x77a],X=(frame.Ram[0x6d]<<8)|frame.Ram[0x86]});
                    File.WriteAllText(Path.Combine(output,game.Name+"-progress.json"),JsonSerializer.Serialize(states));
                    if(frame.Ram[0x770]!=1||frame.Ram[0x772]!=3||frame.Ram[0x0e]!=8||(frame.PpuMask.GetValueOrDefault()&24)!=24)continue;
                    Require(frame.Ram[0x77a]==1,"Ordinary menu must select two-player mode.");
                    using var expected=SmbPlayerOwnershipTests.OwnedImage(frame,out var bounds);if(expected is null)continue;
                    int player=frame.Ram[0x753];
                    if(player==0&&!mario)
                    {poses.Add(Save(game.Name,"Mario",frame,bounds,expected,output,out bool passed));if(!passed)failures++;mario=true;emulator.SetButton(NesButton.Right,true);emulator.SetButton(NesButton.B,true);}
                    if(player==1&&mario)
                    {emulator.SetInputMask(0);poses.Add(Save(game.Name,"Luigi",frame,bounds,expected,output,out bool passed));if(!passed)failures++;luigi=true;}
                }
                emulator.SetInputMask(0);Require(mario&&luigi,"Natural Mario death/next Luigi turn not observed within35seconds.");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{NoRamWrites=true,NoStateLoads=true,NaturalTwoPlayerSwitches=2,Failures=failures,Poses=poses},new JsonSerializerOptions{WriteIndented=true}));return failures==0?0:1;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());return 1;}
    }
    private static object Save(string game,string name,NesFrame frame,Rectangle bounds,Bitmap expected,string output,out bool passed)
    {
        Require(frame.CaptureScanline==96&&frame.NativeScreenSequence==frame.Sequence,"Paired native body required.");
        using var scene=new SmbProfile().Build(frame,true);
        var actors=scene.Objects.Where(o=>o.Kind==SceneObjectKind.Player).ToArray();
        int pixels=0,matched=0;
        for(int y=0;y<expected.Height;y++)for(int x=0;x<expected.Width;x++)
        {
            Color color=expected.GetPixel(x,y);int sx=x+bounds.X,sy=y+bounds.Y;
            if(color.A==0||sx<8||sx>=256||sy<0||sy>=240)continue;pixels++;
            if((color.ToArgb()&0xffffff)==(frame.NativeScreenPixels![sy*256+sx]&0xffffff))matched++;
        }
        bool body=pixels>0&&matched==pixels&&actors.Length==1&&actors[0].Bounds==bounds&&SmbPlayerOwnershipTests.Equal(expected,actors[0].Image);
        passed=body&&actors[0].Label==name;
        string key=game+"-"+name.ToLowerInvariant();
        using Bitmap native=new(256,240);
        for(int y=0;y<240;y++)for(int x=0;x<256;x++)native.SetPixel(x,y,Color.FromArgb(frame.NativeScreenPixels![y*256+x]|unchecked((int)0xff000000)));
        native.Save(Path.Combine(output,key+"-native.png"),ImageFormat.Png);expected.Save(Path.Combine(output,key+"-owned.png"),ImageFormat.Png);
        using var renderer=new WarpRendererControl(){Size=new(820,740),UseGpu=true};renderer.CreateControl();renderer.SetScene(scene.Clone());
        using Bitmap projected=new(820,740);renderer.DrawToBitmap(projected,renderer.ClientRectangle);projected.Save(Path.Combine(output,key+"-projected.png"),ImageFormat.Png);
        return new{Game=game,ExpectedLabel=name,ActualLabels=actors.Select(o=>o.Label).ToArray(),BodyExact=body,NativePixels=pixels,NativeMatched=matched,Player=frame.Ram[0x753],frame.Sequence,Bounds=bounds,Passed=passed};
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
