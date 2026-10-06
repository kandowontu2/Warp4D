using System.Text.Json;
using Warp4D.Profiles;
using Warp4D.UI;

namespace Warp4D;

internal static class PlayerTrackingEditorTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var sample=JsonSerializer.Deserialize<ProfileCoverageTests.Sample[]>(File.ReadAllText(manifest))!.First();
            if(sample.Id is not ("metroid" or "castlevania"))throw new InvalidDataException("Explicit native player editor fixture required.");
            using ArchiveFrameReader reader=new();
            var frame=reader.Read(sample.Frame,sample.Archive,sample.ArchiveSHA256,sample.FrameSHA256);
            var builtIn=GameRecognitionProfileStore.ReadFromFile(sample.Profile);
            string source=JsonSerializer.Serialize(builtIn);
            int expectedFormat=sample.Id=="castlevania"?21:17;
            var old=builtIn.Clone();old.PlayerTracking!.BodyTilesByAnimation=null;old.PlayerTracking.BodyAssembly=null;old.FormatVersion=sample.Id=="castlevania"?20:16;
            old.Name="My custom profile";old.PlayerLabel="My player";old.PlayerOverlaysFlatHud=false;old.SeparateMetatiles=false;
            var key=old.BackgroundRules.Keys.First();old.BackgroundRules[key].Label="My custom scenery";
            old.BackgroundRules.Remove(old.BackgroundRules.Keys.Last());
            using GameProfileEditorForm editor=new(old,frame,false,sample.Id+".nes",new string('A',64),builtInProfile:builtIn);
            editor.CreateControl();editor.PerformLayout();
            var before=editor.WorkingProfileForTest;
            string unchanged=OtherSettings(before),initialEdited=JsonSerializer.Serialize(editor.EditedProfile);
            if(editor.PlayerTrackingUpgradeButtonForTest is not {} button||button.Text!="UPDATE PLAYER TRACKING")throw new InvalidOperationException("Visible tracking upgrade control required.");
            editor.UpdateBuiltInPlayerTracking();
            var after=editor.WorkingProfileForTest;
            if(OtherSettings(after)!=unchanged||after.FormatVersion!=expectedFormat||after.PlayerLabel!=builtIn.PlayerLabel||after.PlayerOverlaysFlatHud!=builtIn.PlayerOverlaysFlatHud||
               JsonSerializer.Serialize(after.PlayerTracking)!=JsonSerializer.Serialize(builtIn.PlayerTracking))throw new InvalidOperationException("Only player tracking, label and HUD policy may change.");
            if(JsonSerializer.Serialize(editor.EditedProfile)!=initialEdited)throw new InvalidOperationException("Upgrade must not save/apply automatically.");
            editor.UndoEdit();if(JsonSerializer.Serialize(editor.WorkingProfileForTest)!=JsonSerializer.Serialize(before))throw new InvalidOperationException("Undo must restore exact custom profile.");
            editor.RedoEdit();if(JsonSerializer.Serialize(editor.WorkingProfileForTest)!=JsonSerializer.Serialize(after))throw new InvalidOperationException("Redo must restore exact upgrade.");
            string exported=Path.Combine(output,"custom-profile.warp4d-game.json");GameRecognitionProfileStore.WriteToFile(exported,after);
            if(JsonSerializer.Serialize(GameRecognitionProfileStore.ReadFromFile(exported))!=JsonSerializer.Serialize(after))throw new InvalidOperationException("Saved metadata round-trip required.");
            if(JsonSerializer.Serialize(builtIn)!=source)throw new InvalidOperationException("Built-in source mutated.");
            foreach(var size in new[]{new Size(1480,860),new Size(1150,720)})
            {
                editor.ShowWithoutActivationForTest=true;editor.ShowInTaskbar=false;editor.StartPosition=FormStartPosition.Manual;editor.Location=new(-4000,-4000);
                if(!editor.Visible){editor.Show();Application.DoEvents();}
                editor.Size=size;editor.PerformLayout();
                if(button.Right>button.Parent!.ClientSize.Width||button.Parent.Right>button.Parent.Parent!.ClientSize.Width)throw new InvalidOperationException("Tracking control clipped at supported size.");
                var scroll=(Panel)editor.Controls.Find("RulePanelScrollHost",true).Single();
                var help=(Label)editor.Controls.Find("ProjectionHelp",true).Single();
                scroll.ScrollControlIntoView(help);Application.DoEvents();
                var visibleHelp=scroll.RectangleToClient(help.RectangleToScreen(help.ClientRectangle));
                if(!scroll.ClientRectangle.Contains(visibleHelp))throw new InvalidOperationException($"Lower projection instructions must be fully reachable at {size}: help={visibleHelp}, viewport={scroll.ClientRectangle}, scroll={scroll.AutoScrollPosition}, parent={help.Parent!.Bounds}.");
                for(Control? ancestor=scroll.Parent;ancestor is not null;ancestor=ancestor.Parent)
                {
                    var inAncestor=ancestor.RectangleToClient(help.RectangleToScreen(help.ClientRectangle));
                    if(!ancestor.ClientRectangle.Contains(inAncestor))throw new InvalidOperationException($"Lower help clipped by {ancestor.GetType().Name}: {inAncestor} outside {ancestor.ClientRectangle}.");
                }
                using Bitmap image=new(editor.Width,editor.Height);editor.DrawToBitmap(image,new(Point.Empty,image.Size));
                image.Save(Path.Combine(output,$"editor-{size.Width}.png"));
                scroll.AutoScrollPosition=Point.Empty;
            }
            editor.Hide();
            using GameProfileEditorForm generic=new(old,frame,false,"Other.nes",new string('B',64));
            string genericBefore=JsonSerializer.Serialize(generic.WorkingProfileForTest);generic.UpdateBuiltInPlayerTracking();
            if(generic.PlayerTrackingUpgradeButtonForTest is not null||JsonSerializer.Serialize(generic.WorkingProfileForTest)!=genericBefore)throw new InvalidOperationException("Unknown games must not receive built-in tracking.");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,OptIn=true,AutomaticSave=false,CustomSceneryPreserved=true,UndoRedo=true,ScrollableHelpReachable=true,HelpInsideAllAncestors=true,Format=expectedFormat,PoseDefinitions=after.PlayerTracking!.BodyAssembly?.Poses.Count??after.PlayerTracking.BodyTilesByAnimation!.Count,SupportedWidths=new[]{1480,1150},Scope="Editor control/layout and opt-in/undo/export semantics; not physical desktop mouse acceptance."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static string OtherSettings(GameRecognitionProfile profile)
    {
        var copy=profile.Clone();copy.PlayerTracking=null;copy.PlayerLabel="";copy.PlayerOverlaysFlatHud=false;copy.FormatVersion=16;
        return JsonSerializer.Serialize(copy);
    }
}
