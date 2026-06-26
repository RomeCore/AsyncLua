using AsyncLua.Values;

namespace AsyncLua.Tests.Values;

/// <summary>
/// Integration tests for <see cref="UserDataMetatableGenerator"/>:
/// registers CLR objects and exercises them from Lua.
/// </summary>
public class UserDataMetatableGeneratorTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		return state;
	}

	// ── Helper types ─────────────────────────────────────────────────────

#pragma warning disable CA1812
	private sealed class Player
	{
		public string? Name { get; set; }
		public int Level { get; set; }
		public bool IsActive { get; set; } = true;

		public string Greet(string greeting) => $"{greeting}, I'm {Name}!";
		public string Greet() => $"Hi, I'm {Name}!";

		public int Add(int a, int b) => a + b;

		public string ParamsSum(params int[] numbers) =>
			$"sum={numbers.Sum()}";

		public override string ToString() => $"Player:{Name}({Level})";
	}

	private sealed class Counter
	{
		public int Count { get; private set; }
		public int Length => Count;
		public void Increment() => Count++;
	}

	private sealed class StringContainer
	{
		public readonly string Value;
		public StringContainer(string value) => Value = value;
	}

	private sealed class EventSource
	{
		public event Action? SomethingHappened;
		public void Trigger() => SomethingHappened?.Invoke();
	}

	private sealed class EnumerableWrapper : IEnumerable<int>
	{
		private readonly int[] _items;
		public EnumerableWrapper(params int[] items) => _items = items;

		public IEnumerator<int> GetEnumerator() =>
			((IEnumerable<int>)_items).GetEnumerator();

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
			GetEnumerator();
	}
#pragma warning restore CA1812

	// ── Types for fuzzy matching and visibility tests ────────────────────

#pragma warning disable CA1812
	private sealed class FuzzyTestClass
	{
		public string Rotate180() => "rotated";
		public int XMLData { get; set; } = 42;
		public string HTTPRequest() => "request";
	}

	private sealed class VisibilityTestClass
	{
		public string PublicMember() => "public";

		[LuaHidden]
		public string HiddenMember() => "hidden";

		[LuaVisible]
		private string PrivateVisibleMember() => "private-visible";

		[LuaHidden]
		public int HiddenProperty { get; set; } = 99;

		[LuaVisible]
		internal int PrivateVisibleField = 123;
	}
