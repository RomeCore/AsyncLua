using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// setmetatable/getmetatable against the real basic library: a table that has no
/// metatable yet is the normal argument, the table passed in stays the metatable (so
/// getmetatable hands back the very same object and edits made afterwards take effect),
/// and the standard metamethods drive the interpreter from there.
/// </summary>
public class SetMetatableTests
{
	private static LuaState CreateState() => new LuaState().LoadDefaultLibraries();

	private static bool Truth(LuaTuple tuple) => Assert.IsType<LuaBoolean>(tuple[0]).Value;

	[Fact]
	public void SetMetatable_OnFreshTable_Succeeds()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = setmetatable({}, {})
			return type(t), getmetatable(t) ~= nil
			""");

		Assert.Equal("table", Assert.IsType<LuaString>(result[0]).Value);
		Assert.True(Truth(new LuaTuple(result[1])));
	}

	[Fact]
	public void GetMetatable_ReturnsTheSameTable()
	{
		var state = CreateState();
		var result = state.Execute("""
			local mt = {}
			local t = setmetatable({}, mt)
			return getmetatable(t) == mt
			""");

		Assert.True(Truth(result));
	}

	[Fact]
	public void MetamethodAddedAfterSetMetatable_TakesEffect()
	{
		// The table is the metatable, not a snapshot of it.
		var state = CreateState();
		var result = state.Execute("""
			local mt = {}
			local t = setmetatable({}, mt)
			mt.__index = function() return 42 end
			return t.anything
			""");

		Assert.Equal(42, (int)Assert.IsType<LuaNumber>(result[0]).Value);
	}

	[Fact]
	public void MetamethodRemovedAfterSetMetatable_TakesEffect()
	{
		var state = CreateState();
		var result = state.Execute("""
			local mt = { __index = function() return 42 end }
			local t = setmetatable({}, mt)
			local before = t.anything
			mt.__index = nil
			return before, t.anything
			""");

		Assert.Equal(42, (int)Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.IsType<LuaNil>(result[1]);
	}

	[Fact]
	public void Index_MetamethodReceivesTableAndKey()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = setmetatable({}, { __index = function(self, key) return key .. "!" end })
			return t.hi
			""");

		Assert.Equal("hi!", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public void Index_TableChainFallsThrough()
	{
		var state = CreateState();
		var result = state.Execute("""
			local base = { inherited = "base" }
			local t = setmetatable({}, { __index = base })
			return t.inherited
			""");

		Assert.Equal("base", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public void NewIndex_MetamethodInterceptsAssignment()
	{
		var state = CreateState();
		var result = state.Execute("""
			local seen = nil
			local t = setmetatable({}, { __newindex = function(self, key, value) seen = key .. "=" .. value end })
			t.x = 1
			return seen, rawget(t, "x")
			""");

		Assert.Equal("x=1", Assert.IsType<LuaString>(result[0]).Value);
		Assert.IsType<LuaNil>(result[1]);
	}

	[Fact]
	public void Len_MetamethodIsUsed()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = setmetatable({}, { __len = function() return 7 end })
			return #t
			""");

		Assert.Equal(7, (int)Assert.IsType<LuaNumber>(result[0]).Value);
	}

	[Fact]
	public void Call_MetamethodIsUsed()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = setmetatable({}, { __call = function(self, x) return x * 2 end })
			return t(21)
			""");

		Assert.Equal(42, (int)Assert.IsType<LuaNumber>(result[0]).Value);
	}

	[Fact]
	public void ToString_MetamethodIsUsed()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = setmetatable({}, { __tostring = function() return "OBJ" end })
			return tostring(t)
			""");

		Assert.Equal("OBJ", Assert.IsType<LuaString>(result[0]).Value);
	}

	[Fact]
	public void Equal_MetamethodIsUsed()
	{
		var state = CreateState();
		var result = state.Execute("""
			local eq = function() return true end
			local a = setmetatable({}, { __eq = eq })
			local b = setmetatable({}, { __eq = eq })
			return a == b
			""");

		Assert.True(Truth(result));
	}

	[Fact]
	public void Concat_MetamethodIsUsed()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = setmetatable({}, { __concat = function() return "CC" end })
			return t .. "x"
			""");

		Assert.Equal("CC", Assert.IsType<LuaString>(result[0]).Value);
	}

	// ── Removing and protecting ─────────────────────────────────────

	[Fact]
	public void SetMetatableWithNil_RemovesTheMetatable()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = setmetatable({}, { __index = function() return 42 end })
			local before = t.anything
			setmetatable(t, nil)
			return before, t.anything, getmetatable(t)
			""");

		Assert.Equal(42, (int)Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.IsType<LuaNil>(result[1]);
		Assert.IsType<LuaNil>(result[2]);
	}

	[Fact]
	public void ProtectedMetatable_RejectsReplacement()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = setmetatable({}, { __metatable = "locked" })
			local ok, err = pcall(setmetatable, t, {})
			return ok, err, getmetatable(t)
			""");

		Assert.False(Truth(new LuaTuple(result[0])));
		Assert.Contains("protected metatable", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("locked", Assert.IsType<LuaString>(result[2]).Value);
	}

	[Fact]
	public void SetMetatable_OnNonTable_Throws()
	{
		var state = CreateState();
		var result = state.Execute("""
			local ok, err = pcall(setmetatable, 5, {})
			return ok, err
			""");

		Assert.False(Truth(new LuaTuple(result[0])));
		Assert.Contains("table expected", Assert.IsType<LuaString>(result[1]).Value);
	}

	// ── The idiom this unblocks ─────────────────────────────────────

	[Fact]
	public void ClassPattern_MethodsResolveThroughSelf()
	{
		var state = CreateState();
		var result = state.Execute("""
			local Account = {}
			Account.__index = Account

			function Account.new(balance)
				return setmetatable({ balance = balance }, Account)
			end

			function Account:deposit(amount)
				self.balance = self.balance + amount
				return self
			end

			local a = Account.new(10)
			a:deposit(5):deposit(1)
			return a.balance
			""");

		Assert.Equal(16, (int)Assert.IsType<LuaNumber>(result[0]).Value);
	}
}
