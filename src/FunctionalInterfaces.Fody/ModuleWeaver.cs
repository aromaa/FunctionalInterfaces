using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace FunctionalInterfaces.Fody;

public sealed class ModuleWeaver : BaseModuleWeaver
{
	public override void Execute()
	{
		foreach (TypeDefinition moduleDefinitionType in this.ModuleDefinition.Types)
		{
			foreach (MethodDefinition methodDefinition in moduleDefinitionType.Methods)
			{
				if (!methodDefinition.HasBody)
				{
					continue;
				}

				foreach (Instruction bodyInstruction in methodDefinition.Body.Instructions.ToList())
				{
					if (bodyInstruction.OpCode == OpCodes.Call)
					{
						if (bodyInstruction.Operand is MethodDefinition target)
						{
							(MethodReference? candidate, Dictionary<int, MethodReference> functionalInterfaces) = this.FindFunctionalInterfaceTarget(target);
							if (candidate is null)
							{
								continue;
							}

							Dictionary<int, Instruction>? functionalInterfaceCandidates = this.ScanFunctionInterfaceCandidate(bodyInstruction, functionalInterfaces);
							if (functionalInterfaceCandidates is null)
							{
								continue;
							}

							this.ConvertToFunctionalInterface(methodDefinition.Body, bodyInstruction, candidate, functionalInterfaces, functionalInterfaceCandidates);
						}
					}
				}
			}
		}
	}

	private (MethodReference? Candidate, Dictionary<int, MethodReference> FunctionalInterfaces) FindFunctionalInterfaceTarget(MethodReference target)
	{
		HashSet<int> candidateParams = new();
		for (int i = 0; i < target.Parameters.Count; i++)
		{
			ParameterDefinition parameter = target.Parameters[i];
			if (parameter.ParameterType.FullName == "System.Action")
			{
				candidateParams.Add(i);
			}
		}

		if (candidateParams.Count <= 0)
		{
			return (null, new Dictionary<int, MethodReference>());
		}

		foreach (MethodDefinition candidate in target.DeclaringType.Resolve().Methods
					 .Where(m => m != target && m.HasGenericParameters && m.Parameters.Count == target.Parameters.Count && m.Name == target.Name))
		{
			for (int i = 0; i < candidate.Parameters.Count; i++)
			{
				ParameterDefinition parameter = candidate.Parameters[i];
				TypeReference parameterType = parameter.ParameterType;
				if (!candidateParams.Contains(i))
				{
					if (parameterType != target.Parameters[i].ParameterType)
					{
						goto CONTINUE;
					}
				}
				else if (!parameterType.IsGenericParameter || parameterType is not GenericParameter { Constraints.Count: 1 })
				{
					goto CONTINUE;
				}
			}

			Dictionary<int, MethodReference> functionalInterfaces = new();
			foreach (int i in candidateParams)
			{
				GenericParameter parameter = (GenericParameter)candidate.Parameters[i].ParameterType;
				if (parameter.Constraints[0].ConstraintType.Resolve() is not { IsInterface: true } interfaceCandidate)
				{
					goto CONTINUE;
				}

				MethodDefinition targetCandidate;
				using (IEnumerator<MethodDefinition> targetCandidates = interfaceCandidate.Methods.Where(m => m.IsAbstract).GetEnumerator())
				{
					if (!targetCandidates.MoveNext() || targetCandidates.Current is not { } current || targetCandidates.MoveNext())
					{
						goto CONTINUE;
					}

					targetCandidate = current;
				}

				for (int j = 0; j < targetCandidate.Parameters.Count; j++)
				{
					//Matches delegate
				}

				functionalInterfaces.Add(i, targetCandidate);
			}

			return (candidate, functionalInterfaces);
		CONTINUE:
			;
		}

		return (null, new Dictionary<int, MethodReference>());
	}

	private Dictionary<int, Instruction>? ScanFunctionInterfaceCandidate(Instruction callInstruction, Dictionary<int, MethodReference> functionalInterfaces)
	{
		if (callInstruction.Operand is not MethodReference target)
		{
			return null;
		}

		Dictionary<int, Instruction> candidates = new();

		Instruction last = callInstruction;
		for (int i = target.Parameters.Count - 1; i >= 0; i--)
		{
			Instruction current = last.Previous;
			if (!functionalInterfaces.TryGetValue(i, out MethodReference? functionalInterface))
			{
				last = this.WalkStack(current);

				continue;
			}

			if (current.OpCode != OpCodes.Newobj || current.Previous is not { } ldftnInstruction || ldftnInstruction.OpCode != OpCodes.Ldftn)
			{
				return null;
			}

			MethodReference lambdaMethod = (MethodReference)ldftnInstruction.Operand;
			if (lambdaMethod.Parameters.Count != functionalInterface.Parameters.Count)
			{
				return null;
			}

			candidates.Add(i, current);

			last = this.WalkStack(current);
		}

		return candidates;
	}

