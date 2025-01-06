using System.Runtime.CompilerServices;
using AsmResolver.DotNet;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils.AsmResolver;
using Cpp2IL.Plugin.BetterAssemblyOutput.ExtendedIsil;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Plugin.BetterAssemblyOutput.Pass;

public class AnalyzeVariablesPass : BasePass
{
    private static readonly string[] X64Args = ["rcx", "rdx", "r8", "r9"];
    
    private readonly MethodDefinition _method;

    readonly Dictionary<Block, Dictionary<string, IsilVariable>> _blockToVariableMap = new();
    readonly List<(Block, Dictionary<string, IsilVariable>)> _requiresLink = new();
    
    public AnalyzeVariablesPass(MethodAnalysisContext context, MethodDefinition method) : base(context)
    {
        _method = method;

        var entry = context.ControlFlowGraph!.EntryBlock;

        var maps = new Dictionary<string, IsilVariable>();
        _blockToVariableMap.Add(entry, maps);

        if (context.AppContext.InstructionSet is X86InstructionSet)
        {
            var parameters = context.Parameters;
            var current = 0;
            var max = 4;
            if (!context.IsStatic)
            {
                var thisVar = new IsilVariable();
                thisVar.Type = AsmResolverUtils.TypeDefsByIndex[context.DeclaringType!.Definition!.TypeIndex];
                thisVar.LinkParamId = 0;
                maps.Add("rcx", thisVar);
                current++;
            };
            for (var i = current; i < parameters.Count; i++)
            {
                if (current == max) // stack is too hard
                    break;
            
                var param = parameters[i];
                var var = new IsilVariable();
                var.Type = param.ParameterTypeContext.GetAsmResolverType().ToTypeDefOrRef();
                var.LinkParamId = current;
                maps.Add(X64Args[i], var);
                current++;
            }
        }
        else throw new NotSupportedException("Only x64 sorry");
    }

    public override void Start()
    {
        base.Start();
        for (var i = 0; i < _requiresLink.Count; i++)
        {
            var (entry, vars) = _requiresLink[i];
            for (var x = 0; x < entry.Predecessors.Count; x++)
            {
                var pre = entry.Predecessors[x];
                if (pre.Predecessors.Count == 0) continue; // DEAD BLOCK SHIT DEAD BLOCK GUYS WTF
                var preMap = _blockToVariableMap[pre];
                for (var z = 0; z < vars.Keys.Count; z++)
                {
                    var entryVarName = vars.Keys.ElementAt(z);
                    if (preMap.TryGetValue(entryVarName, out var preVar))
                    {
                        var entryVar = vars[entryVarName];
                        if (!entryVar.Equals(preVar))
                        {
                            pre.isilInstructions.Add(new InstructionSetIndependentInstruction(InstructionSetIndependentOpCode.Move, 0, IsilFlowControl.Continue, entryVar.ToOperand(), preVar.ToOperand()));
                        }
                    }
                }
            }
        }
    }
    
