using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// Multiple-return expressions must only expand in the positions Lua allows
/// (the trailing field of a table constructor, the trailing value of a return
/// list, ...), the expansion must publish exactly the produced values, and
/// parentheses must always truncate to a single value.
/// </summary>
public class TableConstructorExpansionTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState().LoadDefaultLibraries();

		// echo(...) returns every argument - a "user function" with multiple results.
		state.SetGlobal("echo", new LuaCallbackFunction((ctx, args) => new LuaTuple(args), "echo"));

		// nothing() returns no values at all.
		state.SetGlobal("nothing", new LuaCallbackFunction((ctx, args) => LuaTuple.Empty, "nothing"));

		return state;
	}

	private static string?[] Strings(LuaTable table) => table.Entries
		.OrderBy(e => e.Key.TryToNumber())
		.Select(v => v.Value.TryToString())
		.ToArray();

	private static LuaTable Table(LuaTuple tuple, int index) => Assert.IsType<LuaTable>(tuple[index]);

	// ── Trailing call in a table constructor ────────────────────────

	[Fact]
	public void Constructor_TrailingCall_WithNestedCallInArguments_DoesNotLeakRegisters()
	{
		// The trailing call has a nested call among its arguments. The outer call
		// returns one value, so the table must have two fields.
		var state = CreateState();
		var result = state.Execute("""
			local t = { "h", string.format("%s %s", tostring(1), "b") }
			return t
			""");

		Assert.Equal<string?[]>(["h", "1 b"], Strings(Table(result, 0)));
	}

	[Fact]
	public void Constructor_TrailingCall_ExpandsOnlyItsOwnResults()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = { "h", echo("a", "b") }
			return t
			""");

		Assert.Equal<string?[]>(["h", "a", "b"], Strings(Table(result, 0)));
	}

	[Fact]
	public void Constructor_TrailingCallReturningNothing_AddsNoFields()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = { "h", nothing() }
			return t
			""");

		Assert.Equal<string?[]>(["h"], Strings(Table(result, 0)));
	}

	[Fact]
	public void Constructor_NonTrailingCall_IsTruncatedToOneValue()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = { "h", echo("a", "b"), "c" }
			return t
			""");

		Assert.Equal<string?[]>(["h", "a", "c"], Strings(Table(result, 0)));
	}

	[Fact]
	public void Constructor_ExpansionDoesNotLeakIntoFollowingConstructors()
	{
		// The top of an expanded call must not survive into the next constructor:
		// it must describe that one producer, not the highest register ever written.
		var state = CreateState();
		var result = state.Execute("""
			local t1 = { echo("a", "b", "c") }
			local t2 = { "h", string.format("%s %s", tostring(1), "b") }
			local t3 = { "h", echo("x", "y") }
			return t1, t2, t3
			""");

		Assert.Equal<string?[]>(["a", "b", "c"], Strings(Table(result, 0)));
		Assert.Equal<string?[]>(["h", "1 b"], Strings(Table(result, 1)));
		Assert.Equal<string?[]>(["h", "x", "y"], Strings(Table(result, 2)));
	}

	// ── Parentheses truncate ────────────────────────────────────────

	[Fact]
	public void Constructor_TrailingParenthesizedCall_IsTruncatedToOneValue()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t1 = { "h", (echo("a", "b")) }
			local t2 = { "h", (string.format("%s %s", tostring(1), "b")) }
			return t1, t2
			""");

		Assert.Equal<string?[]>(["h", "a"], Strings(Table(result, 0)));
		Assert.Equal<string?[]>(["h", "1 b"], Strings(Table(result, 1)));
	}

	[Fact]
	public void Constructor_DoubleParenthesizedCall_IsTruncatedToOneValue()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = { "h", ((echo("a", "b"))) }
			return t
			""");

		Assert.Equal<string?[]>(["h", "a"], Strings(Table(result, 0)));
	}

	[Fact]
	public void Constructor_TrailingParenthesizedVararg_IsTruncatedToOneValue()
	{
		var state = CreateState();
		var result = state.Execute("""
			local function build(...)
				return { "h", (...) }
			end
			return build("a", "b")
			""");

		Assert.Equal<string?[]>(["h", "a"], Strings(Table(result, 0)));
	}

	[Fact]
	public void Constructor_NonTrailingVararg_IsTruncatedToOneValue()
	{
		var state = CreateState();
		var result = state.Execute("""
			local function build(...)
				return { "h", ..., "c" }
			end
			return build("a", "b")
			""");

		Assert.Equal<string?[]>(["h", "a", "c"], Strings(Table(result, 0)));
	}

	// ── The same rules in a return list ─────────────────────────────

	[Fact]
	public void Return_TrailingCall_WithNestedCallInArguments_DoesNotLeakRegisters()
	{
		var state = CreateState();
		var result = state.Execute("""
			return "h", string.format("%s %s", tostring(1), "b")
			""");

		Assert.Equal(2, result.Count);
		Assert.Equal("h", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("1 b", Assert.IsType<LuaString>(result[1]).Value);
	}

	[Fact]
	public void Return_TrailingCall_ExpandsItsResults()
	{
		var state = CreateState();
		var result = state.Execute("""
			return "h", echo("a", "b")
			""");

		Assert.Equal<string?[]>(["h", "a", "b"], result.Select(v => v.TryToString()).ToArray());
	}

	[Fact]
	public void Return_TrailingParenthesizedCall_IsTruncatedToOneValue()
	{
		var state = CreateState();
		var result = state.Execute("""
			return "h", (echo("a", "b"))
			""");

		Assert.Equal<string?[]>(["h", "a"], result.Select(v => v.TryToString()).ToArray());
	}

	[Fact]
	public void Return_ParenthesizedCallWithNestedTable_KeepsTheTableIntact()
	{
		// The parentheses must truncate the call itself, not a call nested in it.
		var state = CreateState();
		var result = state.Execute("""
			local function build()
				return { "h", echo("a", "b") }
			end
			return (build())
			""");

		Assert.Equal<string?[]>(["h", "a", "b"], Strings(Table(result, 0)));
	}

	// ── Non-trailing vararg in a call argument list ─────────────────

	[Fact]
	public void Call_NonTrailingVararg_DoesNotOverwriteFollowingArguments()
	{
		var state = CreateState();
		var result = state.Execute("""
			local function build(...)
				return echo(..., "c")
			end
			return build("a", "b")
			""");

		Assert.Equal<string?[]>(["a", "c"], result.Select(v => v.TryToString()).ToArray());
	}
}
