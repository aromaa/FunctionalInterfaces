using Xunit;

namespace FunctionalInterfaces.TestAssembly;

public static partial class FuncTests
{
	public static void CallFuncWithCapturedInt()
	{
		int param = 50;

		int result = FuncTests.Invoke(() =>
		{
			Assert.Equal(50, param);

			return 40;
		});

		Assert.Equal(40, result);
	}

	public static void CallFuncT1WithCapturedInt()
	{
		int param = 50;

		int result = FuncTests.InvokeT1(x =>
		{
			Assert.Equal(50, param);
			Assert.Equal(3, x);

			return 40;
		});

		Assert.Equal(40, result);
	}

	public static void CallFuncT2WithCapturedInt()
	{
		int param = 50;

		int result = FuncTests.InvokeT2((x, y) =>
		{
			Assert.Equal(50, param);
			Assert.Equal(3, x);
			Assert.Equal(6, y);

			return 40;
		});

		Assert.Equal(40, result);
	}

	private static int Invoke<T>(T action)
		where T : IAction
	{
		return action.Invoke();
	}

	private static int InvokeT1<T>(T action)
		where T : IActionT1
	{
		return action.Invoke(3);
	}

	private static int InvokeT2<T>(T action)
		where T : IActionT2
	{
		return action.Invoke(3, 6);
	}

	private interface IAction
	{
		public int Invoke();
	}

	private interface IActionT1
	{
		public int Invoke(int x);
	}

	private interface IActionT2
	{
		public int Invoke(int x, int y);
	}

	private static int Invoke(Func<int> action) => throw new NotSupportedException();
	private static int InvokeT1(Func<int, int> action) => throw new NotSupportedException();
	private static int InvokeT2(Func<int, int, int> action) => throw new NotSupportedException();
}