    protected override void AcceptBlock(Block block)
    {
        switch (block.Predecessors.Count)
        {
            case 0:
                break;
            case 1:
                var singleTarget = block.Predecessors.First();
                Analyze(singleTarget);
                _blockToVariableMap[block] = _blockToVariableMap[singleTarget].Clone();
                break;
            default:
            {
                var anyTarget = block.Predecessors.FirstOrDefault(aboba => _blockToVariableMap.ContainsKey(aboba));
                if (anyTarget == null)
                {
                    anyTarget = block.Predecessors.First();
                    Analyze(anyTarget);
                }
                var map = _blockToVariableMap[anyTarget];
                _blockToVariableMap[block] = map.Clone();
                _requiresLink.Add((block, map.Clone()));
                break;
            }
        }

        for (var i = 0; i < block.isilInstructions.Count; i++)
        {
            var instruction = block.isilInstructions[i];
            var operands = instruction.Operands;
            for (var x = 0; x < operands.Length; x++)
            {
                ref var operand = ref operands[x];
                switch (operand.Type)
                {
                    case InstructionSetIndependentOperand.OperandType.Register when operand.Data is IsilRegisterOperand:
                        ref var register = ref Unsafe.Unbox<IsilRegisterOperand>(operand.Data);
                        var name = SimplifyRegisterName(register.RegisterName);
                        Unsafe.AsRef(in operand.Data) = GetVariable(name, block, alloc: x == 0 && instruction.OpCode.Mnemonic 
                            is IsilMnemonic.Move 
                            or IsilMnemonic.Not
                            or IsilMnemonic.Neg
                            or IsilMnemonic.Add
                            or IsilMnemonic.Subtract
                            or IsilMnemonic.Multiply
                            or IsilMnemonic.Divide
                            or IsilMnemonic.Xor
                            or IsilMnemonic.Or
                            or IsilMnemonic.And
                            or IsilMnemonic.ShiftLeft
                            or IsilMnemonic.ShiftRight
                            );
                        break;
                    case InstructionSetIndependentOperand.OperandType.Register when operand.Data is IsilVectorRegisterElementOperand:
                        ref var vector = ref Unsafe.Unbox<IsilVectorRegisterElementOperand>(operand.Data);
                        var vecName = SimplifyRegisterName(vector.RegisterName);
                        Unsafe.AsRef(in operand.Data) = new IsilVariableVector(GetVariable(vecName, block, alloc: false), vector.Width, vector.Index);
                        break;
                    case InstructionSetIndependentOperand.OperandType.Memory or InstructionSetIndependentOperand.OperandType.MemoryOrStack
                        when operand.Data is IsilMemoryOperand memoryOperand:
                        var memBase = memoryOperand.Base;
                        if (memBase is { Data: IsilRegisterOperand })
                        {
                            ref var memBaseReg = ref Unsafe.Unbox<IsilRegisterOperand>(memBase.Value.Data);
                            var memBaseRegName = SimplifyRegisterName(memBaseReg.RegisterName);
                            Unsafe.AsRef(in operand.Data) = GetVariable(memBaseRegName, block, alloc: false);
                        }
                        break;
                }
            }

            if (instruction.OpCode.Mnemonic == IsilMnemonic.Call)
            {
                block.isilInstructions.Insert(++i, new InstructionSetIndependentInstruction(ExtraInstructions.UpdateRegister, 0, IsilFlowControl.Continue, GetVariable("rax", block).ToOperand()));
            }
        }
    }

    IsilVariable GetVariable(string name, Block block, bool alloc = false)
    {
        if (!_blockToVariableMap.TryGetValue(block, out var variableMap))
        {
            variableMap = new Dictionary<string, IsilVariable>();
            _blockToVariableMap.Add(block, variableMap);
        }

        if (alloc)
        {
            var newVariable = new IsilVariable();
            variableMap[name] = newVariable;
            return newVariable;
        }
        
        if (!variableMap.TryGetValue(name, out var variable))
        {
            variable = new IsilVariable();
            variableMap.Add(name, variable);
        }
        
        return variable;
    }
    
    static string SimplifyRegisterName(string reg) => reg switch
    {
        "al" or "ax" or "eax" or "rax" => "rax",
        "bl" or "bx" or "ebx" or "rbx" => "rbx",
        "cl" or "cx" or "ecx" or "rcx" => "rcx",
        "dl" or "dx" or "edx" or "rdx" => "rdx",
        "si" or "sil" or "esi" or "rsi" => "rsi",
        "di" or "dil" or "edi" or "rdi" => "rdi",
        "sp" or "spl" or "esp" or "rsp" => "rsp",
        "r8b" or "r8w" or "r8d" or "r8" => "r8",
        "r9b" or "r9w" or "r9d" or "r9" => "r9",
        "r10b" or "r10w" or "r10d" or "r10" => "r10",
        "r11b" or "r11w" or "r11d" or "r11" => "r11",
        "r12b" or "r12w" or "r12d" or "r12" => "r12",
        "r13b" or "r13w" or "r13d" or "r13" => "r13",
        "r14b" or "r14w" or "r14d" or "r14" => "r14",
        "r15b" or "r15w" or "r15d" or "r15" => "r15",
        _ => reg
    };
}