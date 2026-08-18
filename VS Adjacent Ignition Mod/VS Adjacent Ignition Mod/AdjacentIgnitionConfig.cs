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
        LoadInternal(api, writeDefaults: true);
    }

    internal static string Reload(ICoreServerAPI api)
    {
        string result = LoadInternal(api, writeDefaults: false);
        WorkstationIgnitionHelper.PatchWorkstationBlockBehaviors(api);
        WorkstationIgnitionHelper.MigrateLoadedWorkstationBehaviors(api);
        return result;
    }

    private static string LoadInternal(ICoreAPI api, bool writeDefaults)
    {
        if (api.Side != EnumAppSide.Server)
        {
            return string.Empty;
        }

        try
        {
            AdjacentIgnitionModConfig? loaded = api.LoadModConfig<AdjacentIgnitionModConfig>(AdjacentIgnitionModConfig.FileName);
            Current = loaded ?? AdjacentIgnitionModConfig.CreateDefault();
            Current.Normalize();

            if (writeDefaults)
            {
                api.StoreModConfig(Current, AdjacentIgnitionModConfig.FileName);
            }

            IsLoaded = true;

            string modConfigDir = api.GetOrCreateDataPath("ModConfig");
            string path = $"{modConfigDir}\\{AdjacentIgnitionModConfig.FileName}";
            api.Logger.Notification("[Adjacent Ignition Mod] Config loaded from {0}", path);
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

    internal static bool IsWorkstationEnabled(Block? block)
    {
        if (block?.EntityClass == null)
        {
            return false;
        }

        return Current.Workstations.IsEnabled(block.EntityClass);
    }

    internal static bool AllowMixedWorkstationIgnition => Current.AllowMixedWorkstationIgnition;

    internal static bool AllowDiagonalSpread => Current.AllowDiagonalSpread;

    internal static float MinIgnitionDelaySeconds => Current.MinIgnitionDelaySeconds;

    internal static float MaxIgnitionDelaySeconds => Current.MaxIgnitionDelaySeconds;

    internal static float NeighborRescanIntervalSeconds => Current.NeighborRescanIntervalSeconds;
}
