using AsyncLua.Values;

namespace AsyncLua.Tests.Values;

/// <summary>
/// Tests for <see cref="LuaValueConverter"/>: CLR ↔ LuaValue conversion.
/// </summary>
public class LuaValueConverterTests
{
	// ── ToLuaValue: null ───────────────────────────────────────────────

	[Fact]
	public void ToLuaValue_Null_ReturnsNil()
	{
		var result = LuaValueConverter.ToLuaValue(null);
		Assert.IsType<LuaNil>(result);
	}

	// ── ToLuaValue: primitives ──────────────────────────────────────────

	[Fact]
	public void ToLuaValue_BoolTrue_ReturnsBoolean()
	{
		var result = LuaValueConverter.ToLuaValue(true);
		var b = Assert.IsType<LuaBoolean>(result);
		Assert.True(b.Value);
	}

	[Fact]
	public void ToLuaValue_BoolFalse_ReturnsBoolean()
	{
		var result = LuaValueConverter.ToLuaValue(false);
		var b = Assert.IsType<LuaBoolean>(result);
		Assert.False(b.Value);
	}

	[Fact]
	public void ToLuaValue_Int_ReturnsNumber()
	{
		var result = LuaValueConverter.ToLuaValue(42);
		var n = Assert.IsType<LuaNumber>(result);
		Assert.Equal(42.0, n.Value);
	}

	[Fact]
	public void ToLuaValue_Double_ReturnsNumber()
	{
		var result = LuaValueConverter.ToLuaValue(3.14);
		var n = Assert.IsType<LuaNumber>(result);
		Assert.Equal(3.14, n.Value);
	}

	[Fact]
	public void ToLuaValue_String_ReturnsString()
	{
		var result = LuaValueConverter.ToLuaValue("hello");
		var s = Assert.IsType<LuaString>(result);
		Assert.Equal("hello", s.Value);
	}

	[Fact]
	public void ToLuaValue_Char_ReturnsString()
	{
		var result = LuaValueConverter.ToLuaValue('A');
		var s = Assert.IsType<LuaString>(result);
		Assert.Equal("A", s.Value);
	}

	[Fact]
	public void ToLuaValue_Decimal_ReturnsNumber()
	{
		var result = LuaValueConverter.ToLuaValue(123.456m);
		var n = Assert.IsType<LuaNumber>(result);
		Assert.Equal(123.456, n.Value);
	}

	[Fact]
	public void ToLuaValue_Enum_ReturnsString()
	{
		var result = LuaValueConverter.ToLuaValue(StringComparison.Ordinal);
		var s = Assert.IsType<LuaString>(result);
		Assert.Equal("Ordinal", s.Value);
	}

	// ── ToLuaValue: LuaValue pass-through ───────────────────────────────

	[Fact]
	public void ToLuaValue_LuaValue_ReturnsSame()
	{
		var original = new LuaNumber(99.0);
		var result = LuaValueConverter.ToLuaValue(original);
		Assert.Same(original, result);
	}

	// ── ToLuaValue: arrays ──────────────────────────────────────────────

