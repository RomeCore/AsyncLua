using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// Tests for <see cref="Libraries.MathLibrary"/>: constants and functions.
/// </summary>
public class MathLibraryTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		return state;
	}

	// ── Constants ──────────────────────────────────────────────────

	[Fact]
	public void Pi_IsCorrect()
	{
		var state = CreateState();
		var result = state.Execute("return math.pi");
		Assert.Equal(Math.PI, Assert.IsType<LuaNumber>(result.First).Value, 1e-15);
	}

	[Fact]
	public void Huge_IsInfinity()
	{
		var state = CreateState();
		var result = state.Execute("return math.huge");
		Assert.Equal(double.PositiveInfinity, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void MaxInteger_IsLongMaxValue()
	{
		var state = CreateState();
		var result = state.Execute("return math.maxinteger");
		Assert.Equal((double)long.MaxValue, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void MinInteger_IsLongMinValue()
	{
		var state = CreateState();
		var result = state.Execute("return math.mininteger");
		Assert.Equal((double)long.MinValue, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── Rounding ───────────────────────────────────────────────────

	[Fact]
	public void Floor_Value_RoundsDown()
	{
		var state = CreateState();
		var result = state.Execute("return math.floor(3.7), math.floor(-3.7)");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(-4.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Floor_Integer_ReturnsSelf()
	{
		var state = CreateState();
		var result = state.Execute("return math.floor(42)");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Ceil_Value_RoundsUp()
	{
		var state = CreateState();
		var result = state.Execute("return math.ceil(3.2), math.ceil(-3.2)");
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(-3.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Ceil_Integer_ReturnsSelf()
	{
		var state = CreateState();
		var result = state.Execute("return math.ceil(42)");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── Absolute value ─────────────────────────────────────────────

	[Fact]
	public void Abs_Positive_ReturnsSame()
	{
		var state = CreateState();
		var result = state.Execute("return math.abs(42)");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Abs_Negative_ReturnsPositive()
	{
		var state = CreateState();
		var result = state.Execute("return math.abs(-42)");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Abs_Zero_ReturnsZero()
	{
		var state = CreateState();
		var result = state.Execute("return math.abs(0)");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── Square root ────────────────────────────────────────────────

	[Fact]
	public void Sqrt_Positive_ReturnsSquareRoot()
	{
		var state = CreateState();
		var result = state.Execute("return math.sqrt(144)");
		Assert.Equal(12.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Sqrt_Zero_ReturnsZero()
	{
		var state = CreateState();
		var result = state.Execute("return math.sqrt(0)");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Sqrt_One_ReturnsOne()
	{
		var state = CreateState();
		var result = state.Execute("return math.sqrt(1)");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── Trigonometric ──────────────────────────────────────────────

	[Fact]
	public void Sin_PiHalf_ReturnsOne()
	{
		var state = CreateState();
		var result = state.Execute("return math.sin(math.pi / 2)");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-15);
	}

	[Fact]
	public void Cos_Pi_ReturnsMinusOne()
	{
		var state = CreateState();
		var result = state.Execute("return math.cos(math.pi)");
		Assert.Equal(-1.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-15);
	}

	[Fact]
	public void Tan_Zero_ReturnsZero()
	{
		var state = CreateState();
		var result = state.Execute("return math.tan(0)");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-15);
	}

	[Fact]
	public void Asin_One_ReturnsPiHalf()
	{
		var state = CreateState();
		var result = state.Execute("return math.asin(1)");
		Assert.Equal(Math.PI / 2, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Acos_One_ReturnsZero()
	{
		var state = CreateState();
		var result = state.Execute("return math.acos(1)");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Atan_One_ReturnsPiFour()
	{
		var state = CreateState();
		var result = state.Execute("return math.atan(1)");
		Assert.Equal(Math.PI / 4, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Atan_TwoArgs_WorksAsAtan2()
	{
		var state = CreateState();
		var result = state.Execute("return math.atan(1, 1)");
		Assert.Equal(Math.PI / 4, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Atan2_Positive_ReturnsCorrect()
	{
		var state = CreateState();
		var result = state.Execute("return math.atan2(1, 1)");
		Assert.Equal(Math.PI / 4, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Atan2_ZeroZero_ReturnsZero()
	{
		var state = CreateState();
		var result = state.Execute("return math.atan2(0, 0)");
		// atan2(0, 0) is implementation-defined; typically 0.0 or NaN -- .NET returns 0.0
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── Hyperbolic ─────────────────────────────────────────────────

	[Fact]
	public void Sinh_Zero_ReturnsZero()
	{
		var state = CreateState();
		var result = state.Execute("return math.sinh(0)");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Cosh_Zero_ReturnsOne()
	{
		var state = CreateState();
		var result = state.Execute("return math.cosh(0)");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-15);
	}

	[Fact]
	public void Tanh_Zero_ReturnsZero()
	{
		var state = CreateState();
		var result = state.Execute("return math.tanh(0)");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── Logarithm / exponential ─────────────────────────────────────

	[Fact]
	public void Log_E_ReturnsOne()
	{
		var state = CreateState();
		var result = state.Execute("return math.log(math.exp(1))");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Log_WithBase2()
	{
		var state = CreateState();
		var result = state.Execute("return math.log(8, 2)");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Log_WithBase10()
	{
		var state = CreateState();
		var result = state.Execute("return math.log(1000, 10)");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Log_WithCustomBase()
	{
		var state = CreateState();
		var result = state.Execute("return math.log(27, 3)");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Log10_100_Returns2()
	{
		var state = CreateState();
		var result = state.Execute("return math.log10(100)");
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Exp_1_ReturnsE()
	{
		var state = CreateState();
		var result = state.Execute("return math.exp(1)");
		Assert.Equal(Math.E, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	// ── Min / max ──────────────────────────────────────────────────

	[Fact]
	public void Min_ReturnsSmallest()
	{
		var state = CreateState();
		var result = state.Execute("return math.min(10, 20, 5, 30)");
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Min_SingleArgument_ReturnsIt()
	{
		var state = CreateState();
		var result = state.Execute("return math.min(42)");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Max_ReturnsLargest()
	{
		var state = CreateState();
		var result = state.Execute("return math.max(10, 20, 5, 30)");
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Max_SingleArgument_ReturnsIt()
	{
		var state = CreateState();
		var result = state.Execute("return math.max(42)");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Min_ThrowsOnNoArgs()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return math.min()"));
		Assert.Contains("value expected", ex.OriginalMessage);
	}

	[Fact]
	public void Max_ThrowsOnNoArgs()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return math.max()"));
		Assert.Contains("value expected", ex.OriginalMessage);
	}

	// ── Random ─────────────────────────────────────────────────────

	[Fact]
	public void Random_NoArgs_ReturnsBetween0And1()
	{
		var state = CreateState();
		var result = state.Execute("return math.random()");
		var val = Assert.IsType<LuaNumber>(result.First).Value;
		Assert.InRange(val, 0.0, 1.0);
	}

	[Fact]
	public void Random_WithUpperBound_ReturnsInRange()
	{
		var state = CreateState();
		var result = state.Execute("return math.random(6)");
		var val = Assert.IsType<LuaNumber>(result.First).Value;
		Assert.InRange(val, 1.0, 6.0);
		Assert.Equal(Math.Truncate(val), val); // must be integer
	}

	[Fact]
	public void Random_WithRange_ReturnsInRange()
	{
		var state = CreateState();
		var result = state.Execute("return math.random(10, 20)");
		var val = Assert.IsType<LuaNumber>(result.First).Value;
		Assert.InRange(val, 10.0, 20.0);
		Assert.Equal(Math.Truncate(val), val);
	}

	[Fact]
	public void Random_UpperZero_ReturnsFullInteger()
	{
		var state = CreateState();
		var result = state.Execute("return math.random(0)");
		var val = Assert.IsType<LuaNumber>(result.First).Value;
		Assert.Equal(Math.Truncate(val), val);
	}

	[Fact]
	public void Random_InvalidRange_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return math.random(10, 5)"));
		Assert.Contains("interval is empty", ex.OriginalMessage);
	}

	// ── Conversion ─────────────────────────────────────────────────

	[Fact]
	public void Deg_Radians_Converts()
	{
		var state = CreateState();
		var result = state.Execute("return math.deg(math.pi)");
		Assert.Equal(180.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	[Fact]
	public void Rad_Degrees_Converts()
	{
		var state = CreateState();
		var result = state.Execute("return math.rad(180)");
		Assert.Equal(Math.PI, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	// ── New Lua 5.4 functions ──────────────────────────────────────

	[Fact]
	public void ToInteger_WithIntegerFloat_ReturnsNumber()
	{
		var state = CreateState();
		var result = state.Execute("return math.tointeger(42.0)");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ToInteger_WithFloat_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return math.tointeger(3.14)");
		Assert.Equal(LuaNil.Instance, result.First);
	}

	[Fact]
	public void ToInteger_WithString_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return math.tointeger('hello')");
		Assert.Equal(LuaNil.Instance, result.First);
	}

	[Fact]
	public void Type_WithIntegerFloat_ReturnsInteger()
	{
		var state = CreateState();
		var result = state.Execute("return math.type(42.0)");
		Assert.Equal("integer", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Type_WithFloat_ReturnsFloat()
	{
		var state = CreateState();
		var result = state.Execute("return math.type(3.14)");
		Assert.Equal("float", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Type_WithNonNumber_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return math.type('hello')");
		Assert.Equal(LuaNil.Instance, result.First);
	}

	// ── fmod ───────────────────────────────────────────────────────

	[Fact]
	public void Fmod_Positive_ReturnsRemainder()
	{
		var state = CreateState();
		var result = state.Execute("return math.fmod(10, 3)");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Fmod_Negative_ReturnsRemainder()
	{
		var state = CreateState();
		var result = state.Execute("return math.fmod(-10, 3)");
		Assert.Equal(-1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Fmod_ByZero_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return math.fmod(10, 0)"));
		Assert.Contains("division by zero", ex.OriginalMessage);
	}

	// ── modf ───────────────────────────────────────────────────────

	[Fact]
	public void Modf_Integer_ReturnsSelfAndZero()
	{
		var state = CreateState();
		var result = state.Execute("return math.modf(42)");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Modf_Positive_ReturnsIntAndFrac()
	{
		var state = CreateState();
		var result = state.Execute("return math.modf(3.7)");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(0.7, Assert.IsType<LuaNumber>(result[1]).Value, 1e-15);
	}

	[Fact]
	public void Modf_Negative_ReturnsIntAndFrac()
	{
		var state = CreateState();
		var result = state.Execute("return math.modf(-3.7)");
		Assert.Equal(-3.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(-0.7, Assert.IsType<LuaNumber>(result[1]).Value, 1e-15);
	}

	// ── frexp / ldexp ──────────────────────────────────────────────

	[Fact]
	public void Frexp_Positive_ReturnsMantissaAndExponent()
	{
		var state = CreateState();
		// 12.0 = 0.75 * 2^4
		var result = state.Execute("local m, e = math.frexp(12); return m, e");
		Assert.Equal(0.75, Assert.IsType<LuaNumber>(result[0]).Value, 1e-15);
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Frexp_One_ReturnsHalfAndOne()
	{
		var state = CreateState();
		// 1.0 = 0.5 * 2^1
		var result = state.Execute("local m, e = math.frexp(1); return m, e");
		Assert.Equal(0.5, Assert.IsType<LuaNumber>(result[0]).Value, 1e-15);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Frexp_Zero_ReturnsZeroZero()
	{
		var state = CreateState();
		var result = state.Execute("local m, e = math.frexp(0); return m, e");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Ldexp_RoundTrip()
	{
		var state = CreateState();
		// ldexp(0.75, 4) = 0.75 * 2^4 = 12
		var result = state.Execute("return math.ldexp(0.75, 4)");
		Assert.Equal(12.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-14);
	}

	[Fact]
	public void Frexp_Ldexp_RoundTrip()
	{
		var state = CreateState();
		// math.ldexp(math.frexp(12)) should be close to 12
		var result = state.Execute(@"
			local m, e = math.frexp(12)
			return math.ldexp(m, e)
		");
		Assert.Equal(12.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-14);
	}

	// ── ult ────────────────────────────────────────────────────────

	[Fact]
	public void Ult_LessThan_ReturnsTrue()
	{
		var state = CreateState();
		var result = state.Execute("return math.ult(-1, 1)");
		// -1 as unsigned is greater than 1, so false
		Assert.False(Assert.IsType<LuaBoolean>(result.First).Value);
	}

	[Fact]
	public void Ult_Unsigned_TrueForPositive()
	{
		var state = CreateState();
		var result = state.Execute("return math.ult(1, 2)");
		Assert.True(Assert.IsType<LuaBoolean>(result.First).Value);
	}

	[Fact]
	public void Ult_NonInteger_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return math.ult(1.5, 2)"));
		Assert.Contains("must be an integer", ex.OriginalMessage);
	}

	// ── pow (deprecated) ───────────────────────────────────────────

	[Fact]
	public void Pow_ReturnsPower()
	{
		var state = CreateState();
		var result = state.Execute("return math.pow(2, 10)");
		Assert.Equal(1024.0, Assert.IsType<LuaNumber>(result.First).Value, 1e-10);
	}

	// ── Utility ────────────────────────────────────────────────────

	[Fact]
	public void Sign_Positive_ReturnsOne()
	{
		var state = CreateState();
		var result = state.Execute("return math.sign(42)");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Sign_Negative_ReturnsMinusOne()
	{
		var state = CreateState();
		var result = state.Execute("return math.sign(-42)");
		Assert.Equal(-1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Sign_Zero_ReturnsZero()
	{
		var state = CreateState();
		var result = state.Execute("return math.sign(0)");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Clamp_ValueInRange_ReturnsValue()
	{
		var state = CreateState();
		var result = state.Execute("return math.clamp(5, 0, 10)");
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Clamp_ValueBelowMin_ReturnsMin()
	{
		var state = CreateState();
		var result = state.Execute("return math.clamp(-5, 0, 10)");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Clamp_ValueAboveMax_ReturnsMax()
	{
		var state = CreateState();
		var result = state.Execute("return math.clamp(15, 0, 10)");
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Clamp_MinGreaterThanMax_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return math.clamp(5, 10, 0)"));
		Assert.Contains("min must not exceed max", ex.OriginalMessage);
	}

	// ── Error handling ─────────────────────────────────────────────

	[Fact]
	public void NonNumberArgument_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return math.sqrt('hello')"));
		Assert.Contains("must be a number", ex.OriginalMessage);
	}

	[Fact]
	public void MissingArgument_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() => state.Execute("return math.sqrt()"));
		Assert.Contains("expected", ex.OriginalMessage);
	}
}
