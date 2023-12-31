using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

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
		foreach (MethodDefinition methodDefinition in type.Methods)
		{
			if (!methodDefinition.HasBody)
			{
				continue;
			}

			foreach (Instruction bodyInstruction in methodDefinition.Body.Instructions.ToList())
			{
				if (bodyInstruction.OpCode != OpCodes.Call && bodyInstruction.OpCode != OpCodes.Callvirt)
				{
					continue;
				}

				if (bodyInstruction.Operand is not MethodReference target)
				{
					continue;
				}

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

		foreach (TypeDefinition nestedType in type.NestedTypes.ToList())
		{
			this.ProcessType(nestedType);
		}
	}

	private (MethodReference? Candidate, Dictionary<int, MethodReference> FunctionalInterfaces) FindFunctionalInterfaceTarget(MethodReference target)
	{
		HashSet<int> candidateParams = new();
		for (int i = 0; i < target.Parameters.Count; i++)
		{
			ParameterDefinition parameter = target.Parameters[i];
			if (parameter.ParameterType.FullName.StartsWith("System.Action")
				|| parameter.ParameterType.FullName.StartsWith("System.Func"))
			{
				candidateParams.Add(i);
			}
		}

		if (candidateParams.Count <= 0)
		{
			return (null, new Dictionary<int, MethodReference>());
		}

		TypeDefinition targetType = target.DeclaringType.Resolve();
		if (targetType is null)
		{
			return (null, new Dictionary<int, MethodReference>());
		}

		foreach (MethodDefinition candidate in targetType.Methods
					 .Where(m => m != target && m.HasGenericParameters && m.Parameters.Count == target.Parameters.Count && m.Name == target.Name))
		{
			for (int i = 0; i < candidate.Parameters.Count; i++)
			{
				ParameterDefinition parameter = candidate.Parameters[i];
				TypeReference parameterType = parameter.ParameterType;
				if (!candidateParams.Contains(i))
				{
					if (parameterType.Resolve() != target.Parameters[i].ParameterType.Resolve())
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

				if (target.Parameters[i].ParameterType is GenericInstanceType targetDelegate)
				{
					if (targetDelegate.GenericArguments.Count < targetCandidate.Parameters.Count)
					{
						goto CONTINUE;
					}

					for (int j = 0; j < targetCandidate.Parameters.Count; j++)
					{
						if (targetDelegate.GenericArguments[j].Resolve() != targetCandidate.Parameters[j].ParameterType.Resolve())
						{
							goto CONTINUE;
						}
					}
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

			if (current.Operand is not VariableDefinition originalVariable)
			{
				body.Optimize();

				return;
			}

			MethodDefinition lambdaMethod = ((MethodReference)instruction.Previous.Operand).Resolve();

			TypeDefinition type = new(lambdaMethod.DeclaringType.Namespace, $"{lambdaMethod.DeclaringType.Name}_FunctionalInterface_{lambdaMethod.DeclaringType.NestedTypes.Count}", TypeAttributes.NestedPrivate | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, this.ModuleDefinition.ImportReference(this.FindTypeDefinition("System.ValueType")));

			lambdaMethod.DeclaringType.DeclaringType.NestedTypes.Add(type);

			type.Interfaces.Add(new InterfaceImplementation(this.ModuleDefinition.ImportReference(functionalInterfaceTarget.DeclaringType)));

			foreach (FieldDefinition declaringTypeField in lambdaMethod.DeclaringType.Fields)
			{
				type.Fields.Add(new FieldDefinition(declaringTypeField.Name, declaringTypeField.Attributes, declaringTypeField.FieldType));
			}

			foreach (GenericParameter genericParameter in lambdaMethod.DeclaringType.GenericParameters)
			{
				GenericParameter newGenericParameter = new(genericParameter.Name, genericParameter.Owner)
				{
					Attributes = genericParameter.Attributes
				};

				foreach (GenericParameterConstraint constraint in genericParameter.Constraints)
				{
					newGenericParameter.Constraints.Add(constraint);
				}

				type.GenericParameters.Add(newGenericParameter);
			}

			MethodDefinition method = new(functionalInterfaceTarget.Name, MethodAttributes.Public | MethodAttributes.Virtual, this.ModuleDefinition.ImportReference(functionalInterfaceTarget.ReturnType));

			type.Methods.Add(method);

			TypeReference genericType = type.DeclaringType.GenericParameters.Count == 0
				? type
				: type.MakeGenericInstanceType(type.DeclaringType.GenericParameters.ToArray());

			VariableDefinition variable = new(genericType);

			body.Variables.Add(variable);

			CopyMethod(method, lambdaMethod);

			void CopyMethod(MethodDefinition destinationMethod, MethodDefinition copyMethod)
			{
				foreach (ParameterDefinition parameter in copyMethod.Parameters)
				{
					destinationMethod.Parameters.Add(parameter);
				}

				foreach (VariableDefinition variableDefinition in copyMethod.Body.Variables)
				{
					destinationMethod.Body.Variables.Add(variableDefinition);
				}

				foreach (Instruction lambdaInstruction in copyMethod.Body.Instructions)
				{
					if (lambdaInstruction.OpCode == OpCodes.Ldfld && ((FieldReference)lambdaInstruction.Operand).DeclaringType.Resolve() == originalVariable.VariableType.Resolve())
					{
						destinationMethod.Body.Instructions.Add(Instruction.Create(lambdaInstruction.OpCode, type.Fields.Single(f => f.Name == ((FieldReference)lambdaInstruction.Operand).Name)));
					}
					else if (lambdaInstruction.OpCode == OpCodes.Call && ((MethodReference)lambdaInstruction.Operand).DeclaringType.Resolve() == originalVariable.VariableType.Resolve())
					{
						MethodDefinition callTarget = ((MethodReference)lambdaInstruction.Operand).Resolve();
						MethodReference? callMethod = type.Methods.FirstOrDefault(f => f.Name == ((MethodReference)lambdaInstruction.Operand).Name);
						if (callMethod == null)
						{
							callMethod = new MethodDefinition(callTarget.Name, MethodAttributes.Private, this.ModuleDefinition.ImportReference(callTarget.ReturnType));

							type.Methods.Add((MethodDefinition)callMethod);

							CopyMethod((MethodDefinition)callMethod, callTarget);
						}

						destinationMethod.Body.Instructions.Add(Instruction.Create(lambdaInstruction.OpCode, type.DeclaringType.GenericParameters.Count == 0
							? callMethod
							: new GenericInstanceMethod(this.ModuleDefinition.ImportReference(callMethod))
							{
								GenericArguments =
								{
									genericType
								}
							}));
					}
					else
					{
						destinationMethod.Body.Instructions.Add(lambdaInstruction);
					}
				}
			}

			callInstruction.Operand = new GenericInstanceMethod(this.ModuleDefinition.ImportReference(candidate))
			{
				GenericArguments =
				{
					genericType
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
					else if (i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).DeclaringType.Resolve() == originalVariable.VariableType.Resolve())
					{
						i.Operand = new FieldReference(((FieldReference)i.Operand).Name, ((FieldReference)i.Operand).FieldType, genericType);
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
