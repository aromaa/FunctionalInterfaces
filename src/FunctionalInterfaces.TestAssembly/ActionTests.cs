using Xunit;

namespace FunctionalInterfaces.TestAssembly;

public static class ActionTests
{
	public static void CallActionWithCapturedInt()
	{
		int param = 50;

		ActionTests.Invoke(() =>
		{
			Assert.Equal(50, param);
		});
	}

	public static void CallActionWithCapturedIntWithParamAfter()
	{
		int param = 50;

		ActionTests.Invoke(() =>
		{
			Assert.Equal(50, param);
		}, 3);
	}

	public static void CallActionWithCapturedIntWithParamBefore()
	{
		int param = 50;

		ActionTests.Invoke(3, () =>
		{
			Assert.Equal(50, param);
		});
	}

	public static void CallActionT1WithCapturedInt()
	{
		int param = 50;

		ActionTests.InvokeT1(x =>
		{
			Assert.Equal(50, param);
			Assert.Equal(3, x);
		});
	}

	public static void CallActionT2WithCapturedInt()
	{
		int param = 50;

		ActionTests.InvokeT2((x, y) =>
		{
			Assert.Equal(50, param);
			Assert.Equal(3, x);
			Assert.Equal(6, y);
		});
	}

	private static void Invoke<T>(T action)
		where T : IAction
		=> action.Invoke();

	private static void Invoke<T>(T action, int param)
		where T : IAction
	{
		action.Invoke();

		Assert.Equal(3, param);
	}

	private static void Invoke<T>(int param, T action)
		where T : IAction
	{
		action.Invoke();

		Assert.Equal(3, param);
	}

	private static void InvokeT1<T>(T action)
		where T : IActionT1
	{
		action.Invoke(3);
	}

	private static void InvokeT2<T>(T action)
		where T : IActionT2
	{
		action.Invoke(3, 6);
	}

	private interface IAction
	{
		public void Invoke();
	}

	private interface IActionT1
	{
		public void Invoke(int x);
	}

	private interface IActionT2
	{
		public void Invoke(int x, int y);
	}

	private static void Invoke(Action action) => throw new NotSupportedException();
	private static void Invoke(Action action, int param) => throw new NotSupportedException();
	private static void Invoke(int param, Action action) => throw new NotSupportedException();
	private static void InvokeT1(Action<int> action) => throw new NotSupportedException();
	private static void InvokeT2(Action<int, int> action) => throw new NotSupportedException();
}
