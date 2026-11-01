using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// A Lua error is a value, not a message: <c>pcall</c>, <c>xpcall</c> and <c>try</c>/<c>catch</c>
/// hand back exactly the object that was raised, <c>error</c>'s level decides whether a string
/// message carries the source position, and a .NET exception escaping a library is a runtime
/// error rather than an interpreter teardown.
/// </summary>
public class ErrorModelTests
{
	private static LuaState CreateState() => new LuaState().LoadDefaultLibraries();

	private static string Text(LuaValue value) => Assert.IsType<LuaString>(value).Value;

	private static bool Flag(LuaValue value) => Assert.IsType<LuaBoolean>(value).Value;

	// ── pcall hands back the value ──────────────────────────────────

	[Fact]
	public void Pcall_TableError_ReturnsTheTable()
	{
		var state = CreateState();
		var result = state.Execute("""
			local ok, err = pcall(error, { code = 7 })
			return ok, type(err), err.code
			""");

		Assert.False(Flag(result[0]));
		Assert.Equal("table", Text(result[1]));
		Assert.Equal(7, (int)Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Pcall_ErrorWithoutArguments_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("""
			local ok, err = pcall(error)
			return ok, type(err)
			""");

		Assert.False(Flag(result[0]));
		Assert.Equal("nil", Text(result[1]));
	}

	[Fact]
	public void Pcall_NumberError_ReturnsTheNumber()
	{
		var state = CreateState();
		var result = state.Execute("""
			local ok, err = pcall(error, 42)
			return ok, type(err), err
			""");

		Assert.False(Flag(result[0]));
		Assert.Equal("number", Text(result[1]));
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Pcall_Success_StillReturnsTheValues()
	{
		var state = CreateState();
		var result = state.Execute("""
			local ok, a, b = pcall(function() return 1, 2 end)
			return ok, a, b
			""");

		Assert.True(Flag(result[0]));
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	// ── error level ─────────────────────────────────────────────────

	[Fact]
	public void Error_LevelZero_KeepsTheMessageBare()
	{
		var state = CreateState();
		var result = state.Execute("""
			local _, err = pcall(function() error("boom", 0) end)
			return err
			""");

		Assert.Equal("boom", Text(result[0]));
	}

	[Fact]
	public void Error_DefaultLevel_CarriesThePosition()
	{
		var state = CreateState();
		var result = state.Execute("""
			local _, err = pcall(function() error("boom") end)
			return err
			""");

		var message = Text(result[0]);
		Assert.Contains("boom", message);
		Assert.NotEqual("boom", message);
	}

	[Fact]
	public void Error_NonStringValue_IsNotPositioned()
	{
		var state = CreateState();
		var result = state.Execute("""
			local _, err = pcall(function() error({ code = 1 }, 2) end)
			return type(err), err.code
			""");

		Assert.Equal("table", Text(result[0]));
		Assert.Equal(1, (int)Assert.IsType<LuaNumber>(result[1]).Value);
	}

	// ── assert raises its message as-is ─────────────────────────────

	[Fact]
	public void Assert_TableMessage_IsRaisedAsTable()
	{
		var state = CreateState();
		var result = state.Execute("""
			local ok, err = pcall(assert, false, { code = 3 })
			return ok, type(err), err.code
			""");

		Assert.False(Flag(result[0]));
		Assert.Equal("table", Text(result[1]));
		Assert.Equal(3, (int)Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Assert_WithoutMessage_UsesTheDefaultText()
	{
		var state = CreateState();
		var result = state.Execute("""
			local _, err = pcall(assert, false)
			return err
			""");

		Assert.Contains("assertion failed", Text(result[0]));
	}

	[Fact]
	public void Assert_WithoutArguments_Throws()
	{
		var state = CreateState();
		var result = state.Execute("""
			local ok, err = pcall(assert)
			return ok, err
			""");

		Assert.False(Flag(result[0]));
		Assert.Contains("value expected", Text(result[1]));
	}

	// ── xpcall handler sees the value ───────────────────────────────

	[Fact]
	public void Xpcall_HandlerReceivesTheValue()
	{
		var state = CreateState();
		var result = state.Execute("""
			local ok, handled = xpcall(
				function() error({ code = 9 }) end,
				function(e) return e.code end)
			return ok, handled
			""");

		Assert.False(Flag(result[0]));
		Assert.Equal(9, (int)Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Xpcall_HandlerSeesThePositionedMessage()
	{
		var state = CreateState();
		var result = state.Execute("""
			local _, handled = xpcall(function() error("boom") end, function(e) return e end)
			return handled
			""");

		Assert.Contains("boom", Text(result[0]));
	}

	// ── try / catch / throw keep values ─────────────────────────────

	[Fact]
	public void Throw_Table_IsCaughtAsTable()
	{
		var state = CreateState();
		var result = state.Execute("""
			try
				throw { code = 11 }
			catch e do
				return type(e), e.code
			end
			""");

		Assert.Equal("table", Text(result[0]));
		Assert.Equal(11, (int)Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Throw_String_KeepsTheMessageBare()
	{
		// catch binds the value as it was raised; the position belongs to pcall's rendering.
		var state = CreateState();
		var result = state.Execute("""
			try
				throw "boom"
			catch e do
				return e
			end
			""");

		Assert.Equal("boom", Text(result[0]));
	}

	[Fact]
	public void Throw_Number_IsCaughtAsNumber()
	{
		var state = CreateState();
		var result = state.Execute("""
			try
				throw 5
			catch e do
				return type(e), e
			end
			""");

		Assert.Equal("number", Text(result[0]));
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Error_Table_InsideTry_IsCaughtAsTable()
	{
		var state = CreateState();
		var result = state.Execute("""
			try
				error({ code = 12 })
			catch e do
				return type(e), e.code
			end
			""");

		Assert.Equal("table", Text(result[0]));
		Assert.Equal(12, (int)Assert.IsType<LuaNumber>(result[1]).Value);
	}

	// ── a .NET exception is a runtime error ─────────────────────────

	[Fact]
	public void Pcall_CatchesHostException_FromACallback()
	{
		// math.random(-1) throws ArgumentOutOfRangeException, which is not a Lua error.
		var state = CreateState();
		var result = state.Execute("""
			local ok, err = pcall(math.random, -1)
			return ok, type(err), err
			""");

		Assert.False(Flag(result[0]));
		Assert.Equal("string", Text(result[1]));
		Assert.False(string.IsNullOrWhiteSpace(Text(result[2])));
	}

	[Fact]
	public void TryCatch_CatchesHostException()
	{
		// A .NET exception from a library callback is a Lua error like any other, so the
		// language-level try/catch must see it - not only pcall.
		var state = CreateState();
		var result = state.Execute("""
			try
				return math.random(-1)
			catch e do
				return type(e), e
			end
			""");

		Assert.Equal("string", Text(result[0]));
		Assert.False(string.IsNullOrWhiteSpace(Text(result[1])));
	}

	[Fact]
	public void TryCatch_CatchesHostException_FromNestedFunction()
	{
		var state = CreateState();
		var result = state.Execute("""
			local function inner() return math.random(-1) end
			local function outer() return inner() end

			try
				outer()
				return "no error"
			catch e do
				return "caught", type(e)
			end
			""");

		Assert.Equal("caught", Text(result[0]));
		Assert.Equal("string", Text(result[1]));
	}

	[Fact]
	public void Pcall_CatchesHostException_FromBytecode()
	{
		var state = CreateState();
		var result = state.Execute("""
			local ok, err = pcall(function() return math.random(-1) end)
			return ok, type(err)
			""");

		Assert.False(Flag(result[0]));
		Assert.Equal("string", Text(result[1]));
	}
}
