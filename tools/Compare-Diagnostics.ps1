#Requires -Version 7.6
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstalledDll,
    [Parameter(Mandatory)][string]$StagedDll,
    [Parameter(Mandatory)][string]$CecilDll
)

$ErrorActionPreference = 'Stop'
$paths = @($InstalledDll, $StagedDll, $CecilDll) | ForEach-Object {
    (Resolve-Path -LiteralPath $_).Path
}
if ($paths[0] -eq $paths[1]) { throw 'Supply two different assembly files.' }
Add-Type -LiteralPath $paths[2]

function Get-MethodBodyRecords($Types) {
    foreach ($type in $Types) {
        foreach ($method in $type.Methods) {
            if ($method.HasBody) {
                [pscustomobject]@{
                    Method = $method.FullName
                    Instructions = ($method.Body.Instructions | ForEach-Object ToString) -join [Environment]::NewLine
                }
            }
        }
        Get-MethodBodyRecords $type.NestedTypes
    }
}

$installed = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($paths[0])
try {
    $staged = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($paths[1])
    try {
        $before = @(Get-MethodBodyRecords $installed.MainModule.Types)
        $after = @(Get-MethodBodyRecords $staged.MainModule.Types)
        if ($before.Count -eq 0 -or $after.Count -eq 0) { throw 'An assembly has no comparable method bodies.' }
        $difference = @(Compare-Object $before $after -Property Method, Instructions -CaseSensitive)
        [ordered]@{
            installedMethods = $before.Count
            stagedMethods = $after.Count
            differences = $difference.Count
            methods = @($difference | Select-Object Method, SideIndicator)
        } | ConvertTo-Json -Depth 4
        if ($difference.Count) { throw 'Method signatures or instructions differ from the installed assembly.' }
    } finally {
        $staged.Dispose()
    }
} finally {
    $installed.Dispose()
}
