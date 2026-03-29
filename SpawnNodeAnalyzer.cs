using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using SpawnAnalyzer.Simulation;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.ID;

namespace SpawnAnalyzer;

class AnalyzerStackFrame
{
    public NodeConnection? incomingConnection;
    public SimulationNodeTimeline timeline;
    public int currentBranch = -1;
    public int nextBranch = 0;

    public int node;

    public List<(AnalyzedSpawns, float)> branches = new();

    public AnalyzerStackFrame(int node, SimulationNodeTimeline timeline, NodeConnection? incomingConnection)
    {
        this.node = node;
        this.timeline = timeline;
        this.incomingConnection = incomingConnection;
    }
}

public static class SpawnNodeAnalyzer
{
    public static AnalyzedSpawns Analyze(SimulationResult simres)
    {
        Stack<AnalyzerStackFrame> stack = new();

        stack.Push(new(simres.startNode, simres.nodes[simres.startNode]!.timelines[0], null));

        AnalyzedSpawns? retvalue = null;

        while (stack.Count > 0)
        {
            AnalyzerStackFrame frame = stack.Peek();

            if (stack.Count > simres.nodes.Count * 2)
            {
                Console.WriteLine("Stack overflow while analyzing");
                retvalue = new();
                stack.Pop();
                continue;
            }

            NodeConnection branch;

            while (true)
            {
                if (frame.currentBranch >= 0)
                {
                    if (retvalue is null)
                        throw new InvalidOperationException();

                    branch = frame.timeline.branches[frame.currentBranch];

                    frame.branches.Add((retvalue, branch.info.chance));
                    retvalue = null;
                }

                if (frame.nextBranch >= frame.timeline.branches.Length)
                {
                    retvalue = new();

                    if (frame.incomingConnection?.spawns is { } inspawns)
                    {
                        foreach (var spawn in inspawns)
                            retvalue.AddSpawn(spawn, frame.incomingConnection.rollInfo);
                    }

                    retvalue = AppendSpawns(retvalue, MergeSpawns(frame.branches));

                    stack.Pop();
                    break;
                }
                else
                {
                    branch = frame.timeline.branches[frame.nextBranch];
                    frame.currentBranch = frame.nextBranch;
                    frame.nextBranch++;

                    if (branch.nextNode is not null)
                    {
                        SimulationNodeTimeline timeline = simres.nodes[branch.nextNode.node]!.timelines[branch.nextNode.timeline];

                        stack.Push(new(branch.nextNode.node, timeline, branch));
                        break;
                    }
                    else
                    {
                        retvalue = new();
                        if (branch.spawns is not null)
                            foreach (var spawn in branch.spawns)
                                retvalue.AddSpawn(spawn, branch.rollInfo);

                        continue;
                    }
                }
            }
        }

        return retvalue ?? new();
    }

    static AnalyzedSpawns AppendSpawns(AnalyzedSpawns @base, AnalyzedSpawns append)
    {
        foreach (var spawn in append.spawns.Values)
        {
            if (!@base.spawns.TryGetValue(spawn.id, out var basespawn))
            {
                @base.spawns.Add(spawn.id, spawn);
                continue;
            }

            basespawn.AppendFrom(spawn);
        }

        return @base;
    }

    static AnalyzedSpawns MergeSpawns(List<(AnalyzedSpawns, float)> branches)
    {
        AnalyzedSpawns branchSpawns = new();
        foreach (var b in branches)
        {
            var (branch, chance) = b;

            foreach (AnalyzedMultiPosSpawn spawn in branch.spawns.Values)
            {
                spawn.MultiplyChance(chance);

                if (!branchSpawns.spawns.TryGetValue(spawn.id, out AnalyzedMultiPosSpawn? branchSpawn))
                {
                    branchSpawns.spawns.Add(spawn.id, spawn);
                    continue;
                }

                branchSpawn.MergeFrom(spawn);
            }
        }

        return branchSpawns;
    }
}

public class AnalyzedSpawns
{
    public Dictionary<int, AnalyzedMultiPosSpawn> spawns = new();

    //todo: fix multiple dragonflies
    public void AddSpawn(NextSpawn spawn, NodeRollInfo rollInfo)
    {
        if (!spawns.TryGetValue(spawn.npcId, out AnalyzedMultiPosSpawn? multiposspawn))
        {
            multiposspawn = new(spawn.npcId);
            spawns.Add(spawn.npcId, multiposspawn);
        }

        AnalyzedMultiSpawn? multispawn;

        if (spawn.pos is null)
        {
            multiposspawn.noPosSpawns ??= new(spawn.npcId);
            multispawn = multiposspawn.noPosSpawns;
        }
        else
        {
            Point pos = spawn.pos.Value;
            if (!multiposspawn.posSpawns.TryGetValue(pos, out multispawn))
            {
                multispawn = new(spawn.npcId);
                multiposspawn.posSpawns.Add(pos, multispawn);
            }
        }

        multispawn.spawns.Add(new()
        {
            chance = 1,
            leaked = spawn.leakedSpawn,
            affectedByLuck = rollInfo.dependsOnLuck,
            spawnOnPlayer = spawn.spawnOnPlayer,
        });
    }
}

