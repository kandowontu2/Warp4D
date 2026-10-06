using System.Security.Cryptography;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

// Explicit developer input only. Coverage is measured per named area: seeing
// one recognized object does not establish support for the rest of a scene.
internal static class ProfileCoverageTests
{
    internal sealed class Sample
    {
        public string Id { get; set; } = "";
        public string Area { get; set; } = "";
        public string Frame { get; set; } = "";
        public string? Archive { get; set; }
        public string? ArchiveSHA256 { get; set; }
        public string? FrameSHA256 { get; set; }
        public string Profile { get; set; } = "";
        public double MinimumCoverage { get; set; } = .85;
        public Dictionary<int, byte> ExpectedRam { get; set; } = [];
        public int? ExpectedWorkRamSize { get; set; }
        public Dictionary<int, byte> ExpectedWorkRam { get; set; } = [];
        public string? ExpectedChrSha256 { get; set; }
        public int ExpectedChrOffset { get; set; }
        public int ExpectedChrLength { get; set; } = 8192;
        public bool Negative { get; set; }
        public bool RenderPreview { get; set; }
        // Explicit developer audit only; do not silently omit blank/protected
        // cells when diagnosing scenery that remains in the flat backdrop.
        public bool InspectExtraction { get; set; }
        public bool RequireCoherentNativeFrame { get; set; }
        public int? ExpectedViewportX { get; set; }
        public int? ExpectedViewportY { get; set; }
        public byte? ExpectedPpuMask { get; set; }
    }

