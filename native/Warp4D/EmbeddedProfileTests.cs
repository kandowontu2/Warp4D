using System.Security.Cryptography;
using System.Text.Json;
using Warp4D.Profiles;

namespace Warp4D;

// Explicit offline diagnostic: prove the shipped resources equal the audited
// source without loading ROMs or depending on the development working directory.
internal static class EmbeddedProfileTests
{
    internal static int Run(string manifest,string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var expected=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(manifest))
                ?? throw new InvalidDataException("Missing resource hashes.");
            if(expected.Count!=BuiltInGameProfiles.Cartridges.Length)
                throw new InvalidDataException("All built-in cartridge profiles required.");
            List<object> profiles=[];
            foreach(var cartridge in BuiltInGameProfiles.Cartridges)
            {
                using var resource=typeof(BuiltInGameProfiles).Assembly.GetManifestResourceStream("Warp4D.Profiles.Data."+cartridge.Id+".json")
                    ?? throw new InvalidDataException("Missing embedded profile: "+cartridge.Id);
                string hash=Convert.ToHexString(SHA256.HashData(resource));
                if(!expected.TryGetValue(cartridge.Id,out string? wanted)||hash!=wanted)
                    throw new InvalidDataException("Embedded profile differs: "+cartridge.Id);
                resource.Position=0;
                var profile=JsonSerializer.Deserialize<GameRecognitionProfile>(resource)
                    ?? throw new InvalidDataException("Invalid profile: "+cartridge.Id);
                profile.Normalize();
                profiles.Add(new {cartridge.Id,SHA256=hash,Rules=profile.BackgroundRules.Count,
                    ArtworkVariants=profile.BackgroundRules.Values.Sum(r=>r.ArtworkVariants.Count)});
            }
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {Passed=true,Profiles=profiles,RomLoaded=false},new JsonSerializerOptions {WriteIndented=true}));
            return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
