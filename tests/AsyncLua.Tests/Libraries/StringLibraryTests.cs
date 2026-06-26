using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// Tests for <see cref="Libraries.StringLibrary"/>: len, sub, byte, char,
/// upper, lower, reverse, rep, format.
/// </summary>
public class StringLibraryTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		return state;
	}

	// ── string.len ─────────────────────────────────────────────────

	[Fact]
	public void Len_EmptyString_ReturnsZero()
	{
		var state = CreateState();
		var result = state.Execute("return string.len('')");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Len_NonEmpty_ReturnsLength()
	{
		var state = CreateState();
		var result = state.Execute("return string.len('hello')");
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Len_WithSpaces_ReturnsLength()
	{
		var state = CreateState();
		var result = state.Execute("return string.len('a b c')");
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── string.sub ─────────────────────────────────────────────────

	[Fact]
	public void Sub_StartToEnd_ReturnsSubstring()
	{
		var state = CreateState();
		var result = state.Execute("return string.sub('hello', 2, 4)");
		Assert.Equal("ell", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Sub_StartOnly_ReturnsToEnd()
	{
		var state = CreateState();
		var result = state.Execute("return string.sub('hello', 3)");
		Assert.Equal("llo", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Sub_FirstChar_ReturnsFirst()
	{
		var state = CreateState();
		var result = state.Execute("return string.sub('hello', 1, 1)");
		Assert.Equal("h", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Sub_LastChar_ReturnsLast()
	{
		var state = CreateState();
		var result = state.Execute("return string.sub('hello', -1)");
		Assert.Equal("o", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Sub_OutOfBounds_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return string.sub('hi', 10)");
		Assert.Equal("", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Sub_NegativeStart_Adjusts()
	{
		var state = CreateState();
		var result = state.Execute("return string.sub('hello', -3, -1)");
		Assert.Equal("llo", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── string.byte ────────────────────────────────────────────────

	[Fact]
	public void Byte_FirstChar_ReturnsFirstByte()
	{
		var state = CreateState();
		var result = state.Execute("return string.byte('ABC')");
		Assert.Equal(65.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Byte_AtPosition_ReturnsByte()
	{
		var state = CreateState();
		var result = state.Execute("return string.byte('ABC', 2)");
		Assert.Equal(66.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Byte_OutOfBounds_ReturnsZero()
	{
		var state = CreateState();
		var result = state.Execute("return string.byte('A', 10)");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── string.char ────────────────────────────────────────────────

	[Fact]
	public void Char_Single_ReturnsCharacter()
	{
		var state = CreateState();
		var result = state.Execute("return string.char(65)");
		Assert.Equal("A", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Char_Multiple_ReturnsConcatenated()
	{
		var state = CreateState();
		var result = state.Execute("return string.char(72, 101, 108, 108, 111)");
		Assert.Equal("Hello", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── string.upper / lower ───────────────────────────────────────

	[Fact]
	public void Upper_MixedCase_ReturnsUpper()
	{
		var state = CreateState();
		var result = state.Execute("return string.upper('Hello World')");
		Assert.Equal("HELLO WORLD", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Upper_AlreadyUpper_Unchanged()
	{
		var state = CreateState();
		var result = state.Execute("return string.upper('HELLO')");
		Assert.Equal("HELLO", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Lower_MixedCase_ReturnsLower()
	{
		var state = CreateState();
		var result = state.Execute("return string.lower('Hello World')");
		Assert.Equal("hello world", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Lower_AlreadyLower_Unchanged()
	{
		var state = CreateState();
		var result = state.Execute("return string.lower('hello')");
		Assert.Equal("hello", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── string.reverse ─────────────────────────────────────────────

	[Fact]
	public void Reverse_Empty_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return string.reverse('')");
		Assert.Equal("", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Reverse_NonEmpty_ReturnsReversed()
	{
		var state = CreateState();
		var result = state.Execute("return string.reverse('hello')");
		Assert.Equal("olleh", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Reverse_Palindrome_Same()
	{
		var state = CreateState();
		var result = state.Execute("return string.reverse('racecar')");
		Assert.Equal("racecar", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── string.rep ─────────────────────────────────────────────────

	[Fact]
	public void Rep_ZeroTimes_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return string.rep('a', 0)");
		Assert.Equal("", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Rep_OneTime_ReturnsSame()
	{
		var state = CreateState();
		var result = state.Execute("return string.rep('x', 1)");
		Assert.Equal("x", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Rep_MultipleTimes_ReturnsRepeated()
	{
		var state = CreateState();
		var result = state.Execute("return string.rep('ab', 3)");
		Assert.Equal("ababab", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Rep_EmptyString_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return string.rep('', 100)");
		Assert.Equal("", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── string.format ──────────────────────────────────────────────

	[Fact]
	public void Format_Simple_ReturnsFormattedString()
	{
		var state = CreateState();
		var result = state.Execute("return string.format('%s %d', 'test', 42)");
		Assert.Equal("test 42", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Format_NoArgs_Throws()
	{
		var state = CreateState();
		Assert.Throws<LuaRuntimeException>(() => state.Execute("return string.format()"));
	}

	[Fact]
	public void Format_Float_Works()
	{
		var state = CreateState();
		var result = state.Execute("return string.format('%.2f', 3.14159)");
		Assert.Equal("3.14", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── Type metatables ────────────────────────────────────────────

	[Fact]
	public void TypeMetatables_Len_Throws()
	{
		var state = CreateState();
		// This should throw because 'len' is not called as method, so 'len' will get empty args
		Assert.Throws<LuaRuntimeException>(() => state.Execute("return 'a b c'.len()"));
	}

	[Fact]
	public void TypeMetatables_Len_MethodStyle()
	{
		var state = CreateState();
		var result = state.Execute("return 'a b c':len()");
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result.First).Value);
	}
}
