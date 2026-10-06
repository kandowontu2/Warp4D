using System.Diagnostics;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

internal static class BuiltInProfileTests
{
    internal sealed class Input
    {
        public string Id { get; set; } = "";
        public string Rom { get; set; } = "";
    }
    internal static int Run(string manifest,string recipes,string output)
    {
        Directory.CreateDirectory(output);
        string? previous=Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        try
        {
            var inputs=JsonSerializer.Deserialize<Input[]>(File.ReadAllText(manifest))!;
            var authored=JsonSerializer.Deserialize<ProfileAuthoring.Recipe[]>(File.ReadAllText(recipes))!;
            List<object> report=[];
            Require(inputs.Length==BuiltInGameProfiles.Cartridges.Length,"All nine cartridges tested");
            foreach(var input in inputs)
            {
                byte[] rom=File.ReadAllBytes(input.Rom);
                string hash=Convert.ToHexString(SHA256.HashData(rom));
                Require(BuiltInGameProfiles.Identify(rom)?.Id==input.Id,"Exact payload identity "+input.Id);
                GameRecognitionProfile profile=BuiltInGameProfiles.Create(rom,hash)!;
                Require(profile is not null&&profile.BackgroundRules.Count>0,"Embedded rules "+input.Id);
                byte[] changed=(byte[])rom.Clone();changed[^1]^=1;
                Require(BuiltInGameProfiles.Create(changed,hash) is null,"Modified payload rejected");
                byte[] header=(byte[])rom.Clone();header[12]=42;
                Require(BuiltInGameProfiles.Identify(header)?.Id==input.Id,"Header-only change accepted");
                byte[] wrongMapper=(byte[])rom.Clone();wrongMapper[6]^=0x10;
                Require(BuiltInGameProfiles.Identify(wrongMapper) is null,"Different mapper rejected");
                (long Copies,long Avoided) captureMetrics=default;
                using(NesEmulator emulator=new())
                {
                    emulator.Load(input.Rom);
                    Require(emulator.BuiltInGameProfile?.BackgroundRules.Count==profile!.BackgroundRules.Count,"Native auto-assignment "+input.Id);
                    Thread.Sleep(450);emulator.TogglePause();Thread.Sleep(30);
                    var nativeFrame=emulator.CaptureFrame();
                    Require(nativeFrame?.NativeScreenPixels?.Length==256*240,"Native screen snapshot "+input.Id);
                    if(input.Id is "smb2" or "smb3" or "castlevania" or "icarus" or "contra" or "megaman2")
                    {
                        Require(nativeFrame!.CaptureScanline==96&&nativeFrame.NativeScreenSequence==nativeFrame.Sequence,
                            "Coherent native frame "+input.Id);
                        captureMetrics=emulator.PairedCaptureMetricsForTest;
                        Require(captureMetrics.Copies>0&&captureMetrics.Avoided>0&&
                            Math.Abs(captureMetrics.Copies-captureMetrics.Avoided)<=1,
                            "Only paired frames decode playfield data "+input.Id);
                    }
                }
                string exported=Path.Combine(output,input.Id+".warp4d-game.json");
                GameRecognitionProfileStore.WriteToFile(exported,profile!);
                var imported=GameRecognitionProfileStore.ReadFromFile(exported);
                Require(imported.CellSize==profile!.CellSize&&imported.FlatRegions.SequenceEqual(profile.FlatRegions)&&imported.SceneAnchors.Count==profile.SceneAnchors.Count,"Editor export metadata round-trip");
                Require(JsonSerializer.Serialize(imported.ConditionalFlatRegions)==JsonSerializer.Serialize(profile.ConditionalFlatRegions),
                    "Conditional protection export round-trip");
                var recipe=authored.Single(r=>r.Id==input.Id);
                {
                    var custom = profile.Clone();
                    string keptKey = custom.BackgroundRules.Keys.First();
                    string addedKey = custom.BackgroundRules.Keys.Last();
                    custom.BackgroundRules[keptKey].Label = "My custom classification";
                    custom.BackgroundRules.Remove(addedKey);
                    NesFrame frame = JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(recipe.Frames[0]))!;
                    if (input.Id == "icarus")
                    {
                        Require(custom.ConditionalFlatRegions?.Count == 6 && custom.FormatVersion == 14, "Chamber and fortress protection metadata");
                        Require(profile.PlayerLabel=="Pit"&&profile.PlayerTracking?.XAddress==1827&&profile.PlayerTracking.YAddress==1824&&profile.PlayerTracking.SpritePalette==0,
                            "Built-in Pit uses native screen coordinates and palette");
                        Require(imported.PlayerLabel=="Pit"&&imported.PlayerTracking?.XAddress==1827&&imported.PlayerTracking.YAddress==1824,
                            "Export/import retains Pit tracking");
                        Require(JsonSerializer.Serialize(custom.ConditionalFlatRegions)==JsonSerializer.Serialize(profile.ConditionalFlatRegions), "Conditional protection clone");
                        byte[] state = (byte[])frame.Ram.Clone();
                        state[0xa0]=2; state[0x3b]=37; state[0x3a]=8;
                        NesFrame shop=frame with {Ram=state};
                        Require(!custom.Allows(new(64,112,16,16),shop)&&custom.Allows(new(0,64,16,16),shop), "Shop prices protected, masonry eligible");
                        state[0x3b]=38;
                        Require(!custom.Allows(new(64,48,16,16),shop), "Black-market lettering protected");
                        foreach(byte room in new byte[]{35,37,38})
                        {
                            state[0x3b]=room;state[0x3a]=0;
                            Require(custom.Allows(new(64,48,16,16),shop), "Stale chamber ID cannot mask returned field");
                            state[0x3a]=8;
                            Require(!custom.Allows(new(64,48,16,16),shop), "Live chamber instruction protected");
                        }
                        state[0x3b]=0;
                        Require(custom.Allows(new(64,112,16,16),shop), "Field unaffected by market protection");
                        state[0x3b]=37; state[0xa0]=1;
                        Require(custom.Allows(new(64,112,16,16),shop), "Wrong mode cannot activate market protection");
                        state[0xa0]=2; state[0x3b]=35;
                        Require(!custom.Allows(new(64,48,16,16),shop)&&custom.Allows(new(112,88,16,16),shop), "Training text protected, instructor eligible");
                        state[0x3b]=0;
                        foreach(byte fortressMode in new byte[]{3,5,7})
                        {
                            state[0xa0]=fortressMode;
                            Require(!custom.Allows(new(96,208,16,16),shop), "Fortress status protected in mode "+fortressMode);
                        }
                        foreach(byte fieldMode in new byte[]{2,4,6,8})
                        {
                            state[0xa0]=fieldMode;
                            Require(custom.Allows(new(96,208,16,16),shop), "Field floor remains eligible in mode "+fieldMode);
                        }
                        Require(profile.ConditionalFlatRegions![0].ExpectedRam[59]==37, "Clone did not mutate source");
                    }
                    using GameProfileEditorForm editor = new(custom, frame, false, input.Rom, hash, builtInProfile: profile);
                    editor.AddBuiltInRules();
                    Require(editor.WorkingProfileForTest.BackgroundRules.ContainsKey(addedKey), "Opt-in built-in expansion");
                    Require(editor.WorkingProfileForTest.BackgroundRules[keptKey].Label == "My custom classification", "User labels preserved");
                    editor.UndoEdit();
                    Require(!editor.WorkingProfileForTest.BackgroundRules.ContainsKey(addedKey), "Built-in expansion is undoable");
                }
                if(profile!.CellSize==8)
                {
                    NesFrame frame=JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(recipe.Frames[0]))!;
                    using GameProfileEditorForm editor=new(profile,frame,false,input.Rom,hash);
                    editor.SelectGamePixelForTest(97,49);
                    editor.SelectGamePixelForTest(105,49,true);
                    Require(editor.SelectedCellCountForTest==2,"8px Ctrl-additive cells");
                    editor.DragSelectGamePixelsForTest(97,49,121,49);
                    Require(editor.SelectedCellCountForTest==4,"8px brush drag");
                    editor.PickerForTest.RectangleSelection=true;
                    editor.DragSelectGamePixelsForTest(97,49,105,57);
                    Require(editor.SelectedCellCountForTest==4,"8px rectangle drag");
                    Require(editor.WorkingProfileForTest.CellSize==8&&editor.WorkingProfileForTest.SceneAnchors.Count==profile.SceneAnchors.Count,"Editor preserves anchors");
                }
                double elapsed=0;int scenery=0,sprites=0;
                foreach(string path in recipe.Frames)
                {
                    NesFrame frame=CartridgeViewport.NormalizeCapture(input.Id, JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!);
                    long start=Stopwatch.GetTimestamp();
                    using var scene=new SmbProfile().Build(frame,false,null,profile);
                    elapsed+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    Require(profile.IsActive(frame),"Gameplay recognized "+input.Id);
                    scenery=scene.Objects.Count(o=>o.SortOrder<20);sprites=scene.Objects.Count(o=>o.SortOrder==20);
                    Require(scenery>0,"Background objects "+input.Id);
                    foreach(Rectangle r in profile.FlatRegions)
                    if(frame.NativeScreenPixels is not null)
                    for(int y=r.Top;y<r.Bottom;y++) for(int x=r.Left;x<r.Right;x++)
                        Require(scene.Background.GetPixel(x,y).ToArgb()==frame.NativeScreenPixels[y*256+x],"Native HUD pixels preserved");
                    using WarpRendererControl renderer=new(){Size=new(820,780)};
                    renderer.CreateControl();renderer.SetScene(scene.Clone());
                    using Bitmap image=new(820,780);renderer.DrawToBitmap(image,renderer.ClientRectangle);
                    image.Save(Path.Combine(output,input.Id+"-"+Path.GetFileNameWithoutExtension(path)+".png"),ImageFormat.Png);
                }
                foreach(string path in recipe.Menus)
                {
                    NesFrame frame=CartridgeViewport.NormalizeCapture(input.Id, JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(path))!);
                    Require(!profile.IsActive(frame),"Menu rejected "+input.Id);
                    using var scene=new SmbProfile().Build(frame,false,null,profile);
                    Require(scene.Objects.Count==0,"Menu stays flat "+input.Id);
                }
                report.Add(new{input.Id,Patterns=profile.BackgroundRules.Count,GameplayFrames=recipe.Frames.Length,MenuFrames=recipe.Menus.Length,SceneryObjects=scenery,SpriteObjects=sprites,AverageRecognitionMs=elapsed/recipe.Frames.Length,AutoAssigned=true,HudPreserved=true,PairedPlayfieldCopies=captureMetrics.Copies,DiscardedPlayfieldCopiesAvoided=captureMetrics.Avoided});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{Environment.SetEnvironmentVariable("WARP4D_HOME",previous);}
    }
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
}
