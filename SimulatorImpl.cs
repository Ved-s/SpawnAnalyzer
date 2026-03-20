using Microsoft.Xna.Framework;
using SpawnAnalyzer.Rewriters;
using SpawnAnalyzer.Rewriters.SpawnANnNPC;
using Terraria;

namespace SpawnAnalyzer;

public class SimulatorImpl
{
    public SimulatorImpl(
        GetSpawnTileParams getSpawnTileParamsImpl, 
        SetSpawnFlagsForChosenTile setSpawnFlagsForChosenTileImpl, 
        GetSpawnRate getSpawnRateImpl, 
        SpawnAnNPCRewriteData spawnAnNpcRewrite
    )
    {
        GetSpawnTileParamsImpl = getSpawnTileParamsImpl;
        SetSpawnFlagsForChosenTileImpl = setSpawnFlagsForChosenTileImpl;
        GetSpawnRateImpl = getSpawnRateImpl;
        SpawnAnNpcRewrite = spawnAnNpcRewrite;
    }

    public delegate bool GetSpawnTileParams(NPC.Spawner spawner, Player player, ref int x, ref int y, Rectangle spawnArea, Rectangle safeArea, out SpawnParamsStage1 spawnParams);
    public GetSpawnTileParams GetSpawnTileParamsImpl;

    public delegate void SetSpawnFlagsForChosenTile(NPC.Spawner spawner, int spawnTileX, int spawnTileY, int spawnTileType, int spawnWallType, SpawnerChances spawnParams);
    public SetSpawnFlagsForChosenTile SetSpawnFlagsForChosenTileImpl;

    public delegate void GetSpawnRate(NPC.Spawner spawner, Player player, out int spawnRate, out int maxSpawns, SpawnerChances spawnParams);
    public GetSpawnRate GetSpawnRateImpl;

    // TODO: offload to a different thread
    public SpawnAnNPCRewriteData SpawnAnNpcRewrite;

    public static SimulatorImpl GenerateImpl()
    {
        var getSpawnTileParamsImpl = GetSpawnTileParamsRewriter.GenerateMethod();
        var setSpawnFlagsForChosenTileImpl = SetSpawnFlagsForChosenTileRewriter.GenerateMethod();
        var getSpawnRateImpl = GetSpawnRateRewriter.GenerateMethod();
        var spawnAnNpcRewrite = SpawnAnNPCRewriter.RewriteMethod(null);

        return new(
            getSpawnTileParamsImpl,
            setSpawnFlagsForChosenTileImpl,
            getSpawnRateImpl,
            spawnAnNpcRewrite
        );
    }
}