#pragma warning restore CA1812


	// ── Setup ────────────────────────────────────────────────────────────

	[Fact]
	public void CreateMetatable_ReturnsMetatableWithEvents()
	{
		var mt = UserDataMetatableGenerator.CreateMetatable(typeof(Player));
		Assert.NotNull(mt);
		Assert.True(mt.HasEvent(LuaMetatableEvent.Name));
		Assert.True(mt.HasEvent(LuaMetatableEvent.ToString));
		Assert.True(mt.HasEvent(LuaMetatableEvent.Index));
		Assert.True(mt.HasEvent(LuaMetatableEvent.NewIndex));
	}

	[Fact]
	public void GetOrCreate_CachesMetatable()
	{
		var mt1 = UserDataMetatableGenerator.GetOrCreate(typeof(Player));
		var mt2 = UserDataMetatableGenerator.GetOrCreate(typeof(Player));
		Assert.Same(mt1, mt2);
	}

	[Fact]
	public void ClearCache_ForcesNewMetatable()
	{
		var mt1 = UserDataMetatableGenerator.GetOrCreate(typeof(Player));
		UserDataMetatableGenerator.ClearCache();
		var mt2 = UserDataMetatableGenerator.GetOrCreate(typeof(Player));
		Assert.NotSame(mt1, mt2);
	}

	// ── __name ───────────────────────────────────────────────────────────

	[Fact]
	public void Name_ReturnsFullTypeName()
	{
		var mt = UserDataMetatableGenerator.CreateMetatable(typeof(Player));
		var nameVal = mt.Get(LuaMetatableEvent.Name);
		var nameStr = Assert.IsType<LuaString>(nameVal);
		Assert.Contains("Player", nameStr.Value);
	}

	// ── __tostring ───────────────────────────────────────────────────────

	[Fact]
	public void ToString_ViaLua_CallsDotNetToString()
	{
		var state = CreateState();
		var player = new Player { Name = "Arthas", Level = 80 };
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return tostring(p)");
		var s = Assert.IsType<LuaString>(result.First);
		Assert.Equal("Player:Arthas(80)", s.Value);
	}

	// ── Property read ────────────────────────────────────────────────────

	[Fact]
	public void PropertyRead_ViaLua_ReturnsValue()
	{
		var state = CreateState();
		var player = new Player { Name = "Thrall", Level = 60 };
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p.Name");
		var s = Assert.IsType<LuaString>(result.First);
		Assert.Equal("Thrall", s.Value);
	}

	[Fact]
	public void PropertyRead_Int_ReturnsNumber()
	{
		var state = CreateState();
		var player = new Player { Name = "Jaina", Level = 55 };
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p.Level");
		var n = Assert.IsType<LuaNumber>(result.First);
		Assert.Equal(55.0, n.Value);
	}

	// ── Property write ───────────────────────────────────────────────────

	[Fact]
	public void PropertyWrite_ViaLua_UpdatesValue()
	{
		var state = CreateState();
		var player = new Player { Name = "Uther", Level = 70 };
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		state.Execute("p.Level = 71");
		Assert.Equal(71, player.Level);
	}

	[Fact]
	public void PropertyWrite_String_SetsValue()
	{
		var state = CreateState();
		var player = new Player { Name = "OldName", Level = 1 };
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		state.Execute("p.Name = 'NewName'");
		Assert.Equal("NewName", player.Name);
	}

	// ── Field read ───────────────────────────────────────────────────────

	[Fact]
	public void FieldRead_ViaLua_ReturnsValue()
	{
		var state = CreateState();
		var container = new StringContainer("field-value");
		UserDataMetatableGenerator.RegisterObject(state, "c", container);

		var result = state.Execute("return c.Value");
		var s = Assert.IsType<LuaString>(result.First);
		Assert.Equal("field-value", s.Value);
	}

	// ── Method call ──────────────────────────────────────────────────────

	[Fact]
	public void MethodCall_NoArgs_ViaColon()
	{
		var state = CreateState();
		var player = new Player { Name = "Muradin" };
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p:Greet()");
		var s = Assert.IsType<LuaString>(result.First);
		Assert.Equal("Hi, I'm Muradin!", s.Value);
	}

	[Fact]
	public void MethodCall_WithArgs_ViaColon()
	{
		var state = CreateState();
		var player = new Player { Name = "Sylvanas" };
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p:Greet('Hello')");
		var s = Assert.IsType<LuaString>(result.First);
		Assert.Equal("Hello, I'm Sylvanas!", s.Value);
	}

	[Fact]
	public void MethodCall_WithArgs_ViaDot()
	{
		var state = CreateState();
		var player = new Player { Name = "Kael'thas" };
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p.Greet(p, 'Ah')");
		var s = Assert.IsType<LuaString>(result.First);
		Assert.Equal("Ah, I'm Kael'thas!", s.Value);
	}

	[Fact]
	public void MethodCall_Overload_NoArgs_SelectsCorrect()
	{
		var state = CreateState();
		var player = new Player { Name = "Test" };
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p:Greet()");
		Assert.Contains("Hi", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void MethodCall_Overload_WithArg_SelectsCorrect()
	{
		var state = CreateState();
		var player = new Player { Name = "Test" };
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p:Greet('Yo')");
		Assert.Contains("Yo", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void MethodCall_IntReturn()
	{
		var state = CreateState();
		var player = new Player();
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p:Add(3, 4)");
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── Params method ────────────────────────────────────────────────────

	[Fact]
	public void MethodCall_Params_ViaLua()
	{
		var state = CreateState();
		var player = new Player();
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p:ParamsSum(1, 2, 3, 4, 5)");
		var s = Assert.IsType<LuaString>(result.First);
		Assert.Equal("sum=15", s.Value);
	}

	// ── __len (Count / Length property) ──────────────────────────────────

	[Fact]
	public void Len_CountProperty_ReturnsCount()
	{
		var state = CreateState();
		var counter = new Counter();
		counter.Increment();
		counter.Increment();
		counter.Increment();
		UserDataMetatableGenerator.RegisterObject(state, "c", counter);

		var result = state.Execute("return #c");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Len_LengthProperty_ReturnsLength()
	{
		var state = CreateState();
		var counter = new Counter();
		counter.Increment();
		UserDataMetatableGenerator.RegisterObject(state, "c", counter);

		var result = state.Execute("return #c");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── __pairs (IEnumerable) ────────────────────────────────────────────

	[Fact]
	public void Pairs_IteratesOverEnumerable()
	{
		var state = CreateState();
		var wrapper = new EnumerableWrapper(10, 20, 30);
		UserDataMetatableGenerator.RegisterObject(state, "w", wrapper);

		var result = state.Execute(@"
			local sum = 0
			for i, v in pairs(w) do
				sum = sum + v
			end
			return sum
		");
		Assert.Equal(60.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Pairs_EmptyEnumerable_ReturnsNil()
	{
		var state = CreateState();
		var wrapper = new EnumerableWrapper();
		UserDataMetatableGenerator.RegisterObject(state, "w", wrapper);

		var result = state.Execute(@"
			local count = 0
			for i, v in pairs(w) do
				count = count + 1
			end
			return count
		");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ── Errors ───────────────────────────────────────────────────────────

	[Fact]
	public void PropertyRead_NonExistent_ReturnsNil()
	{
		var state = CreateState();
		var player = new Player();
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p.NonExistentProperty");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void MethodCall_NonExistent_ReturnsNil()
	{
		var state = CreateState();
		var player = new Player();
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var result = state.Execute("return p.NonExistentMethod");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void MethodCall_WrongArgs_Throws()
	{
		var state = CreateState();
		var player = new Player();
		UserDataMetatableGenerator.RegisterObject(state, "p", player);

		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("return p:Add('not', 'numbers')"));
		Assert.Contains("Call error", ex.Message);
	}

	// ── RegisterObject ───────────────────────────────────────────────────

	[Fact]
	public void RegisterObject_ReturnsUserData()
	{
		var state = CreateState();
		var player = new Player();
		var ud = UserDataMetatableGenerator.RegisterObject(state, "p", player);

		Assert.NotNull(ud.Metatable);
		Assert.Same(player, ud.Target);

		// Check it's accessible from Lua.
		var result = state.Execute("return p");
		Assert.Same(ud, result.First);
	}

	[Fact]
	public void RegisterObject_IntoTable()
	{
		var table = new LuaTable();
		var player = new Player { Name = "Malfurion" };
		UserDataMetatableGenerator.RegisterObject(table, new LuaString("hero"), player);

		var name = Assert.IsType<LuaString>(LuaValueConverter.ToLuaValue(player.Name));
		// Just verify it's stored properly
		var stored = table.Get("hero");
		var ud = Assert.IsType<LuaUserData>(stored);
		Assert.Same(player, ud.Target);
	}

	// ── Static methods ───────────────────────────────────────────────────

	[Fact]
	public void GetOrCreate_FromUserData()
	{
		var player = new Player();
		var ud = new LuaUserData(player);
		var mt = UserDataMetatableGenerator.GetOrCreate(ud);
		Assert.NotNull(mt);
	}

	// ── Fuzzy name matching ─────────────────────────────────────────────

	[Fact]
	public void FuzzyMatch_OriginalName_Works()
	{
		var state = CreateState();
		var obj = new FuzzyTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o:Rotate180()");
		Assert.Equal("rotated", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void FuzzyMatch_Lowercase_Works()
	{
		var state = CreateState();
		var obj = new FuzzyTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o:rotate180()");
		Assert.Equal("rotated", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void FuzzyMatch_SnakeCase_Works()
	{
		var state = CreateState();
		var obj = new FuzzyTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o:rotate_180()");
		Assert.Equal("rotated", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void FuzzyMatch_UpperSnakeCase_Works()
	{
		var state = CreateState();
		var obj = new FuzzyTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o:ROTATE_180()");
		Assert.Equal("rotated", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void FuzzyMatch_XmlData_SnakeCase()
	{
		var state = CreateState();
		var obj = new FuzzyTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o.xml_data");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void FuzzyMatch_HttpRequest_SnakeCase()
	{
		var state = CreateState();
		var obj = new FuzzyTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o:http_request()");
		Assert.Equal("request", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── LuaHidden / LuaVisible attributes ───────────────────────────────

	[Fact]
	public void Visibility_PublicMember_Visible()
	{
		var state = CreateState();
		var obj = new VisibilityTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o:PublicMember()");
		Assert.Equal("public", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Visibility_HiddenMember_NotVisible()
	{
		var state = CreateState();
		var obj = new VisibilityTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o.HiddenMember");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void Visibility_PrivateVisibleMember_Visible()
	{
		var state = CreateState();
		var obj = new VisibilityTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o:PrivateVisibleMember()");
		Assert.Equal("private-visible", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Visibility_HiddenProperty_NotVisible()
	{
		var state = CreateState();
		var obj = new VisibilityTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o.HiddenProperty");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void Visibility_PrivateVisibleField_Visible()
	{
		var state = CreateState();
		var obj = new VisibilityTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		var result = state.Execute("return o.PrivateVisibleField");
		Assert.Equal(123.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Visibility_PrivateVisibleField_Writeable()
	{
		var state = CreateState();
		var obj = new VisibilityTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);
		state.Execute("o.PrivateVisibleField = 456");
		Assert.Equal(456, obj.PrivateVisibleField);
	}
}
