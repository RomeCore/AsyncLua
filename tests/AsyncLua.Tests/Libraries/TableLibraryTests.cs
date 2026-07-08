using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// Tests for <see cref="Libraries.TableLibrary"/>: insert, remove, concat,
/// sort, pack, unpack, move, create.
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

	[Fact]
	public void Insert_AtEndPosition_Works()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 1, 2, 3 }
			table.insert(t, 4, 99)
			return #t, t[4]
		");
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(99.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Insert_WrongNumberOfArgs_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("table.insert({}, 1, 2, 3)"));
		Assert.Contains("wrong number of arguments", ex.Message);
	}

	[Fact]
	public void Insert_InvalidPosition_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("table.insert({1, 2, 3}, 0, 99)"));
		Assert.Contains("position out of bounds", ex.Message);
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

	[Fact]
	public void Remove_PositionBeyondEnd_ReturnsValue()
	{
		// Lua allows pos = size + 1; returns nil and sets t[pos] = nil (no shift).
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 1, 2, 3 }
			local removed = table.remove(t, 4)
			return removed, #t
		");
		Assert.IsType<LuaNil>(result[0]);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Remove_InvalidPosition_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("table.remove({1, 2, 3}, 0)"));
		Assert.Contains("position out of bounds", ex.Message);
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
	public void Concat_WithHole_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("return table.concat({ 'a', 'b', nil, 'd' })"));
		Assert.Contains("invalid value (nil)", ex.Message);
	}

	[Fact]
	public void Concat_WithRange_JoinsSubset()
	{
		var state = CreateState();
		var result = state.Execute("return table.concat({ 'a', 'b', 'c', 'd' }, '-', 2, 3)");
		Assert.Equal("b-c", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Concat_WithStartOnly_Works()
	{
		var state = CreateState();
		var result = state.Execute("return table.concat({ 'a', 'b', 'c' }, ',', 2)");
		Assert.Equal("b,c", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Concat_EmptyRange_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return table.concat({ 'a', 'b' }, ',', 3, 2)");
		Assert.Equal("", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Concat_Numbers_Works()
	{
		var state = CreateState();
		var result = state.Execute("return table.concat({ 1, 2, 3 }, ', ')");
		Assert.Equal("1, 2, 3", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Concat_InvalidType_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("return table.concat({ { } })"));
		Assert.Contains("invalid value (table)", ex.Message);
	}

	[Fact]
	public void Concat_NilInRange_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("return table.concat({ 'a', nil, 'b' }, ',', 1, 3)"));
		Assert.Contains("invalid value (nil)", ex.Message);
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

	[Fact]
	public void Sort_MixedTypes_DefaultOrder()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 'b', 2, 'a', 1 }
			table.sort(t)
			return t[1], t[2], t[3], t[4]
		");
		// Lua convention: numbers < strings
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal("a", Assert.IsType<LuaString>(result[2]).Value);
		Assert.Equal("b", Assert.IsType<LuaString>(result[3]).Value);
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

	// ── table.unpack ───────────────────────────────────────────────

	[Fact]
	public void Unpack_Basic_ReturnsElements()
	{
		var state = CreateState();
		var result = state.Execute("return table.unpack({ 10, 20, 30 })");
		Assert.Equal(3, result.Count);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Unpack_WithRange_ReturnsSubset()
	{
		var state = CreateState();
		var result = state.Execute("return table.unpack({ 10, 20, 30, 40, 50 }, 2, 4)");
		Assert.Equal(3, result.Count);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(40.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Unpack_EmptyRange_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return table.unpack({ 1, 2, 3 }, 5, 3)");
		Assert.Empty(result);
	}

	[Fact]
	public void Unpack_EmptyTable_ReturnsEmpty()
	{
		var state = CreateState();
		var result = state.Execute("return table.unpack({})");
		Assert.Empty(result);
	}

	[Fact]
	public void Unpack_DefaultEnd_ReturnsAll()
	{
		var state = CreateState();
		var result = state.Execute("return table.unpack({ 10, 20, 30 }, 2)");
		Assert.Equal(2, result.Count);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	// ── table.move ─────────────────────────────────────────────────

	[Fact]
	public void Move_SameTableForward_Works()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 1, 2, 3, 4, 5 }
			table.move(t, 1, 3, 4)
			return t[1], t[2], t[3], t[4], t[5], t[6], t[7]
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[5]).Value);
		Assert.Equal(LuaNil.Instance, result[6]);
	}

	[Fact]
	public void Move_SameTableBackward_Works()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 1, 2, 3, 4, 5 }
			table.move(t, 3, 5, 1)
			return t[1], t[2], t[3], t[4], t[5]
		");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[4]).Value);
	}

	[Fact]
	public void Move_DifferentTables_Copies()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local a = { 10, 20, 30 }
			local b = { 1, 2, 3, 4, 5 }
			local r = table.move(a, 1, 3, 2, b)
			return r[1], r[2], r[3], r[4], r[5], r[6], r[7]
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal(LuaNil.Instance, result[5]);
	}

	[Fact]
	public void Move_ReturnsDestinationTable()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local a = { 1, 2 }
			local b = { 3, 4 }
			local r = table.move(a, 1, 2, 2, b)
			return r == b
		");
		Assert.True(Assert.IsType<LuaBoolean>(result.First).Value);
	}

	[Fact]
	public void Move_EmptyRange_DoesNothing()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = { 1, 2, 3 }
			table.move(t, 5, 3, 1)
			return t[1], t[2], t[3]
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	// ── table.create ───────────────────────────────────────────────

	[Fact]
	public void Create_WithBothSizes_CreatesTable()
	{
		var state = CreateState();
		var result = state.Execute(@"
			local t = table.create(3, 2)
			t[1] = 'a'
			t[2] = 'b'
			t.x = 'hash'
			return t[1], t[2], t.x
		");
		Assert.Equal("a", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("b", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("hash", Assert.IsType<LuaString>(result[2]).Value);
	}

	[Fact]
	public void Create_NoArgs_Throws()
	{
		var state = CreateState();
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("local t = table.create(); return #t"));
		Assert.Contains("expected at least 1 argument", ex.Message);
	}

	[Fact]
	public void Create_OnlySequenceSize_Works()
	{
		var state = CreateState();
		var result = state.Execute("local t = table.create(5); return #t");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}
}
