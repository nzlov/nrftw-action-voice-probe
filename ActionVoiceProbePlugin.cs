using System.Reflection;
using System.Collections;
using System.Globalization;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(Nrftw.ActionVoiceProbe.ActionVoiceProbePlugin), "NRFTW Action Voice Probe", "0.1.0", "nzlov")]

namespace Nrftw.ActionVoiceProbe;

public sealed class ActionVoiceProbePlugin : MelonMod
{
    private const int PerHookLogLimit = 30;
    private const string PlaybackPitchRtpcName = "nrftw_action_voice_pitch_cents";
    private static readonly Dictionary<string, int> HookCounts = new();
    private static readonly HashSet<ulong> SuccessfulParryEntities = new();
    private static ProbeLogLevel _logLevel = ProbeLogLevel.None;
    private HarmonyLib.Harmony? _harmony;

    public override void OnInitializeMelon()
    {
        ActionAudio.Load();
        ActionAudio.StartStartupDiagnosticLoop();
        BindingMetadata.Start(ActionAudio.DirectoryPath, ActionAudio.ExportMap);
        _harmony = new HarmonyLib.Harmony("local.nrftw.actionvoiceprobe");

        PatchPrefix("Moon.Forsaken.ActionView", "OnStartView", nameof(OnActionViewStart));
        PatchPrefix("Moon.Forsaken.ActionView", "OnStopView", nameof(OnActionViewStop));
        PatchPrefix("Moon.Forsaken.DodgeRollView", "OnStartView", nameof(OnDodgeRollStart));
        PatchPrefix("Moon.Forsaken.QuickStepActionView", "OnStartView", nameof(OnQuickStepStart));

        PatchPrefix("Quantum.Frame+FrameSignals", "OnActionExecuted", nameof(OnQuantumSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnActionCompleted", nameof(OnQuantumSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnPairedActionExecuted", nameof(OnQuantumSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnDodgeRollStateChanged", nameof(OnQuantumSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnQuickstepExecuted", nameof(OnQuantumSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnItemConsumed", nameof(OnQuantumSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnHeroExecutedSpecial", nameof(OnQuantumSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnPlayerPerformedGestureDirector", nameof(OnQuantumSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnPlayerDied", nameof(OnQuantumSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnLevelChanged", nameof(OnQuantumSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnPlayerBlock", nameof(OnCombatSignal));
        PatchPrefix("Quantum.Frame+FrameSignals", "OnPlayerParry", nameof(OnCombatSignal));

        PatchPrefix("Moon.Timeline.WwiseDialogueAnimator", "OnStartPlayback", nameof(OnWwiseTimeline));
        PatchPrefix("Moon.Timeline.WwiseSoundAnimator", "OnStartPlayback", nameof(OnWwiseTimeline));
        PatchPrefix("Moon.Timeline.WwiseVoiceAnimator", "OnStartPlayback", nameof(OnWwiseTimeline));
        PatchPrefix("Moon.Timeline.WwiseWeaponAnimator", "OnStartPlayback", nameof(OnWwiseTimeline));
        PatchPrefix("Moon.Timeline.WwiseQBridgeWeaponAnimator", "OnStartPlayback", nameof(OnWwiseTimeline));

        LogInfo("[ActionVoiceProbe] loaded");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        => ResetSessionState($"scene-loaded:{buildIndex}:{sceneName}");

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        => ResetSessionState($"scene-unloaded:{buildIndex}:{sceneName}");

    private static void ResetSessionState(string reason)
    {
        SuccessfulParryEntities.Clear();
        ActionAudio.ResetSessionState(reason);
    }

    private void PatchPrefix(string typeName, string methodName, string patchName)
    {
        var type = ResolveType(typeName);
        if (type == null)
        {
            LogWarning($"[ActionVoiceProbe] type not found: {typeName}");
            return;
        }

        var method = AccessTools.Method(type, methodName);
        if (method == null)
        {
            LogWarning($"[ActionVoiceProbe] method not found: {typeName}.{methodName}");
            return;
        }

        var patch = new HarmonyMethod(typeof(ActionVoiceProbePlugin).GetMethod(patchName, BindingFlags.NonPublic | BindingFlags.Static));
        _harmony!.Patch(method, prefix: patch);
        LogInfo($"[ActionVoiceProbe] patched {type.FullName}.{methodName} (prefix)");
    }

    private static Type? ResolveType(string typeName)
    {
        foreach (var candidate in GetTypeNameCandidates(typeName))
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(candidate, throwOnError: false, ignoreCase: false);
                if (type != null)
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetTypeNameCandidates(string typeName)
    {
        yield return typeName;

        if (typeName.StartsWith("Il2Cpp", StringComparison.Ordinal))
        {
            yield break;
        }

        yield return typeName.Contains('.')
            ? $"Il2Cpp{typeName}"
            : $"Il2Cpp.{typeName}";
    }

    private static void OnActionViewStart(object __instance)
    {
        ActionAudio.ObserveActionViewStart(__instance);
        Trace("action.start", __instance);
    }

    private static void OnActionViewStop(object __instance)
    {
        ActionAudio.ObserveActionViewStop(__instance);
        Trace("action.stop", __instance);
    }
    private static void OnDodgeRollStart(object __instance) => Trace("dodge.roll.start", __instance);
    private static void OnQuickStepStart(object __instance) => Trace("quickstep.start", __instance);
    private static void OnWwiseTimeline(object __instance) => Trace("wwise.timeline.start", __instance);
    private static void OnQuantumSignal(object __instance, object[] __args, MethodBase __originalMethod)
    {
        TraceActionIntent(__originalMethod.Name, __instance, __args);
        TraceArgs($"quantum.signal.{__originalMethod.Name}", __instance, __args);
    }

    private static void OnCombatSignal(object __instance, object[] __args, MethodBase __originalMethod)
    {
        var hook = __originalMethod.Name switch
        {
            "OnPlayerBlock" => "combat.block.success",
            "OnPlayerParry" => "combat.parry.success",
            _ => $"combat.{__originalMethod.Name}"
        };

        if (__originalMethod.Name == "OnPlayerParry")
        {
            MarkParrySuccess(__args);
        }

        TraceArgs(hook, __instance, __args);
    }

    private static void TraceActionIntent(string methodName, object instance, object[] args)
    {
        if (args.Length < 2 || methodName is not ("OnActionExecuted" or "OnActionCompleted"))
        {
            return;
        }

        var action = DescribeValue(args[1], instance);
        if (action.Contains("parry", StringComparison.OrdinalIgnoreCase))
        {
            if (methodName == "OnActionCompleted")
            {
                if (!ShouldPlayParryFailure(args))
                {
                    LogDebug("[ActionVoiceProbe] combat.parry.fail.suppressed: reason=success");
                    return;
                }

                TraceArgs("combat.parry.fail.candidate", instance, args);
            }
            else
            {
                BeginParryAttempt(args);
                TraceArgs("combat.parry.attempt.candidate", instance, args);
            }
        }
        else if (action.Contains("block", StringComparison.OrdinalIgnoreCase))
        {
            var hook = methodName == "OnActionCompleted" ? "combat.block.end.candidate" : "combat.block.start.candidate";
            TraceArgs(hook, instance, args);
        }
    }

    private static void BeginParryAttempt(object[] args)
    {
        if (TryGetSignalEntityRaw(args, out var entityRaw))
        {
            SuccessfulParryEntities.Remove(entityRaw);
        }
    }

    private static void MarkParrySuccess(object[] args)
    {
        if (TryGetSignalEntityRaw(args, out var entityRaw))
        {
            SuccessfulParryEntities.Add(entityRaw);
        }
    }

    private static bool ShouldPlayParryFailure(object[] args)
        => !TryGetSignalEntityRaw(args, out var entityRaw) || !SuccessfulParryEntities.Remove(entityRaw);

    private static bool TryGetSignalEntityRaw(object[] args, out ulong entityRaw)
    {
        entityRaw = 0;
        if (args.Length == 0 || args[0] == null)
        {
            return false;
        }

        // GLUE: Reflection reads the generated EntityRef identity without adding a game-assembly reference; remove when Quantum types become stable build inputs.
        try
        {
            var raw = GetFieldValue(args[0], "Raw") ?? GetPropertyValue(args[0], "Raw");
            if (raw is not ulong value || value == 0)
            {
                return false;
            }

            entityRaw = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Trace(string hook, object? instance)
    {
        ActionAudio.TryPlay(hook, instance, Array.Empty<object>());

        if (!ShouldLog(hook))
        {
            return;
        }

        LogDebug($"[ActionVoiceProbe] {hook}: {Describe(instance)}");
    }

    private static void TraceArgs(string hook, object? instance, object[] args)
    {
        ActionAudio.TryPlay(hook, instance, args);

        if (!ShouldLog(hook))
        {
            return;
        }

        LogDebug($"[ActionVoiceProbe] {hook}: {Describe(instance)} | {DescribeArgs(args, instance)}");
    }

    private static bool ShouldLog(string hook)
    {
        HookCounts.TryGetValue(hook, out var count);
        if (count >= PerHookLogLimit)
        {
            return false;
        }

        HookCounts[hook] = count + 1;
        return true;
    }

    private static bool ShouldLogLevel(ProbeLogLevel level)
        => _logLevel != ProbeLogLevel.None && level >= _logLevel;

    private static void LogDebug(string message)
    {
        if (ShouldLogLevel(ProbeLogLevel.Debug))
        {
            MelonLogger.Msg(message);
        }
    }

    private static void LogInfo(string message)
    {
        if (ShouldLogLevel(ProbeLogLevel.Info))
        {
            MelonLogger.Msg(message);
        }
    }

    private static void LogWarning(string message)
    {
        if (ShouldLogLevel(ProbeLogLevel.Warning))
        {
            MelonLogger.Warning(message);
        }
    }

    private static void LogError(string message)
    {
        if (ShouldLogLevel(ProbeLogLevel.Error))
        {
            MelonLogger.Error(message);
        }
    }

    private static bool TryParseLogLevel(string value, out ProbeLogLevel logLevel)
    {
        if (value.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            logLevel = ProbeLogLevel.Debug;
            return true;
        }

        if (value.Equals("info", StringComparison.OrdinalIgnoreCase))
        {
            logLevel = ProbeLogLevel.Info;
            return true;
        }

        if (value.Equals("warning", StringComparison.OrdinalIgnoreCase) || value.Equals("warn", StringComparison.OrdinalIgnoreCase))
        {
            logLevel = ProbeLogLevel.Warning;
            return true;
        }

        if (value.Equals("error", StringComparison.OrdinalIgnoreCase))
        {
            logLevel = ProbeLogLevel.Error;
            return true;
        }

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            logLevel = ProbeLogLevel.None;
            return true;
        }

        logLevel = ProbeLogLevel.None;
        return false;
    }

    private enum ProbeLogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        None = 4,
    }

    private static string Describe(object? instance)
    {
        if (instance == null)
        {
            return "<null>";
        }

        var type = instance.GetType();
        var parts = new List<string> { type.FullName ?? type.Name };

        TryAppendProperty(parts, type, instance, "BakedViewName");
        TryAppendProperty(parts, type, instance, "Layer");
        TryAppendProperty(parts, type, instance, "TimelineIndex");
        TryAppendProperty(parts, type, instance, "ActionViewTime");
        TryAppendProperty(parts, type, instance, "UseChargeEffects");

        return string.Join(" | ", parts);
    }

    private static string DescribeArgs(object[] args, object? context)
    {
        if (args.Length == 0)
        {
            return "args=<none>";
        }

        var parts = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            parts.Add($"arg{i}={DescribeValue(args[i], context)}");
        }

        return string.Join(" | ", parts);
    }

    private static string DescribeValue(object? value, object? context)
    {
        if (value == null)
        {
            return "<null>";
        }

        var type = value.GetType();
        var parts = new List<string> { type.FullName ?? type.Name, $"value={value}" };

        TryAppendField(parts, type, value, "Id");
        TryAppendField(parts, type, value, "_index");
        TryAppendField(parts, type, value, "_version");
        TryAppendProperty(parts, type, value, "Id");
        TryAppendProperty(parts, type, value, "Value");
        TryAppendAssetName(parts, type, value, context);

        return "{" + string.Join(", ", parts) + "}";
    }

    private static void TryAppendAssetName(List<string> parts, Type type, object assetRef, object? context)
    {
        try
        {
            if (!type.FullName!.Contains("AssetRef", StringComparison.Ordinal))
            {
                return;
            }

            var frame = GetFieldValue(context, "_f") ?? GetPropertyValue(context, "_f");
            var id = GetFieldValue(assetRef, "Id") ?? GetPropertyValue(assetRef, "Id");
            if (frame == null || id == null)
            {
                return;
            }

            var findAssetName = frame.GetType().GetMethod("FindAssetName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var assetName = findAssetName?.Invoke(frame, new[] { id });
            if (assetName != null)
            {
                parts.Add($"AssetName={assetName}");
            }

        }
        catch (Exception ex)
        {
            parts.Add($"AssetName=<error:{ex.GetType().Name}>");
        }
    }

    private static object? GetFieldValue(object? instance, string fieldName)
    {
        if (instance == null)
        {
            return null;
        }

        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(instance);
    }

    private static object? GetPropertyValue(object? instance, string propertyName)
    {
        if (instance == null)
        {
            return null;
        }

        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(instance);
    }

    private static string GetUnityObjectName(object value)
    {
        try
        {
            return value is UnityEngine.Object unityObject ? unityObject.name ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string CleanAssetName(string value)
    {
        var name = value.Trim();
        foreach (var suffix in new[] { ".asset", ".weapon", ".rune" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
            }
        }

        return name;
    }

    private static string CleanGestureName(string value)
    {
        var name = CleanAssetName(value);
        foreach (var suffix in new[] { "GestureData", "Data" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }

    private static string ReadStringProperty(object? value, string propertyName)
    {
        try
        {
            return GetPropertyValue(value, propertyName)?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void TryAppendField(List<string> parts, Type type, object instance, string fieldName)
    {
        try
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                return;
            }

            var value = field.GetValue(instance);
            parts.Add($"{fieldName}={value ?? "<null>"}");
        }
        catch (Exception ex)
        {
            parts.Add($"{fieldName}=<error:{ex.GetType().Name}>");
        }
    }

    private static void TryAppendProperty(List<string> parts, Type type, object instance, string propertyName)
    {
        try
        {
            var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                return;
            }

            var value = property.GetValue(instance);
            parts.Add($"{propertyName}={value ?? "<null>"}");
        }
        catch (Exception ex)
        {
            parts.Add($"{propertyName}=<error:{ex.GetType().Name}>");
        }
    }

    private sealed class ActionAudio
    {
        private static readonly Dictionary<string, string[]> ConfiguredEvents = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> ConfiguredRates = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> LastPlayed = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> RuneActionNames = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> GestureActionNames = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] RuneNames =
        {
            "advancingFlurry",
            "airDodge",
            "armageddon",
            "arrowstorm",
            "balanceSmash",
            "bareKnuckle",
            "berserkFlurry",
            "berserkStrike",
            "blazingRupture",
            "blink",
            "bolt",
            "breakerThrust",
            "breakrush",
            "breakstep",
            "brutalReprise",
            "busterRift",
            "chainLightning",
            "channel",
            "chargeStrike",
            "chargedBolt",
            "chargedShot",
            "circular",
            "coldAffliction",
            "coldEnchantment",
            "coldResistance",
            "coldSling",
            "coneShot",
            "convergingFlame",
            "crosscut",
            "crush",
            "crushingDash",
            "crushingDodge",
            "crushingFlurry",
            "crushingQuad",
            "crushingTriple",
            "crushingUppercut",
            "curse",
            "cycloneKick",
            "cycloneSwipe",
            "damage",
            "dashingStab",
            "deflect",
            "dodgeSlamdown",
            "doubleCrush",
            "doubleSpin",
            "doubleTake",
            "dragonflyKick",
            "driveBreak",
            "droneTrap",
            "dropkick",
            "dualBreaker",
            "dualFlurry",
            "dualSlash",
            "eagleEye",
            "electricAffliction",
            "electricEnchantment",
            "electricResistance",
            "eruption",
            "evade",
            "evadeJump",
            "evasivePierce",
            "evasiveShot",
            "evasiveStrike",
            "evasiveSwing",
            "fierceDash",
            "fireArrow",
            "fireBlast",
            "fireBurst",
            "fireDart",
            "fireDash",
            "fireFlurry",
            "fireNova",
            "fireSlam",
            "fireSwipe",
            "fireThrow",
            "fireWalk",
            "fireWall",
            "fireWave",
            "fireWhirl",
            "fireWhirlwind",
            "fireball",
            "firefall",
            "flameSweep",
            "flipkickBurst",
            "focus",
            "frenziedCleave",
            "frigidArc",
            "frontflipKick",
            "frostBlade",
            "frostDash",
            "frostNeedles",
            "frostNova",
            "frostStep",
            "frostStream",
            "frostStrike",
            "glacialSpike",
            "glideburst",
            "groundSlam",
            "gyroSlash",
            "hail",
            "heal",
            "healAura",
            "health",
            "heatAffliction",
            "heatEnchantment",
            "heatResistance",
            "hellfire",
            "homingFrost",
            "iceArrow",
            "iceDart",
            "iceSweep",
            "iceThrow",
            "iceWhirl",
            "icebolt",
            "illuminate",
            "inferno",
            "juggleStrike",
            "jumpCut",
            "kickSlash",
            "knockdownSwirl",
            "knockoutShot",
            "leap",
            "lifeLeech",
            "lightningArrow",
            "lightningAssault",
            "lightningBarrage",
            "lightningBlast",
            "lightningBolt",
            "lightningDart",
            "lightningDash",
            "lightningFlash",
            "lightningFlurry",
            "lightningLeap",
            "lightningSlam",
            "lightningSpin",
            "lightningStorm",
            "lightningSweep",
            "lightningThrow",
            "lightningWhirl",
            "limitBreak",
            "multiSpin",
            "multishot",
            "overrun",
            "pestilence",
            "pestilentRecoil",
            "piercingCrush",
            "piercingDash",
            "piercingDodge",
            "piercingFlash",
            "piercingFlurry",
            "piercingSpin",
            "plagueAffliction",
            "plagueArrow",
            "plagueBarrage",
            "plagueBreath",
            "plagueBurst",
            "plagueColumn",
            "plagueCrush",
            "plagueDart",
            "plagueDash",
            "plagueEnchantment",
            "plagueExplosion",
            "plagueLaunch",
            "plagueNova",
            "plagueResistance",
            "plagueRetch",
            "plagueSmite",
            "plagueSplatter",
            "plagueStrike",
            "plagueSweep",
            "plagueSwirl",
            "plagueThrow",
            "plagueWave",
            "plagueWhirl",
            "poiseDefense",
            "poleDodge",
            "poleFlurry",
            "ragingSpiral",
            "reap",
            "recoilStrike",
            "regurgitate",
            "rejuvenate",
            "relentlessCleave",
            "repair",
            "retreatingTrimshot",
            "return",
            "ripAndTear",
            "risingFang",
            "rotwheel",
            "scream",
            "seismicCrush",
            "shockNova",
            "shockStrike",
            "skybreaker",
            "skyburstVolley",
            "skyfallShot",
            "skyfallSmash",
            "slamdown",
            "slashingDash",
            "slashingDodge",
            "slashingFlurry",
            "slashingPirouette",
            "slashingSpin",
            "slowAura",
            "somersault",
            "spark",
            "spectre",
            "speed",
            "spin",
            "spinCrush",
            "spinStrike",
            "spiralCrash",
            "spiritBurst",
            "stamina",
            "static",
            "stormpiercer",
            "swipeKick",
            "tauntStrike",
            "tempestKick",
            "thorns",
            "throw",
            "throwAxe",
            "throwKnife",
            "thrust",
            "thunderstrike",
            "tornadoStrike",
            "tremorSlam",
            "tremorWave",
            "trinityBarrage",
            "tripleSwipe",
            "turnbackKick",
            "twirlDash",
            "twirlSpin",
            "wallbreaker",
            "whirl",
            "whirlstepStrike",
            "whirlwind",
            "wildRush",
        };
        private static readonly HashSet<string> KnownRuneNames = new(RuneNames, StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> StandardActionKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["action.start"] = "actionStart",
            ["action.stop"] = "actionStop",
            ["melee.charge.start"] = "chargedAttackStart",
            ["melee.charge.stop"] = "chargedAttackStop",
            ["weapon.attack.plunge"] = "attack.plunge",
            ["player.fall"] = "playerFall",
            ["dodge.roll.start"] = "dodgeRoll",
            ["quickstep.start"] = "quickstep",
            ["wwise.timeline.start"] = "wwiseTimeline",
            ["quantum.signal.OnDodgeRollStateChanged"] = "dodgeRollStateChanged",
            ["quantum.signal.OnQuickstepExecuted"] = "quickstepExecuted",
            ["quantum.signal.OnItemConsumed"] = "itemUse",
            ["quantum.signal.OnNpcDied"] = "enemyDeath",
            ["combat.parry.attempt.candidate"] = "parryAttempt",
            ["combat.parry.fail.candidate"] = "parryFailed",
            ["combat.parry.success"] = "parrySuccess",
            ["combat.block.start.candidate"] = "blockStart",
            ["combat.block.end.candidate"] = "blockEnd",
            ["combat.block.success"] = "blockSuccess",
        };
        private static string _directory = "";
        private static string _configPath = "";
        private static bool _enabled = true;
        private static int _cooldownMs = 250;
        private static int _chargeDelayMs = 200;
        private static int _fallDelayMs = 300;
        private static bool _startupDiagnostic;
        private static bool _exportMap;
        private static float _defaultRate = 1f;
        private static object? _localHeroView;
        private static uint _chargePlayingId;
        private static int _chargeSequence;
        private static bool _attackPending;
        private static bool _windupActive;
        private static bool _chargeThresholdReached;
        private static string _pendingAttackPrefix = "";
        private static uint _fallPlayingId;
        private static int _fallSequence;
        private static bool _fallActive;
        public static string DirectoryPath => _directory;
        public static bool ExportMap => _exportMap;

        public static void Load()
        {
            _directory = Path.Combine(MelonLoader.Utils.MelonEnvironment.ModsDirectory, "NRFTW.ActionVoiceProbe");
            _configPath = Path.Combine(_directory, "bindings.ini");
            Directory.CreateDirectory(_directory);
            EnsureDefaultConfig();
            LoadConfig();
        }

        public static void StartStartupDiagnosticLoop()
        {
            if (!_startupDiagnostic)
            {
                return;
            }

            MelonCoroutines.Start(RunStartupDiagnosticLoop());
        }

        public static void ResetSessionState(string reason)
        {
            _localHeroView = null;
            LastPlayed.Clear();
            _chargePlayingId = 0;
            _chargeSequence++;
            _attackPending = false;
            _windupActive = false;
            _chargeThresholdReached = false;
            _pendingAttackPrefix = "";
            _fallPlayingId = 0;
            _fallSequence++;
            _fallActive = false;
            LogDebug($"[ActionVoiceProbe] audio.session.reset: reason={reason}");
        }

        public static void ObserveActionViewStart(object instance)
        {
            if (!IsLocalPlayerPlayback("weapon.attack.state", instance, Array.Empty<object>()))
            {
                return;
            }

            var viewName = GetActionViewName(instance);
            if (IsFallView(viewName))
            {
                CancelPendingAttack("player-fall");
                BeginPlayerFall(instance);
                return;
            }

            if (IsPlungeAttackView(viewName))
            {
                CancelPendingAttack("plunge-attack");
                LogDebug($"[ActionVoiceProbe] audio.attack.complete: view={viewName} kind=plunge");
                TryPlay("weapon.attack.plunge", instance, Array.Empty<object>());
                return;
            }

            if (TryGetWindupAttackPrefix(viewName, out var attackPrefix))
            {
                BeginAttackWindup(instance, viewName, attackPrefix);
                return;
            }

            if (IsBasicAttackAction(instance))
            {
                CompleteAttack(instance, viewName);
                return;
            }

            if (!_attackPending)
            {
                return;
            }

            if (!IsMatchingAttackView(viewName, _pendingAttackPrefix))
            {
                CancelPendingAttack($"next-action:{viewName}");
                return;
            }

            CompleteAttack(instance, viewName);
        }

        public static void ObserveActionViewStop(object instance)
        {
            if (!IsLocalPlayerPlayback("weapon.attack.state", instance, Array.Empty<object>()))
            {
                return;
            }

            var viewName = GetActionViewName(instance);
            if (IsFallView(viewName))
            {
                StopPlayerFall("view-stop");
                return;
            }

            if (!_windupActive
                || !TryGetWindupAttackPrefix(viewName, out var attackPrefix)
                || !attackPrefix.Equals(_pendingAttackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _chargeSequence++;
            _windupActive = false;
        }

        private static void BeginPlayerFall(object context)
        {
            StopPlayerFall("new-fall");
            _fallActive = true;
            var sequence = ++_fallSequence;
            LogDebug($"[ActionVoiceProbe] audio.fall.pending: delayMs={_fallDelayMs}");
            MelonCoroutines.Start(RunFallDelay(sequence, context));
        }

        private static IEnumerator RunFallDelay(int sequence, object context)
        {
            if (_fallDelayMs > 0)
            {
                yield return new WaitForSeconds(_fallDelayMs / 1000f);
            }

            if (!_fallActive || sequence != _fallSequence)
            {
                yield break;
            }

            LogDebug("[ActionVoiceProbe] audio.fall.start");
            _fallPlayingId = TryPlay("player.fall", context, Array.Empty<object>());
        }

        private static void StopPlayerFall(string reason)
        {
            if (_fallActive || _fallPlayingId != 0)
            {
                LogDebug($"[ActionVoiceProbe] audio.fall.stop: reason={reason}");
            }

            _fallSequence++;
            _fallActive = false;
            if (_fallPlayingId == 0)
            {
                return;
            }

            WwiseAudio.StopPlayingId(_fallPlayingId);
            _fallPlayingId = 0;
        }

        private static void BeginAttackWindup(object instance, string viewName, string attackPrefix)
        {
            CancelPendingAttack("new-windup");
            _attackPending = true;
            _windupActive = true;
            _pendingAttackPrefix = attackPrefix;
            var sequence = ++_chargeSequence;
            LogDebug($"[ActionVoiceProbe] audio.attack.pending: view={viewName} prefix={attackPrefix} delayMs={_chargeDelayMs}");
            MelonCoroutines.Start(RunChargeDelay(sequence, instance));
        }

        private static void CompleteAttack(object instance, string viewName)
        {
            var isChargedAttack = IsChargedAttackView(viewName, _pendingAttackPrefix);
            _chargeSequence++;
            _attackPending = false;
            _windupActive = false;
            _pendingAttackPrefix = "";
            StopChargePlayback();
            _chargeThresholdReached = false;
            if (isChargedAttack)
            {
                LogDebug($"[ActionVoiceProbe] audio.attack.complete: view={viewName} kind=charged");
                TryPlay("melee.charge.stop", instance, Array.Empty<object>());
                return;
            }

            LogDebug($"[ActionVoiceProbe] audio.attack.complete: view={viewName} kind=basic");
            TryPlay("weapon.attack.start", instance, Array.Empty<object>());
        }

        private static IEnumerator RunChargeDelay(int sequence, object context)
        {
            if (_chargeDelayMs > 0)
            {
                yield return new WaitForSeconds(_chargeDelayMs / 1000f);
            }

            if (!_windupActive || sequence != _chargeSequence)
            {
                yield break;
            }

            var hasInputState = TryGetHoldingInput(context, out var holdingInput);
            if (!hasInputState || !holdingInput)
            {
                LogDebug($"[ActionVoiceProbe] audio.attack.threshold.skip: view={GetActionViewName(context)} reason={(hasInputState ? "input-released" : "input-state-unavailable")}");
                yield break;
            }

            _chargeThresholdReached = true;
            LogDebug($"[ActionVoiceProbe] audio.attack.threshold: view={GetActionViewName(context)} delayMs={_chargeDelayMs}");
            _chargePlayingId = TryPlay("melee.charge.start", context, Array.Empty<object>());
        }

        private static void StopChargePlayback()
        {
            if (_chargePlayingId == 0)
            {
                return;
            }

            WwiseAudio.StopPlayingId(_chargePlayingId);
            _chargePlayingId = 0;
        }

        private static void CancelPendingAttack(string reason)
        {
            if (_attackPending || _windupActive || _chargeThresholdReached)
            {
                LogDebug($"[ActionVoiceProbe] audio.attack.cancel: reason={reason}");
            }

            _chargeSequence++;
            _attackPending = false;
            _windupActive = false;
            _chargeThresholdReached = false;
            _pendingAttackPrefix = "";
            StopChargePlayback();
        }

        private static bool TryGetWindupAttackPrefix(string viewName, out string attackPrefix)
        {
            attackPrefix = "";
            var normalized = NormalizeActionViewName(viewName);
            const string windupSuffix = "WindupView";
            if (!normalized.EndsWith(windupSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // GLUE: IL2CPP exposes concrete weapon views as ActionView here; remove this name contract when Harmony preserves the runtime view/asset type.
            attackPrefix = normalized[..^windupSuffix.Length];
            return attackPrefix.Length > 0;
        }

        private static bool IsMatchingAttackView(string viewName, string attackPrefix)
        {
            if (attackPrefix.Length == 0)
            {
                return false;
            }

            var normalized = NormalizeActionViewName(viewName);
            return normalized.Equals($"{attackPrefix}View", StringComparison.OrdinalIgnoreCase)
                || IsChargedAttackView(viewName, attackPrefix);
        }

        private static bool IsChargedAttackView(string viewName, string attackPrefix)
            => attackPrefix.Length > 0
                && NormalizeActionViewName(viewName).Equals($"{attackPrefix}ChargeView", StringComparison.OrdinalIgnoreCase);

        // GLUE: Reads the generated Quantum action component because the view lifecycle does not expose whether attack input is still held.
        private static unsafe bool TryGetHoldingInput(object context, out bool holdingInput)
        {
            holdingInput = false;
            try
            {
                var frame = FindFrame(context) as Il2CppQuantum.Frame;
                var entityView = GetOwningEntityView(context);
                var entityRef = GetPropertyValue(entityView, "EntityRef")
                    ?? GetFieldValue(entityView, "_EntityRef_k__BackingField");
                if (frame == null || entityRef is not Il2CppQuantum.EntityRef entity)
                {
                    return false;
                }

                var actionComponent = frame.Unsafe.GetPointer<Il2CppQuantum.ActionComponent>(entity);
                if (actionComponent == null)
                {
                    return false;
                }

                holdingInput = actionComponent->ActionCtx.HoldingInput.Value != 0;
                LogDebug($"[ActionVoiceProbe] audio.attack.input: view={GetActionViewName(context)} holding={holdingInput}");
                return true;
            }
            catch (Exception ex)
            {
                LogDebug($"[ActionVoiceProbe] audio.attack.input.error: view={GetActionViewName(context)} error={ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static string GetActionViewName(object? instance)
            => GetPropertyValue(instance, "BakedViewName")?.ToString()
                ?? (instance != null ? GetUnityObjectName(instance) : null)
                ?? "unknown";

        private static string NormalizeActionViewName(string viewName)
            => viewName.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase)
                ? viewName[..^"(Clone)".Length]
                : viewName;

        private static bool IsFallView(string viewName)
            => NormalizeActionViewName(viewName).Equals("fall", StringComparison.OrdinalIgnoreCase);

        // GLUE: ActionView exposes no HeroAttackType; remove this name contract when the view exposes the attack semantic directly.
        private static bool IsPlungeAttackView(string viewName)
            => NormalizeActionViewName(viewName).EndsWith("PlungeAttackView", StringComparison.OrdinalIgnoreCase);

        private static bool IsBasicAttackAction(object instance)
        {
            if (instance is not Il2CppMoon.Forsaken.ActionView actionView)
            {
                return false;
            }

            var actionData = actionView.GetQuantumActionData<Il2CppQuantum.ActionData>();
            if (actionData == null)
            {
                LogDebug($"[ActionVoiceProbe] audio.attack.classify: view={GetActionViewName(instance)} actionData=null basic=False");
                return false;
            }

            var actionSource = GetPropertyValue(actionView, "QuantumAsset") ?? GetFieldValue(actionView, "QuantumAsset");
            var action = actionSource != null ? ReadAssetIdentity(actionSource, actionView) : default;
            if (TryGetKnownRuneKey(action, out var runeKey))
            {
                LogDebug($"[ActionVoiceProbe] audio.attack.classify: view={GetActionViewName(instance)} actionType={actionData.ActionType} basic=False reason=rune rune={runeKey}");
                return false;
            }

            var isBasicAttack = Il2CppQuantum.ActionData.IsBasicAttackType(actionData.ActionType);
            LogDebug($"[ActionVoiceProbe] audio.attack.classify: view={GetActionViewName(instance)} actionType={actionData.ActionType} basic={isBasicAttack}");
            return isBasicAttack;
        }

        public static uint TryPlay(string hook, object? instance, object[] args)
        {
            try
            {
                if (!_enabled)
                {
                    return 0;
                }

                var keys = BuildKeys(hook, instance, args).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (keys.Count > 0 && ShouldLogLevel(ProbeLogLevel.Debug) && ShouldLog($"keys.{hook}"))
                {
                    LogDebug($"[ActionVoiceProbe] audio.keys: hook={hook} keys={string.Join(",", keys)}");
                }

                if (ConfiguredEvents.Count == 0)
                {
                    return 0;
                }

                foreach (var key in keys)
                {
                    if (!ConfiguredEvents.TryGetValue(key, out var eventNames))
                    {
                        continue;
                    }

                    if (!IsPlaybackAllowed(hook, instance, args))
                    {
                        if (ShouldLogLevel(ProbeLogLevel.Debug) && ShouldLog($"scope.{hook}"))
                        {
                            LogDebug($"[ActionVoiceProbe] audio.play.skip: hook={hook} key={key} reason=scope-mismatch");
                        }

                        return 0;
                    }

                    return PlayWwiseEvent(key, eventNames, GetPlaybackPitchCents(key, hook, instance, args));
                }
            }
            catch (Exception ex)
            {
                LogError($"[ActionVoiceProbe] audio.play.error: {ex.GetType().Name}: {ex.Message}");
            }

            return 0;
        }

        private static bool IsPlaybackAllowed(string hook, object? instance, object[] args)
        {
            if (hook.Equals("quantum.signal.OnNpcDied", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (hook.Equals("quantum.signal.OnPairedActionExecuted", StringComparison.OrdinalIgnoreCase))
            {
                return args.Length > 1 && IsLocalHeroEntity(args[1]);
            }

            return IsLocalPlayerPlayback(hook, instance, args);
        }

        private static float GetPlaybackPitchCents(string key, string hook, object? instance, object[] args)
        {
            var actionRate = GetActionRate(hook, instance, args);
            return CalculatePitchCents(key, hook, actionRate);
        }

        private static float GetActionRate(string hook, object? instance, object[] args)
        {
            if (hook.Equals("weapon.attack.start", StringComparison.OrdinalIgnoreCase)
                || hook.Equals("weapon.attack.plunge", StringComparison.OrdinalIgnoreCase)
                || hook.Equals("melee.charge.start", StringComparison.OrdinalIgnoreCase)
                || hook.Equals("melee.charge.stop", StringComparison.OrdinalIgnoreCase))
            {
                return GetViewActionRate(instance);
            }

            if (hook.Equals("quantum.signal.OnHeroExecutedSpecial", StringComparison.OrdinalIgnoreCase))
            {
                return GetRuneActionRate(instance, args);
            }

            return 1f;
        }

        private static float GetViewActionRate(object? instance)
        {
            // GLUE: Reflection reads the final IL2CPP animation speed across the game assembly boundary; remove when those assemblies become stable build references.
            try
            {
                var animationPlayer = GetPropertyValue(instance, "EffectiveMainAnimationPlayer")
                    ?? GetPropertyValue(instance, "MainAnimationPlayer")
                    ?? GetFieldValue(instance, "MainAnimationPlayer");
                var rawSpeed = GetPropertyValue(animationPlayer, "EffectiveSpeed");
                return ReadPositiveRate(rawSpeed, "view-effective-speed");
            }
            catch (Exception ex)
            {
                LogDebug($"[ActionVoiceProbe] audio.rate.fallback: source=view-effective-speed reason={ex.GetType().Name} rate=1");
                return 1f;
            }
        }

        private static float GetRuneActionRate(object? instance, object[] args)
        {
            if (args.Length < 2)
            {
                LogDebug("[ActionVoiceProbe] audio.rate.fallback: source=rune-time-scale reason=arguments-missing rate=1");
                return 1f;
            }

            // GLUE: Reflection bridges the Quantum signal to ActionData.TimeScale without taking generated game assemblies as build references.
            try
            {
                var frame = FindFrame(instance);
                var actionData = ResolveAsset(args[1], frame);
                var method = actionData?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(candidate => candidate.Name.Equals("TimeScale", StringComparison.Ordinal)
                        && candidate.GetParameters().Length == 2);
                if (frame == null || actionData == null || method == null)
                {
                    LogDebug("[ActionVoiceProbe] audio.rate.fallback: source=rune-time-scale reason=action-data-missing rate=1");
                    return 1f;
                }

                var timeScale = method.Invoke(actionData, new[] { frame, args[0] });
                return ReadPositiveRate(GetPropertyValue(timeScale, "AsFloat"), "rune-time-scale");
            }
            catch (Exception ex)
            {
                LogDebug($"[ActionVoiceProbe] audio.rate.fallback: source=rune-time-scale reason={ex.GetType().Name} rate=1");
                return 1f;
            }
        }

        private static float ReadPositiveRate(object? rawRate, string source)
        {
            if (rawRate == null)
            {
                LogDebug($"[ActionVoiceProbe] audio.rate.fallback: source={source} reason=value-missing rate=1");
                return 1f;
            }

            var rate = Convert.ToSingle(rawRate, CultureInfo.InvariantCulture);
            if (!float.IsFinite(rate) || rate <= 0f)
            {
                LogDebug($"[ActionVoiceProbe] audio.rate.fallback: source={source} reason=invalid-value value={rate} rate=1");
                return 1f;
            }

            return rate;
        }

        private static float CalculatePitchCents(string key, string hook, float actionRate)
        {
            var configuredRate = ConfiguredRates.TryGetValue(key, out var keyRate) ? keyRate : _defaultRate;
            var finalRate = Math.Clamp(actionRate * configuredRate, 0.25f, 4f);
            var pitchCents = 1200f * MathF.Log2(finalRate);
            LogDebug($"[ActionVoiceProbe] audio.pitch: hook={hook} key={key} actionRate={actionRate:0.###} configuredRate={configuredRate:0.###} finalRate={finalRate:0.###} cents={pitchCents:0.#}");
            return pitchCents;
        }

        private static bool IsLocalPlayerPlayback(string hook, object? instance, object[] args)
        {
            try
            {
                // GLUE: Reflection bridges generated IL2CPP view ownership to the generic audio boundary; remove when game assemblies become stable build references.
                var entityView = GetOwningEntityView(instance);
                if (TryGetIsLocalPlayer(entityView ?? instance, out var isLocalPlayer))
                {
                    return isLocalPlayer;
                }

                return args.Length > 0
                    && IsEntityRef(args[0])
                    && IsLocalHeroEntity(args[0]);
            }
            catch (Exception ex)
            {
                LogDebug($"[ActionVoiceProbe] audio.scope.error: hook={hook} error={ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static bool TryGetIsLocalPlayer(object? value, out bool isLocalPlayer)
        {
            isLocalPlayer = false;
            if (value == null)
            {
                return false;
            }

            try
            {
                var property = value.GetType().GetProperty("IsLocalPlayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property?.GetValue(value) is not bool result)
                {
                    return false;
                }

                isLocalPlayer = result;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEntityRef(object? value)
            => value?.GetType().FullName is "Quantum.EntityRef" or "Il2CppQuantum.EntityRef";

        private static bool IsLocalHeroEntity(object entityRef)
        {
            var localHeroView = GetLocalHeroView();
            var localEntityRef = GetPropertyValue(localHeroView, "EntityRef")
                ?? GetFieldValue(localHeroView, "_EntityRef_k__BackingField");
            return localEntityRef != null && localEntityRef.Equals(entityRef);
        }

        private static object? GetLocalHeroView()
        {
            if (IsLiveSceneComponent(_localHeroView)
                && TryGetIsLocalPlayer(_localHeroView, out var isLocalPlayer)
                && isLocalPlayer)
            {
                return _localHeroView;
            }

            var heroViewType = ResolveType("Moon.Forsaken.HeroView");
            if (heroViewType == null)
            {
                return null;
            }

            var il2CppType = Il2CppInterop.Runtime.Il2CppType.From(heroViewType);
            _localHeroView = UnityEngine.Resources.FindObjectsOfTypeAll(il2CppType)
                .Cast<object>()
                .FirstOrDefault(heroView => IsLiveSceneComponent(heroView)
                    && TryGetIsLocalPlayer(heroView, out var local)
                    && local);
            return _localHeroView;
        }

        private static bool IsLiveSceneComponent(object? value)
            => value is Component component
                && component != null
                && component.gameObject.scene.isLoaded;

        private static object? GetOwningEntityView(object? instance)
            => GetPropertyValue(instance, "OwningEntityView")
                ?? GetPropertyValue(instance, "EntityOwner")
                ?? GetPropertyValue(instance, "CharacterView")
                ?? GetFieldValue(instance, "m_entityOwner")
                ?? GetFieldValue(instance, "m_characterView");

        private static IEnumerable<string> BuildKeys(string hook, object? instance, object[] args)
        {
            if (hook.Equals("weapon.attack.start", StringComparison.OrdinalIgnoreCase))
            {
                var actionSource = GetPropertyValue(instance, "QuantumAsset") ?? GetFieldValue(instance, "QuantumAsset");
                var weaponAction = actionSource != null ? ReadAssetIdentity(actionSource, instance) : default;
                foreach (var key in BuildWeaponAttackKeys(instance, weaponAction))
                {
                    yield return key;
                }

                yield return "basicAttack";
                yield break;
            }

            if (hook.Equals("quantum.signal.OnPairedActionExecuted", StringComparison.OrdinalIgnoreCase))
            {
                var actionData = args.Length > 3 ? ResolveAsset(args[3], instance) : null;
                if (IsBackstabAction(actionData))
                {
                    yield return "backstab";
                }

                yield break;
            }

            if (hook.Equals("quantum.signal.OnActionCompleted", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            if (hook.Equals("quantum.signal.OnPlayerPerformedGestureDirector", StringComparison.OrdinalIgnoreCase))
            {
                var gestureAction = args.Length > 1 ? ReadAssetIdentity(args[1], instance) : default;
                var gestureName = CleanGestureName(gestureAction.AssetName ?? "");
                if (gestureName.Length == 0
                    && !string.IsNullOrWhiteSpace(gestureAction.Id)
                    && GestureActionNames.TryGetValue(gestureAction.Id, out var mappedName))
                {
                    gestureName = mappedName;
                    LogDebug($"[ActionVoiceProbe] gesture.action.fallback: actionId={gestureAction.Id} name={gestureName}");
                }

                if (gestureName.Length > 0)
                {
                    yield return $"gesture.{gestureName}";
                }

                yield break;
            }

            if (hook.Equals("quantum.signal.OnPlayerDied", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var key in BuildPlayerDeathKeys(args))
                {
                    yield return key;
                }

                yield break;
            }

            if (hook.Equals("quantum.signal.OnLevelChanged", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length > 2 && Convert.ToInt32(args[2]) > Convert.ToInt32(args[1]))
                {
                    yield return "playerLevelUp";
                }

                yield break;
            }

            var action = args.Length > 1 ? ReadAssetIdentity(args[1], instance) : default;
            if (hook.Equals("quantum.signal.OnActionExecuted", StringComparison.OrdinalIgnoreCase)
                && IsShoulderBargeAction(action))
            {
                yield return "shoulderBarge";
                yield break;
            }

            if (hook.Equals("quantum.signal.OnHeroExecutedSpecial", StringComparison.OrdinalIgnoreCase)
                && TryGetRuneKey(action, instance, out var runeKey))
            {
                yield return runeKey;
            }

            if (StandardActionKeys.TryGetValue(hook, out var actionKey))
            {
                yield return actionKey;
            }
        }

        private static bool IsShoulderBargeAction((string? Id, string? AssetName) action)
            => action.AssetName?.Equals("heroAttackShoulderBargeData", StringComparison.OrdinalIgnoreCase) == true;

        private static bool IsBackstabAction(object? actionData)
        {
            if (actionData is not Il2CppQuantum.PairedActionData pairedActionData)
            {
                LogDebug($"[ActionVoiceProbe] audio.paired.classify: resolved={actionData?.GetType().FullName ?? "null"} finisher=null");
                return false;
            }

            var finisherActionData = pairedActionData.TryCast<Il2CppQuantum.FinisherPairedActionData>();
            if (finisherActionData == null)
            {
                LogDebug($"[ActionVoiceProbe] audio.paired.classify: resolved={actionData.GetType().FullName} finisher=null");
                return false;
            }

            LogDebug($"[ActionVoiceProbe] audio.paired.classify: resolved={actionData.GetType().FullName} finisher={finisherActionData.Type}");
            return finisherActionData.Type == Il2CppQuantum.FinisherType.Backstab;
        }

        private static IEnumerable<string> BuildPlayerDeathKeys(object[] args)
        {
            var damage = args.Length > 1 ? args[1] : null;
            var response = args.Length > 2 ? args[2] : null;
            var damageFlags = GetFieldValue(damage, "Flags") ?? GetPropertyValue(damage, "Flags");
            var eventFlags = GetFieldValue(response, "EventFlags") ?? GetPropertyValue(response, "EventFlags");
            // GLUE: Reads the generated IL2CPP damage proxy flag; remove when game damage types become stable build references.
            var proxy = GetFieldValue(damage, "Proxy") ?? GetPropertyValue(damage, "Proxy");
            var anonymousProxies = GetFieldValue(proxy, "m_anonymousProxies");

            if (HasEnumFlag(anonymousProxies, "Fall"))
            {
                yield return "playerDeath.fall";
            }

            if (HasEnumFlag(damageFlags, "Backstab") || HasEnumFlag(eventFlags, "OnBackstab"))
            {
                yield return "playerDeath.backstab";
            }

            if (HasEnumFlag(damageFlags, "InstaKill") || HasEnumFlag(eventFlags, "OnFatality"))
            {
                yield return "playerDeath.fatality";
            }

            if (HasEnumFlag(damageFlags, "Critical") || HasEnumFlag(eventFlags, "OnCritical"))
            {
                yield return "playerDeath.critical";
            }

            if (HasEnumFlag(eventFlags, "OnRune"))
            {
                yield return "playerDeath.rune";
            }

            var school = GetFieldValue(damage, "School") ?? GetPropertyValue(damage, "School");
            if (school == null || school.ToString()?.Equals("None", StringComparison.OrdinalIgnoreCase) == true)
            {
                school = GetFieldValue(response, "School") ?? GetPropertyValue(response, "School");
            }
            var schoolKey = school?.ToString()?.ToLowerInvariant() switch
            {
                "heat" => "playerDeath.heat",
                "cold" => "playerDeath.cold",
                "electric" => "playerDeath.electric",
                "plague" => "playerDeath.plague",
                "bleed" => "playerDeath.bleed",
                "physical" => "playerDeath.physical",
                _ => null,
            };
            if (schoolKey != null)
            {
                yield return schoolKey;
            }

            yield return "playerDeath";
        }

        private static bool HasEnumFlag(object? value, string flagName)
        {
            if (value == null || !value.GetType().IsEnum)
            {
                return false;
            }

            try
            {
                var flag = Enum.Parse(value.GetType(), flagName, ignoreCase: false);
                var flagValue = Convert.ToUInt64(flag);
                return flagValue != 0 && (Convert.ToUInt64(value) & flagValue) == flagValue;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> BuildWeaponAttackKeys(
            object? instance,
            (string? Id, string? AssetName) action)
        {
            var weaponView = GetFieldValue(instance, "m_weaponView") ?? GetPropertyValue(instance, "m_weaponView");
            var weaponRef = GetFieldValue(weaponView, "WeaponData") ?? GetPropertyValue(weaponView, "WeaponData");
            var weapon = ResolveAsset(weaponRef, instance, weaponView);
            var weaponInfo = weaponRef != null ? ReadAssetIdentity(weaponRef, instance, weaponView) : default;
            var combo = FindCombo(weapon, action, instance, weaponView);

            if (!string.IsNullOrWhiteSpace(weaponInfo.Id) && combo != null)
            {
                yield return $"weapon.{NormalizeKeyPart(weaponInfo.Id)}.{combo.Value.AttackType}.{combo.Value.ComboIndex}";
            }

            if (!string.IsNullOrWhiteSpace(weaponInfo.AssetName) && combo != null)
            {
                yield return $"weapon.{NormalizeKeyPart(weaponInfo.AssetName)}.{combo.Value.AttackType}.{combo.Value.ComboIndex}";
            }

            if (combo != null)
            {
                yield return $"attack.{combo.Value.AttackType}.{combo.Value.ComboIndex}";
                yield return $"attack.{combo.Value.AttackType}";
            }

            if (!string.IsNullOrWhiteSpace(action.Id))
            {
                yield return $"attackId.{NormalizeKeyPart(action.Id)}";
            }
        }

        private static (string AttackType, int ComboIndex)? FindCombo(object? weapon, (string? Id, string? AssetName) action, params object?[] contexts)
        {
            if (weapon == null || string.IsNullOrWhiteSpace(action.Id))
            {
                return null;
            }

            var method = weapon.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                {
                    if (!method.Name.Equals("GetCombo", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 2
                        && parameters[1].ParameterType.IsEnum
                        && parameters[1].ParameterType.Name.Equals("HeroAttackType", StringComparison.Ordinal);
                });
            if (method == null)
            {
                return null;
            }

            var frame = FindFrame(contexts);
            if (frame == null)
            {
                return null;
            }

            foreach (var attackType in Enum.GetNames(method.GetParameters()[1].ParameterType)
                .Where(name => !name.Equals("Count", StringComparison.OrdinalIgnoreCase)))
            {
                var combo = InvokeGetCombo(method, weapon, frame, attackType);
                var comboIndex = FindActionIndex(combo, action.Id, contexts);
                if (comboIndex >= 0)
                {
                    return (NormalizeKeyPart(attackType), comboIndex + 1);
                }
            }

            return null;
        }

        private static object? InvokeGetCombo(MethodInfo method, object weapon, object frame, string attackType)
        {
            try
            {
                var enumValue = Enum.Parse(method.GetParameters()[1].ParameterType, attackType);
                var context = GetAssetResolutionArgument(method.GetParameters()[0], frame);
                return method.Invoke(weapon, new[] { context, enumValue });
            }
            catch (Exception ex)
            {
                LogDebug($"[ActionVoiceProbe] weapon.combo.error: attackType={attackType} error={ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static int FindActionIndex(object? combo, string actionId, params object?[] contexts)
        {
            var index = 0;
            foreach (var item in Enumerate(combo))
            {
                var candidate = ReadAssetIdentity(item, contexts);
                if (!string.IsNullOrWhiteSpace(candidate.Id) && candidate.Id.Equals(actionId, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }

                index++;
            }

            return -1;
        }

        private static IEnumerable<object> Enumerate(object? value)
        {
            if (value is not IEnumerable enumerable)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }

        private static object? ResolveAsset(object? assetRef, params object?[] contexts)
        {
            if (assetRef == null)
            {
                return null;
            }

            var frame = FindFrame(contexts);
            if (frame == null)
            {
                return null;
            }

            try
            {
                if (frame is Il2CppQuantum.Frame quantumFrame)
                {
                    if (assetRef is Il2CppQuantum.AssetRefActionData actionRef)
                    {
                        return quantumFrame.FindAsset<Il2CppQuantum.ActionData>(actionRef.Id);
                    }

                    if (assetRef is Il2CppQuantum.AssetRefPairedActionData pairedActionRef)
                    {
                        return quantumFrame.FindAsset<Il2CppQuantum.PairedActionData>(pairedActionRef.Id);
                    }
                }

                var method = assetRef.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(method => method.Name.Equals("Get", StringComparison.Ordinal) && method.GetParameters().Length == 1);
                if (method == null)
                {
                    return null;
                }

                var argument = GetAssetResolutionArgument(method.GetParameters()[0], frame);
                return method.Invoke(assetRef, new[] { argument });
            }
            catch (Exception ex)
            {
                LogDebug($"[ActionVoiceProbe] asset.resolve.error: type={assetRef.GetType().FullName} error={ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static object GetAssetResolutionArgument(ParameterInfo parameter, object frame)
        {
            if (frame is Il2CppQuantum.Frame typedFrame
                && parameter.ParameterType.Name.Equals("IAssetResolutionContext", StringComparison.Ordinal))
            {
                // GLUE: Generated asset APIs require the IL2CPP interface proxy instead of the concrete Frame proxy.
                return typedFrame.Cast<Il2CppQuantum.IAssetResolutionContext>();
            }

            return frame;
        }

        private static object? FindFrame(params object?[] contexts)
        {
            foreach (var context in contexts)
            {
                if (context?.GetType().FullName is "Quantum.Frame" or "Il2CppQuantum.Frame")
                {
                    return context;
                }

                var frame = GetFieldValue(context, "_f") ?? GetPropertyValue(context, "_f");
                if (frame != null)
                {
                    return frame;
                }

                frame = GetPropertyValue(context, "PredictedFrame") ?? GetPropertyValue(context, "VerifiedFrame");
                if (frame != null)
                {
                    return frame;
                }

                var entityView = GetOwningEntityView(context);
                frame = GetPropertyValue(entityView, "PredictedFrame") ?? GetPropertyValue(entityView, "VerifiedFrame");
                if (frame != null)
                {
                    return frame;
                }
            }

            return null;
        }

        private static string NormalizeKeyPart(string value)
        {
            var chars = value.Trim('[', ']', ' ', '\t').Select(ch =>
            {
                if (char.IsLetterOrDigit(ch))
                {
                    return char.ToLowerInvariant(ch);
                }

                return ch == '_' || ch == '-' ? ch : '-';
            }).ToArray();

            return new string(chars).Trim('-');
        }

        private static bool TryGetRuneKey((string? Id, string? AssetName) action, object? context, out string key)
        {
            if (TryGetKnownRuneKey(action, out key))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(action.Id))
            {
                LoadRuneActionNames(context);
                return TryGetKnownRuneKey(action, out key);
            }

            key = "";
            return false;
        }

        private static bool TryGetKnownRuneKey((string? Id, string? AssetName) action, out string key)
        {
            if (!string.IsNullOrWhiteSpace(action.AssetName) && KnownRuneNames.Contains(action.AssetName))
            {
                key = action.AssetName;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(action.Id) && RuneActionNames.TryGetValue(action.Id, out var runeName))
            {
                key = runeName;
                return true;
            }

            key = "";
            return false;
        }

        private static void LoadRuneActionNames(object? context)
        {
            var frame = FindFrame(context);
            var assetType = ResolveType("HeroRuneDataAsset");
            if (frame == null || assetType == null)
            {
                return;
            }

            var mappedRunes = 0;
            var il2CppType = Il2CppInterop.Runtime.Il2CppType.From(assetType);
            foreach (var asset in UnityEngine.Resources.FindObjectsOfTypeAll(il2CppType).Cast<object>())
            {
                var runeName = CleanAssetName(GetUnityObjectName(asset));
                if (!KnownRuneNames.Contains(runeName))
                {
                    continue;
                }

                var runeData = ResolveAsset(asset, frame);
                var actions = GetPropertyValue(runeData, "Actions") ?? GetFieldValue(runeData, "Actions");
                var mapped = false;
                foreach (var actionRef in Enumerate(actions))
                {
                    var actionId = ReadAssetIdentity(actionRef, frame).Id;
                    if (string.IsNullOrWhiteSpace(actionId))
                    {
                        continue;
                    }

                    RuneActionNames[actionId] = runeName;
                    mapped = true;
                }

                if (mapped)
                {
                    mappedRunes++;
                }
            }

            LogInfo($"[ActionVoiceProbe] rune.actions.loaded: runes={mappedRunes} actions={RuneActionNames.Count}");
        }

        public static void SyncRuneDisplayNames(IReadOnlyDictionary<string, string> displayNames)
        {
            if (displayNames.Count == 0 || !File.Exists(_configPath))
            {
                return;
            }

            const string commentPrefix = "; 中文名：";
            var lines = File.ReadAllLines(_configPath).ToList();
            var section = "";
            var changed = false;
            var synced = 0;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line[1..^1].Trim();
                    continue;
                }

                if (!section.Equals("runes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                var key = line[..equals].Trim();
                if (!displayNames.TryGetValue(key, out var displayName) || string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var comment = commentPrefix + displayName.Trim();
                if (i > 0 && lines[i - 1].TrimStart().StartsWith(commentPrefix, StringComparison.Ordinal))
                {
                    if (!lines[i - 1].Equals(comment, StringComparison.Ordinal))
                    {
                        lines[i - 1] = comment;
                        changed = true;
                    }
                }
                else
                {
                    lines.Insert(i, comment);
                    i++;
                    changed = true;
                }

                synced++;
            }

            if (changed)
            {
                File.WriteAllLines(_configPath, lines, new System.Text.UTF8Encoding(false));
            }

            LogInfo($"[ActionVoiceProbe] bindings.rune-names.synced: names={synced} changed={changed} file={_configPath}");
        }

        public static void SyncGestureActionNames(IReadOnlyDictionary<string, string> actionNames)
        {
            GestureActionNames.Clear();
            foreach (var pair in actionNames)
            {
                GestureActionNames[pair.Key] = pair.Value;
            }

            LogInfo($"[ActionVoiceProbe] gesture.actions.loaded: actions={GestureActionNames.Count}");
        }

        private static (string? Id, string? AssetName) ReadAssetIdentity(object value, params object?[] contexts)
        {
            var type = value.GetType();
            var isAssetRef = type.FullName?.Contains("AssetRef", StringComparison.Ordinal) == true;
            var id = isAssetRef
                ? GetFieldValue(value, "Id") ?? GetPropertyValue(value, "Id")
                : GetFieldValue(value, "AssetGuid") ?? GetPropertyValue(value, "AssetGuid");
            if (id == null)
            {
                return default;
            }

            var idText = id.ToString()?.Trim('[', ']');
            if (!isAssetRef)
            {
                return (idText, GetUnityObjectName(value));
            }

            var frame = FindFrame(contexts);
            var findAssetName = frame?.GetType().GetMethod("FindAssetName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var assetName = findAssetName?.Invoke(frame, new[] { id })?.ToString();
            if (assetName != null && assetName.StartsWith("[", StringComparison.Ordinal) && assetName.EndsWith("]", StringComparison.Ordinal))
            {
                assetName = null;
            }

            return (idText, assetName);
        }

        private static void LoadConfig()
        {
            ConfiguredEvents.Clear();
            ConfiguredRates.Clear();
            LastPlayed.Clear();
            _enabled = true;
            _cooldownMs = 250;
            _chargeDelayMs = 200;
            _fallDelayMs = 300;
            _startupDiagnostic = false;
            _exportMap = false;
            _defaultRate = 1f;
            _logLevel = ProbeLogLevel.None;

            var section = "";
            var configuredEvents = 0;
            foreach (var rawLine in File.ReadAllLines(_configPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line[1..^1].Trim();
                    continue;
                }

                var equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (section.Equals("general", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyGeneral(key, value);
                }
                else if (section.Equals("audio", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyAudio(key, value);
                }
                else if (IsBindingSection(section) && key.Length > 0 && value.Length > 0)
                {
                    var eventNames = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (eventNames.Length > 0)
                    {
                        var bindingKey = section.Equals("gestures", StringComparison.OrdinalIgnoreCase)
                            ? $"gesture.{key}"
                            : key;
                        ConfiguredEvents[bindingKey] = eventNames;
                        configuredEvents++;
                    }
                }
            }

            var wwiseBasePath = Path.Combine(_directory, "wwise");
            var gameWwiseBasePath = Path.Combine(Application.dataPath, "StreamingAssets", "Audio", "GeneratedSoundBanks", "Windows");
            WwiseAudio.Configure(wwiseBasePath, gameWwiseBasePath);
            LogInfo($"[ActionVoiceProbe] audio.config.loaded: configured={configuredEvents} rates={ConfiguredRates.Count} defaultRate={_defaultRate:0.###} file={_configPath}");
        }

        private static IEnumerator RunStartupDiagnosticLoop()
        {
            yield return new WaitForSeconds(5f);

            if (!_enabled)
            {
                LogWarning("[ActionVoiceProbe] audio.startupDiagnostic.skip: audio disabled");
                yield break;
            }

            if (!ConfiguredEvents.TryGetValue("basicAttack", out var eventNames))
            {
                LogWarning("[ActionVoiceProbe] audio.startupDiagnostic.skip: basicAttack not configured");
                yield break;
            }

            LogInfo($"[ActionVoiceProbe] audio.startupDiagnostic.begin: mode=wwise events={eventNames.Length}");
            PlayWwiseEvent("__startupDiagnostic", eventNames, CalculatePitchCents("basicAttack", "startupDiagnostic", 1f));
            LogInfo("[ActionVoiceProbe] audio.startupDiagnostic.end");
        }

        private static uint PlayWwiseEvent(string key, string[] eventNames, float pitchCents)
        {
            var now = DateTime.UtcNow;
            if (LastPlayed.TryGetValue(key, out var lastPlayed) && (now - lastPlayed).TotalMilliseconds < _cooldownMs)
            {
                return 0;
            }

            LastPlayed[key] = now;
            var eventName = eventNames[UnityEngine.Random.Range(0, eventNames.Length)];
            return WwiseAudio.PostEvent(key, eventName, pitchCents);
        }


        private static bool IsBindingSection(string section)
        {
            return section.Equals("actions", StringComparison.OrdinalIgnoreCase)
                || section.Equals("attacks", StringComparison.OrdinalIgnoreCase)
                || section.Equals("runes", StringComparison.OrdinalIgnoreCase)
                || section.Equals("gestures", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyGeneral(string key, string value)
        {
            if (key.Equals("enabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var enabled))
            {
                _enabled = enabled;
            }
            else if (key.Equals("cooldownMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var cooldownMs))
            {
                _cooldownMs = Math.Max(0, cooldownMs);
            }
            else if (key.Equals("chargeDelayMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var chargeDelayMs))
            {
                _chargeDelayMs = Math.Max(0, chargeDelayMs);
            }
            else if (key.Equals("fallDelayMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var fallDelayMs))
            {
                _fallDelayMs = Math.Max(0, fallDelayMs);
            }
            else if (key.Equals("startupDiagnostic", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var startupDiagnostic))
            {
                _startupDiagnostic = startupDiagnostic;
            }
            else if (key.Equals("exportMap", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var exportMap))
            {
                _exportMap = exportMap;
            }
            else if (key.Equals("logLevel", StringComparison.OrdinalIgnoreCase) && TryParseLogLevel(value, out var logLevel))
            {
                _logLevel = logLevel;
            }
        }

        private static void ApplyAudio(string key, string value)
        {
            const string ratePrefix = "rate.";
            if (!key.StartsWith(ratePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var bindingKey = key[ratePrefix.Length..].Trim();
            if (bindingKey.Length == 0
                || !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
                || !float.IsFinite(rate)
                || rate <= 0f)
            {
                LogWarning($"[ActionVoiceProbe] audio.config.rate.invalid: key={key} value={value}");
                return;
            }

            if (bindingKey.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                _defaultRate = rate;
                return;
            }

            ConfiguredRates[bindingKey] = rate;
        }

        private static void EnsureDefaultConfig()
        {
            if (File.Exists(_configPath))
            {
                return;
            }

            File.WriteAllText(_configPath, string.Join(Environment.NewLine, BuildDefaultConfigLines()));
        }

        private static IEnumerable<string> BuildDefaultConfigLines()
        {
            yield return "; NRFTW Action Voice Probe audio bindings.";
            yield return "; Fill a value to enable playback. Empty values do nothing.";
            yield return "; Binding value: EventName or comma-separated EventName list";
            yield return "; Restart the game after changing this file or replacing audio files.";
            yield return "";
            yield return "[general]";
            yield return "enabled=true";
            yield return "cooldownMs=250";
            yield return "chargeDelayMs=200";
            yield return "fallDelayMs=300";
            yield return "startupDiagnostic=false";
            yield return "exportMap=false";
            yield return "; Log level: none, debug, info, warning, or error.";
            yield return "logLevel=none";
            yield return "";
            yield return "[audio]";
            yield return "; Multiplier applied after the action speed. Override with rate.<bindingKey>.";
            yield return "rate.default=1.0";
            yield return "";
            yield return "[actions]";
            yield return "basicAttack=";
            yield return "chargedAttackStart=";
            yield return "chargedAttackStop=";
            yield return "dodgeRoll=";
            yield return "quickstep=";
            yield return "itemUse=";
            yield return "backstab=";
            yield return "shoulderBarge=";
            yield return "playerLevelUp=";
            yield return "playerFall=";
            yield return "playerDeath.fall=";
            yield return "playerDeath.backstab=";
            yield return "playerDeath.fatality=";
            yield return "playerDeath.critical=";
            yield return "playerDeath.rune=";
            yield return "playerDeath.heat=";
            yield return "playerDeath.cold=";
            yield return "playerDeath.electric=";
            yield return "playerDeath.plague=";
            yield return "playerDeath.bleed=";
            yield return "playerDeath.physical=";
            yield return "playerDeath=";
            yield return "enemyDeath=";
            yield return "parryAttempt=";
            yield return "parryFailed=";
            yield return "parrySuccess=";
            yield return "blockStart=";
            yield return "blockEnd=";
            yield return "blockSuccess=";
            yield return "";
            yield return "[attacks]";
            yield return "attack.normal=";
            yield return "attack.normal.1=";
            yield return "attack.normal.2=";
            yield return "attack.run.1=";
            yield return "attack.quickstep.1=";
            yield return "attack.roll.1=";
            yield return "attack.plunge=nrftw_action_voice_ground_slam";
            yield return "attack.parry.1=";
            yield return "";
            yield return "[gestures]";
            yield return "bestialPower=";
            yield return "";
            yield return "[runes]";
            foreach (var runeName in RuneNames)
            {
                yield return $"{runeName}=nrftw_action_voice_{ToSnakeCase(runeName)}";
            }
            yield return "";
        }

        private static string ToSnakeCase(string value)
        {
            var chars = new List<char>(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (i > 0 && char.IsUpper(ch) && char.IsLower(value[i - 1]))
                {
                    chars.Add('_');
                }

                chars.Add(char.ToLowerInvariant(ch));
            }

            return new string(chars.ToArray());
        }
    }

    private static class BindingMetadata
    {
        private const string OutputFileName = "map.csv";
        private const string EmbeddedBindingsResourceName = "Nrftw.ActionVoiceProbe.bindings.ini";

        public static void Start(string directory, bool exportMap)
        {
            MelonCoroutines.Start(SyncAfterDelay(directory, exportMap));
        }

        private static IEnumerator SyncAfterDelay(string directory, bool exportMap)
        {
            yield return new WaitForSeconds(10f);

            var game = GetQuantumGame();
            for (var attempt = 0; attempt < 60 && game == null; attempt++)
            {
                yield return new WaitForSeconds(1f);
                game = GetQuantumGame();
            }

            try
            {
                var runeRows = game != null
                    ? CollectAssets(game, "rune", "Quantum.HeroRuneData", "HeroRuneDataAsset", "ItemNameMsg", isGesture: false).ToList()
                    : new List<MapRow>();
                var gestureRows = game != null
                    ? CollectAssets(game, "gesture", "Quantum.CollectableGestureData", "CollectableGestureDataAsset", "LocalizedName", isGesture: true).ToList()
                    : new List<MapRow>();
                var gestureActionNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in gestureRows)
                {
                    if (!string.IsNullOrWhiteSpace(row.ActionId) && !string.IsNullOrWhiteSpace(row.Name))
                    {
                        gestureActionNames[row.ActionId] = row.Name;
                    }
                }

                ActionAudio.SyncGestureActionNames(gestureActionNames);
                // GLUE: Embedded generated metadata keeps user configs readable when game assets are not resident; remove when the runtime catalog is always enumerable.
                var displayNames = LoadEmbeddedRuneDisplayNames();
                foreach (var row in runeRows)
                {
                    if (!string.IsNullOrWhiteSpace(row.Name) && !string.IsNullOrWhiteSpace(row.ChineseName))
                    {
                        displayNames[row.Name] = row.ChineseName;
                    }
                }

                ActionAudio.SyncRuneDisplayNames(displayNames);

                if (exportMap)
                {
                    if (game == null)
                    {
                        LogWarning("[ActionVoiceProbe] map.export.skip: reason=game-unavailable");
                        yield break;
                    }

                    var rows = new List<MapRow>();
                    rows.AddRange(CollectAssets(game, "weapon", "Quantum.WeaponStaticData", "WeaponStaticDataAsset", "ItemNameMsg", isGesture: false));
                    rows.AddRange(runeRows);
                    rows.AddRange(gestureRows);
                    Export(directory, rows);
                }
            }
            catch (Exception ex)
            {
                LogError($"[ActionVoiceProbe] binding.metadata.error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static Dictionary<string, string> LoadEmbeddedRuneDisplayNames()
        {
            var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var stream = typeof(ActionVoiceProbePlugin).Assembly.GetManifestResourceStream(EmbeddedBindingsResourceName);
            if (stream == null)
            {
                LogWarning($"[ActionVoiceProbe] binding.metadata.resource.missing: name={EmbeddedBindingsResourceName}");
                return displayNames;
            }

            using var reader = new StreamReader(stream);
            const string commentPrefix = "; 中文名：";
            var section = "";
            string? displayName = null;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    section = trimmed[1..^1].Trim();
                    displayName = null;
                    continue;
                }

                if (!section.Equals("runes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (trimmed.StartsWith(commentPrefix, StringComparison.Ordinal))
                {
                    displayName = trimmed[commentPrefix.Length..].Trim();
                    continue;
                }

                var equals = trimmed.IndexOf('=');
                if (equals > 0 && !string.IsNullOrWhiteSpace(displayName))
                {
                    displayNames[trimmed[..equals].Trim()] = displayName;
                }

                displayName = null;
            }

            return displayNames;
        }

        private static void Export(string directory, IReadOnlyCollection<MapRow> rows)
        {
            if (rows.Count == 0)
            {
                LogWarning("[ActionVoiceProbe] map.export.skip: reason=no-assets");
                return;
            }

            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, OutputFileName);

            using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
            writer.WriteLine("type,chineseName,name,assetGuid,actionId,assetObjectName,localizationId");
            foreach (var row in rows
                .OrderBy(row => row.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteLine(string.Join(",", new[]
                {
                    Csv(row.Type),
                    Csv(row.ChineseName),
                    Csv(row.Name),
                    Csv(row.AssetGuid),
                    Csv(row.ActionId),
                    Csv(row.AssetObjectName),
                    Csv(row.LocalizationId),
                }));
            }

            LogInfo($"[ActionVoiceProbe] map.export.done: file={path} rows={rows.Count}");
        }

        private static object? GetQuantumGame()
        {
            try
            {
                var sessionViewBindingType = ResolveType("frameworks.session.SessionViewBinding");
                var session = sessionViewBindingType?
                    .GetProperty("Session", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(null);
                return GetPropertyValue(session, "Game") ?? GetFieldValue(session, "Game");
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<MapRow> CollectAssets(
            object game,
            string type,
            string assetTypeName,
            string wrapperTypeName,
            string localizedNameMember,
            bool isGesture)
        {
            var assetType = ResolveType(assetTypeName);
            var wrapperType = ResolveType(wrapperTypeName);
            if (assetType == null || wrapperType == null)
            {
                LogWarning($"[ActionVoiceProbe] binding.metadata.type.missing: type={assetTypeName} wrapper={wrapperTypeName}");
                yield break;
            }

            // GLUE: The session asset resolver exposes full Quantum data while display names live on Unity wrappers; remove when one API owns both shapes.
            var assets = FindAllAssets(game, assetType);
            var findWrapper = CloseGameGenericMethod(game, "FindWrapper", wrapperType, 1);
            foreach (var asset in assets)
            {
                var guid = GetPropertyValue(asset, "Guid") ?? GetFieldValue(asset, "Guid");
                var path = ReadStringProperty(asset, "Path");
                var wrapper = FindWrapper(game, findWrapper, guid);
                var assetObjectName = wrapper != null ? GetUnityObjectName(wrapper) : "";
                if (string.IsNullOrWhiteSpace(assetObjectName))
                {
                    assetObjectName = Path.GetFileNameWithoutExtension(path);
                }

                var name = isGesture ? CleanGestureName(assetObjectName) : CleanAssetName(assetObjectName);
                var localizedName = GetPropertyValue(wrapper, localizedNameMember) ?? GetFieldValue(wrapper, localizedNameMember);
                var chineseName = ReadStringProperty(localizedName, "SimplifiedChinese");
                var localizationId = ReadStringProperty(localizedName, "Id");
                var assetGuid = guid?.ToString()?.Trim('[', ']') ?? "";
                var gesture = isGesture ? GetPropertyValue(asset, "Gesture") ?? GetFieldValue(asset, "Gesture") : null;
                var actionGuid = GetPropertyValue(gesture, "Id") ?? GetFieldValue(gesture, "Id");
                var actionId = actionGuid?.ToString()?.Trim('[', ']') ?? "";

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(chineseName))
                {
                    continue;
                }

                yield return new MapRow(type, chineseName, name, assetGuid, actionId, assetObjectName, localizationId);
            }
        }

        private static IReadOnlyList<object> FindAllAssets(object game, Type assetType)
        {
            var findAllAssets = CloseGameGenericMethod(game, "FindAllAssets", assetType, 0);
            var pooledAssets = findAllAssets?.Invoke(game, Array.Empty<object>());
            if (pooledAssets == null)
            {
                return Array.Empty<object>();
            }

            try
            {
                var count = Convert.ToInt32(GetPropertyValue(pooledAssets, "Count"));
                var item = pooledAssets.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (item == null)
                {
                    return Array.Empty<object>();
                }

                var assets = new List<object>(count);
                for (var index = 0; index < count; index++)
                {
                    if (item.GetValue(pooledAssets, new object[] { index }) is { } asset)
                    {
                        assets.Add(asset);
                    }
                }

                return assets;
            }
            finally
            {
                pooledAssets.GetType()
                    .GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)?
                    .Invoke(pooledAssets, Array.Empty<object>());
            }
        }

        private static object? FindWrapper(object game, MethodInfo? findWrapper, object? guid)
        {
            if (findWrapper == null || guid == null)
            {
                return null;
            }

            try
            {
                return findWrapper.Invoke(game, new[] { guid });
            }
            catch
            {
                return null;
            }
        }

        private static MethodInfo? CloseGameGenericMethod(object game, string methodName, Type genericType, int parameterCount)
            => game.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name.Equals(methodName, StringComparison.Ordinal)
                    && method.IsGenericMethodDefinition
                    && method.GetGenericArguments().Length == 1
                    && method.GetParameters().Length == parameterCount)
                ?.MakeGenericMethod(genericType);

        private static string Csv(string value)
        {
            if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        private readonly record struct MapRow(
            string Type,
            string ChineseName,
            string Name,
            string AssetGuid,
            string ActionId,
            string AssetObjectName,
            string LocalizationId);
    }

    private static class WwiseAudio
    {
        private static string _basePath = "";
        private static readonly List<string> _banks = new();
        private static readonly HashSet<string> _loadedBanks = new(StringComparer.OrdinalIgnoreCase);
        private static GameObject? _host;
        private static Type? _akSoundEngineType;
        private static string _gameBasePath = "";

        public static void Configure(string basePath, string gameBasePath)
        {
            _basePath = basePath;
            _banks.Clear();
            _loadedBanks.Clear();
            _gameBasePath = gameBasePath;
            LogInfo($"[ActionVoiceProbe] audio.wwise.config: basePath={_basePath} bankScan=*.bnk restoreBasePath={_gameBasePath}");
        }

        public static uint PostEvent(string key, string eventName, float pitchCents)
        {
            if (!EnsureBanksLoaded())
            {
                LogWarning($"[ActionVoiceProbe] audio.wwise.play.skip: key={key} event={eventName} reason=bank-not-loaded");
                return 0;
            }

            SetPitch(pitchCents, _host!);

            var method = FindMethod("PostEvent", typeof(string), typeof(GameObject));
            if (method == null)
            {
                LogWarning("[ActionVoiceProbe] audio.wwise.post.missing: signature=PostEvent(string, GameObject)");
                return 0;
            }

            var result = method.Invoke(null, new object?[] { eventName, _host });
            var playingId = result is uint id ? id : 0;
            if (playingId == 0)
            {
                LogWarning($"[ActionVoiceProbe] audio.wwise.post.failed: key={key} event={eventName} playingId=0");
                return 0;
            }

            SetPitch(pitchCents, playingId);
            LogInfo($"[ActionVoiceProbe] audio.wwise.post: key={key} event={eventName} playingId={playingId} pitchCents={pitchCents:0.#}");
            return playingId;
        }

        public static void StopPlayingId(uint playingId)
        {
            var method = FindMethod("StopPlayingID", typeof(uint));
            if (method == null)
            {
                LogWarning("[ActionVoiceProbe] audio.wwise.stop.missing: signature=StopPlayingID(uint)");
                return;
            }

            method.Invoke(null, new object?[] { playingId });
            LogInfo($"[ActionVoiceProbe] audio.wwise.stop: playingId={playingId}");
        }

        private static void SetPitch(float pitchCents, GameObject host)
        {
            var method = FindMethod("SetRTPCValue", typeof(string), typeof(float), typeof(GameObject));
            if (method == null)
            {
                LogWarning("[ActionVoiceProbe] audio.wwise.rtpc.missing: signature=SetRTPCValue(string, float, GameObject)");
                return;
            }

            var result = method.Invoke(null, new object?[] { PlaybackPitchRtpcName, pitchCents, host });
            if (!IsSuccess(result))
            {
                LogWarning($"[ActionVoiceProbe] audio.wwise.rtpc.failed: scope=gameObject value={pitchCents:0.#} result={FormatResult(result)}");
            }
        }

        private static void SetPitch(float pitchCents, uint playingId)
        {
            var method = FindMethod("SetRTPCValueByPlayingID", typeof(string), typeof(float), typeof(uint));
            if (method == null)
            {
                LogWarning("[ActionVoiceProbe] audio.wwise.rtpc.missing: signature=SetRTPCValueByPlayingID(string, float, uint)");
                return;
            }

            var result = method.Invoke(null, new object?[] { PlaybackPitchRtpcName, pitchCents, playingId });
            if (!IsSuccess(result))
            {
                LogWarning($"[ActionVoiceProbe] audio.wwise.rtpc.failed: scope=playingId playingId={playingId} value={pitchCents:0.#} result={FormatResult(result)}");
            }
        }

        private static bool EnsureBanksLoaded()
        {
            RefreshBanks();

            if (_banks.Count > 0 && _loadedBanks.Count == _banks.Count)
            {
                return true;
            }

            if (_banks.Count == 0)
            {
                LogWarning($"[ActionVoiceProbe] audio.wwise.bank.missing: no .bnk files in {_basePath}");
                return false;
            }

            if (!Directory.Exists(_basePath))
            {
                LogWarning($"[ActionVoiceProbe] audio.wwise.basePath.missing: path={_basePath}");
                return false;
            }

            if (ResolveAkSoundEngine() == null)
            {
                LogWarning("[ActionVoiceProbe] audio.wwise.api.missing: type=AkSoundEngine");
                return false;
            }

            EnsureHost();
            var allLoaded = true;
            foreach (var bank in _banks)
            {
                if (_loadedBanks.Contains(bank))
                {
                    continue;
                }

                if (!LoadBank(bank))
                {
                    allLoaded = false;
                }
            }

            return allLoaded;
        }

        private static void RefreshBanks()
        {
            _banks.Clear();
            if (!Directory.Exists(_basePath))
            {
                return;
            }

            foreach (var bank in Directory.GetFiles(_basePath, "*.bnk", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(bank => !string.IsNullOrWhiteSpace(bank))
                .Where(bank => !bank!.Equals("Init", StringComparison.OrdinalIgnoreCase))
                .OrderBy(bank => bank, StringComparer.OrdinalIgnoreCase))
            {
                _banks.Add(bank!);
            }

            _loadedBanks.RemoveWhere(bank => !_banks.Contains(bank, StringComparer.OrdinalIgnoreCase));
        }

        private static void EnsureHost()
        {
            if (_host != null)
            {
                return;
            }

            _host = new GameObject("NRFTW.ActionVoiceProbe.Wwise");
            UnityEngine.Object.DontDestroyOnLoad(_host);

            var register = FindMethod("RegisterGameObj", typeof(GameObject), typeof(string));
            if (register == null)
            {
                LogWarning("[ActionVoiceProbe] audio.wwise.register.missing: signature=RegisterGameObj(GameObject, string)");
                return;
            }

            var result = register.Invoke(null, new object?[] { _host, "NRFTW.ActionVoiceProbe.Wwise" });
            LogInfo($"[ActionVoiceProbe] audio.wwise.register: result={FormatResult(result)}");
        }

        private static bool SetBasePath(string path, string reason)
        {
            var method = FindMethod("SetBasePath", typeof(string));
            if (method == null)
            {
                LogWarning("[ActionVoiceProbe] audio.wwise.basePath.method.missing: signature=SetBasePath(string)");
                return false;
            }

            var result = method.Invoke(null, new object?[] { path });
            LogInfo($"[ActionVoiceProbe] audio.wwise.basePath.set: path={path} reason={reason} result={FormatResult(result)}");
            return IsSuccess(result);
        }

        private static bool LoadBank(string bank)
        {
            var method = FindLoadBankMethod();
            if (method == null)
            {
                LogWarning("[ActionVoiceProbe] audio.wwise.bank.method.missing: signature=LoadBank(string, out uint)");
                return false;
            }

            if (!SetBasePath(_basePath, "mod-bank-load"))
            {
                return false;
            }

            try
            {
                var args = new object?[] { bank, 0u };
                var result = method.Invoke(null, args);
                var bankId = args[1] is uint id ? id : 0;
                var loaded = IsSuccess(result) || IsResult(result, "AK_BankAlreadyLoaded");
                if (loaded)
                {
                    _loadedBanks.Add(bank);
                }

                LogInfo($"[ActionVoiceProbe] audio.wwise.bank.load: bank={bank} bankId={bankId} result={FormatResult(result)} loaded={loaded}");
                return loaded;
            }
            finally
            {
                RestoreGameBasePath();
            }
        }

        private static void RestoreGameBasePath()
        {
            if (_gameBasePath.Length == 0 || !Directory.Exists(_gameBasePath))
            {
                return;
            }

            SetBasePath(_gameBasePath, "restore-game");
        }

        private static Type? ResolveAkSoundEngine()
        {
            if (_akSoundEngineType != null)
            {
                return _akSoundEngineType;
            }

            _akSoundEngineType = ResolveType("AkSoundEngine");
            if (_akSoundEngineType != null)
            {
                return _akSoundEngineType;
            }

            try
            {
                Assembly.Load("AK.Wwise.Unity.API");
            }
            catch
            {
                return null;
            }

            _akSoundEngineType = ResolveType("AkSoundEngine");
            return _akSoundEngineType;
        }

        private static MethodInfo? FindMethod(string name, params Type[] parameterTypes)
        {
            var type = ResolveAkSoundEngine();
            return type?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                {
                    if (!method.Name.Equals(name, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length != parameterTypes.Length)
                    {
                        return false;
                    }

                    for (var i = 0; i < parameterTypes.Length; i++)
                    {
                        if (parameters[i].ParameterType != parameterTypes[i])
                        {
                            return false;
                        }
                    }

                    return true;
                });
        }

        private static MethodInfo? FindLoadBankMethod()
        {
            var type = ResolveAkSoundEngine();
            return type?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                {
                    if (!method.Name.Equals("LoadBank", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 2
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType.IsByRef
                        && parameters[1].ParameterType.GetElementType() == typeof(uint);
                });
        }

        private static bool IsSuccess(object? result)
        {
            return IsResult(result, "AK_Success");
        }

        private static bool IsResult(object? result, string name)
        {
            return result != null && result.ToString()?.Equals(name, StringComparison.Ordinal) == true;
        }

        private static string FormatResult(object? result)
        {
            return result?.ToString() ?? "<null>";
        }
    }

}
