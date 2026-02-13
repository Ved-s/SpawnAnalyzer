
using System;
using System.Collections.Generic;
using Terraria;
using SpawnAnalyzer.Rewriters.SpawnANnNPC;

namespace SpawnAnalyzer.Simulation;

public class SpawnSimulationContext
{
    public NPC.Spawner spawner;
    public int spawnTileX;
    public int spawnTileY;
    public int spawnTileType;
    public bool xRange;

    List<SimRandomNode?> populatedNodes = [];
    readonly SpawnAnNPCRewriteData runData;

    object? localState;
    public object? stackState;
    int currentTimeline;

    NodeConnection? currentConnection = null;
    int? foundInitialNode = null;

    public SpawnSimulationContext(
        SpawnAnNPCRewriteData runData,
        NPC.Spawner spawner,
        int spawnTileX,
        int spawnTileY,
        int spawnTileType,
        bool xRange
    )
    {
        this.runData = runData;
        this.spawner = spawner;
        this.spawnTileX = spawnTileX;
        this.spawnTileY = spawnTileY;
        this.spawnTileType = spawnTileType;
        this.xRange = xRange;
    }

    internal int NodeHit(int index, object param, out bool stop)
    {
        foundInitialNode ??= index;

        // Console.WriteLine($"Hit node {index}");

        while (populatedNodes.Count <= index)
        {
            populatedNodes.Add(null);
        }

        if (currentConnection is not null)
        {
            currentTimeline = 0;
            if (populatedNodes[index] is not null)
            {
                var thisNode = populatedNodes[index]!;
                bool foundEq = false;
                for (int i = 0; i < thisNode.timelines.Count; i++)
                {
                    if (runData.LocalStateType.Equals(localState!, thisNode.timelines[i].localState))
                    {
                        StateType? stackStateType = runData.Nodes[index].stackStateType;

                        if (stackStateType is null || stackStateType.Equals(stackState!, thisNode.timelines[i].stackState!))
                        {
                            currentTimeline = i;
                            foundEq = true;
                            break;
                        }
                    }
                }
                if (!foundEq)
                {
                    currentTimeline = thisNode.timelines.Count;
                }
            }
            currentConnection.nextNode = new() { node = index, timeline = currentTimeline };
            currentConnection = null;
        }

        if ((populatedNodes[index]?.timelines.Count ?? 0) <= currentTimeline)
        {
            NodeRollParams rollParams = new();

            runData.Nodes[index].node.NodeHit(this, param, rollParams, out BranchInfo[] branchInfos);
            var branches = new NodeConnection[branchInfos.Length];

            for (int b = 0; b < branches.Length; b++)
            {
                branches[b] = new(branchInfos[b], index, currentTimeline, b);
            }

            // Console.WriteLine($"New node timeline {index}/{currentTimeline} with {branches.Length} branches");

            if (populatedNodes[index] is null)
            {
                populatedNodes[index] = new();
            }

            object? stackStateClone = null;
            StateType? stackStateType = runData.Nodes[index].stackStateType;
            if (stackStateType is not null)
            {
                stackStateClone = stackStateType.Clone(stackState!);
            }

            var newTimeline = new SimRandomNodeTimeline(branches, rollParams, runData.LocalStateType.Clone(localState!), stackStateClone);

            populatedNodes[index]!.timelines.Add(newTimeline);
        }
        stackState = null;

        var node = populatedNodes[index]!;

        var timeline = node.timelines[currentTimeline];

        // int nodeOffset = runData.Nodes[index].Offset;

        for (int i = 0; i < timeline.branches.Length; i++)
        {
            if (timeline.branches[i].ConnectionType != NodeConnectionType.NotExplored)
            {
                continue;
            }

            int retval = timeline.branches[i].info.returnValue;

            // Console.WriteLine($"Hit node {index} (IL_{nodeOffset:x4}), return {retval}");

            currentConnection = timeline.branches[i];

            stop = false;
            return retval;
        }

        stop = true;
        // Console.WriteLine($"Hit node {index} (IL_{nodeOffset:x4}), stop");
        return 0;
    }

    internal void ExitNodeHit(int x, int y, int type)
    {
        // Console.WriteLine($"Hit spawn {NPCID.Search.GetName(type)} [{type}] @ {x}, {y}");
        if (currentConnection is null)
        {
            throw new InvalidOperationException("Hit Spawn without hitting a random node first");
        }

        if (currentConnection.spawns is null)
            currentConnection.spawns = new();

        currentConnection.spawns.Add(new()
        {
            npcId = type,
            x = x,
            y = y,
        });
    }

