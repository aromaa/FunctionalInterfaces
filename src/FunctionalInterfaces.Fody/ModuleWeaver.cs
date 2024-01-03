using Fody;
using Mono.Cecil;

namespace FunctionalInterfaces.Fody;

public sealed class ModuleWeaver : BaseModuleWeaver
{
	public override void Execute()
	{
		foreach (TypeDefinition type in this.ModuleDefinition.Types)
		{
			this.ProcessType(type);
		}
	}

	private void ProcessType(TypeDefinition type)
	{
		foreach (MethodDefinition method in type.Methods)
		{
			if (!method.HasBody)
			{
				continue;
			}

			//TODO: Attribute?

			string identifierName = method.Name + "_FunctionalInterface_";

			MethodDefinition? functionalInterface = type.Methods.FirstOrDefault(m => m.Name.StartsWith(identifierName));
			if (functionalInterface is not null)
			{
				method.Body = functionalInterface.Body;

				//TODO: Clean up the old method
			}
		}

		foreach (TypeDefinition nestedType in type.NestedTypes)
		{
			this.ProcessType(nestedType);
		}
	}

	public override IEnumerable<string> GetAssembliesForScanning() => Array.Empty<string>();
}
