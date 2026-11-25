using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

public static class NovemberCompatibility
{
    private static MethodInfo controllerInstance, worldSettings, partySettings;
    private static MethodInfo lunaRectValidator;
    [ThreadStatic] private static int pirateGenerationDepth;
    public static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(MapDrawer), "Dispose"), prefix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(DisposeIncompleteDrawer)));
        h.Patch(AccessTools.Method(typeof(MapDrawer), "RegenerateEverythingNow"), prefix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(DrawerCanRegenerate)));
        Log.Message("[NeloFixes] Installed incomplete-map disposal and disposed-map regeneration guards.");
        lunaRectValidator = AccessTools.Method("KiiroValor.GenStep_LunaStargazingPoint:CanPlaceSiteRect");
        var findLunaRect = AccessTools.Method("KiiroValor.GenStep_LunaStargazingPoint:FindSiteRect");
        if (lunaRectValidator != null && findLunaRect != null)
        {
            h.Patch(lunaRectValidator, postfix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(LunaRectHasExit)));
            h.Patch(findLunaRect, postfix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(LunaFallbackRect)));
            Log.Message("[NeloFixes] Installed Luna encounter accessible-site selection checks.");
        }
        var pirateGen = AccessTools.Method("MiliraImperium.MI_GenStep_MiliraPirateOutpost:Generate");
        var spawnPrefab = AccessTools.Method(typeof(RimWorld.PrefabUtility), "SpawnPrefab");
        if (pirateGen != null && spawnPrefab != null)
        {
            h.Patch(pirateGen, prefix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(BeginPirateGeneration)), finalizer: new HarmonyMethod(typeof(NovemberCompatibility), nameof(EndPirateGeneration)));
            h.Patch(spawnPrefab, prefix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(BeginPiratePrefab)), finalizer: new HarmonyMethod(typeof(NovemberCompatibility), nameof(EndPiratePrefab)));
            Log.Message("[NeloFixes] Installed Milira pirate-prefab region-update batching.");
        }
        var infantType = AccessTools.TypeByName("DragonianBabyEvent.QuestNode_Root_LostDragonianInfant");
        var newGenerator = AccessTools.Method("RimWorld.QuestGen.QuestNode_Root_WandererJoin:GeneratePawn_NewTemp");
        if (infantType != null && newGenerator != null)
            h.Patch(newGenerator, prefix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(DragonianInfantGeneratorBridge)));
        var face = AccessTools.Method("FacialAnimation.FacialAnimationModSettings:Clone");
        if (face != null) h.Patch(face, prefix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(CloneFaceSettings)));
        var faceSame = AccessTools.Method("FacialAnimation.FacialAnimationModSettings:IsSame");
        if (faceSame != null) h.Patch(faceSame, prefix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(SameFaceSettings)));
        var extract = AccessTools.Method("Cumpilation.Leaking.Recipe_ExtractCum:AvailableOnNow");
        if (extract != null) h.Patch(extract, prefix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(SafeExtractionAvailability)));
        var controller = AccessTools.TypeByName("AllowTool.AllowToolController");
        var settings = AccessTools.TypeByName("AllowTool.Settings.WorldSettings");
        var party = AccessTools.Method("AllowTool.PartyHuntHandler:DoBehaviorForPawn");
        if (controller != null && settings != null && party != null)
        {
            controllerInstance = AccessTools.PropertyGetter(controller, "Instance");
            worldSettings = AccessTools.PropertyGetter(controller, "WorldSettings");
            partySettings = AccessTools.PropertyGetter(settings, "PartyHunt");
            if (controllerInstance != null && worldSettings != null && partySettings != null)
                h.Patch(party, prefix: new HarmonyMethod(typeof(NovemberCompatibility), nameof(PartyHuntReady)));
        }
        Log.Message("[NeloFixes] Installed scoped FA settings clone, read-only Cumpilation recipe query and AllowTool world-settings readiness fixes.");
    }
    public static void BeginPirateGeneration() { pirateGenerationDepth++; }
    public static Exception EndPirateGeneration(Exception __exception)
    {
        pirateGenerationDepth = Math.Max(0, pirateGenerationDepth - 1);
        return __exception;
    }
    public static void BeginPiratePrefab(Map __1, ref bool? __state)
    {
        __state = null;
        if (pirateGenerationDepth == 0 || __1?.regionAndRoomUpdater == null) return;
        __state = __1.regionAndRoomUpdater.Enabled;
        // Bed.SpawnSetup queries rooms during a partially placed prefab. Delay those
        // rebuilds until the complete layout exists, retaining all dirty-cell notices.
        __1.regionAndRoomUpdater.Enabled = false;
    }
    public static Exception EndPiratePrefab(Map __1, bool? __state, Exception __exception)
    {
        if (__state.HasValue && __1?.regionAndRoomUpdater != null && !__1.Disposed)
            __1.regionAndRoomUpdater.Enabled = __state.Value;
        return __exception;
    }
    public static bool RectHasMapExit(CellRect rect, Map map)
    {
        if (map?.reachability == null || map.Disposed) return false;
        var traverse = TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Deadly);
        foreach (var cell in rect.ExpandedBy(1).EdgeCells)
            if (cell.InBounds(map) && cell.Standable(map) && map.reachability.CanReachMapEdge(cell, traverse)) return true;
        return false;
    }
    public static void LunaRectHasExit(CellRect __0, Map __1, ref bool __result)
    {
        if (__result) __result = RectHasMapExit(__0, __1);
    }
    public static void LunaFallbackRect(Map __0, ref CellRect __result)
    {
        if (__0 == null || RectHasMapExit(__result, __0)) return;
        // The author's last-resort center rectangle bypasses CanPlaceSiteRect.
        // Search outside the original center radius before accepting that fallback.
        for (int x = 22; x < __0.Size.x - 22; x += 5)
            for (int z = 22; z < __0.Size.z - 22; z += 5)
            {
                var candidate = CellRect.CenteredOn(new IntVec3(x, 0, z), 20, 20);
                if ((bool)lunaRectValidator.Invoke(null, new object[] { candidate, __0 }))
                {
                    __result = candidate;
                    return;
                }
            }
        Log.Warning("[NeloFixes] Luna map has no accessible valid camp rectangle; retaining original fallback, not carving a path through terrain.");
    }
    public static bool DrawerCanRegenerate(Map ___map) => ___map != null && !___map.Disposed;
    public static bool DisposeIncompleteDrawer(ref Section[,] ___sections, List<MapDrawLayer> ___global)
    {
        bool incomplete = ___sections == null || ___global == null;
        if (!incomplete)
            foreach (var section in ___sections)
                if (section == null) { incomplete = true; break; }
        if (!incomplete) return true;
        // A cancelled generation can dispose a map before deferred drawing creates
        // its sections. Release existing resources, without dereferencing empty slots.
        if (___sections != null)
            foreach (var section in ___sections) section?.Dispose();
        ___sections = null;
        if (___global != null)
        {
            foreach (var layer in ___global) layer?.Dispose();
            ___global.Clear();
        }
        return false;
    }
    public static bool DragonianInfantGeneratorBridge(object __instance, ref Pawn __result)
    {
        if (__instance?.GetType().FullName != "DragonianBabyEvent.QuestNode_Root_LostDragonianInfant") return true;
        // 1.7 calls the new Map-taking virtual entry point; this mod still overrides
        // the old no-argument generator. Reuse that generator before quest parts
        // and slate references are created, rather than creating a second pawn later.
        var oldGenerator = AccessTools.DeclaredMethod(__instance.GetType(), "GeneratePawn", Type.EmptyTypes);
        if (oldGenerator == null || oldGenerator.ReturnType != typeof(Pawn)) return true;
        __result = (Pawn)oldGenerator.Invoke(__instance, null);
        return false;
    }
    public static bool CloneFaceSettings(object __instance, ref object __result)
    {
        __result = AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(__instance, null);
        // Settings are scalar fields plus a mutable string list. Keep engine/Def caches
        // as references, as Unity assets must never be JSON-serialized or duplicated.
        foreach (var field in __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (field.FieldType == typeof(List<string>) && field.GetValue(__instance) is List<string> list)
                field.SetValue(__result, new List<string>(list));
        return false;
    }
    public static bool SafeExtractionAvailability(Thing __0, ref bool __result)
    {
        var pawn = __0 as Pawn;
        var hediff = pawn?.health?.hediffSet?.hediffs.FirstOrDefault(h => h.def?.defName == "Cumpilation_Cumflation");
        if (hediff != null && hediff.Severity > 0f && hediff.Part != null && !pawn.health.hediffSet.PartIsMissing(hediff.Part)) return true;
        __result = false;
        return false;
    }
    public static bool SameFaceSettings(object __instance, object __0, ref bool __result)
    {
        __result = false;
        if (__0 == null || __0.GetType() != __instance.GetType()) return false;
        foreach (var field in __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var left = field.GetValue(__instance); var right = field.GetValue(__0);
            if (ReferenceEquals(left, right)) continue;
            if (left is List<string> a && right is List<string> b)
            { if (!a.SequenceEqual(b)) return false; }
            else if (!Equals(left, right)) return false;
        }
        __result = true;
        return false;
    }
    public static bool PartyHuntReady()
    {
        var controller = controllerInstance?.Invoke(null, null);
        var settings = controller == null ? null : worldSettings?.Invoke(controller, null);
        return settings != null && partySettings?.Invoke(settings, null) != null;
    }
}