	[Fact]
	public void ToLuaValue_IntArray_ReturnsTable()
	{
		var result = LuaValueConverter.ToLuaValue(new[] { 10, 20, 30 });
		var table = Assert.IsType<LuaTable>(result);
		Assert.Equal(3, table.Length);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(table.Get(1)).Value);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(table.Get(2)).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(table.Get(3)).Value);
	}

	[Fact]
	public void ToLuaValue_StringArray_ReturnsTable()
	{
		var result = LuaValueConverter.ToLuaValue(new[] { "a", "b" });
		var table = Assert.IsType<LuaTable>(result);
		Assert.Equal(2, table.Length);
		Assert.Equal("a", Assert.IsType<LuaString>(table.Get(1)).Value);
		Assert.Equal("b", Assert.IsType<LuaString>(table.Get(2)).Value);
	}

	// ── ToLuaValue: IEnumerable (List) ──────────────────────────────────

	[Fact]
	public void ToLuaValue_List_ReturnsTable()
	{
		var result = LuaValueConverter.ToLuaValue(new List<int> { 1, 2, 3 });
		var table = Assert.IsType<LuaTable>(result);
		Assert.Equal(3, table.Length);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(table.Get(1)).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(table.Get(2)).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(table.Get(3)).Value);
	}

	// ── ToLuaValue: Dictionary ──────────────────────────────────────────

	[Fact]
	public void ToLuaValue_Dictionary_ReturnsTable()
	{
		var dict = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 };
		var result = LuaValueConverter.ToLuaValue(dict);
		var table = Assert.IsType<LuaTable>(result);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(table.Get("one")).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(table.Get("two")).Value);
	}

	// ── ToLuaValue: UserData fallback ───────────────────────────────────

	[Fact]
	public void ToLuaValue_UnknownObject_ReturnsUserData()
	{
		var obj = new object();
		var result = LuaValueConverter.ToLuaValue(obj);
		var ud = Assert.IsType<LuaUserData>(result);
		Assert.Same(obj, ud.Target);
		Assert.Equal("Object", ud.UserDataTypeName);
	}

	// ── ToClrObject: generic ────────────────────────────────────────────

	[Fact]
	public void ToClrObject_Generic_Int()
	{
		var result = LuaValueConverter.ToClrObject<int>(new LuaNumber(42.0));
		Assert.Equal(42, result);
	}

	[Fact]
	public void ToClrObject_Generic_String()
	{
		var result = LuaValueConverter.ToClrObject<string>(new LuaString("test"));
		Assert.Equal("test", result);
	}

	[Fact]
	public void ToClrObject_Generic_Bool()
	{
		var result = LuaValueConverter.ToClrObject<bool>(LuaBoolean.FromBoolean(true));
		Assert.True(result);
	}

	[Fact]
	public void ToClrObject_Generic_NullableInt_FromNil()
	{
		var result = LuaValueConverter.ToClrObject<int?>(LuaNil.Instance);
		Assert.Null(result);
	}

	[Fact]
	public void ToClrObject_Generic_Int_FromNil_ReturnsDefault()
	{
		var result = LuaValueConverter.ToClrObject<int>(LuaNil.Instance);
		Assert.Equal(0, result);
	}

	// ── ToClrObject: typed ──────────────────────────────────────────────

	[Fact]
	public void ToClrObject_String_FromNumber()
	{
		var result = LuaValueConverter.ToClrObject(new LuaNumber(123.0), typeof(string));
		Assert.Equal("123", result);
	}

	[Fact]
	public void ToClrObject_Int_FromString()
	{
		var result = LuaValueConverter.ToClrObject(new LuaString("456"), typeof(int));
		Assert.Equal(456, result);
	}

	[Fact]
	public void ToClrObject_Double_FromInt()
	{
		var result = LuaValueConverter.ToClrObject(new LuaNumber(42), typeof(double));
		Assert.Equal(42.0, result);
	}

	[Fact]
	public void ToClrObject_UserData_ReturnsTarget()
	{
		var obj = new object();
		var ud = new LuaUserData(obj);
		var result = LuaValueConverter.ToClrObject(ud, typeof(object));
		Assert.Same(obj, result);
	}

	// ── Roundtrip ───────────────────────────────────────────────────────

	[Fact]
	public void Roundtrip_Int()
	{
		var original = 12345;
		var lua = LuaValueConverter.ToLuaValue(original);
		var back = LuaValueConverter.ToClrObject<int>(lua);
		Assert.Equal(original, back);
	}

	[Fact]
	public void Roundtrip_String()
	{
		var original = "Hello, Lua!";
		var lua = LuaValueConverter.ToLuaValue(original);
		var back = LuaValueConverter.ToClrObject<string>(lua);
		Assert.Equal(original, back);
	}

	[Fact]
	public void Roundtrip_Double()
	{
		var original = 3.1415926535;
		var lua = LuaValueConverter.ToLuaValue(original);
		var back = LuaValueConverter.ToClrObject<double>(lua);
		Assert.Equal(original, back);
	}

	[Fact]
	public void Roundtrip_IntArray()
	{
		var original = new[] { 1, 2, 3, 4, 5 };
		var lua = LuaValueConverter.ToLuaValue(original);
		var back = LuaValueConverter.ToClrObject<int[]>(lua);
		Assert.NotNull(back);
		Assert.Equal(original, back);
	}

	[Fact]
	public void Roundtrip_StringArray()
	{
		var original = new[] { "x", "y", "z" };
		var lua = LuaValueConverter.ToLuaValue(original);
		var back = LuaValueConverter.ToClrObject<string[]>(lua);
		Assert.NotNull(back);
		Assert.Equal(original, back);
	}
}
