using System.Text.Json;
using Warp4D.Profiles;
using Warp4D.UI;

namespace Warp4D;

internal static class SpriteAssemblyEditorTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var sample=JsonSerializer.Deserialize<ProfileCoverageTests.Sample[]>(File.ReadAllText(manifest))!.First();
            if(sample.Id!="castlevania")throw new InvalidDataException("Explicit Castlevania fixture required.");
            using ArchiveFrameReader reader=new();
            var frame=reader.Read(sample.Frame,sample.Archive,sample.ArchiveSHA256,sample.FrameSHA256);
            var builtIn=GameRecognitionProfileStore.ReadFromFile(sample.Profile);
            string original=JsonSerializer.Serialize(builtIn);
            var custom=builtIn.Clone();custom.SpriteAssemblies=null;custom.PlayerTracking!.BodyAssembly=null;custom.FormatVersion=15;
            custom.Name="My custom castle";custom.PlayerLabel="My Simon";custom.SeparateMetatiles=false;
            custom.BackgroundRules[custom.BackgroundRules.Keys.First()].Label="My scenery";
            custom.BackgroundRules.Remove(custom.BackgroundRules.Keys.Last());
            using GameProfileEditorForm editor=new(custom,frame,false,"Castlevania.nes",new string('A',64),builtInProfile:builtIn);
            editor.CreateControl();editor.PerformLayout();
            var before=editor.WorkingProfileForTest;string noSave=JsonSerializer.Serialize(editor.EditedProfile);
            var button=editor.SpriteAssemblyUpgradeButtonForTest??throw new Exception("Upgrade button missing.");
            if(button.Text!="UPDATE SPRITE SHAPES"||!button.AccessibleDescription!.Contains("Save & Apply"))throw new Exception("Explicit upgrade instructions required.");
            editor.UpdateBuiltInSpriteAssemblies();var after=editor.WorkingProfileForTest;
            if(OtherSettings(before)!=OtherSettings(after)||after.FormatVersion!=20||
               JsonSerializer.Serialize(after.SpriteAssemblies)!=JsonSerializer.Serialize(builtIn.SpriteAssemblies))throw new Exception("Upgrade changed unrelated settings.");
            if(JsonSerializer.Serialize(editor.EditedProfile)!=noSave)throw new Exception("Upgrade saved automatically.");
            editor.UndoEdit();if(JsonSerializer.Serialize(editor.WorkingProfileForTest)!=JsonSerializer.Serialize(before))throw new Exception("Undo differs.");
            editor.RedoEdit();if(JsonSerializer.Serialize(editor.WorkingProfileForTest)!=JsonSerializer.Serialize(after))throw new Exception("Redo differs.");
            string file=Path.Combine(output,"custom-profile.warp4d-game.json");GameRecognitionProfileStore.WriteToFile(file,after);
            if(JsonSerializer.Serialize(GameRecognitionProfileStore.ReadFromFile(file))!=JsonSerializer.Serialize(after))throw new Exception("Export/import differs.");
            foreach(var size in new[]{new Size(1480,860),new Size(1150,720)})
            {
                editor.ShowWithoutActivationForTest=true;editor.ShowInTaskbar=false;editor.StartPosition=FormStartPosition.Manual;editor.Location=new(-4000,-4000);
                if(!editor.Visible){editor.Show();Application.DoEvents();}
                editor.Size=size;editor.PerformLayout();Application.DoEvents();
                for(Control? parent=button.Parent;parent is not null;parent=parent.Parent)
                    if(!parent.ClientRectangle.Contains(parent.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))))throw new Exception("Upgrade button clipped at "+size);
                using Bitmap image=new(editor.Width,editor.Height);editor.DrawToBitmap(image,new(Point.Empty,image.Size));image.Save(Path.Combine(output,$"editor-{size.Width}.png"));
            }
            editor.Hide();
            if(JsonSerializer.Serialize(builtIn)!=original)throw new Exception("Built-in source mutated.");
            using GameProfileEditorForm generic=new(custom,frame,false,"Other.nes",new string('B',64));
            string unknown=JsonSerializer.Serialize(generic.WorkingProfileForTest);generic.UpdateBuiltInSpriteAssemblies();
            if(generic.SpriteAssemblyUpgradeButtonForTest is not null||JsonSerializer.Serialize(generic.WorkingProfileForTest)!=unknown)throw new Exception("Unknown game upgrade must be unavailable.");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,OptIn=true,AutomaticSave=false,UnrelatedSettingsPreserved=true,UndoRedo=true,ExportImport=true,SupportedWidths=new[]{1480,1150},Format=20,Scope="Rendered editor/layout and semantics, not physical mouse/display acceptance."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
    private static string OtherSettings(GameRecognitionProfile profile)
    {
        var copy=profile.Clone();copy.SpriteAssemblies=null;copy.FormatVersion=15;return JsonSerializer.Serialize(copy);
    }
}
