using System.Reflection;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Collections;
using AsmResolver.PE.DotNet.Cil;
using AssetRipper.CIL;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Plugin.BetterAssemblyOutput;

public ref struct Transformer(MethodDefinition definition, MethodAnalysisContext context)
{
    Dictionary<Parameter, TypeAnalysisContext> ParamToContext = new(4);
    Dictionary<CilLocalVariable, TypeAnalysisContext> LocalToContext = new(16);
    Span<bool> AlreadyProcessed;
    
    public unsafe void Process()
    {
        AlreadyProcessed = stackalloc bool[definition.CilMethodBody!.Instructions.Count];
        
        for (var i = 0; i < definition.Parameters.Count; i++)
        {
            var pDef = definition.Parameters[i];
            var ctx = context.Parameters[i];
            ParamToContext.Add(pDef, ctx.ParameterTypeContext);
        }

        if (!definition.IsStatic)
        {
            ParamToContext[definition.Parameters.GetBySignatureIndex(0)] = context.DeclaringType!;
        }
            
        Trace(0);
    }

    void Trace(int at)
    {
        if (AlreadyProcessed[at])
            return;

        var variables = definition.CilMethodBody!.LocalVariables;
        var instructions = definition.CilMethodBody!.Instructions;
        
        for (; at < instructions.Count - 3; at++)
        {
            var instr = instructions[at];
            if (AlreadyProcessed[at])
                return;
            
            AlreadyProcessed[at] = true;

            if (instr.IsBranch())
            {
                var copy = LocalToContext.Clone();
                Trace(instructions.IndexOf(((CilInstructionLabel)instr.Operand!).Instruction!));
                LocalToContext = copy;
                continue;
            }
            
            var next = instructions[at + 1];
            
            if (instr.IsLdarg() && next.IsStloc())
            {
                LocalToContext[next.GetLocalVariable(variables)] = 
                    ParamToContext[instr.GetParameter(definition.Parameters)];
                
                AlreadyProcessed[at + 1] = true;
                at++;
                continue;
            }
            
            if (instr.IsStloc() && next.IsStloc())
            {
                LocalToContext[next.GetLocalVariable(variables)] =
                    LocalToContext[instr.GetLocalVariable(variables)];
                
                AlreadyProcessed[at + 1] = true;
                at++;
                continue;
            }

            var num = next;
            var add = instructions[at + 2];
            var set = instructions[at + 3];
            
            if (instr.OpCode.Code == CilCode.Ldloc && add.OpCode.Code == CilCode.Add && num.IsLdcI4OrI8(out var offset))
            {
                if (LocalToContext.TryGetValue(instr.GetLocalVariable(variables), out var ctx))
                {
                    var field = ResolveField(ctx, (int)offset);
                    if (field == null)
                        continue;
                    
                    if (set.OpCode.Code == CilCode.Stloc)
                        LocalToContext[set.GetLocalVariable(variables)] = field.FieldTypeContext;
                    
                    num.ReplaceWith(CilOpCodes.Ldflda, definition.Module!.DefaultImporter.ImportField(field.GetExtraData<FieldDefinition>("AsmResolverField")!));
                    add.ReplaceWithNop();

                    AlreadyProcessed[at + 1] = true;
                    AlreadyProcessed[at + 2] = true;
                    at += 2;
                }
            }
        }
    }

    static FieldAnalysisContext? ResolveField(TypeAnalysisContext? ctx, int offset)
    {
        if (ctx == null)
            return null;

        if (ctx.Type is not (Il2CppTypeEnum.IL2CPP_TYPE_CLASS or Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE))
            return null;
        
        for (var i = 0; i < ctx.Fields.Count; i++)
        {
            var field = ctx.Fields[i];

            if (field.Offset == offset)
                return field;
        }

        return null;
        //return ctx
        //    .Fields
        //    .Where(f => f.Offset <= offset && f.FieldTypeContext.IsValueType)
        //    .FirstOrDefault(ctx => ResolveField(ctx.FieldTypeContext, offset - ctx.Offset) != null);
    }
}