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

	public static void CallFuncWithComplexCaptureAssignment()
	{
		DataHolder param = new(50);
		if (param.Data is not int intParam)
		{
			Assert.Fail("Unreachable");

			return;
		}

		int result = FuncTests.Invoke(() =>
		{
			Assert.Equal(50, intParam);

			return 40;
		});

		Assert.Equal(40, result);
	}

	private sealed record DataHolder(object Data);

	public static void CallGenericFuncWithCapturedInt()
	{
		int param = 50;

		int result = FuncTests.InvokeGeneric(() =>
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

	public static void CallGenericFuncT1WithCapturedInt()
	{
		int param = 50;

		int result = FuncTests.InvokeGenericT1(value =>
		{
			Assert.Equal(50, param);
			Assert.Equal(30, value);

			return 40;
		}, 30);

		Assert.Equal(40, result);
	}

	public static void CallGenericFuncT1AsyncWithCapturedInt()
	{
		int param = 50;

		int result = FuncTests.InvokeGenericT1Async(value =>
		{
			Assert.Equal(50, param);
			Assert.Equal(30, value);

			return 40;
		}, 30).GetAwaiter().GetResult();

		Assert.Equal(40, result);
	}

	public static void CallGenericFuncT1Async2WithCapturedInt()
	{
		int param = 50;

		int result = FuncTests.InvokeGenericT1Async2(value =>
		{
			Assert.Equal(50, param);
			Assert.Equal(30, value);

			return ValueTask.FromResult(40);
		}, 30).GetAwaiter().GetResult();

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

	private static TReturn InvokeGeneric<TAction, TReturn>(TAction action)
		where TAction : IGenericAction<TReturn>
	{
		return action.Invoke();
	}

	private static TReturn InvokeGenericT1<TAction, TReturn>(TAction action, int value)
		where TAction : IGenericActionT1<TReturn>
	{
		return action.Invoke(value);
	}

	private static ValueTask<TReturn> InvokeGenericT1Async<TAction, TReturn>(TAction action, int value)
		where TAction : IGenericActionT1<TReturn>
	{
		return ValueTask.FromResult(action.Invoke(value));
	}

	private static ValueTask<TReturn> InvokeGenericT1Async2<TAction, TReturn>(TAction action, int value)
		where TAction : IGenericActionT1Async<TReturn>
	{
		return action.InvokeAsync(value);
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

	private interface IGenericAction<T>
	{
		public T Invoke();
	}

	private interface IGenericActionT1<T>
	{
		public T Invoke(int value);
	}

	private interface IGenericActionT1Async<T>
	{
		public ValueTask<T> InvokeAsync(int value);
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
	private static int InvokeGeneric<T>(Func<T> action) => throw new NotSupportedException();
	private static int InvokeGenericT1<T>(Func<int, T> action, int value) => throw new NotSupportedException();
	private static ValueTask<T> InvokeGenericT1Async<T>(Func<int, T> action, int value) => throw new NotSupportedException();
	private static ValueTask<T> InvokeGenericT1Async2<T>(Func<int, ValueTask<T>> action, int value) => throw new NotSupportedException();
	private static int InvokeT1(Func<int, int> action) => throw new NotSupportedException();
	private static int InvokeT2(Func<int, int, int> action) => throw new NotSupportedException();
}
