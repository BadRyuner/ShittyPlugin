using System.Globalization;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AssetRipper.CIL;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.Utils.AsmResolver;
using Cpp2IL.Plugin.BetterAssemblyOutput.ExtendedIsil;
using Cpp2IL.Plugin.BetterAssemblyOutput.Pass;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Plugin.BetterAssemblyOutput;

public class ShittyOutputFormat : AsmResolverDllOutputFormat
{
    internal static ShittyOutputFormat CurrentInstance = null!;
    
    private bool _isInitialized = false;

    internal TypeDefinition CompareResult = null!;
    internal MethodDefinition Compare = null!;
    internal MethodDefinition CompareEqual = null!;
    internal MethodDefinition CompareGreater = null!;
    internal MethodDefinition CompareLess = null!;
    internal MethodDefinition CompareSign = null!;
    internal StandAloneSignature GenericNativeInvokeSignature = null!;
    internal CorLibTypeFactory CorLibTypeFactory = null!;

    private MethodDefinition _il2CppCodegenInitializeRuntimeMetadata = null!;
    private MethodDefinition _il2CppRuntimeClassInit = null!;

    private readonly Lock _safety = new();

    private class IntrinsicImports
    {
        public ITypeDefOrRef CompareResult = null!;
        public IMethodDefOrRef Compare = null!;
        public IMethodDefOrRef CompareEqual = null!;
        public IMethodDefOrRef CompareGreater = null!;
        public IMethodDefOrRef CompareLess = null!;
        public IMethodDefOrRef CompareSign = null!;
        public StandAloneSignature GenericNativeInvokeSignature = null!;
        public IMethodDefOrRef Il2CppCodegenInitializeRuntimeMetadata = null!;
        public IMethodDefOrRef Il2CppRuntimeClassInit = null!;
    }

    private readonly Dictionary<ModuleDefinition, IntrinsicImports> _intrinsicImports = new(32);
    private IntrinsicImports originIntrinsic = null!;
    
    private void Init(ModuleDefinition module)
    {
        lock (_safety)
        {
            if (_intrinsicImports.ContainsKey(module))
                return;

            var ptr = new PointerTypeSignature(module.CorLibTypeFactory.Void);
            if (_isInitialized)
            {
                var importer = module.DefaultImporter;
                _intrinsicImports[module] = new IntrinsicImports()
                {
                    CompareResult = importer.ImportType(originIntrinsic.CompareResult),
                    CompareEqual = importer.ImportMethod(originIntrinsic.CompareEqual),
                    CompareGreater = importer.ImportMethod(originIntrinsic.CompareGreater),
                    CompareLess = importer.ImportMethod(originIntrinsic.CompareLess),
                    CompareSign = importer.ImportMethod(originIntrinsic.CompareSign),
                    GenericNativeInvokeSignature = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [ptr,ptr,ptr,ptr])),
                    Compare = importer.ImportMethod(originIntrinsic.Compare),
                    Il2CppCodegenInitializeRuntimeMetadata = importer.ImportMethod(originIntrinsic.Il2CppCodegenInitializeRuntimeMetadata),
                    Il2CppRuntimeClassInit = importer.ImportMethod(originIntrinsic.Il2CppRuntimeClassInit),
                };
                return;
            }
            
            // inject some intrinsics
            _isInitialized = true;
            
            CurrentInstance = this;

            CorLibTypeFactory = module.CorLibTypeFactory;

            CompareResult = new(Utf8String.Empty, "IsilCompareResult", TypeAttributes.Public)
            {
                BaseType = module.CorLibTypeFactory.Object.ToTypeDefOrRef()
            };
            
            var container = new TypeDefinition(Utf8String.Empty, "IsilIntrinsics", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract)
            {
                BaseType = module.CorLibTypeFactory.Object.ToTypeDefOrRef()
            };
            container.NestedTypes.Add(CompareResult);
            module.TopLevelTypes.Add(container);

