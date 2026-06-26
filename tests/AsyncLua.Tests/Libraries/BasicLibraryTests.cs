using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// Tests for <see cref="Libraries.BasicLibrary"/>: print, type, tostring,
/// tonumber, error, assert, ipairs, pairs, next, select.
/// </summary>
public class BasicLibraryTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		return state;
	}

	// ── type() ─────────────────────────────────────────────────────

	[Fact]
	public void Type_Nil_ReturnsNilString()
	{
		var state = CreateState();
		var result = state.Execute("return type(nil)");
		Assert.Equal("nil", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Type_Boolean_ReturnsBoolean()
	{
		var state = CreateState();
		var result = state.Execute("return type(true), type(false)");
		Assert.Equal("boolean", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("boolean", Assert.IsType<LuaString>(result[1]).Value);
	}

	[Fact]
	public void Type_Number_ReturnsNumber()
	{
		var state = CreateState();
		var result = state.Execute("return type(42), type(3.14)");
		Assert.Equal("number", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("number", Assert.IsType<LuaString>(result[1]).Value);
	}

	[Fact]
	public void Type_String_ReturnsString()
	{
		var state = CreateState();
		var result = state.Execute("return type('hello')");
		Assert.Equal("string", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Type_Table_ReturnsTable()
	{
		var state = CreateState();
		var result = state.Execute("return type({})");
		Assert.Equal("table", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Type_Function_ReturnsFunction()
	{
		var state = CreateState();
		var result = state.Execute("return type(function() end)");
		Assert.Equal("function", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Type_Thread_ReturnsThread()
	{
		var state = CreateState();
		var result = state.Execute(
			"local co = coroutine.create(function() end); return type(co)");
		Assert.Equal("thread", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Type_NoArgs_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return type()");
		Assert.Equal("nil", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── tostring() ─────────────────────────────────────────────────

	[Fact]
	public void ToString_Nil_ReturnsNilString()
	{
		var state = CreateState();
		var result = state.Execute("return tostring(nil)");
		Assert.Equal("nil", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void ToString_Number_ReturnsNumberString()
	{
		var state = CreateState();
		var result = state.Execute("return tostring(42), tostring(3.14)");
		Assert.Equal("42", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("3.14", Assert.IsType<LuaString>(result[1]).Value);
	}

	[Fact]
	public void ToString_String_ReturnsSame()
	{
		var state = CreateState();
		var result = state.Execute("return tostring('hello')");
		Assert.Equal("hello", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void ToString_Boolean_ReturnsBooleanString()
	{
		var state = CreateState();
		var result = state.Execute("return tostring(true), tostring(false)");
		Assert.Equal("true", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("false", Assert.IsType<LuaString>(result[1]).Value);
	}

	[Fact]
	public void ToString_NoArgs_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return tostring()");
		Assert.Equal("nil", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── tonumber() ─────────────────────────────────────────────────

	[Fact]
	public void ToNumber_ValidInt_ReturnsNumber()
	{
		var state = CreateState();
		var result = state.Execute("return tonumber(42)");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ToNumber_ValidString_Converts()
	{
		var state = CreateState();
		var result = state.Execute("return tonumber('3.14')");
		Assert.Equal(3.14, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ToNumber_InvalidString_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return tonumber('not_a_number')");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void ToNumber_Nil_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return tonumber(nil)");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void ToNumber_NoArgs_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return tonumber()");
		Assert.IsType<LuaNil>(result.First);
	}

	// ── is_async() ─────────────────────────────────────────────────

	[Fact]
	public void IsAsync_AsyncNativeFunction_ReturnsTrue()
	{
		var state = CreateState();
		var result = state.Execute("return is_async(async function() end)");
		Assert.Equal(LuaBoolean.True, Assert.IsType<LuaBoolean>(result.First));
	}

	[Fact]
	public void IsAsync_SyncNativeFunction_ReturnsFalse()
	{
		var state = CreateState();
		var result = state.Execute("return is_async(function() end)");
		Assert.Equal(LuaBoolean.False, Assert.IsType<LuaBoolean>(result.First));
	}

	[Fact]
	public void IsAsync_AsyncCallbackFunction_ReturnsTrue()
	{
		var state = CreateState();
		state.SetGlobal("func", new LuaCallbackFunction((ctx, args) => new LuaTuple(LuaNil.Instance), isAsync: true));
		var result = state.Execute("return is_async(func)");
		Assert.Equal(LuaBoolean.True, Assert.IsType<LuaBoolean>(result.First));
	}

	[Fact]
	public void IsAsync_SyncCallbackFunction_ReturnsFalse()
	{
		var state = CreateState();
		state.SetGlobal("func", new LuaCallbackFunction((ctx, args) => new LuaTuple(LuaNil.Instance), isAsync: false));
		var result = state.Execute("return is_async(func)");
		Assert.Equal(LuaBoolean.False, Assert.IsType<LuaBoolean>(result.First));
	}

	[Fact]
	public void IsAsync_NotAFunction_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return is_async(42)");
		Assert.IsType<LuaNil>(result.First);
	}

	// ── assert() ───────────────────────────────────────────────────

	[Fact]
	public void Assert_True_PassesThrough()
	{
		var state = CreateState();
		var result = state.Execute("return assert(true, 'msg')");
		Assert.Equal(LuaBoolean.True, result[0]);
	}

	[Fact]
	public void Assert_TruthyValue_PassesThrough()
	{
		var state = CreateState();
		var result = state.Execute("return assert(42)");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[0]).Value);
	}

	[Fact]
	public void Assert_False_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("assert(false)"));
		Assert.Contains("assertion failed", ex.Message);
	}

	[Fact]
	public void Assert_FalseWithCustomMessage_ThrowsWithMessage()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("assert(false, 'my custom error')"));
		Assert.Contains("my custom error", ex.OriginalMessage);
	}

	[Fact]
	public void Assert_Nil_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("assert(nil)"));
		Assert.NotNull(ex);
	}

	// ── select() ───────────────────────────────────────────────────

	[Fact]
	public void Select_FirstThree_ReturnsFromIndex()
	{
		var state = CreateState();
		var result = state.Execute("return select(2, 'a', 'b', 'c', 'd')");
		Assert.Equal(3, result.Count);
		Assert.Equal("b", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("c", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("d", Assert.IsType<LuaString>(result[2]).Value);
	}

	[Fact]
	public void Select_Hash_ReturnsCountMinusOne()
	{
		var state = CreateState();
		var result = state.Execute("return select('#', 'a', 'b', 'c')");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Select_ZeroArgs_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return select()");
		Assert.Empty(result);
	}

	[Fact]
	public void Select_LastItem_ReturnsLast()
	{
		var state = CreateState();
		var result = state.Execute("return select(-1, 10, 20, 30)");
		Assert.Single(result);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── next() ─────────────────────────────────────────────────────

	[Fact]
	public void Next_EmptyTable_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return next({})");
		// Our implementation returns a single nil for empty tables.
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void Next_FirstKey_ReturnsFirstPair()
	{
		var state = CreateState();
		var result = state.Execute(
			"local t = { a = 1, b = 2 }; return next(t)");
		Assert.True(result.Count >= 2);
		Assert.IsAssignableFrom<LuaString>(result[0]);
		Assert.IsType<LuaNumber>(result[1]);
	}

	[Fact]
	public void Next_WithKey_ReturnsNextKey()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { x = 10, y = 20, z = 30 }
			local k1, v1 = next(t)
			local k2, v2 = next(t, k1)
			return k2 ~= nil and v2 ~= nil and 1 or 0
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── ipairs() ───────────────────────────────────────────────────

	[Fact]
	public void Ipairs_IteratesArray_Partial()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 10, 20, 30, 40 }
			local sum = 0
			for i, v in ipairs(t) do
				sum = sum + v
			end
			return sum
		");
		Assert.Equal(100.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Ipairs_EmptyTable_NoIterations()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local count = 0
			for i, v in ipairs({}) do
				count = count + 1
			end
			return count
		");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Ipairs_StopsAtHole()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 1, 2, nil, 4 }
			local count = 0
			for i, v in ipairs(t) do
				count = count + 1
			end
			return count
		");
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── pairs() ────────────────────────────────────────────────────

	[Fact]
	public void Pairs_IteratesAllKeys()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { a = 1, b = 2, c = 3 }
			local count = 0
			local sum = 0
			for k, v in pairs(t) do
				count = count + 1
				sum = sum + v
			end
			return count, sum
		");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Pairs_EmptyTable_NoIterations()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local count = 0
			for k, v in pairs({}) do
				count = count + 1
			end
			return count
		");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── error() ────────────────────────────────────────────────────

	[Fact]
	public void Error_WithoutArgs_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("error()"));
		Assert.Equal("error", ex.OriginalMessage);
	}

	[Fact]
	public void Error_WithMessage_ThrowsMessage()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("error('something went wrong')"));
		Assert.Contains("something went wrong", ex.Message);
	}

	[Fact]
	public void Error_InTryCatch_IsCaught()
	{
		var state = CreateState();
		var result = state.Execute(@"
			try
				error('caught me')
			catch e do
				return 'caught: ' .. e
			end
		");
		Assert.Equal("caught: caught me", Assert.IsType<LuaString>(result.First).Value);
	}
}
