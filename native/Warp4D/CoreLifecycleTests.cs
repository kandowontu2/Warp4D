using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

// Explicit diagnostic only. Never selects or discovers a cartridge at startup.
internal static class CoreLifecycleTests
{
    internal static int RunChrClassification(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            uint checks=MesenApi.Warp4DTestChrRamAddressClassification();
            if(checks!=24576)throw new InvalidOperationException($"Expected 24,576 CHR-RAM classification checks; got {checks}.");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,NoRomRequired=true,ForcedOverlappingRomAllocation=true},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception exception){File.WriteAllText(Path.Combine(output,"error.txt"),exception.ToString());return 1;}
    }
    internal static int Run(string manifest,string output,int rounds,string? smbWorld=null,string? smbEurope=null)
    {
        Directory.CreateDirectory(output);
        string? previousHome=Environment.GetEnvironmentVariable("WARP4D_HOME"),previousTrace=Environment.GetEnvironmentVariable("WARP4D_TRACE");
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        Environment.SetEnvironmentVariable("WARP4D_TRACE",Path.Combine(Path.GetFullPath(output),"native-trace.log"));
        List<object> completed=[];
        try
        {
            var inputs=JsonSerializer.Deserialize<BuiltInProfileTests.Input[]>(File.ReadAllText(manifest))!;
            if(inputs.Length!=9||rounds<1||rounds>100)throw new InvalidDataException("All nine explicitly supplied cartridges and 1–100 rounds required.");
            foreach(var input in inputs)
                if(BuiltInGameProfiles.Identify(File.ReadAllBytes(input.Rom))?.Id!=input.Id)
                    throw new InvalidDataException("Exact nine-game manifest identity required: "+input.Id);
            bool mixed=smbWorld is not null||smbEurope is not null;
            if(mixed)
            {
                if(smbWorld is null||smbEurope is null||!SmbCartridgeIdentity.IsSupported(File.ReadAllBytes(smbWorld))||!SmbCartridgeIdentity.IsSupported(File.ReadAllBytes(smbEurope)))
                    throw new InvalidDataException("Both exact supported SMB cartridges required for mixed lifecycle checks.");
                // Exercise both directions around every existing game, not
                // merely one SMB startup in a separate wrapper.
                inputs=inputs.SelectMany(input=>new[]{new BuiltInProfileTests.Input{Id="smb-world",Rom=smbWorld},input,
                    new BuiltInProfileTests.Input{Id="smb-europe",Rom=smbEurope},input}).ToArray();
            }
            // The DLL owns a global console. Reject overlapping wrappers before
            // they can reset native state; then allow the same wrapper after the
            // owner closes. This is prevention, not an asserted AV root cause.
            using(var primary=new NesEmulator())
            using(var secondary=new NesEmulator())
            {
                primary.Load(inputs[0].Rom);var before=WaitFrame(primary);
                bool rejected=false;
                try {secondary.Load(inputs[1].Rom);}
                catch(InvalidOperationException exception) when(exception.Message.StartsWith("Only one native emulator session",StringComparison.Ordinal)){rejected=true;}
                if(!rejected||secondary.IsLoaded)throw new InvalidOperationException("Overlapping native session must be rejected.");
                Thread.Sleep(80);var after=WaitFrame(primary);
                if(after.Sequence<=before.Sequence||primary.RomSha256!=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(inputs[0].Rom))))
                    throw new InvalidOperationException("Rejected session must not disturb the active console.");
                primary.Dispose();secondary.Load(inputs[1].Rom);_ = WaitFrame(secondary);
            }
            using(var emulator=new NesEmulator())
            {
                emulator.Load(inputs[0].Rom);_ = WaitFrame(emulator);
                Exception? inputError=null;
                Thread input=new(()=>{try{emulator.SetButton(NesButton.Right,true);}catch(Exception exception){inputError=exception;}}){IsBackground=true};
                emulator.HoldNativeLockForTest(()=>
                {
                    input.Start();
                    if(!SpinWait.SpinUntil(()=>input.ThreadState.HasFlag(System.Threading.ThreadState.WaitSleepJoin),1000))throw new TimeoutException("Input must queue behind native lock.");
                    emulator.Dispose(); // queued input must recheck loaded state after this
                });
                if(!input.Join(2000)||inputError is not null)throw new InvalidOperationException("Queued input must survive native shutdown.",inputError);
                emulator.Load(inputs[1].Rom);
                if(emulator.InputMaskForTest!=0)throw new InvalidOperationException("ROM load must clear previous input state.");
                _ = WaitFrame(emulator);
            }
            foreach(string phase in new[]{"fresh-instance","same-instance-switch"})
            {
                using var shared=phase=="same-instance-switch"?new NesEmulator():null;
                for(int round=0;round<rounds;round++)
                foreach(var input in inputs)
                {
                    using var owned=shared is null?new NesEmulator():null;
                    var emulator=shared??owned!;
                    Checkpoint("before-load");
                    emulator.Load(input.Rom);
                    bool smb=input.Id.StartsWith("smb-",StringComparison.Ordinal);
                    if(mixed&&(emulator.IsSmbWorld!=smb||emulator.InputMaskForTest!=0||emulator.SmbInjuryFullRatePairsForTest!=0))
                        throw new InvalidOperationException("Cartridge load must reset SMB identity, input and injury capture counter.");
                    Checkpoint("await-native-video");
                    NesFrame first=WaitFrame(emulator);
                    emulator.SetButton(NesButton.Right,true);Thread.Sleep(80);
                    int? secondPortRight=null;
                    if(mixed&&smb)
                    {
                        // SMB's actual native ReadJoypads stores port two at
                        // $06fd. NES serial order makes Right bit0 here.
                        secondPortRight=WaitSmbPortTwo(emulator,1);
                    }
                    emulator.SetButton(NesButton.Right,false);
                    int? secondPortReleased=mixed&&smb?WaitSmbPortTwo(emulator,0):null;
                    Checkpoint("before-pause");
                    if(!emulator.TogglePause())throw new InvalidOperationException("Pause must apply.");
                    Thread.Sleep(40); // native Pause sets a request flag; acknowledgement is asynchronous
                    NesFrame paused=WaitFrame(emulator);
                    if(emulator.CaptureFrame() is not {} again||!paused.Ram.SequenceEqual(again.Ram))throw new InvalidOperationException("Paused RAM must remain stable.");
                    if(smb||input.Id is "icarus" or "castlevania" or "smb2" or "smb3" or "celeste" or "tetris" or "metroid" or "contra" or "megaman2")
                        if(paused.CaptureScanline!=96||paused.Sequence!=paused.NativeScreenSequence)throw new InvalidOperationException("Coherent native frame required.");
                    bool stateInputCleared=false;
                    if(mixed&&smb)
                    {
                        emulator.SaveState(1);emulator.SetInputMask((int)(NesButton.Right|NesButton.A));
                        emulator.LoadState(1);
                        if(emulator.InputMaskForTest!=0)throw new InvalidOperationException("State load must clear held input.");
                        if(emulator.TogglePause())throw new InvalidOperationException("Resume after state load must apply.");
                        _=WaitSmbPortTwo(emulator,0);stateInputCleared=true;
                        if(!emulator.TogglePause())throw new InvalidOperationException("Restore pause before switch.");
                        Thread.Sleep(40);
                    }
                    if(mixed&&!smb&&emulator.SmbInjuryFullRatePairsForTest!=0)
                        throw new InvalidOperationException("Other games must never enter SMB full-rate injury capture.");
                    // Alternate running/paused disposal and running/paused switch.
                    if(round%2==1&&emulator.TogglePause())throw new InvalidOperationException("Resume must apply.");
                    Checkpoint("before-dispose-or-switch");
                    if(owned is not null)owned.Dispose();
                    if(emulator.InputMaskForTest!=0)throw new InvalidOperationException("Input must be released.");
                    completed.Add(new{Phase=phase,Round=round,Game=input.Id,first.Sequence,PausedSequence=paused.Sequence,PausedAtEnd=round%2==0,
                        MixedSmbChecks=mixed,secondPortRight,secondPortReleased,stateInputCleared,SmbInjuryPairs=emulator.SmbInjuryFullRatePairsForTest});
                    File.WriteAllText(Path.Combine(output,"completed.json"),JsonSerializer.Serialize(completed,new JsonSerializerOptions{WriteIndented=true}));
                    // Stress delegate collection after unregistering the callback.
                    if(shared is null){GC.Collect();GC.WaitForPendingFinalizers();}
                    void Checkpoint(string step)=>File.WriteAllText(Path.Combine(output,"checkpoint.json"),JsonSerializer.Serialize(new{Phase=phase,Round=round,Game=input.Id,Step=step,Completed=completed.Count,TimeUtc=DateTime.UtcNow}));
                }
            }
            long ordinaryTraceFailures=NesEmulator.TraceWriteFailuresForTest;
            if(ordinaryTraceFailures!=0)throw new InvalidOperationException("Ordinary diagnostic trace must not lose writes.");
            // Intentionally invalid sink: a directory, not a log file. Optional
            // diagnostics must not stop startup, native video or shutdown.
            Environment.SetEnvironmentVariable("WARP4D_TRACE",Path.Combine(Path.GetFullPath(output),"isolated-data"));
            using(var emulator=new NesEmulator()){emulator.Load(inputs[0].Rom);_ = WaitFrame(emulator);}
            if(NesEmulator.TraceWriteFailuresForTest<=ordinaryTraceFailures)throw new InvalidOperationException("Invalid trace sink exercised.");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Scope="Repeated native load/capture/pause/resume/dispose and same-instance ROM switching. Diagnostic evidence, not proof that an intermittent crash is fixed.",RoundsPerPhase=rounds,Cartridges=inputs.Select(i=>i.Id),Completed=completed.Count,NoRomAutoload=true,CoherentFrames=true,PausedRamStable=true,DelegateGcStress=true,OverlappingSessionRejected=true,ActiveConsoleUnaffected=true,OwnerReleaseAllowsReuse=true,QueuedInputSurvivesShutdown=true,RomLoadClearsHeldInput=true,OrdinaryTraceFailures=ordinaryTraceFailures,InvalidTraceSinkNonFatal=true,SuppressedTraceFailures=NesEmulator.TraceWriteFailuresForTest-ordinaryTraceFailures},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception exception){File.WriteAllText(Path.Combine(output,"error.txt"),exception.ToString());return 1;}
        finally{Environment.SetEnvironmentVariable("WARP4D_HOME",previousHome);Environment.SetEnvironmentVariable("WARP4D_TRACE",previousTrace);}
    }
    private static int WaitSmbPortTwo(NesEmulator emulator,int expected)
    {
        Stopwatch timeout=Stopwatch.StartNew();
        long before=emulator.CaptureFrame()?.Sequence??-1;
        while(timeout.ElapsedMilliseconds<2000)
        {
            Thread.Sleep(20);
            if(emulator.CaptureFrame() is {} frame&&frame.Sequence>before&&frame.Ram[0x6fd]==expected)return expected;
        }
        throw new InvalidOperationException($"Native SMB port-two joypad latch must become {expected}.");
    }
    private static NesFrame WaitFrame(NesEmulator emulator)
    {
        var timeout=Stopwatch.StartNew();
        while(timeout.ElapsedMilliseconds<2500)
        {
            if(emulator.CaptureFrame() is {NativeScreenPixels.Length:61440} frame)return frame;
            Thread.Sleep(5);
        }
        throw new TimeoutException("Native video did not become ready.");
    }
}
