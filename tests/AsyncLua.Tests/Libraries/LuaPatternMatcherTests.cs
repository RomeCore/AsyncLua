using AsyncLua.Libraries;
using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// Tests for <see cref="LuaPatternMatcher"/> used as a host-facing API by code outside the library.
/// </summary>
public class LuaPatternMatcherTests
{
	private static (int Start, int End)[] Matches(string source, string pattern, bool plain = false) =>
		[.. LuaPatternMatcher.EnumerateMatches(source, pattern, 1, plain)];

	[Fact]
	public void EnumerateMatches_FindsEveryPatternMatch()
	{
		(int Start, int End)[] expected = [(1, 2), (3, 5)];
		Assert.Equal(expected, Matches("a1b22", "%d+"));
	}

	[Fact]
	public void EnumerateMatches_PlainDelimiter_FindsLiteralSpans()
	{
		(int Start, int End)[] expected = [(1, 2), (3, 4)];
		Assert.Equal(expected, Matches("a,b,c", ",", plain: true));
	}

	[Fact]
	public void EnumerateMatches_EmptyPattern_ReportsEveryPositionOnceAndStops()
	{
		(int Start, int End)[] expected = [(0, 0), (1, 1), (2, 2), (3, 3)];
		Assert.Equal(expected, Matches("abc", ""));
	}

	[Fact]
	public void EnumerateMatches_PatternMatchingEmpty_ReportsEveryPositionOnceAndStops()
	{
		(int Start, int End)[] expected = [(0, 0), (1, 1), (2, 2), (3, 3)];
		Assert.Equal(expected, Matches("abc", "x*"));
	}

	[Fact]
	public void EnumerateMatches_NoMatch_YieldsNothing()
	{
		Assert.Empty(Matches("abc", "%d+"));
	}

	[Fact]
	public void EnumerateMatches_AnchoredPattern_MatchesAtStartOnly()
	{
		(int Start, int End)[] expected = [(0, 1)];
		Assert.Equal(expected, Matches("abc", "^a"));
	}

	[Fact]
	public void Find_WithCaptures_ReturnsBoundsAndCaptures()
	{
		var result = LuaPatternMatcher.Find("abc123", "(%a+)(%d+)", 1, false);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal("abc", Assert.IsType<LuaString>(result[2]).Value);
		Assert.Equal("123", Assert.IsType<LuaString>(result[3]).Value);
	}

	[Fact]
	public void Match_PatternWithoutSpecials_ReturnsTheLiteralMatch()
	{
		var result = LuaPatternMatcher.Match("a,b", ",", 1);
		Assert.Equal(",", Assert.IsType<LuaString>(result.First).Value);
	}
}