    internal object GetLastNodeStackStateClone()
    {
        object state = populatedNodes[currentConnection!.startNode]!.timelines[currentConnection!.startNodeTimeline].stackState!;
        StateType type = runData.Nodes[currentConnection!.startNode].stackStateType!;

        return type.Clone(state);
    }

    public SimulationResult? Simulate()
    {
        foundInitialNode = null;
        localState = Activator.CreateInstance(runData.LocalStateType.Type);
        currentTimeline = 0;

        int? entry = null;
        while (true)
        {
            // Console.WriteLine($"Start at entry {entry}");
            runData.Method(entry, this);

            if (currentConnection is not null)
            {
                if (currentConnection.ConnectionType == NodeConnectionType.NotExplored) {
                    currentConnection.disconnected = true;
                }
                currentConnection = null;
            }

            int? nextNode = null;

            for (int i = 0; i < populatedNodes.Count; i++)
            {
                var node = populatedNodes[i];
                if (node is null)
                {
                    continue;
                }

                for (int ti = 0; ti < node.timelines.Count; ti++)
                {
                    var timeline = node.timelines[ti];
                    for (int j = 0; j < timeline.branches.Length; j++)
                    {
                        NodeConnection branch = timeline.branches[j];
                        if (branch.ConnectionType == NodeConnectionType.NotExplored)
                        {
                            // Console.WriteLine($"Start at node {i}, timeline {ti}");
                            localState = runData.LocalStateType.Clone(timeline.localState);
                            currentTimeline = ti;
                            nextNode = i;
                            break;
                        }
                    }
                }

                if (nextNode is not null)
                    break;
            }

            if (nextNode is null)
                break;

            entry = nextNode.Value;
        }

        if (foundInitialNode is null)
            return null;

        SimulationResult res = new()
        {
            nodes = populatedNodes,
            startNode = foundInitialNode.Value,
        };

        populatedNodes = new();
        foundInitialNode = null;
        return res;
    }
}

public struct SimulationResult
{
    public int startNode;

    public List<SimRandomNode?> nodes;
}

public class SimulationNodeInfo
{
    public int Offset;

    public StateType? stackStateType;

    public SimulationNode node;

    public SimulationNodeInfo(StateType? stackStateType, SimulationNode node)
    {
        this.stackStateType = stackStateType;
        this.node = node;
    }
}

public abstract class SimulationNode
{
    public abstract void NodeHit(SpawnSimulationContext context, object param, NodeRollParams rollParams, out BranchInfo[] branches);
}

public struct BranchInfo
{
    public float chance;
    public int returnValue;
}

public enum NodeConnectionType
{
    NotExplored,
    NoConnection,
    RandomNode,
    SpawnNode,
}

public class NodeConnection
{
    public int startNode;
    public int startNodeTimeline;
    public int startNodeBranch;

    public BranchInfo info;

    public NodeConnectionType ConnectionType
    {
        get
        {
            if (disconnected)
            {
                return NodeConnectionType.NoConnection;
            }
            else if (nextNode is not null)
            {
                return NodeConnectionType.RandomNode;
            }
            else if (spawns is not null)
            {
                return NodeConnectionType.SpawnNode;
            }
            return NodeConnectionType.NotExplored;
        }
    }

    public bool disconnected;
    public NextRandomNode? nextNode;
    public List<NextSpawnNode>? spawns;

    public NodeConnection(BranchInfo info, int node, int timeline, int branch)
    {
        this.info = info;
        startNode = node;
        startNodeTimeline = timeline;
        startNodeBranch = branch;
    }
}

public class NextRandomNode
{
    public int node;

    public int timeline;
}

public class NextSpawnNode
{
    public int npcId;
    public int x;
    public int y;
}

public class SimRandomNode
{
    public List<SimRandomNodeTimeline> timelines = new();
}

public class SimRandomNodeTimeline
{
    public object localState;
    public object? stackState;
    public NodeRollParams rollParams;
    public NodeConnection[] branches;

    public SimRandomNodeTimeline(NodeConnection[] branches, NodeRollParams rollParams, object localState, object? stackState)
    {
        this.branches = branches;
        this.rollParams = rollParams;
        this.localState = localState;
        this.stackState = stackState;
    }
}

public class NodeRollParams
{
    public bool dependsOnLuck;
}