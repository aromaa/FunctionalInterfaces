# FunctionalInterfaces

Allows lambdas to implement interfaces and to target generic methods to create zero allocation capturing lambdas. The lambda can target interfaces that implement single virtual method and equal parameters.

## Example
```csharp
interface IAction
{
    //Single method the lambda can implement
	void Invoke();
}

static class Consumer
{
    //The actual lambda target, generic to allow struct specialization
	static void Run<T>(T action)
		where T : IAction //Generic constraint to specify the interface
	{
		action.Invoke();
	}

    //C# doesn't support the syntax so use dummy method
	static void Run(Action a) => a(); //Or you can throw to catch any problems
}

//Partial class to allow source generator to do its work
static partial class Sample
{
	static void Call()
	{
		int param = 10;

        //Targets actually Consumer.Run<T>
		Consumer.Run(() =>
		{
			Console.WriteLine($"First: {param}");

			param = 20;

            //Targets actually Consumer.Run<T>
			Consumer.Run(() =>
			{
				Console.WriteLine($"Second: {param}");
			});
		});
	}
}
```

The generated code ends up looking something like the following:
```csharp
static class Sample
{
	static void Call()
	{
		Call_35_7A source = default(Call_35_7A);
		source.param = 10;
		Consumer.Run(Unsafe.As<Call_35_7A, Call_35_7A>(ref source));
	}
	
	private struct Call_35_7A : IAction
	{
		public int param;

		public void Invoke()
		{
			Console.WriteLine($"First: {param}");
			param = 20;
			Consumer.Run(Unsafe.As<Call_35_7A, Call_35_D4>(ref this));
		}
	}

	private struct Call_35_D4 : IAction
	{
		public int param;

		public void Invoke()
		{
			Console.WriteLine($"Second: {param}");
		}
	}
}
```