using AsyncLua.Libraries;
using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// Tests for the <see cref="LuaLibrary"/> base classes and <see cref="LuaState.LoadDefaultLibraries"/>.
/// </summary>
public class LibrarySystemTests
{
	// ── Loading ────────────────────────────────────────────────────

	[Fact]
	public void LoadDefaultLibraries_AllAvailable()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();

		// Global functions
		Assert.NotEqual(LuaNil.Instance, state.GetGlobal("print"));
		Assert.NotEqual(LuaNil.Instance, state.GetGlobal("type"));
		Assert.NotEqual(LuaNil.Instance, state.GetGlobal("tostring"));
		Assert.NotEqual(LuaNil.Instance, state.GetGlobal("tonumber"));
		Assert.NotEqual(LuaNil.Instance, state.GetGlobal("error"));
		Assert.NotEqual(LuaNil.Instance, state.GetGlobal("assert"));
		Assert.NotEqual(LuaNil.Instance, state.GetGlobal("ipairs"));
		Assert.NotEqual(LuaNil.Instance, state.GetGlobal("pairs"));
		Assert.NotEqual(LuaNil.Instance, state.GetGlobal("next"));
		Assert.NotEqual(LuaNil.Instance, state.GetGlobal("select"));
	}

	[Fact]
	public void LoadDefaultLibraries_MathTableExists()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		var math = state.GetGlobal("math");
		Assert.IsType<LuaTable>(math);
	}

	[Fact]
	public void LoadDefaultLibraries_StringTableExists()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		var str = state.GetGlobal("string");
		Assert.IsType<LuaTable>(str);
	}

	[Fact]
	public void LoadDefaultLibraries_TableModuleExists()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		var table = state.GetGlobal("table");
		Assert.IsType<LuaTable>(table);
	}

	[Fact]
	public void LoadDefaultLibraries_CoroutineTableExists()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		var coroutine = state.GetGlobal("coroutine");
		Assert.IsType<LuaTable>(coroutine);
	}

	[Fact]
	public void StateWithoutLibraries_GlobalTableIsEmpty()
	{
		var state = new LuaState();
		// Only _G should exist.
		var print = state.GetGlobal("print");
		Assert.IsType<LuaNil>(print);
	}

	// ── Fluent chaining ────────────────────────────────────────────

	[Fact]
	public void LoadLibrary_Extension_ReturnsSameState()
	{
		var state = new LuaState();
		var result = state.LoadLibrary(new MathLibrary());
		Assert.Same(state, result);
	}

	[Fact]
	public void LoadLibraries_Multiple_Chains()
	{
		var state = new LuaState()
			.LoadLibrary(new MathLibrary())
			.LoadLibrary(new StringLibrary());
		Assert.IsType<LuaTable>(state.GetGlobal("math"));
		Assert.IsType<LuaTable>(state.GetGlobal("string"));
	}

	// ── Custom library ─────────────────────────────────────────────

	private sealed class TestLibrary : LuaTableBaseLibrary
	{
		public override string Namespace => "testlib";
		protected override void PopulateTable(LuaState state, LuaTable table)
		{
			table.Set(new LuaString("greet"), new LuaCallbackFunction(
				(ctx, args) => new LuaTuple(new LuaString("Hello, " + args[0])),
				"testlib.greet"));
			table.Set(new LuaString("answer"), new LuaNumber(42));
		}
	}

	[Fact]
	public void CustomLibrary_Works()
	{
		var state = new LuaState();
		state.LoadLibrary(new TestLibrary());
		var result = state.Execute("return testlib.greet('World'), testlib.answer");
		Assert.Equal("Hello, World", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	private sealed class GlobalTestLibrary : LuaGlobalBaseLibrary
	{
		public override void Import(LuaState state)
		{
			state.SetGlobal("myVersion", new LuaCallbackFunction(
				(ctx, args) => new LuaTuple(new LuaString("1.0")), "myVersion"));
		}
	}

	[Fact]
	public void CustomGlobalLibrary_Works()
	{
		var state = new LuaState();
		state.LoadLibrary(new GlobalTestLibrary());
		var result = state.Execute("return myVersion()");
		Assert.Equal("1.0", Assert.IsType<LuaString>(result.First).Value);
	}
}
