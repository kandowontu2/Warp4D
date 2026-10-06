using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D;

internal static class SmbTerrainCatalogTests
{
    // Independent classification inventory from the unmodified game's 101
    // metatile definitions. Blank/black backdrop and solid fluid stay flat.
    private static readonly string[][] Expected =
    [
        ["-","-","Bush","Bush","Bush","Hill","Hill","Hill","Hill","Hill","Hill","Sprite","Sprite","Tree","Tree","Tree","Pipe","Pipe","Pipe","Pipe","Pipe","Pipe","Tree","Tree","Tree","Tree","Tree","Tree","Pipe","Pipe","Pipe","Pipe","Pipe","Pipe","Bush","-","Flagpole","Flagpole","-"],
        ["Sprite","Sprite","Sprite","Sprite","-","Castle","Castle","Castle","Castle","Castle","Castle","Castle","Tree","Tree","Tree","Tree","Tree","Brick","Brick","Brick","Terrain","Brick","Brick","Brick","Brick","Brick","Brick","Brick","Brick","Brick","Brick","-","-","Brick","Brick","Terrain","Pipe","Pipe","Pipe","-","Brick","Brick","Brick","Pipe","Pipe","Flagpole"],
        ["Cloud","Cloud","Cloud","Cloud","Cloud","Cloud","Terrain","-","Terrain","Terrain"],
        ["QuestionBlock","QuestionBlock","Item","Item","QuestionBlock","Item"]
    ];

