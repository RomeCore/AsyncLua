using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// Tests for <see cref="Libraries.TableLibrary"/>: insert, remove, concat,
/// sort, pack.
/// </summary>
public class TableLibraryTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		return state;
	}

	// ── table.insert ───────────────────────────────────────────────

	[Fact]
	public void Insert_End_Appends()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 1, 2, 3 }
			table.insert(t, 4)
			return #t, t[1], t[2], t[3], t[4]
		");
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[4]).Value);
	}

	[Fact]
	public void Insert_AtPosition_ShiftsRight()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 1, 2, 3 }
			table.insert(t, 2, 99)
			return #t, t[1], t[2], t[3], t[4]
		");
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(99.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[4]).Value);
	}

	[Fact]
	public void Insert_EmptyTable_Works()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = {}
			table.insert(t, 'first')
			return #t, t[1]
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal("first", Assert.IsType<LuaString>(result[1]).Value);
	}

	// ── table.remove ───────────────────────────────────────────────

	[Fact]
	public void Remove_Last_RemovesAndReturns()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 10, 20, 30 }
			local removed = table.remove(t)
			return removed, #t, t[1], t[2], t[3]
		");
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(LuaNil.Instance, result[4]);
	}

	[Fact]
	public void Remove_AtPosition_ShiftsLeft()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 1, 2, 3, 4 }
			local removed = table.remove(t, 2)
			return removed, #t, t[1], t[2], t[3], t[4]
		");
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal(LuaNil.Instance, result[5]);
	}

	[Fact]
	public void Remove_EmptyTable_ReturnsNil()
	{
		var state = CreateState();
		var result = state.Execute("return table.remove({})");
		Assert.IsType<LuaNil>(result.First);
	}

	// ── table.concat ───────────────────────────────────────────────

	[Fact]
	public void Concat_Basic_JoinsWithSeparator()
	{
		var state = CreateState();
		var result = state.Execute("return table.concat({ 'a', 'b', 'c' }, ', ')");
		Assert.Equal("a, b, c", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Concat_NoSeparator_JoinsWithout()
	{
		var state = CreateState();
		var result = state.Execute("return table.concat({ 'x', 'y', 'z' })");
		Assert.Equal("xyz", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Concat_EmptyTable_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return table.concat({})");
		Assert.Equal("", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Concat_StopsAtHole()
	{
		var state = CreateState();
		var result = state.Execute("return table.concat({ 'a', 'b', nil, 'd' })");
		Assert.Equal("ab", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── table.sort ─────────────────────────────────────────────────

	[Fact]
	public void Sort_Numbers_Ascending()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 3, 1, 4, 1, 5, 9, 2, 6 }
			table.sort(t)
			return t[1], t[2], t[3], t[4], t[5], t[6], t[7], t[8]
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[5]).Value);
		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result[6]).Value);
		Assert.Equal(9.0, Assert.IsType<LuaNumber>(result[7]).Value);
	}

	[Fact]
	public void Sort_EmptyTable_NoError()
	{
		var state = CreateState();
		var result = state.Execute("table.sort({}); return 'ok'");
		Assert.Equal("ok", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Sort_SingleElement_NoError()
	{
		var state = CreateState();
		var result = state.Execute("local t = { 42 }; table.sort(t); return t[1]");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Sort_WithComparator_Descending()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 1, 5, 3, 2, 4 }
			table.sort(t, function(a, b) return a > b end)
			return t[1], t[2], t[3], t[4], t[5]
		");
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[4]).Value);
	}

	[Fact]
	public void Sort_Strings_Alphabetical()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 'banana', 'apple', 'cherry', 'date' }
			table.sort(t)
			return t[1], t[2], t[3], t[4]
		");
		Assert.Equal("apple", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("banana", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("cherry", Assert.IsType<LuaString>(result[2]).Value);
		Assert.Equal("date", Assert.IsType<LuaString>(result[3]).Value);
	}

	// ── table.pack ─────────────────────────────────────────────────

	[Fact]
	public void Pack_Varargs_CreatesTable()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = table.pack(10, 20, 30, 40)
			return #t, t[1], t[2], t[3], t[4], t.n
		");
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(40.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[5]).Value);
	}

	[Fact]
	public void Pack_NoArgs_ReturnsEmptyTable()
	{
		var state = CreateState();
		var result = state.Execute("local t = table.pack(); return #t, t.n");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}
}
