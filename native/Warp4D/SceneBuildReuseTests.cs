using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;
using Warp4D.UI;

namespace Warp4D;

// Explicit offline diagnostic exercising the same scene-build path as the worker.
internal static class SceneBuildReuseTests
{
    internal static int Run(string output,string framePath,string profilePath,string id)
    {
        Directory.CreateDirectory(output);
        string? previous=Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        try
        {
            var frame=CartridgeViewport.NormalizeCapture(id,JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(framePath))!);
            var game=GameRecognitionProfileStore.ReadFromFile(profilePath);
            if(frame.CaptureScanline!=96||frame.NativeScreenSequence!=frame.Sequence||frame.NativeScreenPixels?.Length!=61440||!game.IsActive(frame))
                throw new InvalidDataException("Coherent active native fixture required.");
            using MainForm main=new();
            var projection=ProjectionProfile.CreateDefault();
            main.SetCaptureProfilesForTest(projection,game);
            int builds=0,skips=0;long allocated=0;double elapsed=0;string? digest=null;
            for(int attempt=0;attempt<80;attempt++)
            {
                long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                using var scene=main.BuildCapturedFrameForTest(frame,0);
                elapsed+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocated+=GC.GetAllocatedBytesForCurrentThread()-bytes;
                if(scene is null){skips++;continue;}
                builds++;string current=Digest(scene);
                digest??=current;if(current!=digest)throw new InvalidDataException("Repeated scene content changed.");
            }
            bool repeatedPass=builds==1&&skips==79;
            File.WriteAllText(Path.Combine(output,"repeated-frame.json"),JsonSerializer.Serialize(new{Passed=repeatedPass,Requests=80,Builds=builds,Skipped=skips,BuildMilliseconds=elapsed,ManagedBuildBytes=allocated,SceneDigest=digest,Scope="Offline repeated coherent publication; build cost only, not sustained native gameplay FPS or GPU display acceptance"},new JsonSerializerOptions{WriteIndented=true}));
            if(!repeatedPass)throw new InvalidDataException("Repeated coherent frame must build once, not80 times.");
            int cases=1;
            var next=frame with{Sequence=frame.Sequence+1,NativeScreenSequence=frame.Sequence+1};
            Check(next,0,true);Check(next,0,false);cases++;
            int generation=main.AdvanceCaptureGenerationForTest();
            Check(next,generation,true);Check(next,generation,false);cases++;
            game=game.Clone();main.SetCaptureProfilesForTest(projection,game);
            Check(next,generation,true);Check(next,generation,false);cases++;
            projection=projection.Clone();main.SetCaptureProfilesForTest(projection,game);
            Check(next,generation,true);Check(next,generation,false);cases++;
            Check(next,0,false);Check(next,generation,false);cases++;
            var unpaired=next with{CaptureScanline=null,NativeScreenSequence=null};
            Check(unpaired,generation,true);Check(unpaired,generation,true);
            Check(next,generation,true);Check(next,generation,false);cases++;
            var edited=game.Clone();edited.Name="Paused edit diagnostic";edited.BackgroundRules.Clear();
            main.SetCaptureProfilesForTest(projection,edited);
            using(var changed=main.BuildCapturedFrameForTest(next,generation))
                if(changed is null||Digest(changed)==digest||changed.RecognitionProfileName!=edited.Name)
                    throw new InvalidDataException("Actual profile edits must apply without native-frame advancement.");
            using(var duplicate=main.BuildCapturedFrameForTest(next,generation))
                if(duplicate is not null)throw new InvalidDataException("Edited scene must also be reusable.");
            main.SetCaptureProfilesForTest(projection,game);
            Check(next,generation,true);Check(next,generation,false);cases++;
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases,RepeatedRequests=80,Builds=builds,Skipped=skips,BuildMilliseconds=elapsed,ManagedBuildBytes=allocated,SceneDigest=digest,NewSequenceRebuilds=true,GenerationRebuilds=true,GameProfileReferenceRebuilds=true,ProjectionProfileReferenceRebuilds=true,RetiredGenerationDoesNotInvalidateCurrent=true,UnpairedCapturesNotDeduplicated=true,ChangedGameProfileAppliedWithoutNewNativeFrame=true,NativeDisplayVerified=false,WholeGameVerified=false,SustainedPerformanceClaim=false},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
            void Check(NesFrame input,int currentGeneration,bool expectedBuild)
            {
                using var scene=main.BuildCapturedFrameForTest(input,currentGeneration);
                if((scene is not null)!=expectedBuild)throw new InvalidDataException("Scene build invalidation mismatch.");
                if(scene is not null&&Digest(scene)!=digest)throw new InvalidDataException("Scene content differs after invalidation.");
            }
        }
        catch(Exception exception){File.WriteAllText(Path.Combine(output,"error.txt"),exception.ToString());return 1;}
        finally{Environment.SetEnvironmentVariable("WARP4D_HOME",previous);}
    }
    private static string Digest(SmbScene scene)
    {
        var value=new StringBuilder(ImagePixels.Read(scene.Background).Key);
        foreach(var item in scene.Objects)
            value.Append('|').Append(item.Kind).Append(':').Append(item.Bounds).Append(':').Append(item.ProjectionEnabled).Append(':').Append(item.SortOrder).Append(':').Append(item.PresentationKey).Append(':').Append(ImagePixels.Read(item.Image).Key);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }
}
