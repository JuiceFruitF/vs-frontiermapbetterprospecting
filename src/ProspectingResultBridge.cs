using FrontiersMap;
using BetterErProspecting.Config;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FrontierMapBetterProspecting;

internal static class ProspectingResultBridge
{
    private static MapNetworkHandler? networkHandler;
    private static ILogger? logger;

    internal static void Configure(MapNetworkHandler handler, ILogger modLogger)
    {
        networkHandler = handler;
        logger = modLogger;
    }

    internal static void Clear()
    {
        networkHandler = null;
        logger = null;
    }

    internal static void Send(PropickReading? reading, IServerPlayer? player)
    {
        if (networkHandler is null || reading?.Position is null || player is null)
        {
            return;
        }

        try
        {
            var packet = CreatePacket(reading, player.LanguageCode);
            // Stone mode also calls DidProbe. Do not open an ore-measurement dialog for it.
            if (reading.OreReadings?.Count > 0 && packet.OreCodes.Count == 0)
            {
                return;
            }

            networkHandler.SendProspectingResultToClient(player, packet);
        }
        catch (Exception exception)
        {
            logger?.Error(
                "[Frontier Map Better Prospecting] Failed to forward a prospecting reading for player {0}: {1}",
                player.PlayerName,
                exception
            );
        }
    }

    internal static ProspectingResultPacket CreatePacket(
        PropickReading reading,
        string? languageCode = null
    )
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(reading.Position);

        var packet = new ProspectingResultPacket
        {
            WorldX = reading.Position.X,
            WorldY = reading.Position.Y,
            WorldZ = reading.Position.Z
        };

        if (reading.OreReadings is null)
        {
            return packet;
        }

        foreach (var (oreCode, oreReading) in reading.OreReadings)
        {
            if (
                string.IsNullOrWhiteSpace(oreCode)
                || oreReading is null
                || oreCode.StartsWith("rock-", StringComparison.Ordinal)
            )
            {
                continue;
            }

            var quality = GetQualityLabel(oreReading.TotalFactor, languageCode);
            packet.OreCodes.Add($"{quality} {oreCode}");
            packet.Densities.Add(oreReading.PartsPerThousand);
        }

        return packet;
    }

    private static string GetQualityLabel(double totalFactor, string? languageCode)
    {
        var qualityIndex = (int)Math.Clamp(totalFactor * Constants.LinearFactorValue, 0, 5);
        var key = qualityIndex switch
        {
            0 => "propick-density-verypoor",
            1 => "propick-density-poor",
            2 => "propick-density-decent",
            3 => "propick-density-high",
            4 => "propick-density-veryhigh",
            _ => "propick-density-ultrahigh"
        };
        var fallback = qualityIndex switch
        {
            0 => "Very poor",
            1 => "Poor",
            2 => "Decent",
            3 => "High",
            4 => "Very high",
            _ => "Ultra high"
        };

        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return fallback;
        }

        try
        {
            var localized = Lang.GetL(languageCode, key, Array.Empty<object>());
            return string.IsNullOrEmpty(localized) || localized == key
                ? fallback
                : localized;
        }
        catch
        {
            return fallback;
        }
    }
}
