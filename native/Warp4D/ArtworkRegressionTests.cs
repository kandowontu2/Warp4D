using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class ArtworkRegressionTests
{
    internal sealed class Case
    {
        public string Id {get;set;}="";
        public string Area {get;set;}="";
        public string Frame {get;set;}="";
        public string Archive {get;set;}="";
        public string ArchiveSHA256 {get;set;}="";
        public string FrameSHA256 {get;set;}="";
        public string? Profile {get;set;}
        public string? Rom {get;set;}
        public string? RomSHA256 {get;set;}
        public int MinimumScenery {get;set;}=1;
        public int? MaximumScenery {get;set;}
        public int? MaximumObjects {get;set;}
        public bool DerivedUniformFade {get;set;}
        public ProfileCoverageTests.Sample? Guards {get;set;}
    }
    internal static int Run(string manifest,string output,bool spritePreflight=false,bool requireDefault=false)
    {
        Directory.CreateDirectory(output);
        Dictionary<string,ZipArchive> archives=[];
        List<object> results=[];
        try
        {
            Case[] cases=JsonSerializer.Deserialize<Case[]>(File.ReadAllText(manifest))??[];
            if(cases.Length==0)throw new InvalidDataException("Cases required.");
            long pixels=0;
            foreach(Case sample in cases)
            {
                if(!sample.Frame.StartsWith("artifacts/coverage/",StringComparison.Ordinal)||sample.Frame.Split('/').Any(s=>s is ".." or "." or ""))throw new InvalidDataException("Unsafe archive entry.");
                if(!archives.TryGetValue(sample.Archive,out ZipArchive? archive))
                {
                    using FileStream archiveBytes=File.OpenRead(sample.Archive);
                    if(Convert.ToHexString(SHA256.HashData(archiveBytes))!=sample.ArchiveSHA256)throw new InvalidDataException("Archive hash differs.");
                    archive=ZipFile.OpenRead(sample.Archive);archives.Add(sample.Archive,archive);
                }
                ZipArchiveEntry entry=archive.GetEntry(sample.Frame)??throw new InvalidDataException("Missing archived frame: "+sample.Frame);
                using(Stream bytes=entry.Open())if(Convert.ToHexString(SHA256.HashData(bytes))!=sample.FrameSHA256)throw new InvalidDataException("Frame hash differs.");
                NesFrame frame;
                using(Stream bytes=entry.Open())frame=CartridgeViewport.NormalizeCapture(sample.Id,JsonSerializer.Deserialize<NesFrame>(bytes)!);
                ValidateGuards(frame,sample.Guards);
                if(sample.DerivedUniformFade)
                {
                    // Explicit negative control, not a claimed native capture:
                    // stale CHR/OAM/world metadata with uniformly faded video.
                    int rgb=Warp4D.Rendering.NesPalette.Get(0).ToArgb();
                    frame=frame with{Palette=new byte[32],NativeScreenPixels=Enumerable.Repeat(rgb,61440).ToArray(),NativeScreenSequence=frame.Sequence,CaptureScanline=96};
                    if(!frame.IsUniformPairedFade())throw new InvalidDataException("Derived fade control did not construct the intended gate.");
                }
                GameRecognitionProfile? profile=null;string? profileHash=null;
                if(sample.Rom is not null)
                {
                    byte[] rom=File.ReadAllBytes(sample.Rom);
                    if(Convert.ToHexString(SHA256.HashData(rom))!=sample.RomSHA256)throw new InvalidDataException("ROM hash differs.");
                    profile=BuiltInFamiDashProfile.Create(rom,sample.RomSHA256!)??throw new InvalidDataException("Supported FamiDash payload required.");
                }
                else if(sample.Profile is not null)
                {
                    profileHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sample.Profile)));
                    profile=GameRecognitionProfileStore.ReadFromFile(sample.Profile);
                }
                using SmbScene old=new SmbProfile{UseCombinedArtworkExtractionForTest=spritePreflight,UseSpriteArtworkPreflightForTest=false}.Build(frame,sample.Id=="smb",null,profile);
                SmbProfile normal=new();
                if(!normal.UseCombinedArtworkExtractionForTest)throw new InvalidDataException("Production default disabled.");
                if(spritePreflight){if(requireDefault&&!normal.UseSpriteArtworkPreflightForTest)throw new InvalidDataException("Sprite preflight production default disabled.");normal.UseSpriteArtworkPreflightForTest=true;}
                using SmbScene current=normal.Build(frame,sample.Id=="smb",null,profile);
                int scenery=old.Objects.Count(o=>o.ProjectionEnabled&&o.IdentityKey.StartsWith("background:",StringComparison.Ordinal));
                if(scenery<sample.MinimumScenery||sample.MaximumScenery is int max&&scenery>max)throw new InvalidDataException($"{sample.Id}/{sample.Area}: wrong fixture exercise: {scenery} background objects.");
                if(sample.MaximumObjects is int maxObjects&&old.Objects.Count>maxObjects)throw new InvalidDataException("Suppressed frame emitted stale objects.");
                long casePixels=CompareScenes(old,current);pixels+=casePixels;
                if(sample.Profile is not null&&profileHash!=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sample.Profile))))throw new InvalidDataException("Profile changed while testing.");
                results.Add(new{sample.Id,sample.Area,sample.Frame,sample.FrameSHA256,sample.DerivedUniformFade,UniformFade=frame.IsUniformPairedFade(),sample.Profile,ProfileSHA256=profileHash,sample.RomSHA256,CellSize=profile?.CellSize??16,Active=profile?.IsActive(frame)??true,Objects=old.Objects.Count,Scenery=scenery,ExactArgbPixels=casePixels,frame.ScrollX,frame.ScrollY,frame.PpuMask,Coherent=frame.CaptureScanline==96&&frame.NativeScreenSequence==frame.Sequence});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Optimization=spritePreflight?"Sprite artwork preflight":"Combined artwork",ProductionDefaultRequired=requireDefault,Cases=cases.Length,ExactArgbPixels=pixels,Runs=results,Scope="Archived exact old/current scene regression only. Original profile data and capture state guards retained; no new scenery coverage/performance/physical display claim. No raw frames restored or changed."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{foreach(var archive in archives.Values)archive.Dispose();}
    }
    private static void ValidateGuards(NesFrame frame,ProfileCoverageTests.Sample? sample)
    {
        if(sample is null)return;
        foreach(var pair in sample.ExpectedRam)if(pair.Key<0||pair.Key>=frame.Ram.Length||frame.Ram[pair.Key]!=pair.Value)throw new InvalidDataException("Expected RAM differs.");
        if(sample.ExpectedWorkRamSize is int length&&frame.CalibrationWorkRam?.Length!=length)throw new InvalidDataException("Work RAM size differs.");
        foreach(var pair in sample.ExpectedWorkRam)if(frame.CalibrationWorkRam is null||pair.Key<0||pair.Key>=frame.CalibrationWorkRam.Length||frame.CalibrationWorkRam[pair.Key]!=pair.Value)throw new InvalidDataException("Expected work RAM differs.");
        if(sample.ExpectedPpuMask is byte mask&&frame.PpuMask!=mask)throw new InvalidDataException("PPU mask differs.");
        if(sample.ExpectedViewportX is int x&&frame.ScrollX!=x||sample.ExpectedViewportY is int y&&frame.ScrollY!=y)throw new InvalidDataException("Viewport differs.");
        if(sample.RequireCoherentNativeFrame&&(frame.CaptureScanline!=96||frame.NativeScreenSequence!=frame.Sequence||frame.NativeScreenPixels?.Length!=61440))throw new InvalidDataException("Coherent frame required.");
        if(sample.ExpectedChrSha256 is not null&&Convert.ToHexString(SHA256.HashData(frame.Chr.AsSpan(sample.ExpectedChrOffset,sample.ExpectedChrLength)))!=sample.ExpectedChrSha256)throw new InvalidDataException("CHR guard differs.");
    }
    private static long CompareScenes(SmbScene a,SmbScene b)
    {
        if(a.Objects.Count!=b.Objects.Count||!a.FlatRegions.SequenceEqual(b.FlatRegions)||a.PlayerOverlaysFlatHud!=b.PlayerOverlaysFlatHud||a.Sequence!=b.Sequence)throw new InvalidDataException("Scene state/order changed.");
        long pixels=Compare(a.Background,b.Background);
        for(int i=0;i<a.Objects.Count;i++)
        {
            var x=a.Objects[i];var y=b.Objects[i];
            if(x.Kind!=y.Kind||x.Label!=y.Label||x.IdentityKey!=y.IdentityKey||x.Bounds!=y.Bounds||x.Accent!=y.Accent||x.Depth!=y.Depth||x.ProjectionEnabled!=y.ProjectionEnabled||x.SortOrder!=y.SortOrder)throw new InvalidDataException("Object metadata/order differs.");
            pixels+=Compare(x.Image,y.Image);
            if(!x.PixelGeometry.Runs.SequenceEqual(y.PixelGeometry.Runs)||!x.PixelGeometry.Boundary.SequenceEqual(y.PixelGeometry.Boundary))throw new InvalidDataException("Topology differs.");
        }
        return pixels;
        static long Compare(Bitmap x,Bitmap y)
        {
            if(x.Size!=y.Size)throw new InvalidDataException("Bitmap dimensions differ.");
            for(int row=0;row<x.Height;row++)for(int col=0;col<x.Width;col++)if(x.GetPixel(col,row).ToArgb()!=y.GetPixel(col,row).ToArgb())throw new InvalidDataException("Exact bitmap ARGB differs.");
            return (long)x.Width*x.Height;
        }
    }
}
