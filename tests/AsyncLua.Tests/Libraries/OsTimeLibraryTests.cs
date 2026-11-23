using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// Tests for <see cref="Libraries.OsTimeLibrary"/>: the time-related <c>os</c> functions.
/// </summary>
public class OsTimeLibraryTests
{
	private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	private static LuaState CreateState()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		return state;
	}

	private static double LocalSeconds(int year, int month, int day, int hour, int minute, int second)
	{
		var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
		return Math.Floor((local.ToUniversalTime() - UnixEpoch).TotalSeconds);
	}

	// ── os.time ───────────────────────────────────────────────────

	[Fact]
	public void Time_WithoutArguments_ReturnsCurrentEpochSeconds()
	{
		var state = CreateState();
		var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

		var result = state.Execute("return os.time()");

		var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		var seconds = Assert.IsType<LuaNumber>(result.First).Value;
		Assert.True(seconds >= before && seconds <= after, $"os.time() returned {seconds}, expected {before}..{after}");
	}

	[Fact]
	public void Time_FromTable_ConvertsLocalTime()
	{
		var state = CreateState();
		var result = state.Execute("return os.time{year = 2020, month = 3, day = 15, hour = 10, min = 30, sec = 0}");

		Assert.Equal(LocalSeconds(2020, 3, 15, 10, 30, 0), Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Time_FromTable_DefaultsHourToNoon()
	{
		var state = CreateState();
		var result = state.Execute("return os.time{year = 2020, month = 3, day = 15}");

		Assert.Equal(LocalSeconds(2020, 3, 15, 12, 0, 0), Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Time_FromTable_RollsOverOutOfRangeFields()
	{
		var state = CreateState();
		var rolled = state.Execute("return os.time{year = 2020, month = 1, day = 32, hour = 12}");
		var direct = state.Execute("return os.time{year = 2020, month = 2, day = 1, hour = 12}");

		Assert.Equal(
			Assert.IsType<LuaNumber>(direct.First).Value,
			Assert.IsType<LuaNumber>(rolled.First).Value);
	}

	[Fact]
	public void Time_MissingDay_Throws()
	{
		var state = CreateState();

		Assert.ThrowsAny<Exception>(() => state.Execute("return os.time{year = 2020, month = 1}"));
	}

	// ── os.date ───────────────────────────────────────────────────

	[Fact]
	public void Date_StarT_ReturnsBrokenDownTable()
	{
		var state = CreateState();
		var result = state.Execute("local t = os.date('!*t', 0) return t.year, t.month, t.day, t.hour, t.wday, t.yday");

		Assert.Equal(1970.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[4]).Value); // 1970-01-01 was a Thursday, and Lua numbers Sunday as 1
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[5]).Value);
	}

	[Fact]
	public void Date_FormatString_FormatsUtc()
	{
		var state = CreateState();
		var result = state.Execute("return os.date('!%Y-%m-%d %H:%M:%S', 0)");

		Assert.Equal("1970-01-01 00:00:00", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Date_DefaultFormat_ReturnsString()
	{
		var state = CreateState();
		var result = state.Execute("return type(os.date())");

		Assert.Equal("string", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Date_InvalidSpecifier_Throws()
	{
		var state = CreateState();

		Assert.ThrowsAny<Exception>(() => state.Execute("return os.date('!%Q', 0)"));
	}

	// ── os.clock ──────────────────────────────────────────────────

	[Fact]
	public void Clock_ReturnsNonNegativeCpuSeconds()
	{
		var state = CreateState();
		var result = state.Execute("return os.clock()");

		Assert.True(Assert.IsType<LuaNumber>(result.First).Value > 0);
	}

	// ── os.difftime ───────────────────────────────────────────────

	[Fact]
	public void DiffTime_Subtracts()
	{
		var state = CreateState();
		var result = state.Execute("return os.difftime(100, 40)");

		Assert.Equal(60.0, Assert.IsType<LuaNumber>(result.First).Value);
	}
}