    internal static int Run(string worldRom,string europeRom,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            byte[] world=File.ReadAllBytes(worldRom);
            Require(SmbCartridgeIdentity.IsSupported(world),"Exact supported world payload required.");
            int offset=16+((world[6]&4)!=0?512:0);
            // Local listing code offsets differ; validate the complete data,
            // not its relocated address. The100-row local listing omits the
            // residual flag-ball entry used by its FlagBalls_Residual routine.
            byte[] prefix=[0x24,0x24,0x24,0x24,0x27,0x27,0x27,0x27,0x24,0x24,0x24,0x35,0x36,0x25,0x37,0x25];
            int first=world.AsSpan(offset,32768).IndexOf(prefix);
            Require(first>=0 && first+404<=32768,"Catalog prefix found in verified PRG.");
            byte[] catalog=world.AsSpan(offset+first,404).ToArray();
            Require(catalog.AsSpan(336,4).SequenceEqual(new byte[]{0x24,0x2f,0x24,0x3d}),"Residual flag-ball definition confirmed.");
            byte[] listing=catalog[..336].Concat(catalog[340..]).ToArray();
            Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(listing))=="52E987FBED0A6FA44E8DD7E0202C6AE32E8F03548581A170882861FF63FCD5D4","All100 other metatile rows exactly match independent assembly data.");
            List<object> cases=[];int failures=0,roundtrips=0,controls=0;
            foreach(var game in new[]{(Path:worldRom,Name:"world"),(Path:europeRom,Name:"europe")})
            {
                byte[] rom=File.ReadAllBytes(game.Path);Require(SmbCartridgeIdentity.IsSupported(rom),"Exact supported payload.");
                int header=16+((rom[6]&4)!=0?512:0);
                int catalogOffset=rom.AsSpan(header,32768).IndexOf(catalog);
                Require(catalogOffset>=0,"All404 catalog bytes independently found in this cartridge.");
                byte[] chr=rom.AsSpan(header+32768,8192).ToArray();int row=0;
                List<(byte Palette,byte[] Tiles)> additions=[];
                for(byte palette=0;palette<4;palette++)
                for(int index=0;index<Expected[palette].Length;index++,row++)
                {
                    byte[] tiles=catalog.AsSpan(row*4,4).ToArray();
                    bool castle=palette==1 && index is >=5 and <=11;
                    var frame=CreateFrame(chr,palette,tiles,castle);
                    using var scene=new SmbProfile().Build(frame,true);
                    var target=scene.Objects.Where(o=>o.Bounds.Contains(40,72)).ToArray();
                    string expected=Expected[palette][index];
                    bool passed=expected=="-"?target.Length==0:target.Any(o=>o.Kind.ToString()==expected && o.ProjectionEnabled);
                    if(!passed)failures++;
                    Require(RecomposesExactly(frame,scene),"Flat object/background reassembly must preserve all61440 source pixels.");roundtrips++;
                    Require(scene.Objects.All(o=>o.Bounds.Top>=32),"HUD must never become scenery.");
                    Require(scene.Objects.Where(o=>o.Kind is SceneObjectKind.Terrain or SceneObjectKind.Brick or SceneObjectKind.QuestionBlock)
                        .All(o=>o.Bounds.Width<=16 && o.Bounds.Height<=16),"Solid terrain and blocks remain independent metatiles.");
                    foreach(var guard in new[]{(Mode:0,Task:0,Timer:0),(Mode:0,Task:1,Timer:0),(Mode:0,Task:2,Timer:0),(Mode:0,Task:3,Timer:1)})
                    {
                        byte[] ram=(byte[])frame.Ram.Clone();ram[0x770]=(byte)guard.Mode;ram[0x772]=(byte)guard.Task;ram[0x7a2]=(byte)guard.Timer;
                        using var menu=new SmbProfile().Build(frame with{Ram=ram},true);
                        Require(menu.Objects.Count==0,"Title/loading/timed-demo gates stay flat.");controls++;
                    }
                    using(var disabled=new SmbProfile().Build(frame with{PpuMask=0x16},true))
                    {Require(disabled.Objects.Count==0,"Disabled background must not project metadata.");controls++;}
                    using(var generic=new SmbProfile().Build(frame,false))
                    {Require(generic.Objects.Count==0,"SMB recognition never leaks into generic games.");controls++;}
                    byte[] demoRam=(byte[])frame.Ram.Clone();demoRam[0x770]=0;demoRam[0x772]=3;demoRam[0x7a2]=0;
                    using(var demo=new SmbProfile().Build(frame with{Ram=demoRam},true))
                    {Require(Describe(scene)==Describe(demo),"Attract-demo scenery equals gameplay recognition.");controls++;}
                    if(IsSupplemental(palette,index))
                    {
                        additions.Add((palette,tiles));
                        foreach(byte other in Enumerable.Range(0,4).Select(p=>(byte)p).Where(p=>p!=palette))
                        {
                            int otherStart=Expected.Take(other).Sum(p=>p.Length)*4;
                            bool declared=Enumerable.Range(0,Expected[other].Length).Any(i=>catalog.AsSpan(otherStart+i*4,4).SequenceEqual(tiles));
                            if(declared)continue; // legitimate palette aliases, e.g. rope/flagpole
                            using var wrongPalette=new SmbProfile().Build(CreateFrame(chr,other,tiles,false),true);
                            Require(wrongPalette.Objects.Count==0,"Undeclared palette aliases remain flat.");controls++;
                        }
                        var projection=ProjectionProfile.CreateDefault();projection.Objects[expected].Enabled=false;
                        using(var disabledClass=new SmbProfile().Build(frame,true,projection))
                        {Require(disabledClass.Objects.Any(o=>o.Bounds.Contains(40,72)&&!o.ProjectionEnabled),"Existing user class switches apply to added scenery.");controls++;}
                        var custom=new GameRecognitionProfile();
                        custom.BackgroundRules[MetatileSignature.Read(frame,4,8).Key]=new(){Kind="Enemy",Label="User override"};
                        using(var overridden=new SmbProfile().Build(frame,true,null,custom))
                        {Require(overridden.Objects.Any(o=>o.Bounds.Contains(40,72)&&o.Kind==SceneObjectKind.Enemy&&o.Label=="User override"),"User-authored rules take precedence over supplemental built-ins.");controls++;}
                    }
                    cases.Add(new{Game=game.Name,CatalogOffset=catalogOffset,Palette=palette,Index=index,
                        Tiles=Convert.ToHexString(tiles),Expected=expected,Actual=target.Select(o=>new{Kind=o.Kind.ToString(),o.Label,o.Bounds}).ToArray(),Passed=passed});
                }
                controls+=CheckGroups(chr);
                var gallery=CreateFrame(chr,0,[0x24,0x24,0x24,0x24],false);
                int cell=0;HashSet<string> drawn=[];
                foreach(var addition in additions)
                {
                    if(!drawn.Add(addition.Palette+":"+Convert.ToHexString(addition.Tiles)))continue;
                    Place(gallery,2+cell%7*4,6+cell/7*4,addition.Palette,addition.Tiles);cell++;
                }
                using var galleryScene=new SmbProfile().Build(gallery,true);
                Require(galleryScene.Objects.Count==28 && RecomposesExactly(gallery,galleryScene),"All28 new unique shapes survive gallery grouping and exact reassembly.");
                using var renderer=new WarpRendererControl{Size=new(900,780),UseGpu=true,EnableStyleTransitions=false,MotionSecondsForTest=.5};
                renderer.CreateControl();renderer.SetScene(galleryScene.Clone());
                using Bitmap image=new(900,780);renderer.DrawToBitmap(image,renderer.ClientRectangle);
                Require(renderer.RendererStatus.StartsWith("GPU"),"GPU gallery rendering required.");
                image.Save(Path.Combine(output,game.Name+"-added-scenery.png"));
            }
            Require(cases.Count==202,"Both complete101-definition inventories required.");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=failures==0,Definitions=cases.Count,FailedDefinitions=failures,
                ExactFlatReassemblies=roundtrips,Controls=controls,Cases=cases,
                Scope="Complete ROM metatile catalog, reconstructed CHR pixels under synthetic palettes/nametable placement. Classification, grouping, HUD and visibility controls; not natural traversal, native actor identity or direct GPU presentation."},new JsonSerializerOptions{WriteIndented=true}));
            return failures==0?0:1;
        }
        catch(Exception exception){File.WriteAllText(Path.Combine(output,"error.txt"),exception.ToString());return 1;}
    }

    private static NesFrame CreateFrame(byte[] chr,byte palette,byte[] quartet,bool castle)
    {
        byte[] colors=Enumerable.Range(0,32).Select(i=>new byte[]{0x22,0x18,0x2a,0x30}[i%4]).ToArray();
        int backdrop=NesPalette.Get(colors[0]).ToArgb();
        var frame=new NesFrame{Chr=chr,Palette=colors,Ram=new byte[2048],Oam=Enumerable.Repeat((byte)255,256).ToArray(),
            Tiles=Enumerable.Range(0,4).Select(_=>Enumerable.Repeat((byte)0x24,960).ToArray()).ToArray(),
            Attributes=Enumerable.Range(0,4).Select(_=>new byte[960]).ToArray(),
            NametablePixels=Enumerable.Range(0,4).Select(_=>Enumerable.Repeat(backdrop,61440).ToArray()).ToArray(),
            PpuMask=0x1e,ScrollSource="Synthetic ROM catalog placement"};
        frame.Ram[0x770]=1;frame.Ram[0x772]=3;
        Place(frame,4,8,palette,quartet);
        if(castle)Place(frame,6,8,1,[0x9d,0x47,0x9e,0x47]);
        return frame;
    }
    private static void Place(NesFrame frame,int tx,int ty,byte pal,byte[] tiles)
        {
            for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
            {
                int tile=tiles[dx*2+dy],slot=(ty+dy)*32+tx+dx;
                frame.Tiles[0][slot]=(byte)tile;frame.Attributes[0][slot]=(byte)(pal*0x55);
                for(int y=0;y<8;y++)for(int x=0;x<8;x++)
                {
                    int address=0x1000+tile*16+y,bit=7-x;
                    int color=((frame.Chr[address]>>bit)&1)|(((frame.Chr[address+8]>>bit)&1)<<1);
                    frame.NametablePixels[0][((ty+dy)*8+y)*256+(tx+dx)*8+x]=NesPalette.Get(frame.Palette[color==0?0:pal*4+color]).ToArgb();
                }
            }
        }
    private static bool IsSupplemental(byte palette,int index)=>palette switch
    {
        0=>index is 11 or 12 or 25 or 26 or 27 or 34,
        1=>index is 0 or 1 or 2 or 3 or 12 or 13 or 14 or 15 or 16 or 35 or 36 or 37 or 38 or 40 or 42 or 43 or 44 or 45,
        2=>index is 6 or 9,
        3=>index is 2 or 3 or 5,
        _=>false
    };
    private static int CheckGroups(byte[] chr)
    {
        var mushroom=CreateFrame(chr,0,[0x6b,0x70,0x2c,0x2d],false);
        Place(mushroom,6,8,0,[0x6c,0x71,0x6d,0x72]);Place(mushroom,8,8,0,[0x6e,0x73,0x6f,0x74]);
        Place(mushroom,6,10,1,[0x75,0xba,0x76,0xbb]);Place(mushroom,6,12,1,[0xba,0xba,0xbb,0xbb]);
        using(var scene=new SmbProfile().Build(mushroom,true))
        {Require(scene.Objects.Count==1&&scene.Objects[0].Label=="Mushroom platform"&&scene.Objects[0].Bounds==new Rectangle(32,64,48,48),"Mushroom cap and stem form one shape across palettes.");Require(RecomposesExactly(mushroom,scene),"Mushroom reassembly exact.");}
        var cannon=CreateFrame(chr,1,[0xc6,0xc8,0xc7,0xc9],false);
        Place(cannon,4,10,1,[0xca,0xcc,0xcb,0xcd]);Place(cannon,4,12,1,[0x2a,0x2a,0x40,0x40]);
        using(var scene=new SmbProfile().Build(cannon,true))
        {Require(scene.Objects.Count==1&&scene.Objects[0].Label=="Bullet Bill cannon"&&scene.Objects[0].Bounds==new Rectangle(32,64,16,48),"Cannon sections form one shape.");Require(RecomposesExactly(cannon,scene),"Cannon reassembly exact.");}
        var tree=CreateFrame(chr,0,[0xb8,0xba,0xb9,0xbb],false);Place(tree,4,10,1,[0xbe,0xbe,0xbf,0xbf]);
        using(var scene=new SmbProfile().Build(tree,true))
        {Require(scene.Objects.Count==1&&scene.Objects[0].Bounds==new Rectangle(32,64,16,32),"Existing tree canopy joins newly recognized trunk.");Require(RecomposesExactly(tree,scene),"Tree reassembly exact.");}
        var unknown=CreateFrame(chr,0,[0x10,0x11,0x12,0x13],false);
        using(var scene=new SmbProfile().Build(unknown,true))Require(scene.Objects.Count==0,"Unknown glyphs remain flat.");
        return 7;
    }
    private static bool RecomposesExactly(NesFrame frame,SmbScene scene)
    {
        using var original=SmbProfile.ComposeBackground(frame,true);
        using var restored=(Bitmap)scene.Background.Clone();
        foreach(var obj in scene.Objects)
            for(int y=0;y<obj.Image.Height;y++)for(int x=0;x<obj.Image.Width;x++)
            {var pixel=obj.Image.GetPixel(x,y);if(pixel.A!=0)restored.SetPixel(obj.Bounds.X+x,obj.Bounds.Y+y,pixel);}
        return ImagePixels.Read(original).Pixels.SequenceEqual(ImagePixels.Read(restored).Pixels);
    }
    private static string Describe(SmbScene scene)=>JsonSerializer.Serialize(scene.Objects.Select(o=>new{o.Kind,o.Label,o.Bounds,o.ProjectionEnabled}));
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
