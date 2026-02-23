using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using SpawnAnalyzer.Simulation;
using Terraria.GameContent.Bestiary;

namespace SpawnAnalyzer;

class AnalyzerStackFrame
{
    public NodeConnection? incomingConnection;
    public SimulationNodeTimeline timeline;
    public int currentBranch = -1;
    public int nextBranch = 0;

    public List<(AnalyzedSpawns, float)> branches = new();

    public AnalyzerStackFrame(SimulationNodeTimeline timeline, NodeConnection? incomingConnection)
    {
        this.timeline = timeline;
        this.incomingConnection = incomingConnection;
    }
}

public static class SpawnNodeAnalyzer
{
    public static AnalyzedSpawns Analyze(SimulationResult simres)
    {
        Stack<AnalyzerStackFrame> stack = new();

        stack.Push(new(simres.nodes[simres.startNode]!.timelines[0], null));

        AnalyzedSpawns? retvalue = null;

        while (stack.Count > 0)
        {
            AnalyzerStackFrame frame = stack.Peek();

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

                    MergeNodeBranches(frame.branches, retvalue);
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

                        stack.Push(new(timeline, branch));
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

    static void MergeNodeBranches(List<(AnalyzedSpawns, float)> branches, AnalyzedSpawns result)
    {
        foreach (var b in branches)
        {
            var (branch, chance) = b;

            foreach (AnalyzedMultiSpawn spawn in branch.spawns.Values)
            {
                spawn.MultiplyChance(chance);

                if (!result.spawns.TryGetValue(spawn.id, out AnalyzedMultiSpawn? resspawn))
                {
                    result.spawns.Add(spawn.id, spawn);
                    continue;
                }



                resspawn.MergeFrom(spawn);
            }
        }
    }
}

public class AnalyzedSpawns
{
    public Dictionary<int, AnalyzedMultiSpawn> spawns = new();

    public void AddSpawn(NextSpawn spawn, NodeRollInfo rollInfo)
    {
        if (!spawns.TryGetValue(spawn.npcId, out AnalyzedMultiSpawn? multispawn))
        {
            multispawn = new(spawn.npcId);
            spawns.Add(spawn.npcId, multispawn);
        }

        multispawn.spawns.Add(new()
        {
            chance = 1,
            pixelWorldPos = new(spawn.x, spawn.y),
            leaked = spawn.leakedSpawn,
            affectedByLuck = rollInfo.dependsOnLuck,
        });
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

    public void MergeFrom(AnalyzedMultiSpawn spawn)
    {
        HashSet<int> excludeThisIndices = new();
        foreach (var newSpawn in spawn.spawns)
        {
            bool found = false;
            for (int i = 0; i < spawns.Count; i++)
            {
                if (excludeThisIndices.Contains(i))
                    continue;

                AnalyzedSpawn? thisSpawn = spawns[i];
                if (thisSpawn.pixelWorldPos == newSpawn.pixelWorldPos)
                {
                    thisSpawn.MergeFrom(newSpawn);
                    found = true;
                    excludeThisIndices.Add(i);
                    break;
                }
            }

            if (found)
                continue;

            excludeThisIndices.Add(spawns.Count);
            spawns.Add(newSpawn);
        }
    }

    public void MultiplyChance(float chance)
    {
        foreach (AnalyzedSpawn spawn in spawns)
            spawn.chance *= chance;
    }
}

public class AnalyzedSpawn
{
    public float chance;

    public bool leaked;

    public bool affectedByLuck;

    public Point pixelWorldPos;

    public Point TileWorldPos => new(pixelWorldPos.X / 16, pixelWorldPos.Y / 16);

    public void MergeFrom(AnalyzedSpawn spawn)
    {
        chance += spawn.chance;
        leaked |= spawn.leaked;
        affectedByLuck |= spawn.affectedByLuck;
    }
}