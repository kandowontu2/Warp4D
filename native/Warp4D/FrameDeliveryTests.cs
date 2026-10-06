using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.UI;

namespace Warp4D;

internal static class FrameDeliveryTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        string? previous=Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        try
        {
            using MainForm main=new();
            _=main.Handle; // Own hidden handle; no ROM, OS focus or user window.
            int[] red=Enumerable.Repeat(Color.Red.ToArgb(),61440).ToArray();
            int[] blue=Enumerable.Repeat(Color.Blue.ToArgb(),61440).ToArray();
            main.PublishCapturedSceneForTest(Scene(1),Frame(1,red),Frame(2,blue));
            Application.DoEvents();
            Assert(1,red,"capture completed before newer native publication");
            int before=main.SceneDeliveryCountForTest;
            for(int sequence=2;sequence<=81;sequence++)
            {
                int[] pixels=sequence%2==0?blue:red,wrong=sequence%2==0?red:blue;
                main.PublishCapturedSceneForTest(Scene(sequence),Frame(sequence,pixels),Frame(sequence+1,wrong));
            }
            Application.DoEvents();
            Assert(81,red,"coalesced scenes retain their own native pixels");
            if(main.SceneDeliveryCountForTest!=before+1)throw new InvalidDataException("Only newest queued scene must be delivered.");
            main.PublishCapturedSceneForTest(Scene(80),Frame(80,blue),Frame(82,blue));
            Application.DoEvents();
            Assert(81,red,"old scene cannot replace completed scene or native pixels");
            main.PublishSceneForTest(Scene(82));Application.DoEvents();
            if(main.PublishedSequenceForTest!=82||main.DeliveredNativePixelsForTest is not null)
                throw new InvalidDataException("Scene without native pixels must not borrow unrelated latest pixels.");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=4,QueuedCaptures=80,Scope="Deterministic capture/publication interleaving; paired scene pixels, newest-only queue, old-frame rejection and absent native payload",RomRequired=false,NativeDisplayVerified=false},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
            void Assert(long sequence,int[] pixels,string description)
            {
                if(main.PublishedSequenceForTest!=sequence||!ReferenceEquals(main.DeliveredNativePixelsForTest,pixels))
                    throw new InvalidDataException(description);
            }
            SmbScene Scene(long sequence)=>new(){Background=new Bitmap(256,240),Objects=[],Sequence=sequence,Location="Pairing test",ExactProfile=false,RecognitionProfileName="Test",ProjectionProfileName="Test"};
            NesFrame Frame(long sequence,int[] pixels)=>new(){Sequence=sequence,NativeScreenSequence=sequence,NativeScreenPixels=pixels,
                NametablePixels=[],Tiles=[],Attributes=[],Oam=[],Chr=[],Palette=[],Ram=[],ScrollSource="Synthetic publication interleaving"};
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{Environment.SetEnvironmentVariable("WARP4D_HOME",previous);}
    }
}
