using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VS_Adjacent_Ignition_Mod;

internal static class AdjacentIgnitionConfig
{
    internal static AdjacentIgnitionModConfig Current { get; private set; } = AdjacentIgnitionModConfig.CreateDefault();

    internal static bool IsLoaded { get; private set; }

    internal static void Load(ICoreAPI api)
    {
        LoadInternal(api, writeIfMissing: true);
    }

    internal static string Reload(ICoreServerAPI api)
    {
        string result = LoadInternal(api, writeIfMissing: false);
        WorkstationIgnitionHelper.PatchWorkstationBlockBehaviors(api);
        WorkstationIgnitionHelper.MigrateLoadedWorkstationBehaviors(api);
        return result;
    }

    private static string LoadInternal(ICoreAPI api, bool writeIfMissing)
    {
        if (api.Side != EnumAppSide.Server)
        {
            return string.Empty;
        }

        try
        {
            AdjacentIgnitionModConfig? loaded = api.LoadModConfig<AdjacentIgnitionModConfig>(AdjacentIgnitionModConfig.FileName);
            bool isNewFile = loaded == null;
            Current = loaded ?? AdjacentIgnitionModConfig.CreateDefault();
            Current.Normalize();

            if (writeIfMissing && isNewFile)
            {
                api.StoreModConfig(Current, AdjacentIgnitionModConfig.FileName);
            }

            IsLoaded = true;

            string modConfigDir = api.GetOrCreateDataPath("ModConfig");
            string path = $"{modConfigDir}\\{AdjacentIgnitionModConfig.FileName}";
            LogLoadedConfig(api, path);
            return path;
        }
        catch (Exception ex)
        {
            api.Logger.Error("[Adjacent Ignition Mod] Could not load config; using defaults.");
            api.Logger.Error(ex);
            Current = AdjacentIgnitionModConfig.CreateDefault();
            Current.Normalize();
            IsLoaded = true;
            return "Failed to load config. See server log for details.";
        }
    }

    private static void LogLoadedConfig(ICoreAPI api, string path)
    {
        WorkstationToggles workstations = Current.Workstations;
        api.Logger.Notification(
            "[Adjacent Ignition Mod] Config loaded from {0}. " +
            "Workstations [Firepit={1}, Oven={2}, PitKiln={3}, Bloomery={4}, Forge={5}, Boiler={6}]. " +
            "AllowMixedWorkstationIgnition={7}, AllowTorchIgnition={8}, AllowDiagonalSpread={9}, " +
            "MinIgnitionDelaySeconds={10}, MaxIgnitionDelaySeconds={11}, NeighborRescanIntervalSeconds={12}",
            path,
            workstations.Firepit,
            workstations.Oven,
            workstations.PitKiln,
            workstations.Bloomery,
            workstations.Forge,
            workstations.Boiler,
            Current.AllowMixedWorkstationIgnition,
            Current.AllowTorchIgnition,
            Current.AllowDiagonalSpread,
            Current.MinIgnitionDelaySeconds,
            Current.MaxIgnitionDelaySeconds,
            Current.NeighborRescanIntervalSeconds);
    }

    internal static bool IsWorkstationEnabled(Block? block)
    {
        if (block?.EntityClass == null)
        {
            return false;
        }

        return Current.Workstations.IsEnabled(block.EntityClass);
    }

    internal static bool AllowMixedWorkstationIgnition => Current.AllowMixedWorkstationIgnition;

    internal static bool AllowTorchIgnition => Current.AllowTorchIgnition;

    internal static bool AllowDiagonalSpread => Current.AllowDiagonalSpread;

    internal static float MinIgnitionDelaySeconds => Current.MinIgnitionDelaySeconds;

    internal static float MaxIgnitionDelaySeconds => Current.MaxIgnitionDelaySeconds;

    internal static float NeighborRescanIntervalSeconds => Current.NeighborRescanIntervalSeconds;
}
