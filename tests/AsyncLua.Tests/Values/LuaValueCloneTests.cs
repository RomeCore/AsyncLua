using AsyncLua.Values;

namespace AsyncLua.Tests.Values;

/// <summary>
/// Deep and shallow cloning after a metatable became an ordinary table: a metatable may now point back at the
/// value it belongs to, and one table may be reachable through several paths, so the clone has to keep the
/// graph's shape without walking a cycle forever.
/// </summary>
public class LuaValueCloneTests
{
	[Fact]
	public void DeepClone_SelfReferentialTable_PointsAtItsClone()
	{
		var table = new LuaTable();
		table.Set("self", table);

		var clone = table.DeepClone();

		Assert.NotSame(table, clone);
		Assert.Same(clone, clone.Get("self"));
	}

	[Fact]
	public void DeepClone_MetatablePointingBackAtItsValue_DoesNotRecurse()
	{
		// The cycle the old _G skip in DeepClone used to paper over.
		var metatable = new LuaTable();
		var value = new LuaTable { Metatable = metatable };
		metatable.Set(LuaMetatableEvent.Name, new LuaString("owner"));
		metatable.Set("owner", value);

		var clone = value.DeepClone();

		Assert.NotSame(value, clone);
		Assert.NotNull(clone.Metatable);
		Assert.Same(clone, clone.Metatable.Get("owner"));
		Assert.Equal("owner", Assert.IsType<LuaString>(clone.Metatable.Get(LuaMetatableEvent.Name)).Value);
	}

	[Fact]
	public void DeepClone_SharedNestedTable_StaysShared()
	{
		var shared = new LuaTable();
		var root = new LuaTable();
		root.Set("a", shared);
		root.Set("b", shared);

		var clone = root.DeepClone();

		Assert.NotSame(shared, clone.Get("a"));
		Assert.Same(clone.Get("a"), clone.Get("b"));
	}

	[Fact]
	public void ShallowClone_KeepsTheSameMetatable()
	{
		var metatable = new LuaTable();
		var table = new LuaTable { Metatable = metatable };

		Assert.Same(metatable, table.ShallowClone().Metatable);
	}

	[Fact]
	public void DeepClone_CopiesTheMetatable()
	{
		// Pinned as it is: the clone gets its own metatable, so getmetatable(clone) is not getmetatable(original).
		// Share it instead if a metatable is to be treated as a class rather than as part of the value.
		var metatable = new LuaTable();
		var table = new LuaTable { Metatable = metatable };

		var clone = table.DeepClone();

		Assert.NotSame(metatable, clone.Metatable);
	}

	[Fact]
	public void DeepClone_EqualLiteralsWithDifferentMetatables_CollapseIntoOne()
	{
		// Pinned as it is: the visited map compares by value, so two equal strings are one entry and the second
		// one's own metatable is dropped. Track by reference instead if per-object metatables must survive.
		var first = new LuaString("x") { Metatable = new LuaTable() };
		var second = new LuaString("x") { Metatable = new LuaTable() };
		var table = new LuaTable();
		table.Set(1, first);
		table.Set(2, second);

		var clone = table.DeepClone();

		Assert.Same(clone.Get(1), clone.Get(2));
	}

}
