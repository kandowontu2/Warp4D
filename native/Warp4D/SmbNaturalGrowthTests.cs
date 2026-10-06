using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D;

internal static class SmbNaturalGrowthTests
{
    internal static int Run(string worldRom,string europeRom,string output,bool luigi=false,bool forms=false)
    {
        Directory.CreateDirectory(output);List<object> games=[];
        try
        {
            foreach(var game in new[]{(Path:worldRom,Name:"world"),(Path:europeRom,Name:"europe")})
            {
                using NesEmulator emulator=new();emulator.Load(game.Path);
                Require(emulator.IsSmbWorld,"Supported exact SMB required.");
                Thread.Sleep(1000);
                if(luigi)
                {emulator.SetButton(NesButton.Select,true);Thread.Sleep(120);emulator.SetInputMask(0);Thread.Sleep(150);}
                emulator.SetButton(NesButton.Start,true);Thread.Sleep(120);emulator.SetInputMask(0);
                if(luigi)WaitForNaturalLuigiTurn(emulator,game.Name,output);
                Stopwatch time=Stopwatch.StartNew();List<object> states=[];List<object> poses=[];
                bool approached=false,spawned=false,grown=false,hopping=false,avoided=false;int growthPoses=0;double jumpUntil=0,nextJump=0;
                while(time.Elapsed.TotalSeconds<35&&!grown)
                {
                    Thread.Sleep(40);var frame=emulator.CaptureFrame();if(frame is null)continue;
                    int x=(frame.Ram[0x6d]<<8)|frame.Ram[0x86],y=frame.Ram[0xce];
                    int size=frame.Ram[0x754],status=frame.Ram[0x756],change=frame.Ram[0x70b];
                    bool gameplay=frame.Ram[0x770]==1&&frame.Ram[0x772]==3&&frame.Ram[0x0e] is 8 or 9&&(frame.PpuMask.GetValueOrDefault()&24)==24;
                    if(!gameplay)continue;
                    Require(frame.Ram[0x753]==(luigi?1:0),"Keep the requested native player turn during growth.");
                    if(poses.Count==0)
                    {
                        using var initial=SmbPlayerOwnershipTests.OwnedImage(frame,out _);
                        if(initial is not null)poses.Add(Save(game.Name,"small",frame,output));
                    }
                    if(change!=0&&growthPoses<3)
                    {poses.Add(Save(game.Name,"growth-"+growthPoses,frame,output));growthPoses++;}
                    if(size==0&&status>0&&change==0&&frame.Ram[0x0e]==8)
                    {emulator.SetInputMask(0);poses.Add(Save(game.Name,"big",frame,output));grown=true;break;}
                    spawned|=frame.Ram[0x14]!=0&&frame.Ram[0x1b]==0x2e;
                    states.Add(new{Seconds=time.Elapsed.TotalSeconds,X=x,Y=y,Size=size,Status=status,Change=change,Spawned=spawned,Engine=frame.Ram[0x0e],PlayerState=frame.Ram[0x1d],Hopping=hopping,Avoided=avoided});
                    if(hopping&&time.Elapsed.TotalSeconds>jumpUntil+.2&&frame.Ram[0x1d]==0)avoided=true;
                    if(avoided&&x is >=324 and <=336)approached=true;
                    bool danger=Enumerable.Range(0,5).Any(slot=>frame.Ram[0x0f+slot]!=0&&frame.Ram[0x16+slot]==6&&
                        ((frame.Ram[0x6e+slot]<<8)|frame.Ram[0x87+slot])-x is >-12 and <65);
                    if(frame.Ram[0x1d]==0&&frame.Ram[0x0e]==8&&time.Elapsed.TotalSeconds>=nextJump&&
                        (!hopping&&danger||avoided&&approached&&!spawned))
                    {hopping=true;jumpUntil=time.Elapsed.TotalSeconds+.25;nextJump=time.Elapsed.TotalSeconds+.7;}
                    int target=hopping&&!avoided?270:328;
                    int horizontal=spawned?(int)NesButton.Right:x<target-2?(int)NesButton.Right:x>target+2?(int)NesButton.Left:0;
                    int input=horizontal|(time.Elapsed.TotalSeconds<jumpUntil?(int)NesButton.A:0);
                    emulator.SetInputMask(input);
                    File.WriteAllText(Path.Combine(output,game.Name+"-progress.json"),JsonSerializer.Serialize(new{Approached=approached,Spawned=spawned,Grown=grown,GrowthPoses=growthPoses,States=states}));
                }
                emulator.SetInputMask(0);
                Require(grown,$"{game.Name}: natural mushroom collection/growth not reached within35seconds.");
                Require(growthPoses>0,"Observe natural size-changing animation, not just a large starting state.");
                // Larger standing, walking, left-facing and airborne textures.
                foreach(var pose in new[]{("left",NesButton.Left), ("walk",NesButton.Right), ("jump",NesButton.A)})
                {
                    var before=emulator.CaptureFrame()!;int beforeX=(before.Ram[0x6d]<<8)|before.Ram[0x86];
                    emulator.SetButton(pose.Item2,true);Thread.Sleep(160);emulator.SetInputMask(0);
                    var frame=emulator.CaptureFrame()!;Require(frame.Ram[0x754]==0,"Keep naturally earned large form.");
                    if(pose.Item2==NesButton.A)Require(frame.Ram[0x1d]!=0,"Actual airborne pose required.");
                    else
                    {
                        int afterX=(frame.Ram[0x6d]<<8)|frame.Ram[0x86];
                        Require(pose.Item2==NesButton.Right?afterX>beforeX:afterX<beforeX,"Actual movement in requested direction required.");
                    }
                    poses.Add(Save(game.Name,"big-"+pose.Item1,frame,output,beforeX));
                }
                object? formChecks=forms?CheckNaturalForms(emulator,game.Name,luigi,output,poses):null;
                games.Add(new{game.Name,PlayerName=luigi?"Luigi":"Mario",NaturallyEarnedMushroom=true,GrowthPoses=growthPoses,Seconds=time.Elapsed.TotalSeconds,FormChecks=formChecks,FullRateInjuryPairs=emulator.SmbInjuryFullRatePairsForTest,Poses=poses});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{NoRamWrites=true,NoStateLoads=true,NaturalTwoPlayerSwitches=luigi?2:0,Games=games},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());return 1;}
    }
    private static object CheckNaturalForms(NesEmulator emulator,string game,bool luigi,string output,List<object> poses)
    {
        Stopwatch landing=Stopwatch.StartNew();NesFrame? standing=null;
        while(landing.Elapsed.TotalSeconds<3)
        {Thread.Sleep(20);standing=emulator.CaptureFrame();if(standing?.Ram[0x1d]==0&&standing.Ram[0x0e]==8)break;}
        Require(standing is not null&&standing.Ram[0x1d]==0,"Land naturally before crouch.");
        emulator.SetButton(NesButton.Down,true);Thread.Sleep(140);var crouch=emulator.CaptureFrame()!;emulator.SetInputMask(0);
        Require(crouch.Ram[0x714]!=0&&crouch.Ram[0x754]==0,"Actual native big-player crouch required.");
        using(var body=SmbPlayerOwnershipTests.OwnedImage(crouch,out var bounds))
            Require(body is not null&&bounds.Height<32,"Crouching body must be shorter than standing, not cut by a guessed fixed rectangle.");
        poses.Add(Save(game,"crouch",crouch,output));Thread.Sleep(140);
        var released=emulator.CaptureFrame()!;Require(released.Ram[0x714]==0,"Release crouch naturally.");
        poses.Add(Save(game,"big-after-crouch",released,output));
        Stopwatch time=Stopwatch.StartNew();double jumpUntil=0,nextJump=0;bool injured=false,shrunk=false;int injuryPoses=0,hiddenChecks=0;long previousSequence=-1;List<object> states=[];List<object> hidden=[];
        while(time.Elapsed.TotalSeconds<40&&!shrunk)
        {
            Thread.Sleep(40);var frame=emulator.CaptureFrame();if(frame is null||frame.Sequence==previousSequence)continue;previousSequence=frame.Sequence;
            Require(frame.CaptureScanline==96&&frame.NativeScreenSequence==frame.Sequence,"Paired native shrink/blink capture required.");
            Require(frame.Ram[0x770]==1&&frame.Ram[0x772]==3&&(frame.PpuMask.GetValueOrDefault()&24)==24,"Active rendered gameplay required throughout crouch/shrink checks.");
            Require(frame.Ram[0x753]==(luigi?1:0),"Requested player must survive through native shrink.");
            int x=(frame.Ram[0x6d]<<8)|frame.Ram[0x86],engine=frame.Ram[0x0e],size=frame.Ram[0x754],status=frame.Ram[0x756],change=frame.Ram[0x70b],timer=frame.Ram[0x79e];
            var enemies=Enumerable.Range(0,5).Where(slot=>frame.Ram[0x0f+slot]!=0)
                .Select(slot=>new{Slot=slot,Id=frame.Ram[0x16+slot],State=frame.Ram[0x1e+slot],X=(frame.Ram[0x6e+slot]<<8)|frame.Ram[0x87+slot],Y=frame.Ram[0xcf+slot]}).ToArray();
            states.Add(new{Seconds=time.Elapsed.TotalSeconds,X=x,Y=frame.Ram[0xce],Engine=engine,Size=size,Status=status,Change=change,InjuryTimer=timer,PlayerState=frame.Ram[0x1d],Enemies=enemies});
            File.WriteAllText(Path.Combine(output,game+"-forms-progress.json"),JsonSerializer.Serialize(states));
            injured|=status==0&&timer>0;
            if(injured)
            {
                emulator.SetInputMask(0);
                using var body=SmbPlayerOwnershipTests.OwnedImage(frame,out _);
                if(body is null)
                {
                    using var scene=new SmbProfile().Build(frame,true);
                    Require(!scene.Objects.Any(o=>o.Kind==SceneObjectKind.Player),"Hidden injury body must not fabricate a player from nearby enemies.");
                    hidden.Add(new{frame.Sequence,frame.NativeScreenSequence,NativeFrameCounter=frame.Ram[9],Player=frame.Ram[0x753],Engine=engine,Size=size,Status=status,InjuryTimer=timer,PlayerObjects=0});
                    if(hiddenChecks==0)SaveHidden(game,frame,scene,output);
                    hiddenChecks++;
                }
                else if(injuryPoses<3&&timer>0)
                {poses.Add(Save(game,"injury-"+injuryPoses,frame,output));injuryPoses++;}
                if(body is not null&&size==1&&change==0&&engine==8&&timer==0)
                {poses.Add(Save(game,"small-after-hit",frame,output));shrunk=true;break;}
                continue;
            }
            Require(engine!=11,"Do not substitute a death/restart for natural shrink.");
            int[] pipes=[448,608,736,912];
            var target=enemies.Where(enemy=>enemy.Id==6&&(enemy.State&0x20)==0&&enemy.Y>=150&&Math.Abs(enemy.X-x)<88&&
                !pipes.Any(pipe=>pipe>Math.Min(x,enemy.X)&&pipe+32<Math.Max(x,enemy.X))).OrderBy(enemy=>Math.Abs(enemy.X-x)).FirstOrDefault();
            if(engine==8&&frame.Ram[0x1d]==0&&target is not null)
            {
                // Meet the actual live Goomba on the ground; do not let the
                // next pipe jump accidentally turn the desired hit into a stomp.
                jumpUntil=0;emulator.SetInputMask((int)(target.X>=x?NesButton.Right:NesButton.Left));continue;
            }
            bool pipeApproach=pipes.Any(pipe=>x>=pipe-50&&x<=pipe+25);
            if(engine==8&&frame.Ram[0x1d]==0&&pipeApproach&&time.Elapsed.TotalSeconds>=nextJump)
            {jumpUntil=time.Elapsed.TotalSeconds+.35;nextJump=time.Elapsed.TotalSeconds+.65;}
            emulator.SetInputMask((int)(NesButton.Right|NesButton.B)|(time.Elapsed.TotalSeconds<jumpUntil?(int)NesButton.A:0));
        }
        emulator.SetInputMask(0);Require(injured&&shrunk,"Native enemy collision/shrink not reached within40seconds.");
        Require(hiddenChecks>0,"Observe actual native hidden injury phases, not just visible shrinking poses.");
        return new{NaturalCrouch=true,NaturalCrouchRelease=true,NaturalEnemyInjury=true,NaturalShrink=true,VisibleInjuryPoses=injuryPoses,HiddenBodyChecks=hiddenChecks,Seconds=time.Elapsed.TotalSeconds,HiddenCases=hidden};
    }
    private static void SaveHidden(string game,NesFrame frame,SmbScene scene,string output)
    {
        using var native=new Bitmap(256,240);
        for(int y=0;y<240;y++)for(int x=0;x<256;x++)native.SetPixel(x,y,Color.FromArgb(frame.NativeScreenPixels![y*256+x]|unchecked((int)0xff000000)));
        native.Save(Path.Combine(output,game+"-hidden-injury-native.png"),ImageFormat.Png);
        using var renderer=new WarpRendererControl(){Size=new(820,740),UseGpu=true};renderer.CreateControl();renderer.SetScene(scene.Clone());
        using var projected=new Bitmap(820,740);renderer.DrawToBitmap(projected,renderer.ClientRectangle);projected.Save(Path.Combine(output,game+"-hidden-injury-projected.png"),ImageFormat.Png);
    }
    private static void WaitForNaturalLuigiTurn(NesEmulator emulator,string game,string output)
    {
        Stopwatch time=Stopwatch.StartNew();bool mario=false;List<object> states=[];
        while(time.Elapsed.TotalSeconds<35)
        {
            Thread.Sleep(60);var frame=emulator.CaptureFrame();if(frame is null)continue;
            states.Add(new{Seconds=time.Elapsed.TotalSeconds,Player=frame.Ram[0x753],Mode=frame.Ram[0x770],Task=frame.Ram[0x772],Engine=frame.Ram[0x0e],NumberOfPlayers=frame.Ram[0x77a]});
            File.WriteAllText(Path.Combine(output,game+"-turn-progress.json"),JsonSerializer.Serialize(states));
            if(frame.Ram[0x770]!=1||frame.Ram[0x772]!=3||frame.Ram[0x0e]!=8||(frame.PpuMask.GetValueOrDefault()&24)!=24)continue;
            Require(frame.Ram[0x77a]==1,"Ordinary menu must select two-player mode.");
            using var body=SmbPlayerOwnershipTests.OwnedImage(frame,out _);if(body is null)continue;
            if(frame.Ram[0x753]==0&&!mario)
            {Save(game,"mario-before-switch",frame,output);mario=true;emulator.SetInputMask((int)(NesButton.Right|NesButton.B));}
            if(frame.Ram[0x753]==1&&mario){emulator.SetInputMask(0);return;}
        }
        emulator.SetInputMask(0);throw new InvalidOperationException("Natural Mario-death/Luigi turn not reached within35seconds.");
    }
    internal static object Save(string game,string pose,NesFrame frame,string output,int? beforeWorldX=null)
    {
        Require(frame.CaptureScanline==96&&frame.NativeScreenSequence==frame.Sequence,"Paired native pose required.");
        using var expected=SmbPlayerOwnershipTests.OwnedImage(frame,out var bounds);
        using var scene=new SmbProfile().Build(frame,true);
        var actors=scene.Objects.Where(o=>o.Kind==SceneObjectKind.Player).ToArray();
        Require(expected is not null&&actors.Length==1&&actors[0].Bounds==bounds&&SmbPlayerOwnershipTests.Equal(actors[0].Image,expected),
            game+"/"+pose+": exact reserved player body required. "+JsonSerializer.Serialize(new{Expected=bounds,Actual=actors.Select(o=>o.Bounds).ToArray()}));
        string playerName=frame.Ram[0x753]==1?"Luigi":"Mario";
        Require(actors[0].Label==playerName,"Exact native player label required throughout growth and motion.");
        if(pose.StartsWith("big"))Require(bounds.Height==32,"Stable large body must retain all four sprite rows.");
        int opaque=0,matched=0;
        for(int y=0;y<expected!.Height;y++)for(int x=0;x<expected.Width;x++)
        {
            Color pixel=expected.GetPixel(x,y);int sx=x+bounds.X,sy=y+bounds.Y;
            if(pixel.A==0||sx<8||sx>=256||sy<0||sy>=240)continue;opaque++;
            if((pixel.ToArgb()&0xffffff)==(frame.NativeScreenPixels![sy*256+sx]&0xffffff))matched++;
        }
        Require(opaque>0&&matched==opaque,"Exact visible native player colors required.");
        string key=game+"-"+pose;
        using var native=new Bitmap(256,240);
        for(int y=0;y<240;y++)for(int x=0;x<256;x++)native.SetPixel(x,y,Color.FromArgb(frame.NativeScreenPixels![y*256+x]|unchecked((int)0xff000000)));
        native.Save(Path.Combine(output,key+"-native.png"),ImageFormat.Png);expected.Save(Path.Combine(output,key+"-owned.png"),ImageFormat.Png);
        using var renderer=new WarpRendererControl(){Size=new(820,740),UseGpu=true};renderer.CreateControl();renderer.SetScene(scene.Clone());
        using var projected=new Bitmap(820,740);renderer.DrawToBitmap(projected,renderer.ClientRectangle);projected.Save(Path.Combine(output,key+"-projected.png"),ImageFormat.Png);
        return new{Pose=pose,Player=frame.Ram[0x753],ActualLabel=actors[0].Label,ExpectedLabel=playerName,frame.Sequence,NativeFrameCounter=frame.Ram[9],Bounds=bounds,Size=frame.Ram[0x754],Status=frame.Ram[0x756],Change=frame.Ram[0x70b],Crouching=frame.Ram[0x714],InjuryTimer=frame.Ram[0x79e],Engine=frame.Ram[0x0e],PlayerState=frame.Ram[0x1d],WorldX=(frame.Ram[0x6d]<<8)|frame.Ram[0x86],BeforeWorldX=beforeWorldX,Y=frame.Ram[0xce],NativeOpaque=opaque,NativeMatched=matched};
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
