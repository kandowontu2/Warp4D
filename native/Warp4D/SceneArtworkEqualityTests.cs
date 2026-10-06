using System.Text.Json;
using System.Security.Cryptography;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class SceneArtworkEqualityTests
{
    internal static int Run(string output,string framePath,string profilePath,string cartridgeId,bool combined=false,bool runBackdrop=false,bool backdropCache=false)
    {
        Directory.CreateDirectory(output);
        try
        {
            var frame=CartridgeViewport.NormalizeCapture(cartridgeId,JsonSerializer.Deserialize<NesFrame>(File.ReadAllText(framePath))!);
            var profile=GameRecognitionProfileStore.ReadFromFile(profilePath);
            if(!profile.IsActive(frame))throw new InvalidDataException("Active native fixture required.");
            using SmbScene original=new SmbProfile{UseFusedBackdropVisibilityForTest=combined,UseCombinedArtworkExtractionForTest=runBackdrop||backdropCache,UseBackdropCacheForTest=false}.Build(frame,cartridgeId=="smb",null,profile);
            SmbProfile normal=new(){UseRunBackdropCountingForTest=runBackdrop,UseBackdropCacheForTest=backdropCache};
            if(backdropCache){using var warmup=normal.Build(frame,cartridgeId=="smb",null,profile);}
            if(!normal.UseFusedBackdropVisibilityForTest)throw new InvalidDataException("Normal production path must enable fused visibility.");
            if(combined&&!normal.UseCombinedArtworkExtractionForTest)throw new InvalidDataException("Normal production path must enable combined artwork.");
            using SmbScene fused=normal.Build(frame,cartridgeId=="smb",null,profile);
            if(backdropCache&&JsonSerializer.SerializeToElement(normal.BackdropCacheStatsForTest).GetProperty("Hits").GetInt64()<1)
                throw new InvalidDataException("Actual backdrop cache hit required.");
            if(original.Objects.Count<50||original.Objects.Count!=fused.Objects.Count)throw new InvalidDataException("Missing/changed objects.");
            long pixels=0;Compare(original.Background,fused.Background);
            if(!original.FlatRegions.SequenceEqual(fused.FlatRegions)||original.Sequence!=fused.Sequence)throw new InvalidDataException("Scene state changed.");
            for(int i=0;i<original.Objects.Count;i++)
            {
                var a=original.Objects[i];var b=fused.Objects[i];
                if(a.Kind!=b.Kind||a.Label!=b.Label||a.IdentityKey!=b.IdentityKey||a.Bounds!=b.Bounds||a.Depth!=b.Depth||a.Accent!=b.Accent||a.ProjectionEnabled!=b.ProjectionEnabled||a.SortOrder!=b.SortOrder)throw new InvalidDataException($"Object metadata/order changed: {i}");
                Compare(a.Image,b.Image);
                if(!a.PixelGeometry.Runs.SequenceEqual(b.PixelGeometry.Runs)||!a.PixelGeometry.Boundary.SequenceEqual(b.PixelGeometry.Boundary))throw new InvalidDataException("Object topology changed.");
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,CombinedArtwork=combined,RunBackdrop=runBackdrop,BackdropCache=backdropCache,CacheStats=normal.BackdropCacheStatsForTest,FrameSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(framePath))),ProfileSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(profilePath))),Objects=original.Objects.Count,ExactArgbPixels=pixels,Scope="One active captured native scene: exact flat background/object ARGB, order/metadata/flat regions and pixel runs/boundaries, old versus candidate. Not full game coverage or physical display."},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
            void Compare(Bitmap a,Bitmap b)
            {
                if(a.Size!=b.Size)throw new InvalidDataException("Image dimensions changed.");
                for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)
                    if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())throw new InvalidDataException($"ARGB changed {x},{y}");
                pixels+=(long)a.Width*a.Height;
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
