using System;
using System.Collections.Generic;

namespace SpawnAnalyzer.Simulation.Test;

/*
          C - D
         /
    A - B - E - F

    timelineStateType: Cumulative data for what happened from the beginning of the simulation until this point
      example: All triggered chance variables and their values at point F

    blockResultsType:  Type that holds valuable results of execution of a specific block
      example: All spawns that happened between points C and D
*/

public delegate void SimulatedMethod(int? entryNode, Simulator sim);

public class Simulator
{
    readonly SimulatedMethod method;
    public readonly object staticSimData;

    readonly SimulationNodeInfo[] nodes;

    readonly DynamicStateType localsType;
    readonly DynamicStateType timelineStateType;
    readonly Func<object> blockResultsTypeConstructor;

    List<SimulationNode?> populatedNodes = [];

    object? localState;
    object? stackState;
    int currentTimeline;

    object? startBlockRunResult;
    object? startBlockTimelineState;

    NodeBranch? currentBranch = null;
    int? foundInitialNode = null;

    bool hasErrors = false;

    public Simulator(
        object staticSimData,
        SimulatedMethod method,
        SimulationNodeInfo[] nodes,
        DynamicStateType localsType,
        DynamicStateType timelineStateType,
        Func<object> blockResultsTypeConstructor)
    {
        this.staticSimData = staticSimData;
        this.method = method;
        this.nodes = nodes;
        this.localsType = localsType;
        this.timelineStateType = timelineStateType;
        this.blockResultsTypeConstructor = blockResultsTypeConstructor;
    }

