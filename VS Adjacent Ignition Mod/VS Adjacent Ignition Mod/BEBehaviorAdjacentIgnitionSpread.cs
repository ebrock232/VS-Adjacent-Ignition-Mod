using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VS_Adjacent_Ignition_Mod;

public class BEBehaviorAdjacentIgnitionSpread : BlockEntityBehavior
{
    public const string BehaviorName = "AdjacentIgnitionSpread";

    public const float ActiveTickIntervalSeconds = 0.5f;
    public const float IdleTickIntervalSeconds = 1f;

    private sealed class WarmingState
    {
        public float Progress;
        public float RequiredSeconds;

        public WarmingState(float requiredSeconds)
        {
            RequiredSeconds = requiredSeconds;
        }

        public float Intensity => RequiredSeconds <= 0f ? 0f : GameMath.Clamp(Progress / RequiredSeconds, 0f, 1f);
    }

    private readonly Dictionary<BlockPos, WarmingState> warmingNeighbors = new();
    private long? tickListenerId;
    private bool wasLit;
    private bool isActivelySpreading;
    private float rescanTimer;

    public BEBehaviorAdjacentIgnitionSpread(BlockEntity blockEntity) : base(blockEntity)
    {
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        if (api.Side != EnumAppSide.Server)
        {
            return;
        }

        wasLit = WorkstationIgnitionHelper.IsLit(api.World, Pos);
        isActivelySpreading = wasLit;
        tickListenerId = Api.Event.RegisterGameTickListener(
            OnTick,
            (int)(GetTickIntervalMs(isActivelySpreading)));

        if (wasLit)
        {
            ScanForIgnitableNeighbors();
        }
    }

    public void NotifyLitStateChanged()
    {
        if (Api.Side != EnumAppSide.Server)
        {
            return;
        }

        bool isLit = WorkstationIgnitionHelper.IsLit(Api.World, Pos);
        if (isLit && !isActivelySpreading)
        {
            BeginActiveSpreading();
        }
    }

    public void OnConfigReloaded()
    {
        if (Api.Side != EnumAppSide.Server)
        {
            return;
        }

        warmingNeighbors.Clear();
        rescanTimer = 0f;
        wasLit = WorkstationIgnitionHelper.IsLit(Api.World, Pos);

        if (wasLit)
        {
            BeginActiveSpreading();
        }
        else
        {
            EndActiveSpreading();
            wasLit = false;
        }
    }

    public override void OnBlockRemoved()
    {
        StopTickListener();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        StopTickListener();
        base.OnBlockUnloaded();
    }

    private void OnTick(float dt)
    {
        if (Api.Side != EnumAppSide.Server)
        {
            return;
        }

        bool isLit = WorkstationIgnitionHelper.IsLit(Api.World, Pos);

        if (!isLit)
        {
            if (wasLit || isActivelySpreading)
            {
                EndActiveSpreading();
            }

            wasLit = false;
            return;
        }

        if (!wasLit)
        {
            wasLit = true;
            BeginActiveSpreading();
        }

        UpdateWarmingNeighbors(dt);

        rescanTimer += dt;
        if (rescanTimer >= AdjacentIgnitionConfig.NeighborRescanIntervalSeconds)
        {
            rescanTimer = 0f;
            ScanForIgnitableNeighbors();
        }
    }

    private void BeginActiveSpreading()
    {
        if (isActivelySpreading)
        {
            ScanForIgnitableNeighbors();
            return;
        }

        isActivelySpreading = true;
        rescanTimer = 0f;
        RestartTickListener();
        ScanForIgnitableNeighbors();
    }

    private void EndActiveSpreading()
    {
        isActivelySpreading = false;
        warmingNeighbors.Clear();
        rescanTimer = 0f;
        RestartTickListener();
    }

    private void RestartTickListener()
    {
        if (tickListenerId == null)
        {
            return;
        }

        Api.Event.UnregisterGameTickListener(tickListenerId.Value);
        tickListenerId = Api.Event.RegisterGameTickListener(
            OnTick,
            (int)(GetTickIntervalMs(isActivelySpreading)));
    }

    private static int GetTickIntervalMs(bool activelySpreading)
    {
        float interval = activelySpreading ? ActiveTickIntervalSeconds : IdleTickIntervalSeconds;
        return (int)(interval * 1000);
    }

