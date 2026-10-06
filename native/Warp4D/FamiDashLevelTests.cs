using System.Diagnostics;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class FamiDashLevelTests
{
    // Both verified debug symbol files place _level at $0481 and _gameState
    // at $049E. These are read-only identity assertions, never RAM writes.
    internal static int Run(string normalRom,string hugeRom,string output,bool wider=false,int[]? levels=null,bool nativeReference=false,bool requirePaired=false,int? sampleCount=null)
    {
        Directory.CreateDirectory(output);
        string? prior=Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.GetFullPath(Path.Combine(output,"isolated-data")));
        List<object> cases=[];
        try
        {
            Require(sampleCount is null or >=1 and <=12,"Bounded sample count must be 1..12.");
            foreach(var game in new[]{(Path:normalRom,Name:"famidash"),(Path:hugeRom,Name:"huge-man")})
            foreach(int target in levels ?? (wider ? new[]{3,6,10,15} : new[]{0,1,2,5}))
            {
                using NesEmulator emulator=new();emulator.Load(game.Path);
                var profile=emulator.BuiltInGameProfile??throw new InvalidOperationException("Unsupported build.");
                List<object> navigation=[];var boot=Stopwatch.StartNew();
                while(true)
                {
                    var frame=emulator.CaptureFrame();
                    if(frame is null){Require(boot.Elapsed.TotalSeconds<10,"First native publication timeout.");Thread.Sleep(10);continue;}
                    int state=frame.Ram[0x49e];
                    navigation.Add(new{Seconds=boot.Elapsed.TotalSeconds,State=state,Level=frame.Ram[0x481]});
                    if(state==6)break;
                    if(boot.Elapsed.TotalSeconds>65)throw new InvalidOperationException($"Level select timeout: {game.Name}, state={state}.");
                    if(state is 1 or 5)Press(NesButton.Start,120,600);else Thread.Sleep(250);
                }
                for(int step=0;emulator.CaptureFrame()!.Ram[0x481]!=target;step++)
                {
                    if(step>=80)throw new InvalidOperationException("Requested menu level not reached.");
                    Require(emulator.CaptureFrame()!.Ram[0x49e]==6,"Selection must remain in level menu.");
                    Press(NesButton.Right,100,220);
                }
                for(int step=0;emulator.CaptureFrame()!.Ram[0x49e]!=2;step++)
                {
                    if(step>=8)throw new InvalidOperationException("Requested gameplay did not start.");
                    Press(NesButton.Start,120,500);
                }
                if(wider)
                {
                    var loading=Stopwatch.StartNew();int stable=0;
                    while(stable<3)
                    {
                        var f=emulator.CaptureFrame()!;
                        stable=f.Ram[0x49e]==2 && f.Ram[0x481]==target && f.PpuMask is byte m && (m&0x18)==0x18 ? stable+1 : 0;
                        Require(loading.Elapsed.TotalSeconds<20,"Visible gameplay did not stabilize.");
                        Thread.Sleep(100);
                    }
                }
                List<object> samples=[];
                for(int sample=0;sample<(sampleCount??(wider?12:3));sample++)
                {
                    Thread.Sleep(wider?400:sample==0?80:250);
                    emulator.TogglePause();Thread.Sleep(50);
                    var frame=emulator.CaptureFrame()!;
                    Require(frame.Ram[0x49e]==2&&frame.Ram[0x481]==target,"Native selected gameplay identity.");
                    if(requirePaired)Require(frame.CaptureScanline==96 && frame.NativeScreenSequence==frame.Sequence && frame.NativeScreenPixels?.Length==61440,"Coherent FamiDash publication required.");
                    string stem=$"{game.Name}-level-{target:D2}-{sample}";
                    if(nativeReference)
                    {
                        var native=requirePaired ? frame.NativeScreenPixels! : emulator.ReadPausedScreenForTest();
                        Require(native.Length==61440,"Native reference dimensions.");
                        string reference=Path.Combine(output,"native-reference");Directory.CreateDirectory(reference);
                        using Bitmap original=new(256,240);
                        for(int y=0;y<240;y++)for(int x=0;x<256;x++)original.SetPixel(x,y,Color.FromArgb(native[y*256+x]));
                        original.Save(Path.Combine(reference,stem+".png"));
                    }
                    File.WriteAllText(Path.Combine(output,stem+".frame.json"),JsonSerializer.Serialize(frame));
                    using var scene=new SmbProfile().Build(frame,false,null,profile);
                    using var renderer=new WarpRendererControl{Size=new(900,780),UseGpu=true};
                    renderer.CreateControl();renderer.SetScene(scene.Clone());
                    using Bitmap bitmap=new(900,780);renderer.DrawToBitmap(bitmap,renderer.ClientRectangle);
                    bitmap.Save(Path.Combine(output,stem+".png"));
                    Require(renderer.RendererStatus.StartsWith("GPU"),"GPU render required.");
                    List<object> artworkMisses=[];int knownCells=0,matchedCells=0;
                    if(profile.IsActive(frame) && (frame.PpuMask is not byte pm || (pm&8)!=0))
                    {
                        for(int y=frame.ScrollY/16*2;y<(frame.ScrollY+240)/8;y+=2)
                        for(int x=frame.ScrollX/16*2;x<(frame.ScrollX+256)/8;x+=2)
                        {
                            var sig=MetatileSignature.Read(frame,x,y);
                            if(!profile.BackgroundRules.ContainsKey(sig.Key))continue;
                            var bounds=new Rectangle(x*8-frame.ScrollX,y*8-frame.ScrollY,16,16);
                            // Partial viewport cells aren't positive bank evidence.
                            if(!new Rectangle(0,0,256,240).Contains(bounds))continue;
                            knownCells++;
                            if(profile.Match(sig,frame,x,y)!=null)matchedCells++;
                            else artworkMisses.Add(new{sig.Key,X=x,Y=y,Fingerprint=MetatileVisualFingerprint.Read(frame,x,y)});
                        }
                    }
                    samples.Add(new{sample,frame.ScrollX,frame.ScrollY,frame.SpritePatternBase,frame.LargeSprites,frame.PpuMask,frame.Sequence,frame.NativeScreenSequence,frame.CaptureScanline,
                        NativeScroll=BitConverter.ToUInt32(frame.Ram,0x4a8),LevelDataBank=frame.Ram[0x482],
                        ChrSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame.Chr)),
                        Active=profile.IsActive(frame),KnownCells=knownCells,MatchedCells=matchedCells,ArtworkMisses=artworkMisses,
                        Backgrounds=scene.Objects.Count(o=>!o.IdentityKey.StartsWith("sprite:")),
                        Sprites=scene.Objects.Count(o=>o.IdentityKey.StartsWith("sprite:")),
                        Labels=scene.Objects.Select(o=>o.Label).Distinct().ToArray(),Frame=stem+".frame.json",Image=stem+".png"});
                    emulator.TogglePause();
                    emulator.SetButton(NesButton.A,sample%2==0);
                }
                emulator.SetInputMask(0);
                cases.Add(new{Game=game.Name,Level=target,Navigation=navigation,Samples=samples});
                File.WriteAllText(Path.Combine(output,"checkpoint.json"),JsonSerializer.Serialize(cases,new JsonSerializerOptions{WriteIndented=true}));
                void Press(NesButton button,int held,int settle){emulator.SetButton(button,true);Thread.Sleep(held);emulator.SetButton(button,false);Thread.Sleep(settle);}
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases,NativeReference=nativeReference,RequirePaired=requirePaired,Scope=wider?
                $"Levels {string.Join(',',levels??[3,6,10,15])} on both builds, {sampleCount??12} timed gameplay samples per level after visible startup, ordinary alternating jump input. No RAM writes or artwork learning. Deaths/restarts are retained, not counted as route completion. Known-cell fingerprint audit does not measure unknown scenery. "+(requirePaired?"Scanline96/completed-video coherent publication, paired reference saved from that same immutable frame; actor ownership and camera alignment require separate verification.":"Optional paused native reference is diagnostic only, not proof of scanline-paired video."):
                "Four level entrances per build through ordinary menu input, three paused snapshots per entrance. May include rendering-disabled loading frames despite gameplay RAM state. No RAM writes, artwork learning or whole-route/completion claim. Native metadata is a paused synchronous capture, not scanline-paired video."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{Environment.SetEnvironmentVariable("WARP4D_HOME",prior);}
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    internal static int RunPairedAlignment(string normalRom,string hugeRom,string fixtures,string output,bool committedViewport=false)
    {
        Directory.CreateDirectory(output);
        try
        {
            List<object> cases=[];int failed=0;
            foreach(var game in new[]{(Path:normalRom,Name:"famidash"),(Path:hugeRom,Name:"huge-man")})
            {
                byte[] rom=File.ReadAllBytes(game.Path);
                var profile=BuiltInFamiDashProfile.Create(rom,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rom)))!;
                foreach(string path in Directory.GetFiles(fixtures,game.Name+"-level-*.frame.json").Order())
                {
                    var frame=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!;
                    Require(frame.CaptureScanline==96 && frame.NativeScreenSequence==frame.Sequence && frame.NativeScreenPixels?.Length==61440,"Paired native evidence required.");
                    if(committedViewport)frame=frame with{ScrollX=frame.RawScrollX,ScrollY=frame.RawScrollY,ScrollSource="FamiDash playfield PPU viewport"};
                    using var scene=new SmbProfile().Build(frame,false,null,profile);
                    var spriteBounds=scene.Objects.Where(o=>o.IdentityKey.StartsWith("sprite:")).Select(o=>o.Bounds).ToArray();
                    int eligible=0,matched=0,occluded=0;List<object> mismatches=[];
                    foreach(var obj in scene.Objects.Where(o=>o.IdentityKey.StartsWith("background:")))
                    for(int dy=0;dy<obj.Image.Height;dy++)for(int dx=0;dx<obj.Image.Width;dx++)
                    {
                        var color=obj.Image.GetPixel(dx,dy);if(color.A==0)continue;
                        int x=obj.Bounds.X+dx,y=obj.Bounds.Y+dy;
                        if(x<0||y<0||x>=256||y>=240)continue;
                        if(spriteBounds.Any(r=>r.Contains(x,y))){occluded++;continue;}
                        eligible++;int native=frame.NativeScreenPixels![y*256+x]&0xffffff;
                        if((color.ToArgb()&0xffffff)==native)matched++;
                        else if(mismatches.Count<12)mismatches.Add(new{obj.IdentityKey,X=x,Y=y,Metadata=color.ToArgb()&0xffffff,Native=native});
                    }
                    bool passed=eligible==0 || matched/(double)eligible>=.95;if(!passed)failed++;
                    cases.Add(new{Frame=Path.GetFileName(path),frame.Sequence,frame.ScrollX,frame.ScrollY,frame.RawScrollX,frame.RawScrollY,
                        EligibleOpaquePixels=eligible,NativeMatchingPixels=matched,SpriteBoundsExcludedPixels=occluded,
                        Coverage=eligible==0?1:matched/(double)eligible,Passed=passed,Mismatches=mismatches});
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=failed==0,CommittedViewport=committedViewport,FailedFrames=failed,Cases=cases,
                Scope="95% exact native-color alignment for projected background opaque pixels outside extracted sprite bounds. Does not measure unknown scenery, sprite identity, full routes or pixels hidden by conservative sprite bounding rectangles."},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    internal static int RunAliasGuards(string normalRom,string hugeRom,string baselineDirectory,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            List<object> cases=[];
            foreach(var game in new[]{(Path:normalRom,Name:"famidash"),(Path:hugeRom,Name:"huge-man")})
            {
                byte[] rom=File.ReadAllBytes(game.Path);
                var profile=BuiltInFamiDashProfile.Create(rom,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rom)))!;
                var baseline=GameRecognitionProfileStore.ReadFromFile(Path.Combine(baselineDirectory,game.Name+".warp4d-game.json"));
                foreach(var pair in baseline.BackgroundRules)
                    Require(JsonSerializer.Serialize(profile.BackgroundRules[pair.Key])==JsonSerializer.Serialize(pair.Value),"Every old rule remains exactly unchanged.");
                var settings=profile.Clone();settings.BackgroundRules=baseline.BackgroundRules;
                Require(JsonSerializer.Serialize(settings)==JsonSerializer.Serialize(baseline),"All profile settings stay unchanged.");
                int aliases=0,guards=0;
                byte[] ram=new byte[2048];ram[0x49e]=2;
                var probe=new NesFrame{Ram=ram,PpuMask=0x18,Tiles=Enumerable.Range(0,4).Select(_=>new byte[960]).ToArray(),
                    Attributes=Enumerable.Range(0,4).Select(_=>new byte[960]).ToArray(),Oam=new byte[256],Chr=new byte[8192],Palette=new byte[32],ScrollSource="Synthetic alias negative controls",
                    NametablePixels=Enumerable.Range(0,4).Select(_=>Enumerable.Range(0,61440).Select(i=>i&0xffffff).ToArray()).ToArray()};
                var menuRam=(byte[])ram.Clone();menuRam[0x49e]=6;
                foreach(var pair in profile.BackgroundRules)
                {
                    string key=pair.Key;
                    var sig=new MetatileSignature(Convert.ToByte(key.Substring(1,1),16),Convert.ToByte(key.Substring(3,2),16),
                        Convert.ToByte(key.Substring(5,2),16),Convert.ToByte(key.Substring(7,2),16),Convert.ToByte(key.Substring(9,2),16));
                    Require(profile.Match(sig,probe,0,0)==null,"Unknown 256-color artwork must never match a ROM-derived key.");guards++;
                    Require(profile.Match(sig,probe with{Ram=menuRam},0,0)==null,"Menus must remain inactive.");guards++;
                    Require(profile.Match(sig,probe with{PpuMask=0},0,0)==null,"Disabled backgrounds must remain inactive.");guards++;
                    if(baseline.BackgroundRules.ContainsKey(key))continue;
                    aliases++;
                    var sources=baseline.BackgroundRules.Where(p=>p.Key[3..]==key[3..]).Select(p=>p.Value).ToArray();
                    Require(sources.Length>0 && sources.All(r=>r.Kind==pair.Value.Kind && r.Label==pair.Value.Label),"Alias classification is unambiguous and ROM-derived.");
                    var allowed=sources.SelectMany(r=>r.ArtworkVariants).Select(v=>v.Fingerprint).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    Require(pair.Value.ArtworkVariants.All(v=>allowed.Contains(v.Fingerprint)),"Alias artwork comes only from existing ROM-derived rules.");
                }
                Require(aliases>0,"Actual palette aliases required.");
                cases.Add(new{Game=game.Name,OldRules=baseline.BackgroundRules.Count,Rules=profile.BackgroundRules.Count,Aliases=aliases,
                    OldRulesAndSettingsExact=true,AllAliasesRomDerived=true,NegativeGuards=guards});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    internal static int RunPaletteAudit(string normalRom,string hugeRom,string fixtures,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            List<object> cells=[];HashSet<string> illustrated=[];
            foreach(var game in new[]{(Path:normalRom,Name:"famidash"),(Path:hugeRom,Name:"huge-man")})
            {
                byte[] rom=File.ReadAllBytes(game.Path);
                var profile=BuiltInFamiDashProfile.Create(rom,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rom)))!;
                foreach(string path in Directory.GetFiles(fixtures,game.Name+"-level-*.frame.json").Order())
                {
                    var frame=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!;
                    if(!profile.IsActive(frame)||frame.PpuMask is not byte m||(m&8)==0)continue;
                    string stem=Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
                    using Bitmap native=new(Path.Combine(fixtures,"native-reference",stem+".png"));
                    for(int y=frame.ScrollY/16*2;y<(frame.ScrollY+240)/8;y+=2)
                    for(int x=frame.ScrollX/16*2;x<(frame.ScrollX+256)/8;x+=2)
                    {
                        var sig=MetatileSignature.Read(frame,x,y);
                        Rectangle bounds=new(x*8-frame.ScrollX,y*8-frame.ScrollY,16,16);
                        if(!new Rectangle(0,0,256,240).Contains(bounds)||profile.BackgroundRules.ContainsKey(sig.Key))continue;
                        var sources=Enumerable.Range(0,4).Select(p=>sig with{Palette=(byte)p})
                            .Where(s=>profile.BackgroundRules.ContainsKey(s.Key)).ToArray();
                        if(sources.Length==0)continue;
                        string visual=MetatileVisualFingerprint.Read(frame,x,y);
                        string raw=BuiltInFamiDashProfile.Fingerprint(frame.Chr,0,sig.TopLeft,sig.BottomLeft,sig.TopRight,sig.BottomRight);
                        int equalPixels=0;
                        using Bitmap metadata=new(16,16);
                        for(int dy=0;dy<16;dy++)for(int dx=0;dx<16;dx++)
                        {
                            int wx=((x*8+dx)%512+512)%512,wy=((y*8+dy)%480+480)%480;
                            int table=(wx>=256?1:0)+(wy>=240?2:0);
                            int rgb=frame.NametablePixels[table][(wy%240)*256+wx%256]&0xffffff;
                            metadata.SetPixel(dx,dy,Color.FromArgb(unchecked((int)0xff000000)|rgb));
                            if((native.GetPixel(bounds.X+dx,bounds.Y+dy).ToArgb()&0xffffff)==rgb)equalPixels++;
                        }
                        var accepted=sources.Where(s=>profile.BackgroundRules[s.Key].AcceptsArtwork(visual)).Select(s=>s.Key).ToArray();
                        var rawAccepted=sources.Where(s=>profile.BackgroundRules[s.Key].AcceptsArtwork(raw)).Select(s=>s.Key).ToArray();
                        cells.Add(new{Game=game.Name,Frame=stem,Key=sig.Key,X=x,Y=y,Bounds=bounds,
                            SourceKeys=sources.Select(s=>s.Key).ToArray(),VisualAcceptedBy=accepted,RawAcceptedBy=rawAccepted,
                            NativeReferenceEqualPixels=equalPixels,ExactNativeCrop=equalPixels==256});
                        string identity=game.Name+"-"+sig.Key;
                        if(illustrated.Add(identity))
                        {
                            using Bitmap comparison=new(32,16);using Graphics g=Graphics.FromImage(comparison);
                            g.DrawImageUnscaled(metadata,0,0);
                            g.DrawImage(native,new Rectangle(16,0,16,16),bounds,GraphicsUnit.Pixel);
                            comparison.Save(Path.Combine(output,identity+".png"));
                        }
                    }
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cells=cells,
                Scope="Read-only alternate-palette candidates. Left crop is metadata artwork, right is separate paused native reference. Exact crop equality is local static-art evidence, not whole-frame publication pairing. No aliases generated or artwork learned."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    internal static int RunEditorUpgrade(string normalRom,string hugeRom,string legacyDirectory,string fixtures,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            List<object> cases=[];
            foreach(var game in new[]{(Path:normalRom,Name:"famidash"),(Path:hugeRom,Name:"huge-man")})
            {
                byte[] rom=File.ReadAllBytes(game.Path);
                var builtIn=BuiltInFamiDashProfile.Create(rom,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rom)))!;
                var legacy=GameRecognitionProfileStore.ReadFromFile(Path.Combine(legacyDirectory,game.Name+".warp4d-game.json"));
                const string key="P0-00D0C1D1";
                legacy.Name="User-owned bank edits";legacy.BackgroundRules[key].Label="User saw label";legacy.BackgroundRules[key].Kind="Cloud";
                legacy.FlatRegions.Add(new(0,0,5,5));
                var userRule=legacy.BackgroundRules[key].Clone();userRule.Label="User-only pattern";
                legacy.BackgroundRules["P3-FFFDFCFB"]=userRule;
                string original=JsonSerializer.Serialize(legacy);
                var frame=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(Path.Combine(fixtures,game.Name+"-level-15-0.frame.json")))!;
                using var editor=new GameProfileEditorForm(legacy,frame,false,game.Path,builtIn.RomSha256,builtInProfile:builtIn);
                editor.CreateControl();editor.AddBuiltInRules();
                var upgraded=editor.WorkingProfileForTest;
                Require(upgraded.Name==legacy.Name && upgraded.BackgroundRules[key].Label=="User saw label" && upgraded.BackgroundRules[key].Kind=="Cloud","User labels and classification preserved.");
                Require(upgraded.FlatRegions.Contains(new(0,0,5,5)) && upgraded.BackgroundRules["P3-FFFDFCFB"].Label=="User-only pattern","Custom regions and extra rule preserved.");
                int newVariants=0;
                foreach(var pair in builtIn.BackgroundRules)
                {
                    var actual=upgraded.BackgroundRules[pair.Key];
                    Require(pair.Value.ArtworkVariants.All(v=>actual.AcceptsArtwork(v.Fingerprint)),"Every new built-in variant merged.");
                    if(legacy.BackgroundRules.TryGetValue(pair.Key,out var oldRule))
                    {
                        Require(oldRule.ArtworkVariants.All(v=>actual.AcceptsArtwork(v.Fingerprint)),"Every old variant retained.");
                        newVariants+=actual.ArtworkVariants.Count-oldRule.ArtworkVariants.Count;
                    }
                    else newVariants+=actual.ArtworkVariants.Count;
                }
                Require(newVariants>0,"An actual older profile must gain variants.");
                editor.UndoEdit();Require(JsonSerializer.Serialize(editor.WorkingProfileForTest)==original,"Merge undo restores exact edited legacy profile.");
                editor.RedoEdit();Require(JsonSerializer.Serialize(editor.WorkingProfileForTest)==JsonSerializer.Serialize(upgraded),"Merge redo restores exact upgraded profile.");
                string path=Path.Combine(output,game.Name+".warp4d-game.json");GameRecognitionProfileStore.WriteToFile(path,upgraded);
                Require(JsonSerializer.Serialize(GameRecognitionProfileStore.ReadFromFile(path))==JsonSerializer.Serialize(upgraded),"Saved upgrade roundtrip exact.");
                Require(JsonSerializer.Serialize(legacy)==original,"Editor must not mutate caller-owned legacy profile.");
                cases.Add(new{Game=game.Name,NewRules=builtIn.BackgroundRules.Keys.Count(k=>!legacy.BackgroundRules.ContainsKey(k)),NewVariants=newVariants,LabelsPreserved=true,CustomRegionsPreserved=true,UndoRedoExact=true,SaveRoundtripExact=true});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    internal static int RunBankRender(string normalRom,string hugeRom,string fixtures,string output,int expectedFrames=96,bool requirePaired=false)
    {
        Directory.CreateDirectory(output);
        try
        {
            List<object> cases=[];int frames=0,empty=0,roundtripChecks=0,uniformFades=0,fadeGuardChecks=0;
            foreach(var game in new[]{(Path:normalRom,Name:"famidash"),(Path:hugeRom,Name:"huge-man")})
            {
                byte[] rom=File.ReadAllBytes(game.Path);
                var profile=BuiltInFamiDashProfile.Create(rom,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rom)))!;
                string export=Path.Combine(output,game.Name+".warp4d-game.json");
                GameRecognitionProfileStore.WriteToFile(export,profile);
                var imported=GameRecognitionProfileStore.ReadFromFile(export);
                Require(JsonSerializer.Serialize(profile)==JsonSerializer.Serialize(imported),"Export/import rules must match exactly.");
                foreach(string path in Directory.GetFiles(fixtures,game.Name+"-level-*.frame.json").Order())
                {
                    var f=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!;
                    if(requirePaired)Require(f.CaptureScanline==96 && f.NativeScreenSequence==f.Sequence && f.NativeScreenPixels?.Length==61440,"Paired frame provenance required.");
                    using var scene=new SmbProfile().Build(f,false,null,profile);
                    using var importedScene=new SmbProfile().Build(f,false,null,imported);
                    Require(scene.Objects.Select(o=>(o.IdentityKey,o.Bounds)).SequenceEqual(importedScene.Objects.Select(o=>(o.IdentityKey,o.Bounds))),"Imported scene object identity.");roundtripChecks++;
                    foreach(var o in scene.Objects.Where(o=>o.IdentityKey.StartsWith("background:")))
                    {
                        bool visible=false;
                        for(int y=0;y<o.Image.Height && !visible;y++)for(int x=0;x<o.Image.Width;x++)if(o.Image.GetPixel(x,y).A!=0){visible=true;break;}
                        Require(visible,"Empty transparent scenery must not project.");
                    }
                    if(f.PpuMask is byte m && (m&0x18)==0){Require(scene.Objects.Count==0,"Disabled rendering stays flat.");empty++;}
                    if(f.IsUniformPairedFade())
                    {
                        Require(scene.Objects.Count==0 && !profile.IsActive(f),"Uniform native fade must not resurrect black sprite projections.");
                        for(int y=0;y<240;y++)for(int x=0;x<256;x++)
                            Require((scene.Background.GetPixel(x,y).ToArgb()&0xffffff)==(f.NativeScreenPixels![y*256+x]&0xffffff),"Uniform fade stays exactly native flat.");
                        uniformFades++;
                    }
                    if(requirePaired && fadeGuardChecks==0)fadeGuardChecks=CheckUniformFadeGuards(f,profile);
                    using var renderer=new WarpRendererControl{Size=new(900,780),UseGpu=true};
                    renderer.CreateControl();renderer.SetScene(scene.Clone());
                    using Bitmap image=new(900,780);renderer.DrawToBitmap(image,renderer.ClientRectangle);
                    Require(renderer.RendererStatus.StartsWith("GPU"),"GPU rendering required.");
                    string name=Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
                    image.Save(Path.Combine(output,name+".png"));
                    cases.Add(new{Name=name,Objects=scene.Objects.Count,Backgrounds=scene.Objects.Count(o=>o.IdentityKey.StartsWith("background:")),f.PpuMask});frames++;
                }
            }
            Require(frames==expectedFrames,"Full preserved route cohort required.");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Frames=frames,RequirePaired=requirePaired,UniformNativeFadeFrames=uniformFades,UniformFadeGuardChecks=fadeGuardChecks,RenderingDisabledFrames=empty,RoundtripSceneChecks=roundtripChecks,NoEmptyBackgroundObjects=true,Cases=cases},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static int CheckUniformFadeGuards(NesFrame source,GameRecognitionProfile profile)
    {
        int black=NesPalette.Get(0x0f).ToArgb();
        var fade=source with{CaptureScanline=96,NativeScreenSequence=source.Sequence,
            NativeScreenPixels=Enumerable.Repeat(black,61440).ToArray(),Palette=Enumerable.Repeat((byte)0x0f,32).ToArray(),PpuMask=0x1e};
        Require(fade.IsUniformPairedFade(),"Complete uniform fade positive control.");
        Require(!(fade with{NativeScreenSequence=fade.Sequence+1}).IsUniformPairedFade(),"Mismatched video must not suppress objects.");
        Require(!(fade with{NativeScreenPixels=null}).IsUniformPairedFade(),"Missing video must not suppress objects.");
        Require(!(fade with{CaptureScanline=null}).IsUniformPairedFade(),"Unpaired event screenshot must not suppress objects.");
        var palette=(byte[])fade.Palette.Clone();palette[31]=0x30;
        Require(!(fade with{Palette=palette}).IsUniformPairedFade(),"Noncollapsed palette must not be treated as full fade.");
        var pixels=(int[])fade.NativeScreenPixels!.Clone();pixels[1234]=NesPalette.Get(0x30).ToArgb();
        Require(!(fade with{NativeScreenPixels=pixels}).IsUniformPairedFade(),"Even one visible native pixel keeps normal extraction.");
        int checks=6;
        foreach(string mode in new[]{"custom","exact-smb","generic"})
        {
            using var scene=new SmbProfile().Build(fade,mode=="exact-smb",null,mode=="custom"?profile:null);
            Require(scene.Objects.Count==0,"All scene modes preserve uniform native fade.");checks++;
        }
        return checks;
    }
    internal static int RunArtworkAudit(string normalRom,string hugeRom,string fixtures,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            List<object> misses=[];int frames=0,known=0,matched=0,fullCells=0;
            Dictionary<string,int> unknownSignatures=new(StringComparer.Ordinal);
            foreach(var game in new[]{(Path:normalRom,Name:"famidash"),(Path:hugeRom,Name:"huge-man")})
            {
                byte[] rom=File.ReadAllBytes(game.Path);
                var profile=BuiltInFamiDashProfile.Create(rom,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rom)))!;
                foreach(string path in Directory.GetFiles(fixtures,game.Name+"-level-*.frame.json").Order())
                {
                    var f=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!;frames++;
                    if(!profile.IsActive(f)||f.PpuMask is not byte m || (m&8)==0)continue;
                    for(int y=f.ScrollY/16*2;y<(f.ScrollY+240)/8;y+=2)
                    for(int x=f.ScrollX/16*2;x<(f.ScrollX+256)/8;x+=2)
                    {
                        var sig=MetatileSignature.Read(f,x,y);
                        if(!new Rectangle(0,0,256,240).Contains(new Rectangle(x*8-f.ScrollX,y*8-f.ScrollY,16,16)))continue;
                        fullCells++;
                        if(!profile.BackgroundRules.TryGetValue(sig.Key,out var rule))
                        {
                            string identity=game.Name+":"+sig.Key;
                            unknownSignatures.TryGetValue(identity,out int count);
                            unknownSignatures[identity]=count+1;
                            continue;
                        }
                        known++;
                        if(profile.Match(sig,f,x,y)!=null){matched++;continue;}
                        string raw=BuiltInFamiDashProfile.Fingerprint(f.Chr,0,sig.TopLeft,sig.BottomLeft,sig.TopRight,sig.BottomRight);
                        int chrStart=16+((rom[6]&4)!=0?512:0)+rom[4]*16384;
                        int[][] mappedBanks=Enumerable.Range(0,4).Select(q=>Enumerable.Range(0,rom[5]*8)
                            .Where(b=>rom.AsSpan(chrStart+b*1024,1024).SequenceEqual(f.Chr.AsSpan(q*1024,1024))).ToArray()).ToArray();
                        Require(mappedBanks.All(b=>b.Length>0),"Every captured background CHR quarter must come from this exact cartridge.");
                        byte[] reconstructed=new byte[4096];
                        for(int q=0;q<4;q++)Array.Copy(rom,chrStart+mappedBanks[q][0]*1024,reconstructed,q*1024,1024);
                        Require(BuiltInFamiDashProfile.Fingerprint(reconstructed,0,sig.TopLeft,sig.BottomLeft,sig.TopRight,sig.BottomRight)==raw,"ROM bank reconstruction must reproduce the missed raw artwork.");
                        misses.Add(new{Game=game.Name,Frame=Path.GetFileName(path),Key=sig.Key,X=x,Y=y,
                            Visual=MetatileVisualFingerprint.Read(f,x,y),RawChr=raw,RawAlreadyKnown=rule.AcceptsArtwork(raw),
                            ExactRomBackgroundBankCandidates=mappedBanks,RomReconstructionExact=true,
                            Palette=f.Palette.Take(16).ToArray(),Banks=f.Ram.Skip(0x494).Take(3).ToArray()});
                    }
                }
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Frames=frames,FullViewportCells=fullCells,KnownCells=known,MatchedCells=matched,Misses=misses,
                UnknownSignatures=unknownSignatures.OrderBy(p=>p.Key).Select(p=>new{Identity=p.Key,Cells=p.Value}).ToArray(),
                Scope="Diagnostic only: raw CHR bitplane comparison against existing ROM-derived fingerprints. Unknown signature counts include intentionally flat ground, backdrop and interface tiles; they are not automatically missing artwork. Partial viewport cells excluded. No artwork learning or rule changes."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    internal static int RunLoading(string normalRom,string hugeRom,string fixtures,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            int blank=0,visible=0;List<object> cases=[];
            foreach(var game in new[]{(Path:normalRom,Name:"famidash"),(Path:hugeRom,Name:"huge-man")})
            {
                byte[] rom=File.ReadAllBytes(game.Path);
                var profile=BuiltInFamiDashProfile.Create(rom,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rom)))!;
                foreach(string path in Directory.GetFiles(fixtures,game.Name+"-level-*.frame.json").Order())
                {
                    var frame=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!;
                    Require(frame.Ram[0x49e]==2,"Preserved RAM-gameplay fixture required.");
                    bool disabled=frame.PpuMask is byte mask && (mask&0x18)==0;
                    using var scene=new SmbProfile().Build(frame,false,null,profile);
                    if(disabled){Require(!profile.IsActive(frame)&&scene.Objects.Count==0,"Stale loading artwork must stay flat.");blank++;}
                    else {Require(profile.IsActive(frame),"Visible gameplay remains active.");visible++;}
                    using var renderer=new WarpRendererControl{Size=new(900,780),UseGpu=true};
                    renderer.CreateControl();renderer.SetScene(scene.Clone());
                    using Bitmap image=new(900,780);renderer.DrawToBitmap(image,renderer.ClientRectangle);
                    Require(renderer.RendererStatus.StartsWith("GPU"),"GPU required.");
                    string name=Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
                    image.Save(Path.Combine(output,name+".png"));
                    cases.Add(new{Name=name,Disabled=disabled,Objects=scene.Objects.Count});
                }
            }
            Require(blank==8&&visible==16,"Full preserved pilot inventory required.");
            int visibilityChecks = CheckIndependentVisibility();
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,BlankLoadingFrames=blank,VisibleGameplayFrames=visible,IndependentVisibilityChecks=visibilityChecks,Cases=cases},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }

    private static int CheckIndependentVisibility()
    {
        // Known SMB ground plus one opaque sprite: a positive control for
        // each layer, with no native screenshot to hide stale metadata.
        byte[][] tiles=Enumerable.Range(0,4).Select(_=>new byte[960]).ToArray();
        foreach(var table in tiles)
            for(int y=0;y<30;y++)for(int x=0;x<32;x++)
                table[y*32+x]=(byte)((y%2,x%2) switch{(0,0)=>0xb4,(1,0)=>0xb6,(0,1)=>0xb5,_=>0xb7});
        byte[] chr=new byte[8192],palette=new byte[32],oam=new byte[256],ram=new byte[2048];
        Array.Fill(oam,(byte)250);oam[0]=80;oam[1]=0;oam[2]=0;oam[3]=80;
        for(int y=0;y<8;y++)chr[y]=255;
        palette[17]=0x16;ram[0x770]=1;
        var frame=new NesFrame{Tiles=tiles,Attributes=Enumerable.Range(0,4).Select(_=>Enumerable.Repeat((byte)0x55,960).ToArray()).ToArray(),
            Chr=chr,Palette=palette,Oam=oam,Ram=ram,ScrollSource="Synthetic visibility positive controls",
            NametablePixels=Enumerable.Range(0,4).Select(_=>Enumerable.Repeat(Color.Orange.ToArgb(),61440).ToArray()).ToArray()};
        var signature=MetatileSignature.Read(frame,0,4);
        var profile=new GameRecognitionProfile{BackgroundRules=new(){[signature.Key]=new(){Label="Visibility ground"}}};
        int checks=0;
        foreach(byte? mask in Enumerable.Range(0,32).Select(i=>(byte?)i).Append(null))
        {
            var input=frame with{PpuMask=mask};
            bool bg=mask is null || (mask&8)!=0,sp=mask is null || (mask&16)!=0;
            Require(profile.IsActive(input)==(bg||sp),"Independent profile activation.");checks++;
            Require((profile.Match(signature,input,0,4)!=null)==bg,"Hidden background must not match.");checks++;
            foreach(var mode in new[]{"custom","exact-smb","generic"})
            {
                using var scene=new SmbProfile().Build(input,mode=="exact-smb",null,mode=="custom"?profile:null);
                Require(scene.Objects.Any(o=>o.IdentityKey.StartsWith("background:"))==(bg&&mode!="generic"),$"Background visibility: {mode}, mask={mask}.");checks++;
                Require(scene.Objects.Any(o=>o.IdentityKey.StartsWith("sprite:"))==sp,$"Sprite visibility: {mode}, mask={mask}.");checks++;
            }
        }
        return checks;
    }
}
