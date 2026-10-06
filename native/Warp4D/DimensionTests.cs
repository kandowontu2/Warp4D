using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class DimensionTests
{
    internal static int Run(string output,string? rom)
    {
        Directory.CreateDirectory(output);string? home=Environment.GetEnvironmentVariable("WARP4D_HOME");bool motion=InterfaceMotion.Enabled;
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        Dictionary<string,object> results=[];
        try
        {
            InterfaceMotion.Enabled=true;
            var box=Geometry4D.SolidSection(10,12,8,9,default,0);
            Require(box.Count==6,"Unrotated solid section has six faces");
            var rotated=Geometry4D.SolidSection(10,12,8,9,Rotation4D.Default,3);
            Require(rotated.Count>=4&&rotated.SelectMany(f=>f).All(p=>Math.Abs(p.Position.W-3)<.0001f),"Rotated polytope lies precisely on slicing plane");
            Require(Geometry4D.SolidSection(10,12,8,9,default,20).Count==0,"Outside solid has empty section");results["TrueSolidSections"]=true;
            var choreography=new DimensionSettings();
            var player=choreography.ClassMotions["Player"];var pipe=choreography.ClassMotions["Pipe"];
            Require(DimensionalMotion.Offset(player,"player",1)!=DimensionalMotion.Offset(pipe,"pipe",1),"Class motion differs");
            Require(DimensionalMotion.Offset(player,"p1",1)!=DimensionalMotion.Offset(player,"p2",1),"Independent identities phase differently");
            Require(DimensionalMotion.Offset(player,"p1",1)!=DimensionalMotion.Offset(player,"p1",2),"Motion progresses");results["Choreography"]=true;
            using var sample=LookGalleryForm.CreateSample();
            using WarpRendererControl renderer=new(){Size=new(720,600),UseGpu=false,PresentationMode=true};renderer.CreateControl();renderer.SetScene(sample.Clone());
            PresentationSettings current=new(){Opacity=.73f};renderer.ApplySettings(current);
            var a=LookCatalog.Create(5,current);var b=LookCatalog.Create(6,current);a.Animate=b.Animate=false;
            renderer.ApplyCrossfade(a,b,.45f,current);using var middle=Capture(renderer,"blend-middle");
            var blend=renderer.Settings.Clone();PresentationSettingsStore.WriteToFile(Path.Combine(output,"blend.json"),blend);
            renderer.ApplySettings(PresentationSettingsStore.ReadFromFile(Path.Combine(output,"blend.json")));using var restored=Capture(renderer,"blend-restored");
            Require(Same(middle,restored),"Saved mixture restores actual UV geometry");
            renderer.ApplyCrossfade(a,b,1,current);using var endpoint=Capture(renderer,"blend-b");Require(!Same(middle,endpoint),"Visible cross-mode blend");
            Require(renderer.ProjectionOpacity==.73f,"Crossfader does not touch opacity");results["PersistentCrossfade"]=true;
            current.Dimensions.Choreography=true;renderer.ApplySettings(current);renderer.MotionSecondsForTest=1;using var moving=Capture(renderer,"choreography-1");
            renderer.MotionSecondsForTest=2;using var moving2=Capture(renderer,"choreography-2");Require(!Same(moving,moving2),"Choreography visible");
            current.Dimensions.Choreography=false;current.Dimensions.AudioReactive=true;renderer.ApplySettings(current);renderer.AudioLevel=0;using var quiet=Capture(renderer,"audio-quiet");
            renderer.AudioLevel=1;using var loud=Capture(renderer,"audio-loud");Require(!Same(quiet,loud),"Audio envelope visibly affects projection");results["AudioReaction"]=true;
            using(var meter=new SessionAudioMeter()){Require(meter.Sample(false)==0,"Disabled meter neutral");float level=meter.Sample(true);Require(float.IsFinite(level)&&level>=0&&level<=1,"Safe session peak meter");}
            current.Dimensions.AudioReactive=false;current.Dimensions.Trails=true;renderer.ApplySettings(current);renderer.MotionSecondsForTest=.2;renderer.AngleXWDegrees=0;using var echo1=Capture(renderer,"trail-first");
            renderer.MotionSecondsForTest=.4;renderer.AngleXWDegrees=60;using var echoes=Capture(renderer,"trail-on");
            current.Dimensions.Trails=false;renderer.RefreshEffects();using var noEcho=Capture(renderer,"trail-off");Require(!Same(echoes,noEcho),"Rotation echoes visible");results["CurrentArtworkTrails"]=true;
            current.Dimensions.Choreography=true;current.Dimensions.AudioReactive=true;renderer.ApplySettings(current);InterfaceMotion.Enabled=false;
            renderer.MotionSecondsForTest=20;renderer.AudioLevel=0;using var frozen=Capture(renderer,"reduced-motion-1");renderer.MotionSecondsForTest=30;renderer.AudioLevel=1;using var frozen2=Capture(renderer,"reduced-motion-2");Require(Same(frozen,frozen2),"Reduced motion freezes dimensional animation and audio glow");InterfaceMotion.Enabled=true;results["ReducedMotion"]=true;
            current.Dimensions.Choreography=false;current.Dimensions.AudioReactive=false;current.ClassGeometries["Player"]=new(){Mode=GeometryMode.Ribbon};renderer.ApplySettings(current);
            Require(renderer.Settings.GeometryFor(sample.Objects[0]).Mode==GeometryMode.Ribbon,"Class paint applies");
            current.ObjectGeometries[sample.Objects[0].PresentationKey]=new(){Mode=GeometryMode.Duocylinder};renderer.ApplySettings(current);
            Require(renderer.Settings.GeometryFor(sample.Objects[0]).Mode==GeometryMode.Duocylinder,"Object painting wins over class");results["ProfilePainting"]=true;
            var adaptive=new AdaptiveDetail();for(int i=0;i<350;i++)adaptive.Sample(40,true,60);Require(adaptive.Level==3,"Slow renderer reduces detail");for(int i=0;i<900;i++)adaptive.Sample(2,true,60);Require(adaptive.Level==0,"Fast renderer restores detail");adaptive.Sample(50,false,60);Require(adaptive.Level==0,"Opt out restores detail");results["AdaptiveQuality"]=true;
            float xy=renderer.AngleXYDegrees;renderer.AdjustPlane("XY",12);Require(Math.Abs(renderer.AngleXYDegrees-xy-12)<.01,"All-plane compass changes orientation");
            var w=renderer.RotationForCompass;renderer.DragCompass(.2f,.1f,Keys.Control);Require(renderer.RotationForCompass.XW==w.XW&&renderer.RotationForCompass.YW==w.YW&&renderer.RotationForCompass.ZW==w.ZW,"Compass W lock");results["SixPlaneCompass"]=true;
            renderer.NativeScreenPixels=Enumerable.Range(0,61440).Select(i=>unchecked((int)0xff000000)|((i%256)<<16)|((i/256)<<8)).ToArray();renderer.RevealOriginal=true;
            Bitmap? native=null;void GetNative(Bitmap image)=>native=new(image);renderer.CleanFrameRendered+=GetNative;using var reveal=Capture(renderer,"original-reveal");renderer.CleanFrameRendered-=GetNative;
            using(native){Require(native is not null&&ImagePixels.Read(native).Pixels.SequenceEqual(renderer.NativeScreenPixels),"Reveal is pixel-exact completed native frame");}
            renderer.RevealOriginal=false;results["OriginalReveal"]=true;
            using(var studio=new DimensionStudioForm(renderer,()=>current,s=>{current=s;renderer.ApplySettings(s);},(left,right,t)=>{renderer.ApplyCrossfade(left,right,t,current);current=renderer.Settings;},()=>{},()=>{}))
            {
                studio.Show();Pump(100);studio.SelectObject(sample.Objects[0]);studio.SetFaderForTest(700);Require(current.BlendAmount==.7f,"Live UI fader");
                studio.SetAutoForTest(true);Pump(250);Require(current.BlendAmount!=.7f,"Automatic UI blend progresses");studio.StopAutoBlend();
                for(int tab=0;tab<4;tab++){studio.SelectTabForTest(tab);Pump(50);using var image=Capture(studio,"studio-tab-"+tab);}
                studio.Close();
            }
            results["LiveStudioControls"]=true;
            string video=Path.Combine(output,"clean-synthetic.avi");
            using(var recorder=new CleanVideoRecorder(video))
            {
                WriteWave(recorder.WavePath);for(int i=0;i<8;i++){recorder.Offer(middle);Thread.Sleep(40);}recorder.FinishAsync().GetAwaiter().GetResult();
            }
            Require(File.Exists(video)&&new FileInfo(video).Length>4096,"AVI finalized");results["CleanVideoWithPcm"]=true;
            if(rom is not null)
            {
                using Form window=new(){ClientSize=new(600,560)};using WarpRendererControl live=new(){Dock=DockStyle.Fill,UseGpu=true,PresentationMode=true};window.Controls.Add(live);window.Show();Pump(100);
                using NesEmulator emulator=new();emulator.Load(rom,window.Handle,live.Handle);using SessionAudioMeter meter=new();
                Pump(1000);emulator.SetButton(NesButton.Start,true);Pump(120);emulator.SetButton(NesButton.Start,false);Pump(1000);
                using CleanVideoRecorder recorder=new(Path.Combine(output,"native-gameplay.avi"));MesenApi.WaveRecord(recorder.WavePath);
                void Offer(Bitmap bitmap)=>recorder.Offer(bitmap);live.CleanFrameRendered+=Offer;
                float peak=0;SmbProfile profile=new();long started=Environment.TickCount64;
                while(Environment.TickCount64-started<3200)
                {
                    var frame=emulator.CaptureFrame();if(frame is not null){live.NativeScreenPixels=frame.NativeScreenPixels;live.SetScene(profile.Build(frame,emulator.IsSmbWorld,ProjectionProfile.CreateDefault(),emulator.BuiltInGameProfile));}
                    using Bitmap image=new(live.Width,live.Height);live.DrawToBitmap(image,live.ClientRectangle);
                    // Capture only the renderer's stage bitmap, never the window chrome.
                    peak=Math.Max(peak,meter.Sample(true));Application.DoEvents();Thread.Sleep(35);
                }
                MesenApi.WaveStop();recorder.FinishAsync().GetAwaiter().GetResult();
                live.CleanFrameRendered-=Offer;
                Require(peak>.001f,"Native game audio drives its own session meter");
                Require(new FileInfo(Path.Combine(output,"native-gameplay.avi")).Length>4096,"Native gameplay recording");
                results["NativeAudioMeterPeak"]=peak;results["NativeAudioMeterStatus"]=meter.Status;results["NativeRecording"]=true;window.Close();
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{InterfaceMotion.Enabled=motion;Environment.SetEnvironmentVariable("WARP4D_HOME",home);}
        Bitmap Capture(Control control,string name){Bitmap image=new(control.Width,control.Height);control.DrawToBitmap(image,control.ClientRectangle);image.Save(Path.Combine(output,name+".png"),ImageFormat.Png);return image;}
    }
    private static void WriteWave(string path)
    {
        using BinaryWriter writer=new(File.Create(path));int count=44100;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(44100);writer.Write(88200);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
        for(int i=0;i<count;i++)writer.Write((short)(Math.Sin(i*Math.Tau*440/44100)*6000));
    }
    private static bool Same(Bitmap a,Bitmap b)=>ImagePixels.Read(a).Pixels.SequenceEqual(ImagePixels.Read(b).Pixels);
    private static void Pump(int milliseconds){long stop=Environment.TickCount64+milliseconds;while(Environment.TickCount64<stop){Application.DoEvents();Thread.Sleep(10);}}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