            Compare = new("Compare", MethodAttributes.Public | MethodAttributes.Static, MethodSignature.CreateStatic(CompareResult.ToTypeSignature(), module.CorLibTypeFactory.Object, module.CorLibTypeFactory.Object));
            Compare.ThrowNullBody();
            container.Methods.Add(Compare);

            var compareResultSignature = MethodSignature.CreateInstance(module.CorLibTypeFactory.Boolean);
            CompareEqual = new("IsEqual", MethodAttributes.Public, compareResultSignature);
            CompareResult.Methods.Add(CompareEqual);
            CompareGreater = new("IsGreater", MethodAttributes.Public, compareResultSignature);
            CompareResult.Methods.Add(CompareGreater);
            CompareLess = new("IsLess", MethodAttributes.Public, compareResultSignature);
            CompareResult.Methods.Add(CompareLess);
            CompareSign = new("IsSign", MethodAttributes.Public, compareResultSignature);
            CompareResult.Methods.Add(CompareSign);
            var @void = module.CorLibTypeFactory.Void;
            
            GenericNativeInvokeSignature = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [ptr,ptr,ptr,ptr]));

            _il2CppCodegenInitializeRuntimeMetadata =
                new MethodDefinition("il2cpp_codegen_initialize_runtime_metadata", MethodAttributes.Public | MethodAttributes.Static, 
                    MethodSignature.CreateStatic(@void, ptr));
            container.Methods.Add(_il2CppCodegenInitializeRuntimeMetadata);
            
            _il2CppRuntimeClassInit =
                new MethodDefinition("il2cpp_runtime_class_init", MethodAttributes.Public | MethodAttributes.Static, 
                    MethodSignature.CreateStatic(@void, ptr));
            container.Methods.Add(_il2CppRuntimeClassInit);

            originIntrinsic = new()
            {
                CompareResult = CompareResult,
                CompareEqual = CompareEqual,
                CompareGreater = CompareGreater,
                CompareLess = CompareLess,
                CompareSign = CompareSign,
                GenericNativeInvokeSignature = GenericNativeInvokeSignature,
                Compare = Compare,
                Il2CppCodegenInitializeRuntimeMetadata = _il2CppCodegenInitializeRuntimeMetadata,
                Il2CppRuntimeClassInit = _il2CppRuntimeClassInit,
            };
            _intrinsicImports.Add(module, originIntrinsic);
        }
    }
    
    
    protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
        if (!_isInitialized || !_intrinsicImports.ContainsKey(methodDefinition.Module!))
            Init(methodDefinition.Module!);
        
        if (methodDefinition.IsManagedMethodWithBody())
        {
            methodDefinition.CilMethodBody = new(methodDefinition);
            
            methodContext.Analyze(); // ensure
            if (methodContext.ConvertedIsil == null || methodContext.ConvertedIsil.Count == 0)
            {
                // very bad method
                methodDefinition.ThrowNullBody();
                return;
            }

            var body = methodDefinition.CilMethodBody;
            body.VerifyLabelsOnBuild = false;
            body.ComputeMaxStackOnBuild = false;
            body.MaxStack = 8;
            
            var myIntrinsics = _intrinsicImports[methodDefinition.Module!];
            var importer = methodDefinition.Module!.DefaultImporter;
            
            try
            {
                var instructions = body.Instructions;
                var isilVarToCilVar = new Dictionary<IsilVariable, CilLocalVariable>();

                var labels = methodContext.ConvertedIsil
                    .Where(isil => isil.Operands is
                        [{ Type: InstructionSetIndependentOperand.OperandType.Instruction } _])
                    .Select(isil => (InstructionSetIndependentInstruction)isil.Operands[0].Data).Distinct().ToArray();
                var labelMap = new Dictionary<InstructionSetIndependentInstruction, CilInstructionLabel>(labels.Length);
                foreach (var label in labels)
                    labelMap.Add(label, new());
                
                new AnalyzeVariablesPass(methodContext, methodDefinition).Start();
                
                var compareResult = new CilLocalVariable(myIntrinsics.CompareResult.ToTypeSignature());
                body.LocalVariables.Add(compareResult);

                foreach (var block in methodContext.ControlFlowGraph!.Blocks.OrderBy(block =>
                             block.isilInstructions.FirstOrDefault()?.InstructionIndex))
                {
                    for (var index = 0; index < block.isilInstructions.Count; index++)
                    {
                        var instruction = block.isilInstructions[index];
                        var operands = instruction.Operands;

                        if (labelMap.TryGetValue(instruction, out var label))
                            label.Instruction = instructions.Add(CilOpCodes.Nop); // bad but ez

                        switch (instruction.OpCode.Mnemonic)
                        {
                            case IsilMnemonic.Nop:
                                instructions.Add(CilOpCodes.Nop);
                                break;

                            case IsilMnemonic.Move:
                                WriteToFirstOperand(() => { PushOperand(operands[1].Data); });
                                break;

                            case IsilMnemonic.LoadAddress:
                                PushOperand(operands[1].Data);
                                instructions.RemoveAt(instructions.Count - 1);
                                instructions.Add(CilOpCodes.Stloc, GetVar((IsilVariable)operands[0].Data));
                                break;

                            case IsilMnemonic.JumpIfEqual:
                                instructions.Add(CilOpCodes.Ldloca, compareResult);
                                instructions.Add(CilOpCodes.Call, myIntrinsics.CompareEqual);
                                instructions.Add(CilOpCodes.Brtrue,
                                    labelMap[(InstructionSetIndependentInstruction)operands[0].Data]);
                                break;

                            case IsilMnemonic.JumpIfNotEqual:
                                instructions.Add(CilOpCodes.Ldloca, compareResult);
                                instructions.Add(CilOpCodes.Call, myIntrinsics.CompareEqual);
                                instructions.Add(CilOpCodes.Brfalse,
                                    labelMap[(InstructionSetIndependentInstruction)operands[0].Data]);
                                break;

                            case IsilMnemonic.JumpIfGreater:
                                instructions.Add(CilOpCodes.Ldloca, compareResult);
                                instructions.Add(CilOpCodes.Call, myIntrinsics.CompareGreater);
                                instructions.Add(CilOpCodes.Brtrue,
                                    labelMap[(InstructionSetIndependentInstruction)operands[0].Data]);
                                break;

                            case IsilMnemonic.JumpIfLessOrEqual:
                                instructions.Add(CilOpCodes.Ldloca, compareResult);
                                instructions.Add(CilOpCodes.Call, myIntrinsics.CompareGreater);
                                instructions.Add(CilOpCodes.Brfalse,
                                    labelMap[(InstructionSetIndependentInstruction)operands[0].Data]);
                                break;

                            case IsilMnemonic.JumpIfLess:
                                instructions.Add(CilOpCodes.Ldloca, compareResult);
                                instructions.Add(CilOpCodes.Call, myIntrinsics.CompareLess);
                                instructions.Add(CilOpCodes.Brtrue,
                                    labelMap[(InstructionSetIndependentInstruction)operands[0].Data]);
                                break;

                            case IsilMnemonic.JumpIfGreaterOrEqual:
                                instructions.Add(CilOpCodes.Ldloca, compareResult);
                                instructions.Add(CilOpCodes.Call, myIntrinsics.CompareLess);
                                instructions.Add(CilOpCodes.Brfalse,
                                    labelMap[(InstructionSetIndependentInstruction)operands[0].Data]);
                                break;

                            case IsilMnemonic.JumpIfSign:
                                instructions.Add(CilOpCodes.Ldloca, compareResult);
                                instructions.Add(CilOpCodes.Call, myIntrinsics.CompareSign);
                                instructions.Add(CilOpCodes.Brtrue,
                                    labelMap[(InstructionSetIndependentInstruction)operands[0].Data]);
                                break;

                            case IsilMnemonic.JumpIfNotSign:
                                instructions.Add(CilOpCodes.Ldloca, compareResult);
                                instructions.Add(CilOpCodes.Call, myIntrinsics.CompareSign);
                                instructions.Add(CilOpCodes.Brfalse,
                                    labelMap[(InstructionSetIndependentInstruction)operands[0].Data]);
                                break;

                            case IsilMnemonic.Goto:
                                instructions.Add(CilOpCodes.Br,
                                    labelMap[(InstructionSetIndependentInstruction)operands[0].Data]);
                                break;

                            case IsilMnemonic.Add:
                            case IsilMnemonic.Subtract:
                            case IsilMnemonic.Divide:
                            case IsilMnemonic.Multiply:
                            case IsilMnemonic.Or:
                            case IsilMnemonic.And:
                            case IsilMnemonic.Xor:
                                WriteToFirstOperand(() =>
                                {
                                    PushOperand(operands[1].Data);
                                    PushOperand(operands[2].Data);
                                    instructions.Add(instruction.OpCode.Mnemonic switch
                                    {
                                        IsilMnemonic.Add => CilOpCodes.Add,
                                        IsilMnemonic.Subtract => CilOpCodes.Sub,
                                        IsilMnemonic.Multiply => CilOpCodes.Mul,
                                        IsilMnemonic.Divide => CilOpCodes.Div,
                                        IsilMnemonic.Or => CilOpCodes.Or,
                                        IsilMnemonic.And => CilOpCodes.And,
                                        IsilMnemonic.Xor => CilOpCodes.Xor,
                                        _ => throw new NotImplementedException(),
                                    });
                                });
                                break;

                            case IsilMnemonic.Neg:
                                WriteToFirstOperand(() =>
                                {
                                    PushOperand(operands[0].Data);
                                    instructions.Add(CilOpCodes.Neg);
                                });
                                break;
                            case IsilMnemonic.Not:
                            case IsilMnemonic.ShiftLeft:
                            case IsilMnemonic.ShiftRight:
                                WriteToFirstOperand(() =>
                                {
                                    PushOperand(operands[1].Data);
                                    instructions.Add(instruction.OpCode.Mnemonic switch
                                    {
                                        IsilMnemonic.Not => CilOpCodes.Not,
                                        IsilMnemonic.ShiftLeft => CilOpCodes.Shl,
                                        IsilMnemonic.ShiftRight => CilOpCodes.Shr,
                                        _ => throw new NotImplementedException(),
                                    });
                                });
                                break;

                            case IsilMnemonic.Call:
                                var method = instruction.Operands[0];
                                var resultVar = block.isilInstructions[++index];
                                bool returnValue = false;

                                if (method.Data is not IsilImmediateOperand { Value: string })
                                    foreach (var operand in operands.Skip(1))
                                        PushOperand(operand.Data);

                                switch (method.Data)
                                {
                                    // call managed function
                                    case IsilMethodOperand { Method: { } managedMethod }:
                                        instructions.Add(CilOpCodes.Call, (IMethodDescriptor)managedMethod.GetAsmResolverMethod().ImportWith(importer));
                                        returnValue = managedMethod.ReturnTypeContext.Type !=
                                                      Il2CppTypeEnum.IL2CPP_TYPE_VOID;
                                        break;
                                    // call il2cpp function
                                    case IsilImmediateOperand { Value: string il2CppExport }:
                                        switch (il2CppExport)
                                        {
                                            case "il2cpp_runtime_class_init_export":
                                            case "il2cpp_runtime_class_init_actual":
                                                instructions.Add(CilOpCodes.Ldloc,
                                                    GetVar((IsilVariable)instruction.Operands[1].Data));
                                                instructions.Add(CilOpCodes.Call, myIntrinsics.Il2CppRuntimeClassInit);
                                                returnValue = false;
                                                break;
                                            case "il2cpp_codegen_initialize_runtime_metadata":
                                                instructions.Add(CilOpCodes.Ldloc,
                                                    GetVar((IsilVariable)instruction.Operands[1].Data));
                                                instructions.Add(CilOpCodes.Call,
                                                    myIntrinsics.Il2CppCodegenInitializeRuntimeMetadata);
                                                returnValue = false;
                                                break;
                                            default:
                                                body.ThrowError($"Oops, u need implement: {il2CppExport}");
                                                returnValue = true;
                                                break;
                                        }

                                        break;
                                    // native call
                                    case IsilImmediateOperand { Value: not string } value:
                                        instructions.Add(CilOpCodes.Ldc_I8,
                                            (long)value.Value.ToUInt64(CultureInfo.InvariantCulture));
                                        instructions.Add(CilOpCodes.Calli, GenericNativeInvokeSignature);
                                        returnValue = true;
                                        break;
                                    // call reg
                                    case IsilVariable variable:
                                        instructions.Add(CilOpCodes.Ldloc, GetVar(variable));
                                        instructions.Add(CilOpCodes.Calli, GenericNativeInvokeSignature);
                                        returnValue = true;
                                        break;
                                    default:
                                        body.ThrowError(
                                            $"Unsupported call operand: {method} of type {method.GetType()}");
                                        return;
                                }

                                if (returnValue)
                                    instructions.Add(CilOpCodes.Stloc,
                                        GetVar((IsilVariable)resultVar.Operands[0].Data));
                                break;

                            case IsilMnemonic.Compare:
                                PushOperand(operands[0].Data);
                                PushOperand(operands[1].Data);
                                body.Instructions.Add(CilOpCodes.Call, myIntrinsics.Compare);
                                body.Instructions.Add(CilOpCodes.Stloc, compareResult);
                                break;

                            case IsilMnemonic.Return:
                                if (operands.Length != 0)
                                    instructions.Add(CilOpCodes.Stloc, GetVar((IsilVariable)operands[0].Data));
                                instructions.Add(CilOpCodes.Ret);
                                break;

                            case IsilMnemonic.ShiftStack:
                            case IsilMnemonic.Push:
                            case IsilMnemonic.Pop:
                                continue;

                            default:
                            {
                                body.ThrowError($"Unimplemented mnemonic: {instruction.OpCode.Mnemonic}");
                                return;
                            }
                        }

                        void WriteToFirstOperand(Action pushOperands)
                        {
                            if (operands[0].Data is IsilVariable moveVar)
                            {
                                pushOperands();
                                instructions.Add(CilOpCodes.Stloc, GetVar(moveVar));
                            }
                            else if (operands[0].Data is IsilVariableVector moveVector)
                            {
                                instructions.Add(CilOpCodes.Ldloca, GetVar(moveVector.Variable));
                                instructions.Add(CilOpCodes.Ldc_I4, moveVector.Index);
                                instructions.Add(CilOpCodes.Sizeof, moveVector.Width switch
                                {
                                    IsilVectorRegisterElementOperand.VectorElementWidth.B => methodDefinition.Module!.CorLibTypeFactory.Byte
                                        .ToTypeDefOrRef(),
                                    IsilVectorRegisterElementOperand.VectorElementWidth.S => methodDefinition.Module!.CorLibTypeFactory.Single
                                        .ToTypeDefOrRef(),
                                    IsilVectorRegisterElementOperand.VectorElementWidth.H => methodDefinition.Module!.CorLibTypeFactory.Byte
                                        .ToTypeDefOrRef(), // no half life 
                                    IsilVectorRegisterElementOperand.VectorElementWidth.D => methodDefinition.Module!.CorLibTypeFactory.Double
                                        .ToTypeDefOrRef(),

                                    _ => throw new NotImplementedException()
                                });
                                instructions.Add(CilOpCodes.Mul);
                                instructions.Add(CilOpCodes.Add);
                                pushOperands();
                                instructions.Add(moveVector.Width switch
                                {
                                    IsilVectorRegisterElementOperand.VectorElementWidth.B => CilOpCodes.Stind_I1,
                                    IsilVectorRegisterElementOperand.VectorElementWidth.S => CilOpCodes.Stind_R4,
                                    IsilVectorRegisterElementOperand.VectorElementWidth
                                        .H => CilOpCodes.Stind_I1, // no half life 
                                    IsilVectorRegisterElementOperand.VectorElementWidth.D => CilOpCodes.Stind_R8,
                                    _ => throw new NotImplementedException()
                                });
                            }
                            else if (operands[0].Data is IsilMemoryOperand setMemory)
                            {
                                if (setMemory.Base != null)
                                    PushOperand(setMemory.Base.Value.Data);
                                instructions.Add(CilOpCodes.Ldc_I8, setMemory.Addend);
                                if (setMemory.Base != null)
                                    instructions.Add(CilOpCodes.Add);
                                if (setMemory.Index != null)
                                {
                                    PushOperand(setMemory.Index.Value.Data);
                                    if (setMemory.Scale > 1)
                                    {
                                        instructions.Add(CilOpCodes.Ldc_I4, setMemory.Scale);
                                        instructions.Add(CilOpCodes.Mul);
                                    }
                                }

                                pushOperands();
                                instructions.Add(CilOpCodes.Stind_Ref);
                            }
                            else if (operands[0].Data is IsilStackOperand moveStack)
                            {
                                instructions.Add(CilOpCodes.Ldstr, moveStack.ToString());
                                pushOperands();
                                instructions.Add(CilOpCodes.Stind_Ref); // 100 IQ when u lazy
                            }
                            else throw new NotImplementedException($"Move to {operands[0].Data.GetType()}");
                        }
                    }
                }

                foreach (var cilInstructionLabel in labelMap.Where(kv => kv.Value.Instruction == null))
                {
                    cilInstructionLabel.Value.Instruction = instructions.Add(CilOpCodes.Nop);
                    body.ThrowError("Cant resolve label");
                }
                
                CilLocalVariable GetVar(IsilVariable variable)
                {
                    if (isilVarToCilVar.TryGetValue(variable, out var local))
                        return local;
                    local = new CilLocalVariable(variable.Type?.ToTypeSignature().ImportWith(importer) ?? methodDefinition.Module!.CorLibTypeFactory.Object);
                    body.LocalVariables.Add(local);
                    isilVarToCilVar.Add(variable, local);
                    if (variable.LinkParamId != int.MaxValue)
                    {
                        switch (variable.LinkParamId)
                        {
                            case 0:
                                instructions.Add(CilOpCodes.Ldarg_0);
                                instructions.Add(CilOpCodes.Stloc, local);
                                break;
                            case 1:
                                instructions.Add(CilOpCodes.Ldarg_1);
                                instructions.Add(CilOpCodes.Stloc, local);
                                break;
                            case 2:
                                instructions.Add(CilOpCodes.Ldarg_2);
                                instructions.Add(CilOpCodes.Stloc, local);
                                break;
                            case 3:
                                instructions.Add(CilOpCodes.Ldarg_3);
                                instructions.Add(CilOpCodes.Stloc, local);
                                break;
                        }
                    }
                    return local;
                }

                void PushOperand(IsilOperandData operandData)
                {
                    switch (operandData)
                    {
                        case IsilImmediateOperand immediate:
                            switch (immediate.Value)
                            {
                                case string stringValue:
                                    instructions.Add(CilOpCodes.Ldstr, stringValue);
                                    break;
                                default:
                                    instructions.Add(CilOpCodes.Ldc_I8,
                                        (long)immediate.Value.ToUInt64(CultureInfo.InvariantCulture));
                                    break;
                            }

                            break;
                        case IsilTypeMetadataUsageOperand metadata:
                            instructions.Add(CilOpCodes.Ldtoken,
                                metadata.TypeAnalysisContext.GetAsmResolverType().ToTypeDefOrRef().ImportWith(importer));
                            break;
                        case IsilStackOperand stack:
                            instructions.Add(CilOpCodes.Ldstr, stack.ToString());
                            break;
                        case IsilMemoryOperand memory:
                            int onStack = 0;
                            if (memory.Base != null)
                            {
                                PushOperand(memory.Base.Value.Data);
                                onStack = 1;
                            }

                            if (memory.Addend != 0)
                            {
                                instructions.Add(CilOpCodes.Ldc_I8, memory.Addend);
                                onStack++;
                            }

                            if (onStack == 2)
                            {
                                instructions.Add(CilOpCodes.Add);
                                onStack = 1;
                            }
                            if (memory.Index != null)
                            {
                                PushOperand(memory.Index.Value.Data);
                                onStack++; // 1..2
                                
                                if (memory.Scale > 1)
                                {
                                    instructions.Add(CilOpCodes.Ldc_I4, memory.Scale); // 2..3
                                    instructions.Add(CilOpCodes.Mul); // 1..2
                                }
                                
                                if (onStack == 2)
                                    instructions.Add(CilOpCodes.Add);
                            }

                            instructions.Add(CilOpCodes.Ldind_Ref);
                            break;
                        case IsilVariableVector vector:
                            instructions.Add(CilOpCodes.Ldloca, GetVar(vector.Variable));
                            instructions.Add(CilOpCodes.Ldc_I4, vector.Index);
                            instructions.Add(CilOpCodes.Sizeof, vector.Width switch
                            {
                                IsilVectorRegisterElementOperand.VectorElementWidth.B => methodDefinition.Module!.CorLibTypeFactory.Byte
                                    .ToTypeDefOrRef(),
                                IsilVectorRegisterElementOperand.VectorElementWidth.S => methodDefinition.Module!.CorLibTypeFactory.Single
                                    .ToTypeDefOrRef(),
                                // CorLibTypeFactory.Half.ToTypeDefOrRef(),
                                IsilVectorRegisterElementOperand.VectorElementWidth.H => methodDefinition.Module!.CorLibTypeFactory.Byte
                                    .ToTypeDefOrRef(), // no half life 
                                IsilVectorRegisterElementOperand.VectorElementWidth.D => methodDefinition.Module!.CorLibTypeFactory.Double
                                    .ToTypeDefOrRef(),

                                _ => throw new NotImplementedException()
                            });
                            instructions.Add(CilOpCodes.Mul);
                            instructions.Add(CilOpCodes.Add);
                            instructions.Add(vector.Width switch
                            {
                                IsilVectorRegisterElementOperand.VectorElementWidth.B => CilOpCodes.Ldind_U1,
                                IsilVectorRegisterElementOperand.VectorElementWidth.S => CilOpCodes.Ldind_R4,
                                IsilVectorRegisterElementOperand.VectorElementWidth
                                    .H => CilOpCodes.Ldind_U1, // no half life 
                                IsilVectorRegisterElementOperand.VectorElementWidth.D => CilOpCodes.Ldind_R8,
                                _ => throw new NotImplementedException()
                            });
                            break;
                        case IsilVariable variable:
                            instructions.Add(CilOpCodes.Ldloc, GetVar(variable));
                            break;
                        default:
                            throw new NotImplementedException($"{operandData} of type {operandData.GetType().FullName}");
                    }
                }
            }
            catch (Exception e)
            {
                body.ThrowError(e.ToString());
            }
        }
    }

    public override string OutputFormatId => "shitty_il";
    public override string OutputFormatName => "Better Assembly Output";
}