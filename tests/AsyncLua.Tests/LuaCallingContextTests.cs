using AsyncLua.Values;

namespace AsyncLua.Tests;

/// <summary>
/// Tests for manually driving a <see cref="LuaCallingContext"/>: compiling and executing code in
/// the context's own environment, and invoking functions through it.
/// </summary>
public class LuaCallingContextTests
{
	[Fact]
	public void Execute_RunsCodeInContextEnvironment()
	{
		var state = new LuaState();
		var environment = new LuaTable();
		environment.Set("seeded", new LuaNumber(41));
		var context = state.CreateContext(environment);

		var result = context.Execute("return seeded + 1");

		Assert.Equal(42d, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Execute_AssignsIntoContextGlobals()
	{
		var context = new LuaState().CreateContext();

		context.Execute("answer = 7");

		Assert.True(context.Globals.ContainsKey(new LuaString("answer")));
		Assert.Equal(7d, context.Globals.Get("answer").TryToNumber()!.Value);
	}

	[Fact]
	public async Task ExecuteAsync_RunsAwaitCode()
	{
		var context = new LuaState().CreateContext();

		var result = await context.ExecuteAsync(@"
			async function compute() return 5 end
			return await compute()");

		Assert.Equal(5d, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Compile_ReturnsCodeBoundToTheSameContext()
	{
		var context = new LuaState().CreateContext();

		var compiled = context.Compile("return 1 + 2");

		Assert.Same(context, compiled.Context);
		Assert.Equal(3d, Assert.IsType<LuaNumber>(compiled.Execute().First).Value);
	}

	[Fact]
	public void Call_InvokesFunctionWithArguments()
	{
		var context = new LuaState().CreateContext();
		context.Execute("function add(a, b) return a + b end");
		var function = Assert.IsAssignableFrom<LuaFunction>(context.Globals.Get("add"));

		var result = context.Call(function, new LuaNumber(20), new LuaNumber(22));

		Assert.Equal(42d, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public async Task CallAsync_InvokesAsyncFunction()
	{
		var context = new LuaState().CreateContext();
		context.Execute("async function compute() return 99 end");
		var function = Assert.IsAssignableFrom<LuaFunction>(context.Globals.Get("compute"));

		var result = await context.CallAsync(function);

		Assert.Equal(99d, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Compile_NullCode_Throws()
	{
		var context = new LuaState().CreateContext();

		Assert.Throws<ArgumentNullException>(() => context.Compile(null!));
	}
}
