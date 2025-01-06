using System.Runtime.CompilerServices;
using AsmResolver.DotNet;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Plugin.BetterAssemblyOutput.ExtendedIsil;

public sealed class IsilVariable : IsilOperandData, IEquatable<IsilVariable>
{
    private static uint _internalIndex = 0;
    private uint _idx = _internalIndex++;
    public int LinkParamId = int.MaxValue;
    public ITypeDefOrRef? Type;

    public bool Equals(IsilVariable? other)
    {
        if (other == null) return false;
        return other._idx == _idx;
    }

    public override bool Equals(object? obj)
    {
        if (obj is IsilVariable variable) return variable._idx == _idx;
        return false;
    }

    public override int GetHashCode() => Unsafe.As<uint, int>(ref _idx);
}

public sealed class IsilVariableVector(IsilVariable variable, IsilVectorRegisterElementOperand.VectorElementWidth width, int index) : IsilOperandData
{
    public IsilVariable Variable { get; } = variable;
    public IsilVectorRegisterElementOperand.VectorElementWidth Width { get; } = width;
    public int Index { get; } = index;
}