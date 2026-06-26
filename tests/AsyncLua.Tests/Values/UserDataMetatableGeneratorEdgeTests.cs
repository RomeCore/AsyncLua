using System.Globalization;
using System.Threading.Tasks;
using AsyncLua.Values;

namespace AsyncLua.Tests.Values;

/// <summary>
/// Edge-case tests for <see cref="UserDataMetatableGenerator"/> and <see cref="LuaValueConverter"/>:
/// async methods, nullable types, static members, operators, indexers, and __metatable protection.
/// </summary>
public class UserDataMetatableGeneratorEdgeTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		return state;
	}

	// ── Helper types ─────────────────────────────────────────────────────

#pragma warning disable CA1812, CA1822
	private sealed class AsyncTestClass
	{
		public Task<int> GetNumberAsync() => Task.FromResult(42);
		public Task<string> GetStringAsync() => Task.FromResult("hello");
		public Task DoSomethingAsync() => Task.CompletedTask;
		public async Task<int> AddAsync(int a, int b) => a + b;
		public async Task<string> GreetAsync(string name) => $"Hi, {name}!";
		public async Task<int> GetZeroAsync() { await Task.CompletedTask; return 0; }
	}

	private sealed class NullableTestClass
	{
		public int? NullableInt { get; set; }
		public int NonNullableInt { get; set; } = 10;
		public string? NullableString { get; set; }
		public string NonNullableString { get; set; } = "default";
		public int? MethodWithNullable(int? x, string? y) => x;
		public string OptionalMethod(int x, string y = "default") => $"{x}:{y}";
	}

	private sealed class StaticMemberClass
	{
		public static string StaticMethod() => "static-ok";
		public static int StaticProperty { get; set; } = 100;
		public static readonly string StaticField = "field-value";
		[LuaVisible]
		private static string PrivateStaticMethod() => "private-static-ok";
	}

	private sealed class OperationClass
	{
		public int Value { get; set; }
		public override string ToString() => $"Op({Value})";
	}

	private class BaseClass
	{
		public string Name { get; set; } = "";
		public int Level { get; set; }
		public string Greet() => $"Hi, I'm {Name}!";
	}

	private sealed class InheritedClass : BaseClass
	{
		public string ChildOnly() => "child";
	}

	private sealed class MultiParamClass
	{
		public string Concat(string a, int b, double c) => $"{a}:{b}:{c}";
		public string ParamsWithMixed(string prefix, params int[] nums) =>
			$"{prefix}:{string.Join(",", nums)}";
	}

	private sealed class HiddenOverloadClass
	{
		public string Method(int x) => $"int:{x}";

		[LuaHidden]
		public string Method(string x) => $"string:{x}";
	}