    internal static int Run(string manifest, string output)
    {
        Directory.CreateDirectory(output);
        List<object> results = [];
        List<string> failures = [];
        Dictionary<string, GameRecognitionProfile> profiles = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> profileHashes = new(StringComparer.OrdinalIgnoreCase);
        using ArchiveFrameReader frameReader=new();
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var samples = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(manifest));
            if (samples is null || samples.Length == 0) throw new InvalidDataException("Coverage requires at least one named sample.");
            foreach (var sample in samples)
            {
                var frame = CartridgeViewport.NormalizeCapture(sample.Id, frameReader.Read(sample.Frame,sample.Archive,sample.ArchiveSHA256,sample.FrameSHA256));
                if ((sample.ExpectedViewportX is int expectedX && frame.ScrollX != expectedX) ||
                    (sample.ExpectedViewportY is int expectedY && frame.ScrollY != expectedY))
                    throw new InvalidDataException($"{sample.Area}: normalized viewport mismatch: {frame.ScrollX},{frame.ScrollY}");
                string profilePath = Path.GetFullPath(sample.Profile);
                if (!profiles.TryGetValue(profilePath, out var profile))
                {
                    string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(profilePath)));
                    profile = GameRecognitionProfileStore.ReadFromFile(profilePath);
                    if (hash != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(profilePath))))
                        throw new InvalidDataException("Profile changed while loading: " + profilePath);
                    profiles.Add(profilePath, profile);
                    profileHashes.Add(profilePath, hash);
                }
                if (sample.RequireCoherentNativeFrame &&
                    (frame.CaptureScanline != 96 || frame.NativeScreenSequence != frame.Sequence || frame.NativeScreenPixels?.Length != 256 * 240))
                    throw new InvalidDataException($"{sample.Area}: missing coherent playfield/native-video evidence");
                if (sample.ExpectedPpuMask is byte mask && frame.PpuMask != mask)
                    throw new InvalidDataException($"{sample.Area}: native PPU mask mismatch");
                if (sample.ExpectedWorkRam.Count > 0 && sample.ExpectedWorkRamSize is null)
                    throw new InvalidDataException($"{sample.Area}: work RAM assertions require an exact snapshot size");
                if (sample.ExpectedWorkRamSize is int workSize && (workSize < 1 || workSize > 8192 || frame.CalibrationWorkRam?.Length != workSize))
                    throw new InvalidDataException($"{sample.Area}: missing or incorrect cartridge work RAM evidence");
                foreach (var expected in sample.ExpectedWorkRam)
                    if (frame.CalibrationWorkRam is null || expected.Key < 0 || expected.Key >= frame.CalibrationWorkRam.Length || frame.CalibrationWorkRam[expected.Key] != expected.Value)
                        throw new InvalidDataException($"{sample.Area}: wrong game area/state at work RAM offset {expected.Key:X4}");
                if (!ProfileCalibration.MatchesChr(frame, sample.ExpectedChrSha256, sample.ExpectedChrOffset, sample.ExpectedChrLength))
                    throw new InvalidDataException($"{sample.Area}: wrong CHR artwork for named game area");
                foreach (var expected in sample.ExpectedRam)
                    if (expected.Key < 0 || expected.Key >= frame.Ram.Length || frame.Ram[expected.Key] != expected.Value)
                        throw new InvalidDataException($"{sample.Area}: wrong game area/state at RAM {expected.Key:X4}");
                int size = profile.CellSize, step = size / 8, eligible = 0, recognized = 0;
                int visibleEligible = 0, visibleRecognized = 0;
                string blank = Convert.ToHexString(SHA256.HashData(new byte[size * size]));
                Dictionary<string, int> unknown = [];
                List<object> unknownCells = [];
                for (int y = frame.ScrollY / size * step; y <= (frame.ScrollY + 239) / 8; y += step)
                    for (int x = frame.ScrollX / size * step; x <= (frame.ScrollX + 255) / 8; x += step)
                    {
                        Rectangle bounds = new(x * 8 - frame.ScrollX, y * 8 - frame.ScrollY, size, size);
                        if (!new Rectangle(0, 0, 256, 240).Contains(bounds) || !profile.Allows(bounds, frame)) continue;
                        if (MetatileVisualFingerprint.Read(frame, x, y, size) == blank) continue;
                        eligible++;
                        var signature = MetatileSignature.Read(frame, x, y, size);
                        bool matched = profile.Match(signature, frame, x, y) is not null;
                        bool visible = frame.NativeScreenPixels is null || MetatileVisualFingerprint.IsVisible(frame, x, y, size, profile.AllowMaskedLeftEdgeArtwork);
                        if (visible) { visibleEligible++; if (matched) visibleRecognized++; }
                        if (matched) recognized++;
                        else
                        {
                            unknown[signature.Key] = unknown.GetValueOrDefault(signature.Key) + 1;
                            if (unknownCells.Count < 64)
                                unknownCells.Add(new { signature.Key, WorldTileX = x, WorldTileY = y,
                                    ScreenX = bounds.X, ScreenY = bounds.Y, Visible = visible,
                                    Fingerprint = MetatileVisualFingerprint.Read(frame, x, y, size) });
                        }
                    }
                bool active = profile.IsActive(frame);
                double coverage = eligible == 0 ? 0 : (double)recognized / eligible;
                double visibleCoverage = visibleEligible == 0 ? 0 : (double)visibleRecognized / visibleEligible;
                bool passed = sample.Negative ? !active : active && coverage >= sample.MinimumCoverage && visibleCoverage >= sample.MinimumCoverage;
                bool hudPreserved = true;
                using var scene = new SmbProfile().Build(frame, false, null, profile);
                object? extraction = sample.InspectExtraction ? InspectExtraction(frame, profile, scene) : null;
                string rendererStatus = "Not requested";
                if (sample.RenderPreview)
                {
                    using var renderer = new Rendering.WarpRendererControl { Size = new(820, 780), UseGpu = true };
                    renderer.CreateControl(); renderer.SetScene(scene.Clone());
                    using Bitmap preview = new(820, 780);
                    renderer.DrawToBitmap(preview, renderer.ClientRectangle);
                    rendererStatus = renderer.RendererStatus;
                    if (!rendererStatus.StartsWith("GPU")) throw new InvalidOperationException("GPU preview required: " + rendererStatus);
                    // Different capture runs can reuse a basename. Retain a
                    // distinct preview for every checked sample instead of
                    // silently overwriting another area's visual evidence.
                    preview.Save(Path.Combine(output, $"{results.Count:D4}-" + Path.GetFileNameWithoutExtension(sample.Frame) + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                if (frame.NativeScreenPixels is not null)
                    foreach (Rectangle region in profile.FlatRegionsFor(frame))
                        for (int y = region.Top; y < region.Bottom; y++)
                            for (int x = region.Left; x < region.Right; x++)
                            {
                                int expected=frame.NativeScreenPixels[y*256+x];
                                // Independent HUD layer expectation: only an
                                // opaque, native-color-matching tracked body
                                // pixel is peeled out; every other pixel is
                                // still checked against exact native video.
                                if(profile.PlayerOverlaysFlatHud)
                                    foreach(var actor in scene.Objects.Where(o=>o.Kind==SceneObjectKind.Player && o.ProjectionEnabled && o.Bounds.Contains(x,y)))
                                    {
                                        var body=actor.Image.GetPixel(x-actor.Bounds.X,y-actor.Bounds.Y);
                                        if(body.A==0 || (body.ToArgb()&0xffffff)!=(expected&0xffffff))continue;
                                        int wx=(x+frame.ScrollX)&511,wy=((y+frame.ScrollY)%480+480)%480;
                                        expected=frame.NametablePixels[(wy>=240?2:0)+(wx>=256?1:0)][(wy%240)*256+(wx&255)]|unchecked((int)0xff000000);
                                        break;
                                    }
                                hudPreserved &= scene.Background.GetPixel(x,y).ToArgb()==expected;
                            }
                passed &= hudPreserved;
                if (!passed) failures.Add(sample.Id + ": " + sample.Area);
                results.Add(new
                {
                    sample.Id,
                    sample.Area,
                    sample.Frame,
                    sample.Archive,
                    sample.ArchiveSHA256,
                    sample.FrameSHA256,
                    sample.Negative,
                    Active = active,
                    EligibleCells = eligible,
                    RecognizedCells = recognized,
                    Coverage = coverage,
                    VisibleEligibleCells = visibleEligible,
                    VisibleRecognizedCells = visibleRecognized,
                    VisibleCoverage = visibleCoverage,
                    SceneryObjects = scene.Objects.Count(o => o.SortOrder < 20 && o.ProjectionEnabled),
                    Renderer = rendererStatus,
                    sample.MinimumCoverage,
                    HudPreserved = hudPreserved,
                    Passed = passed,
                    Extraction = extraction,
                    UnknownCells = unknownCells,
                    UnknownPatterns = unknown.OrderByDescending(p => p.Value).Select(p => new { Key = p.Key, Cells = p.Value })
                });
                if(results.Count%100==0)
                    File.WriteAllText(Path.Combine(output,"progress.json"),JsonSerializer.Serialize(new{Processed=results.Count,Total=samples.Length,Failures=failures.Count,ElapsedSeconds=elapsed.Elapsed.TotalSeconds,ArchivedReads=frameReader.ArchivedReads}));
            }
            // A run scores one immutable normalized profile per source file,
            // as live emulation does, rather than parsing it for every frame.
            // Bind the report to those exact files and reject mid-run edits.
            foreach (var source in profileHashes)
                if (source.Value != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.Key))))
                    throw new InvalidDataException("Profile changed during coverage: " + source.Key);
            File.WriteAllText(Path.Combine(output, "coverage.json"), JsonSerializer.Serialize(new
            {
                Scope = "Only the named captures below; not a whole-game completion claim.",
                ProfileSources = profileHashes.Select(source => new { Path = source.Key, Sha256 = source.Value }),
                ArchiveCount=frameReader.ArchiveCount,
                ArchivedReads=frameReader.ArchivedReads,
                Passed = failures.Count == 0,
                Failures = failures,
                Samples = results
            }, new JsonSerializerOptions { WriteIndented = true }));
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); return 1; }
    }

    private static object InspectExtraction(NesFrame frame, GameRecognitionProfile profile, SmbScene scene)
    {
        int size=profile.CellSize, step=size/8;
        string blank=Convert.ToHexString(SHA256.HashData(new byte[size*size]));
        List<object> cells=[];
        for(int y=frame.ScrollY/size*step;y<=(frame.ScrollY+239)/8;y+=step)
        for(int x=frame.ScrollX/size*step;x<=(frame.ScrollX+255)/8;x+=step)
        {
            Rectangle bounds=new(x*8-frame.ScrollX,y*8-frame.ScrollY,size,size);
            if(!bounds.IntersectsWith(new Rectangle(0,0,256,240)))continue;
            string fingerprint=MetatileVisualFingerprint.Read(frame,x,y,size);
            var signature=MetatileSignature.Read(frame,x,y,size);
            var rule=profile.Match(signature,frame,x,y);
            var objects=scene.Objects.Where(o=>o.SortOrder<20&&o.Bounds.IntersectsWith(bounds)).ToArray();
            cells.Add(new { ScreenX=bounds.X,ScreenY=bounds.Y,signature.Key,
                Fingerprint=fingerprint,Blank=fingerprint==blank,Allowed=profile.Allows(bounds,frame),
                FullyVisible=new Rectangle(0,0,256,240).Contains(bounds),
                Matched=rule is not null,Kind=rule?.Kind,rule?.Label,
                ExtractedObjects=objects.Length,ProjectedObjects=objects.Count(o=>o.ProjectionEnabled) });
        }
        using var geometry=new Rendering.WarpRendererControl { EnableStyleTransitions=false };
        float initialXW=geometry.AngleXWDegrees;
        List<object> extracted=[];
        foreach(var item in scene.Objects.Where(o=>o.SortOrder<20))
        {
            geometry.AngleXWDegrees=initialXW;
            var original=geometry.GeometrySurfacesForTest(item);
            geometry.AngleXWDegrees=initialXW+25;
            var rotated=geometry.GeometrySurfacesForTest(item);
            float displacement=0;
            if(original.Triangles.Count!=rotated.Triangles.Count)
                throw new InvalidDataException("Extraction audit pose changed mesh topology.");
            for(int i=0;i<original.Triangles.Count;i++)
            {
                var a=original.Triangles[i];var b=rotated.Triangles[i];
                displacement=Math.Max(displacement,Distance(a.A.Point,b.A.Point));
                displacement=Math.Max(displacement,Distance(a.B.Point,b.B.Point));
                displacement=Math.Max(displacement,Distance(a.C.Point,b.C.Point));
            }
            extracted.Add(new { Kind=item.Kind.ToString(),item.Label,item.Bounds,item.ProjectionEnabled,item.Depth,
                Triangles=original.Triangles.Count,
                TexturedLayers=original.Triangles.Where(t=>t.Texture is not null).Select(t=>t.LayerKey).Distinct().ToArray(),
                MaximumVertexMotionFor25DegreeXW=displacement });
        }
        return new { Cells=cells,Objects=extracted,
            GeometryAuditScope="Default full-detail Hyperprism at scale1; XW changed by25degrees. Mesh coordinates, not physical-display or performance evidence." };
        static float Distance(PointF a,PointF b)=>MathF.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
    }
}
