using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D;

internal static class IcarusHudBrickTests
{
    internal static int Run(string input,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var frame=CartridgeViewport.NormalizeCapture("icarus",JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(input))!);
            using var resource=typeof(BuiltInGameProfiles).Assembly.GetManifestResourceStream("Warp4D.Profiles.Data.icarus.json")!;
            var profile=JsonSerializer.Deserialize<GameRecognitionProfile>(resource)!;profile.Normalize();
            var legacy=profile.Clone();legacy.FlatRegions=[new(16,8,48,24)];legacy.FlatSpriteRegions=null;legacy.FlatSpriteStateAddress=null;legacy.FlatSpriteSlotsByState=null;
            using var oldScene=new SmbProfile().Build(frame,false,null,legacy);
            using var scene=new SmbProfile().Build(frame,false,null,profile);
            Rectangle hud=new(16,8,48,24);
            var bricks=scene.Objects.Where(o=>o.SortOrder<20&&o.ProjectionEnabled&&o.Bounds.IntersectsWith(hud)).ToArray();
            var flat=scene.Objects.Where(o=>o.SortOrder==100&&o.Label=="HUD / interface").ToArray();
            using var renderer=new WarpRendererControl{Size=new(800,740),UseGpu=true,MotionSecondsForTest=0,EnableStyleTransitions=false};
            renderer.ApplySettings(new(){Animate=false,Opacity=.65f,Rotation=new(){XW=60,YW=74,ZW=81},Dimensions=new(){AdaptiveQuality=false},Effects=new(){Enabled=false}});
            renderer.CreateControl();renderer.SetScene(oldScene.Clone());Save("legacy");
            renderer.SetScene(scene.Clone());Save("fixed");
            scene.Background.Save(Path.Combine(output,"background.png"));
            Require(profile.FlatRegions.Count==0&&profile.FlatSpriteRegions?.Count==1,"Sprite-only HUD declaration");
            Require(bricks.Length>0&&oldScene.Objects.All(o=>o.SortOrder>=20||!o.Bounds.IntersectsWith(hud)),"Previously protected top-left bricks now projected");
            Require(flat.Length>0&&flat.All(o=>!o.ProjectionEnabled),"Score/health sprites remain flat");
            int hudPixels=0;
            if(frame.NativeScreenPixels is not {Length:61440})throw new InvalidOperationException("Native HUD reference required");
            foreach(var item in flat)for(int y=0;y<item.Image.Height;y++)for(int x=0;x<item.Image.Width;x++)
            {
                int pixel=item.Image.GetPixel(x,y).ToArgb();if((uint)pixel>>24==0)continue;
                int sx=item.Bounds.X+x,sy=item.Bounds.Y+y;
                Require((pixel&0xffffff)==(frame.NativeScreenPixels[sy*256+sx]&0xffffff),"Flat HUD opaque pixels match native reference");hudPixels++;
            }
            var roundTrip=JsonSerializer.Deserialize<GameRecognitionProfile>(JsonSerializer.Serialize(profile))!;roundTrip.Normalize();
            Require(roundTrip.FlatSpriteRegions!.SequenceEqual(profile.FlatSpriteRegions!)&&profile.Clone().FlatSpriteRegions!.SequenceEqual(profile.FlatSpriteRegions!),"Clone/export preserve sprite-only regions");
            var custom=legacy.Clone();custom.FormatVersion=14;custom.Name="My Icarus profile";custom.FlatRegions.Add(new(0,200,8,8));
            string key=custom.BackgroundRules.Keys.First();custom.BackgroundRules[key].Label="My custom scenery";
            using(var editor=new Warp4D.UI.GameProfileEditorForm(custom,frame,false,"Kid Icarus.nes",new string('A',64),builtInProfile:profile))
            {
                editor.CreateControl();var before=editor.WorkingProfileForTest;string initial=JsonSerializer.Serialize(before),notSaved=JsonSerializer.Serialize(editor.EditedProfile);
                Require(editor.HudProtectionUpgradeButtonForTest?.Text=="UPDATE HUD PROTECTION","Explicit user-facing HUD upgrade button");
                editor.UpdateBuiltInHudProtection();var after=editor.WorkingProfileForTest;
                Require(!after.FlatRegions.Contains(hud)&&after.FlatRegions.Contains(new(0,200,8,8))&&after.FlatSpriteRegions!.Contains(hud)&&after.Name==before.Name&&JsonSerializer.Serialize(after.BackgroundRules)==JsonSerializer.Serialize(before.BackgroundRules),"HUD upgrade preserves custom regions/name/artwork");
                Require(JsonSerializer.Serialize(editor.EditedProfile)==notSaved,"No automatic save/apply");
                string upgraded=JsonSerializer.Serialize(after);editor.UndoEdit();Require(JsonSerializer.Serialize(editor.WorkingProfileForTest)==initial,"Exact HUD upgrade undo");
                editor.RedoEdit();Require(JsonSerializer.Serialize(editor.WorkingProfileForTest)==upgraded,"Exact HUD upgrade redo");
                editor.ShowWithoutActivationForTest=true;editor.ShowInTaskbar=false;editor.StartPosition=FormStartPosition.Manual;editor.Location=new(-4000,-4000);editor.Show();
                foreach(var size in new[]{new Size(1480,860),new Size(1150,720)})
                {
                    editor.Size=size;editor.PerformLayout();Application.DoEvents();
                    var button=editor.HudProtectionUpgradeButtonForTest!;
                    for(Control? ancestor=button.Parent;ancestor is not null;ancestor=ancestor.Parent)
                        Require(ancestor.ClientRectangle.Contains(ancestor.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))),"HUD upgrade button visible within every ancestor");
                    using Bitmap image=new(editor.Width,editor.Height);editor.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(output,$"editor-{size.Width}.png"));
                    for(Control? ancestor=button.Parent;ancestor is not null;ancestor=ancestor.Parent)
                        Require(ancestor.ClientRectangle.Contains(ancestor.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))),"HUD upgrade button remains visible after paint/layout");
                }
                editor.Hide();
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,ExactNativeHudOpaquePixels=hudPixels,frame.ScrollY,frame.ScrollX,NewBricks=bricks.Select(o=>new{o.Label,o.Bounds}),FlatHudSprites=flat.Select(o=>new{o.Bounds,o.IdentityKey}),OldScenery=oldScene.Objects.Count(o=>o.SortOrder<20),NewScenery=scene.Objects.Count(o=>o.SortOrder<20),OptInEditorUpgrade=true,CustomSettingsPreserved=true,UndoRedo=true,AutomaticSave=false,Format=profile.FormatVersion},new JsonSerializerOptions{WriteIndented=true}));return 0;
            void Save(string name){using Bitmap bitmap=new(renderer.Width,renderer.Height);renderer.DrawToBitmap(bitmap,renderer.ClientRectangle);bitmap.Save(Path.Combine(output,name+".png"));}
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
