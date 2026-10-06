using System.Security.Cryptography;

namespace Warp4D.Profiles;

internal static class BuiltInFamiDashProfile
{
    // All nonconstant partitions of four palette indices (Bell(4)-1).
    // Canonical RGB fingerprints depend on which colors coincide, not their
    // actual hues. Generate these from ROM bits, never from held-out views.
    private static readonly byte[][] PalettePartitions = [
        [0,0,0,1],[0,0,1,0],[0,0,1,1],[0,0,1,2],
        [0,1,0,0],[0,1,0,1],[0,1,0,2],[0,1,1,0],
        [0,1,1,1],[0,1,1,2],[0,1,2,0],[0,1,2,1],
        [0,1,2,2],[0,1,2,3]
    ];
    // These payloads were verified against their matching debug symbols. Only
    // known builds use these RAM addresses/table offsets; filenames are ignored.
    private const string NormalPayload = "535438F6CD8F21ED4AD1EF011DCEC01346E1D699AD1FCC9D1A547928881703CB";
    private const string HugePayload = "E571D56F4E64C39783DEEC9BB48AFC9BD82CADB9406041FAC24ACECD3F2A38F3";
    internal static GameRecognitionProfile? Create(byte[] rom, string fileHash)
    {
        if (rom.Length < 16 || rom[0] != 'N' || rom[1] != 'E' || rom[2] != 'S' || rom[3] != 0x1a) return null;
        int trainer = (rom[6] & 4) != 0 ? 512 : 0;
        bool nes2 = (rom[7] & 12) == 8;
        int mapper = (rom[6] >> 4) | (rom[7] & 0xf0) | (nes2 ? (rom[8] & 15) << 8 : 0);
        if (mapper != 4 || nes2 && rom[9] != 0) return null;
        int length = rom[4] * 16384 + rom[5] * 8192;
        if (rom.Length < 16 + trainer + length) return null;
        string payload = Convert.ToHexString(SHA256.HashData(rom.AsSpan(16 + trainer, length)));
        bool huge = payload == HugePayload;
        if (!huge && payload != NormalPayload) return null;
        int table = (huge ? 2056208 : 483344) + trainer;
        int collisions = (huge ? 2087668 : 514591) + trainer;
        GameRecognitionProfile profile = GameRecognitionProfile.Create(huge ? "Famidash - Huge Man.nes" : "famidash.nes", fileHash);
        profile.Name = huge ? "FamiDash Huge Man · built-in" : "FamiDash · built-in";
        profile.GameplayStateAddress = 0x49e; profile.GameplayStateValue = 2;
        profile.TransparentBackdrop = true; profile.SeparateMetatiles = true;
        for (int index = 0; index < 256; index++)
        {
            int collision = rom[collisions + index];
            // Ground/ceiling stripes remain the unmodified gameplay reference.
            if (collision == 9) continue;
            byte tl = rom[table + index], tr = rom[table + 256 + index], bl = rom[table + 512 + index], br = rom[table + 768 + index];
            if ((tl | tr | bl | br) == 0) continue;
            byte palette = (byte)(rom[table + 1024 + index] & 3);
            MetatileSignature signature = new(palette, tl, bl, tr, br);
            bool hazard = collision is 1 or 2 or 3 or 4 or 8 or >= 0x28;
            string label = hazard ? "Spike / hazard" : collision == 0 ? "Decoration / orb" : "Block / platform";
            SceneObjectKind kind = hazard ? SceneObjectKind.Enemy : collision == 0 ? SceneObjectKind.Item : SceneObjectKind.Brick;
            if (!profile.BackgroundRules.TryGetValue(signature.Key, out BackgroundObjectRule? rule))
                profile.BackgroundRules[signature.Key] = rule = new() { Kind = kind.ToString(), Label = label };
            // MMC3 switches CHR in 1KB units, not necessarily on a contiguous
            // 4KB boundary. Include all mapped starts used by background art.
            int chrOffset = 16 + trainer + rom[4] * 16384;
            int required = (Math.Max(Math.Max(tl, bl), Math.Max(tr, br)) + 1) * 16;
            for (int bank = chrOffset; bank + required <= 16 + trainer + length; bank += 1024)
                rule.CaptureArtwork(Fingerprint(rom, bank, tl, bl, tr, br));
            // Verified GAMECHR/PARALLAXCHR layout of these exact payloads:
            // spike, block, slope/parallax and saw slots are independent 1KB
            // banks, not a sliding contiguous 4KB window. Enumerate only the
            // quarters this declared metatile uses; never learn from captures.
            int[] quarters = new[]{tl,bl,tr,br}.Select(t=>t>>6).Distinct().ToArray();
            HashSet<string> known = rule.ArtworkVariants.Select(v=>v.Fingerprint).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int[] mapped = new int[4];
            for(int phase=0;phase<2;phase++)
            {
                int[][] choices = [
                    new[]{0,2,4,58}.Select(b=>b+phase).ToArray(),
                    new[]{6,8,10,12,60,62}.Select(b=>b+phase).ToArray(),
                    new[]{16,64,66,88,90}.Select(b=>b+phase).Concat(Enumerable.Range(112,144)).ToArray(),
                    new[]{14+phase,111}
                ];
                Visit(0);
                void Visit(int depth)
                {
                    if(depth<quarters.Length)
                    {
                        int q=quarters[depth];
                        foreach(int b in choices[q]){mapped[q]=b;Visit(depth+1);}
                        return;
                    }
                    foreach(string fingerprint in MappedFingerprintVariants(rom,chrOffset,tl,bl,tr,br,mapped))
                    {
                        // This fresh ROM-derived rule already has its legacy
                        // primary fingerprint. The set proves uniqueness, so
                        // avoid another linear scan of the growing variant list.
                        if(known.Add(fingerprint))rule.ArtworkVariants.Add(new ArtworkVariant{Fingerprint=fingerprint});
                    }
                }
            }
        }
        // Attribute palettes can differ from the metatile table in the live
        // nametable (e.g. overlapping portal/terrain palette quadrants). The
        // ROM-derived tile shape and artwork still identify the object. Add
        // missing palette keys only for an unambiguous declared class/label;
        // retain every existing key and its artwork guard. No capture learning
        // and no general palette fallback for user-authored/other-game rules.
        foreach(var group in profile.BackgroundRules.ToArray().GroupBy(p=>p.Key[3..]))
        {
            var sources=group.Select(p=>p.Value).ToArray();
            if(sources.Select(r=>(r.Kind,r.Label)).Distinct().Count()!=1)continue;
            for(int palette=0;palette<4;palette++)
            {
                string key=$"P{palette}-{group.Key}";
                if(profile.BackgroundRules.ContainsKey(key))continue;
                var alias=sources[0].Clone();
                foreach(var source in sources.Skip(1))
                    foreach(var variant in source.ArtworkVariants)
                        alias.CaptureArtwork(variant.Fingerprint);
                profile.BackgroundRules.Add(key,alias);
            }
        }
        profile.Normalize(); return profile;
    }
    private static string[] MappedFingerprintVariants(byte[] rom,int bank,byte tl,byte bl,byte tr,byte br,int[] mapped)
    {
        // Decode once per layout. Color partitions only remap these indices;
        // re-decoding CHR for every palette variant would slow ROM loading.
        Span<byte> raw=stackalloc byte[256],canonical=stackalloc byte[256],ids=stackalloc byte[4];
        for(int y=0;y<16;y++)for(int x=0;x<16;x++)
        {
            int tile=y<8 ? x<8 ? tl:tr : x<8 ? bl:br;
            int offset=bank+mapped[tile>>6]*1024+(tile&63)*16+(y&7),bit=7-(x&7);
            raw[y*16+x]=(byte)(((rom[offset]>>bit)&1)|(((rom[offset+8]>>bit)&1)<<1));
        }
        string[] hashes=new string[PalettePartitions.Length];
        for(int p=0;p<PalettePartitions.Length;p++)
        {
            ids.Fill(255);byte next=0;
            for(int i=0;i<256;i++)
            {
                byte color=PalettePartitions[p][raw[i]];
                if(ids[color]==255)ids[color]=next++;
                canonical[i]=ids[color];
            }
            hashes[p]=Convert.ToHexString(SHA256.HashData(canonical));
        }
        return hashes;
    }
    internal static string Fingerprint(byte[] rom, int bank, byte tl, byte bl, byte tr, byte br,int[]? mappedBanks=null,bool fade=false)
    {
        Span<byte> pixels = stackalloc byte[256]; Span<byte> ids = stackalloc byte[4]; ids.Fill(255); byte next = 0;
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        {
            int tile = y < 8 ? x < 8 ? tl : tr : x < 8 ? bl : br;
            int offset = bank + (mappedBanks is null ? tile*16 : mappedBanks[tile>>6]*1024+(tile&63)*16) + (y & 7), bit = 7 - (x & 7);
            int color = ((rom[offset] >> bit) & 1) | (((rom[offset + 8] >> bit) & 1) << 1);
            // Native fades coalesce palette indices 0/1/2 to black while
            // index3 remains visible. Canonical IDs must model that collapse.
            if(fade)color=color==3?1:0;
            if (ids[color] == 255) ids[color] = next++;
            pixels[y * 16 + x] = ids[color];
        }
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
}
