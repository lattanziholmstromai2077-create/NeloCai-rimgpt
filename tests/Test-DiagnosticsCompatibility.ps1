#Requires -Version 7.6
param(
    [Parameter(Mandatory)][string]$FixPath,
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$WorkshopRoot
)
$ErrorActionPreference='Stop'
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($sender,$event) if($event.Name.StartsWith('UnityEngine.')){return [Reflection.Assembly]::LoadFrom("$GameRoot/UnityStubs.dll")}})
Add-Type -Path "$WorkshopRoot/2009463077/1.7/Assemblies/0Harmony.dll"
foreach($p in @("$GameRoot/UnityStubs.dll","$GameRoot/Assembly-CSharp.dll","$WorkshopRoot/839005762/1.7/Assemblies/AlienRace.dll","$WorkshopRoot/1635901197/1.6/Assemblies/FacialAnimation.dll",(Resolve-Path -LiteralPath $FixPath).Path)){[Reflection.Assembly]::LoadFrom($p)|Out-Null}
$type=[HarmonyLib.AccessTools]::TypeByName('NovemberCompatibility')
if(!$type){throw 'FAIL: no settings-clone compatibility repair'}
$face=[HarmonyLib.AccessTools]::TypeByName('FacialAnimation.FacialAnimationModSettings')
$instance=[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($face)
$list=[Collections.Generic.List[string]]::new();$list.Add('Human')
$face.GetField('IgnoreEnableAnimationForRaceXenoTypeList').SetValue($instance,$list)
$face.GetField('DarkSkinThreshold').SetValue($instance,42)
$argsToCall=[object[]]::new(2);$argsToCall[0]=$instance
if($type.GetMethod('CloneFaceSettings').Invoke($null,$argsToCall)){throw 'FAIL: unsafe JSON cloning remains active'}
$clone=$argsToCall[1]
if(!$type.GetMethod('SameFaceSettings')){throw 'FAIL: settings equality still serializes the engine object graph'}
$sameArgs=[object[]]::new(3);$sameArgs[0]=$instance;$sameArgs[1]=$clone;$sameArgs[2]=$false
if($type.GetMethod('SameFaceSettings').Invoke($null,$sameArgs) -or !$sameArgs[2]){throw 'FAIL: equivalent settings compare differently'}
if(!$clone -or [object]::ReferenceEquals($clone,$instance) -or $face.GetField('DarkSkinThreshold').GetValue($clone) -ne 42){throw 'FAIL: setting values not copied'}
$clonedList=$face.GetField('IgnoreEnableAnimationForRaceXenoTypeList').GetValue($clone)
$clonedList.Add('Ratkin')
if($list.Count -ne 1 -or $clonedList.Count -ne 2){throw 'FAIL: mutable list shared with original'}
$sameArgs[2]=$true
[void]$type.GetMethod('SameFaceSettings').Invoke($null,$sameArgs)
if($sameArgs[2]){throw 'FAIL: changed settings list was ignored'}
$h=[HarmonyLib.Harmony]::new('nelo.test.faceclone')
$cloneMethod=[HarmonyLib.AccessTools]::Method($face,'Clone')
[void]$h.Patch($cloneMethod,[HarmonyLib.HarmonyMethod]::new($type.GetMethod('CloneFaceSettings')))
$actual=$cloneMethod.Invoke($instance,@())
if(!$actual -or $actual.GetType() -ne $face -or [object]::ReferenceEquals($actual,$instance)){throw 'FAIL: actual Facial Animation Clone hook failed'}
$pawn=[Verse.Pawn]::new();$health=[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject([Verse.Pawn_HealthTracker]);$set=[Verse.HediffSet]::new($pawn)
[Verse.Pawn].GetField('health').SetValue($pawn,$health);[Verse.Pawn_HealthTracker].GetField('hediffSet').SetValue($health,$set)
$before=$set.hediffs.Count
$argsToCall=[object[]]::new(2);$argsToCall[0]=$pawn;$argsToCall[1]=$true
if($type.GetMethod('SafeExtractionAvailability').Invoke($null,$argsToCall) -or $argsToCall[1] -or $set.hediffs.Count -ne $before){throw 'FAIL: surgery listing mutated an unaffected pawn'}
$part=[Verse.BodyPartRecord]::new();$hediff=[Verse.Hediff]::new();$def=[Verse.HediffDef]::new();[Verse.Def].GetField('defName').SetValue($def,'Cumpilation_Cumflation')
[Verse.Hediff].GetField('def').SetValue($hediff,$def)
[HarmonyLib.AccessTools]::Field([Verse.Hediff],'part').SetValue($hediff,$part)
[HarmonyLib.AccessTools]::Field([Verse.Hediff],'severityInt').SetValue($hediff,[single]0.5)
$set.hediffs.Add($hediff);$argsToCall[1]=$false
if(!$type.GetMethod('SafeExtractionAvailability').Invoke($null,$argsToCall) -or $set.hediffs.Count -ne 1){throw 'FAIL: valid existing state no longer reaches original recipe checks'}
$missing=[Verse.Hediff_MissingPart]::new();[HarmonyLib.AccessTools]::Field([Verse.Hediff],'part').SetValue($missing,$part);$set.hediffs.Add($missing);$argsToCall[1]=$true
if($type.GetMethod('SafeExtractionAvailability').Invoke($null,$argsToCall) -or $argsToCall[1] -or $set.hediffs.Count -ne 2){throw 'FAIL: missing body part not excluded without mutation'}
'PASS: actual FA Clone hook works and lists remain independent; surgery query is read-only, preserves valid states and excludes missing parts.'
