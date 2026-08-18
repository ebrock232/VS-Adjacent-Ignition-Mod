using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace VS_Adjacent_Ignition_Mod;

internal static class AdjacentIgnitionParticles
{
    private const string FireBlockCode = "fire";

    public static void SpawnWarming(IWorldAccessor world, BlockPos pos, float intensity)
    {
        if (intensity <= 0f)
        {
            return;
        }

        if (world.Rand.NextDouble() > 0.45f + intensity * 0.35f)
        {
            return;
        }

        AdvancedParticleProperties? props = GetSmolderParticleProperties(world);
        if (props == null)
        {
            return;
        }

        Random rand = world.Rand;
        double offsetX = rand.NextDouble() * 0.25 - 0.125;
        double offsetY = rand.NextDouble() * 0.15;
        double offsetZ = rand.NextDouble() * 0.25 - 0.125;

        props.basePos = pos.ToVec3d().Add(0.5 + offsetX, 0.55 + offsetY, 0.5 + offsetZ);
        props.Quantity.avg = 0.15f + intensity * 0.85f;

        world.SpawnParticles(props);

        props.Quantity.avg = 0f;
    }

    private static AdvancedParticleProperties? GetSmolderParticleProperties(IWorldAccessor world)
    {
        return ObjectCacheUtil.GetOrCreate(world.Api, "adjacentignition-smolder-particles", () =>
        {
            Block? fireBlock = world.GetBlock(new AssetLocation(FireBlockCode));
            if (fireBlock?.ParticleProperties == null || fireBlock.ParticleProperties.Length == 0)
            {
                return null;
            }

            return fireBlock.ParticleProperties[^1].Clone();
        });
    }
}
