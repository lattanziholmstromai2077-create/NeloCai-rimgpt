using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using Verse;
using AlienRace;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

public class NeloCompatibilityMod : Mod
{
    public static readonly HashSet<ThingDef> RatkinApparel = new HashSet<ThingDef>();
    public static readonly HashSet<RimWorld.ApparelProperties> RatkinApparelProperties = new HashSet<RimWorld.ApparelProperties>();
    public static readonly HashSet<RimWorld.ApparelProperties> DmsArmorProperties = new HashSet<RimWorld.ApparelProperties>();
    [ThreadStatic] private static int apparelGenerationDepth;
    public static readonly Dictionary<Type, HashSet<string>> DeferredFields = new Dictionary<Type, HashSet<string>>();
    public NeloCompatibilityMod(ModContentPack content) : base(content)
    {
        var harmony = new Harmony("nelo.local.compatibilityfixes");
        UapLocalization.Install(harmony);
        NovemberCompatibility.Install(harmony);
        AuditsEnabled = DiagnosticsRequested(GenFilePaths.SaveDataFolderPath);
        if (AuditsEnabled)
        {
            harmony.Patch(AccessTools.Method(typeof(Game), "LoadGame"),
                postfix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(AuditLoadedRaidAndDraw)));
            harmony.Patch(AccessTools.Method(typeof(Thing), "DynamicDrawPhase"),
                transpiler: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(TraceDrawStages)));
        }
        harmony.Patch(AccessTools.Method(typeof(RimWorld.Pawn_RoyaltyTracker), "ExposeData"),
            prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(CleanOrphanRoyalRecordsOnLoad)));
        harmony.Patch(AccessTools.Method(typeof(RaceRestrictionSettings), nameof(RaceRestrictionSettings.CanWear)),
            postfix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(RatkinOnly)));
        harmony.Patch(AccessTools.Method(typeof(DefInjectionPackage), nameof(DefInjectionPackage.InjectIntoDefs)),
            prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(RepairPackedTranslations)));
        harmony.Patch(AccessTools.Method(typeof(RimWorld.PawnApparelGenerator), "GenerateStartingApparelFor"),
            prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(BeginApparelGeneration)),
            finalizer: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(EndApparelGeneration)));
        harmony.Patch(AccessTools.Method(typeof(RimWorld.ApparelProperties), "PawnCanWear", new[] { typeof(Pawn), typeof(bool) }),
            postfix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(FilterGeneratedApparel)));
        harmony.Patch(AccessTools.Method(typeof(Verse.AI.HaulAIUtility), "PawnCanAutomaticallyHaulFast_NewTemp"),
            prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(ValidHaulCandidate)));
        harmony.Patch(AccessTools.Method(typeof(RimWorld.StatPart_NoxiousHaze), "ActiveFor"),
            prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(ReadyHazeMap)));
        harmony.Patch(AccessTools.Method(typeof(RimWorld.StatsReportUtility), "DrawStatsWorker"),
            prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(RecoverStatsCache)));
        harmony.Patch(AccessTools.Method(typeof(RimWorld.QuestGen.QuestNode_Root_WandererJoin), "GeneratePawn_NewTemp"),
            prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(BridgeLegacyKiiroGenerator)));
        var arrivalPopulation = AccessTools.Method("RimWorld.PawnsArriveQuestPartUtility:IncreasesPopulation");
        if (arrivalPopulation != null)
            harmony.Patch(arrivalPopulation, prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(FilterArrivalPopulation)));
        harmony.Patch(AccessTools.Method(typeof(RimWorld.QuestPart_PawnsArrive), "ExposeData"),
            postfix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(CleanLoadedArrivalPawns)));
        var eroBenefits = AccessTools.Method("EROStuff.BookOutcomeDoerPornMagazine:GetBenefitsString");
        if (eroBenefits != null)
            harmony.Patch(eroBenefits, transpiler: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(TranslateEroBenefits)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Book), "DescriptionDetailed"),
            postfix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(EroDescriptionFallback)));
        var tentDraw = AccessTools.Method("FieldTents.TentHideDynamicContents:DrawVisible");
        if (tentDraw != null)
            harmony.Patch(tentDraw, prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(ValidTentDraw)));
        var extraRules = AccessTools.Method("SpeakUp.ExtraGrammarUtility:ExtraRulesForPawn");
        if (extraRules != null)
            harmony.Patch(extraRules, transpiler: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(SafeSpeakUpMoving)));
        var tribeAllowed = AccessTools.Method("BreakingNews.JobGiver_BNTribe:Allowed");
        var tribeMark = AccessTools.Method("BreakingNews.JobGiver_BNTribe:MarkUsed");
        if (tribeAllowed != null && tribeMark != null)
        {
            harmony.Patch(tribeAllowed, prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(GuardTribePick)));
            harmony.Patch(tribeMark, postfix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(RecordTribePick)));
        }
        var discard = AccessTools.Method("MiliraImperium.WorldComponent_MiliraDeathSkip:TryDiscardPawn");
        if (discard != null)
            harmony.Patch(discard, transpiler: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(ClearDiscardReferences)));
        harmony.Patch(AccessTools.Method(typeof(PawnRenderNode), "ColorFor"),
            prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(RepairAnimalNodeColor)));
        var plantBatch = AccessTools.PropertyGetter(AccessTools.TypeByName("GUIPerformancePatch.GppPlantLongRate"), "On");
        if (plantBatch != null)
            harmony.Patch(plantBatch, prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(DisablePlantBatch)));
        string[] assemblyNames = { "AncotLibrary", "AriandelLibrary", "MiliraImperium", "KiiroValor" };
        int patched = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => assemblyNames.Contains(a.GetName().Name)))
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!(typeof(Def).IsAssignableFrom(type) || typeof(AbilityCompProperties).IsAssignableFrom(type) ||
                    typeof(CompProperties).IsAssignableFrom(type) || typeof(HediffCompProperties).IsAssignableFrom(type) ||
                    type.FullName == "AncotLibrary.LetterProperty"))
                    continue;
                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var instructions = PatchProcessor.GetOriginalInstructions(ctor).ToList();
                    if (!instructions.Any(i => i.opcode == OpCodes.Ldsfld && i.operand is FieldInfo f && f.DeclaringType.Name.EndsWith("DefOf")))
                        continue;
                    harmony.Patch(ctor, transpiler: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(DeferDefaultReferences)));
                    patched++;
                }
            }
        }
        var alpha = AccessTools.Method("AlphaBiomes.AlphaBiomes_GenCelestial_CurCelestialSunGlow_Patch:AvoidTheLight");
        if (alpha != null)
            harmony.Patch(alpha, prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(ValidBiome)));
        var signals = AccessTools.Method("RimIris.Scenarios.ScenarioSignals:Refresh");
        if (signals != null)
            harmony.Patch(signals, prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(ValidCurrentScenario)));
        var roomRoleTranspiler = AccessTools.Method("RJW_Onahole.Patches.BulkPatches:GetScore_Transpiler");
        if (roomRoleTranspiler != null)
            harmony.Patch(roomRoleTranspiler, prefix: new HarmonyMethod(typeof(NeloCompatibilityMod), nameof(RepairRoomRoleTranspiler)));
        Log.Message("[NeloFixes] Deferred default references in " + patched + " constructors.");
    }

    public static void BeginApparelGeneration() { apparelGenerationDepth++; }
    public static void EndApparelGeneration() { apparelGenerationDepth = Math.Max(0, apparelGenerationDepth - 1); }

    public static bool ReadyHazeMap(Thing __0, ref bool __result)
    {
        var map = __0?.MapHeld; // Resolves the map before PositionHeld traverses Map.ParentHolder.
        if (map?.info != null && map.gameConditionManager != null) return true;
        __result = false;
        return false;
    }
    public static bool RecoverStatsCache(List<RimWorld.StatDrawEntry> ___cachedDrawEntries, List<string> ___cachedEntryValues)
    {
        if (___cachedDrawEntries.Count == ___cachedEntryValues.Count) return true;
        // A failed enumeration can leave one list populated without its matching value cache.
        // Skip this draw before BeginScrollView; next frame rebuilds both through vanilla code.
        ___cachedDrawEntries.Clear();
        ___cachedEntryValues.Clear();
        AccessTools.Field(typeof(RimWorld.StatsReportUtility), "selectedEntry").SetValue(null, null);
        AccessTools.Field(typeof(RimWorld.StatsReportUtility), "mousedOverEntry").SetValue(null, null);
        return false;
    }
    public static MethodInfo LegacyKiiroGenerator(Type type)
    {
        if (type?.FullName != "Kiiro_Event.QuestNode_Root_KiiroWandererJoin_WalkIn") return null;
        return type.GetMethod("GeneratePawn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null, Type.EmptyTypes, null);
    }
    public static IEnumerable<Pawn> ValidQuestPawns(IEnumerable<Pawn> pawns)
    {
        if (pawns == null) return Enumerable.Empty<Pawn>();
        if (pawns is IList<Pawn> list && !list.Contains(null)) return pawns;
        return pawns.Where(p => p != null);
    }
    public static void FilterArrivalPopulation(ref IEnumerable<Pawn> __0) { __0 = ValidQuestPawns(__0); }
    public static void CleanLoadedArrivalPawns(RimWorld.QuestPart_PawnsArrive __instance)
    {
        if (Scribe.mode != LoadSaveMode.PostLoadInit || __instance.pawns == null) return;
        int count = __instance.pawns.RemoveAll(p => p == null);
        if (count != 0) Log.Message("[NeloFixes] Removed " + count + " missing pawn reference(s) from a loaded arrival quest part.");
    }
    public static bool BridgeLegacyKiiroGenerator(object __instance, ref Pawn __result)
    {
        var old = LegacyKiiroGenerator(__instance.GetType());
        if (old == null) return true;
        __result = (Pawn)old.Invoke(__instance, null);
        return false;
    }
    public static string LocalizedEroLiteral(string key) { return key.Translate().ToString(); }
    public static IEnumerable<CodeInstruction> TranslateEroBenefits(IEnumerable<CodeInstruction> input)
    {
        foreach (var code in input)
        {
            if (code.opcode == OpCodes.Ldstr && Equals(code.operand, "Horny Factor"))
            {
                code.operand = "Nelo_Ero_DesireFactor";
                yield return code;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeloCompatibilityMod), nameof(LocalizedEroLiteral)));
            }
            else yield return code;
        }
    }
    public static void EroDescriptionFallback(Book __instance, ref string __result)
    {
        if (__instance.def?.defName != "RJW_lewd_magazine") return;
        __result = LocalizeLegacyEroDescription(__result, __instance.def.description, LocalizedEroLiteral("Nelo_Ero_DesireFactor"));
    }
    public static string LocalizeLegacyEroDescription(string text, string description, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return description;
        // Previously generated book prose and benefits are serialized together.
        // Replace the legacy English narrative only; retain the serialized benefit values.
        if (text.Contains("Midway through the magazine"))
        {
            int marker = text.IndexOf("Horny Factor", StringComparison.Ordinal);
            int line = marker < 0 ? -1 : text.LastIndexOf('\n', marker);
            string benefits = marker < 0 ? "" : text.Substring(line + 1).Trim();
            text = description + (benefits.Length == 0 ? "" : "\n\n" + benefits);
        }
        return text.Replace("Horny Factor", label);
    }
    public static bool ValidTentDraw(Thing __0)
    {
        return __0 != null && !__0.Destroyed && !__0.Discarded && __0.MapHeld?.info != null;
    }
    private static readonly HashSet<string> drawProbeKeys = new HashSet<string>();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Xml.XmlDocument, HashSet<string>> royalFactionIds = new System.Runtime.CompilerServices.ConditionalWeakTable<System.Xml.XmlDocument, HashSet<string>>();
    public static int CleanOrphanRoyalXml(System.Xml.XmlNode node, ISet<string> known)
    {
        if (node == null || known == null || known.Count == 0) return 0;
        int removed = 0;
        foreach (string tag in new[] { "favor", "highestTitles", "heirs" })
        {
            var keys = node.SelectNodes(tag + "/keys/li");
            var values = node.SelectNodes(tag + "/values/li");
            if (keys == null || values == null || keys.Count != values.Count) continue;
            for (int i = keys.Count - 1; i >= 0; i--)
                if (!known.Contains(keys[i].InnerText))
                {
                    keys[i].ParentNode.RemoveChild(keys[i]);
                    values[i].ParentNode.RemoveChild(values[i]);
                    removed++;
                }
        }
        foreach (string tag in new[] { "titles", "permits" })
        {
            var entries = node.SelectNodes(tag + "/li");
            if (entries == null) continue;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var faction = entries[i].SelectSingleNode("faction");
                if (faction != null && !known.Contains(faction.InnerText))
                { entries[i].ParentNode.RemoveChild(entries[i]); removed++; }
            }
        }
        return removed;
    }
    public static int CleanOrphanRoyalRecords(List<RimWorld.RoyalTitle> titles, List<RimWorld.FactionPermit> permits)
    {
        return (titles?.RemoveAll(t => t == null || t.faction == null) ?? 0)
            + (permits?.RemoveAll(p => p == null || p.Faction == null) ?? 0);
    }
    public static void CleanOrphanRoyalRecordsOnLoad(List<RimWorld.RoyalTitle> ___titles, List<RimWorld.FactionPermit> ___factionPermits)
    {
        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            var node = Scribe.loader?.curXmlParent;
            var doc = node?.OwnerDocument;
            if (doc != null)
            {
                var known = royalFactionIds.GetValue(doc, d => {
                    var ids = new HashSet<string>();
                    foreach (System.Xml.XmlNode faction in d.SelectNodes("//factionManager/allFactions/li"))
                        ids.Add("Faction_" + (faction.SelectSingleNode("loadID")?.InnerText ?? "0"));
                    return ids;
                });
                int fixedCount = CleanOrphanRoyalXml(node, known);
                if (fixedCount != 0) Log.Message("[NeloFixes] Repaired " + fixedCount + " missing-faction royal save record(s) before reference resolution.");
            }
        }
        if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
        int n = CleanOrphanRoyalRecords(___titles, ___factionPermits);
        if (n != 0) Log.Message("[NeloFixes] Removed " + n + " orphan royal title/permit record(s) whose faction no longer exists.");
    }
    public static IEnumerable<CodeInstruction> TraceDrawStages(IEnumerable<CodeInstruction> input)
    {
        var pos = AccessTools.PropertyGetter(typeof(Thing), "DrawPos");
        var draw = AccessTools.Method(typeof(Thing), "DynamicDrawPhaseAt");
        foreach (var i in input)
        {
            if (i.Calls(pos)) { i.opcode = OpCodes.Call; i.operand = AccessTools.Method(typeof(NeloCompatibilityMod), nameof(TracedDrawPos)); }
            else if (i.Calls(draw)) { i.opcode = OpCodes.Call; i.operand = AccessTools.Method(typeof(NeloCompatibilityMod), nameof(TracedDrawAt)); }
            yield return i;
        }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static UnityEngine.Vector3 TracedDrawPos(Thing t)
    {
        try { return t.DrawPos; }
        catch (Exception e) { RecordDrawProbe(t, "DrawPos", e); throw; }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void TracedDrawAt(Thing t, DrawPhase phase, UnityEngine.Vector3 pos, bool flip)
    {
        try { t.DynamicDrawPhaseAt(phase, pos, flip); }
        catch (Exception e) { RecordDrawProbe(t, "DrawAt/" + phase, e); throw; }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)]
    private static void RecordDrawProbe(Thing t, string stage, Exception e)
    {
        string key = t?.ThingID + "/" + stage;
        if (!drawProbeKeys.Add(key)) return;
        Log.Warning("[NeloDrawProbe] " + key + " def=" + t?.def?.defName + " spawned=" + t?.Spawned + " destroyed=" + t?.Destroyed + " pos=" + t?.Position + " exception=" + e);
    }
    public static void AuditLoadedRaidAndDraw()
    {
        LongEventHandler.ExecuteWhenFinished(() => {
            try {
                var lines = new List<string>();
                Rand.PushState();
                try {
                    var worker = RimWorld.IncidentDefOf.RaidEnemy.Worker as RimWorld.IncidentWorker_RaidEnemy;
                    foreach (var map in Find.Maps.Where(m => m.IsPlayerHome)) {
                        var parms = RimWorld.StorytellerUtility.DefaultParmsNow(RimWorld.IncidentCategoryDefOf.ThreatBig, map);
                        lines.Add("map=" + map.uniqueID + " raidPoints=" + parms.points + " ticks=" + Find.TickManager.TicksGame);
                        foreach (var f in Find.FactionManager.AllFactionsListForReading.Where(f => f.def.defName == "Tyranid_Swarm")) {
                            lines.Add("Tyranid faction=" + f.loadID + " defeated=" + f.defeated + " deactivated=" + f.deactivated + " eligible=" + worker.FactionCanBeGroupSource(f, parms, false));
                        }
                    }
                } finally { Rand.PopState(); }
                var dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "NeloLocalFixes");
                File.WriteAllLines(Path.Combine(dir, "泰伦袭击运行检查.txt"), lines);
                Log.Message("[NeloFixes] Raid eligibility audit written; no incident fired.");
            } catch (Exception e) { Log.Warning("[NeloFixes] Raid audit failed: " + e); }
        });
    }

    private static readonly Dictionary<(int, string), int> tribePickHistory = new Dictionary<(int, string), int>();
    public static bool TribePickAllowed(int pawn, string key, int now)
    {
        return !tribePickHistory.TryGetValue((pawn, key), out int last) || now < last || now - last >= 600;
    }
    public static void RememberTribePick(int pawn, string key, int now)
    {
        tribePickHistory[(pawn, key)] = now;
    }
    public static bool GuardTribePick(Pawn __0, string __1, ref bool __result)
    {
        if (__0 != null && TribePickAllowed(__0.thingIDNumber, __1, Find.TickManager.TicksGame)) return true;
        __result = false;
        return false;
    }
    public static void RecordTribePick(Pawn __0, string __1)
    {
        if (__0 != null) RememberTribePick(__0.thingIDNumber, __1, Find.TickManager.TicksGame);
    }
    public static IEnumerable<CodeInstruction> ClearDiscardReferences(IEnumerable<CodeInstruction> input)
    {
        var code = input.ToList();
        var discard = AccessTools.Method(typeof(Thing), "Discard", new[] { typeof(bool) });
        for (int i = 1; i < code.Count; i++)
            if (code[i].Calls(discard) && code[i - 1].opcode == OpCodes.Ldc_I4_0)
                code[i - 1].opcode = OpCodes.Ldc_I4_1;
        return code;
    }
    public static void RepairAnimalNodeColor(PawnRenderNode __instance, Pawn __0)
    {
        if (__0 == null || __0.story != null || __0.RaceProps?.Humanlike != false) return;
        var field = AccessTools.Field(typeof(PawnRenderNode), "props");
        var props = (PawnRenderNodeProperties)field.GetValue(__instance);
        if (props == null || (props.colorType != PawnRenderNodeProperties.AttachmentColorType.Skin &&
            props.colorType != PawnRenderNodeProperties.AttachmentColorType.Hair)) return;
        var copy = (PawnRenderNodeProperties)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(props, null);
        copy.colorType = PawnRenderNodeProperties.AttachmentColorType.Custom;
        field.SetValue(__instance, copy); // Preserve the shared Def and normal color factors/rot handling.
    }
    public static bool DisablePlantBatch(ref bool __result)
    {
        // Isolate the optional optimization implicated by the Plant.TickLong null stack.
        // The normal vanilla plant update remains active; unknown exceptions are not swallowed.
        __result = false;
        return false;
    }

    public static bool DmsIdentityAllowed(string race, string xenotype, bool customOrHybrid)
    {
        return race == "Human" && xenotype == "Baseliner" && !customOrHybrid;
    }

    public static bool BaselineHuman(Pawn pawn)
    {
        return pawn != null && DmsIdentityAllowed(pawn.def?.defName,
            pawn.genes == null ? "Baseliner" : pawn.genes.Xenotype?.defName,
            pawn.genes != null && (pawn.genes.UniqueXenotype || pawn.genes.hybrid));
    }

    public static bool NativeDmsRequiredArmor(string kindOwner, RimWorld.ApparelProperties candidate,
        IEnumerable<RimWorld.ApparelProperties> required)
    {
        return kindOwner?.StartsWith("aoba.deadmanswitch", StringComparison.OrdinalIgnoreCase) == true &&
            required != null && required.Any(p => ReferenceEquals(p, candidate));
    }

    public static void FilterGeneratedApparel(RimWorld.ApparelProperties __instance, Pawn __0, ref bool __result)
    {
        if (!__result || apparelGenerationDepth == 0) return;
        bool nativeRequired = NativeDmsRequiredArmor(__0?.kindDef?.modContentPack?.PackageId, __instance,
            __0?.kindDef?.apparelRequired?.Select(d => d.apparel));
        if ((DmsArmorProperties.Contains(__instance) && !BaselineHuman(__0) && !nativeRequired) ||
            (RatkinApparelProperties.Contains(__instance) && __0?.def?.defName != "Ratkin"))
            __result = false;
    }

    public static bool ValidHaulCandidate(Pawn __0, Thing __1, ref bool __result)
    {
        if (__0?.health?.capacities != null && __0.Spawned && __1 != null && __1.Spawned &&
            !__1.Destroyed && __1.Map == __0.Map &&
            !(__1 is UnfinishedThing unfinished && unfinished.BoundBill != null && unfinished.BoundBill.billStack == null))
            return true;
        __result = false;
        return false;
    }

    public static bool SafePawnMoving(Verse.AI.Pawn_PathFollower path) { return path != null && path.Moving; }
    public static IEnumerable<CodeInstruction> SafeSpeakUpMoving(IEnumerable<CodeInstruction> input)
    {
        var getter = AccessTools.PropertyGetter(typeof(Verse.AI.Pawn_PathFollower), "Moving");
        foreach (var code in input)
        {
            if (code.Calls(getter))
            {
                code.opcode = OpCodes.Call;
                code.operand = AccessTools.Method(typeof(NeloCompatibilityMod), nameof(SafePawnMoving));
            }
            yield return code;
        }
    }

    // The installed Brothel worker stores the getter's boolean in a debug local before branching.
    // Reuse Onahole's exclusion helper without changing the worker's branches, locals or labels.
    public static bool RepairRoomRoleTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original,
        MethodBase __originalMethod, ref IEnumerable<CodeInstruction> __result)
    {
        if (original.DeclaringType?.FullName != "BrothelColony.RoomRoleWorker_Brothel") return true;
        var codes = instructions.ToList();
        var getter = AccessTools.PropertyGetter(typeof(RimWorld.Building_Bed), "ForPrisoners");
        var helper = AccessTools.Method(__originalMethod.DeclaringType, "ForNonOnaholePrisoners");
        if (helper == null) return true;
        for (int i = 0; i + 3 < codes.Count; i++)
        {
            if (!codes[i].Calls(getter) || codes[i + 1].opcode != OpCodes.Stloc_S ||
                codes[i + 2].opcode != OpCodes.Ldloc_S || !Equals(codes[i + 1].operand, codes[i + 2].operand) ||
                (codes[i + 3].opcode != OpCodes.Brfalse_S && codes[i + 3].opcode != OpCodes.Brfalse)) continue;
            codes[i].opcode = OpCodes.Call;
            codes[i].operand = helper;
            __result = codes;
            return false;
        }
        return true; // Changed upstream structure keeps the original diagnostic visible.
    }

    public static IEnumerable<CodeInstruction> DeferDefaultReferences(IEnumerable<CodeInstruction> input)
    {
        var code = input.ToList();
        for (int i = 0; i < code.Count; i++)
        {
            if (i + 2 < code.Count && code[i].opcode == OpCodes.Ldarg_0 && code[i + 1].opcode == OpCodes.Ldsfld && code[i + 2].opcode == OpCodes.Stfld
                && code[i + 1].operand is FieldInfo source && source.DeclaringType.Name.EndsWith("DefOf")
                && code[i + 2].operand is FieldInfo destination && typeof(Def).IsAssignableFrom(source.FieldType))
            {
                if (!DeferredFields.TryGetValue(destination.DeclaringType, out var names))
                    DeferredFields[destination.DeclaringType] = names = new HashSet<string>();
                names.Add(destination.Name);
                yield return code[i];
                yield return new CodeInstruction(OpCodes.Ldstr, destination.Name);
                yield return new CodeInstruction(OpCodes.Ldstr, source.Name);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeloCompatibilityMod), nameof(RegisterDefault)).MakeGenericMethod(source.FieldType));
                i += 2;
            }
            else yield return code[i];
        }
    }

    public static void RegisterDefault<T>(object target, string fieldName, string defName) where T : Def, new()
    {
        var value = DefDatabase<T>.GetNamedSilentFail(defName);
        if (value != null)
            AccessTools.Field(target.GetType(), fieldName).SetValue(target, value);
        else
            DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(target, fieldName, defName);
    }

    public static bool ValidBiome(Map __0)
    {
        if (__0 == null) return false;
        try { return __0.Biome != null; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    public static bool ValidCurrentScenario()
    {
        return ValidBiome(Find.CurrentMap);
    }

    public static void RatkinOnly(ThingDef __0, ThingDef __1, ref bool __result)
    {
        if (__result && __1?.defName != "Ratkin" && RatkinApparel.Contains(__0))
            __result = false;
    }

    private static readonly Dictionary<string, HashSet<string>> RecentStaleTranslations = new Dictionary<string, HashSet<string>>
    {
        { "Verse.GeneDef", new HashSet<string> { "Nivarian.label" } },
        { "Verse.ApparelLayerDef", new HashSet<string> { "Nivarian_Outerwear.label" } },
        { "Verse.HediffDef", new HashSet<string> { "Nivarian_Hediff_CryobladeOn.comps.0.tools.0.label", "Nivarian_Hediff_DisintegrationFlare.stages.1.label", "Nivarian_Lumin_PsionicAttunement.stages.0.label", "Nivarian_Lumin_PsionicAttunement.stages.1.label", "Nivarian_Lumin_PsionicAttunement.stages.2.label" } },
        { "Verse.RecipeDef", new HashSet<string> { "NivarianApparelConvenience_Make_Apparel_FloralDress.description", "NivarianApparelConvenience_Make_Apparel_FloralDress.jobString", "NivarianApparelConvenience_Make_Apparel_FloralDress.label", "Nivarian_Make_Apparel_PolarExpeditionHood.description", "Nivarian_Make_Apparel_PolarExpeditionHood.jobString", "Nivarian_Make_Apparel_PolarExpeditionHood.label", "Nivarian_Make_Apparel_PolarExpeditionSuit.description", "Nivarian_Make_Apparel_PolarExpeditionSuit.jobString", "Nivarian_Make_Apparel_PolarExpeditionSuit.label" } },
        { "Verse.ResearchProjectDef", new HashSet<string> { "Nivarian_Hidden_Military.label" } },
        { "Verse.ThingDef", new HashSet<string> { "Apparel_NivarianPolarExpeditionHood.description", "Apparel_NivarianPolarExpeditionHood.label", "Apparel_NivarianPolarExpeditionSuit.description", "Apparel_NivarianPolarExpeditionSuit.label", "Nivarian_Bullet_LGM_Bullet.label", "Nivarian_Bullet_Nilock.label", "Nivarian_CryoForge.comps.1.fuelGizmoLabel", "Nivarian_CryoForge.comps.1.fuelLabel", "Nivarian_FrostDrakeATB.labelFemale", "Nivarian_FrostDrakeATB.labelFemalePlural", "Nivarian_FrostDrakeATB.labelMale", "Nivarian_FrostDrakeATB.labelMalePlural", "Nivarian_FrostDrakeATB.labelPlural", "Nivarian_PsionicFocusDrugBase.ingestible.ingestCommandString", "Nivarian_PsionicFocusDrugBase.ingestible.ingestReportString" } },
        { "RimWorld.AbilityDef", new HashSet<string> { "Nivarian_Ability_MinorPainblock.description", "Nivarian_Ability_MinorPainblock.label", "Nivarian_Ability_PsionicStun.description", "Nivarian_Ability_PsionicStun.label" } },
        { "RimWorld.IncidentDef", new HashSet<string> { "NivarianKoelimeApologyAid.description", "NivarianKoelimeApologyAid.label" } },
        { "RimWorld.QuestScriptDef", new HashSet<string> { "Nivarian_AncientShelterQuest.root.nodes.Delay-0.node.parms.customLetterLabel.value.slateRef", "Nivarian_AncientShelterQuest.root.nodes.Delay-0.node.parms.customLetterText.value.slateRef", "Nivarian_AncientShelterQuest.root.nodes.Delay-1.node.nodes.Delay.node.nodes.Util_SendItemPods.parms.customLetterLabel.value.slateRef", "Nivarian_AncientShelterQuest.root.nodes.Delay-1.node.nodes.Delay.node.nodes.Util_SendItemPods.parms.customLetterText.value.slateRef", "Nivarian_AncientShelterQuest.root.nodes.Delay-1.node.nodes.Letter.label.slateRef", "Nivarian_AncientShelterQuest.root.nodes.Delay-1.node.nodes.Letter.text.slateRef", "Nivarian_AncientShelterQuest.root.nodes.Util_Nivarian_JoinerThreat_Joiner.parms.customLetterLabel.value.slateRef", "Nivarian_AncientShelterQuest.root.nodes.Util_Nivarian_JoinerThreat_Joiner.parms.customLetterText.value.slateRef" } },
        { "RimWorld.ScenarioDef", new HashSet<string> { "NivarianScenarioExt_Shipwrecked.description", "NivarianScenarioExt_Shipwrecked.label", "NivarianScenarioExt_Shipwrecked.scenario.description", "NivarianScenarioExt_Shipwrecked.scenario.name", "NivarianScenarioExt_Shipwrecked.scenario.parts.GameStartDialog.text", "NivarianScenarioExt_Shipwrecked.scenario.summary", "Nivarian_Newborn.scenario.label" } },
        { "CustomBaseFramework.PawnListDef", new HashSet<string> { "CBF_MO_Pawns_AncientCrypt.label", "CBF_MO_Pawns_CryptAlps.label" } },
        { "CustomBaseFramework.PieceCategoryDef", new HashSet<string> { "CBF_MO_AncientDangerMedieval.label" } },
        { "CustomBaseFramework.SettlementPlanDef", new HashSet<string> { "CBF_Plan_Dungeon_AncientCrypt.label", "CBF_Plan_Fortress.label", "CBF_Plan_FortressCity_New.label", "CBF_Plan_MarketCity_New.label", "CBF_Plan_MarketTown_New.label", "CBF_Plan_TribalCamp.label", "CBF_Plan_Village_New.label" } },
        { "Nivarian_Race.Code.Defs.NivarianMotherShipRequestDef", new HashSet<string> { "Request_PrefabDrop_SmallRoom.description", "Request_PrefabDrop_SmallRoom.label" } },
        { "AlienRace.ThingDef_AlienRace", new HashSet<string> { "Nivarian_Luminis_Adult_SoulShepherd.title" } },
    };

    public static void RepairPackedTranslations(DefInjectionPackage __instance)
    {
        var type = (Type)AccessTools.Field(typeof(DefInjectionPackage), "defType").GetValue(__instance);
        var entries = (System.Collections.IDictionary)AccessTools.Field(typeof(DefInjectionPackage), "injections").GetValue(__instance);
        var stale = new HashSet<string>();
        if (RecentStaleTranslations.TryGetValue(type.FullName, out var recent)) stale.UnionWith(recent);
        if (type.FullName == "Verse.GeneDef")
            stale.UnionWith(new[] { "rjw_genes_Orc_genitalia.label", "rjw_genes_Orc_genitalia.description", "rjw_genes_Pig_genitalia.label", "rjw_genes_Pig_genitalia.description" });
        if (type.FullName == "Verse.HediffDef")
            stale.UnionWith(new[] { "MaZuiDick.label", "MaZuiDick.description", "MaZuiDick.stages.Micro.label", "MaZuiDick.stages.Small.label", "MaZuiDick.stages.Average.label", "MaZuiDick.stages.Large.label", "MaZuiDick.stages.Huge.label", "MaZuiDick.stages.Towering.label" });
        if (type.FullName == "Verse.ThingDef")
            stale.UnionWith(new[] { "MaZuiDick.label", "MaZuiDick.description", "Autobong_Aphrodisiac_Blueprint.label", "Autobong_Aphrodisiac_Frame.label", "Autobong_Aphrodisiac_Frame.description" });
        if (type.FullName == "RimWorld.ThoughtDef")
            stale.UnionWith(new[] { "rjw_genes_has_more_diseases.stages.carries_more_way_genetic_diseases_than_me.label" });
        if (type == typeof(GeneDef))
            stale.Add("WoundHealing_Fast.symbolPack.prefixSymbols.1.symbol");
        if (type == typeof(RimWorld.LifeStageDef) &&
            DefDatabase<RimWorld.LifeStageDef>.GetNamedSilentFail("HumanlikeAdult_Dragonian_Elder") == null)
            stale.Add("HumanlikeAdult_Dragonian_Elder.label");
        if (type == typeof(HediffDef))
            stale.Add("Lactating.comps.HediffComp_Lactating.labelInBrackets");
        if (type == typeof(ThingDef))
        {
            stale.Add("Blackboard.comps.CompInspectStringBlackboard.inspectString");
            stale.Add("SchoolDesk.comps.CompStatEntrySchoolDesk.reportText");
            stale.Add("SchoolDesk.comps.CompStatEntrySchoolDesk.statLabel");
        }
        if (type == typeof(Verse.ThinkTreeDef))
            stale.Add("Mechanoid.thinkRoot.subNodes.8.subNodes.0.subNodes.1.subNodes.4.subNodes.1.subNodes.0.reportStringOverride");
        if (type == typeof(RimWorld.InteractionDef))
        {
            for (int index = 18; index <= 21; index++)
                stale.Add("AnimalChat.logRulesInitiator.rulesStrings." + index);
            stale.Add("Nuzzle.logRulesInitiator.rulesStrings.22");
            stale.Add("Nuzzle.logRulesInitiator.rulesStrings.23");
            stale.Add("Breakup.logRulesInitiator.rulesStrings.3");
        }
        var duplicates = new Dictionary<string, string>
        {
            { "RJWFailedSolicitation.stages.tried_to_solicit_me.label", "RJWFailedSolicitation.stages.0.label" },
            { "RJWTurnedDownWhore.stages.bothered_me.label", "RJWTurnedDownWhore.stages.0.label" },
            { "SleptInBrothel.stages.slept_in_brothel-0.label", "SleptInBrothel.stages.0.label" },
            { "SleptInBrothel.stages.slept_in_brothel-0.description", "SleptInBrothel.stages.0.description" },
            { "SleptInBrothel.stages.slept_in_brothel-1.label", "SleptInBrothel.stages.1.label" },
            { "SleptInBrothel.stages.slept_in_brothel-1.description", "SleptInBrothel.stages.1.description" }
        };
        var paths = new HashSet<string>();
        foreach (System.Collections.DictionaryEntry entry in entries)
            paths.Add((string)AccessTools.Field(entry.Value.GetType(), "path").GetValue(entry.Value));
        var remove = new List<object>();
        foreach (System.Collections.DictionaryEntry entry in entries)
        {
            var injection = entry.Value;
            string path = (string)AccessTools.Field(injection.GetType(), "path").GetValue(injection);
            if (stale.Contains(path) || (type == typeof(RimWorld.ThoughtDef) && duplicates.TryGetValue(path, out var original) && paths.Contains(original)))
            {
                remove.Add(entry.Key);
                continue;
            }
            if (type != typeof(RimWorld.InteractionDef)) continue;
            int count = path == "AnimalChat.logRulesInitiator.rulesStrings" ? 18 :
                path == "Nuzzle.logRulesInitiator.rulesStrings" ? 22 :
                path == "Breakup.logRulesInitiator.rulesStrings" ? 3 : -1;
            var list = AccessTools.Field(injection.GetType(), "fullListInjection").GetValue(injection) as List<string>;
            if (count >= 0 && list != null && list.Count > count)
                list.RemoveRange(count, list.Count - count);
        }
        foreach (var key in remove) entries.Remove(key);
    }
    internal static bool AuditsEnabled { get; private set; }

    internal static bool DiagnosticsRequested(string saveDataFolder) =>
        File.Exists(Path.Combine(saveDataFolder, "NeloLocalFixes", "enable-audits.txt"));
}

[StaticConstructorOnStartup]
public static class NeloRepairDiagnostics
{
    static NeloRepairDiagnostics()
    {
        var ratkin = DefDatabase<ThingDef>.GetNamedSilentFail("Ratkin") as ThingDef_AlienRace;
        var ratkinRestriction = ratkin?.alienRace?.raceRestriction?.apparelList;
        var defs = DefDatabase<ThingDef>.AllDefsListForReading;
        var clothes = defs.Where(d => d.IsApparel &&
            (d.modContentPack?.PackageId.Equals("solaris.ratkinracemod", StringComparison.OrdinalIgnoreCase) == true ||
             ratkinRestriction?.Contains(d) == true)).ToList();
        NeloCompatibilityMod.RatkinApparel.UnionWith(clothes);
        NeloCompatibilityMod.RatkinApparelProperties.UnionWith(clothes.Select(d => d.apparel));
        var dmsArmor = defs.Where(d => d.IsApparel &&
            d.modContentPack?.PackageId.StartsWith("aoba.deadmanswitch", StringComparison.OrdinalIgnoreCase) == true &&
            (d.thingCategories?.Any(c => c.defName == "ApparelArmor") == true ||
             d.tradeTags?.Any(t => t == "Armor" || t == "DMS_Armor") == true)).ToList();
        NeloCompatibilityMod.DmsArmorProperties.UnionWith(dmsArmor.Select(d => d.apparel));
        if (!NeloCompatibilityMod.AuditsEnabled) return;

        string output = Path.Combine(GenFilePaths.SaveDataFolderPath, "NeloLocalFixes");
        try
        {
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, "DMS盔甲生成限制.txt"), new[] {
                "Random generation: only Human/Baseliner without custom/hybrid xenotype. Native DMS PawnKind mandatory apparel preserved. Normal hair/skin genes allowed; manual equip unchanged.",
                "Registered armor: " + dmsArmor.Count }.Concat(dmsArmor.Select(d => d.defName)));
            var races = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => !d.IsCorpse && d.race != null && d.race.Humanlike && d != ratkin).ToList();
            var report = new StringBuilder();
            int permitted = 0;
            foreach (var apparel in clothes)
            {
                foreach (var race in races)
                {
                    if (RaceRestrictionSettings.CanWear(apparel, race))
                    {
                        permitted++;
                        report.AppendLine("Unexpected permission " + race.defName + " / " + apparel.defName);
                    }
                }
                if (ratkin != null && !RaceRestrictionSettings.CanWear(apparel, ratkin))
                    report.AppendLine("Ratkin cannot wear " + apparel.defName);
            }
            report.Insert(0, "Apparel " + clothes.Count + ", non-Ratkin races " + races.Count + ", unexpected permissions " + permitted + Environment.NewLine);
            File.WriteAllText(Path.Combine(output, "鼠族衣物运行检查.txt"), report.ToString());
            Log.Message("[NeloFixes] " + report.ToString().Trim());
            var translation = new StringBuilder();
            foreach (var method in typeof(LanguageReportGenerator).GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            {
                var parameters = method.GetParameters();
                if (method.Name.StartsWith("Append") && parameters.Length == 1 && parameters[0].ParameterType == typeof(StringBuilder))
                {
                    translation.AppendLine("=== " + method.Name + " ===");
                    try { method.Invoke(null, new object[] { translation }); }
                    catch (Exception error) { translation.AppendLine(error.ToString()); }
                }
            }
            File.WriteAllText(Path.Combine(output, "汉化详细报告.txt"), translation.ToString());
            int checkedDefaults = 0;
            int missingDefaults = 0;
            var defaults = new StringBuilder();
            Action<object> inspect = target =>
            {
                foreach (var pair in NeloCompatibilityMod.DeferredFields)
                {
                    if (!pair.Key.IsInstanceOfType(target)) continue;
                    foreach (var name in pair.Value)
                    {
                        checkedDefaults++;
                        if (AccessTools.Field(target.GetType(), name).GetValue(target) == null)
                        {
                            missingDefaults++;
                            defaults.AppendLine(target.GetType().FullName + "." + name);
                        }
                    }
                }
            };
            foreach (var def in DefDatabase<RimWorld.AbilityDef>.AllDefsListForReading)
                if (def.comps != null) foreach (var comp in def.comps) inspect(comp);
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (def.comps != null) foreach (var comp in def.comps) inspect(comp);
            defaults.Insert(0, "Checked default references " + checkedDefaults + ", unresolved " + missingDefaults + Environment.NewLine);
            File.WriteAllText(Path.Combine(output, "默认引用运行检查.txt"), defaults.ToString());
            Log.Message("[NeloFixes] " + defaults.ToString().Trim());
        }
        catch (Exception error)
        {
            Log.Error("[NeloFixes] Diagnostic failure " + error);
        }
    }
}
