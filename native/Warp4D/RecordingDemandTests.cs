using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class RecordingDemandTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            CheckColdCapture();
            double seconds=.001;
            string destination=Path.Combine(Path.GetFullPath(output),"capture.avi");
            using var recorder=new CleanVideoRecorder(destination,()=>seconds);
            using var scene=LookGalleryForm.CreateSample();
            using Form form=new(){ClientSize=new(720,600),StartPosition=FormStartPosition.Manual,Location=new(40,40),Text="Warp4D recording demand regression"};
            using var renderer=new WarpRendererControl{Dock=DockStyle.Fill,UseGpu=true,EnableStyleTransitions=false,MotionSecondsForTest=0};
            renderer.ApplySettings(new(){Animate=false,Geometry=new(){Mode=Warp4D.Profiles.GeometryMode.Duocylinder},Dimensions=new(){AdaptiveQuality=false}});
            renderer.SetScene(scene.Clone());form.Controls.Add(renderer);
            form.Show();DirectGpuStageTests.ShowOwnedWindowForTest(form);
            renderer.DirectGpuPresentation=false;renderer.Invalidate();renderer.Update();
            using var reference=renderer.CloneLastReadbackForTest();
            var referencePixels=ImagePixels.Read(reference).Pixels;
            renderer.DirectGpuPresentation=true;
            List<byte[]> expected=[];
            renderer.NeedsCleanFrame=()=>recorder.WantsFrame;
            renderer.CleanFrameRendered+=stage=>
            {
                if(!recorder.WantsFrame)return;
                Require(ImagePixels.Read(stage).Pixels.SequenceEqual(referencePixels),"Recording texture exactly matches legacy clean GPU pixels");
                using Bitmap resized=new(CleanVideoRecorder.Width,CleanVideoRecorder.Height,PixelFormat.Format24bppRgb);
                using(var g=Graphics.FromImage(resized))
                {
                    g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.DrawImage(stage,new Rectangle(0,0,resized.Width,resized.Height));
                }
                var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid);
                using EncoderParameters parameters=new(1);
                parameters.Param[0]=new(System.Drawing.Imaging.Encoder.Quality,90L);
                using MemoryStream bytes=new();resized.Save(bytes,codec,parameters);expected.Add(bytes.ToArray());
                recorder.Offer(stage);
            };
            long start=renderer.GpuReadbackFrames;
            for(int frame=0;frame<3;frame++)
            {
                seconds=(frame+.03)/CleanVideoRecorder.Fps;
                Require(recorder.WantsFrame,"Next recording frame due");
                renderer.Invalidate();renderer.Update();
                Require(renderer.DirectGpuStageShown&&renderer.GpuReadbackFrames==start+frame+1,"Due frame uses exactly one clean readback without hiding GPU stage");
                Require(!recorder.WantsFrame,"Accepted frame suppresses duplicate work");
                renderer.Invalidate();renderer.Update();
                Require(renderer.DirectGpuStageShown&&renderer.GpuReadbackFrames==start+frame+1,"Intervening paint stays GPU-local with no readback");
            }
            seconds=.1;recorder.FinishAsync().GetAwaiter().GetResult();
            Require(!recorder.WantsFrame,"Finished recorder requests no frames");
            long recordingReadbacks=renderer.GpuReadbackFrames-start;
            renderer.Invalidate();renderer.Update();
            Require(renderer.DirectGpuStageShown&&renderer.GpuReadbackFrames-start==recordingReadbacks,"Stopped recording leaves ordinary playback GPU-local");
            using Bitmap capture=new(renderer.Width,renderer.Height),legacy=new(renderer.Width,renderer.Height);
            renderer.DrawToBitmap(capture,renderer.ClientRectangle);
            Require(renderer.DirectGpuStageShown,"Screenshot does not hide native stage");
            renderer.DirectGpuPresentation=false;renderer.DrawToBitmap(legacy,renderer.ClientRectangle);
            Require(ImagePixels.Read(capture).Pixels.SequenceEqual(ImagePixels.Read(legacy).Pixels),"Screenshot equals legacy full control output");
            using var stream=File.OpenRead(destination);using BinaryReader reader=new(stream);
            Require(Encoding.ASCII.GetString(reader.ReadBytes(4))=="RIFF","RIFF output");
            stream.Position=4116;
            Require(Encoding.ASCII.GetString(reader.ReadBytes(4))=="LIST","Movie list");
            uint size=reader.ReadUInt32();long end=stream.Position+size;
            Require(Encoding.ASCII.GetString(reader.ReadBytes(4))=="movi","Movie chunks");
            int count=0;
            while(stream.Position<end)
            {
                Require(Encoding.ASCII.GetString(reader.ReadBytes(4))=="00dc","MJPEG frame");
                int length=reader.ReadInt32();byte[] jpeg=reader.ReadBytes(length);
                Require(count<expected.Count&&jpeg.SequenceEqual(expected[count]),"Recorded JPEG identical to unmodified clean stage encoding");
                if((length&1)!=0)reader.ReadByte();count++;
            }
            Require(count==3&&expected.Count==3,"Exactly three scheduled frames, no missing/duplicate frames");
            form.Close();
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,ColdDirectCaptureExact=true,PlaybackPaints=6,RecordingFrames=count,CleanReadbacks=recordingReadbacks,InterveningGpuPaints=3,GpuStageContinuouslyShown=true,ExactLegacySourcePixels=true,ExactEncodedFrames=true,StoppedRecordingGpuLocal=true,ExactFullControlScreenshot=true,Scope="Deterministic 30-fps recording demand, GPU/readback routing and exact AVI JPEG bytes. Not sustained playback FPS or physical display proof."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static void CheckColdCapture()
    {
        using var scene=LookGalleryForm.CreateSample();
        using Form form=new(){ClientSize=new(720,600),StartPosition=FormStartPosition.Manual,Location=new(40,40),Text="Warp4D cold capture regression"};
        using var renderer=new WarpRendererControl{Dock=DockStyle.Fill,UseGpu=true,EnableStyleTransitions=false,MotionSecondsForTest=0};
        renderer.ApplySettings(new(){Animate=false,Geometry=new(){Mode=Warp4D.Profiles.GeometryMode.Duocylinder},Dimensions=new(){AdaptiveQuality=false}});
        renderer.SetScene(scene.Clone());form.Controls.Add(renderer);
        int[]? captured=null;
        renderer.NeedsCleanFrame=()=>true;
        renderer.CleanFrameRendered+=image=>captured=ImagePixels.Read(image).Pixels;
        form.Show();DirectGpuStageTests.ShowOwnedWindowForTest(form);renderer.Invalidate();renderer.Update();
        Require(renderer.DirectGpuStageShown&&renderer.GpuReadbackFrames>0&&captured is not null,"First direct capture allocates readback storage without legacy warmup");
        int[] first=captured!;
        renderer.DirectGpuPresentation=false;renderer.Invalidate();renderer.Update();
        using var reference=renderer.CloneLastReadbackForTest();
        Require(first.SequenceEqual(ImagePixels.Read(reference).Pixels),"Cold direct capture equals subsequent legacy render");form.Close();
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
