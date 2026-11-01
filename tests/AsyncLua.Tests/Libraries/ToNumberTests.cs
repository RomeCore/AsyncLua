using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// tonumber() follows Lua in both of its shapes: a plain conversion that answers nil when the
/// text is not a numeral, and an integer parse in an explicit base. Neither may raise on bad
/// input - only a missing argument or an impossible base is an error.
/// </summary>
public class ToNumberTests
{
	private static LuaState CreateState() => new LuaState().LoadDefaultLibraries();

	private static LuaValue Call(string arguments)
	{
		var state = CreateState();
		return state.Execute("return tonumber(" + arguments + ")").First;
	}

	private static double Number(string arguments) => Assert.IsType<LuaNumber>(Call(arguments)).Value;

	// ── Plain conversion ────────────────────────────────────────────

	[Theory]
	[InlineData("42", 42.0)]
	[InlineData("3.14", 3.14)]
	[InlineData("-7", -7.0)]
	[InlineData("+7", 7.0)]
	[InlineData("  10  ", 10.0)]
	[InlineData("1e3", 1000.0)]
	[InlineData("1E3", 1000.0)]
	[InlineData("1e-3", 0.001)]
	[InlineData(".5", 0.5)]
	[InlineData("5.", 5.0)]
	[InlineData("0x10", 16.0)]
	[InlineData("0xFF", 255.0)]
	[InlineData("-0x10", -16.0)]
	public void Plain_NumeralText_Converts(string text, double expected)
	{
		Assert.Equal(expected, Number("'" + text + "'"));
	}

	[Theory]
	[InlineData("42")]
	[InlineData("0")]
	[InlineData("-1.25")]
	public void Plain_NumberArgument_IsReturnedAsIs(string literal)
	{
		Assert.Equal(double.Parse(literal, System.Globalization.CultureInfo.InvariantCulture), Number(literal));
	}

	[Theory]
	[InlineData("'not a number'")]
	[InlineData("''")]
	[InlineData("'   '")]
	[InlineData("'1x'")]
	[InlineData("'0x'")]
	[InlineData("'12 34'")]
	[InlineData("'1_000'")]
	[InlineData("'inf'")]
	[InlineData("'nan'")]
	[InlineData("'Infinity'")]
	[InlineData("nil")]
	[InlineData("true")]
	[InlineData("{}")]
	public void Plain_NonNumeral_ReturnsNil(string arguments)
	{
		Assert.IsType<LuaNil>(Call(arguments));
	}

	// ── Explicit base ───────────────────────────────────────────────

	[Theory]
	[InlineData("'10', 2", 2.0)]
	[InlineData("'101', 2", 5.0)]
	[InlineData("'-10', 2", -2.0)]
	[InlineData("'  101  ', 2", 5.0)]
	[InlineData("'10', 8", 8.0)]
	[InlineData("'7', 8", 7.0)]
	[InlineData("'ff', 16", 255.0)]
	[InlineData("'FF', 16", 255.0)]
	[InlineData("'-ff', 16", -255.0)]
	[InlineData("'z', 36", 35.0)]
	[InlineData("'10', 36", 36.0)]
	public void Base_DigitSequence_Converts(string arguments, double expected)
	{
		Assert.Equal(expected, Number(arguments));
	}

	[Theory]
	[InlineData("'8', 8")]
	[InlineData("'12', 2")]
	[InlineData("'0x10', 16")]
	[InlineData("'1.5', 10")]
	[InlineData("'', 10")]
	[InlineData("'-', 10")]
	public void Base_TextIsNotACleanDigitSequence_ReturnsNil(string arguments)
	{
		Assert.IsType<LuaNil>(Call(arguments));
	}

	[Theory]
	[InlineData("'10', 1")]
	[InlineData("'10', 37")]
	[InlineData("'10', 0")]
	public void Base_OutOfRange_Throws(string arguments)
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return tonumber(" + arguments + ")"));
		Assert.Contains("base out of range", ex.Message);
	}

	[Theory]
	[InlineData("10, 10")]
	[InlineData("nil, 10")]
	public void Base_FirstArgumentIsNotAString_Throws(string arguments)
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return tonumber(" + arguments + ")"));
		Assert.Contains("string expected", ex.Message);
	}

	[Fact]
	public void Base_NotAnInteger_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return tonumber('10', 2.5)"));
		Assert.Contains("integer representation", ex.Message);
	}

	[Fact]
	public void Base_NilMeansNoBase()
	{
		// An explicit nil second argument is the same as passing none at all.
		Assert.Equal(10.0, Number("'10', nil"));
	}

	// ── The failure mode that started all this ──────────────────────

	[Fact]
	public void FailingConversion_IsAGuardNotAnError()
	{
		var state = CreateState();
		var result = state.Execute("local n = tonumber('nope') or 0 return n + 1");

		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void MissingArgument_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return tonumber()"));
		Assert.Contains("value expected", ex.Message);
	}
}
