using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D;

// Ordinary no-input title -> demo -> title. This is not a paired-native video
// alignment test or evidence of physical GPU presentation.
internal static class SmbAttractTransitionTests
{
    internal static int Run(string worldRom, string europeRom, string output,bool storeFrames=true)
    {
        Directory.CreateDirectory(output);
        List<object> games=[];
        try
        {
            foreach(var game in new[]{(Path:worldRom,Name:"world"),(Path:europeRom,Name:"europe")})
            {
                using NesEmulator emulator=new(); emulator.Load(game.Path);
                Require(emulator.IsSmbWorld,"Supported SMB identity required.");
                Stopwatch clock=Stopwatch.StartNew(); List<object> transitions=[];
                string previous=""; int phase=0; bool pipe=false;
                while(clock.Elapsed.TotalSeconds<65 && phase<3)
                {
                    Thread.Sleep(100);
                    NesFrame? frame=emulator.CaptureFrame(); if(frame is null)continue;
                    int mode=frame.Ram[0x770],task=frame.Ram[0x772],timer=frame.Ram[0x7a2];
                    string state=$"{mode}:{task}:{timer}";
                    if(state!=previous)
                    {
                        transitions.Add(new{Seconds=clock.Elapsed.TotalSeconds,Mode=mode,Task=task,Timer=timer,frame.ScrollX});
                        previous=state;
                    }
                    bool title=mode==0&&task==3&&timer>0 && (frame.PpuMask is not byte mask || (mask&0x08)!=0);
                    bool demo=SmbProfile.IsAttractDemo(frame);
                    using SmbScene scene=new SmbProfile().Build(frame,true);
                    var background=scene.Objects.Where(o=>o.IdentityKey.StartsWith("background:")).ToArray();
                    if(title)Require(background.Length==0,"Natural title must keep scenery flat.");
                    if(demo)
                    {
                        pipe|=background.Any(o=>o.Kind==SceneObjectKind.Pipe&&o.ProjectionEnabled);
                        if(phase==1 && pipe && background.Any(o=>o.Kind==SceneObjectKind.Terrain&&o.ProjectionEnabled)
                            &&background.Any(o=>o.Kind is SceneObjectKind.Bush or SceneObjectKind.Hill or SceneObjectKind.Cloud))
                        {Save(game.Name,"demo",frame,scene,output,storeFrames);phase=2;}
                    }
                    if(title&&phase==0){Save(game.Name,"title",frame,scene,output,storeFrames);phase=1;}
                    else if(title&&phase==2){Save(game.Name,"return",frame,scene,output,storeFrames);phase=3;}
                    File.WriteAllText(Path.Combine(output,game.Name+"-progress.json"),JsonSerializer.Serialize(new{Phase=phase,Seconds=clock.Elapsed.TotalSeconds,Transitions=transitions}));
                }
                Require(phase==3,$"{game.Name}: natural complete title/demo/title cycle timed out (phase {phase}).");
                games.Add(new{game.Name,CompleteCycle=true,PipeProjected=pipe,Seconds=clock.Elapsed.TotalSeconds,Transitions=transitions});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{NoInput=true,NoRamWrites=true,PhysicalDisplayVerified=false,Games=games},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());return 1;}
    }
    private static void Save(string game,string pose,NesFrame frame,SmbScene scene,string output,bool storeFrames)
    {
        if(storeFrames)File.WriteAllText(Path.Combine(output,$"{game}-{pose}-frame.json"),JsonSerializer.Serialize(frame));
        File.WriteAllText(Path.Combine(output,$"{game}-{pose}-state.json"),JsonSerializer.Serialize(new{frame.Sequence,frame.NativeScreenSequence,frame.CaptureScanline,frame.PpuMask,frame.ScrollX,Mode=frame.Ram[0x770],Task=frame.Ram[0x772],Timer=frame.Ram[0x7a2]}));
        using WarpRendererControl renderer=new(){Size=new(820,740),UseGpu=true};
        renderer.CreateControl(); renderer.SetScene(scene.Clone());
        using Bitmap image=new(820,740);renderer.DrawToBitmap(image,renderer.ClientRectangle);
        image.Save(Path.Combine(output,$"{game}-{pose}.png"),ImageFormat.Png);
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
