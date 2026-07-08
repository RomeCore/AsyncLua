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

	// ── string.byte (range) ──────────────────────────────────────────

	[Fact]
	public void Byte_Range_ReturnsMultipleBytes()
	{
		var state = CreateState();
		var result = state.Execute("return string.byte('ABC', 1, 3)");
		Assert.Equal(3, result.Count);
		Assert.Equal(65.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(66.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(67.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Byte_EmptyRange_ReturnsNone()
	{
		var state = CreateState();
		var result = state.Execute("return string.byte('ABC', 3, 2)");
		Assert.Empty(result);
	}

	[Fact]
	public void Byte_NegativeEnd_ReturnsCorrect()
	{
		var state = CreateState();
		var result = state.Execute("return string.byte('Hello', -2, -1)");
		Assert.Equal(2, result.Count);
		Assert.Equal(108.0, Assert.IsType<LuaNumber>(result[0]).Value);  // 'l'
		Assert.Equal(111.0, Assert.IsType<LuaNumber>(result[1]).Value); // 'o'
	}

	// ── string.char (validation) ─────────────────────────────────────

	[Fact]
	public void Char_OutOfRange_Throws()
	{
		var state = CreateState();
		Assert.Throws<LuaRuntimeException>(() => state.Execute("return string.char(256)"));
	}

	[Fact]
	public void Char_Negative_Throws()
	{
		var state = CreateState();
		Assert.Throws<LuaRuntimeException>(() => state.Execute("return string.char(-1)"));
	}

	// ── string.rep (with separator) ──────────────────────────────────

	[Fact]
	public void Rep_WithSeparator_ReturnsJoined()
	{
		var state = CreateState();
		var result = state.Execute("return string.rep('a', 3, ',')");
		Assert.Equal("a,a,a", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Rep_WithEmptySeparator_EqualsNoSep()
	{
		var state = CreateState();
		var withSep = state.Execute("return string.rep('ab', 3, '')");
		var withoutSep = state.Execute("return string.rep('ab', 3)");
		Assert.Equal(withoutSep.First.ToString(), withSep.First.ToString());
	}

	// ── string.find ─────────────────────────────────────────────────

	[Fact]
	public void Find_Simple_ReturnsPositions()
	{
		var state = CreateState();
		var result = state.Execute("return string.find('hello world', 'world')");
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(11.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Find_WithPattern_ReturnsCaptures()
	{
		var state = CreateState();
		var result = state.Execute("return string.find('hello 42 world', '(%d+)')");
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(8.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal("42", Assert.IsType<LuaString>(result[2]).Value);
	}

	[Fact]
	public void Find_NotFound_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return string.find('hello', 'xyz')");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void Find_WithInit_StartsFromPosition()
	{
		var state = CreateState();
		var result = state.Execute("return string.find('aaaa', 'aa', 3)");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[0]).Value);
	}

	[Fact]
	public void Find_Plain_DisablesPattern()
	{
		var state = CreateState();
		// Without plain: '.' matches any char
		var withPattern = state.Execute("return string.find('hello', 'h.')");
		Assert.NotEqual(LuaNil.Instance, withPattern.First);
		// With plain: '.' is literal
		var withPlain = state.Execute("return string.find('h.llo', 'h.', 1, true)");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(withPlain[0]).Value);
	}

	// ── string.match ────────────────────────────────────────────────

	[Fact]
	public void Match_Simple_ReturnsMatch()
	{
		var state = CreateState();
		var result = state.Execute("return string.match('hello 42 world', '%d+')");
		Assert.Equal("42", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Match_WithCaptures_ReturnsCaptures()
	{
		var state = CreateState();
		var result = state.Execute("return string.match('one two three', '(%a+) (%a+)')");
		Assert.Equal("one", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("two", Assert.IsType<LuaString>(result[1]).Value);
	}

	[Fact]
	public void Match_NotFound_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return string.match('hello', '%d+')");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void Match_WithAnchor_OnlyMatchesStart()
	{
		var state = CreateState();
		var found = state.Execute("return string.match('123 abc', '^%d+')");
		Assert.Equal("123", Assert.IsType<LuaString>(found.First).Value);
		var notFound = state.Execute("return string.match('abc 123', '^%d+')");
		Assert.IsType<LuaNil>(notFound.First);
	}

	// ── string.gmatch ───────────────────────────────────────────────

	[Fact]
	public void GMatch_IteratesAllMatches()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = {}
			for w in string.gmatch('one two three', '%a+') do
				t[#t + 1] = w
			end
			return t[1], t[2], t[3]
		");
		Assert.Equal("one", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("two", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("three", Assert.IsType<LuaString>(result[2]).Value);
	}

	[Fact]
	public void GMatch_WithCaptures_ReturnsCaptures()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = {}
			for k, v in string.gmatch('key1=val1 key2=val2', '(%w+)=(%w+)') do
				t[#t + 1] = k .. ':' .. v
			end
			return t[1], t[2]
		");
		Assert.Equal("key1:val1", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("key2:val2", Assert.IsType<LuaString>(result[1]).Value);
	}

	// ── string.gsub ─────────────────────────────────────────────────

	[Fact]
	public void GSub_StringReplacement_ReturnsReplaced()
	{
		var state = CreateState();
		var result = state.Execute("return string.gsub('hello world', '%l+', 'X')");
		Assert.Equal("X X", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value); // 'hello' and 'world'
	}

	[Fact]
	public void GSub_WithCaptures_ReplacesWithRefs()
	{
		var state = CreateState();
		var result = state.Execute("return string.gsub('hello world', '(%l)(%l+)', '%1')");
		Assert.Equal("h w", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public void GSub_WithFunction_ReturnsProcessed()
	{
		var state = CreateState();
		var result = state.Execute(@"
			return string.gsub('hello world', '(%l)(%l+)', function(a, b)
				return string.upper(a) .. b
			end)
		");
		Assert.Equal("Hello World", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public void GSub_WithTable_LooksUpKey()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { hello = 'HELLO', world = 'WORLD' }
			return string.gsub('hello world', '(%a+)', t)
		");
		Assert.Equal("HELLO WORLD", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public void GSub_WithLimit_StopsAfterN()
	{
		var state = CreateState();
		var result = state.Execute("return string.gsub('a a a a', '%a', 'X', 2)");
		Assert.Equal("X X a a", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void GSub_NoMatch_ReturnsOriginal()
	{
		var state = CreateState();
		var result = state.Execute("return string.gsub('hello', '%d', 'X')");
		Assert.Equal("hello", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	// ── string.format (extensions) ───────────────────────────────────

	[Fact]
	public void Format_Octal_Works()
	{
		var state = CreateState();
		var result = state.Execute("return string.format('%o', 255)");
		Assert.Equal("377", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Format_Quoted_Works()
	{
		var state = CreateState();
		var result = state.Execute("return string.format('%q', 'hello world')");
		Assert.Equal("\"hello world\"", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Format_QuotedWithEscapes_EscapesCorrectly()
	{
		var state = CreateState();
		var result = state.Execute("return string.format('%q', 'hello \"world\"')");
		Assert.Equal("\"hello \\\"world\\\"\"", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Format_HexFloat_Works()
	{
		var state = CreateState();
		var result = state.Execute("return string.format('%a', 1.0)");
		// Lua: %a on 1.0 gives "0x1p+0" or similar
		var str = Assert.IsType<LuaString>(result.First).Value;
		Assert.Contains("0x", str);
	}

	[Fact]
	public void Format_WidthAndFlags_Works()
	{
		var state = CreateState();
		var result = state.Execute("return string.format('%05d', 42)");
		Assert.Equal("00042", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Format_LeftAlign_Works()
	{
		var state = CreateState();
		var result = state.Execute("return string.format('%-5d', 42)");
		Assert.Equal("42   ", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Format_Pointer_ReturnsTypeName()
	{
		var state = CreateState();
		// %p should return something (type name in our impl)
		var result = state.Execute("return string.format('%p', 'test')");
		Assert.False(string.IsNullOrEmpty(Assert.IsType<LuaString>(result.First).Value));
	}

	// ── string.pack / packsize / unpack ──────────────────────────────

	[Fact]
	public void Pack_SignedChar_PacksCorrectly()
	{
		var state = CreateState();
		var packed = state.Execute("return string.pack('b', -128)");
		Assert.Equal(1, Assert.IsType<LuaString>(packed.First).Value.Length);
	}

	[Fact]
	public void PackAndUnpack_Roundtrip_Works()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local packed = string.pack('i4', 12345)
			local val = string.unpack('i4', packed)
			return val
		");
		Assert.Equal(12345.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void PackAndUnpack_MultipleValues_Works()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local packed = string.pack('i2f', 42, 3.14)
			local a, b = string.unpack('i2f', packed)
			return a, b
		");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(3.14, Math.Round(Assert.IsType<LuaNumber>(result[1]).Value, 2));
	}

	[Fact]
	public void PackSize_ReturnsCorrectSize()
	{
		var state = CreateState();
		var result = state.Execute("return string.packsize('i4')");
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void PackSize_MultipleTypes_ReturnsTotal()
	{
		var state = CreateState();
		var result = state.Execute("return string.packsize('bhi')");
		// b=1, h=2, i=4 = 7
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Pack_WithString_Roundtrips()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local packed = string.pack('c5', 'hello')
			local val = string.unpack('c5', packed)
			return val
		");
		Assert.Equal("hello", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Unpack_WithPosition_ReturnsNextPosition()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local packed = string.pack('i2i2', 10, 20)
			local a, pos = string.unpack('i2', packed)
			local b = string.unpack('i2', packed, pos)
			return a, b
		");
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	// ── Type metatables ────────────────────────────────────────────

	// ── Additional edge cases ────────────────────────────────────────

	[Fact]
	public void Find_NegativeInit_Works()
	{
		var state = CreateState();
		var result = state.Execute("return string.find('hello', 'l', -2)");
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[0]).Value);
	}

	[Fact]
	public void Find_InitAfterEnd_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return string.find('hello', 'l', 10)");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void Match_WithoutCaptures_ReturnsFullMatch()
	{
		var state = CreateState();
		var result = state.Execute("return string.match('hello 123', '%a+')");
		Assert.Equal("hello", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Find_WithoutCaptures_ReturnsOnlyPositions()
	{
		var state = CreateState();
		var result = state.Execute("return string.find('hello 42', '%d+')");
		Assert.Equal(2, result.Count); // start, end only, no captures
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(8.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void GSub_WithEmptyPattern_ReplacesEveryChar()
	{
		var state = CreateState();
		var result = state.Execute("return string.gsub('ab', '', 'X')");
		// Each position matches empty pattern: before a, between a and b, after b
		Assert.Equal("XaXbX", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public void GSub_WithZeroReference_ReplacesWithFullMatch()
	{
		var state = CreateState();
		var result = state.Execute("return string.gsub('hello world', '%a+', '%0')");
		Assert.Equal("hello world", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public void Sub_IgreaterThanJ_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return string.sub('hello', 4, 2)");
		Assert.Equal("", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]

	public void Char_ZeroTo255_AllValid()
	{
		var state = CreateState();
		var result = state.Execute("return string.char(0, 128, 255)");
		Assert.Equal(3, Assert.IsType<LuaString>(result.First).Value.Length);
	}

	[Fact]
	public void GSub_TableWithMissingKey_KeepsOriginal()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { hello = 'HELLO' }
			return string.gsub('hello world', '(%a+)', t)
		");
		Assert.Equal("HELLO world", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public void Rep_EmptyStringWithSep_ReturnsSeparators()
	{
		var state = CreateState();
		var result = state.Execute("return string.rep('', 3, ',')");
		Assert.Equal(",,", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Rep_NegativeCount_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return string.rep('a', -1)");
		Assert.Equal("", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Format_HexUppercase_Works()
	{
		var state = CreateState();
		var result = state.Execute("return string.format('%#X', 255)");
		Assert.Equal("0XFF", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Format_Infinity_Works()
	{
		var state = CreateState();
		var result = state.Execute("return string.format('%f', 1/0)");
		Assert.Equal("inf", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Pack_UnsignedShort_Roundtrips()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local packed = string.pack('H', 65000)
			local val = string.unpack('H', packed)
			return val
		");
		Assert.Equal(65000.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void PackSize_FloatDouble_ReturnsCorrect()
	{
		var state = CreateState();
		var result = state.Execute("return string.packsize('fd')");
		Assert.Equal(12.0, Assert.IsType<LuaNumber>(result.First).Value); // 4 + 8
	}


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
