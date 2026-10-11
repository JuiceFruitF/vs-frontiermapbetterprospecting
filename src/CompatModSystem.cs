using System.Reflection;
using FrontiersMap;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FrontierMapBetterProspecting;

public sealed class CompatModSystem : ModSystem
{
    internal const string HarmonyId = "frontiermapbetterprospecting";

    private Harmony? harmony;
    private MethodInfo? frontierOriginal;
    private MethodInfo? frontierPostfix;
    private Patch? removedPostfix;

    public override double ExecuteOrder() => 1.0;

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        try
        {
            var frontiersMap = api.ModLoader.GetModSystem<FrontiersMapModSystem>();
            var networkHandler = frontiersMap?.GetNetworkHandler();
            if (networkHandler is null)
            {
                Mod.Logger.Error(
                    "[Frontier Map Better Prospecting] Frontier's Map network handler is unavailable; compatibility was not enabled."
                );
                return;
            }

            frontierOriginal = AccessTools.Method(
                typeof(ItemProspectingPick),
                "PrintProbeResults",
                [
                    typeof(IWorldAccessor),
                    typeof(IServerPlayer),
                    typeof(ItemSlot),
                    typeof(Vintagestory.API.MathTools.BlockPos)
                ]
            );
            frontierPostfix = AccessTools.Method(
                typeof(ProspectingPickPatch),
                nameof(ProspectingPickPatch.Postfix)
            );

            if (frontierOriginal is null || frontierPostfix is null)
            {
                Mod.Logger.Error(
                    "[Frontier Map Better Prospecting] Expected Frontier's Map prospecting patch signatures were not found; compatibility was not enabled."
                );
                return;
            }

            harmony = new Harmony(HarmonyId);
            ProspectingResultBridge.Configure(networkHandler, Mod.Logger);
            harmony.CreateClassProcessor(typeof(OreMapDidProbePatch)).Patch();
            RemoveFrontiersOriginalPostfix();

            Mod.Logger.Notification(
                "[Frontier Map Better Prospecting] Density-reading compatibility enabled."
            );
        }
        catch (Exception exception)
        {
            harmony?.UnpatchAll(HarmonyId);
            RestoreFrontiersOriginalPostfix();
            harmony = null;
            ProspectingResultBridge.Clear();
            Mod.Logger.Error(
                "[Frontier Map Better Prospecting] Failed to enable compatibility: {0}",
                exception
            );
        }
    }

    private void RemoveFrontiersOriginalPostfix()
    {
        var patchInfo = Harmony.GetPatchInfo(frontierOriginal!);
        var isInstalled = patchInfo?.Postfixes.Any(
            patch => patch.PatchMethod == frontierPostfix
        ) == true;

        if (!isInstalled)
        {
            Mod.Logger.Warning(
                "[Frontier Map Better Prospecting] Frontier's original prospecting postfix was not installed. The shared reading bridge will still be used."
            );
            return;
        }

        removedPostfix = patchInfo!.Postfixes.Single(patch => patch.PatchMethod == frontierPostfix);
        harmony!.Unpatch(frontierOriginal!, frontierPostfix!);
    }

    private void RestoreFrontiersOriginalPostfix()
    {
        if (removedPostfix is null || frontierOriginal is null || frontierPostfix is null)
        {
            return;
        }

        // Restore the patch under its original owner so Frontier's own cleanup still works.
        if (Harmony.GetPatchInfo(frontierOriginal)?.Postfixes.Any(
            patch => patch.PatchMethod == frontierPostfix
        ) != true)
        {
            new Harmony(removedPostfix.owner).Patch(
                frontierOriginal,
                postfix: new HarmonyMethod(frontierPostfix)
                {
                    priority = removedPostfix.priority,
                    before = removedPostfix.before,
                    after = removedPostfix.after
                }
            );
        }

        removedPostfix = null;
    }

    public override void Dispose()
    {
        if (harmony is not null)
        {
            harmony.UnpatchAll(HarmonyId);
            RestoreFrontiersOriginalPostfix();
            harmony = null;
            ProspectingResultBridge.Clear();
        }
        base.Dispose();
    }
}
