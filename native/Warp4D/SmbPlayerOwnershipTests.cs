using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class SmbPlayerOwnershipTests
{
    internal static int Run(string worldRom,string europeRom,string output)
    {
        Directory.CreateDirectory(output);List<object> results=[];List<object> contracts=[];int failures=0,controls=0;
        try
        {
            foreach(var game in new[]{(Path:worldRom,Name:"world"),(Path:europeRom,Name:"europe")})
            {
                byte[] rom=File.ReadAllBytes(game.Path);int header=16+((rom[6]&4)!=0?512:0);
                byte[] offsets=[0x04,0x30,0x48,0x60,0x78,0x90,0xa8,0xc0,0xd8,0xe8,0x24,0xf8,0xfc,0x28,0x2c];
                int tableOffset=rom.AsSpan(header,32768).IndexOf(offsets);
                Require(tableOffset>=0,"Independent assembly default offsets must exist in verified PRG.");
                contracts.Add(new{Game=game.Name,PrgOffset=tableOffset,DefaultPlayerOffset=4,OwnedSlots="1..8"});
                using NesEmulator emulator=new();emulator.Load(game.Path);
                Require(emulator.IsSmbWorld,"Supported exact SMB identity required.");
                Thread.Sleep(1000);emulator.SetButton(NesButton.Start,true);Thread.Sleep(120);emulator.SetInputMask(0);Thread.Sleep(2500);
                emulator.SetButton(NesButton.Right,true);emulator.SetButton(NesButton.B,true);
                bool testedControls=false;
                for(int sample=0;sample<16;sample++)
                {
                    emulator.SetButton(NesButton.A,sample%3!=2);Thread.Sleep(250);
                    var frame=emulator.CaptureFrame()??throw new InvalidOperationException("Missing frame.");
                    Require(frame.CaptureScanline==96&&frame.NativeScreenSequence==frame.Sequence,"Paired native frame required.");
                    Require(frame.Ram[0x6e4]==4,"Player OAM offset remains fixed through shuffling.");
                    if(frame.PpuMask is not byte mask||(mask&16)==0)continue;
                    using var expected=OwnedImage(frame,out Rectangle bounds);
                    using var scene=new SmbProfile().Build(frame,true);
                    var players=scene.Objects.Where(o=>o.Kind==SceneObjectKind.Player).ToArray();
                    bool passed=expected is null?players.Length==0:players.Length==1&&players[0].Bounds==bounds&&Equal(players[0].Image,expected);
                    int nativeOpaque=0,nativeMatched=0,nativeOccluded=0;
                    using var background=SmbProfile.ComposeBackground(frame,true);
                    if(expected is not null)
                    for(int y=0;y<expected.Height;y++)for(int x=0;x<expected.Width;x++)
                    {
                        Color pixel=expected.GetPixel(x,y);int sx=x+bounds.X,sy=y+bounds.Y;
                        if(pixel.A==0||sx<8||sx>=256||sy<0||sy>=240)continue;
                        nativeOpaque++;int native=frame.NativeScreenPixels![sy*256+sx]&0xffffff;
                        if((pixel.ToArgb()&0xffffff)==native)nativeMatched++;
                        else if((background.GetPixel(sx,sy).ToArgb()&0xffffff)==native)nativeOccluded++;
                    }
                    // RGB diagnostics are reported separately: a body may be
                    // hidden by native background priority or scanline limits.
                    passed &= nativeMatched+nativeOccluded==nativeOpaque;
                    if(!passed)failures++;
                    string key=$"{game.Name}-{sample:00}";
                    using var reference=new Bitmap(256,240);
                    for(int y=0;y<240;y++)for(int x=0;x<256;x++)reference.SetPixel(x,y,Color.FromArgb(frame.NativeScreenPixels![y*256+x]|unchecked((int)0xff000000)));
                    reference.Save(Path.Combine(output,key+"-native.png"),ImageFormat.Png);
                    if(expected is not null)expected.Save(Path.Combine(output,key+"-owned.png"),ImageFormat.Png);
                    results.Add(new{Game=game.Name,Sample=sample,frame.Sequence,frame.ScrollX,ExpectedBounds=bounds,PlayerCount=players.Length,Actual=players.Select(o=>new{o.Label,o.Bounds}).ToArray(),NativeOpaque=nativeOpaque,NativeMatched=nativeMatched,NativeBackgroundOccluded=nativeOccluded,Passed=passed});
                    if(!testedControls&&expected is not null&&bounds.Left>8&&bounds.Right<230&&bounds.Top>32&&bounds.Bottom<220)
                    {
                        controls+=CheckControls(frame,bounds,out int controlFailures);failures+=controlFailures;testedControls=true;
                    }
                }
                Require(testedControls,"Live body fixture required for independent ownership controls.");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Frames=results.Count,Failures=failures,Controls=controls,NoRamWrites=true,Contracts=contracts,Cases=results},new JsonSerializerOptions{WriteIndented=true}));
            return failures==0?0:1;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());return 1;}
    }
    private static int CheckControls(NesFrame frame,Rectangle body,out int failures)
    {
        failures=0;
        // Clone a real visible body tile into a touching non-owned OAM slot.
        // It must not expand Mario or create a second Mario, even same palette.
        int source=Enumerable.Range(1,8).First(i=>IsVisibleOwnedTile(frame,i));
        byte[] adjacent=(byte[])frame.Oam.Clone();Array.Copy(adjacent,source*4,adjacent,12*4,4);
        adjacent[12*4]=(byte)(body.Top-1);adjacent[12*4+3]=(byte)(body.Right+1);
        using(var scene=new SmbProfile().Build(frame with{Oam=adjacent},true))
        {var actors=scene.Objects.Where(o=>o.Kind==SceneObjectKind.Player).ToArray();if(actors.Length!=1||actors[0].Bounds!=body)failures++;}
        // Hidden/blinking body plus nearby enemy must never guess a player.
        byte[] hidden=(byte[])adjacent.Clone();for(int i=1;i<=8;i++)hidden[i*4]=255;
        using(var scene=new SmbProfile().Build(frame with{Oam=hidden},true))
        {if(scene.Objects.Any(o=>o.Kind==SceneObjectKind.Player))failures++;}
        using(var scene=new SmbProfile().Build(frame,false))
        {if(scene.Objects.Any(o=>o.Kind==SceneObjectKind.Player))failures++;}
        return 3;
    }
    private static bool IsVisibleOwnedTile(NesFrame frame,int index)
    {
        int offset=index*4;if(frame.Oam[offset]>=239)return false;
        using var tile=SmbProfile.DecodeSpriteTile(frame.Chr,frame.Palette,frame.Oam[offset+1],frame.Oam[offset+2],frame.SpritePatternBase);
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)if(tile.GetPixel(x,y).A!=0)return true;
        return false;
    }
    internal static Bitmap? OwnedImage(NesFrame frame,out Rectangle bounds)
    {
        List<(int Index,Rectangle Bounds,Bitmap Image)> tiles=[];bounds=Rectangle.Empty;
        try
        {
            // Independent assembly contract: DefaultSprOffsets[0]=$04;
            // DrawPlayerLoop writes four two-tile rows, slots1..8. Shuffler
            // skips offsets below$28, so this player range never rotates.
            for(int i=1;i<=8;i++)
            {
                int offset=i*4;if(frame.Oam[offset]>=239)continue;
                var image=SmbProfile.DecodeSpriteTile(frame.Chr,frame.Palette,frame.Oam[offset+1],frame.Oam[offset+2],frame.SpritePatternBase);
                bool visible=false;for(int y=0;y<8;y++)for(int x=0;x<8;x++)visible|=image.GetPixel(x,y).A!=0;
                if(!visible){image.Dispose();continue;}
                Rectangle tile=new(frame.Oam[offset+3],frame.Oam[offset]+1,8,8);
                bounds=bounds.IsEmpty?tile:Rectangle.Union(bounds,tile);tiles.Add((i,tile,image));
            }
            if(tiles.Count==0)return null;
            Bitmap result=new(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb);
            using var graphics=Graphics.FromImage(result);graphics.Clear(Color.Transparent);
            foreach(var tile in tiles.OrderByDescending(t=>t.Index))graphics.DrawImageUnscaled(tile.Image,tile.Bounds.X-bounds.X,tile.Bounds.Y-bounds.Y);
            return result;
        }
        finally{foreach(var tile in tiles)tile.Image.Dispose();}
    }
    internal static bool Equal(Bitmap a,Bitmap b)
    {if(a.Size!=b.Size)return false;for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))return false;return true;}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
