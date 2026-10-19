using AsyncLua.Values;

namespace AsyncLua.Tests.Values;

/// <summary>
/// Tests for the table storage contract: an array part for <c>1..n</c>, an insertion-ordered hash
/// part, the dense-prefix <c>#</c>, tombstones that survive a traversal, and snapshot enumeration.
/// </summary>
public class LuaTableTests
{
	private static LuaState CreateState() => new LuaState().LoadDefaultLibraries();

	// ── Ordering ─────────────────────────────────────────────────────

	[Fact]
	public void Pairs_ArrayPartFirst_ThenHashInInsertionOrder()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = {}
			t[3] = 'c'          -- far index: hash until the array reaches it
			t.name = 'n'
			t[1] = 'a'
			t[2] = 'b'          -- absorbs index 3 into the array part
			t.speed = 5
			local keys = {}
			for k in pairs(t) do keys[#keys + 1] = tostring(k) end
			return table.concat(keys, ',')
			""");

		Assert.Equal("1,2,3,name,speed", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void HashOrder_IsIndependentOfHashCodes()
	{
		var state = CreateState();
		var result = state.Execute("""
			local names = { 'alpha', 'beta', 'gamma', 'delta', 'epsilon' }
			local t = {}
			for _, name in ipairs(names) do t[name] = #name end
			local keys = {}
			for k in pairs(t) do keys[#keys + 1] = k end
			return table.concat(keys, ',')
			""");

		Assert.Equal("alpha,beta,gamma,delta,epsilon", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void KeysAndValues_Snapshot_FollowTheSameOrder()
	{
		var table = new LuaTable();
		table.Set("b", new LuaNumber(2));
		table.Set(2, new LuaNumber(20));   // far index -> hash part
		table.Set(1, new LuaNumber(10));   // absorbs index 2 into the array part
		table.Set("a", new LuaNumber(1));

		Assert.Equal(["1", "2", "b", "a"], table.Keys.Select(k => k.ToString()));
		Assert.Equal(["10", "20", "2", "1"], table.Values.Select(v => v.ToString()));
	}

	[Fact]
	public void Pairs_WithHole_YieldsAllPresentKeys()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = {}
			t[3] = 'c'
			t[1] = 'a'
			local keys = {}
			for k in pairs(t) do keys[#keys + 1] = tostring(k) end
			return table.concat(keys, ',')
			""");

		Assert.Equal("1,3", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── Traversal under mutation ─────────────────────────────────────

	[Fact]
	public void Pairs_DeletingEntriesDuringTraversal_Works()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = { 1, 2, 3, 4, 5, bad = 6 }
			local visited = 0
			for k, v in pairs(t) do
				visited = visited + 1
				if type(v) == 'number' and v % 2 == 0 then t[k] = nil end
			end
			local left = 0
			for k in pairs(t) do left = left + 1 end
			return visited, left
			""");

		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[1]).Value); // 1, 3, 5
	}

	[Fact]
	public void Pairs_DeletingCurrentHashKeyDuringTraversal_Continues()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = { a = 1, b = 2, c = 3 }
			local seen = {}
			for k in pairs(t) do
				seen[#seen + 1] = k
				t[k] = nil
			end
			return table.concat(seen, ','), next(t) == nil and 1 or 0
			""");

		Assert.Equal("a,b,c", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	// ── Length and holes ─────────────────────────────────────────────

	[Fact]
	public void Length_HoleInTheMiddle_IsDensePrefix()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = { 1, 2, 3 }
			t[2] = nil
			return #t
			""");

		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Length_AppendingAfterHole_FillsItAndAbsorbsTheTail()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = {}
			t[1] = 'a'
			t[3] = 'c'
			local before = #t
			t[#t + 1] = 'b'
			return before, #t, t[2]
			""");

		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal("b", Assert.IsType<LuaString>(result[2]).Value);
	}

	[Fact]
	public void FarIndex_DoesNotGrowTheArrayPart()
	{
		var state = CreateState();
		var result = state.Execute("""
			local t = {}
			t[1000000] = 'x'
			local keys = 0
			for k in pairs(t) do keys = keys + 1 end
			return #t, t[1000000], keys
			""");

		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal("x", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	// ── Tombstones and snapshots ─────────────────────────────────────

	[Fact]
	public void Tombstones_AreNotVisibleThroughEnumeration()
	{
		var table = new LuaTable();
		for (int i = 0; i < 1000; i++)
		{
			table.Set("k" + i, new LuaNumber(i));
			if (i % 2 == 0)
				table.Set("k" + i, LuaNil.Instance);
		}

		Assert.Equal(500, table.Count);
		Assert.Equal(500, table.Entries.Count);
		Assert.Equal(500, table.Keys.Count);
		Assert.DoesNotContain(table.Entries, e => e.Value is LuaNil);
	}

	[Fact]
	public void Entries_IsSnapshot_DoesNotChangeAfterMutation()
	{
		var table = new LuaTable();
		table.Set("a", new LuaNumber(1));

		var snapshot = table.Entries;

		table.Set("b", new LuaNumber(2));
		table.Set("a", LuaNil.Instance);

		Assert.Single(snapshot);
		Assert.Equal("a", Assert.IsType<LuaString>(snapshot[0].Key).Value);

		var current = Assert.Single(table.Entries);
		Assert.Equal("b", Assert.IsType<LuaString>(current.Key).Value);
	}

	[Fact]
	public void Clear_ResetsBothParts()
	{
		var table = new LuaTable();
		table.Set(1, new LuaNumber(1));
		table.Set(2, new LuaNumber(2));
		table.Set("x", new LuaNumber(3));

		table.Clear();

		Assert.Equal(0, table.Count);
		Assert.Equal(0, table.Length);
		Assert.Empty(table.Entries);
		Assert.IsType<LuaNil>(table.Get(1));
		Assert.IsType<LuaNil>(table.Get("x"));
	}

	// ── Concurrency ──────────────────────────────────────────────────

	[Fact]
	public async Task Pararun_ConcurrentWritesToSharedTable_AreConsistent()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync("""
			local source = {}
			for i = 1, 40 do source[i] = i end

			local out = {}
			await task.pararun(source, function(v, k)
				out[k] = v * 2
				return v * 2
			end, 4)

			local sum = 0
			for i = 1, 40 do sum = sum + out[i] end
			return sum
			""");

		// 2 * (1 + 2 + ... + 40) = 1640
		Assert.Equal(1640.0, Assert.IsType<LuaNumber>(result.First).Value);
	}
}
