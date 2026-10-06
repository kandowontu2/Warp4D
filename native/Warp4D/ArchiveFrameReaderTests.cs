using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Warp4D.Emulation;

namespace Warp4D;

internal static class ArchiveFrameReaderTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var samples=JsonSerializer.Deserialize<ProfileCoverageTests.Sample[]>(File.ReadAllText(manifest))!;
            using ArchiveFrameReader reader=new();int exact=0,rejected=0;
            foreach(var sample in samples)
            {
                var current=reader.Read(sample.Frame,sample.Archive,sample.ArchiveSHA256,sample.FrameSHA256);
                string text;
                if(sample.Archive is null)text=File.ReadAllText(sample.Frame);
                else
                {
                    using var zip=ZipFile.OpenRead(sample.Archive);
                    using var reference=new StreamReader(zip.GetEntry(sample.Frame)!.Open());text=reference.ReadToEnd();
                }
                var prior=JsonSerializer.Deserialize<NesFrame>(text)!;
                if(!SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(prior)).SequenceEqual(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(current))))
                    throw new InvalidDataException("Stream/text frame content differs.");
                exact++;
            }
            var archived=samples.First(s=>s.Archive is not null);
            void Reject(Action action){try{action();}catch(InvalidDataException){rejected++;return;}throw new InvalidOperationException("Negative reader control accepted.");}
            Reject(()=>reader.Read("artifacts/coverage/../escape.frame.json",archived.Archive,archived.ArchiveSHA256,archived.FrameSHA256));
            Reject(()=>reader.Read(archived.Frame,archived.Archive,null,archived.FrameSHA256));
            Reject(()=>reader.Read(archived.Frame,archived.Archive,archived.ArchiveSHA256,new string('0',64)));
            Reject(()=>reader.Read(archived.Frame,archived.Archive,new string('0',64),archived.FrameSHA256));
            Reject(()=>reader.Read("artifacts/coverage/no-such-test-frame.frame.json",archived.Archive,archived.ArchiveSHA256,archived.FrameSHA256));
            var loose=samples.First(s=>s.Archive is null);
            Reject(()=>reader.Read(loose.Frame,null,null,new string('0',64)));
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,ExactFrames=exact,NegativeControls=rejected,Scope="Exact all deserialized frame fields versus previous text reader; traversal/missing/conflicting/archive-entry/loose hashes reject. Two fixtures, not every corrupt archive or coverage claim."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
