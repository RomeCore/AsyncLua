using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// A <c>__call</c> metamethod must behave like a function call: an async one hands the caller a task
/// instead of resolving before the call returns.
/// </summary>
public class CallMetamethodAsyncTests
{
	private static LuaState CreateState(LuaCallbackFunction.AsyncCallbackDelegate handler)
		=> CreateState(new LuaCallbackFunction(handler, "call", isAsync: true));

	private static LuaState CreateState(LuaCallbackFunction.CallbackDelegate handler)
		=> CreateState(new LuaCallbackFunction(handler, "call", isAsync: false));

	private static LuaState CreateState(LuaCallbackFunction call)
	{
		var state = new LuaState().LoadDefaultLibraries();

		var metatable = new LuaMetatable();
		metatable.Set("__call", call);
		state.Globals.Set("obj", new LuaTable { Metatable = metatable });

		return state;
	}

	[Fact]
	public async Task AsyncCallMetamethod_ReturnsATaskThatCanBeAwaited()
	{
		var state = CreateState(async (_, args) =>
		{
			await Task.Yield();
			return new LuaTuple(new LuaString($"called with {args.Length - 1}"));
		});

		var result = await state.ExecuteAsync("return await obj(1, 2)");

		Assert.Equal("called with 2", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public async Task AsyncCallMetamethod_WithoutAwait_YieldsATaskValue()
	{
		var state = CreateState(async (_, _) =>
		{
			await Task.Yield();
			return new LuaTuple(LuaBoolean.True);
		});

		var result = await state.ExecuteAsync("return type(obj(1))");

		Assert.Equal("task", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public async Task AsyncCallMetamethod_PassesTheTargetAsTheFirstArgument()
	{
		var state = CreateState(async (_, args) =>
		{
			await Task.Yield();
			var target = args[0] is LuaTable ? "table" : args[0].TypeName;
			return new LuaTuple(new LuaString(target));
		});

		var result = await state.ExecuteAsync("return await obj()");

		Assert.Equal("table", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public async Task SyncCallMetamethod_IsResolvedByTheCall()
	{
		// A non-async metamethod has no task to hand back, so the call itself produces the values.
		var state = CreateState((_, _) => new LuaTuple(new LuaString("sync")));

		var result = await state.ExecuteAsync("return obj(1)");

		Assert.Equal("sync", Assert.IsType<LuaString>(result[0]).Value);
	}
}
