using System.Reflection;
using System.Runtime.CompilerServices;
using FrontierMapBetterProspecting;
using FrontiersMap;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using GameOreReading = Vintagestory.GameContent.OreReading;

var bridge = typeof(CompatModSystem).Assembly.GetType("FrontierMapBetterProspecting.ProspectingResultBridge")!;
var createPacket = AccessTools.Method(bridge, "CreatePacket");
ProspectingResultPacket Packet(PropickReading reading) =>
    (ProspectingResultPacket)createPacket.Invoke(null, [reading, null])!;
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
    Console.WriteLine($"PASS {name}");
}
PropickReading Reading(string code = "cassiterite", double factor = 0.2, double density = 0.03) => new()
{
    Position = new Vec3d(123.25, 76.5, -456.75),
    OreReadings = new Dictionary<string, GameOreReading>
    {
        [code] = new() { DepositCode = code, TotalFactor = factor, PartsPerThousand = density }
    }
};

var reading = Reading();
var packet = Packet(reading);
Check(packet.WorldX == 123.25 && packet.WorldY == 76.5 && packet.WorldZ == -456.75, "exact reading position");
Check(packet.Densities.Single() == 0.03, "small density retained without rounding");
Check(packet.OreCodes.Single() == "Poor cassiterite", "BetterEr quality multiplier (vanilla would say Decent)");
foreach (var (factor, label) in new[] { (0.04, "Very poor"), (0.2, "Poor"), (0.4, "Decent"), (0.6, "High"), (0.8, "Very high"), (1.0, "Ultra high") })
    Check(Packet(Reading(factor: factor)).OreCodes.Single() == $"{label} cassiterite", $"quality tier {label}");
var labels = new[] { "Very poor", "Poor", "Decent", "High", "Very high", "Ultra high" };
for (var tier = 1; tier <= 5; tier++)
{
    var boundary = tier / BetterErProspecting.Config.Constants.LinearFactorValue;
    Check(Packet(Reading(factor: boundary - 1e-8)).OreCodes.Single() == $"{labels[tier - 1]} cassiterite", $"below quality boundary {tier}");
    Check(Packet(Reading(factor: boundary + 1e-8)).OreCodes.Single() == $"{labels[tier]} cassiterite", $"above quality boundary {tier}");
}
Check(Packet(Reading("rock-granite")).OreCodes.Count == 0, "stone readings excluded");
reading.OreReadings["rock-granite"] = new() { TotalFactor = 1, PartsPerThousand = 90 };
Check(Packet(reading).Densities.SequenceEqual([0.03]), "mixed reading keeps only ore densities");
Check(Packet(new PropickReading()).OreCodes.Count == 0, "empty density reading accepted");

