using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;
using Cpp2IL.Plugin.BetterAssemblyOutput;

[assembly: RegisterCpp2IlPlugin(pluginType: typeof(Plugin))]

namespace Cpp2IL.Plugin.BetterAssemblyOutput;

public class Plugin : Cpp2IlPlugin
{
    public override void OnLoad()
    {
        Console.WriteLine("Fuck yeah");
        OutputFormatRegistry.Register<ShittyOutputFormat>();
    }

    public override string Name => "BetterAssemblyOutput";
    public override string Description => "Adds assembly output formats that contains some shitty IL";
}