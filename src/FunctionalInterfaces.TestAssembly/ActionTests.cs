using Xunit;

namespace FunctionalInterfaces.TestAssembly;

public static partial class ActionTests
{
	private static IActionConsumer Instance { get; } = new ActionConsumer();

	public static void CallActionWithCapturedInt()
	{
		int param = 50;

		ActionTests.Invoke(() =>
		{
			Assert.Equal(50, param);
		});
	}

	public static void CallActionWithCapturedIntReferenceOutside()
	{
		int param = 50;

		_ = param;

		ActionTests.Invoke(() =>
		{
			Assert.Equal(50, param);
		});
	}

	public static void CallActionWithCapturedIntTwoTimes()
	{
		int param = 50;

		ActionTests.Invoke(() =>
		{
			Assert.Equal(50, param);
		});

		ActionTests.Invoke(() =>
		{
			Assert.Equal(50, param);
		});
	}

	public static void CallVirtualActionWithCapturedInt()
	{
		int param = 50;

		ActionTests.Instance.Invoke(() =>
		{
			Assert.Equal(50, param);
		});
	}

	public static void CallVirtualActionWithDoubleCapture()
	{
		byte paramB = 50;
		short paramS = 49;
		int paramI = 48;
		long paramL = 47;

		ActionTests.Instance.Invoke(() =>
		{
			Another();

			Assert.Equal(50, paramB);
			Assert.Equal(49, paramS);
			Assert.Equal(48, paramI);
			Assert.Equal(47, paramL);

			void Another()
			{
				//TODO: Capturing rules
				//Assert.Equal(50, paramB);
				//Assert.Equal(49, paramS);
				//Assert.Equal(48, paramI);
				//Assert.Equal(47, paramL);
			}
		});
	}

	public static void CallActionWithCapturedThis()
	{
		new ActionHolder().CaptureThis();
	}

	public static void CallActionWithCapturedThisAndParams()
	{
		new ActionHolder().CaptureThisAndParams();
	}

	public static void CallActionWithCapturedInstanceFieldsAndParams()
	{
		new ActionHolder().CaptureInstanceFieldsAndParams();
	}

	public static void CallActionWithCapturedGenericThisAndParams()
	{
		new ActionHolder<string>().CaptureThisAndParams();
	}

	public static void CallActionWithCapturedGenericRefOnlyThisAndParams()
	{
		new ActionHolderRefOnly<string>().CaptureThisAndParams();
	}

	public static void CallActionWithCapturedGenericConstraintThisAndParams()
	{
		new ActionHolderConstraint<IAction>().CaptureThisAndParams();
	}

	public static void CallActionWithCapturedIntWithParamAfter()
	{
		int param = 50;

		ActionTests.Invoke2(() =>
		{
			Assert.Equal(50, param);
		}, 3);
	}

	public static void CallActionWithCapturedIntWithParamBefore()
	{
		int param = 50;

		ActionTests.Invoke2(3, () =>
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

	public static void CallActionWithCapturedCapturedInt()
	{
		int param = 50;

		ActionTests.Invoke(() =>
		{
			ActionTests.Invoke(() =>
			{
				Assert.Equal(50, param);
			});
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

	private sealed partial class ActionHolder
	{
		private readonly string hey = nameof(hey);

		internal void CaptureThis()
		{
			ActionTests.Invoke(() =>
			{
				Assert.NotNull(this);
			});
		}

		internal void CaptureThisAndParams()
		{
			int param = 50;

			ActionTests.Invoke(() =>
			{
				Assert.NotNull(this);
				Assert.Equal(50, param);
			});
		}

		internal void CaptureInstanceFieldsAndParams()
		{
			int param = 50;

			ActionTests.Invoke(() =>
			{
				Assert.NotNull(this);
				Assert.Equal(this.hey, nameof(this.hey));
				Assert.Equal(50, param);
			});
		}
	}

	private sealed partial class ActionHolder<T>
	{
		internal void CaptureThisAndParams()
		{
			int param = 50;

			ActionTests.Invoke(() =>
			{
				Assert.NotNull(this);
				Assert.Equal(50, param);
			});
		}
	}

	private sealed partial class ActionHolderRefOnly<T>
		where T : class
	{
		internal void CaptureThisAndParams()
		{
			int param = 50;

			ActionTests.Invoke(() =>
			{
				Assert.NotNull(this);
				Assert.Equal(50, param);
			});
		}
	}

	private sealed partial class ActionHolderConstraint<T>
		where T : IAction
	{
		internal void CaptureThisAndParams()
		{
			int param = 50;

			ActionTests.Invoke(() =>
			{
				Assert.NotNull(this);
				Assert.Equal(50, param);
			});
		}
	}

	private static void Invoke<T>(T action)
		where T : IAction
		=> action.Invoke();

	private static void Invoke2<T>(T action, int param)
		where T : IAction
	{
		action.Invoke();

		Assert.Equal(3, param);
	}

	private static void Invoke2<T>(int param, T action)
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

	private interface IActionConsumer
	{
		public void Invoke<T>(T action)
			where T : IAction;

		public void Invoke(Action action);
	}

	private sealed class ActionConsumer : IActionConsumer
	{
		public void Invoke<T>(T action)
			where T : IAction => ActionTests.Invoke(action);

		public void Invoke(Action action) => throw new NotSupportedException();
	}

	private static void Invoke(Action action) => throw new NotSupportedException();
	private static void Invoke2(Action action, int param) => throw new NotSupportedException();
	private static void Invoke2(int param, Action action) => throw new NotSupportedException();
	private static void InvokeT1(Action<int> action) => throw new NotSupportedException();
	private static void InvokeT2(Action<int, int> action) => throw new NotSupportedException();
}