    internal int NodeHit(int index, object param, out bool stop)
    {
        foundInitialNode ??= index;

        while (populatedNodes.Count <= index)
        {
            populatedNodes.Add(null);
        }

        object prevTimelineState = startBlockTimelineState!;

        if (currentBranch is not null)
        {
            currentTimeline = 0;

            prevTimelineState = populatedNodes[currentBranch.node]!.timelines[currentBranch.nodeTimeline].timelineState;

            if (populatedNodes[index] is not null)
            {
                var thisNode = populatedNodes[index]!;
                bool foundEq = false;
                for (int i = 0; i < thisNode.timelines.Count; i++)
                {
                    SimulationNodeTimeline thisNodeTimeline = thisNode.timelines[i];
                    if (localsType.Equals(localState!, thisNodeTimeline.localState) && timelineStateType.Equals(prevTimelineState, thisNodeTimeline.timelineState))
                    {
                        DynamicStateType? stackStateType = nodes[index].stackStateType;

                        if (stackStateType is null || stackStateType.Equals(stackState!, thisNodeTimeline.stackState!))
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
            currentBranch.nextNode = new() { node = index, timeline = currentTimeline };
            currentBranch.explored = true;
            currentBranch = null;
        }

        if ((populatedNodes[index]?.timelines.Count ?? 0) <= currentTimeline)
        {
            nodes[index].node.NodeHit(this, param, out BranchInfo[] branchInfos);
            var branches = new NodeBranch[branchInfos.Length];

            for (int b = 0; b < branches.Length; b++)
            {
                branches[b] = new(index, currentTimeline, b, branchInfos[b]);
            }

            // Console.WriteLine($"New node timeline {index}/{currentTimeline} with {branches.Length} branches");

            if (populatedNodes[index] is null)
            {
                populatedNodes[index] = new();
            }

            object? stackStateClone = null;
            DynamicStateType? stackStateType = nodes[index].stackStateType;
            if (stackStateType is not null)
            {
                stackStateClone = stackStateType.Clone(stackState!);
            }

            object newTimelineState = timelineStateType.Clone(prevTimelineState);

            var newTimeline = new SimulationNodeTimeline(branches, localsType.Clone(localState!), stackStateClone, newTimelineState);

            populatedNodes[index]!.timelines.Add(newTimeline);
        }
        stackState = null;

        var node = populatedNodes[index]!;

        var timeline = node.timelines[currentTimeline];

        for (int i = 0; i < timeline.branches.Length; i++)
        {
            NodeBranch branch = timeline.branches[i];
            if (branch.explored)
            {
                continue;
            }

            int retval = branch.info.returnValue;

            currentBranch = branch;

            branch.info.onBranchSelected?.Invoke(this);

            stop = false;
            return retval;
        }

        stop = true;
        return 0;
    }

    public object GetCurrentBlockRunResult()
    {
        if (currentBranch is null)
        {
            startBlockRunResult ??= blockResultsTypeConstructor();
            return startBlockRunResult;
        }

        currentBranch.blockResult ??= blockResultsTypeConstructor();
        return currentBranch.blockResult;
    }

    public object GetLastNodeStackStateClone()
    {
        object state = populatedNodes[currentBranch!.node]!.timelines[currentBranch!.nodeTimeline].stackState!;
        DynamicStateType type = nodes[currentBranch!.node].stackStateType!;

        return type.Clone(state);
    }

    public object GetCurrentTimelineState()
    {
        if (currentBranch is null)
            return startBlockTimelineState!;

        return populatedNodes[currentBranch.node]!.timelines[currentBranch.nodeTimeline].timelineState;
    }

    public SimulationResult Simulate()
    {
        foundInitialNode = null;
        localState = localsType.CreateNew();
        startBlockTimelineState = timelineStateType.CreateNew();
        currentTimeline = 0;

        int? entry = null;
        while (true)
        {
            // Console.WriteLine($"Start at entry {entry}");
            try
            {
                method(entry, this);
            }
            catch (Exception e)
            {
                if (currentBranch is null)
                {
                    Console.WriteLine($"Exception simulating from the beginning: {e}");
                }
                else
                {
                    int rv = currentBranch.info.returnValue;
                    int startNode = currentBranch.node;
                    int startNodeTimeline = currentBranch.nodeTimeline;
                    int offset = nodes[startNode].Offset;
                    Console.WriteLine($"Exception simulating after node {startNode}/{startNodeTimeline} at IL_{offset:x4} return value {rv}: {e}");

                    hasErrors = true;
                }
            }

            if (currentBranch is not null)
            {
                currentBranch.explored = true;
                currentBranch = null;
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
                        NodeBranch branch = timeline.branches[j];
                        if (!branch.explored)
                        {
                            localState = localsType.Clone(timeline.localState);
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

        SimulationResult res = BuildResult();

        populatedNodes = new();
        foundInitialNode = null;
        return res;
    }

    SimulationResult BuildResult()
    {

        if (foundInitialNode is null)
        {
            SimulationBlock block = new(startBlockRunResult, []);
            return new(hasErrors, [block]);
        }

        Dictionary<NodeBranch, int> branchBlockIds = new();
        List<NodeBranch> branches = new();

        for (int i = 0; i < nodes.Length; i++)
        {
            SimulationNode? node = populatedNodes[i];
            if (node is null) continue;

            for (int j = 0; j < node.timelines.Count; j++)
            {
                SimulationNodeTimeline timeline = node.timelines[j];
                for (int k = 0; k < timeline.branches.Length; k++)
                {
                    NodeBranch branch = timeline.branches[k];

                    if (branchBlockIds.ContainsKey(branch))
                        continue;

                    int id = branches.Count + 1;
                    branchBlockIds[branch] = id;
                    branches.Add(branch);
                }
            }
        }

        SimulationBlock[] blocks = new SimulationBlock[branches.Count + 1];

        for (int i = 0; i < blocks.Length; i++)
        {
            int node = 0;
            int timeline = 0;
            object? results;
            bool validNode = true;

            if (i == 0)
            {
                node = foundInitialNode.Value;
                timeline = 0;
                results = startBlockRunResult;
            }
            else
            {
                NodeBranch branch = branches[i - 1];
                results = branch.blockResult;
                if (branch.nextNode is null)
                {
                    validNode = false;
                }
                else
                {
                    node = branch.nextNode.node;
                    timeline = branch.nextNode.timeline;
                }
            }

            SimulationBlockBranch[] blockBranches;

            if (!validNode)
            {
                blockBranches = [];
            }
            else
            {
                SimulationNode simnode = populatedNodes[node]!;
                SimulationNodeTimeline simtimeline = simnode.timelines[timeline];
                blockBranches = new SimulationBlockBranch[simtimeline.branches.Length];

                for (int j = 0; j < blockBranches.Length; j++)
                {
                    NodeBranch branch = simtimeline.branches[j];
                    blockBranches[j] = new(branch.info.chance, branchBlockIds[branch]);
                }
            }

            blocks[i] = new(results, blockBranches);
        }

        return new(hasErrors, blocks);
    }
}

public struct SimulationResult
{
    public readonly bool hasErrors;
    public readonly SimulationBlock[] blocks;

    public SimulationResult(bool hasErrors, SimulationBlock[] blocks)
    {
        this.hasErrors = hasErrors;
        this.blocks = blocks;
    }
}

public struct SimulationBlock
{
    public readonly object? results;
    public readonly SimulationBlockBranch[] nextBranches;

    public SimulationBlock(object? results, SimulationBlockBranch[] nextBranches)
    {
        this.results = results;
        this.nextBranches = nextBranches;
    }
}

public struct SimulationBlockBranch
{
    float chance;
    int nextBlock;

    public SimulationBlockBranch(float chance, int nextBlock)
    {
        this.chance = chance;
        this.nextBlock = nextBlock;
    }

}

public class SimulationNodeInfo
{
    public int Offset;

    public DynamicStateType? stackStateType;

    public SimulationNodeImpl node;

    public SimulationNodeInfo(DynamicStateType? stackStateType, SimulationNodeImpl node)
    {
        this.stackStateType = stackStateType;
        this.node = node;
    }
}

public abstract class SimulationNodeImpl
{
    public abstract void NodeHit(Simulator context, object param, out BranchInfo[] branches);
}

public struct BranchInfo
{
    public float chance;
    public int returnValue;
    public Action<Simulator>? onBranchSelected;
}

class NodeBranch
{
    public int node;
    public int nodeTimeline;
    public int nodeBranch;

    public bool explored;

    public NextNode? nextNode;
    public object? blockResult;

    public BranchInfo info;

    public NodeBranch(int node, int timeline, int branch, BranchInfo info)
    {
        this.node = node;
        this.nodeTimeline = timeline;
        this.nodeBranch = branch;
        this.info = info;

    }
}

class NextNode
{
    public int node;

    public int timeline;
}

class SimulationNode
{
    public List<SimulationNodeTimeline> timelines = new();
}

class SimulationNodeTimeline
{
    /// <summary>Object holding the locals at the end of this timeline</summary>
    public object localState;

    /// <summary>Object holding the stack values that were present when a node was hit</summary>
    public object? stackState;

    /// <summary>Object holding overall simulation data up until this point in the simulation</summary>
    public object timelineState;
    public NodeBranch[] branches;

    public SimulationNodeTimeline(NodeBranch[] branches, object localState, object? stackState, object timelineState)
    {
        this.branches = branches;
        this.localState = localState;
        this.stackState = stackState;
        this.timelineState = timelineState;
    }
}