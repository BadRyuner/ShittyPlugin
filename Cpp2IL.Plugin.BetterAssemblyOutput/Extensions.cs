using System.Runtime.CompilerServices;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Plugin.BetterAssemblyOutput.ExtendedIsil;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Plugin.BetterAssemblyOutput;

public static class Extensions
{
    public static void ThrowNullBody(this MethodDefinition method)
    {
        method.CilMethodBody = new CilMethodBody(method);
        method.CilMethodBody.Instructions.Add(CilOpCodes.Ldnull);
        method.CilMethodBody.Instructions.Add(CilOpCodes.Throw);
    }

    public static void ThrowError(this CilMethodBody body, string whatTheFuck)
    {
        body.Instructions.Add(CilOpCodes.Ldstr, whatTheFuck);
        body.Instructions.Add(CilOpCodes.Throw);
    }

    public static TypeSignature GetAsmResolverType(this TypeAnalysisContext context)
    {
        switch (context)
        {
            case ArrayTypeAnalysisContext arrayType:
                return new ArrayTypeSignature(arrayType.ElementType.GetAsmResolverType());
            case SzArrayTypeAnalysisContext arrayType:
                return new SzArrayTypeSignature(arrayType.ElementType.GetAsmResolverType());
            case PointerTypeAnalysisContext pointer:
                return new PointerTypeSignature(pointer.ElementType.GetAsmResolverType());
            case ByRefTypeAnalysisContext byRef:
                return new ByReferenceTypeSignature(byRef.ElementType.GetAsmResolverType());
            case GenericInstanceTypeAnalysisContext genericInstance:
                return new GenericInstanceTypeSignature(genericInstance.GenericType.GetAsmResolverType().ToTypeDefOrRef(), 
                    genericInstance.IsValueType, genericInstance.GenericArguments.Select(ga => ga.GetAsmResolverType()).ToArray());
            case GenericParameterTypeAnalysisContext genericParameter:
                return new GenericParameterSignature(genericParameter.Type == Il2CppTypeEnum.IL2CPP_TYPE_VAR ? GenericParameterType.Type : GenericParameterType.Method, genericParameter.Index);
        }
        return context.GetExtraData<TypeDefinition>("AsmResolverType")?.ToTypeSignature() ?? throw new NullReferenceException("fuck");
    }

    public static IMethodDescriptor GetAsmResolverMethod(this MethodAnalysisContext context)
    {
        if (context is ConcreteGenericMethodAnalysisContext concreteGenericMethod)
            return new MethodSpecification((IMethodDefOrRef)concreteGenericMethod.BaseMethodContext.GetAsmResolverMethod(), new GenericInstanceMethodSignature(CallingConventionAttributes.Default, concreteGenericMethod.ResolveMethodGenericParameters()
                .Select(t => t.GetAsmResolverType())));
        
        return context.GetExtraData<MethodDefinition>("AsmResolverMethod") ?? throw new NullReferenceException("Shit happens");
    }
    
    private struct MockOperand
    {
        public InstructionSetIndependentOperand.OperandType Type;
        public IsilOperandData Data;
    }
    
    public static InstructionSetIndependentOperand ToOperand(this IsilVariable instruction)
    {
        var mocked = new MockOperand()
        {
            Type = InstructionSetIndependentOperand.OperandType.Register,
            Data = instruction
        };
        return Unsafe.As<MockOperand, InstructionSetIndependentOperand>(ref mocked);
    }
}