#Requires -Version 7.6
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CecilDll
)

$ErrorActionPreference = 'Stop'
$comparator = Join-Path $PSScriptRoot '../tools/Compare-Diagnostics.ps1'
$cecil = (Resolve-Path -LiteralPath $CecilDll).Path
Add-Type -LiteralPath $cecil

$folder = Join-Path ([IO.Path]::GetTempPath()) ('RimGPT-handler-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$reference = Join-Path $folder 'reference.dll'
$unchanged = Join-Path $folder 'unchanged.dll'
$changed = Join-Path $folder 'changed.dll'

try {
    Add-Type -OutputAssembly $reference -TypeDefinition @'
using System;
public static class DiagnosticsComparisonFixture
{
    public static int Run(Action action)
    {
        try { action(); return 0; }
        catch (ArgumentException) { return 1; }
    }
}
'@
    Copy-Item -LiteralPath $reference -Destination $unchanged
    $result = & $comparator -InstalledDll $reference -StagedDll $unchanged -CecilDll $cecil | ConvertFrom-Json
    if ($result.differences -ne 0 -or $result.installedMethods -ne 1) {
        throw 'The byte-identical comparison fixture did not match.'
    }

    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($reference)
    try {
        $method = $assembly.MainModule.GetType('DiagnosticsComparisonFixture').Methods[0]
        $handler = $method.Body.ExceptionHandlers[0]
        if ($handler.CatchType.FullName -ne 'System.ArgumentException') { throw 'Unexpected fixture catch type.' }
        $handler.CatchType = $assembly.MainModule.ImportReference([InvalidOperationException])
        $assembly.Write($changed)
    } finally {
        $assembly.Dispose()
    }

    $result = $null
    $failure = $null
    try {
        & $comparator -InstalledDll $reference -StagedDll $changed -CecilDll $cecil |
            ForEach-Object { $result = $_ | ConvertFrom-Json }
    } catch {
        $failure = $_
    }
    if (!$failure -or $result.differences -ne 2 -or
        @($result.methods | Where-Object Method -Like '*DiagnosticsComparisonFixture::Run*').Count -ne 2) {
        throw 'Changing only the catch type did not produce a method difference.'
    }

    'PASS unchanged method matches and a catch-type-only change is detected.'
} finally {
    foreach ($file in @($reference, $unchanged, $changed)) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    Remove-Item -LiteralPath $folder
}
