#Requires -Version 7.6
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$WorkshopRoot,
    [Parameter(Mandatory)][string]$UserDataRoot,
    [Parameter(Mandatory)][string]$UapLocalizationSource,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $PSScriptRoot '../src/NeloRepairDiagnostics'
$uapSource = (Resolve-Path -LiteralPath $UapLocalizationSource).Path
$expectedUap = 'B98C1938A7881D6A0A02ADF25AE99E704E2FCE95DA32A01419CAAFFDFC2022A5'
if ((Get-FileHash -LiteralPath $uapSource).Hash -ne $expectedUap) {
    throw 'The local UAP source does not match the archived baseline.'
}
$sources = @(
    (Join-Path $sourceRoot 'Diagnostics.cs')
    $uapSource
    (Join-Path $sourceRoot 'NovemberCompatibility.cs')
)
$dependencies = @(
    (Join-Path $GameRoot 'Assembly-CSharp.dll')
    (Join-Path $GameRoot 'UnityStubs.dll')
    (Join-Path $WorkshopRoot '839005762/1.7/Assemblies/AlienRace.dll')
    (Join-Path $WorkshopRoot '2009463077/1.7/Assemblies/0Harmony.dll')
)
foreach ($file in ($sources + $dependencies)) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Missing build input: $file"
    }
}
$framework = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' -File).FullName
if (!$framework.Count) { throw 'PowerShell .NET reference assemblies are missing.' }

$stage = [IO.Path]::GetFullPath($OutputDirectory, (Get-Location).ProviderPath)
foreach ($root in @($GameRoot, $WorkshopRoot, $UserDataRoot)) {
    $rootPath = (Resolve-Path -LiteralPath $root).Path
    $relative = [IO.Path]::GetRelativePath($rootPath, $stage)
    $parentPrefix = '..' + [IO.Path]::DirectorySeparatorChar
    if (![IO.Path]::IsPathRooted($relative) -and $relative -ne '..' -and !$relative.StartsWith($parentPrefix)) {
        throw "Choose a staging directory outside $rootPath"
    }
}
$parent = Get-Item -LiteralPath (Split-Path -Parent $stage)
for ($directory = $parent; $directory; $directory = $directory.Parent) {
    if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Choose a staging location without directory links: $($directory.FullName)"
    }
}
if (Test-Path -LiteralPath $stage) { throw "Choose a fresh staging directory: $stage" }
New-Item -ItemType Directory -Path $stage | Out-Null

$output = Join-Path $stage 'NeloRepairDiagnostics.dll'
Add-Type -Path $sources -ReferencedAssemblies ($dependencies + $framework) -OutputAssembly $output -OutputType Library -CompilerOptions /nowarn:1701
[ordered]@{
    powerShell = $PSVersionTable.PSVersion.ToString()
    artifact   = (Get-FileHash -LiteralPath $output).Hash
    inputs     = @(Get-FileHash -LiteralPath ($sources + $dependencies) |
        Select-Object @{Name = 'File'; Expression = { [IO.Path]::GetFileName($_.Path) } }, Hash)
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'build-evidence.json') -Encoding utf8

Write-Output $output
