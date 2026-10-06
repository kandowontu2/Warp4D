using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using Warp4D.Rendering;

namespace Warp4D;

internal static class RecordingMuxTests
{
    private const int Rate=48000,Align=4,Frames=180;
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            List<object> cases=[];
            foreach(int audioSeconds in new[]{0,1,8})
            {
                double seconds=0;
                string name=audioSeconds==0?"silent":audioSeconds==1?"short-pcm":"long-pcm";
                string path=Path.Combine(Path.GetFullPath(output),name+".avi");
                using var recorder=new CleanVideoRecorder(path,()=>seconds);
                byte[] pcm=new byte[audioSeconds*Rate*Align];
                for(int i=0;i<pcm.Length;i++)pcm[i]=(byte)((i*17+i/101)&255);
                if(audioSeconds>0)WriteWave(recorder.WavePath,pcm);
                List<byte[]> images=[];
                foreach(Color color in new[]{Color.OrangeRed,Color.LimeGreen,Color.DodgerBlue})
                {
                    using Bitmap image=new(CleanVideoRecorder.Width,CleanVideoRecorder.Height,PixelFormat.Format24bppRgb);
                    using(var g=Graphics.FromImage(image)){g.Clear(color);g.FillRectangle(Brushes.White,7,11,31,43);}
                    images.Add(Encode(image));
                    seconds=(images.Count-1)*2+.001;
                    Require(recorder.WantsFrame,"Sparse scheduled frame due");recorder.Offer(image);
                    Require(!recorder.WantsFrame,"Accepted sparse frame suppresses duplicate");
                }
                seconds=6;recorder.FinishAsync().GetAwaiter().GetResult();
                Require(!File.Exists(recorder.WavePath),"Owned PCM spool removed only after successful mux");
                int video=0,audio=0;List<(string Tag,uint Offset,uint Size)> chunks=[];
                using var stream=File.OpenRead(path);using BinaryReader reader=new(stream);
                Require(Tag(reader)=="RIFF"&&reader.ReadUInt32()==stream.Length-8&&Tag(reader)=="AVI ","Final RIFF size and AVI signature");
                Require(Tag(reader)=="LIST"&&reader.ReadUInt32()==4096&&Tag(reader)=="hdrl","Reserved header list");
                Require(Tag(reader)=="avih"&&reader.ReadUInt32()==56,"AVI main header");
                stream.Position=48;Require(reader.ReadInt32()==Frames,"AVI total video frames");
                stream.Position=56;Require(reader.ReadInt32()==(audioSeconds>0?2:1),"AVI stream count");
                stream.Position=64;Require(reader.ReadInt32()==512&&reader.ReadInt32()==480,"AVI dimensions");
                stream.Position=4116;Require(Tag(reader)=="LIST","Movie list");
                uint movieSize=reader.ReadUInt32();long movieType=stream.Position,end=movieType+movieSize;
                Require(Tag(reader)=="movi","Movie type");
                using MemoryStream actualPcm=new();
                while(stream.Position<end)
                {
                    long begin=stream.Position;string tag=Tag(reader);uint size=reader.ReadUInt32();
                    Require(size<=stream.Length-stream.Position,"Chunk length within file");
                    byte[] bytes=reader.ReadBytes(checked((int)size));
                    chunks.Add((tag,checked((uint)(begin-movieType)),size));
                    if(tag=="00dc")
                    {
                        Require(video<Frames&&bytes.SequenceEqual(images[video/60]),"Sparse frames hold exact previous JPEG until next pose");video++;
                    }
                    else if(tag=="01wb"){actualPcm.Write(bytes);audio++;}
                    else throw new InvalidDataException("Unexpected movie chunk "+tag);
                    if((size&1)!=0)Require(reader.ReadByte()==0,"Chunk padding");
                }
                Require(stream.Position==end&&video==Frames,"Movie exact end and six-second duration");
                byte[] embedded=actualPcm.ToArray();int expectedBytes=audioSeconds>0?6*Rate*Align:0;
                Require(embedded.Length==expectedBytes,"Audio duration exactly matches video");
                for(int i=0;i<embedded.Length;i++)Require(embedded[i]==(i<pcm.Length?pcm[i]:0),"PCM bytes preserved, excess trimmed or shortage padded with silence");
                Require(Tag(reader)=="idx1"&&reader.ReadUInt32()==chunks.Count*16,"Index length");
                foreach(var chunk in chunks)
                    Require(Tag(reader)==chunk.Tag&&reader.ReadUInt32()==(chunk.Tag=="00dc"?0x10u:0u)&&reader.ReadUInt32()==chunk.Offset&&reader.ReadUInt32()==chunk.Size,"Every AVI index entry matches actual chunk and keyframe flag");
                Require(stream.Position==stream.Length,"Index exact file end");
                cases.Add(new{Name=name,VideoFrames=video,ExactJpegFrames=video,AudioChunks=audio,ExactPcmBytes=embedded.Length,IndexEntries=chunks.Count});
            }
            string protectedPath=Path.Combine(Path.GetFullPath(output),"zero-frame-existing.avi");
            byte[] sentinel=Encoding.ASCII.GetBytes("Existing user output must survive a failed empty recording.");
            File.WriteAllBytes(protectedPath,sentinel);
            using(var empty=new CleanVideoRecorder(protectedPath,()=>0))
            {
                bool rejected=false;
                try{empty.FinishAsync().GetAwaiter().GetResult();}catch(IOException e){rejected=e.Message.StartsWith("No video frames were captured.");}
                Require(rejected&&File.ReadAllBytes(protectedPath).SequenceEqual(sentinel),"Empty capture preserves prior output");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Clock="Deterministic six-second sparse timeline, three accepted poses at 0/2/4 seconds",Cases=cases,EmptyRecordingPreservesExistingOutput=true,Scope="AVI mux/JPEG hold-frame/PCM duration and index verification with synthetic PCM. Not native game audio, sustained playback FPS or physical display."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static byte[] Encode(Bitmap image)
    {
        var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid);
        using EncoderParameters parameters=new(1);parameters.Param[0]=new(System.Drawing.Imaging.Encoder.Quality,90L);
        using MemoryStream bytes=new();image.Save(bytes,codec,parameters);return bytes.ToArray();
    }
    private static void WriteWave(string path,byte[] pcm)
    {
        using var stream=new FileStream(path,FileMode.CreateNew);using BinaryWriter writer=new(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+pcm.Length);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);writer.Write((short)1);writer.Write((short)2);writer.Write(Rate);writer.Write(Rate*Align);writer.Write((short)Align);writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(pcm.Length);writer.Write(pcm);
    }
    private static string Tag(BinaryReader reader)=>Encoding.ASCII.GetString(reader.ReadBytes(4));
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
