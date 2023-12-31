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

	private interface IAction
	{
		public void Invoke();
	}

	private static void Invoke(Action action) => throw new NotSupportedException();
	private static void Invoke(Action action, int param) => throw new NotSupportedException();
	private static void Invoke(int param, Action action) => throw new NotSupportedException();
}