var sent = new List<ProspectingResultPacket>();
var errors = new List<string>();
var logger = Stub.Create<ILogger>((method, args) =>
{
    if (method.Name == "Error") errors.Add(string.Join(" ", args ?? []));
    return null;
});
var channel = Stub.Create<IServerNetworkChannel>((method, args) =>
{
    if (method.Name == "SendPacket" && args?[0] is ProspectingResultPacket result) sent.Add(result);
    return null;
});
var network = Stub.Create<IServerNetworkAPI>((method, _) => method.Name == "GetChannel" ? channel : null);
var worldMap = new WorldMapManager();
var frontier = new FrontiersMapModSystem();
var loader = Stub.Create<IModLoader>((method, _) =>
{
    if (method.Name != "GetModSystem") return null;
    var type = method.GetGenericArguments().Single();
    return type == typeof(FrontiersMapModSystem) ? frontier : type == typeof(WorldMapManager) ? worldMap : null;
});
var api = Stub.Create<ICoreServerAPI>((method, _) => method.Name switch
{
    "get_Side" => EnumAppSide.Server,
    "get_Network" => network,
    "get_ModLoader" => loader,
    "get_Logger" => logger,
    _ => null
});
AccessTools.Field(typeof(FrontiersMapModSystem), "networkHandler").SetValue(frontier, new MapNetworkHandler(api));
var oreMap = new ModSystemOreMap();
AccessTools.Field(typeof(ModSystemOreMap), "api").SetValue(oreMap, api);
// IPlayer has an internal interface member that DispatchProxy cannot implement.
// The bridge only needs LanguageCode; avoid starting a game server for this player.
var player = (IServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(Vintagestory.Server.ServerPlayer));
var mod = new TestMod();
AccessTools.Property(typeof(Mod), nameof(Mod.Logger)).SetValue(mod, logger);
var compat = new CompatModSystem();
AccessTools.Property(typeof(ModSystem), nameof(ModSystem.Mod)).SetValue(compat, mod);
var original = AccessTools.Method(typeof(ItemProspectingPick), "PrintProbeResults");
var frontierPostfix = AccessTools.Method(typeof(ProspectingPickPatch), nameof(ProspectingPickPatch.Postfix));
var unrelatedPostfix = AccessTools.Method(typeof(OtherPatch), nameof(OtherPatch.Postfix));
var frontierHarmony = new Harmony("frontiersmap");
var otherHarmony = new Harmony("compatibilitychecks.other");
frontierHarmony.Patch(original, postfix: new HarmonyMethod(frontierPostfix));
otherHarmony.Patch(original, postfix: new HarmonyMethod(unrelatedPostfix));
try
{
    compat.StartServerSide(api);
    Check(errors.Count == 0, "server startup succeeds against real DLLs");
    Check(Harmony.GetPatchInfo(original)!.Postfixes.All(p => p.PatchMethod != frontierPostfix), "duplicate vanilla postfix removed");
    Check(Harmony.GetPatchInfo(original)!.Postfixes.Any(p => p.owner == "compatibilitychecks.other"), "unrelated patches preserved");
    oreMap.DidProbe(reading, player);
    Check(sent.Count == 1 && sent[0].Densities.SequenceEqual([0.03]), "DidProbe sends exactly one final reading through Frontier network");
    oreMap.DidProbe(Reading("rock-granite"), player);
    Check(sent.Count == 1, "stone mode sends no ore dialog packet");
    oreMap.DidProbe(new PropickReading(), player);
    Check(sent.Count == 2 && sent[1].OreCodes.Count == 0, "empty density result still forwarded");
    // The client ModSystem shares the process in single player. Its cleanup must not clear the server bridge.
    new CompatModSystem().Dispose();
    oreMap.DidProbe(Reading(), player);
    Check(sent.Count == 3, "client disposal leaves server forwarding active");
    compat.Dispose();
    Check(Harmony.GetPatchInfo(original)!.Postfixes.Any(p => p.owner == "frontiersmap" && p.PatchMethod == frontierPostfix), "Frontier postfix restored under original owner");
    oreMap.DidProbe(Reading(), player);
    Check(sent.Count == 3, "compatibility forwarding removed on disposal");
    compat.StartServerSide(api);
    oreMap.DidProbe(Reading(), player);
    Check(sent.Count == 4, "reload installs exactly one forwarding patch");
    Check(errors.Count == 0, "no forwarding or lifecycle errors");
}
finally
{
    compat.Dispose();
    frontierHarmony.UnpatchAll("frontiersmap");
    otherHarmony.UnpatchAll("compatibilitychecks.other");
}
Console.WriteLine($"{checks} compatibility checks passed.");

public class Stub : DispatchProxy
{
    private System.Func<MethodInfo, object?[]?, object?> handler = null!;
    public static T Create<T>(System.Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = Create<T, Stub>();
        ((Stub)(object)proxy).handler = handler;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var result = handler(targetMethod!, args);
        return result ?? (targetMethod!.ReturnType != typeof(void) && targetMethod.ReturnType.IsValueType
            ? Activator.CreateInstance(targetMethod.ReturnType)
            : null);
    }
}

public static class OtherPatch
{
    public static void Postfix() { }
}

public sealed class TestMod : Mod { }