	private void ConvertToFunctionalInterface(MethodBody body, Instruction callInstruction, MethodReference candidate, Dictionary<int, MethodReference> functionalInterfaces, Dictionary<int, Instruction> functionalInterfaceCandidates)
	{
		if (callInstruction.Operand is not MethodReference target)
		{
			return;
		}

		body.SimplifyMacros();

		List<Instruction> toRemove = new();

		foreach (KeyValuePair<int, Instruction> kvp in functionalInterfaceCandidates)
		{
			(int index, Instruction instruction) = (kvp.Key, kvp.Value);
			MethodReference functionalInterfaceTarget = functionalInterfaces[index];

			toRemove.Add(instruction);

			Instruction current = instruction.Previous;
			for (int i = 1; i < ((MethodReference)instruction.Operand).Parameters.Count; i++)
			{
				current = this.WalkStack(current, toRemove.Add).Previous;
			}

			VariableDefinition originalVariable = (VariableDefinition)current.Operand;

			TypeDefinition nestedType = originalVariable.VariableType.DeclaringType.Resolve();

			TypeDefinition type = new(originalVariable.VariableType.Namespace, $"{originalVariable.VariableType.Name}_FunctionalInterface_{nestedType.NestedTypes.Count}", TypeAttributes.NestedPrivate | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, this.ModuleDefinition.ImportReference(this.FindTypeDefinition("System.ValueType")));

			nestedType.NestedTypes.Add(type);

			type.Interfaces.Add(new InterfaceImplementation(functionalInterfaceTarget.DeclaringType));

			foreach (FieldDefinition declaringTypeField in ((MethodDefinition)instruction.Previous.Operand).DeclaringType.Fields)
			{
				type.Fields.Add(new FieldDefinition(declaringTypeField.Name, declaringTypeField.Attributes, declaringTypeField.FieldType));
			}

			MethodDefinition method = new(functionalInterfaceTarget.Name, MethodAttributes.Public | MethodAttributes.Virtual, functionalInterfaceTarget.ReturnType);
			type.Methods.Add(method);

			VariableDefinition variable = new(type);

			body.Variables.Add(variable);

			foreach (Instruction lambdaInstruction in ((MethodDefinition)instruction.Previous.Operand).Body.Instructions)
			{
				if (lambdaInstruction.OpCode == OpCodes.Ldfld)
				{
					method.Body.Instructions.Add(Instruction.Create(lambdaInstruction.OpCode, type.Fields.Single(f => f.Name == ((FieldReference)lambdaInstruction.Operand).Name)));
				}
				else
				{
					method.Body.Instructions.Add(lambdaInstruction);
				}
			}

			callInstruction.Operand = new GenericInstanceMethod(candidate)
			{
				GenericArguments =
				{
					type
				}
			};

			current.OpCode = OpCodes.Ldloc;
			current.Operand = variable;

			while (true)
			{
				current = this.WalkStack(current, i =>
				{
					if (i.OpCode == OpCodes.Ldloc && ((VariableDefinition)i.Operand).Index == originalVariable.Index)
					{
						i.OpCode = OpCodes.Ldloca;
						i.Operand = variable;
					}
					else if (i.OpCode == OpCodes.Stfld && ((FieldDefinition)i.Operand).DeclaringType == originalVariable.VariableType)
					{
						i.Operand = type.Fields.Single(f => f.Name == ((FieldReference)i.Operand).Name);
					}
				}).Previous;

				if (current.OpCode == OpCodes.Stloc && ((VariableDefinition)current.Operand).Index == originalVariable.Index)
				{
					body.Instructions.Remove(current.Previous);
					body.Instructions.Remove(current);

					break;
				}
			}
		}

		foreach (Instruction instruction in toRemove)
		{
			body.Instructions.Remove(instruction);
		}

		body.Optimize();
	}

	private Instruction WalkStack(Instruction root, Action<Instruction>? consumer = null)
	{
		consumer?.Invoke(root);

		if (root.OpCode == OpCodes.Newobj)
		{
			MethodReference constructor = (MethodReference)root.Operand;

			Instruction last = root;
			for (int i = 0; i < constructor.Parameters.Count; i++)
			{
				last = this.WalkStack(last.Previous, consumer);
			}

			return last;
		}

		return root;
	}

	public override IEnumerable<string> GetAssembliesForScanning()
	{
		yield return "netstandard";
		yield return "mscorlib";
	}
}
