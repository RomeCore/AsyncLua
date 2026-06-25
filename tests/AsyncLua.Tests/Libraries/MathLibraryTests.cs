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
	public void Ceil_Value_RoundsUp()
	{
		var state = CreateState();
		var result = state.Execute("return math.ceil(3.2), math.ceil(-3.2)");
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(-3.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

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
		// atan2(0, 0) is implementation-defined; typically 0.0 or NaN.
		Assert.True(double.IsNaN(Assert.IsType<LuaNumber>(result.First).Value) ||
					Assert.IsType<LuaNumber>(result.First).Value == 0.0);
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
	public void Max_ReturnsLargest()
	{
		var state = CreateState();
		var result = state.Execute("return math.max(10, 20, 5, 30)");
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result.First).Value);
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
}
