using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Text;

namespace Warp4D.Rendering;

// A bounded background MJPEG encoder and standard RIFF AVI muxer. Requires no
// external codec executable. Native game PCM is attached when recording stops.
internal sealed class CleanVideoRecorder : IDisposable
{
    private readonly BlockingCollection<(Bitmap Image,int Frame)> _queue=new(3);
    private readonly Task _worker;
    private readonly System.Diagnostics.Stopwatch _clock=System.Diagnostics.Stopwatch.StartNew();
    private readonly Func<double> _elapsedSeconds;
    private readonly string _temporary;
    private readonly string _destination;
    private readonly List<(string Tag,uint Offset,uint Size)> _index=[];
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private long _moviType;
    private int _frames,_lastQueued=-1,_finalFrame;
    private uint _largest;
    private Exception? _fault;
    private bool _finished;
    public const int Width=512,Height=480,Fps=30;
    public string WavePath {get;}
    public bool WantsFrame=>!_finished&&_fault is null&&(int)(_elapsedSeconds()*Fps)>_lastQueued;
    public bool AtLimit=>!_finished&&(_clock.Elapsed.TotalSeconds>=1200||_stream.Position>800_000_000||_fault is not null);
    public string Status=>_fault is null?$"REC · {_clock.Elapsed:mm\\:ss} · {_frames} frames":"Recording error: "+_fault.Message;
    public CleanVideoRecorder(string destination) : this(destination,null) { }
    // Deterministic capture scheduling tests; normal recordings use Stopwatch.
    internal CleanVideoRecorder(string destination,Func<double>? elapsedSeconds)
    {
        _elapsedSeconds=elapsedSeconds??(()=>_clock.Elapsed.TotalSeconds);
        _destination=Path.GetFullPath(destination);
        _temporary=_destination+"."+Guid.NewGuid().ToString("N")+".partial";WavePath=_temporary+".wav";
        _stream=new(_temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);
        _writer=new(_stream,Encoding.ASCII,true);
        Four("RIFF");_writer.Write(0u);Four("AVI ");WriteHeader(null);
        _stream.Position=4116;Four("LIST");_writer.Write(0u);_moviType=_stream.Position;Four("movi");
        _worker=Task.Run(Encode);
    }
    public void Offer(Bitmap stage)
    {
        if(!WantsFrame)return;
        int frame=(int)(_elapsedSeconds()*Fps);
        Bitmap image=new(Width,Height,PixelFormat.Format24bppRgb);
        using(var g=Graphics.FromImage(image)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.DrawImage(stage,new Rectangle(0,0,Width,Height));}
        if(!_queue.TryAdd((image,frame))){image.Dispose();return;}_lastQueued=frame;
    }
    private void Encode()
    {
        byte[]? previous=null;
        try
        {
            var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid);
            using EncoderParameters parameters=new(1);parameters.Param[0]=new(System.Drawing.Imaging.Encoder.Quality,90L);
            foreach(var item in _queue.GetConsumingEnumerable())
            {
                using(item.Image)
                {
                    using MemoryStream jpeg=new();item.Image.Save(jpeg,codec,parameters);byte[] bytes=jpeg.ToArray();
                    while(_frames<item.Frame)WriteFrame(previous??bytes);
                    WriteFrame(bytes);previous=bytes;
                }
            }
            while(previous is not null&&_frames<_finalFrame)WriteFrame(previous);
        }
        catch(Exception e) when(e is IOException or InvalidOperationException or System.Runtime.InteropServices.ExternalException){_fault=e;}
        finally{while(_queue.TryTake(out var item))item.Image.Dispose();}
    }
    private void WriteFrame(byte[] bytes)
    {
        if(_stream.Position>1_000_000_000)throw new IOException("AVI reached its 1 GB safety limit. Stop recording to keep the partial capture.");
        Chunk("00dc",bytes);_frames++;_largest=Math.Max(_largest,(uint)bytes.Length);
    }
    private void Chunk(string tag,byte[] data)
    {
        long start=_stream.Position;Four(tag);_writer.Write((uint)data.Length);_writer.Write(data);if((data.Length&1)!=0)_writer.Write((byte)0);
        _index.Add((tag,checked((uint)(start-_moviType)),(uint)data.Length));
    }
    public async Task FinishAsync()
    {
        if(_finished)return;_finished=true;_finalFrame=(int)Math.Ceiling(_elapsedSeconds()*Fps);_clock.Stop();_queue.CompleteAdding();await _worker.ConfigureAwait(false);
        if(_fault is not null){Close();throw new IOException("Capture could not finish. Partial file retained at "+_temporary,_fault);}
        if(_frames==0){Close();throw new IOException("No video frames were captured. Existing output was not replaced; partial capture retained at "+_temporary);}
        try
        {
            WaveInfo? wave=File.Exists(WavePath)?ReadWave(WavePath):null;
            if(wave is not null)
            {
                using FileStream audio=File.OpenRead(WavePath);audio.Position=wave.Offset;
                int targetBytes=checked((int)((long)_frames*wave.Rate*wave.Align/Fps));
                int available=(int)Math.Min(wave.Size,targetBytes),written=0;
                while(written<targetBytes)
                {
                    int length=Math.Min(65536/wave.Align*wave.Align,targetBytes-written);byte[] buffer=new byte[length];
                    int read=Math.Min(length,available-written);
                    if(read>0)audio.ReadExactly(buffer.AsSpan(0,read));Chunk("01wb",buffer);written+=length;
                }
                wave=wave with{Size=targetBytes};
            }
            long endMovi=_stream.Position;_stream.Position=_moviType-4;_writer.Write(checked((uint)(endMovi-_moviType)));_stream.Position=endMovi;
            Four("idx1");_writer.Write(_index.Count*16);
            foreach(var item in _index){Four(item.Tag);_writer.Write(item.Tag=="00dc"?0x10u:0u);_writer.Write(item.Offset);_writer.Write(item.Size);}
            long end=_stream.Position;_stream.Position=4;_writer.Write(checked((uint)(end-8)));_stream.Position=12;WriteHeader(wave);_stream.Position=end;_writer.Flush();Close();
            File.Move(_temporary,_destination,true);
            // This is the recorder's own temporary PCM spool, now embedded in the AVI.
            if(File.Exists(WavePath))File.Delete(WavePath);
        }
        catch{Close();throw;}
    }
    private void WriteHeader(WaveInfo? audio)
    {
        Four("LIST");_writer.Write(4096u);Four("hdrl");Four("avih");_writer.Write(56);
        _writer.Write(1_000_000/Fps);_writer.Write(0);_writer.Write(0);_writer.Write(0x10);_writer.Write(_frames);_writer.Write(0);
        _writer.Write(audio is null?1:2);_writer.Write(_largest);_writer.Write(Width);_writer.Write(Height);_writer.Write(new byte[16]);
        long list=_stream.Position;Four("LIST");_writer.Write(0);Four("strl");Four("strh");_writer.Write(56);Four("vids");Four("MJPG");
        StreamHeader(1,Fps,_frames,_largest,0,true);
        Four("strf");_writer.Write(40);_writer.Write(40);_writer.Write(Width);_writer.Write(Height);_writer.Write((short)1);_writer.Write((short)24);Four("MJPG");
        _writer.Write(Width*Height*3);_writer.Write(new byte[16]);FinishList(list);
        if(audio is not null)
        {
            list=_stream.Position;Four("LIST");_writer.Write(0);Four("strl");Four("strh");_writer.Write(56);Four("auds");_writer.Write(0);
            StreamHeader(audio.Align,audio.Rate*audio.Align,audio.Size/audio.Align,65536,audio.Align,false);
            Four("strf");_writer.Write(16);_writer.Write((short)1);_writer.Write(audio.Channels);_writer.Write(audio.Rate);_writer.Write(audio.Rate*audio.Align);_writer.Write(audio.Align);_writer.Write(audio.Bits);FinishList(list);
        }
        int padding=checked((int)(4116-_stream.Position-8));Four("JUNK");_writer.Write(padding);_writer.Write(new byte[padding]);
    }
    private void StreamHeader(int scale,int rate,int length,uint buffer,int sample,bool video)
    {
        _writer.Write(0);_writer.Write((short)0);_writer.Write((short)0);_writer.Write(0);_writer.Write(scale);_writer.Write(rate);_writer.Write(0);_writer.Write(length);
        _writer.Write(buffer);_writer.Write(uint.MaxValue);_writer.Write(sample);_writer.Write((short)0);_writer.Write((short)0);_writer.Write((short)(video?Width:0));_writer.Write((short)(video?Height:0));
    }
    private void FinishList(long start){long end=_stream.Position;_stream.Position=start+4;_writer.Write((int)(end-start-8));_stream.Position=end;}
    private void Four(string name)=>_writer.Write(Encoding.ASCII.GetBytes(name));
    private sealed record WaveInfo(short Channels,int Rate,short Align,short Bits,long Offset,int Size);
    private static WaveInfo? ReadWave(string path)
    {
        using var stream=File.OpenRead(path);using BinaryReader reader=new(stream);
        if(Encoding.ASCII.GetString(reader.ReadBytes(4))!="RIFF")return null;reader.ReadUInt32();if(Encoding.ASCII.GetString(reader.ReadBytes(4))!="WAVE")return null;
        short format=0,channels=0,align=0,bits=0;int rate=0;
        while(stream.Position+8<=stream.Length)
        {
            string tag=Encoding.ASCII.GetString(reader.ReadBytes(4));uint size=reader.ReadUInt32();long start=stream.Position;
            if(tag=="fmt "&&size>=16){format=reader.ReadInt16();channels=reader.ReadInt16();rate=reader.ReadInt32();reader.ReadInt32();align=reader.ReadInt16();bits=reader.ReadInt16();}
            if(tag=="data"&&format==1&&align>0&&rate>0&&bits==16)return new(channels,rate,align,bits,start,(int)Math.Min(size,stream.Length-start));
            stream.Position=Math.Min(stream.Length,start+size+(size&1));
        }
        return null;
    }
    private void Close(){_writer.Dispose();_stream.Dispose();}
    public void Dispose(){if(!_finished)FinishAsync().GetAwaiter().GetResult();_queue.Dispose();}
}
