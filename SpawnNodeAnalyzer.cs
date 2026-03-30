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

    public AnalyzedSpawns branchSpawns = new();

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

                    retvalue.MultiplyChance(branch.info.chance);

                    frame.branchSpawns.MergeFrom(retvalue);
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

                    retvalue.AppendFrom(frame.branchSpawns);

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
}

public class AnalyzedSpawns
{
    public Dictionary<int, AnalyzedNPCSpawns> npcSpawns = new();

    public void AddSpawn(NextSpawn spawn, NodeRollInfo rollInfo)
    {
        if (!npcSpawns.TryGetValue(spawn.npcId, out var npcSpawn))
        {
            npcSpawn = new(spawn.npcId);
            npcSpawns.Add(spawn.npcId, npcSpawn);
        }

        AnalyzedSpawn analyzed = new()
        {
            chance = 1,

            leaked = spawn.leakedSpawn,
            affectedByLuck = rollInfo.dependsOnLuck,
            spawnOnPlayer = spawn.spawnOnPlayer
        };

        AnalyzedMultiPosSpawn posspawn = new(spawn.npcId);

        npcSpawn.spawns.Add(posspawn);
        posspawn.AddSpawn(spawn.pos, analyzed);
    }

    public void MultiplyChance(float mul)
    {
        foreach (var npcSpawn in npcSpawns.Values)
            npcSpawn.MultiplyChance(mul);
    }

    public void AppendFrom(AnalyzedSpawns aspawns)
    {
        foreach (var (id, spawns) in aspawns.npcSpawns)
        {
            if (!npcSpawns.TryGetValue(id, out var thisSpawns))
            {
                npcSpawns.Add(id, spawns);
                continue;
            }

            thisSpawns.AppendFrom(spawns);
        }
    }

    public void MergeFrom(AnalyzedSpawns aspawns)
    {
        foreach (var (id, spawns) in aspawns.npcSpawns)
        {
            if (!npcSpawns.TryGetValue(id, out var thisSpawns))
            {
                npcSpawns.Add(id, spawns);
                continue;
            }

            thisSpawns.MergeFrom(spawns);
        }
    }
}

public class AnalyzedNPCSpawns
{
    public int id;
    public List<AnalyzedMultiPosSpawn> spawns = new();

    public AnalyzedNPCSpawns(int id)
    {
        this.id = id;
    }

    public void MultiplyChance(float mul)
    {
        foreach (var spawn in spawns)
            spawn.MultiplyChance(mul);
    }

    public void AppendFrom(AnalyzedNPCSpawns spawns)
    {
        this.spawns.AddRange(spawns.spawns);
    }

    public void MergeFrom(AnalyzedNPCSpawns spawns)
    {
        HashSet<int> usedSpawnIndexes = new();

        List<AnalyzedMultiPosSpawn> unmatchedSpawns = new();

        AnalyzedMultiPosSpawn thisSpawn;

        foreach (var newSpawn in spawns.spawns)
        {
            int mostMatchedPos = -1;
            int mostMatchedPosMatchCount = -1;

            for (int i = 0; i < this.spawns.Count; i++)
            {
                if (usedSpawnIndexes.Contains(i))
                    continue;

                thisSpawn = this.spawns[i];

                int matchedPositions = 0;

                foreach (Point? pos in newSpawn.IterPositions())
                    if (thisSpawn.HasPosition(pos))
                        matchedPositions++;

                if (matchedPositions > 0 && matchedPositions > mostMatchedPosMatchCount)
                {
                    mostMatchedPos = i;
                    mostMatchedPosMatchCount = matchedPositions;
                }
            }

            if (mostMatchedPos < 0)
            {
                unmatchedSpawns.Add(newSpawn);
                continue;
            }

            usedSpawnIndexes.Add(mostMatchedPos);

            thisSpawn = this.spawns[mostMatchedPos];

            thisSpawn.MergeFrom(newSpawn);
        }

        foreach (var newSpawn in unmatchedSpawns)
        {
            bool merged = false;
            for (int i = 0; i < this.spawns.Count; i++)
            {
                if (usedSpawnIndexes.Contains(i))
                    continue;

                this.spawns[i].MergeFrom(newSpawn);
                usedSpawnIndexes.Add(i);
                merged = true;
                break;
            }

            if (merged)
                continue;

            usedSpawnIndexes.Add(this.spawns.Count);
            this.spawns.Add(newSpawn);
        }
    }

