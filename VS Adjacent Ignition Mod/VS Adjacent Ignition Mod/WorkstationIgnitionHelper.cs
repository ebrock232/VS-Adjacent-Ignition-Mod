using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VS_Adjacent_Ignition_Mod;

internal static class WorkstationIgnitionHelper
{
    private static readonly HashSet<string> KnownWorkstationEntityClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Firepit",
        "Oven",
        "Bloomery",
        "PitKiln",
        "Forge",
        "Boiler"
    };

    internal static readonly Vec3i[] SpreadCandidateOffsets =
    {
        new(0, 1, 0), new(0, -1, 0),
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 0, 1), new(0, 0, -1),
        new(1, 0, 1), new(1, 0, -1),
        new(-1, 0, 1), new(-1, 0, -1)
    };

    public static bool CanSpreadHeat(Block? sourceBlock, Block? targetBlock, Vec3i offset)
    {
        if (sourceBlock == null || targetBlock == null)
        {
            return false;
        }

        if (!CanSpreadBetweenWorkstationTypes(sourceBlock, targetBlock))
        {
            return false;
        }

        if (IsPitKiln(sourceBlock) && IsPitKiln(targetBlock))
        {
            return IsHorizontalDiagonal(offset);
        }

        if (IsFaceAdjacent(offset))
        {
            return true;
        }

        return AdjacentIgnitionConfig.AllowDiagonalSpread && IsHorizontalDiagonal(offset);
    }

    public static bool CanSpreadHeat(Block? sourceBlock, Block? targetBlock, BlockPos targetPos, BlockPos sourcePos)
    {
        Vec3i offset = new(targetPos.X - sourcePos.X, targetPos.Y - sourcePos.Y, targetPos.Z - sourcePos.Z);
        return CanSpreadHeat(sourceBlock, targetBlock, offset);
    }

    public static bool CanSpreadBetweenWorkstationTypes(Block sourceBlock, Block targetBlock)
    {
        if (AdjacentIgnitionConfig.AllowMixedWorkstationIgnition)
        {
            return true;
        }

        return sourceBlock.EntityClass != null
            && targetBlock.EntityClass != null
            && sourceBlock.EntityClass.Equals(targetBlock.EntityClass, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPitKiln(Block block)
    {
        return block.EntityClass != null
            && block.EntityClass.Equals("PitKiln", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFaceAdjacent(Vec3i offset)
    {
        return Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z) == 1;
    }

    private static bool IsHorizontalDiagonal(Vec3i offset)
    {
        return offset.Y == 0 && Math.Abs(offset.X) == 1 && Math.Abs(offset.Z) == 1;
    }

    public static bool IsTorchHeatSource(Block? block)
    {
        if (block == null)
        {
            return false;
        }

        if (block is BlockTorchHolder holder)
        {
            return !holder.Empty;
        }

        if (block is BlockTorch torch)
        {
            return !torch.IsExtinct;
        }

        string? path = block.Code?.Path;
        if (path == null)
        {
            return false;
        }

        if (path.StartsWith("torchholder-", StringComparison.OrdinalIgnoreCase)
            && path.Contains("-filled-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith("torch-", StringComparison.OrdinalIgnoreCase)
            && path.Contains("-lit-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanTorchHeatWorkstation(Vec3i offset)
    {
        if (IsFaceAdjacent(offset))
        {
            return true;
        }

        return AdjacentIgnitionConfig.AllowDiagonalSpread && IsHorizontalDiagonal(offset);
    }

    public static bool IsChunkLoadedAt(IWorldAccessor world, BlockPos pos)
    {
        return world.BlockAccessor.GetChunkAtBlockPos(pos) != null;
    }

    public static bool HasAdjacentTorchHeatSource(IWorldAccessor world, BlockPos workstationPos)
    {
        if (!AdjacentIgnitionConfig.AllowTorchIgnition)
        {
            return false;
        }

        if (!IsChunkLoadedAt(world, workstationPos))
        {
            return false;
        }

        foreach (Vec3i offset in SpreadCandidateOffsets)
        {
            if (!CanTorchHeatWorkstation(offset))
            {
                continue;
            }

            BlockPos torchPos = workstationPos.AddCopy(offset.X, offset.Y, offset.Z);
            if (!IsChunkLoadedAt(world, torchPos))
            {
                continue;
            }

            Block block = world.BlockAccessor.GetBlock(torchPos);
            if (IsTorchHeatSource(block))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsKnownWorkstation(Block? block)
    {
        return block?.EntityClass != null && KnownWorkstationEntityClasses.Contains(block.EntityClass);
    }

    public static bool ShouldAttachSpreadBehavior(Block? block)
    {
        if (block?.Code == null || !IsKnownWorkstation(block))
        {
            return false;
        }

        return AdjacentIgnitionConfig.IsWorkstationEnabled(block);
    }

    public static bool IsTargetWorkstation(Block? block)
    {
        if (!ShouldAttachSpreadBehavior(block))
        {
            return false;
        }

        if (IsFirepitUnderConstruction(block!))
        {
            return false;
        }

        return true;
    }

    private static bool IsFirepitUnderConstruction(Block block)
    {
        return block is BlockFirepit firepit && firepit.Stage != 5;
    }

    public static void EnsureSpreadBehaviorAttached(ICoreAPI api, BlockEntity be)
    {
        if (be.GetBehavior<BEBehaviorAdjacentIgnitionSpread>() != null)
        {
            return;
        }

        if (!ShouldAttachSpreadBehavior(be.Block))
        {
            return;
        }

        BlockEntityBehavior? behavior = api.World.ClassRegistry.CreateBlockEntityBehavior(
            be,
            BEBehaviorAdjacentIgnitionSpread.BehaviorName);
        if (behavior == null)
        {
            return;
        }

        behavior.properties = new JsonObject(new JObject());
        be.Behaviors.Add(behavior);

        if (be.Api != null)
        {
            behavior.Initialize(be.Api, behavior.properties);
        }
    }

    public static void PatchWorkstationBlockBehaviors(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
        {
            return;
        }

        foreach (Block? block in api.World.Blocks)
        {
            if (block == null || !ShouldAttachSpreadBehavior(block) || BlockHasSpreadBehavior(block))
            {
                continue;
            }

            block.BlockEntityBehaviors = block.BlockEntityBehaviors.Append(new BlockEntityBehaviorType
            {
                Name = BEBehaviorAdjacentIgnitionSpread.BehaviorName
            });
        }
    }

    public static void MigrateLoadedWorkstationBehaviors(ICoreServerAPI api)
    {
        foreach (IWorldChunk chunk in api.WorldManager.AllLoadedChunks.Values)
        {
            if (chunk.BlockEntities == null)
            {
                continue;
            }

            foreach (BlockEntity be in chunk.BlockEntities.Values)
            {
                EnsureSpreadBehaviorAttached(api, be);
                be.GetBehavior<BEBehaviorAdjacentIgnitionSpread>()?.OnConfigReloaded();
            }
        }
    }

    private static bool BlockHasSpreadBehavior(Block block)
    {
        BlockEntityBehaviorType[] behaviors = block.BlockEntityBehaviors;
        for (int i = 0; i < behaviors.Length; i++)
        {
            if (behaviors[i].Name == BEBehaviorAdjacentIgnitionSpread.BehaviorName)
            {
                return true;
            }
        }

        return false;
    }

    public static BlockEntity? GetWorkstationBlockEntity(IWorldAccessor world, BlockPos pos)
    {
        BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);
        if (be == null || !IsKnownWorkstation(be.Block))
        {
            return null;
        }

        return be;
    }

    public static bool IsLit(IWorldAccessor world, BlockPos pos)
    {
        BlockEntity? be = GetWorkstationBlockEntity(world, pos);
        if (be == null || !IsTargetWorkstation(be.Block))
        {
            return false;
        }

        return be switch
        {
            BlockEntityFirepit firepit => firepit.IsBurning || firepit.IsSmoldering,
            BlockEntityOven oven => oven.IsBurning,
            BlockEntityBloomery bloomery => bloomery.IsBurning,
            BlockEntityPitKiln pitKiln => pitKiln.Lit,
            BlockEntityForge forge => forge.IsBurning,
            BlockEntityBoiler boiler => boiler.IsBurning || boiler.IsSmoldering,
            _ => false
        };
    }

    public static bool CanIgnite(IWorldAccessor world, BlockPos pos)
    {
        BlockEntity? be = GetWorkstationBlockEntity(world, pos);
        if (be == null || !IsTargetWorkstation(be.Block))
        {
            return false;
        }

        if (IsLit(world, be.Pos))
        {
            return false;
        }

        return CanIgniteBlockEntity(world, be);
    }

    public static bool CanIgniteFromTorchHeat(IWorldAccessor world, BlockPos pos)
    {
        BlockEntity? be = GetWorkstationBlockEntity(world, pos);
        if (be == null || !IsTargetWorkstation(be.Block))
        {
            return false;
        }

        if (IsLit(world, be.Pos))
        {
            return false;
        }

        if (be is BlockEntityPitKiln pitKiln)
        {
            return CanPitKilnIgniteFromTorchHeat(world, pitKiln);
        }

        return CanIgniteBlockEntity(world, be);
    }

    private static bool CanIgniteBlockEntity(IWorldAccessor world, BlockEntity be)
    {
        return be switch
        {
            BlockEntityFirepit firepit => firepit.GetIgnitableState(0) != EnumIgniteState.NotIgnitablePreventDefault,
            BlockEntityOven oven => oven.CanIgnite(),
            BlockEntityBloomery bloomery => bloomery.CanIgnite(),
            BlockEntityPitKiln pitKiln => pitKiln.CanIgnite,
            BlockEntityForge forge => forge.CanIgnite,
            BlockEntityBoiler boiler => CanBoilerIgnite(boiler),
            _ => false
        };
    }

    private static bool CanPitKilnIgniteFromTorchHeat(IWorldAccessor world, BlockEntityPitKiln pitKiln)
    {
        if (!pitKiln.IsComplete || pitKiln.Lit)
        {
            return false;
        }

        if (pitKiln.GetBehavior<BEBehaviorBurning>()?.IsBurning == true)
        {
            return false;
        }

        return IsValidPitKilnForTorchHeat(world, pitKiln.Pos);
    }

    private static bool IsValidPitKilnForTorchHeat(IWorldAccessor world, BlockPos pos)
    {
        if (!IsChunkLoadedAt(world, pos))
        {
            return false;
        }

        IBlockAccessor blockAccessor = world.BlockAccessor;
        Block pitKilnBlock = blockAccessor.GetBlock(pos);

        if (blockAccessor.GetBlock(pos, BlockLayersAccess.Fluid).BlockId != 0)
        {
            return false;
        }

        BlockPos upPos = pos.UpCopy();
        if (IsChunkLoadedAt(world, upPos) && IsTorchHeatSource(blockAccessor.GetBlock(upPos)))
        {
            return true;
        }

        BlockFacing[] horizontalFaces = BlockFacing.HORIZONTALS;
        for (int i = 0; i < horizontalFaces.Length; i++)
        {
            BlockFacing face = horizontalFaces[i];
            BlockPos neighborPos = pos.AddCopy(face);
            if (!IsChunkLoadedAt(world, neighborPos))
            {
                return false;
            }

            Block neighborBlock = blockAccessor.GetBlock(neighborPos);
            if (!neighborBlock.CanAttachBlockAt(blockAccessor, pitKilnBlock, neighborPos, face.Opposite))
            {
                return false;
            }

            if (neighborBlock.GetCombustibleProperties(world, null, neighborPos) != null)
            {
                return false;
            }
        }

        BlockPos downPos = pos.DownCopy();
        if (!IsChunkLoadedAt(world, downPos))
        {
            return false;
        }

        Block downBlock = blockAccessor.GetBlock(downPos);
        if (!downBlock.CanAttachBlockAt(blockAccessor, pitKilnBlock, downPos, BlockFacing.UP))
        {
            return false;
        }

        if (downBlock.GetCombustibleProperties(world, null, downPos) != null)
        {
            return false;
        }

        Block upBlock = blockAccessor.GetBlock(upPos);
        return upBlock.Replaceable >= 6000;
    }

    private static bool CanBoilerIgnite(BlockEntityBoiler boiler)
    {
        return boiler.CanIgnite() && boiler.fuelHours > 0f;
    }

    public static bool TryIgnite(IWorldAccessor world, BlockPos pos)
    {
        BlockEntity? be = GetWorkstationBlockEntity(world, pos);
        if (be == null || !CanIgnite(world, be.Pos))
        {
            return false;
        }

        return TryIgniteBlockEntity(world, be);
    }

    public static bool TryIgniteFromTorchHeat(IWorldAccessor world, BlockPos pos)
    {
        BlockEntity? be = GetWorkstationBlockEntity(world, pos);
        if (be == null || !CanIgniteFromTorchHeat(world, be.Pos))
        {
            return false;
        }

        return TryIgniteBlockEntity(world, be);
    }

    private static bool TryIgniteBlockEntity(IWorldAccessor world, BlockEntity be)
    {
        return be switch
        {
            BlockEntityFirepit firepit => TryIgniteFirepit(world, firepit),
            BlockEntityOven oven => oven.TryIgnite(),
            BlockEntityBloomery bloomery => bloomery.TryIgnite(),
            BlockEntityPitKiln pitKiln => TryIgnitePitKiln(pitKiln),
            BlockEntityForge forge => TryIgniteForge(forge),
            BlockEntityBoiler boiler => TryIgniteBoiler(boiler),
            _ => false
        };
    }

    private static bool TryIgniteFirepit(IWorldAccessor world, BlockEntityFirepit firepit)
    {
        if (firepit.fuelSlot.Empty)
        {
            return false;
        }

        firepit.canIgniteFuel = true;
        firepit.extinguishedTotalHours = world.Calendar.TotalHours;
        firepit.igniteFuel();
        return true;
    }

    private static bool TryIgnitePitKiln(BlockEntityPitKiln pitKiln)
    {
        pitKiln.TryIgnite(null);
        return true;
    }

    private static bool TryIgniteForge(BlockEntityForge forge)
    {
        forge.TryIgnite();
        return true;
    }

    private static bool TryIgniteBoiler(BlockEntityBoiler boiler)
    {
        if (!CanBoilerIgnite(boiler))
        {
            return false;
        }

        boiler.TryIgnite();
        return boiler.IsBurning;
    }
}
