using System.Reflection;
using Fody;
using FunctionalInterfaces.Fody;
using FunctionalInterfaces.TestAssembly;

namespace FunctionalInterfaces.Tests;

public class WeaverTests
{
	private static readonly TestResult result;

	static WeaverTests()
	{
		ModuleWeaver weaver = new();

		WeaverTests.result = weaver.ExecuteTestRun("FunctionalInterfaces.TestAssembly.dll");
	}

	[Theory]
	[MemberData(nameof(WeaverTests.GetStaticMethods), parameters: typeof(ActionTests))]
	public void Test(string methodName)
	{
		WeaverTests.CreateDelegate<Action>(typeof(ActionTests), methodName)();
	}

	private static T CreateDelegate<T>(Type type, string methodName)
		where T : Delegate
	{
		return result.Assembly.GetType(type.FullName!)!.GetMethod(methodName)!.CreateDelegate<T>();
	}

	public static IEnumerable<object[]> GetStaticMethods(Type data)
	{
		foreach (MethodInfo method in data.GetMethods(BindingFlags.Static | BindingFlags.Public))
		{
			yield return new object[] { method.Name };
		}
	}
}
