using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// The last argument of a call is one of the positions where Lua expands multiple
/// results: every value the trailing call produced must reach the callee, a producer
/// that yields nothing must leave no argument behind, parentheses must truncate, and
/// only the trailing position may expand.
/// </summary>
public class CallArgumentExpansionTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState().LoadDefaultLibraries();

		// echo(...) hands every argument back as a result.
		state.SetGlobal("echo", new LuaCallbackFunction((ctx, args) => new LuaTuple(args), "echo"));

		// count(...) reports how many arguments it received.
		state.SetGlobal("count", new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(args.Length)), "count"));

		// nothing() returns no values at all.
		state.SetGlobal("nothing", new LuaCallbackFunction((ctx, args) => LuaTuple.Empty, "nothing"));

		return state;
	}

	private static int Number(LuaTuple tuple) => (int)Assert.IsType<LuaNumber>(tuple[0]).Value;

	// ── Trailing call in an argument list ───────────────────────────

	[Fact]
	public void Call_TrailingCallback_ExpandsItsResults()
	{
		var state = CreateState();
		var result = state.Execute("""return count(echo("a", "b", "c"))""");

		Assert.Equal(3, Number(result));
	}

	[Fact]
	public void Call_TrailingBytecodeFunction_ExpandsItsResults()
	{
		var state = CreateState();
		var result = state.Execute("""
			local function three() return 1, 2, 3 end
			return count(three())
			""");

		Assert.Equal(3, Number(result));
	}

	[Fact]
	public void Call_TrailingCallReturningNothing_PassesNoArguments()
	{
		var state = CreateState();
		var result = state.Execute("""return count(nothing())""");

		Assert.Equal(0, Number(result));
	}

	[Fact]
	public void Call_TrailingCallWithArgumentsBefore_KeepsThemAll()
	{
		var state = CreateState();
		var result = state.Execute("""return count("h", echo("a", "b"))""");

		Assert.Equal(3, Number(result));
	}

	[Fact]
	public void Call_TrailingCallWithNilHoles_KeepsEveryPosition()
	{
		// Expanding through the register range must not shorten the list at a nil.
		var state = CreateState();
		var result = state.Execute("""return count(echo(1, nil, 3))""");

		Assert.Equal(3, Number(result));
	}

	[Fact]
	public void Call_NestedTrailingCalls_ExpandLevelByLevel()
	{
		var state = CreateState();
		var result = state.Execute("""return count(echo("a", echo("c", "d")))""");

		Assert.Equal(3, Number(result));
	}

	// ── Only the trailing argument expands ──────────────────────────

	[Fact]
	public void Call_NonTrailingCall_IsTruncatedToOneValue()
	{
		var state = CreateState();
		var result = state.Execute("""return count(echo("a", "b"), "c")""");

		Assert.Equal(2, Number(result));
	}

	[Fact]
	public void Call_TrailingParenthesizedCall_IsTruncatedToOneValue()
	{
		var state = CreateState();
		var result = state.Execute("""return count((echo("a", "b")))""");

		Assert.Equal(1, Number(result));
	}

	// ── Method calls and varargs keep their own paths ───────────────

	[Fact]
	public void Call_MethodWithTrailingCall_PassesSelfAndExpandedArguments()
	{
		var state = CreateState();
		var result = state.Execute("""
			local obj = {}
			function obj:count(...) return select("#", ...) end
			return obj:count(echo("a", "b", "c"))
			""");

		Assert.Equal(3, Number(result));
	}

	[Fact]
	public void Call_TrailingVararg_StillExpands()
	{
		var state = CreateState();
		var result = state.Execute("""
			local function forward(...) return count(...) end
			return forward("a", "b", "c")
			""");

		Assert.Equal(3, Number(result));
	}

	[Fact]
	public void Call_NonTrailingVararg_DoesNotOverwriteFollowingArguments()
	{
		var state = CreateState();
		var result = state.Execute("""
			local function forward(...) return count(..., "c") end
			return forward("a", "b")
			""");

		Assert.Equal(2, Number(result));
	}

	// ── Loopers must not leak one expansion into the next call ──────

	[Fact]
	public void Call_ExpansionDoesNotLeakIntoTheNextStatement()
	{
		var state = CreateState();
		var result = state.Execute("""
			local a = count(echo(1, 2, 3))
			local b = count(echo(4))
			return a, b
			""");

		Assert.Equal(2, result.Count);
		Assert.Equal(3, (int)Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(1, (int)Assert.IsType<LuaNumber>(result[1]).Value);
	}

	// ── The real-world shapes that used to collapse ─────────────────

	[Fact]
	public void Call_FormatReceivesEveryExpandedValue()
	{
		var state = CreateState();
		var result = state.Execute("""return string.format("%s-%s-%s", echo(1, 2, 3))""");

		Assert.Equal("1-2-3", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public void Call_SelectCountsExpandedValues()
	{
		var state = CreateState();
		var result = state.Execute("""return select("#", table.unpack({1, 2, 3}))""");

		Assert.Equal(3, Number(result));
	}
}
