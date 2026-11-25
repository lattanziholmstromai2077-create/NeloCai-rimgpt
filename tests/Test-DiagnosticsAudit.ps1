#Requires -Version 7.6
param(
    [Parameter(Mandatory)][string]$FixPath,
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$WorkshopRoot
)
$ErrorActionPreference='Stop'
$g=$GameRoot
$s=$WorkshopRoot
foreach($f in "$g/UnityStubs.dll","$g/Assembly-CSharp.dll","$s/839005762/1.7/Assemblies/AlienRace.dll","$s/2009463077/1.7/Assemblies/0Harmony.dll"){
 [Reflection.Assembly]::LoadFrom($f) | Out-Null
}
$fix=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $FixPath).Path)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$policy=$fix.GetType('NeloCompatibilityMod',$true)
$guard=$policy.GetMethod('DiagnosticsRequested',$flags)
$tmp=Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $FixPath).Path) ('test-audit-marker-' + [Guid]::NewGuid().ToString('N'))
$dir=Join-Path $tmp 'NeloLocalFixes'
New-Item -ItemType Directory -Path $dir -Force | Out-Null
if($guard.Invoke($null,@([string](Join-Path $tmp 'nonexistent')))){throw 'Default audit enabled'}
New-Item -ItemType File -Path (Join-Path $dir 'enable-audits.txt') -Force | Out-Null
if(-not $guard.Invoke($null,@([string]$tmp))){throw 'Explicit audit toggle did not work'}

function Make-ApparelDef($id,$packageId,$armor){
 $def=[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([Verse.ThingDef])
 $mod=[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([Verse.ModContentPack])
 [Verse.ModContentPack].GetField('packageIdInt',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($mod,$packageId)
 [Verse.Def].GetField('defName').SetValue($def,$id)
 [Verse.Def].GetField('modContentPack').SetValue($def,$mod)
 [Verse.ThingDef].GetField('apparel').SetValue($def,[RimWorld.ApparelProperties]::new())
 if($armor){
   $tags=[Collections.Generic.List[string]]::new()
   $tags.Add('Armor')
   [Verse.ThingDef].GetField('tradeTags').SetValue($def,$tags)
 }
 return $def
}
$ratkin=Make-ApparelDef 'AuditRatkin' 'solaris.ratkinracemod' $false
$dms=Make-ApparelDef 'AuditDms' 'aoba.deadmanswitch.core' $true
$other=Make-ApparelDef 'AuditOther' 'other.test' $true
[Verse.DefDatabase[Verse.ThingDef]]::Add($ratkin)
[Verse.DefDatabase[Verse.ThingDef]]::Add($dms)
[Verse.DefDatabase[Verse.ThingDef]]::Add($other)
[Runtime.CompilerServices.RuntimeHelpers]::RunClassConstructor($fix.GetType('NeloRepairDiagnostics',$true).TypeHandle)
$clothes=$policy.GetField('RatkinApparel').GetValue($null)
$armor=$policy.GetField('DmsArmorProperties').GetValue($null)
if($clothes.Count -ne 1 -or -not $clothes.Contains($ratkin)){throw 'Ratkin apparel policy regression'}
if($armor.Count -ne 1 -or -not $armor.Contains([Verse.ThingDef].GetField('apparel').GetValue($dms))){throw 'DMS armor policy regression'}
'PASS marker opt-in; functional Ratkin/DMS caches populate with audits off and missing optional race def.'
