using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Plugin.BetterAssemblyOutput.Pass;

public abstract class BasePass(MethodAnalysisContext context)
{
    public virtual void Start()
    {
        for (var i = 0; i < context.ControlFlowGraph!.Blocks.Count; i++)
        {
            context.ControlFlowGraph!.Blocks[i].Visited = false;
        }
        var entry = context.ControlFlowGraph!.EntryBlock;
        Analyze(entry);
    }

    protected void Analyze(Block block) // plz no stack overflow ><
    {
        if (block.Visited) return;
        block.Visited = true;
        
        AcceptBlock(block);
            
        for (var i = 0; i < block.Successors.Count; i++)
        {
            Analyze(block.Successors[i]);
        }
        // sometimes... they dont CONNECTED AAAAAAAAAAAAAAAARGH
        for (var i = 0; i < block.Predecessors.Count; i++)
        {
            Analyze(block.Predecessors[i]);
        }
    }
    
    protected abstract void AcceptBlock(Block block);
}