using System;

namespace VS_Adjacent_Ignition_Mod;

public class AdjacentIgnitionModConfig
{
    public const string FileName = "adjacentignition.json";

    public WorkstationToggles Workstations { get; set; } = WorkstationToggles.CreateDefaults();

    public bool AllowMixedWorkstationIgnition { get; set; }

    public float MinIgnitionDelaySeconds { get; set; } = 5f;
    public float MaxIgnitionDelaySeconds { get; set; } = 8f;

    public float NeighborRescanIntervalSeconds { get; set; } = 5f;

    public bool AllowDiagonalSpread { get; set; } = true;

    public static AdjacentIgnitionModConfig CreateDefault()
    {
        return new AdjacentIgnitionModConfig();
    }

    public void Normalize()
    {
        Workstations ??= WorkstationToggles.CreateDefaults();

        MinIgnitionDelaySeconds = Math.Max(0.1f, MinIgnitionDelaySeconds);
        MaxIgnitionDelaySeconds = Math.Max(MinIgnitionDelaySeconds, MaxIgnitionDelaySeconds);
        NeighborRescanIntervalSeconds = Math.Max(1f, NeighborRescanIntervalSeconds);
    }
}

public class WorkstationToggles
{
    public bool Firepit { get; set; } = true;
    public bool Oven { get; set; } = true;
    public bool PitKiln { get; set; } = true;
    public bool Bloomery { get; set; } = true;
    public bool Forge { get; set; } = true;
    public bool Boiler { get; set; }

    public static WorkstationToggles CreateDefaults()
    {
        return new WorkstationToggles();
    }

    public bool IsEnabled(string? entityClass)
    {
        if (entityClass == null)
        {
            return false;
        }

        return entityClass.ToLowerInvariant() switch
        {
            "firepit" => Firepit,
            "oven" => Oven,
            "pitkiln" => PitKiln,
            "bloomery" => Bloomery,
            "forge" => Forge,
            "boiler" => Boiler,
            _ => false
        };
    }
}
