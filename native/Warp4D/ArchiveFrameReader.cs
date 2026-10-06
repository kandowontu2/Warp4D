using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Warp4D.Emulation;

namespace Warp4D;

// Offline diagnostic input only. No extraction, ROM loading or startup hook.
internal sealed class ArchiveFrameReader : IDisposable
{
    private sealed record OpenArchive(ZipArchive Zip, string Hash, Dictionary<string,(long Length,string Hash)> Index);
    private readonly Dictionary<string,OpenArchive> _archives=new(StringComparer.OrdinalIgnoreCase);
    internal int ArchiveCount=>_archives.Count;
    internal long ArchivedReads {get;private set;}
    internal NesFrame Read(string framePath,string? archivePath,string? archiveHash,string? frameHash)
    {
        if(string.IsNullOrEmpty(archivePath))
        {
            if(!string.IsNullOrEmpty(archiveHash))throw new InvalidDataException("Archive hash without archive.");
            if(!string.IsNullOrEmpty(frameHash))
            {
                using var bytes=File.OpenRead(framePath);
                if(Convert.ToHexString(SHA256.HashData(bytes))!=frameHash)throw new InvalidDataException("Loose frame hash differs.");
            }
            using var input=File.OpenRead(framePath);
            return JsonSerializer.Deserialize<NesFrame>(input)??throw new InvalidDataException("Null frame.");
        }
        if(archiveHash is null||frameHash is null||archiveHash.Length!=64||frameHash.Length!=64)
            throw new InvalidDataException("Explicit archive/frame hashes required.");
        if(!framePath.StartsWith("artifacts/coverage/",StringComparison.Ordinal)||framePath.Contains('\\')||
           framePath.Split('/').Any(p=>p is "" or "." or ".."))throw new InvalidDataException("Unsafe archived frame path.");
        string archive=Path.GetFullPath(archivePath);
        if(!_archives.TryGetValue(archive,out var opened))
        {
            using(var bytes=File.OpenRead(archive))
                if(Convert.ToHexString(SHA256.HashData(bytes))!=archiveHash)throw new InvalidDataException("Archive hash differs.");
            var zip=ZipFile.OpenRead(archive);
            try
            {
                var index=new Dictionary<string,(long,string)>(StringComparer.Ordinal);
                using var reader=new StreamReader((zip.GetEntry("__coverage_archive_index.tsv")??throw new InvalidDataException("Archive index missing.")).Open());
                while(reader.ReadLine() is string row)
                {
                    var fields=row.Split('\t');
                    if(fields.Length!=3||!long.TryParse(fields[1],out long length)||length<0||fields[2].Length!=64||
                       !index.TryAdd(fields[0],(length,fields[2])))throw new InvalidDataException("Invalid/duplicate archive index entry.");
                }
                opened=new(zip,archiveHash,index);_archives.Add(archive,opened);
            }
            catch{zip.Dispose();throw;}
        }
        if(opened.Hash!=archiveHash)throw new InvalidDataException("Conflicting archive hash.");
        var entry=opened.Zip.GetEntry(framePath)??throw new InvalidDataException("Archived frame missing: "+framePath);
        if(entry.Length>16*1024*1024||!opened.Index.TryGetValue(framePath,out var expected)||
           expected.Length!=entry.Length||expected.Hash!=frameHash)throw new InvalidDataException("Frame/index metadata mismatch.");
        using(var bytes=entry.Open())if(Convert.ToHexString(SHA256.HashData(bytes))!=frameHash)throw new InvalidDataException("Archived frame hash differs.");
        using var stream=entry.Open();
        var frame=JsonSerializer.Deserialize<NesFrame>(stream)??throw new InvalidDataException("Null archived frame.");
        ArchivedReads++;return frame;
    }
    public void Dispose(){foreach(var archive in _archives.Values)archive.Zip.Dispose();_archives.Clear();}
}
