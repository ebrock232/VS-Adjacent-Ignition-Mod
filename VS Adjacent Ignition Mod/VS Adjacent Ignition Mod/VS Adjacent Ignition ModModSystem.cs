using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace VS_Adjacent_Ignition_Mod;

public class VS_Adjacent_Ignition_ModModSystem : ModSystem
{
    private ICoreServerAPI? sapi;

    public override void Start(ICoreAPI api)
    {
        AdjacentIgnitionConfig.Load(api);

        api.RegisterBlockEntityBehaviorClass(
            BEBehaviorAdjacentIgnitionSpread.BehaviorName,
            typeof(BEBehaviorAdjacentIgnitionSpread));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        api.Event.ChunkDirty += OnChunkDirty;

        api.ChatCommands.Create("adjignition")
            .WithDescription("Adjacent Ignition Mod admin commands")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSub("reload")
                .WithDescription("Reload adjacentignition.json without restarting")
                .HandleWith(args =>
                {
                    string result = AdjacentIgnitionConfig.Reload(api);
                    return TextCommandResult.Success($"Adjacent Ignition config reloaded from {result}");
                })
            .EndSub();
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
        {
            return;
        }

        WorkstationIgnitionHelper.PatchWorkstationBlockBehaviors(api);
    }

    private void OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
    {
        if (sapi == null || reason != EnumChunkDirtyReason.NewlyLoaded || chunk.BlockEntities == null)
        {
            return;
        }

        foreach (BlockEntity be in chunk.BlockEntities.Values)
        {
            WorkstationIgnitionHelper.EnsureSpreadBehaviorAttached(sapi, be);
        }
    }
}
