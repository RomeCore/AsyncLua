using AsyncLua.Values;

namespace AsyncLua.Tests.Parsing;

/// <summary>
/// Calls without parentheses: <c>f{...}</c>, <c>f"..."</c>, <c>f[[...]]</c> and the method form after a colon.
/// Every case is checked by executing the code, so a call that parses but passes the wrong argument fails here
/// just as loudly as one that does not parse at all.
/// </summary>
public class CallSugarTests
{
	private static LuaState CreateState() => new LuaState().LoadDefaultLibraries();

	private static LuaValue Run(string code) => CreateState().Execute(code)[0];

	private static double Number(string code) => Assert.IsType<LuaNumber>(Run(code)).Value;

	private static string Text(string code) => Assert.IsType<LuaString>(Run(code)).Value;

	private static bool Flag(string code) => Assert.IsType<LuaBoolean>(Run(code)).Value;

	/// <summary>
	/// Checks whether the parser accepts the code, through <c>load</c>: it answers nil for anything it refuses.
	/// </summary>
	private static bool Parses(string code)
		=> Flag("local f = load([====[" + code + "]====]) return f ~= nil");

	// ── A table constructor as the whole argument list ──────────────

	[Fact]
	public void TableArgument_IsTheOnlyArgument()
	{
		Assert.Equal(1.0, Number("local function f(t) return t.x end return f{ x = 1 }"));
	}

	[Fact]
	public void EmptyTableArgument_IsAnEmptyTable()
	{
		Assert.Equal(0.0, Number("local function f(t) return #t end return f{}"));
	}

	[Fact]
	public void ArrayTableArgument_KeepsItsElements()
	{
		Assert.Equal("2:20", Text("local function f(t) return #t .. ':' .. t[2] end return f{10, 20}"));
	}

	[Fact]
	public void MixedTableArgument_KeepsKeysAndElements()
	{
		Assert.Equal("2c", Text("local function f(t) return #t .. t.k end return f{ 'a', 'b', k = 'c' }"));
	}

	[Fact]
	public void NestedSugarTable_InAField()
	{
		Assert.Equal(6.0, Number(
			"local function g(t) return t.v * 2 end " +
			"local function f(t) return t.inner end " +
			"return f{ inner = g{ v = 3 } }"));
	}

	// ── The method form: self plus the sugar ────────────────────────

	[Fact]
	public void MethodTableArgument_ReceivesSelf()
	{
		Assert.Equal(7.0, Number(
			"local o = { n = 5 } " +
			"function o:add(t) return self.n + t.v end " +
			"return o:add{ v = 2 }"));
	}

	[Fact]
	public void MethodShortStringArgument_ReceivesSelf()
	{
		Assert.Equal("<x>", Text(
			"local o = {} " +
			"function o:tag(s) return '<' .. s .. '>' end " +
			"return o:tag'x'"));
	}

	// ── Strings in both forms ───────────────────────────────────────

	[Fact]
	public void ShortStringArgument_IsTheArgument()
	{
		Assert.Equal("hi!", Text("local function f(s) return s .. '!' end return f'hi'"));
	}

	[Fact]
	public void LongStringArgument_IsTheArgument()
	{
		Assert.Equal("hi", Text("local function f(s) return s end return f[[hi]]"));
	}

	[Fact]
	public void LongStringArgument_DoesNotProcessEscapes()
	{
		// Four characters: a, backslash, n, b - a long bracket is raw text.
		Assert.Equal(4.0, Number("local function f(s) return #s end return f[[a\\nb]]"));
	}

	[Fact]
	public void LevelledLongStringArgument_KeepsInnerBrackets()
	{
		Assert.Equal("x]y", Text("local function f(s) return s end return f[==[x]y]==]"));
	}

	// ── The chain continues after the sugar ─────────────────────────

	[Fact]
	public void IndexAfterSugar_AppliesToTheResult()
	{
		Assert.Equal(1.0, Number("local function f(t) return t end return f{ x = 1 }.x"));
	}

	[Fact]
	public void CallAfterSugar_AppliesToTheResult()
	{
		Assert.Equal(3.0, Number(
			"local function f(t) return function(u) return u.y end end return f{}{ y = 3 }"));
	}

	[Fact]
	public void MethodAfterSugar_AppliesToTheResult()
	{
		Assert.Equal("ABC", Text("local function f(s) return s end return f[[abc]]:upper()"));
	}

