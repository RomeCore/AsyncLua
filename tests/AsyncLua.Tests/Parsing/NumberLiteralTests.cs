using AsyncLua.Values;

namespace AsyncLua.Tests.Parsing;

/// <summary>
/// Number literals: decimal and hexadecimal forms, fractional and exponent notation, in every
/// combination Lua accepts - and nothing it rejects. A literal is checked by executing it, so a
/// silent mis-parse (the wrong value) fails just as loudly as a rejected one.
/// </summary>
public class NumberLiteralTests
{
	private static LuaState CreateState() => new LuaState().LoadDefaultLibraries();

	private static LuaNumber Literal(string literal)
	{
		var state = CreateState();
		var result = state.Execute("return " + literal);
		return Assert.IsType<LuaNumber>(result[0]);
	}

	// ── Decimal, with and without an exponent ───────────────────────

	[Theory]
	[InlineData("1e3", 1000.0)]
	[InlineData("1E3", 1000.0)]
	[InlineData("1e+3", 1000.0)]
	[InlineData("2e0", 2.0)]
	[InlineData("1.5e3", 1500.0)]
	[InlineData("0.5e1", 5.0)]
	[InlineData("1e-3", 0.001)]
	[InlineData("25e-2", 0.25)]
	[InlineData("1e300", 1e300)]
	[InlineData("1e-300", 1e-300)]
	public void DecimalExponent_IsParsed(string literal, double expected)
	{
		Assert.Equal(expected, Literal(literal).Value);
	}

	[Theory]
	[InlineData(".5", 0.5)]
	[InlineData("5.", 5.0)]
	[InlineData("3.0", 3.0)]
	public void DecimalWithoutTheOtherHalfOfThePoint_IsParsed(string literal, double expected)
	{
		Assert.Equal(expected, Literal(literal).Value);
	}

	// ── Hexadecimal ─────────────────────────────────────────────────

	[Theory]
	[InlineData("0x10", 16.0)]
	[InlineData("0xFF", 255.0)]
	[InlineData("0xA", 10.0)]
	public void HexadecimalLiteral_IsParsed(string literal, double expected)
	{
		Assert.Equal(expected, Literal(literal).Value);
	}

	/// <summary>
	/// Hexadecimal floats are part of Lua 5.3+ and are the exact way to spell a power of two.
	/// This lexer stops the literal at <c>p</c> and leaves <c>p4</c> as an identifier, so the
	/// cases stay here as the target behaviour of a gap that is deliberately not closed yet.
	/// </summary>
	[Theory(Skip = "Hexadecimal floats (0x1p4, 0x1.8p1, ...) are not lexed yet")]
	[InlineData("0x1p4", 16.0)]
	[InlineData("0x1p-2", 0.25)]
	[InlineData("0x1.8p1", 3.0)]
	[InlineData("0x.8p1", 1.0)]
	public void HexadecimalFloat_IsParsed(string literal, double expected)
	{
		Assert.Equal(expected, Literal(literal).Value);
	}

	// ── The literal is a number, not a name glued to something ──────

	[Fact]
	public void ExponentLiteralInAnExpression_KeepsItsValue()
	{
		var state = CreateState();
		var result = state.Execute("return 1e3 + 1, 10 / 1e1, #{1e3, 2e0}");

		Assert.Equal(1001.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void ExponentLiteralIsNotAnIdentifier()
	{
		// `1e3` must not swallow the identifier that follows it.
		var state = CreateState();
		var result = state.Execute("local x = 1e3 return x");

		Assert.Equal(1000.0, Assert.IsType<LuaNumber>(result[0]).Value);
	}

	// ── What must NOT compile ───────────────────────────────────────

	[Theory]
	[InlineData("1e")]
	[InlineData("1e+")]
	[InlineData("1e-")]
	[InlineData("1e3e")]
	[InlineData("0x")]
	[InlineData("0x1p")]
	[InlineData("0x1p+")]
	public void MalformedLiteral_DoesNotCompile(string literal)
	{
		var state = CreateState();
		var result = state.Execute("local f = load('return " + literal + "') return f == nil");

		Assert.True(Assert.IsType<LuaBoolean>(result[0]).Value,
			$"'{literal}' should not compile");
	}
}