    private void StopTickListener()
    {
        if (tickListenerId != null)
        {
            Api.Event.UnregisterGameTickListener(tickListenerId.Value);
            tickListenerId = null;
        }

        warmingNeighbors.Clear();
        isActivelySpreading = false;
        rescanTimer = 0f;
    }

    private float RollIgnitionDelaySeconds()
    {
        float minDelay = AdjacentIgnitionConfig.MinIgnitionDelaySeconds;
        float maxDelay = AdjacentIgnitionConfig.MaxIgnitionDelaySeconds;
        float range = maxDelay - minDelay;
        return minDelay + (float)Api.World.Rand.NextDouble() * range;
    }

    private void ScanForIgnitableNeighbors()
    {
        Block sourceBlock = Blockentity.Block;

        foreach (Vec3i offset in WorkstationIgnitionHelper.SpreadCandidateOffsets)
        {
            BlockPos neighborPos = Pos.AddCopy(offset.X, offset.Y, offset.Z);
            BlockEntity? neighborBe = WorkstationIgnitionHelper.GetWorkstationBlockEntity(Api.World, neighborPos);
            if (neighborBe == null)
            {
                warmingNeighbors.Remove(neighborPos);
                continue;
            }

            BlockPos spreadPos = neighborBe.Pos;
            Block neighborBlock = neighborBe.Block;

            if (!WorkstationIgnitionHelper.IsTargetWorkstation(neighborBlock))
            {
                continue;
            }

            if (!WorkstationIgnitionHelper.CanSpreadHeat(sourceBlock, neighborBlock, spreadPos, Pos))
            {
                warmingNeighbors.Remove(spreadPos);
                continue;
            }

            if (WorkstationIgnitionHelper.IsLit(Api.World, spreadPos))
            {
                warmingNeighbors.Remove(spreadPos);
                continue;
            }

            if (!WorkstationIgnitionHelper.CanIgnite(Api.World, spreadPos))
            {
                warmingNeighbors.Remove(spreadPos);
                continue;
            }

            if (!warmingNeighbors.ContainsKey(spreadPos))
            {
                warmingNeighbors[spreadPos.Copy()] = new WarmingState(RollIgnitionDelaySeconds());
            }
        }
    }

    private void UpdateWarmingNeighbors(float dt)
    {
        if (warmingNeighbors.Count == 0)
        {
            return;
        }

        List<BlockPos>? toRemove = null;
        Block sourceBlock = Blockentity.Block;

        foreach ((BlockPos neighborPos, WarmingState warmingState) in warmingNeighbors)
        {
            BlockEntity? neighborBe = WorkstationIgnitionHelper.GetWorkstationBlockEntity(Api.World, neighborPos);
            if (neighborBe == null)
            {
                (toRemove ??= new List<BlockPos>()).Add(neighborPos);
                continue;
            }

            BlockPos spreadPos = neighborBe.Pos;
            Block neighborBlock = neighborBe.Block;

            if (!WorkstationIgnitionHelper.CanSpreadHeat(sourceBlock, neighborBlock, spreadPos, Pos))
            {
                (toRemove ??= new List<BlockPos>()).Add(neighborPos);
                continue;
            }

            if (!WorkstationIgnitionHelper.IsTargetWorkstation(neighborBlock))
            {
                (toRemove ??= new List<BlockPos>()).Add(neighborPos);
                continue;
            }

            if (WorkstationIgnitionHelper.IsLit(Api.World, spreadPos))
            {
                (toRemove ??= new List<BlockPos>()).Add(neighborPos);
                continue;
            }

            if (!WorkstationIgnitionHelper.CanIgnite(Api.World, spreadPos))
            {
                (toRemove ??= new List<BlockPos>()).Add(neighborPos);
                continue;
            }

            warmingState.Progress += dt;
            AdjacentIgnitionParticles.SpawnWarming(Api.World, spreadPos, warmingState.Intensity);

            if (warmingState.Progress >= warmingState.RequiredSeconds)
            {
                if (WorkstationIgnitionHelper.TryIgnite(Api.World, spreadPos))
                {
                    neighborBe.GetBehavior<BEBehaviorAdjacentIgnitionSpread>()?.NotifyLitStateChanged();
                    (toRemove ??= new List<BlockPos>()).Add(neighborPos);
                }
                else
                {
                    warmingState.Progress = 0f;
                }

                continue;
            }
        }

        if (toRemove != null)
        {
            foreach (BlockPos neighborPos in toRemove)
            {
                warmingNeighbors.Remove(neighborPos);
            }
        }
    }
}
