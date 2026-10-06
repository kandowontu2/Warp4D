using System.Diagnostics;
using System.Text.Json;
using Warp4D.Emulation;
using Warp4D.Profiles;

namespace Warp4D;

internal static class NametablePixelSnapshotTests
{
    internal static int Run(string output,string? nativeManifest=null)
    {
        Directory.CreateDirectory(output);
        string? previous=Environment.GetEnvironmentVariable("WARP4D_HOME");
        Environment.SetEnvironmentVariable("WARP4D_HOME",Path.Combine(Path.GetFullPath(output),"isolated-data"));
        try
        {
            NametablePixelSnapshots snapshots=new();List<(int[] Published,int[] Expected)> held=[];int checks=0;
            foreach(int phase in Enumerable.Range(0,12))
            for(int table=0;table<4;table++)
            {
                int[] scratch=snapshots.Scratch(table);
                if(phase==0)Array.Fill(scratch,unchecked((int)0xff123456)+table);
                if(phase==2)scratch[0]^=0x00000101;
                if(phase==4)scratch[^1]^=0x01000001;
                if(phase==6)Array.Fill(scratch,unchecked((int)0xffabcdef)+table);
                if(phase==8)Array.Fill(scratch,unchecked((int)0xff123456)+table);
                if(phase==10)Array.Clear(scratch);
                int[] expected=(int[])scratch.Clone(),published=snapshots.Publish(table);
                Require(published.AsSpan().SequenceEqual(expected),"All raw ARGB bits exact, including alpha-zero RGB and final pixel");
                foreach(var old in held)Require(old.Published.AsSpan().SequenceEqual(old.Expected),"Later native scratch writes never mutate an old frame");
                held.Add((published,expected));
                Require(ReferenceEquals(published,snapshots.Publish(table)),"Exact repeated content uses same immutable snapshot");
                checks++;
            }
            Require(snapshots.ScratchPixels==4*61440&&snapshots.RetainedPixels==4*61440,"Bounded four private scratch/four current images");
            // Published arrays are not writable native destinations, even when
            // an external diagnostic deliberately modifies one of them.
            snapshots.Scratch(0)[17]=0x00010203;
            int[] first=snapshots.Publish(0);first[17]=0x00040506;
            int[] repaired=snapshots.Publish(0);
            Require(!ReferenceEquals(first,repaired)&&repaired[17]==0x00010203,"Changed externally-owned published content cannot validate a hit");
            snapshots.Clear();Require(snapshots.ScratchPixels==0&&snapshots.RetainedPixels==0,"Reset releases native staging and current snapshots");
            for(int table=0;table<4;table++){Array.Fill(snapshots.Scratch(table),table);snapshots.Publish(table);}
            long start=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<128;i++)for(int table=0;table<4;table++)snapshots.Publish(table);
            long reused=GC.GetAllocatedBytesForCurrentThread()-start;
            start=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<128;i++)for(int table=0;table<4;table++)_=(int[])snapshots.Scratch(table).Clone();
            long original=GC.GetAllocatedBytesForCurrentThread()-start;
            Require(reused<original/100,"Exact hits avoid large decoded-table publication arrays");
            int nativeGames=nativeManifest is null?0:CheckNative(nativeManifest);
            using NesEmulator defaults=new();
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,OriginalAllocatedBytes=original,ReusedAllocatedBytes=reused,AllocationIterations=128,NativeGames=nativeGames,NativeSnapshotVariantsPerGame=3,MaxScratchPixels=245760,MaxRetainedPixels=245760,AllPixelsCompared=true,OldArraysImmutable=true,ResetReleasesAllSlots=true,ExternalMutationInvalidates=true,ProductionEnabled=defaults.UseNametablePixelReuseForTest,Scope="Exact snapshot publication/lifecycle and optional paused native decoder equivalence. Not full gameplay or physical display acceptance."},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
        finally{Environment.SetEnvironmentVariable("WARP4D_HOME",previous);}
    }

    private static int CheckNative(string manifest)
    {
        var inputs=JsonSerializer.Deserialize<BuiltInProfileTests.Input[]>(File.ReadAllText(manifest))!;
        Require(inputs.Length==9,"All nine explicit cartridge inputs required");
        using NesEmulator emulator=new();
        foreach(var input in inputs)
        {
            Require(BuiltInGameProfiles.Identify(File.ReadAllBytes(input.Rom))?.Id==input.Id,"Exact native cartridge identity");
            emulator.Load(input.Rom);
            Stopwatch timer=Stopwatch.StartNew();
            while(emulator.CaptureFrame()?.NativeScreenPixels?.Length!=61440){Require(timer.ElapsedMilliseconds<3000,"Native pair ready");Thread.Sleep(5);}
            Require(emulator.TogglePause(),"Native pause applies");
            Thread.Sleep(80);
            var before=emulator.CaptureFrame()!;Thread.Sleep(40);var stable=emulator.CaptureFrame()!;
            Require(before.Sequence==stable.Sequence,"Pause acknowledged before exact decoder comparison");
            var original=emulator.ReadPausedNametablesForTest(false);
            var candidate=emulator.ReadPausedNametablesForTest(true);
            var repeated=emulator.ReadPausedNametablesForTest(true);
            for(int table=0;table<4;table++)
            {
                Require(original.Pixels[table].AsSpan().SequenceEqual(candidate.Pixels[table])&&candidate.Pixels[table].AsSpan().SequenceEqual(repeated.Pixels[table]),"All native decoded table ARGB exact");
                Require(original.Tiles[table].AsSpan().SequenceEqual(candidate.Tiles[table])&&original.Attributes[table].AsSpan().SequenceEqual(candidate.Attributes[table]),"Native tiles and attributes remain fresh/exact");
                Require(ReferenceEquals(candidate.Pixels[table],repeated.Pixels[table])&&!ReferenceEquals(candidate.Tiles[table],repeated.Tiles[table]),"Only exact pixel content shared, not mutable metadata");
            }
        }
        return inputs.Length;
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
}
