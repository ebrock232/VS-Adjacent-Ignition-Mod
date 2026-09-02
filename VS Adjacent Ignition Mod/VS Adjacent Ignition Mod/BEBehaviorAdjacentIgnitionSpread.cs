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
    private bool isWarmingFromTorch;
    private float torchWarmingProgress;
    private float torchWarmingRequired;
    private float rescanTimer;
    private bool pendingInitialScan;

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
        pendingInitialScan = true;
        tickListenerId = Api.Event.RegisterGameTickListener(
            OnTick,
            GetTickIntervalMs(isActivelySpreading || isWarmingFromTorch));
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
            EndTorchWarming();
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
        EndTorchWarming();
        wasLit = WorkstationIgnitionHelper.IsLit(Api.World, Pos);
        pendingInitialScan = true;

        if (wasLit)
        {
            isActivelySpreading = true;
            rescanTimer = 0f;
            RestartTickListener();
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

        if (pendingInitialScan)
        {
            RunPendingInitialScan();
        }

        bool isLit = WorkstationIgnitionHelper.IsLit(Api.World, Pos);

        if (isLit)
        {
            if (isWarmingFromTorch)
            {
                EndTorchWarming();
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

            return;
        }

        if (wasLit || isActivelySpreading)
        {
            EndActiveSpreading();
        }

        wasLit = false;

        if (!AdjacentIgnitionConfig.AllowTorchIgnition)
        {
            if (isWarmingFromTorch)
            {
                EndTorchWarming();
            }

            return;
        }

        if (isWarmingFromTorch)
        {
            UpdateTorchWarming(dt);
            return;
        }

        rescanTimer += dt;
        if (rescanTimer >= AdjacentIgnitionConfig.NeighborRescanIntervalSeconds)
        {
            rescanTimer = 0f;
            TryBeginTorchWarming();
        }
    }

    private void RunPendingInitialScan()
    {
        pendingInitialScan = false;

        if (WorkstationIgnitionHelper.IsLit(Api.World, Pos))
        {
            ScanForIgnitableNeighbors();
            return;
        }

        if (AdjacentIgnitionConfig.AllowTorchIgnition)
        {
            TryBeginTorchWarming();
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
        if (!isActivelySpreading)
        {
            return;
        }

        isActivelySpreading = false;
        warmingNeighbors.Clear();
        rescanTimer = 0f;
        RestartTickListener();
    }

    private void TryBeginTorchWarming()
    {
        if (!WorkstationIgnitionHelper.CanIgniteFromTorchHeat(Api.World, Pos))
        {
            return;
        }

        if (!WorkstationIgnitionHelper.HasAdjacentTorchHeatSource(Api.World, Pos))
        {
            return;
        }

        isWarmingFromTorch = true;
        torchWarmingProgress = 0f;
        torchWarmingRequired = RollIgnitionDelaySeconds();
        RestartTickListener();
    }

    private void UpdateTorchWarming(float dt)
    {
        if (!WorkstationIgnitionHelper.HasAdjacentTorchHeatSource(Api.World, Pos))
        {
            EndTorchWarming();
            return;
        }

        torchWarmingProgress += dt;
        float intensity = torchWarmingRequired <= 0f
            ? 0f
            : GameMath.Clamp(torchWarmingProgress / torchWarmingRequired, 0f, 1f);
        AdjacentIgnitionParticles.SpawnWarming(Api.World, Pos, intensity);

        if (torchWarmingProgress < torchWarmingRequired)
        {
            return;
        }

        if (WorkstationIgnitionHelper.TryIgniteFromTorchHeat(Api.World, Pos))
        {
            NotifyLitStateChanged();
        }
        else
        {
            torchWarmingProgress = 0f;
        }
    }

    private void EndTorchWarming()
    {
        if (!isWarmingFromTorch)
        {
            return;
        }

        isWarmingFromTorch = false;
        torchWarmingProgress = 0f;
        torchWarmingRequired = 0f;
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
            GetTickIntervalMs(isActivelySpreading || isWarmingFromTorch));
    }

    private static int GetTickIntervalMs(bool useActiveInterval)
    {
        float interval = useActiveInterval ? ActiveTickIntervalSeconds : IdleTickIntervalSeconds;
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
        isWarmingFromTorch = false;
        torchWarmingProgress = 0f;
        torchWarmingRequired = 0f;
        rescanTimer = 0f;
        pendingInitialScan = false;
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
            if (!WorkstationIgnitionHelper.IsChunkLoadedAt(Api.World, neighborPos))
            {
                continue;
            }

            Block neighborBlock = Api.World.BlockAccessor.GetBlock(neighborPos);
            if (WorkstationIgnitionHelper.IsTorchHeatSource(neighborBlock))
            {
                warmingNeighbors.Remove(neighborPos);
                continue;
            }

            BlockEntity? neighborBe = WorkstationIgnitionHelper.GetWorkstationBlockEntity(Api.World, neighborPos);
            if (neighborBe == null)
            {
                warmingNeighbors.Remove(neighborPos);
                continue;
            }

            BlockPos spreadPos = neighborBe.Pos;
            Block workstationBlock = neighborBe.Block;

            if (!WorkstationIgnitionHelper.IsTargetWorkstation(workstationBlock))
            {
                continue;
            }

            if (!WorkstationIgnitionHelper.CanSpreadHeat(sourceBlock, workstationBlock, spreadPos, Pos))
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
