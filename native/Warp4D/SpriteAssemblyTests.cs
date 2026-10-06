using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class SpriteAssemblyTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var samples=JsonSerializer.Deserialize<ProfileCoverageTests.Sample[]>(File.ReadAllText(manifest))!;
            using ArchiveFrameReader reader=new();int cases=0;
            foreach(var sample in samples)
            {
                var frame=reader.Read(sample.Frame,sample.Archive,sample.ArchiveSHA256,sample.FrameSHA256);
                var profile=GameRecognitionProfileStore.ReadFromFile(sample.Profile);
                var assembly=profile.SpriteAssemblies!.Single();
                int[] slots=assembly.Match(frame).Single();
                byte[] shuffled=new byte[256];
                for(int i=0;i<64;i++)Array.Copy(frame.Oam,i*4,shuffled,(63-i)*4,4);
                var expected=slots.Select(i=>63-i).Order().ToArray();
                if(!assembly.Match(frame with{Oam=shuffled}).Single().SequenceEqual(expected))throw new Exception("Shuffled ownership differs.");
                byte[] missing=(byte[])frame.Oam.Clone();missing[slots[0]*4]=244;
                if(assembly.Match(frame with{Oam=missing}).Any())throw new Exception("Incomplete pose claimed.");
                int unused=Enumerable.Range(0,64).First(i=>frame.Oam[i*4]>=239);
                byte[] duplicate=(byte[])frame.Oam.Clone();Array.Copy(frame.Oam,slots[0]*4,duplicate,unused*4,4);
                if(assembly.Match(frame with{Oam=duplicate}).Any())throw new Exception("Ambiguous duplicate claimed.");
                byte[] wrong=(byte[])frame.Ram.Clone();wrong[40]=2;
                if(assembly.Match(frame with{Ram=wrong}).Any()||assembly.Match(frame with{Ram=[]}).Any())throw new Exception("State gate failed.");
                var clone=profile.Clone();clone.SpriteAssemblies![0].Poses[0][0].Tile^=1;
                if(clone.SpriteAssemblies[0].Poses[0][0].Tile==assembly.Poses[0][0].Tile)throw new Exception("Clone shares mutable layout.");
                var saved=JsonSerializer.Deserialize<GameRecognitionProfile>(JsonSerializer.Serialize(profile))!;saved.Normalize();
                if(saved.FormatVersion!=(profile.PlayerTracking?.BodyAssembly is not null?21:20)||!saved.SpriteAssemblies![0].Match(frame).Single().SequenceEqual(slots))throw new Exception("Import lost ownership.");
                cases++;
            }
            // Invalid layouts must be rejected, not clipped into partial owners.
            foreach(var invalid in new[]{new SpriteAssembly{Poses=[]},new SpriteAssembly{Poses=[[new(){Tile=1},new(){Tile=1}]]},new SpriteAssembly{Poses=[[new(){Tile=300},new(){Tile=2}]]}})
            {
                bool rejected=false;try{invalid.Validate();}catch(InvalidDataException){rejected=true;}
                if(!rejected)throw new Exception("Invalid assembly accepted.");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Cases=cases,ChecksPerCase=7,InvalidLayouts=3,reader.ArchivedReads},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
