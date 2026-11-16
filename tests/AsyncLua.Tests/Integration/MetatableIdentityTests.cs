using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// A metatable is an ordinary table, so <c>setmetatable</c> stores the table it was given and
/// <c>getmetatable</c> hands the very same object back - with every key it carries, not only the metamethods.
/// </summary>
public class MetatableIdentityTests
{
	private static LuaState CreateState() => new LuaState().LoadDefaultLibraries();

	private static LuaValue Run(string code) => CreateState().Execute(code)[0];

	private static bool Flag(string code) => Assert.IsType<LuaBoolean>(Run(code)).Value;

	private static string Text(string code) => Assert.IsType<LuaString>(Run(code)).Value;

	[Fact]
	public void GetMetatable_ReturnsTheVeryTableThatWasSet()
	{
		Assert.True(Flag(
			"local mt = { __index = function() return 1 end } " +
			"local t = setmetatable({}, mt) " +
			"return getmetatable(t) == mt"));
	}

	[Fact]
	public void NonMetamethodKeysOnTheMetatable_Survive()
	{
		// Before the refactor only the known events were copied out of the table, everything else was dropped.
		Assert.Equal("hello", Text(
			"local t = setmetatable({}, { tag = 'hello' }) " +
			"return getmetatable(t).tag"));
	}

	[Fact]
	public void MetamethodAddedAfterSetMetatable_TakesEffect()
	{
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(Run(
			"local mt = {} " +
			"local t = setmetatable({}, mt) " +
			"mt.__index = function() return 7 end " +
			"return t.anything")).Value);
	}

	[Fact]
	public void MetamethodRemovedAfterSetMetatable_StopsWorking()
	{
		Assert.Same(LuaNil.Instance, Run(
			"local mt = { __index = function() return 7 end } " +
			"local t = setmetatable({}, mt) " +
			"mt.__index = nil " +
			"return t.anything"));
	}

	[Fact]
	public void MetatableProtection_IsReadFromTheTable()
	{
		// getmetatable answers the __metatable value, and setmetatable then refuses to replace it.
		var state = CreateState();
		var result = state.Execute(
			"local t = setmetatable({}, { __metatable = 'locked' }) " +
			"local ok = pcall(setmetatable, t, {}) " +
			"return getmetatable(t), ok");

		Assert.Equal("locked", Assert.IsType<LuaString>(result[0]).Value);
		Assert.False(Assert.IsType<LuaBoolean>(result[1]).Value);
	}

	[Fact]
	public void TwoTables_ShareOneMetatableObject()
	{
		Assert.True(Flag(
			"local mt = { __index = function() return 1 end } " +
			"local a = setmetatable({}, mt) " +
			"local b = setmetatable({}, mt) " +
			"mt.__index = function() return 2 end " +
			"return a.x == 2 and b.x == 2"));
	}
}
