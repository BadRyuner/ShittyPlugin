using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Plugin.BetterAssemblyOutput.ExtendedIsil;

public static class ExtraInstructions
{
    public static readonly InstructionSetIndependentOpCode UpdateRegister = new(IsilMnemonic.Push, 1);
}