public class AnalyzedMultiPosSpawn
{
    public int id;

    public Dictionary<Point, AnalyzedMultiSpawn> posSpawns = new();

    public AnalyzedMultiSpawn? noPosSpawns = null;

    public AnalyzedMultiPosSpawn(int id)
    {
        this.id = id;
    }

    public void AppendFrom(AnalyzedMultiPosSpawn spawn)
    {
        foreach (var kvp in spawn.posSpawns)
        {
            if (!posSpawns.TryGetValue(kvp.Key, out var thisSpawns))
            {
                posSpawns.Add(kvp.Key, kvp.Value);
                continue;
            }

            thisSpawns.AppendFrom(kvp.Value);
        }

        if (spawn.noPosSpawns is not null)
        {
            if (noPosSpawns is null)
            {
                noPosSpawns = spawn.noPosSpawns;
            }
            else
            {
                noPosSpawns.AppendFrom(spawn.noPosSpawns);
            }
        }
    }

    public void MergeFrom(AnalyzedMultiPosSpawn spawn)
    {
        foreach (var kvp in spawn.posSpawns)
        {
            if (!posSpawns.TryGetValue(kvp.Key, out var thisSpawns))
            {
                posSpawns.Add(kvp.Key, kvp.Value);
                continue;
            }

            thisSpawns.MergeFrom(kvp.Value, false);
        }

        if (spawn.noPosSpawns is not null)
        {
            if (noPosSpawns is null)
            {
                noPosSpawns = spawn.noPosSpawns;
            }
            else
            {
                noPosSpawns.MergeFrom(spawn.noPosSpawns, false);
            }
        }
    }

    public void MultiplyChance(float mul)
    {
        foreach (var spawnPos in posSpawns.Values)
            foreach (var spawn in spawnPos.spawns)
                spawn.chance *= mul;

        if (noPosSpawns is not null)
            foreach (var spawn in noPosSpawns.spawns)
                spawn.chance *= mul;
    }

    public void ConvertToTilePos()
    {
        var oldSpawns = posSpawns;
        posSpawns = new();

        foreach (var kvp in oldSpawns)
        {
            Point pos = new(kvp.Key.X / 16, kvp.Key.Y / 16);

            if (!posSpawns.TryGetValue(pos, out var thisSpawns))
            {
                posSpawns.Add(pos, kvp.Value);
                continue;
            }

            thisSpawns.MergeFrom(kvp.Value, false);
        }
    }

    public IEnumerable<(Point?, AnalyzedMultiSpawn)> IterAllSpawns() {
        var iter = posSpawns.Select(kvp => (new Point?(kvp.Key), kvp.Value));

        if (noPosSpawns is not null)
            iter = iter.Append((null, noPosSpawns));

        return iter;
    }
}

public class AnalyzedMultiSpawn
{
    public int id;
    public List<AnalyzedSpawn> spawns = new();

    public AnalyzedMultiSpawn(int id)
    {
        this.id = id;
    }

    public void AppendFrom(AnalyzedMultiSpawn spawn)
    {
        spawns.AddRange(spawn.spawns);
    }

    public void MergeFrom(AnalyzedMultiSpawn spawn, bool keepOriginal)
    {
        for (int i = 0; i < spawn.spawns.Count; i++)
        {
            if (spawns.Count <= i)
            {
                AnalyzedSpawn s = spawn.spawns[i];
                if (keepOriginal)
                    s = s.Clone();

                spawns.Add(s);
            }
            else
                spawns[i].MergeFrom(spawn.spawns[i]);
        }
    }

    public void MultiplyChance(float mul)
    {
        foreach (var spawn in spawns)
            spawn.chance *= mul;
    }
}

public class AnalyzedSpawn
{
    public float chance;

    public bool leaked;

    public bool affectedByLuck;

    public bool spawnOnPlayer;

    /// <summary>
    /// doesn't modify `spawn`
    /// </summary>
    public void MergeFrom(AnalyzedSpawn spawn)
    {
        chance += spawn.chance;
        leaked |= spawn.leaked;
        affectedByLuck |= spawn.affectedByLuck;
        spawnOnPlayer |= spawn.spawnOnPlayer;
    }

    public AnalyzedSpawn Clone()
    {
        return (AnalyzedSpawn)MemberwiseClone();
    }
}