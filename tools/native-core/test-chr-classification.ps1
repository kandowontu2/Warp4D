param([Parameter(Mandatory=$true)][string]$Library,[Parameter(Mandatory=$true)][int]$ExpectedChecks)
$ErrorActionPreference='Stop'
$resolvedLibrary=(Resolve-Path -LiteralPath $Library).Path
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ChrClassificationProbe {
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate uint Probe();
    public static uint Run(string library) {
        IntPtr handle=NativeLibrary.Load(library);
        try {return Marshal.GetDelegateForFunctionPointer<Probe>(NativeLibrary.GetExport(handle,"Warp4DTestChrRamAddressClassification"))();}
        finally {NativeLibrary.Free(handle);}
    }
}
'@
$actualChecks=[ChrClassificationProbe]::Run($resolvedLibrary)
if($actualChecks -ne $ExpectedChecks){throw "Expected $ExpectedChecks address checks; got $actualChecks."}
[pscustomobject]@{Library=$resolvedLibrary;Checks=$actualChecks;ExpectedChecks=$ExpectedChecks;Passed=$true} | ConvertTo-Json