	[Fact]
	public void SugarAfterAFieldAccess_IsStillACall()
	{
		Assert.Equal(2.0, Number("local a = { b = function(t) return t.y end } return a.b{ y = 2 }"));
	}

	[Fact]
	public void ParenthesisedSugar_KeepsItsResult()
	{
		Assert.Equal(1.0, Number("local function f(t) return t end return (f{ x = 1 }).x"));
	}

	// ── The sugar binds tighter than every binary operator ──────────

	[Fact]
	public void SugarBindsBeforeConcatenation()
	{
		Assert.Equal("1x", Text("local function f(t) return t.v end return f{ v = 1 } .. 'x'"));
	}

	[Fact]
	public void SugarBindsBeforeLogicalOperators()
	{
		Assert.Equal("yes", Text(
			"local function f(t) return t.v end return f{ v = true } and 'yes' or 'no'"));
	}

	[Fact]
	public void SugarBindsBeforeArithmetic()
	{
		Assert.Equal(5.0, Number("local function f(t) return t.v end return f{ v = 2 } + 3"));
	}

	// ── Statement position ──────────────────────────────────────────

	[Fact]
	public void SugarTableAsStatement_IsACallStatement()
	{
		Assert.Equal(42.0, Number(
			"local seen = 0 " +
			"local function f(t) seen = t.v end " +
			"f{ v = 42 } " +
			"return seen"));
	}

	[Fact]
	public void SugarStringAsStatement_IsACallStatement()
	{
		Assert.Equal("str", Text(
			"local got = '' " +
			"local function f(s) got = s end " +
			"f'str' " +
			"return got"));
	}

	[Fact]
	public void SugarMethodAsStatement_IsACallStatement()
	{
		Assert.Equal(1.0, Number(
			"local hits = 0 " +
			"local o = {} " +
			"function o:hit(t) hits = hits + 1 end " +
			"o:hit{} " +
			"return hits"));
	}

	// ── Whitespace between the name and the sugar is allowed ────────

	[Fact]
	public void SpaceBeforeTheTable_IsStillTheSameCall()
	{
		Assert.Equal(1.0, Number("local function f(t) return t.x end return f { x = 1 }"));
	}

	[Fact]
	public void NewLineBeforeTheTable_IsStillTheSameCall()
	{
		Assert.Equal(1.0, Number("local function f(t) return t.x end return f\n{ x = 1 }"));
	}

	// ── The `{` belongs to the call, never to the block ─────────────

	[Fact]
	public void SugarInIfCondition_BelongsToTheCall()
	{
		Assert.Equal("taken", Text(
			"local function f(t) return t.ok end " +
			"if f{ ok = true } then return 'taken' else return 'not' end"));
	}

	[Fact]
	public void SugarInWhileCondition_BelongsToTheCall()
	{
		Assert.Equal(3.0, Number(
			"local n = 0 " +
			"local function f(t) n = n + 1 return n < 3 end " +
			"while f{} do end " +
			"return n"));
	}

	[Fact]
	public void SugarInGenericFor_ProducesTheIteration()
	{
		Assert.Equal(3.0, Number(
			"local function iter(t) return next, t, nil end " +
			"local sum = 0 " +
			"for k, v in iter{ a = 1, b = 2 } do sum = sum + v end " +
			"return sum"));
	}

	// ── A table in expression position is still just a table ────────

	[Fact]
	public void TableInExpressionPosition_IsNotACall()
	{
		Assert.Equal("table", Text("local x = {} return type(x)"));
	}

	[Fact]
	public void TableAsAStatement_DoesNotParse()
	{
		Assert.False(Parses("{}"), "only a call may stand as an expression statement");
	}

	/// <summary>
	/// Only a prefix expression takes an argument list. A literal takes none, so this is a syntax error in
	/// Lua 5.4 - accepting it turns a typo into a runtime "attempt to call a number value".
	/// </summary>
	[Fact]
	public void TableAfterANumber_DoesNotParse()
	{
		Assert.False(Parses("local x = 1 {}"), "'{}' after a number is not an argument list");
	}

	[Fact]
	public void TableAfterATable_DoesNotParse()
	{
		Assert.False(Parses("local x = {} {}"), "a table constructor is not a prefix expression");
	}

	[Fact]
	public void TableFieldAccessWithoutParentheses_DoesNotParse()
	{
		Assert.False(Parses("local x = {}.x"), "a table constructor is not a prefix expression");
	}
}
