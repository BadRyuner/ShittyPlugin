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
    private const int MaxFieldChainSize = 8;
    
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

    FieldAnalysisContext? ResolveField(TypeAnalysisContext? ctx, int offset)
    {
        if (offset < 0)
            return null;
        
        if (ctx == null)
            return null;

        if (ctx.Type is not (Il2CppTypeEnum.IL2CPP_TYPE_CLASS or Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE))
            return null;

        // force get from base class
        if (ctx.BaseType != null && ctx.Fields.Count != 0 && ctx.Fields[0].Offset > offset)
        {
            return ResolveField(ctx.BaseType, offset);
        }

        if (ctx.Fields.Count == 0)
            return null;

        FieldAnalysisContext? field = null;
        
        // get fields from class
        for (var i = 0; i < ctx.Fields.Count; i++)
        {
            field = ctx.Fields[i];
            if (field.Offset == offset)
                return field;
        }

        // get from base class
        if (ctx.BaseType != null)
        {
            field = ResolveField(ctx.BaseType, offset);
            if (field != null)
                return field;
        }
        
        // maybe field from struct field
        // hello stack overflow my old friend
        // WHY YOU CANT EMIT TALI CALL LIKE A NORMAL COMPILER AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA WHYYYYYYYYYY
        //var structField = ctx.Fields.Where(f => f.Offset < offset).MaxBy(f => f.Offset)!;
        //return ResolveField(structField.FieldTypeContext, offset - structField.Offset);

        return null;
    }
}