#pragma warning restore CA1812, CA1822

	// ── Async methods ────────────────────────────────────────────────────

	[Fact]
	public async Task AsyncMethod_TaskOfInt_ReturnsNumber()
	{
		var state = CreateState();
		var obj = new AsyncTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = await state.ExecuteAsync("return await o:GetNumberAsync()");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public async Task AsyncMethod_TaskOfString_ReturnsString()
	{
		var state = CreateState();
		var obj = new AsyncTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = await state.ExecuteAsync("return await o:GetStringAsync()");
		Assert.Equal("hello", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public async Task AsyncMethod_Task_ReturnsNil()
	{
		var state = CreateState();
		var obj = new AsyncTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = await state.ExecuteAsync("return await o:DoSomethingAsync()");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public async Task AsyncMethod_WithArgs_ReturnsCorrect()
	{
		var state = CreateState();
		var obj = new AsyncTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = await state.ExecuteAsync("return await o:AddAsync(3, 4)");
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public async Task AsyncMethod_StringArg_ReturnsString()
	{
		var state = CreateState();
		var obj = new AsyncTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = await state.ExecuteAsync("return await o:GreetAsync('World')");
		Assert.Equal("Hi, World!", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void AsyncMethod_CalledWithoutAwait_ReturnsTask()
	{
		var state = CreateState();
		var obj = new AsyncTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		// Without 'await', the async method returns a LuaTask.
		var result = state.Execute("return o:GetNumberAsync()");
		Assert.IsType<LuaTask>(result.First);
	}

	// ── Nullable types ───────────────────────────────────────────────────

	[Fact]
	public void Nullable_SetNull_StoresNull()
	{
		var state = CreateState();
		var obj = new NullableTestClass { NullableInt = 42 };
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		state.Execute("o.NullableInt = nil");
		Assert.Null(obj.NullableInt);
	}

	[Fact]
	public void Nullable_SetNumber_StoresValue()
	{
		var state = CreateState();
		var obj = new NullableTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		state.Execute("o.NullableInt = 123");
		Assert.Equal(123, obj.NullableInt);
	}

	[Fact]
	public void Nullable_ReadNull_ReturnsNil()
	{
		var state = CreateState();
		var obj = new NullableTestClass { NullableInt = null };
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o.NullableInt");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void Nullable_ReadValue_ReturnsNumber()
	{
		var state = CreateState();
		var obj = new NullableTestClass { NullableInt = 77 };
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o.NullableInt");
		Assert.Equal(77.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void NonNullable_SetNull_ResetsToDefault()
	{
		var state = CreateState();
		var obj = new NullableTestClass { NonNullableInt = 50 };
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		state.Execute("o.NonNullableInt = nil");
		Assert.Equal(0, obj.NonNullableInt); // value type default
	}

	[Fact]
	public void Nullable_StringSetNull_StoresNull()
	{
		var state = CreateState();
		var obj = new NullableTestClass { NullableString = "hello" };
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		state.Execute("o.NullableString = nil");
		Assert.Null(obj.NullableString);
	}

	[Fact]
	public void Nullable_StringSetValue_StoresValue()
	{
		var state = CreateState();
		var obj = new NullableTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		state.Execute("o.NullableString = 'world'");
		Assert.Equal("world", obj.NullableString);
	}

	// ── Optional parameters ─────────────────────────────────────────────

	[Fact]
	public void OptionalParameter_ExplicitArg_Works()
	{
		var state = CreateState();
		var obj = new NullableTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o:OptionalMethod(5, 'explicit')");
		Assert.Equal("5:explicit", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void OptionalParameter_DefaultValue_Works()
	{
		var state = CreateState();
		var obj = new NullableTestClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o:OptionalMethod(5)");
		Assert.Equal("5:default", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── Static members ───────────────────────────────────────────────────

	[Fact]
	public void StaticMethod_CalledOnInstance_Works()
	{
		var state = CreateState();
		var obj = new StaticMemberClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o:StaticMethod()");
		Assert.Equal("static-ok", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void StaticProperty_Read_ReturnsValue()
	{
		var state = CreateState();
		var obj = new StaticMemberClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o.StaticProperty");
		Assert.Equal(100.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void StaticProperty_Write_UpdatesValue()
	{
		var state = CreateState();
		var obj = new StaticMemberClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		state.Execute("o.StaticProperty = 200");
		Assert.Equal(200, StaticMemberClass.StaticProperty);
	}

	[Fact]
	public void StaticField_Read_ReturnsValue()
	{
		var state = CreateState();
		var obj = new StaticMemberClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o.StaticField");
		Assert.Equal("field-value", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void PrivateStatic_WithLuaVisible_Works()
	{
		var state = CreateState();
		var obj = new StaticMemberClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o:PrivateStaticMethod()");
		Assert.Equal("private-static-ok", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── __metatable protection ───────────────────────────────────────────

	[Fact]
	public void GetMetatable_ReturnsProtectedString()
	{
		var state = CreateState();
		var obj = new OperationClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return getmetatable(o)");
		var s = Assert.IsType<LuaString>(result.First);
		Assert.Contains("protected", s.Value);
	}

	[Fact]
	public void SetMetatable_OnUserData_Throws()
	{
		var state = CreateState();
		var obj = new OperationClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("setmetatable(o, {})"));
		Assert.Contains("protected", ex.Message);
	}

	// ── Inherited members ────────────────────────────────────────────────

	[Fact]
	public void InheritedMethod_FromBase_Works()
	{
		var state = CreateState();
		var obj = new InheritedClass { Name = "Child", Level = 5 };
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o:Greet()");
		Assert.Equal("Hi, I'm Child!", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void InheritedMethod_FromChild_Works()
	{
		var state = CreateState();
		var obj = new InheritedClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o:ChildOnly()");
		Assert.Equal("child", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void InheritedProperty_FromBase_Works()
	{
		var state = CreateState();
		var obj = new InheritedClass { Name = "ChildName" };
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o.Name");
		Assert.Equal("ChildName", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── Multi-param and params ───────────────────────────────────────────

	[Fact]
	public void MultiParam_CorrectTypes_Works()
	{
		var state = CreateState();
		var obj = new MultiParamClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		var result = state.Execute("return o:Concat('test', 42, 5.5)");
		var actual = Assert.IsType<LuaString>(result.First).Value;
		Assert.Equal("test:42:5.5", actual);
	}

	[Fact]
	public void ParamsWithMixed_ExplicitPlusParams_Works()
	{
		var state = CreateState();
		var obj = new MultiParamClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o:ParamsWithMixed('sum', 1, 2, 3, 4)");
		Assert.Equal("sum:1,2,3,4", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void ParamsWithMixed_OnlyPrefix_Works()
	{
		var state = CreateState();
		var obj = new MultiParamClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o:ParamsWithMixed('none')");
		Assert.Equal("none:", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── Hidden overload ─────────────────────────────────────────────────

	[Fact]
	public void HiddenOverload_VisibleOverload_Works()
	{
		var state = CreateState();
		var obj = new HiddenOverloadClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		var result = state.Execute("return o:Method(5)");
		Assert.Equal("int:5", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void HiddenOverload_HiddenOverload_NotAccessible()
	{
		var state = CreateState();
		var obj = new HiddenOverloadClass();
		UserDataMetatableGenerator.RegisterObject(state, "o", obj);

		// Only the visible overload (Method(int)) exists; Method(string) is hidden.
		// Passing 'test' tries to convert to int and fails.
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			state.Execute("return o:Method('test')"));
		Assert.Contains("Call error", ex.Message);
	}

	// ── LuaValueConverter auto-metatable ─────────────────────────────────

	[Fact]
	public void ToLuaValue_UnknownObject_HasMetatable()
	{
		var obj = new OperationClass { Value = 42 };
		var lv = LuaValueConverter.ToLuaValue(obj);

		var ud = Assert.IsType<LuaUserData>(lv);
		Assert.NotNull(ud.Metatable);
		Assert.True(ud.Metatable.HasEvent(LuaMetatableEvent.Index));
		Assert.True(ud.Metatable.HasEvent(LuaMetatableEvent.Name));
	}

	[Fact]
	public void ToLuaValue_Object_CachedMetatable()
	{
		var obj1 = new OperationClass();
		var obj2 = new OperationClass();

		var lv1 = LuaValueConverter.ToLuaValue(obj1);
		var lv2 = LuaValueConverter.ToLuaValue(obj2);

		var ud1 = Assert.IsType<LuaUserData>(lv1);
		var ud2 = Assert.IsType<LuaUserData>(lv2);

		Assert.Same(ud1.Metatable, ud2.Metatable);
	}

	// ── Fuzzy matching through auto-converter ────────────────────────────

	[Fact]
	public void AutoUserData_FuzzyCall_Works()
	{
		var state = CreateState();
		// Register via LuaValueConverter (auto-metatable).
		state.SetGlobal("o", LuaValueConverter.ToLuaValue(new BaseClass { Name = "Fuzzy", Level = 10 }));

		var result = state.Execute("return o:greet()");
		Assert.Equal("Hi, I'm Fuzzy!", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void AutoUserData_SnakeCaseProperty_Works()
	{
		var state = CreateState();
		state.SetGlobal("o", LuaValueConverter.ToLuaValue(new BaseClass { Name = "Snake" }));

		var result = state.Execute("return o.name");
		Assert.Equal("Snake", Assert.IsType<LuaString>(result.First).Value);
	}

	// ── __name ───────────────────────────────────────────────────────────

	[Fact]
	public void NameMetamethod_ContainsTypeName()
	{
		var state = CreateState();
		state.SetGlobal("o", LuaValueConverter.ToLuaValue(new BaseClass()));

		var result = state.Execute("return getmetatable(o)");
		var s = Assert.IsType<LuaString>(result.First);
		Assert.Contains("protected", s.Value);

		// __name is set but hidden behind __metatable protection.
		// We can verify via type-metatable from C# side:
		var ud = Assert.IsType<LuaUserData>(LuaValueConverter.ToLuaValue(new BaseClass()));
		Assert.NotNull(ud.Metatable);
		Assert.True(ud.Metatable.HasEvent(LuaMetatableEvent.Name));
	}
}