    public void ConvertToTilePos()
    {
        foreach (var spawn in spawns)
            spawn.ConvertToTilePos();
    }
}

public class AnalyzedMultiPosSpawn
{
    public int id;

    public Dictionary<Point, AnalyzedSpawn> posSpawns = new();

    public AnalyzedSpawn? noPosSpawn = null;

    public AnalyzedMultiPosSpawn(int id)
    {
        this.id = id;
    }

    public bool HasPosition(Point? pos)
    {
        if (pos is null)
        {
            return noPosSpawn is not null;
        }
        else
        {
            return posSpawns.ContainsKey(pos.Value);
        }
    }

    public AnalyzedSpawn? GetPosition(Point? pos)
    {
        if (pos is null)
        {
            return noPosSpawn;
        }
        else
        {
            if (posSpawns.TryGetValue(pos.Value, out var posSpawn))
            {
                return posSpawn;
            }
            return null;
        }
    }

    public IEnumerable<Point?> IterPositions()
    {
        foreach (Point pos in posSpawns.Keys)
            yield return pos;

        if (noPosSpawn is not null)
            yield return null;

        yield break;
    }

    public IEnumerable<AnalyzedSpawn> IterSpawns()
    {
        foreach (var spawn in posSpawns.Values)
            yield return spawn;

        if (noPosSpawn is not null)
            yield return noPosSpawn;

        yield break;
    }

    public IEnumerable<(Point?, AnalyzedSpawn)> Iter()
    {
        foreach (var (pos, spawn) in posSpawns)
            yield return (pos, spawn);

        if (noPosSpawn is not null)
            yield return (null, noPosSpawn);

        yield break;
    }

    public void AddSpawn(Point? pos, AnalyzedSpawn spawn)
    {
        if (pos is null)
        {
            if (noPosSpawn is not null)
                noPosSpawn.MergeFrom(spawn);
            else
                noPosSpawn = spawn;
        }
        else
        {
            if (posSpawns.TryGetValue(pos.Value, out var posSpawn))
            {
                posSpawn.MergeFrom(spawn);
            }
            else
            {
                posSpawns.Add(pos.Value, spawn);
            }
        }
    }

    public void MergeFrom(AnalyzedMultiPosSpawn spawns)
    {
        foreach (var (pos, spawn) in spawns.posSpawns)
        {
            if (posSpawns.TryGetValue(pos, out var posSpawn))
            {
                posSpawn.MergeFrom(spawn);
            }
            else
            {
                posSpawns.Add(pos, spawn);
            }
        }

        if (spawns.noPosSpawn is not null)
        {
            if (noPosSpawn is not null)
                noPosSpawn.MergeFrom(spawns.noPosSpawn);
            else
                noPosSpawn = spawns.noPosSpawn;
        }
    }

    public void MultiplyChance(float mul)
    {
        foreach (var spawn in posSpawns.Values)
            spawn.chance *= mul;

        if (noPosSpawn is not null)
            noPosSpawn.chance *= mul;
    }

    public void ConvertToTilePos()
    {
        var oldSpawns = posSpawns;
        posSpawns = new();

        foreach (var (pos, oldSpawn) in oldSpawns) {
            Point tilePos = new(pos.X / 16, pos.Y / 16);

            if (posSpawns.TryGetValue(tilePos, out var spawn)) {
                spawn.MergeFrom(oldSpawn);
            }
            else {
                posSpawns.Add(tilePos, oldSpawn);
            }
        }
    }

    public AnalyzedSpawn AllPositionsSpawn() {
        AnalyzedSpawn spawn = new();

        foreach (var s in IterSpawns()) {
            spawn.MergeFrom(s);
        }

        return spawn;
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