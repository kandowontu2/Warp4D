using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D;

internal static class IcarusFortressHudTests
{
    internal static int Run(string manifest,string output,bool baseline=false)
    {
        Directory.CreateDirectory(output);
        try
        {
            var samples=JsonSerializer.Deserialize<ProfileCoverageTests.Sample[]>(File.ReadAllText(manifest))!;
            using var reader=new ArchiveFrameReader();List<object> results=[];
            foreach(var sample in samples)
            {
                var frame=CartridgeViewport.NormalizeCapture("icarus",reader.Read(sample.Frame,sample.Archive,sample.ArchiveSHA256,sample.FrameSHA256));
                Require(frame.CaptureScanline==96&&frame.Sequence==frame.NativeScreenSequence&&frame.NativeScreenPixels?.Length==61440,"Paired native fixture");
                var profile=GameRecognitionProfileStore.ReadFromFile(sample.Profile);
                if(baseline){profile.FlatSpriteStateAddress=null;profile.FlatSpriteSlotsByState=null;profile.FormatVersion=18;}
                int[] expectedSlots=[0,1,2,3,4,5,6,7,57,58,59,60,61,62,63];
                using var scene=new SmbProfile().Build(frame,false,null,profile);
                int opaque=0,hiddenSnapshotPixels=0;
                foreach(int slot in expectedSlots)
                {
                    int offset=slot*4,y=frame.Oam[offset]+1,x=frame.Oam[offset+3];if(y>=240)continue;
                    using var tile=SmbProfile.DecodeSpriteTile(frame.Chr,frame.Palette,frame.Oam[offset+1],frame.Oam[offset+2],frame.SpritePatternBase,frame.LargeSprites);
                    for(int py=0;py<tile.Height&&y+py<240;py++)for(int px=0;px<tile.Width&&x+px<256;px++)
                    {
                        int argb=tile.GetPixel(px,py).ToArgb();if((uint)argb>>24==0)continue;
                        var point=new Point(x+px,y+py);
                        if((argb&0xffffff)!=(frame.NativeScreenPixels![point.Y*256+point.X]&0xffffff))
                        {
                            Require(!scene.Objects.Any(o=>o.SortOrder==100&&o.Bounds.Contains(point)&&o.Image.GetPixel(point.X-o.Bounds.X,point.Y-o.Bounds.Y).ToArgb()==argb),"Stale HUD pixel must not be redrawn");
                            hiddenSnapshotPixels++;continue;
                        }
                        // Native reserved HUD pixels, not a projection-derived
                        // count or protection-rectangle acceptance shortcut.
                        Require(scene.Objects.Any(o=>!o.ProjectionEnabled&&o.SortOrder==100&&o.Bounds.Contains(point)&&
                            o.Image.GetPixel(point.X-o.Bounds.X,point.Y-o.Bounds.Y).ToArgb()==argb),"Reserved fortress HUD pixel is not a native-flat sprite: slot"+slot);
                        opaque++;
                    }
                }
                Require(opaque>0,"Nonempty independent HUD reference");
                var pit=scene.Objects.Single(o=>o.Kind==SceneObjectKind.Player);
                Require(pit.ProjectionEnabled,"Actual Pit remains projected");
                Require(scene.Objects.Any(o=>o.SortOrder<20&&o.ProjectionEnabled),"Masonry remains projected");
                foreach(int state in new[]{3,5,7})
                {
                    var ram=(byte[])frame.Ram.Clone();ram[160]=(byte)state;var changed=frame with{Ram=ram};
                    Require(expectedSlots.All(i=>profile.ProtectsSpriteSlot(changed,i))&&Enumerable.Range(8,49).All(i=>!profile.ProtectsSpriteSlot(changed,i)),"Only declared HUD owners protected in each fortress mode");
                    Require(!profile.ProtectsSprite(new(24,16,8,8),changed),"Overworld HUD rectangle must not flatten fortress enemies");
                }
                foreach(int state in new[]{0,1,2,4,6,8,255})
                {
                    var ram=(byte[])frame.Ram.Clone();ram[160]=(byte)state;
                    Require(Enumerable.Range(0,64).All(i=>!profile.ProtectsSpriteSlot(frame with{Ram=ram},i)),"Fortress ownership must not affect other game modes");
                }
                var clone=profile.Clone();var imported=JsonSerializer.Deserialize<GameRecognitionProfile>(JsonSerializer.Serialize(profile))!;imported.Normalize();
                foreach(var copy in new[]{clone,imported})Require(copy.FormatVersion==19&&copy.FlatSpriteStateAddress==160&&
                    JsonSerializer.Serialize(copy.FlatSpriteSlotsByState)==JsonSerializer.Serialize(profile.FlatSpriteSlotsByState),"Clone/import preserves exact HUD ownership");
                clone.FlatSpriteSlotsByState![3][0]=8;
                Require(profile.FlatSpriteSlotsByState![3][0]==0,"Deep clone HUD ownership");
                var invalid=profile.Clone();invalid.FlatSpriteSlotsByState![3].Add(8);Reject(invalid);
                invalid=profile.Clone();invalid.FlatSpriteSlotsByState![3].Add(64);Reject(invalid);
                invalid=profile.Clone();invalid.FlatSpriteSlotsByState![3].Add(0);Reject(invalid);
                invalid=profile.Clone();invalid.FlatSpriteStateAddress=2048;Reject(invalid);
                if(results.Count==0)
                {
                    var legacy=profile.Clone();legacy.FlatSpriteStateAddress=null;legacy.FlatSpriteSlotsByState=null;legacy.FormatVersion=18;
                    legacy.Name="My custom fortress profile";legacy.FlatRegions.Add(new(0,200,8,8));
                    using var editor=new UI.GameProfileEditorForm(legacy,frame,false,"Kid Icarus.nes",new string('A',64),builtInProfile:profile);
                    editor.CreateControl();string before=JsonSerializer.Serialize(editor.WorkingProfileForTest),unsaved=JsonSerializer.Serialize(editor.EditedProfile);
                    editor.UpdateBuiltInHudProtection();var after=editor.WorkingProfileForTest;
                    Require(after.Name==legacy.Name&&after.FlatRegions.Contains(new(0,200,8,8))&&
                        JsonSerializer.Serialize(after.BackgroundRules)==JsonSerializer.Serialize(legacy.BackgroundRules)&&
                        JsonSerializer.Serialize(after.FlatSpriteSlotsByState)==JsonSerializer.Serialize(profile.FlatSpriteSlotsByState),"Opt-in HUD update preserves custom scenery/name/regions");
                    Require(JsonSerializer.Serialize(editor.EditedProfile)==unsaved,"HUD upgrade does not auto-save");
                    string upgraded=JsonSerializer.Serialize(after);editor.UndoEdit();Require(JsonSerializer.Serialize(editor.WorkingProfileForTest)==before,"Exact HUD ownership undo");
                    editor.RedoEdit();Require(JsonSerializer.Serialize(editor.WorkingProfileForTest)==upgraded,"Exact HUD ownership redo");
                }
                using var renderer=new WarpRendererControl{Size=new(820,780),UseGpu=true};renderer.CreateControl();renderer.SetScene(scene.Clone());
                using Bitmap preview=new(820,780);renderer.DrawToBitmap(preview,renderer.ClientRectangle);
                Require(renderer.RendererStatus.StartsWith("GPU"),"GPU readback required");
                preview.Save(Path.Combine(output,$"{results.Count:D4}-"+Path.GetFileNameWithoutExtension(sample.Frame)+".png"));
                results.Add(new{sample.Area,Mode=frame.Ram[160],Room=frame.Ram[70],ExactNativeHudOpaquePixels=opaque,HiddenSnapshotPixels=hiddenSnapshotPixels,ProjectedPit=true,ProjectedMasonry=true,OtherModesUnaffected=true,InvalidDeclarationsRejected=true,Passed=true});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=results.Count,Scope="Named archived native fortress HUD fixtures; synthetic state guards are not additional native-game coverage",Results=results},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Reject(GameRecognitionProfile profile){try{profile.Normalize();}catch(InvalidDataException){return;}throw new InvalidOperationException("Invalid HUD declaration accepted");}
}
