using System.Drawing.Imaging;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;
using Warp4D.Rendering;

namespace Warp4D;

// Explicit-input developer diagnostics. Never invoked during ordinary startup.
internal static class ProfileCalibration
{
    internal sealed class Step
    {
        public string Name { get; set; } = "capture";
        public int Mask { get; set; }
        public int Milliseconds { get; set; } = 1000;
        public bool Capture { get; set; } = true;
        // Animation sweeps still save the complete frame/native pixels, but
        // can omit expensive PNG previews that are unnecessary for auditing.
        public bool RenderPreviews { get; set; } = true;
        // Explicit diagnostic only: retain new paired publications in memory,
        // then serialize after sampling. No pause or disk I/O between frames.
        public int? BurstFrames { get; set; }
        public Dictionary<int, byte> BurstStartRam { get; set; } = [];
        public bool Reset { get; set; }
        // Explicit diagnostic scripts only; slots live under isolated-data.
        public int? SaveStateSlot { get; set; }
        public int? LoadStateSlot { get; set; }
        // Diagnostic-only native controller steering. Never writes item,
        // equipment, character or room state, and never runs at startup.
        public int? MetroidPickupType { get; set; }
        // Opt-in inventory sweeps retain failed evidence but never accept it.
        public bool ContinueOnRejectedCapture { get; set; }
        public string? CaptureGroup { get; set; }
        public int? OutputIndex { get; set; }
        // Test-only level setup. Cannot address ROM, PPU or mapper registers.
        public Dictionary<int, byte> RamWrites { get; set; } = [];
        public Dictionary<int, byte> ExpectedRam { get; set; } = [];
        // Physical cartridge WorkRam offsets, never CPU/mapper/ROM addresses.
        // An exact allocation size is mandatory for these explicit diagnostics.
        public int? ExpectedWorkRamSize { get; set; }
        public Dictionary<int, byte> WorkRamWrites { get; set; } = [];
        public Dictionary<int, byte> ExpectedWorkRam { get; set; } = [];
        // RAM stage labels alone cannot prove the native loader changed CHR.
        public string? ExpectedChrSha256 { get; set; }
        public int ExpectedChrOffset { get; set; }
        public int ExpectedChrLength { get; set; } = 8192;
        // Optional diagnostics for RAM-backed level tables, never cartridge ROM.
        public bool CaptureCartridgeRam { get; set; }
        public int MinimumVisibleCells { get; set; }
        public int StabilityTimeoutMilliseconds { get; set; } = 4000;
        public bool WaitForExpectedRam { get; set; }
        // Explicit calibration only: use a supported cartridge's native door
        // table to approach a doorway, then let ordinary Up input enter it.
        // This never writes the room identifier or game mode.
        public int? IcarusDoorIndex { get; set; }
        public int? IcarusTrainingDodgeMilliseconds { get; set; }
        // Explicit stage-one stair calibration only; controller steering,
        // never RAM/coordinate writes or ordinary-startup behavior.
        public int? CastlevaniaStairTop { get; set; }
        public bool CastlevaniaHallStairFoot { get; set; }
        public bool CastlevaniaHallStairClimb { get; set; }
    }
    internal static int Run(string rom, string script, string output)
    {
        Directory.CreateDirectory(output);
        string? previous = Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME", Path.Combine(Path.GetFullPath(output), "isolated-data"));
        try
        {
            Step[] steps = JsonSerializer.Deserialize<Step[]>(File.ReadAllText(script))!;
            foreach (Step step in steps)
            {
                ValidateTrainingStep(step);
                if (step.BurstStartRam.Count > 0 && (step.BurstFrames is null || step.BurstStartRam.Count > 16 ||
                    step.BurstStartRam.Keys.Any(address => address < 0 || address > 0x7ff)))
                    throw new InvalidDataException("Burst start gate requires a burst and1..16 internal RAM addresses.");
                if (step.BurstFrames is int burst &&
                    (burst is < 1 or > 512 || !step.Capture || step.RenderPreviews ||
                     step.ExpectedRam.Count != 0 || step.WorkRamWrites.Count != 0 || step.ExpectedWorkRam.Count != 0 ||
                     step.ExpectedWorkRamSize is not null || step.CaptureCartridgeRam || step.ExpectedChrSha256 is not null ||
                     step.WaitForExpectedRam || step.MinimumVisibleCells != 0 || step.Reset ||
                     step.SaveStateSlot is not null || step.LoadStateSlot is not null || step.OutputIndex is not null ||
                     step.IcarusDoorIndex is not null || step.IcarusTrainingDodgeMilliseconds is not null ||
                     step.MetroidPickupType is not null || step.CastlevaniaStairTop is not null ||
                     step.CastlevaniaHallStairFoot || step.CastlevaniaHallStairClimb || step.ContinueOnRejectedCapture))
                    throw new InvalidDataException("Burst capture requires1..512 raw paired frames, no preview/state assertions, work RAM, save slots or steering; audit each saved frame separately.");
                if (step.CastlevaniaStairTop is int stair &&
                    (stair is < 1 or > 2 || step.RamWrites.Count != 0 || step.WorkRamWrites.Count != 0))
                    throw new InvalidDataException("Stair approach requires top1/2 and no state writes.");
                if (step.CastlevaniaHallStairFoot &&
                    (step.CastlevaniaStairTop is not null || step.CastlevaniaHallStairClimb || step.RamWrites.Count != 0 || step.WorkRamWrites.Count != 0))
                    throw new InvalidDataException("Hall stair-foot approach cannot combine destinations or state writes.");
                if (step.CastlevaniaHallStairClimb &&
                    (step.CastlevaniaStairTop is not null || step.RamWrites.Count != 0 || step.WorkRamWrites.Count != 0))
                    throw new InvalidDataException("Hall climb cannot combine destinations or state writes.");
                if (step.IcarusDoorIndex is int doorIndex && (doorIndex < 0 || doorIndex > 127))
                    throw new InvalidDataException("Kid Icarus door index must be between 0 and 127.");
                if (step.ExpectedWorkRamSize is int workSize && (workSize < 1 || workSize > 8192))
                    throw new InvalidDataException("Calibration work RAM size must be between 1 and 8192 bytes.");
                if ((step.WorkRamWrites.Count > 0 || step.ExpectedWorkRam.Count > 0) && step.ExpectedWorkRamSize is null)
                    throw new InvalidDataException("Calibration work RAM access requires an exact allocation size.");
                if (step.WorkRamWrites.Keys.Concat(step.ExpectedWorkRam.Keys).Any(address => address < 0 || address >= step.ExpectedWorkRamSize))
                    throw new InvalidDataException("Calibration work RAM offsets must stay within the declared allocation.");
                if (step.SaveStateSlot is int saveSlot && (saveSlot < 1 || saveSlot > 10))
                    throw new InvalidDataException("Calibration save-state slot must be between 1 and 10.");
                if (step.LoadStateSlot is int loadSlot && (loadSlot < 1 || loadSlot > 10))
                    throw new InvalidDataException("Calibration load-state slot must be between 1 and 10.");
                if (step.SaveStateSlot is not null && !step.Capture)
                    throw new InvalidDataException("Calibration save states require a captured, asserted frame.");
                if (step.ContinueOnRejectedCapture && string.IsNullOrWhiteSpace(step.CaptureGroup))
                    throw new InvalidDataException("Continuing inventory captures require a named capture group.");
            }
            foreach (var group in steps.Where(s => s.ContinueOnRejectedCapture).GroupBy(s => s.CaptureGroup))
                if (steps.First(s => s.CaptureGroup == group.Key).LoadStateSlot is null)
                    throw new InvalidDataException("Continuing inventory groups must begin with an isolated save-state restore.");
            using NesEmulator emulator = new(); emulator.Load(rom);
            int index = 0;
            List<string> acceptedFrames = [];
            List<object> rejections = [];
            List<string> skipped = [];
            HashSet<string> failedGroups = [];
            foreach (Step step in steps)
            {
                if (step.CaptureGroup is string group && failedGroups.Contains(group))
                {
                    skipped.Add(step.Name);
                    continue;
                }
                try
                {
                    if (step.LoadStateSlot is int loadSlot) emulator.LoadState(loadSlot);
                    if (step.Reset) emulator.Reset();
                    if(step.IcarusTrainingDodgeMilliseconds is int trainingTime)
                        acceptedFrames.AddRange(DodgeIcarusTraining(emulator,rom,trainingTime,Path.Combine(output,Path.GetFileName(step.Name))));
                    if (step.MetroidPickupType is int pickupType)
                        ApproachMetroidPickup(emulator,rom,pickupType,Path.Combine(output,Path.GetFileName(step.Name)));
                    if (step.IcarusDoorIndex is int doorIndex) ApproachIcarusDoor(emulator, rom, doorIndex,
                        Path.Combine(output, Path.GetFileName(step.Name)));
                    if (step.CastlevaniaStairTop is int stairTop) ApproachCastlevaniaStairPoint(emulator, rom,
                        stairTop == 1 ? 752 : 688, stairTop == 1 ? 96 : 128, $"top{stairTop}", Path.Combine(output, Path.GetFileName(step.Name)));
                    if (step.CastlevaniaHallStairFoot) ApproachCastlevaniaStairPoint(emulator, rom,
                        1312, 192, "hall-foot", Path.Combine(output, Path.GetFileName(step.Name)));
                    if (step.CastlevaniaHallStairClimb) ApproachCastlevaniaStairPoint(emulator, rom,
                        1312, 96, "hall-climb", Path.Combine(output, Path.GetFileName(step.Name)), climbHall:true);
                    if (step.ExpectedWorkRamSize is int declaredSize && MesenApi.DebugGetMemorySize(DebugMemoryType.WorkRam) != declaredSize)
                        throw new InvalidDataException("Calibration cartridge work RAM allocation does not match the declared size.");
                    if (step.RamWrites.Count > 0 || step.WorkRamWrites.Count > 0)
                    {
                        if (step.RamWrites.Keys.Any(address => address < 0 || address > 0x7ff))
                            throw new InvalidDataException("Calibration writes must target internal RAM only.");
                        emulator.TogglePause();
                        try
                        {
                            // Pause is requested asynchronously by the core. Let its
                            // current frame finish before changing loader state; writing
                            // while a banked routine is executing can strand transitions.
                            Thread.Sleep(100);
                            foreach (var write in step.RamWrites)
                                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, (uint)write.Key, write.Value);
                            foreach (var write in step.WorkRamWrites)
                                MesenApi.DebugSetMemoryValue(DebugMemoryType.WorkRam, (uint)write.Key, write.Value);
                        }
                        finally { emulator.TogglePause(); }
                    }
                    emulator.SetInputMask(step.Mask);
                    if (step.BurstFrames is int burstCount)
                    {
                        acceptedFrames.AddRange(CapturePairedBurst(emulator, burstCount, step.BurstStartRam,
                            Path.Combine(output, Path.GetFileName(step.Name))));
                        continue;
                    }
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (wait.ElapsedMilliseconds < Math.Clamp(step.Milliseconds, 1, 60000))
                    {
                        Thread.Sleep(50); _ = emulator.CaptureFrame();
                    }
                    if (!step.Capture) continue;
                    if (step.MinimumVisibleCells > 0 || step.WaitForExpectedRam || step.ExpectedChrSha256 is not null || step.ExpectedWorkRam.Count > 0)
                    {
                        var settle = System.Diagnostics.Stopwatch.StartNew();
                        while (true)
                        {
                            NesFrame candidate = emulator.CaptureFrame()!;
                            bool stateReady = !step.WaitForExpectedRam || step.ExpectedRam.All(e =>
                                e.Key >= 0 && e.Key < candidate.Ram.Length && candidate.Ram[e.Key] == e.Value);
                            bool videoReady = step.MinimumVisibleCells <= 0 || HasVisibleScenery(candidate, step.MinimumVisibleCells, emulator.BuiltInGameProfile);
                            bool artworkReady = MatchesChr(candidate, step.ExpectedChrSha256, step.ExpectedChrOffset, step.ExpectedChrLength);
                            bool workRamReady = step.ExpectedWorkRam.All(e => MesenApi.DebugGetMemoryValue(DebugMemoryType.WorkRam, (uint)e.Key) == e.Value);
                            if (stateReady && videoReady && artworkReady && workRamReady) break;
                            if (settle.ElapsedMilliseconds >= Math.Clamp(step.StabilityTimeoutMilliseconds, 100, 60000))
                            {
                                emulator.TogglePause(); Thread.Sleep(25);
                                candidate = CaptureWorkRam(emulator.CaptureFrame()!, step.ExpectedWorkRamSize);
                                File.WriteAllText(Path.Combine(output, Path.GetFileName(step.Name) + ".rejected.frame.json"), JsonSerializer.Serialize(candidate));
                                string actualArtwork = step.ExpectedChrSha256 is null ? "not asserted" :
                                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(candidate.Chr.AsSpan(step.ExpectedChrOffset, step.ExpectedChrLength)));
                                throw new InvalidDataException($"{step.Name}: state/native video has not settled; refusing transition/blank training data. " +
                                    $"Ready flags: state={stateReady}, video={videoReady}, artwork={artworkReady}, work RAM={workRamReady}. " +
                                    $"Captured CHR SHA256: {actualArtwork}. " +
                                    string.Join(", ", step.ExpectedRam.Select(e => $"RAM {e.Key:X4}: expected {e.Value:X2}, actual {(e.Key >= 0 && e.Key < candidate.Ram.Length ? candidate.Ram[e.Key].ToString("X2") : "out of range")}")
                                        .Concat(step.ExpectedWorkRam.Select(e => $"Work RAM {e.Key:X4}: expected {e.Value:X2}, actual {candidate.CalibrationWorkRam![e.Key]:X2}"))));
                            }
                            Thread.Sleep(80);
                        }
                    }
                    emulator.TogglePause(); Thread.Sleep(25);
                    NesFrame f = CaptureWorkRam(emulator.CaptureFrame()!, step.ExpectedWorkRamSize);
                    // The running-frame readiness check above is not the saved
                    // snapshot: a moving camera can advance before pause takes
                    // effect. Never accept invisible/stale PPU artwork merely
                    // because a preceding frame was settled.
                    if (step.MinimumVisibleCells > 0 && !HasVisibleScenery(f, step.MinimumVisibleCells, emulator.BuiltInGameProfile))
                    {
                        File.WriteAllText(Path.Combine(output, Path.GetFileName(step.Name) + ".rejected.frame.json"), JsonSerializer.Serialize(f));
                        throw new InvalidDataException($"{step.Name}: paused native snapshot has insufficient visible scenery; refusing stale/blank training data.");
                    }
                    if (f.CalibrationWorkRam is byte[] workRam)
                    {
                        foreach (var expected in step.ExpectedWorkRam)
                            if (workRam[expected.Key] != expected.Value)
                            {
                                File.WriteAllText(Path.Combine(output, Path.GetFileName(step.Name) + ".rejected.frame.json"), JsonSerializer.Serialize(f));
                                throw new InvalidDataException($"{step.Name}: unexpected cartridge work RAM at offset {expected.Key:X4}; refusing mislabeled capture.");
                            }
                    }
                    if (!MatchesChr(f, step.ExpectedChrSha256, step.ExpectedChrOffset, step.ExpectedChrLength))
                    {
                        File.WriteAllText(Path.Combine(output, Path.GetFileName(step.Name) + ".rejected.frame.json"), JsonSerializer.Serialize(f));
                        throw new InvalidDataException($"{step.Name}: unexpected CHR artwork; refusing mislabeled capture.");
                    }
                    foreach (var expected in step.ExpectedRam)
                        if (expected.Key < 0 || expected.Key >= f.Ram.Length || f.Ram[expected.Key] != expected.Value)
                        {
                            File.WriteAllText(Path.Combine(output, Path.GetFileName(step.Name) + ".rejected.frame.json"), JsonSerializer.Serialize(f));
                            string actual = expected.Key >= 0 && expected.Key < f.Ram.Length ? f.Ram[expected.Key].ToString("X2") : "out of range";
                            throw new InvalidDataException($"{step.Name}: unexpected state at RAM {expected.Key:X4} (expected {expected.Value:X2}, actual {actual}); refusing mislabeled capture.");
                        }
                    int captureIndex = step.OutputIndex ?? index++;
                    if (step.SaveStateSlot is int saveSlot) emulator.SaveState(saveSlot);
                    if (captureIndex < 0 || captureIndex > 10000) throw new InvalidDataException("Invalid calibration output index.");
                    string prefix = Path.Combine(output, $"{captureIndex:00}-{Path.GetFileName(step.Name)}");
                    if (step.CaptureCartridgeRam)
                    {
                        byte[] cpu = new byte[65536];
                        var handle = System.Runtime.InteropServices.GCHandle.Alloc(cpu, System.Runtime.InteropServices.GCHandleType.Pinned);
                        try { MesenApi.DebugGetMemoryState(DebugMemoryType.CpuMemory, handle.AddrOfPinnedObject()); }
                        finally { handle.Free(); }
                        File.WriteAllBytes(prefix + ".cartridge-ram.bin", cpu.AsSpan(0x6000, 0x2000).ToArray());
                    }
                    File.WriteAllText(prefix + ".frame.json", JsonSerializer.Serialize(f));
                    acceptedFrames.Add(prefix + ".frame.json");
                    // Long diagnostic inventories may stop at a later route.
                    // Preserve completed assertions without claiming terminal
                    // success or promoting rejected/approach-only frames.
                    File.WriteAllText(Path.Combine(output, "capture-checkpoint.json"),
                        JsonSerializer.Serialize(new { Complete=false, CapturedAfterAssertions=true,
                            LastStep=step.Name, AcceptedFrames=acceptedFrames, Rejections=rejections,
                            Scope="Partial diagnostic checkpoint, not terminal inventory success" },
                            new JsonSerializerOptions { WriteIndented=true }));
                    if (!step.RenderPreviews)
                    {
                        emulator.TogglePause();
                        continue;
                    }
                    if (f.NativeScreenPixels?.Length == 256 * 240)
                    {
                        using Bitmap native = new(256, 240);
                        for (int y = 0; y < 240; y++) for (int x = 0; x < 256; x++) native.SetPixel(x, y, Color.FromArgb(f.NativeScreenPixels[y * 256 + x]));
                        native.Save(prefix + "-native.png", ImageFormat.Png);
                    }
                    using SmbScene scene = new SmbProfile().Build(f, emulator.IsSmbWorld, null, emulator.BuiltInGameProfile);
                    using WarpRendererControl renderer = new() { Size = new(820, 780), DepthAmount = 0 };
                    renderer.CreateControl(); renderer.SetScene(scene.Clone());
                    using Bitmap image = new(820, 780); renderer.DrawToBitmap(image, renderer.ClientRectangle);
                    image.Save(prefix + ".png", ImageFormat.Png);
                    using Bitmap background = SmbProfile.ComposeBackground(f, emulator.IsSmbWorld);
                    background.Save(prefix + "-background.png", ImageFormat.Png);
                    File.WriteAllText(prefix + ".stats.json", JsonSerializer.Serialize(new { emulator.RomSha256, f.ScrollX, f.ScrollY, f.RawScrollX, f.RawScrollY, f.SpritePatternBase, f.LargeSprites, Objects = scene.Objects.Select(o => new { o.Label, o.Bounds }) }, new JsonSerializerOptions { WriteIndented = true }));
                    emulator.TogglePause();
                }
                catch (InvalidDataException e) when (step.ContinueOnRejectedCapture && e.Message.Contains("refusing", StringComparison.OrdinalIgnoreCase))
                {
                    failedGroups.Add(step.CaptureGroup!);
                    rejections.Add(new { step.Name, step.CaptureGroup, Error = e.Message });
                    if (emulator.IsPaused) emulator.TogglePause();
                }
            }
            File.WriteAllText(Path.Combine(output, "capture-report.json"), JsonSerializer.Serialize(new
            {
                Passed = rejections.Count == 0,
                AcceptedFrames = acceptedFrames,
                Rejections = rejections,
                Skipped = skipped
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "capture-checkpoint.json"),
                JsonSerializer.Serialize(new { Complete=true, Passed=rejections.Count == 0,
                    CapturedAfterAssertions=true, AcceptedFrames=acceptedFrames,
                    Rejections=rejections, Scope="Terminal enumerated diagnostic steps, not whole-game coverage" },
                    new JsonSerializerOptions { WriteIndented=true }));
            return rejections.Count == 0 ? 0 : 1;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); return 1; }
        finally { Environment.SetEnvironmentVariable("WARP4D_HOME", previous); }
    }

    private static NesFrame CaptureWorkRam(NesFrame frame, int? size)
    {
        if (size is not int snapshotSize) return frame;
        byte[] workRam = new byte[snapshotSize];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(workRam, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { MesenApi.DebugGetMemoryState(DebugMemoryType.WorkRam, handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
        return frame with { CalibrationWorkRam = workRam };
    }

    private static string[] CapturePairedBurst(NesEmulator emulator, int count, Dictionary<int, byte> startRam, string prefix)
    {
        List<NesFrame> frames = [];
        long previous = emulator.CaptureFrame()?.Sequence ?? -1;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        bool complete = false;
        bool started = startRam.Count == 0;
        List<object> prelude = [];
        try
        {
            while (frames.Count < count)
            {
                NesFrame? frame = emulator.CaptureFrame();
                if (frame is not null && frame.Sequence > previous)
                {
                    if (frame.CaptureScanline != 96 || frame.NativeScreenSequence != frame.Sequence ||
                        frame.NativeScreenPixels?.Length != 61440 || frame.Ram.Length != 2048 ||
                        frame.Chr.Length != 8192 || frame.Oam.Length != 256)
                        throw new InvalidDataException("Burst requires complete paired native publications.");
                    previous = frame.Sequence;
                    if (!started && startRam.All(e => frame.Ram[e.Key] == e.Value)) started = true;
                    if (started) frames.Add(frame);
                    else prelude.Add(new { frame.Sequence, Values = startRam.Keys.ToDictionary(address => address, address => frame.Ram[address]) });
                }
                if (timer.ElapsedMilliseconds > 60000)
                    throw new TimeoutException("Burst did not collect the requested new publications; partial observations are not an accepted cohort.");
                Thread.Sleep(1);
            }
            complete = true;
        }
        finally
        {
            // Stop sampling before expensive serialization. The published frame
            // records own immutable snapshot arrays, not the current core state.
            emulator.TogglePause();
            try
            {
                Thread.Sleep(25);
                for (int i = 0; i < frames.Count; i++)
                    File.WriteAllText(prefix + $"-{i:D3}.burst.frame.json", JsonSerializer.Serialize(frames[i]));
                File.WriteAllText(prefix + ".burst-report.json", JsonSerializer.Serialize(new
                {
                    Complete = complete, Requested = count, Captured = frames.Count,
                    Scope = "New paired publications observed by polling, not proof that none were skipped; raw native state/visibility requires independent audit before training.",
                    Sequences = frames.Select(f => f.Sequence).ToArray(),
                    StartGate = startRam, Prelude = prelude,
                    NoInterFramePauseOrSerialization = true, NoPostEntryStateWrites = true
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally { emulator.TogglePause(); }
        }
        return Enumerable.Range(0, frames.Count).Select(i => prefix + $"-{i:D3}.burst.frame.json").ToArray();
    }

    internal static bool MatchesChr(NesFrame frame, string? expected, int offset, int length)
    {
        if (expected is null) return true;
        if (offset < 0 || length <= 0 || offset > frame.Chr.Length || length > frame.Chr.Length - offset)
            throw new InvalidDataException("CHR assertion range is outside captured artwork.");
        return string.Equals(expected, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame.Chr.AsSpan(offset, length))), StringComparison.OrdinalIgnoreCase);
    }

    private static void ApproachMetroidPickup(NesEmulator emulator,string rom,int type,string evidencePrefix)
    {
        byte[] cartridge=File.ReadAllBytes(rom);
        if(type is <0 or >7 || BuiltInGameProfiles.Identify(cartridge)?.Id!="metroid" ||
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cartridge))!=emulator.RomSha256 ||
            MesenApi.DebugGetMemorySize(DebugMemoryType.WorkRam)!=8192)
            throw new InvalidDataException("Pickup approach requires the explicitly loaded supported Metroid and gear type 0–7.");
        List<object> trace=[];
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        long jumpUntil=0,nextJump=0;
        try
        {
            emulator.SetInputMask(0);
            Thread.Sleep(150);
            while(elapsed.ElapsedMilliseconds<15000)
            {
                NesFrame frame=emulator.CaptureFrame()!;
                byte[] r=frame.Ram;
                int gear=MesenApi.DebugGetMemoryValue(DebugMemoryType.WorkRam,2168);
                int cheat=MesenApi.DebugGetMemoryValue(DebugMemoryType.WorkRam,2482);
                if(cheat!=0 || r[29]!=0 || r[768] is 7 or 8)
                    throw new InvalidDataException("Pickup approach must remain live native gameplay without password cheat.");
                if((gear&(1<<type))!=0)return;
                int slot=Enumerable.Range(0,2).FirstOrDefault(i=>(r[1864+i*8]&15)==type && r[1864+i*8]!=255,-1);
                if(slot<0)throw new InvalidDataException("Requested native item is not present; refusing guessed destination.");
                int ix=r[1866+slot*8],iy=r[1865+slot*8];
                int dx=((ix-r[782]+128)&255)-128;
                int dy=iy-r[781];
                // Small feedback steps stop on the actual orb rather than
                // assuming a wall-clock trajectory repeats frame-for-frame.
                int mask=dx>3 ? 128 : dx< -3 ? 64 : 0;
                // Keep the jump pressed long enough to clear a raised
                // pillar; releasing at the orb's Y truncates the ascent.
                if(dy< -10 && elapsed.ElapsedMilliseconds>=nextJump)
                {
                    jumpUntil=elapsed.ElapsedMilliseconds+450;
                    nextJump=elapsed.ElapsedMilliseconds+1100;
                }
                if(elapsed.ElapsedMilliseconds<jumpUntil)mask|=1;
                trace.Add(new{frame.Sequence,Milliseconds=elapsed.ElapsedMilliseconds,PlayerX=(int)r[782],PlayerY=(int)r[781],PlayerTable=(int)r[780],ItemX=ix,ItemY=iy,ItemTable=(int)r[1867+slot*8],Dx=dx,Dy=dy,Gear=gear,Mask=mask});
                emulator.SetInputMask(mask);
                Thread.Sleep(50);
            }
            File.WriteAllText(evidencePrefix+".rejected.frame.json",JsonSerializer.Serialize(CaptureWorkRam(emulator.CaptureFrame()!,8192)));
            throw new InvalidDataException("Native pickup did not complete; no equipment or coordinate writes were used.");
        }
        finally
        {
            emulator.SetInputMask(0);
            File.WriteAllText(evidencePrefix+".approach.json",JsonSerializer.Serialize(new{Scope="Native controller feedback only, not a completed route by itself.",Type=type,Trace=trace},new JsonSerializerOptions{WriteIndented=true}));
        }
    }

    private static void ApproachCastlevaniaStairPoint(NesEmulator emulator, string rom, int targetX, int targetY, string point, string evidencePrefix, bool climbHall=false)
    {
        byte[] cartridge = File.ReadAllBytes(rom);
        if (BuiltInGameProfiles.Identify(cartridge)?.Id != "castlevania" ||
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cartridge)) != emulator.RomSha256)
            throw new InvalidDataException("Stair approach requires the explicitly loaded supported Castlevania payload.");
        List<object> trace = [];
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        bool sweepRight = false;
        try
        {
            emulator.SetInputMask(0);
            Thread.Sleep(150);
            while (elapsed.ElapsedMilliseconds < (climbHall ? 30000 : 15000))
            {
                NesFrame frame = emulator.CaptureFrame()!;
                byte[] r = frame.Ram;
                if (frame.CaptureScanline != 96 || frame.NativeScreenSequence != frame.Sequence || r[24] != 5 || r[40] != 1 || r[70] != 0)
                    throw new InvalidDataException("Stair approach left coherent stage-one gameplay.");
                int x = r[64] + 256 * r[65], y = r[63], action = r[1132];
                // Native hit/knockback may interrupt the walk. Let it finish;
                // do not erase enemies, damage, stun or any movement state.
                int mask = action is 5 or 9 ? 0 : x > targetX + 2 ? 64 : x < targetX - 2 ? 128 : 0;
                if (climbHall)
                {
                    if (y == targetY && action == 0) return;
                    if (action is 4 or 6) mask=16;
                    else if (action is not (5 or 9) && y == 192)
                    {
                        // The stair extends across the previous screen edge.
                        // Sweep a bounded floor interval with Up held rather
                        // than treating a guessed foot coordinate as proof.
                        int left=targetX-48, right=targetX+32;
                        if (x>right) mask=64;
                        else if (x<left) mask=128;
                        else
                        {
                            if (x<=left+2) sweepRight=true;
                            if (x>=right-2) sweepRight=false;
                            mask=16 | (sweepRight ? 128 : 64);
                        }
                    }
                    else if (action is not (5 or 9) && y != 192) mask=0;
                }
                trace.Add(new { frame.Sequence, X=x, Y=y, Action=action, HP=(int)r[69], TargetX=targetX, TargetY=targetY, Mask=mask });
                if (!climbHall && Math.Abs(x - targetX) <= 2 && y == targetY && action == 0) return;
                if (!climbHall && action == 0 && y != targetY)
                    throw new InvalidDataException("Stair-point approach is on the wrong platform; refusing a guessed traversal.");
                emulator.SetInputMask(mask);
                Thread.Sleep(50);
            }
            throw new InvalidDataException("Native stair-point approach timed out; no coordinate/state writes used.");
        }
        catch
        {
            File.WriteAllText(evidencePrefix + ".rejected.frame.json", JsonSerializer.Serialize(emulator.CaptureFrame()));
            throw;
        }
        finally
        {
            emulator.SetInputMask(0);
            File.WriteAllText(evidencePrefix + ".approach.json", JsonSerializer.Serialize(new { Scope="Native controller feedback only, not traversal proof.", Point=point, Trace=trace }, new JsonSerializerOptions { WriteIndented=true }));
        }
    }

    internal static void ValidateTrainingStep(Step step)
    {
        if(step.IcarusTrainingDodgeMilliseconds is int duration &&
           (duration<1000 || duration>60000 || step.RamWrites.Count!=0 || step.WorkRamWrites.Count!=0 ||
            step.Reset || step.LoadStateSlot is not null || step.IcarusDoorIndex is not null ||
            step.MetroidPickupType is not null || step.CastlevaniaStairTop is not null ||
            step.CastlevaniaHallStairFoot || step.CastlevaniaHallStairClimb || step.Mask!=0))
            throw new InvalidDataException("Training feedback requires1..60 seconds, no other destination/buttons/reset/restore/state writes.");
    }

    private static List<string> DodgeIcarusTraining(NesEmulator emulator,string rom,int duration,string prefix)
    {
        byte[] cartridge=File.ReadAllBytes(rom);
        if(BuiltInGameProfiles.Identify(cartridge)?.Id!="icarus" ||
           Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cartridge))!=emulator.RomSha256)
            throw new InvalidDataException("Training feedback requires the explicitly loaded supported Kid Icarus payload.");
        var policy=new IcarusTrainingController();List<object> trace=[];List<string> captures=[];
        var elapsed=System.Diagnostics.Stopwatch.StartNew();long previousSequence=-1,nextCapture=0;
        int previousHealth=-1,previousChamber=-1;bool active=false;string outcome="time-limit";
        try
        {
            while(elapsed.ElapsedMilliseconds<duration)
            {
                var f=emulator.CaptureFrame();
                if(f is null || f.Sequence==previousSequence){Thread.Sleep(10);continue;}
                if(f.CaptureScanline!=96 || f.NativeScreenSequence!=f.Sequence || f.NativeScreenPixels?.Length!=61440)
                    throw new InvalidDataException("Training feedback refuses unpaired native capture.");
                previousSequence=f.Sequence;
                var decision=policy.Choose(f.Ram,f.Oam,f.Sequence);
                active|=f.Ram[58]==9;
                bool end=active&&(f.Ram[166]==0 || f.Ram[58]!=9);
                trace.Add(new{f.Sequence,Milliseconds=elapsed.ElapsedMilliseconds,X=(int)f.Ram[1827],Y=(int)f.Ram[1824],Health=(int)f.Ram[166],Chamber=(int)f.Ram[58],decision.Mask,decision.Target,decision.Hazards});
                if(elapsed.ElapsedMilliseconds>=nextCapture || f.Ram[166]!=previousHealth || f.Ram[58]!=previousChamber || end)
                {
                    string file=prefix+$"-feedback-{captures.Count:D3}.frame.json";
                    File.WriteAllText(file,JsonSerializer.Serialize(f));captures.Add(file);
                    nextCapture=elapsed.ElapsedMilliseconds+500;
                    previousHealth=f.Ram[166];previousChamber=f.Ram[58];
                }
                if(end){outcome=f.Ram[166]==0?"native-health-zero":"native-chamber-changed-not-yet-reviewed";break;}
                emulator.SetInputMask(decision.Mask);Thread.Sleep(30);
            }
        }
        finally
        {
            emulator.SetInputMask(0);
            File.WriteAllText(prefix+".feedback.json",JsonSerializer.Serialize(new{Outcome=outcome,ActiveObserved=active,NoStateWrites=true,Trace=trace},new JsonSerializerOptions{WriteIndented=true}));
        }
        return captures;
    }

    private static void ApproachIcarusDoor(NesEmulator emulator, string rom, int index, string evidencePrefix)
    {
        byte[] cartridge = File.ReadAllBytes(rom);
        if (BuiltInGameProfiles.Identify(cartridge)?.Id != "icarus" ||
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cartridge)) != emulator.RomSha256)
            throw new InvalidDataException("Door approach requires the explicitly loaded supported Kid Icarus payload.");
        int header = 16 + ((cartridge[6] & 4) != 0 ? 512 : 0);
        int pointer = MesenApi.DebugGetMemoryValue(DebugMemoryType.CpuMemory, 0x7f56) |
            (MesenApi.DebugGetMemoryValue(DebugMemoryType.CpuMemory, 0x7f57) << 8);
        int fieldMode = MesenApi.DebugGetMemoryValue(DebugMemoryType.InternalRam, 0xa0);
        // Only the three captured native field tables are supported. Sky
        // World's table uses zero-based segments while its live camera uses
        // one-based segments; its cyclic Y placement still uses the table ID.
        int expectedPointer = fieldMode switch { 2 => 0xefc9, 4 => 0xefff, 6 => 0xf05d, _ => -1 };
        if (pointer != expectedPointer || cartridge[4] != 8)
            throw new InvalidDataException("Native door table must point into the supported fixed PRG bank.");
        int start = header + 7 * 16384 + pointer - 0xc000;
        for (int i = 0; i <= index; i++)
            if (start + i * 4 + 3 >= cartridge.Length || cartridge[start + i * 4] == 0xff)
                throw new InvalidDataException("Door index reaches the native table terminator.");
        int offset = start + index * 4;
        int stage = cartridge[offset], segment = cartridge[offset + 1], position = cartridge[offset + 2];
        int targetSegment = segment + (fieldMode == 6 ? 1 : 0);
        int arrivalHealth = MesenApi.DebugGetMemoryValue(DebugMemoryType.InternalRam, 0xa6);
        if (arrivalHealth is < 1 or > 15)
            throw new InvalidDataException("Door approach requires a live supported health value.");
        if (fieldMode == 4)
        {
            ApproachIcarusHorizontalDoor(emulator, index, stage, segment, position,
                cartridge[offset + 3], arrivalHealth, evidencePrefix);
            return;
        }
        int doorX = (position & 15) * 16;
        int doorY = (position >> 4) * 16 + ((segment - 1) & 1) * 240;
        // A fixed center ascent can touch an earlier doorway and start its
        // native entry transition before reaching the requested segment.
        // Pick the lane farthest from every doorway in this native stage.
        List<int> stageDoorXs=[];
        for(int entry=0;entry<128;entry++)
        {
            int at=start+entry*4;
            if(at+3>=cartridge.Length || cartridge[at]==0xff)break;
            if(cartridge[at]==stage)stageDoorXs.Add((cartridge[at+2]&15)*16);
        }
        int ascentX=Enumerable.Range(1,14).Select(i=>i*16)
            .MaxBy(x=>stageDoorXs.Min(dx=>Math.Abs(dx-x)));
        List<object> approachTrace = [];
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        emulator.SetInputMask(0);
        // A restored slot can briefly leave the previous room's published
        // video pair in the managed cache. Let the native pair advance first.
        Thread.Sleep(150);
        // Late Underworld doors lie many scrolling segments above the seed.
        // Keep this diagnostic bounded without accepting a nearer wrong room.
        while (elapsed.ElapsedMilliseconds < 120000)
        {
            emulator.TogglePause();
            try
            {
                Thread.Sleep(100);
                NesFrame frame = emulator.CaptureFrame() ?? throw new InvalidDataException("No native door approach frame.");
                if (frame.CaptureScanline != 96 || frame.NativeScreenSequence != frame.Sequence ||
                    frame.Ram[0xa0] != fieldMode || frame.Ram[0x130] != stage || frame.Ram[0x3b] != 0)
                {
                    File.WriteAllText(evidencePrefix + ".rejected.approach.frame.json", JsonSerializer.Serialize(frame));
                    throw new InvalidDataException($"Door approach requires coherent native field state; refusing a wrong-stage/chamber setup. " +
                        $"Actual mode={frame.Ram[0xa0]}, stage={frame.Ram[0x130]}, room={frame.Ram[0x3b]}, " +
                        $"scanline={frame.CaptureScanline}, sequences={frame.Sequence}/{frame.NativeScreenSequence}.");
                }
                int currentSegment = frame.Ram[0x4d1];
                int top = (doorY - frame.RawScrollY + 480) % 480;
                approachTrace.Add(new { TargetIndex=index, Stage=stage, FieldMode=fieldMode, TableSegment=segment, TargetSegment=targetSegment,
                    Position=position, RoomType=cartridge[offset+3], CurrentSegment=currentSegment,
                    frame.RawScrollY, DoorTop=top, AscentX=ascentX, PlayerX=frame.Ram[0x723], PlayerY=frame.Ram[0x720],
                    frame.Sequence, frame.NativeScreenSequence });
                File.WriteAllText(evidencePrefix + ".approach.json", JsonSerializer.Serialize(approachTrace));
                // Exploratory evidence, never an AcceptedFrames entry. Preserve
                // the actual camera/doorway when a rejected approach overshoots.
                if (currentSegment >= targetSegment - 1)
                    File.WriteAllText(evidencePrefix + ".approach.frame.json", JsonSerializer.Serialize(frame));
                if (currentSegment > targetSegment)
                    throw new InvalidDataException("Door approach overshot its native segment; refusing mislabeled capture.");
                // Upper-segment doors can leave the segment before reaching
                // Y=64. Their complete body is already visible at Y>=16;
                // keep space below for the 16-pixel player entry alignment.
                bool ready = currentSegment == targetSegment && top >= 16 && top <= (fieldMode == 6 ? 208 : 160);
                // Sky spring entry can begin immediately after alignment.
                // Restore its initial health before unpausing; never seed an
                // injury after the native room has already been entered.
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0xa6,
                    (byte)(ready && fieldMode == 6 ? arrivalHealth : 15));
                for (uint address = 0x1c; address <= 0x1f; address++)
                    MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, address, 0);
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x3e, 0);
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x721, 128);
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x724, 0);
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x728, 0);
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x720, (byte)(ready ? top + 16 : 48));
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x723, (byte)(ready ? doorX : ascentX));
                if (ready) return;
            }
            finally { if (emulator.IsPaused) emulator.TogglePause(); }
            Thread.Sleep(500);
        }
        throw new InvalidDataException("Native door approach timed out; refusing unverified room evidence.");
    }

    private static void ApproachIcarusHorizontalDoor(NesEmulator emulator, int index,
        int stage, int segment, int position, int roomType, int arrivalHealth, string evidencePrefix)
    {
        // EFFF inserts into the incoming map via ($62),Y (native bank6:9503).
        // Its segment counter is one screen ahead; logical screen0 is PPU
        // page1. Require the active map's actual door marker, not just the
        // incoming segment label. Advance the
        // native camera by assisted player placement, never camera/map writes.
        int cyclicDoorX = ((segment + 1) * 256 + (position & 15) * 16) % 512;
        int targetSegment = segment + 1;
        int doorTop = (position >> 4) * 16;
        List<object> trace = [];
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        emulator.SetInputMask(0);
        Thread.Sleep(150);
        while (elapsed.ElapsedMilliseconds < 180000)
        {
            emulator.TogglePause();
            try
            {
                Thread.Sleep(100);
                NesFrame frame = emulator.CaptureFrame() ?? throw new InvalidDataException("No horizontal door frame.");
                if (frame.CaptureScanline != 96 || frame.NativeScreenSequence != frame.Sequence ||
                    frame.Ram[0xa0] != 4 || frame.Ram[0x130] != stage || frame.Ram[0x3b] != 0 ||
                    frame.RawScrollY % 240 != 0)
                {
                    File.WriteAllText(evidencePrefix + ".rejected.approach.frame.json", JsonSerializer.Serialize(frame));
                    throw new InvalidDataException("Horizontal door approach requires coherent matching native field/camera state; refusing unverified room evidence.");
                }
                int currentSegment = frame.Ram[0x4d1];
                int left = (cyclicDoorX - frame.RawScrollX + 512) % 512;
                // Native grids have fifteen 16-pixel rows: their captured
                // bases are0500/05F0 (240 bytes apart), not04F0/05F0.
                int activeMap = frame.RawScrollX < 256 ? 0x5f0 : 0x500;
                bool activeDoor = position <= 0xdf && frame.Ram[activeMap + position] == 0x50 &&
                    frame.Ram[activeMap + position + 16] == roomType;
                trace.Add(new { TargetIndex=index, Stage=stage, FieldMode=4, TableSegment=segment,
                    TargetSegment=targetSegment, Position=position, RoomType=roomType, CurrentSegment=currentSegment,
                    frame.RawScrollX, frame.RawScrollY, DoorLeft=left, DoorTop=doorTop,
                    ActiveMap=activeMap, ActiveDoor=activeDoor,
                    PlayerX=frame.Ram[0x723], PlayerY=frame.Ram[0x720], frame.Sequence, frame.NativeScreenSequence });
                File.WriteAllText(evidencePrefix + ".approach.json", JsonSerializer.Serialize(trace));
                if (currentSegment >= targetSegment - 1)
                    File.WriteAllText(evidencePrefix + ".approach.frame.json", JsonSerializer.Serialize(frame));
                if (currentSegment > targetSegment)
                    throw new InvalidDataException("Horizontal door approach overshot native segment; refusing mislabeled room evidence.");
                bool ready = currentSegment == targetSegment && activeDoor && left >= 16 && left <= 208 && doorTop >= 16 && doorTop <= 208;
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0xa6, (byte)(ready ? arrivalHealth : 15));
                for (uint address=0x1c; address<=0x1f; address++)
                    MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, address, 0);
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x3e, 0);
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x721, 128);
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x724, 0);
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x728, 0);
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x720, (byte)(ready ? doorTop + 16 : 48));
                MesenApi.DebugSetMemoryValue(DebugMemoryType.InternalRam, 0x723, (byte)(ready ? left : 208));
                if (ready) return;
            }
            finally { if (emulator.IsPaused) emulator.TogglePause(); }
            Thread.Sleep(500);
        }
        throw new InvalidDataException("Horizontal door approach timed out; refusing unverified room evidence.");
    }

    private static bool HasVisibleScenery(NesFrame frame, int minimum, GameRecognitionProfile? profile)
    {
        string blank = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[256]));
        int visible = 0;
        for (int y = frame.ScrollY / 16 * 2; y <= (frame.ScrollY + 239) / 8; y += 2)
            for (int x = frame.ScrollX / 16 * 2; x <= (frame.ScrollX + 255) / 8; x += 2)
            {
                int screenX = x * 8 - frame.ScrollX, screenY = y * 8 - frame.ScrollY;
                if (screenX < 0 || screenX > 240 || screenY < 0 || screenY > 224) continue;
                if (profile is not null && !profile.Allows(new Rectangle(screenX, screenY, 16, 16), frame)) continue;
                if (MetatileVisualFingerprint.Read(frame, x, y, 16) == blank) continue;
                if (MetatileVisualFingerprint.IsVisible(frame, x, y, 16) && ++visible >= Math.Clamp(minimum, 1, 100)) return true;
            }
        return false;
    }
}
