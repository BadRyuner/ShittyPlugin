using System.Globalization;
using System.Reflection.Emit;
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
using LibCpp2IL.BinaryStructures;
using Microsoft.VisualBasic;

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
    internal StandAloneSignature GenericNativeInvokeSignature0 = null!;
    internal StandAloneSignature GenericNativeInvokeSignature1 = null!;
    internal StandAloneSignature GenericNativeInvokeSignature2 = null!;
    internal StandAloneSignature GenericNativeInvokeSignature3 = null!;
    internal StandAloneSignature GenericNativeInvokeSignature4 = null!;
    internal CorLibTypeFactory CorLibTypeFactory = null!;

    private MethodDefinition _il2CppCodegenInitializeRuntimeMetadata = null!;
    private MethodDefinition _il2CppInternalCallResolve = null!;
    private MethodDefinition _il2CppThrow = null!;
    private MethodDefinition _il2CppObjectNew = null!;
    private MethodDefinition _il2CppNewArraySpecific = null!;
    private MethodDefinition _il2CppRuntimeClassInit = null!;

    private readonly Lock _safety = new();

    private class IntrinsicImports
    {
        public PointerTypeSignature Ptr;
        public ITypeDefOrRef CompareResult = null!;
        public IMethodDefOrRef Compare = null!;
        public IMethodDefOrRef CompareEqual = null!;
        public IMethodDefOrRef CompareGreater = null!;
        public IMethodDefOrRef CompareLess = null!;
        public IMethodDefOrRef CompareSign = null!;
        public StandAloneSignature GenericNativeInvokeSignature0 = null!;
        public StandAloneSignature GenericNativeInvokeSignature1 = null!;
        public StandAloneSignature GenericNativeInvokeSignature2 = null!;
        public StandAloneSignature GenericNativeInvokeSignature3 = null!;
        public StandAloneSignature GenericNativeInvokeSignature4 = null!;
        public Dictionary<int, StandAloneSignature> GenericNativeInvokeSignatureOther = new(16);
        public IMethodDefOrRef Il2CppCodegenInitializeRuntimeMetadata = null!;
        public IMethodDefOrRef Il2CppInternalCallResolve = null!;
        public IMethodDefOrRef Il2CppObjectNew = null!;
        public IMethodDefOrRef Il2CppThrow = null!;
        public IMethodDefOrRef Il2CppNewArraySpecific = null!;
        public IMethodDefOrRef Il2CppRuntimeClassInit = null!;

        public StandAloneSignature GetCalliSignature(int count)
        {
            if (GenericNativeInvokeSignatureOther.TryGetValue(count, out var result))
                return result;
            GenericNativeInvokeSignatureOther[count] = result = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, Ptr, Enumerable.Repeat<TypeSignature>(Ptr, count)));
            return result;
        }
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
                    Ptr = ptr,
                    CompareResult = importer.ImportType(originIntrinsic.CompareResult),
                    CompareEqual = importer.ImportMethod(originIntrinsic.CompareEqual),
                    CompareGreater = importer.ImportMethod(originIntrinsic.CompareGreater),
                    CompareLess = importer.ImportMethod(originIntrinsic.CompareLess),
                    CompareSign = importer.ImportMethod(originIntrinsic.CompareSign),
                    GenericNativeInvokeSignature0 = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [])),
                    GenericNativeInvokeSignature1 = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [ptr])),
                    GenericNativeInvokeSignature2 = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [ptr,ptr])),
                    GenericNativeInvokeSignature3 = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [ptr,ptr,ptr])),
                    GenericNativeInvokeSignature4 = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [ptr,ptr,ptr,ptr])),
                    Compare = importer.ImportMethod(originIntrinsic.Compare),
                    Il2CppCodegenInitializeRuntimeMetadata = importer.ImportMethod(originIntrinsic.Il2CppCodegenInitializeRuntimeMetadata),
                    Il2CppInternalCallResolve = importer.ImportMethod(originIntrinsic.Il2CppInternalCallResolve),
                    Il2CppThrow = importer.ImportMethod(originIntrinsic.Il2CppThrow),
                    Il2CppObjectNew = importer.ImportMethod(originIntrinsic.Il2CppObjectNew),
                    Il2CppNewArraySpecific = importer.ImportMethod(originIntrinsic.Il2CppNewArraySpecific),
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

            Compare = new("Compare", MethodAttributes.Public | MethodAttributes.Static, MethodSignature.CreateStatic(CompareResult.ToTypeSignature(), ptr, ptr));
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
            
            GenericNativeInvokeSignature0 = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, []));
            GenericNativeInvokeSignature1 = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [ptr]));
            GenericNativeInvokeSignature2 = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [ptr,ptr]));
            GenericNativeInvokeSignature3 = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [ptr,ptr,ptr]));
            GenericNativeInvokeSignature4 = new StandAloneSignature(new MethodSignature(CallingConventionAttributes.StdCall, ptr, [ptr,ptr,ptr,ptr]));

            _il2CppCodegenInitializeRuntimeMetadata =
                new MethodDefinition("il2cpp_codegen_initialize_runtime_metadata", MethodAttributes.Public | MethodAttributes.Static, 
                    MethodSignature.CreateStatic(@void, ptr));
            container.Methods.Add(_il2CppCodegenInitializeRuntimeMetadata);
            
            _il2CppInternalCallResolve =
                new MethodDefinition("il2cpp_InternalCallResolve", MethodAttributes.Public | MethodAttributes.Static, 
                    MethodSignature.CreateStatic(ptr, ptr));
            container.Methods.Add(_il2CppInternalCallResolve);
            
            _il2CppThrow =
                new MethodDefinition("il2cpp_throw_exception", MethodAttributes.Public | MethodAttributes.Static, 
                    MethodSignature.CreateStatic(@void, ptr));
            container.Methods.Add(_il2CppThrow);
            _il2CppNewArraySpecific =
                new MethodDefinition("il2cpp_vm_array_new_specific", MethodAttributes.Public | MethodAttributes.Static, 
                    MethodSignature.CreateStatic(ptr, ptr, ptr));
            container.Methods.Add(_il2CppNewArraySpecific);
            
            _il2CppObjectNew =
                new MethodDefinition("il2cpp_object_new", MethodAttributes.Public | MethodAttributes.Static, 
                    MethodSignature.CreateStatic(ptr, ptr));
            container.Methods.Add(_il2CppObjectNew);
            
            _il2CppRuntimeClassInit =
                new MethodDefinition("il2cpp_runtime_class_init", MethodAttributes.Public | MethodAttributes.Static, 
                    MethodSignature.CreateStatic(@void, ptr));
            container.Methods.Add(_il2CppRuntimeClassInit);

            originIntrinsic = new()
            {
                Ptr = ptr,
                CompareResult = CompareResult,
                CompareEqual = CompareEqual,
                CompareGreater = CompareGreater,
                CompareLess = CompareLess,
                CompareSign = CompareSign,
                GenericNativeInvokeSignature0 = GenericNativeInvokeSignature0,
                GenericNativeInvokeSignature1 = GenericNativeInvokeSignature1,
                GenericNativeInvokeSignature2 = GenericNativeInvokeSignature2,
                GenericNativeInvokeSignature3 = GenericNativeInvokeSignature3,
                GenericNativeInvokeSignature4 = GenericNativeInvokeSignature4,
                Compare = Compare,
                Il2CppCodegenInitializeRuntimeMetadata = _il2CppCodegenInitializeRuntimeMetadata,
                Il2CppInternalCallResolve = _il2CppInternalCallResolve,
                Il2CppThrow = _il2CppThrow,
                Il2CppObjectNew = _il2CppObjectNew,
                Il2CppNewArraySpecific = _il2CppNewArraySpecific,
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

            InstructionSetIndependentInstruction instruction = null;
            try
            {
                var instructions = body.Instructions;
                var registerToCilVar = new Dictionary<string, CilLocalVariable>();

                var labels = methodContext.ConvertedIsil
                    .Where(isil => isil.Operands is
                        [{ Type: InstructionSetIndependentOperand.OperandType.Instruction } _])
                    .Select(isil => (InstructionSetIndependentInstruction)isil.Operands[0].Data).Distinct().ToArray();
                var labelMap = new Dictionary<InstructionSetIndependentInstruction, CilInstructionLabel>(labels.Length);
                foreach (var label in labels)
                    labelMap.Add(label, new());
                
                var compareResult = new CilLocalVariable(myIntrinsics.CompareResult.ToTypeSignature());
                body.LocalVariables.Add(compareResult);
                
                for (var index = 0; index < methodContext.ConvertedIsil.Count; index++)
                {
                    instruction = methodContext.ConvertedIsil[index];
                    var operands = instruction.Operands;

                    if (labelMap.TryGetValue(instruction, out var label))
                    {
                        label.Instruction = instructions.Add(CilOpCodes.Nop); // bad but ez
                    }

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
                            if (operands[1].Data is IsilMemoryOperand)
                                instructions.RemoveAt(instructions.Count - 1);
                            instructions.Add(CilOpCodes.Stloc, GetVar((IsilRegisterOperand)operands[0].Data));
                            break;

                        case IsilMnemonic.JumpIfEqual:
                            instructions.Add(CilOpCodes.Ldloca, compareResult);
                            instructions.Add(CilOpCodes.Call, myIntrinsics.CompareEqual);
                            instructions.Add(CilOpCodes.Brtrue,
                                labelMap[(InstructionSetIndependentInstruction)operands[0].Data] ?? throw new Exception("shit br 223"));
                            break;

                        case IsilMnemonic.JumpIfNotEqual:
                            instructions.Add(CilOpCodes.Ldloca, compareResult);
                            instructions.Add(CilOpCodes.Call, myIntrinsics.CompareEqual);
                            instructions.Add(CilOpCodes.Brfalse,
                                labelMap[(InstructionSetIndependentInstruction)operands[0].Data] ?? throw new Exception("shit br 230"));
                            break;

                        case IsilMnemonic.JumpIfGreater:
                            instructions.Add(CilOpCodes.Ldloca, compareResult);
                            instructions.Add(CilOpCodes.Call, myIntrinsics.CompareGreater);
                            instructions.Add(CilOpCodes.Brtrue,
                                labelMap[(InstructionSetIndependentInstruction)operands[0].Data] ?? throw new Exception("shit br 237"));
                            break;

                        case IsilMnemonic.JumpIfLessOrEqual:
                            instructions.Add(CilOpCodes.Ldloca, compareResult);
                            instructions.Add(CilOpCodes.Call, myIntrinsics.CompareGreater);
                            instructions.Add(CilOpCodes.Brfalse,
                                labelMap[(InstructionSetIndependentInstruction)operands[0].Data] ?? throw new Exception("shit br 244"));
                            break;

                        case IsilMnemonic.JumpIfLess:
                            instructions.Add(CilOpCodes.Ldloca, compareResult);
                            instructions.Add(CilOpCodes.Call, myIntrinsics.CompareLess);
                            instructions.Add(CilOpCodes.Brtrue,
                                labelMap[(InstructionSetIndependentInstruction)operands[0].Data] ?? throw new Exception("shit br 251"));
                            break;

                        case IsilMnemonic.JumpIfGreaterOrEqual:
                            instructions.Add(CilOpCodes.Ldloca, compareResult);
                            instructions.Add(CilOpCodes.Call, myIntrinsics.CompareLess);
                            instructions.Add(CilOpCodes.Brfalse,
                                labelMap[(InstructionSetIndependentInstruction)operands[0].Data] ?? throw new Exception("shit br 258"));
                            break;

                        case IsilMnemonic.JumpIfSign:
                            instructions.Add(CilOpCodes.Ldloca, compareResult);
                            instructions.Add(CilOpCodes.Call, myIntrinsics.CompareSign);
                            instructions.Add(CilOpCodes.Brtrue,
                                labelMap[(InstructionSetIndependentInstruction)operands[0].Data] ?? throw new Exception("shit br 265"));
                            break;

                        case IsilMnemonic.JumpIfNotSign:
                            instructions.Add(CilOpCodes.Ldloca, compareResult);
                            instructions.Add(CilOpCodes.Call, myIntrinsics.CompareSign);
                            instructions.Add(CilOpCodes.Brfalse,
                                labelMap[(InstructionSetIndependentInstruction)operands[0].Data] ?? throw new Exception("shit br 272"));
                            break;

                        case IsilMnemonic.Goto:
                            instructions.Add(CilOpCodes.Br,
                                labelMap[(InstructionSetIndependentInstruction)operands[0].Data]  ?? throw new Exception("shit br 277"));
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
                            WriteToFirstOperand(() =>
                            {
                                PushOperand(operands[1].Data);
                                instructions.Add(instruction.OpCode.Mnemonic switch
                                {
                                    IsilMnemonic.Not => CilOpCodes.Not,
                                    _ => throw new NotImplementedException(),
                                });
                            });
                            break;
                        
                        case IsilMnemonic.ShiftLeft:
                        case IsilMnemonic.ShiftRight:
                            WriteToFirstOperand(() =>
                            {
                                PushOperand(operands[0].Data);
                                PushOperand(operands[1].Data);
                                instructions.Add(instruction.OpCode.Mnemonic switch
                                {
                                    IsilMnemonic.ShiftLeft => CilOpCodes.Shl,
                                    IsilMnemonic.ShiftRight => CilOpCodes.Shr,
                                    _ => throw new NotImplementedException(),
                                });
                            });
                            break;

                        case IsilMnemonic.CallNoReturn:
                        case IsilMnemonic.Call:
                            var method = instruction.Operands[0];
                            bool returnValue = false;

                            int args = 0;
                            
                            if (method.Data is not IsilImmediateOperand { Value: string })
                                foreach (var operand in operands.Skip(1))
                                {
                                    PushOperand(operand.Data);
                                    args++;
                                }

                            switch (method.Data)
                            {
                                // call managed function
                                case IsilMethodOperand { Method: { } managedMethod }:
                                    while (args != managedMethod.Parameters.Count + (managedMethod.IsStatic ? 0 : 1))
                                    {
                                        args--;
                                        instructions.Add(CilOpCodes.Pop); // why
                                    }
                                    
                                    instructions.Add(managedMethod.IsStatic ? CilOpCodes.Call : CilOpCodes.Callvirt, (IMethodDescriptor)managedMethod.GetAsmResolverMethod().ImportWith(importer));
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
                                                GetVar((IsilRegisterOperand)instruction.Operands[1].Data));
                                            instructions.Add(CilOpCodes.Call, myIntrinsics.Il2CppRuntimeClassInit);
                                            returnValue = false;
                                            break;
                                        case "il2cpp_codegen_initialize_runtime_metadata":
                                            instructions.Add(CilOpCodes.Ldloc,
                                                GetVar((IsilRegisterOperand)instruction.Operands[1].Data));
                                            instructions.Add(CilOpCodes.Call,
                                                myIntrinsics.Il2CppCodegenInitializeRuntimeMetadata);
                                            returnValue = false;
                                            break;
                                        case "InternalCalls_Resolve":
                                            instructions.Add(CilOpCodes.Ldloc,
                                                GetVar((IsilRegisterOperand)instruction.Operands[1].Data));
                                            instructions.Add(CilOpCodes.Call,
                                                myIntrinsics.Il2CppInternalCallResolve);
                                            returnValue = true;
                                            break;
                                        case "il2cpp_codegen_object_new":
                                            instructions.Add(CilOpCodes.Ldloc,
                                                GetVar((IsilRegisterOperand)instruction.Operands[1].Data));
                                            instructions.Add(CilOpCodes.Call,
                                                myIntrinsics.Il2CppObjectNew);
                                            returnValue = true;
                                            break;
                                        case "il2cpp_vm_exception_raise":
                                            instructions.Add(CilOpCodes.Ldloc,
                                                GetVar((IsilRegisterOperand)instruction.Operands[1].Data));
                                            instructions.Add(CilOpCodes.Call,
                                                myIntrinsics.Il2CppThrow);
                                            returnValue = false;
                                            break;
                                        case "il2cpp_vm_array_new_specific":
                                            instructions.Add(CilOpCodes.Ldloc,
                                                GetVar((IsilRegisterOperand)instruction.Operands[1].Data));
                                            instructions.Add(CilOpCodes.Ldloc,
                                                GetVar((IsilRegisterOperand)instruction.Operands[2].Data));
                                            instructions.Add(CilOpCodes.Call,
                                                myIntrinsics.Il2CppNewArraySpecific);
                                            returnValue = true;
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
                                    instructions.Add(CilOpCodes.Calli, GetCalliSignature(args));
                                    returnValue = true;
                                    break;
                                // call reg
                                case IsilRegisterOperand variable:
                                    instructions.Add(CilOpCodes.Ldloc, GetVar(variable));
                                    instructions.Add(CilOpCodes.Calli, GetCalliSignature(args));
                                    returnValue = true;
                                    break;
                                default:
                                    body.ThrowError(
                                        $"Unsupported call operand: {method} of type {method.GetType()}");
                                    return;
                            }

                            StandAloneSignature GetCalliSignature(int argCount)
                            {
                                return args switch
                                {
                                    0 => myIntrinsics.GenericNativeInvokeSignature0,
                                    1 => myIntrinsics.GenericNativeInvokeSignature1,
                                    2 => myIntrinsics.GenericNativeInvokeSignature2,
                                    3 => myIntrinsics.GenericNativeInvokeSignature3,
                                    4 => myIntrinsics.GenericNativeInvokeSignature4,
                                    _ => myIntrinsics.GetCalliSignature(argCount)
                                };
                            }

                            if (returnValue)
                                instructions.Add(CilOpCodes.Stloc,
                                    GetVar(new IsilRegisterOperand("rax")));

                            if (instruction.OpCode.Mnemonic == IsilMnemonic.CallNoReturn)
                                instructions.Add(CilOpCodes.Ret);
                            break;

                        case IsilMnemonic.Compare:
                            PushOperand(operands[0].Data);
                            PushOperand(operands[1].Data);
                            body.Instructions.Add(CilOpCodes.Call, myIntrinsics.Compare);
                            body.Instructions.Add(CilOpCodes.Stloc, compareResult);
                            break;

                        case IsilMnemonic.Return:
                            if (operands.Length != 0)
                                instructions.Add(CilOpCodes.Ldloc, GetVar((IsilRegisterOperand)operands[0].Data));
                            instructions.Add(CilOpCodes.Ret);
                            break;

                        case IsilMnemonic.ShiftStack:
                        case IsilMnemonic.Push:
                        case IsilMnemonic.Pop:
                            continue;

                        case IsilMnemonic.Interrupt:
                            body.ThrowError("Interrupt");
                            break;
                        
                        case IsilMnemonic.NotImplemented: // why my decompiled looks so uncompleted, hmmmmmmmm
                            break;
                        
                        default:
                        {
                            body.ThrowError($"Unimplemented mnemonic: {instruction.OpCode.Mnemonic}");
                            break;
                        }
                    }

                    void WriteToFirstOperand(Action pushOperands)
                    {
                        if (operands[0].Data is IsilRegisterOperand moveVar)
                        {
                            pushOperands();
                            instructions.Add(CilOpCodes.Stloc, GetVar(moveVar));
                        }
                        else if (operands[0].Data is IsilVectorRegisterElementOperand moveVector)
                        {
                            instructions.Add(CilOpCodes.Ldloca, GetVar(new IsilRegisterOperand(moveVector.RegisterName))); // 1
                            instructions.Add(CilOpCodes.Conv_I); // 1
                            instructions.Add(CilOpCodes.Ldc_I4, moveVector.Index); // 2
                            instructions.Add(CilOpCodes.Sizeof, moveVector.Width switch // 3
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
                            instructions.Add(CilOpCodes.Mul); // 2
                            instructions.Add(CilOpCodes.Add); // 1
                            pushOperands(); // 1 + val
                            instructions.Add(moveVector.Width switch
                            {
                                IsilVectorRegisterElementOperand.VectorElementWidth.B => CilOpCodes.Stind_I1,
                                IsilVectorRegisterElementOperand.VectorElementWidth.S => CilOpCodes.Stind_R4,
                                IsilVectorRegisterElementOperand.VectorElementWidth
                                    .H => CilOpCodes.Stind_I1, // no half life 
                                IsilVectorRegisterElementOperand.VectorElementWidth.D => CilOpCodes.Stind_R8,
                                _ => throw new NotImplementedException()
                            }); // 0 ?
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
                
                foreach (var cilInstructionLabel in labelMap.Where(kv => kv.Value.Instruction == null))
                {
                    cilInstructionLabel.Value.Instruction = instructions.Add(CilOpCodes.Nop);
                    body.ThrowError("Cant resolve label");
                }

                if (body.Instructions.Last().OpCode.Code != CilCode.Ret)
                {
                    if (body.Owner.HasReturnType())
                        body.Instructions.Add(new CilInstruction(CilOpCodes.Ldloc, GetVar(new IsilRegisterOperand("rax"))));
                    body.Instructions.Add(new CilInstruction(CilOpCodes.Ret));
                }

                new Transformer(methodDefinition, methodContext).Process();
                
                CilLocalVariable GetVar(IsilRegisterOperand variable)
                {
                    var reg = variable.RegisterName switch
                    {
                        "al" or "ax" or "eax" => "rax",
                        "bl" or "bx" or "ebx" => "rbx",
                        "cl" or "cx" or "ecx" => "rcx",
                        "dl" or "dx" or "edx" => "rdx",
                        "di" or "dil" or "edi" => "rdi",
                        "si" or "sil" or "esi" => "rsi",
                        "sp" or "spl" or "esp" => "rsp",
                        "r8b" or "r8w" or "r8d" => "r8",
                        "r9b" or "r9w" or "r9d" => "r9",
                        "r10b" or "r10w" or "r10d" => "r10",
                        "r11b" or "r11w" or "r11d" => "r11",
                        "r12b" or "r12w" or "r12d" => "r12",
                        "r13b" or "r13w" or "r13d" => "r13",
                        "r14b" or "r14w" or "r14d" => "r14",
                        "r15b" or "r15w" or "r15d" => "r15",
                        _ => variable.RegisterName
                    };
                    
                    if (registerToCilVar.TryGetValue(reg, out var local))
                        return local;
                    local = new CilLocalVariable(methodDefinition.Module!.CorLibTypeFactory.UIntPtr);
                    body.LocalVariables.Add(local);
                    registerToCilVar.Add(reg, local);
                    
                    var paramCount = methodDefinition.Parameters.Count;
                    if (!methodDefinition.IsStatic)
                        paramCount++;
                    
                    switch (reg)
                    {
                        case "rcx" when paramCount > 0:
                            instructions.Insert(0, CilOpCodes.Ldarg_0);
                            instructions.Insert(1, CilOpCodes.Stloc, local);
                            break;
                        case "rdx" when paramCount > 1:
                            instructions.Insert(0, CilOpCodes.Ldarg_1);
                            instructions.Insert(1, CilOpCodes.Stloc, local);
                            break;
                        case "r8" when paramCount > 2:
                            instructions.Insert(0, CilOpCodes.Ldarg_2);
                            instructions.Insert(1, CilOpCodes.Stloc, local);
                            break;
                        case "r9" when paramCount > 3:
                            instructions.Insert(0, CilOpCodes.Ldarg_3);
                            instructions.Insert(1, CilOpCodes.Stloc, local);
                            break;
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
                        case IsilVectorRegisterElementOperand vector:
                            instructions.Add(CilOpCodes.Ldloca, GetVar(new IsilRegisterOperand(vector.RegisterName))); // 1
                            instructions.Add(CilOpCodes.Ldc_I4, vector.Index); // 2
                            instructions.Add(CilOpCodes.Sizeof, vector.Width switch // 3
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
                            instructions.Add(CilOpCodes.Mul); // 2
                            instructions.Add(CilOpCodes.Add); // 1
                            instructions.Add(vector.Width switch // 1
                            {
                                IsilVectorRegisterElementOperand.VectorElementWidth.B => CilOpCodes.Ldind_U1,
                                IsilVectorRegisterElementOperand.VectorElementWidth.S => CilOpCodes.Ldind_R4,
                                IsilVectorRegisterElementOperand.VectorElementWidth
                                    .H => CilOpCodes.Ldind_U1, // no half life 
                                IsilVectorRegisterElementOperand.VectorElementWidth.D => CilOpCodes.Ldind_R8,
                                _ => throw new NotImplementedException()
                            });
                            break;
                        case IsilRegisterOperand variable:
                            instructions.Add(CilOpCodes.Ldloc, GetVar(variable));
                            break;
                        default:
                            throw new NotImplementedException($"{operandData} of type {operandData.GetType().FullName}");
                    }
                }
            }
            catch (Exception e)
            {
                body.ThrowError($"(at {instruction}) " + e.ToString());
            }
        }
    }

    public override string OutputFormatId => "shitty_il";
    public override string OutputFormatName => "Better Assembly Output";
}