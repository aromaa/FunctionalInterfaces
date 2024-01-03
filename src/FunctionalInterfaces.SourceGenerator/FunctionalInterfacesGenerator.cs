using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FunctionalInterfaces.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class FunctionalInterfacesGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		IncrementalValuesProvider<(InvocationExpressionSyntax Invocation, SemanticModel SemanticModel)> invocationValuesProvider = context.SyntaxProvider.CreateSyntaxProvider(
			static (node, _) => node is InvocationExpressionSyntax,
			static (context, _) => ((InvocationExpressionSyntax)context.Node, context.SemanticModel));

		Debugger.Launch();

		context.RegisterImplementationSourceOutput(invocationValuesProvider, static (sourceProductionContext, value) =>
		{
			(InvocationExpressionSyntax invocation, SemanticModel semanticModel) = value;

			SymbolInfo invokeTargetInfo = value.SemanticModel.GetSymbolInfo(invocation);
			if (invokeTargetInfo.Symbol is not IMethodSymbol invokeTargetSymbol)
			{
				return;
			}

			(IMethodSymbol? candidate, Dictionary<int, IMethodSymbol> functionalInterfaces) = FunctionalInterfacesGenerator.FindFunctionalInterfaceTarget(invocation, invokeTargetSymbol);
			if (candidate is null)
			{
				return;
			}

			FunctionalInterfacesGenerator.GenerateFunctionalInterfaces(sourceProductionContext, semanticModel, invocation, invokeTargetSymbol, candidate, functionalInterfaces);
		});
	}

	private static (IMethodSymbol? Target, Dictionary<int, IMethodSymbol> FunctionalInterfaces) FindFunctionalInterfaceTarget(InvocationExpressionSyntax invoke, IMethodSymbol invokeTargetSymbol)
	{
		HashSet<int> candidateParams = new();
		for (int i = 0; i < invoke.ArgumentList.Arguments.Count; i++)
		{
			ArgumentSyntax argument = invoke.ArgumentList.Arguments[i];
			if (argument.Expression is ParenthesizedLambdaExpressionSyntax)
			{
				candidateParams.Add(i);
			}
			else if (argument.Expression is SimpleLambdaExpressionSyntax)
			{
				candidateParams.Add(i);
			}
		}

		if (candidateParams.Count <= 0)
		{
			return (null, new Dictionary<int, IMethodSymbol>());
		}

		Dictionary<int, IMethodSymbol> functionalInterfaces = new();
		foreach (IMethodSymbol candidate in invokeTargetSymbol.ContainingType.GetMembers(invokeTargetSymbol.Name)
					 .Where(s => !SymbolEqualityComparer.Default.Equals(s, invokeTargetSymbol) && s is IMethodSymbol { Arity: > 0 } methodSymbol && methodSymbol.Parameters.Length == invokeTargetSymbol.Parameters.Length)
					 .Cast<IMethodSymbol>())
		{
			for (int i = 0; i < candidate.Parameters.Length; i++)
			{
				IParameterSymbol candidateParameter = candidate.Parameters[i];
				IParameterSymbol targetParameter = invokeTargetSymbol.Parameters[i];
				if (!candidateParams.Contains(i))
				{
					if (!SymbolEqualityComparer.Default.Equals(candidateParameter, targetParameter))
					{
						goto CONTINUE;
					}
				}
				else if (candidateParameter.Type is not ITypeParameterSymbol { ConstraintTypes.Length: 1 })
				{
					goto CONTINUE;
				}
			}

			foreach (int i in candidateParams)
			{
				ITypeParameterSymbol candidateParameter = (ITypeParameterSymbol)candidate.Parameters[i].Type;
				ITypeSymbol functionalInterfaceType = candidateParameter.ConstraintTypes[0];
				if (functionalInterfaceType.TypeKind != TypeKind.Interface)
				{
					goto CONTINUE;
				}

				ImmutableArray<ISymbol> targetCandidates = functionalInterfaceType.GetMembers();
				if (targetCandidates.Length != 1 || targetCandidates[0] is not IMethodSymbol targetSymbol)
				{
					goto CONTINUE;
				}

				functionalInterfaces.Add(i, targetSymbol);
			}

			return (candidate, functionalInterfaces);

		CONTINUE:
			;
		}

		return (null, new Dictionary<int, IMethodSymbol>());
	}

	private static void GenerateFunctionalInterfaces(SourceProductionContext sourceProductionContext, SemanticModel semanticModel, InvocationExpressionSyntax invoke, IMethodSymbol invokeTargetSymbol, IMethodSymbol candidateSymbol, Dictionary<int, IMethodSymbol> functionalInterfaces)
	{
		foreach (KeyValuePair<int, IMethodSymbol> kvp in functionalInterfaces)
		{
			(int i, IMethodSymbol candidateTarget) = (kvp.Key, kvp.Value);

			LambdaExpressionSyntax lambda = (LambdaExpressionSyntax)invoke.ArgumentList.Arguments[i].Expression;

			SymbolInfo lambdaSymbolInfo = semanticModel.GetSymbolInfo(lambda);
			if (lambdaSymbolInfo.Symbol is not IMethodSymbol lambdaSymbol)
			{
				continue;
			}

			//Top to bottom
			List<ClassDeclarationSyntax> hierarchy = GetHierarchy(invoke.Parent);
			if (hierarchy.Count <= 0)
			{
				continue;
			}

			static List<ClassDeclarationSyntax> GetHierarchy(SyntaxNode? node)
			{
				List<ClassDeclarationSyntax> hierarchy = new();

				while (node != null)
				{
					if (node is ClassDeclarationSyntax classDeclaration)
					{
						hierarchy.Add(classDeclaration);
					}

					node = node.Parent;
				}

				return hierarchy;
			}

			INamedTypeSymbol? classSymbol = semanticModel.GetDeclaredSymbol(hierarchy[0]);
			if (classSymbol is null)
			{
				continue;
			}

			MethodDeclarationSyntax? declaringMethod = GetDeclaringMethod(invoke.Parent);
			static MethodDeclarationSyntax? GetDeclaringMethod(SyntaxNode? node)
			{
				while (node != null)
				{
					if (node is MethodDeclarationSyntax method)
					{
						return method;
					}
					else if (node is LambdaExpressionSyntax)
					{
						break;
					}

					node = node.Parent;
				}

				return null;
			}

			DataFlowAnalysis? dataFlowAnalysis = semanticModel.AnalyzeDataFlow(lambda);

			string typeName = $"{lambdaSymbol.ContainingType.Name}_{lambda.Span.Start}".Replace('<', '_').Replace('>', '_');

			using StringWriter stream = new();
			using (IndentedTextWriter writer = new(stream, "\t"))
			{
				writer.WriteLine("// <auto-generated/>");
				writer.WriteLine("#nullable enable");
				writer.WriteLine("#pragma warning disable");
				writer.WriteLine();

				for (SyntaxNode? node = invoke.Parent; node != null; node = node.Parent)
				{
					if (node is CompilationUnitSyntax compilationUnit)
					{
						writer.WriteLine(compilationUnit.Usings);
						writer.WriteLine();

						break;
					}
				}

				if (!classSymbol.ContainingNamespace.IsGlobalNamespace)
				{
					writer.WriteLine($"namespace {classSymbol.ContainingNamespace}");
					writer.WriteLine("{");
					writer.Indent++;
				}

				for (int j = hierarchy.Count - 1; j >= 0; j--)
				{
					ClassDeclarationSyntax classDeclaration = hierarchy[j];

					writer.WriteLine($"partial class {classDeclaration.Identifier.ToString() + classDeclaration.TypeParameterList}");
					writer.WriteLine("{");
					writer.Indent++;
				}

				//TODO: Auto layout?
				writer.WriteLine($"private struct {typeName} : {candidateTarget.ContainingType}");
				writer.WriteLine("{");
				writer.Indent++;

				if (dataFlowAnalysis is not null)
				{
					foreach (ISymbol symbol in dataFlowAnalysis.Captured)
					{
						if (symbol is ILocalSymbol local)
						{
							writer.WriteLine($"public {local.Type} {symbol.Name};");
						}
						else if (symbol is IParameterSymbol param)
						{
							if (param.Name == "this")
							{
								writer.WriteLine($"public {param.Type} _{param.Name};");
							}
							else
							{
								writer.WriteLine($"public {param.Type} {param.Name};");
							}
						}
					}
				}

				writer.WriteLine();
				writer.WriteLine($"public {(lambda.AsyncKeyword != default ? "async " : string.Empty)}{candidateTarget.ReturnType} {candidateTarget.Name}({string.Join(", ", candidateTarget.Parameters)})");

				SyntaxNode lambdaBody = lambda.Body.ReplaceNodes(lambda.Body.DescendantNodes(), (original, modified) =>
				{
					if (modified is ThisExpressionSyntax)
					{
						return SyntaxFactory.IdentifierName("_this");
					}
					else if (modified is InvocationExpressionSyntax inv)
					{
						return TransformCall(original, inv);
					}

					return modified;
				});

				SyntaxNode TransformCall(SyntaxNode original, InvocationExpressionSyntax invoke, bool call = false)
				{
					SymbolInfo invokeTargetInfo = semanticModel.GetSymbolInfo(original);
					if (invokeTargetInfo.Symbol is not IMethodSymbol invokeTarget)
					{
						return invoke;
					}

					(IMethodSymbol? candidateSymbol, Dictionary<int, IMethodSymbol> functionalInterfaces) = FunctionalInterfacesGenerator.FindFunctionalInterfaceTarget(invoke, invokeTarget);
					if (candidateSymbol is null)
					{
						return invoke;
					}

					foreach (KeyValuePair<int, IMethodSymbol> methodSymbol in functionalInterfaces)
					{
						ExpressionSyntax lambda = invoke.ArgumentList.Arguments[methodSymbol.Key].Expression;

						string otherName = $"{lambdaSymbol.ContainingType.Name}_{lambda.Span.Start}".Replace('<', '_').Replace('>', '_');

						invoke = invoke.ReplaceNode(invoke.ArgumentList.Arguments[methodSymbol.Key], invoke.ArgumentList.Arguments[methodSymbol.Key].WithExpression(
							SyntaxFactory.InvocationExpression(
									SyntaxFactory.MemberAccessExpression(
										SyntaxKind.SimpleMemberAccessExpression,
										SyntaxFactory.MemberAccessExpression(
											SyntaxKind.SimpleMemberAccessExpression,
											SyntaxFactory.MemberAccessExpression(
												SyntaxKind.SimpleMemberAccessExpression,
												SyntaxFactory.MemberAccessExpression(
													SyntaxKind.SimpleMemberAccessExpression,
													SyntaxFactory.IdentifierName("System"),
													SyntaxFactory.IdentifierName("Runtime")),
												SyntaxFactory.IdentifierName("CompilerServices")),
											SyntaxFactory.IdentifierName("Unsafe")),
										SyntaxFactory.GenericName(
												SyntaxFactory.Identifier("As"))
											.WithTypeArgumentList(
												SyntaxFactory.TypeArgumentList(
													SyntaxFactory.SeparatedList<TypeSyntax>(new SyntaxNodeOrToken[]
														{
																SyntaxFactory.IdentifierName(typeName),
																SyntaxFactory.Token(SyntaxKind.CommaToken),
																SyntaxFactory.IdentifierName(call ? typeName : otherName)
														})))))
								.WithArgumentList(
									SyntaxFactory.ArgumentList(
										SyntaxFactory.SingletonSeparatedList(
											SyntaxFactory.Argument(
													call
														? SyntaxFactory.IdentifierName("__functionalInterface")
														: SyntaxFactory.ThisExpression())
												.WithRefOrOutKeyword(
													SyntaxFactory.Token(SyntaxKind.RefKeyword)))))));
					}

					return invoke;
				}

				writer.WriteLineNoTabs(lambdaBody.NormalizeWhitespace().ToFullString());

				writer.Indent--;
				writer.WriteLine("}");

				if (declaringMethod is not null)
				{
					IMethodSymbol? methodSymbolInfo = semanticModel.GetDeclaredSymbol(declaringMethod);
					if (methodSymbolInfo is not null)
					{
						SyntaxNode declaringMethodBody = declaringMethod.Body!;

						declaringMethodBody = declaringMethodBody.ReplaceNodes(declaringMethodBody.DescendantNodes(), (original, modified) =>
						{
							if (modified is InvocationExpressionSyntax inv)
							{
								return TransformCall(original, inv, true);
							}
							else if (dataFlowAnalysis is not null)
							{
								if (modified is LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 } localDeclaration)
								{
									bool captured = dataFlowAnalysis.Captured.Any(i => i.Name == localDeclaration.Declaration.Variables[0].Identifier.Text);

									if (captured)
									{
										return SyntaxFactory.ExpressionStatement(
											SyntaxFactory.AssignmentExpression(
												SyntaxKind.SimpleAssignmentExpression,
												SyntaxFactory.MemberAccessExpression(
													SyntaxKind.SimpleMemberAccessExpression,
													SyntaxFactory.IdentifierName("__functionalInterface"),
													SyntaxFactory.IdentifierName(localDeclaration.Declaration.Variables[0].Identifier)),
												localDeclaration.Declaration.Variables[0].Initializer!.Value));
									}
								}
								else if (modified is IdentifierNameSyntax identifier)
								{
									bool captured = dataFlowAnalysis.Captured.Any(i => i.Name == identifier.Identifier.Text);

									if (captured)
									{
										return SyntaxFactory.MemberAccessExpression(
											SyntaxKind.SimpleMemberAccessExpression,
											SyntaxFactory.IdentifierName("__functionalInterface"),
											identifier);
									}
								}
							}

							return modified;
						});

						foreach (SyntaxNode descendantNode in declaringMethodBody.DescendantNodes())
						{
							if (descendantNode is InvocationExpressionSyntax inv && inv.ArgumentList.Arguments
									.Any(a => a.Expression is InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } exp
											  && exp.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax { Identifier.Text: "__functionalInterface" }))
							{
								if (dataFlowAnalysis is not null && inv.Parent is ExpressionStatementSyntax expression)
								{
									List<ExpressionStatementSyntax> initVariables = new();
									foreach (ISymbol symbol in dataFlowAnalysis.DataFlowsIn)
									{
										foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
										{
											SyntaxNode declaration = reference.GetSyntax();
											if (declaration is VariableDeclaratorSyntax)
											{
												continue;
											}

											initVariables.Add(SyntaxFactory.ExpressionStatement(
												SyntaxFactory.AssignmentExpression(
													SyntaxKind.SimpleAssignmentExpression,
													SyntaxFactory.MemberAccessExpression(
														SyntaxKind.SimpleMemberAccessExpression,
														SyntaxFactory.IdentifierName("__functionalInterface"),
														SyntaxFactory.IdentifierName(symbol.Name)),
													SyntaxFactory.IdentifierName(symbol.Name))));
										}
									}

									declaringMethodBody = declaringMethodBody.InsertNodesBefore(expression, initVariables);

									break;
								}
							}
						}

						writer.WriteLine();
						writer.WriteLine($"private {(methodSymbolInfo.IsStatic ? "static " : string.Empty)}{methodSymbolInfo.ReturnType} {methodSymbolInfo.Name}_FunctionalInterface({string.Join(", ", methodSymbolInfo.Parameters)})");
						writer.WriteLine("{");
						writer.Indent++;

						writer.WriteLine($"{typeName} __functionalInterface = default;");

						if (dataFlowAnalysis is not null)
						{
							if (dataFlowAnalysis.Captured.Any(i => i is IParameterSymbol))
							{
								writer.WriteLine($"__functionalInterface._this = this;");
							}
						}

						writer.WriteLineNoTabs(declaringMethodBody.NormalizeWhitespace().ToFullString());

						writer.Indent--;
						writer.WriteLine("}");
						writer.WriteLine();
					}
				}

				for (int j = 0; j < hierarchy.Count; j++)
				{
					writer.Indent--;
					writer.WriteLine("}");
				}

				if (!classSymbol.ContainingNamespace.IsGlobalNamespace)
				{
					writer.Indent--;
					writer.WriteLine("}");
				}
			}

			string hintName = $"{typeName}.g.cs";
			string source = stream.ToString();

			sourceProductionContext.AddSource(hintName, source);
		}
	}
}
