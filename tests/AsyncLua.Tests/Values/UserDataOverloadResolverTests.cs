using System.Reflection;
using AsyncLua.Values;

namespace AsyncLua.Tests.Values;

/// <summary>
/// Tests for <see cref="UserDataOverloadResolver"/>: overload resolution and compatibility.
/// </summary>
public class UserDataOverloadResolverTests
{
	// ── Helper types ─────────────────────────────────────────────────────

#pragma warning disable CA1812
	private sealed class OverloadTestClass
	{
		public static string Method(int x) => $"int:{x}";
		public static string Method(double x) => $"double:{x}";
		public static string Method(string x) => $"string:{x}";
		public static string Method(int x, int y) => $"int_int:{x},{y}";
		public static string Method(int x, string y) => $"int_string:{x},{y}";
		public static string Method(params int[] vals) => $"params:{string.Join(",", vals)}";
		public static string Method() => "empty";
	}
#pragma warning restore CA1812

	private static readonly MethodInfo[] Methods = typeof(OverloadTestClass)
		.GetMethods(BindingFlags.Public | BindingFlags.Static)
		.Where(m => m.Name == "Method" && m.ReturnType == typeof(string))
		.ToArray();

	// ── ResolveOverload ──────────────────────────────────────────────────

	[Fact]
	public void ResolveOverload_NoArgs_ReturnsEmptyOrParamsOverload()
	{
		var match = UserDataOverloadResolver.ResolveOverload(Methods, Array.Empty<LuaValue>(), out _);
		Assert.NotNull(match);
		// Either Method() or Method(params int[]) — both accept 0 args.
		Assert.True(match.GetParameters().Length == 0
			|| match.GetParameters()[0].GetCustomAttributes(typeof(ParamArrayAttribute), false).Any());
	}

	[Fact]
	public void ResolveOverload_IntArg_ReturnsIntOverload()
	{
		var match = UserDataOverloadResolver.ResolveOverload(Methods, new LuaValue[] { new LuaNumber(42) }, out _);
		Assert.NotNull(match);
		var pars = match.GetParameters();
		Assert.Single(pars);
		// LuaNumber prefers double over int (cost 1 vs 2).
		Assert.Equal(typeof(double), pars[0].ParameterType);
	}

	[Fact]
	public void ResolveOverload_StringArg_ReturnsStringOverload()
	{
		var match = UserDataOverloadResolver.ResolveOverload(Methods, new LuaValue[] { new LuaString("hi") }, out _);
		Assert.NotNull(match);
		var pars = match.GetParameters();
		Assert.Single(pars);
		Assert.Equal(typeof(string), pars[0].ParameterType);
	}

	[Fact]
	public void ResolveOverload_TwoInts_ReturnsIntIntOverload()
	{
		var match = UserDataOverloadResolver.ResolveOverload(
			Methods, new LuaValue[] { new LuaNumber(1), new LuaNumber(2) }, out _);
		Assert.NotNull(match);
		var pars = match.GetParameters();
		Assert.Equal(2, pars.Length);
		Assert.Equal(typeof(int), pars[0].ParameterType);
		Assert.Equal(typeof(int), pars[1].ParameterType);
	}

	[Fact]
	public void ResolveOverload_IntAndString_ReturnsIntStringOverload()
	{
		var match = UserDataOverloadResolver.ResolveOverload(
			Methods, new LuaValue[] { new LuaNumber(1), new LuaString("hi") }, out _);
		Assert.NotNull(match);
		var pars = match.GetParameters();
		Assert.Equal(2, pars.Length);
		Assert.Equal(typeof(int), pars[0].ParameterType);
		Assert.Equal(typeof(string), pars[1].ParameterType);
	}

	[Fact]
	public void ResolveOverload_ThreeInts_ReturnsParamsOverload()
	{
		var match = UserDataOverloadResolver.ResolveOverload(
			Methods, new LuaValue[] { new LuaNumber(1), new LuaNumber(2), new LuaNumber(3) }, out _);
		Assert.NotNull(match);
		Assert.True(match.GetParameters()[0].GetCustomAttributes(typeof(ParamArrayAttribute), false).Any(),
			"Expected the params overload to be selected.");
	}

	[Fact]
	public void ResolveOverload_TooManyArgs_ReturnsNull()
	{
		var match = UserDataOverloadResolver.ResolveOverload(
			Methods,
			new LuaValue[] { new LuaNumber(1), new LuaNumber(2), new LuaNumber(3), new LuaNumber(4), new LuaNumber(5) },
			out _);
		Assert.NotNull(match); // params can handle it
	}

	[Fact]
	public void ResolveOverload_IncompatibleArg_ReturnsNull()
	{
		// Only methods that take a LuaTable param — none exist, so should return null.
		var match = UserDataOverloadResolver.ResolveOverload(
			Methods, new LuaValue[] { new LuaTable() }, out _);
		Assert.Null(match);
	}

	// ── IsCompatible ─────────────────────────────────────────────────────

	[Fact]
	public void IsCompatible_NilWithReferenceType_ReturnsTrue()
	{
		Assert.True(UserDataOverloadResolver.IsCompatible(LuaNil.Instance, typeof(string), out var cost));
		Assert.Equal(1, cost);
	}

	[Fact]
	public void IsCompatible_NilWithValueType_ReturnsFalse()
	{
		Assert.False(UserDataOverloadResolver.IsCompatible(LuaNil.Instance, typeof(int), out _));
	}

	[Fact]
	public void IsCompatible_NilWithNullable_ReturnsTrue()
	{
		Assert.True(UserDataOverloadResolver.IsCompatible(LuaNil.Instance, typeof(int?), out _));
	}

	[Fact]
	public void IsCompatible_BoolWithBool_ReturnsTrue()
	{
		Assert.True(UserDataOverloadResolver.IsCompatible(
			LuaBoolean.FromBoolean(true), typeof(bool), out var cost));
		Assert.Equal(1, cost);
	}

	[Fact]
	public void IsCompatible_NumberWithDouble_ReturnsTrue()
	{
		Assert.True(UserDataOverloadResolver.IsCompatible(
			new LuaNumber(3.14), typeof(double), out var cost));
		Assert.Equal(1, cost);
	}

	[Fact]
	public void IsCompatible_NumberWithInt_ReturnsTrue()
	{
		Assert.True(UserDataOverloadResolver.IsCompatible(
			new LuaNumber(42.0), typeof(int), out var cost));
		Assert.Equal(2, cost); // conversion cost
	}

	[Fact]
	public void IsCompatible_StringWithString_ReturnsTrue()
	{
		Assert.True(UserDataOverloadResolver.IsCompatible(
			new LuaString("test"), typeof(string), out var cost));
		Assert.Equal(1, cost);
	}

	[Fact]
	public void IsCompatible_UserDataWithCompatibleType_ReturnsTrue()
	{
		var ud = new LuaUserData("hello");
		Assert.True(UserDataOverloadResolver.IsCompatible(
			ud, typeof(string), out var cost));
		Assert.Equal(1, cost);
	}

	[Fact]
	public void IsCompatible_UserDataWithIncompatibleType_ReturnsFalse()
	{
		var ud = new LuaUserData("hello");
		Assert.False(UserDataOverloadResolver.IsCompatible(
			ud, typeof(int), out _));
	}

	[Fact]
	public void IsCompatible_FunctionWithDelegate_ReturnsTrue()
	{
		var func = new LuaCallbackFunction((ctx, args) => LuaTuple.Empty, "test");
		Assert.True(UserDataOverloadResolver.IsCompatible(
			func, typeof(Action), out var cost));
		Assert.Equal(5, cost);
	}

	// ── PrepareCallArguments ─────────────────────────────────────────────

	[Fact]
	public void PrepareCallArguments_SimpleArgs()
	{
		var method = Methods.First(m => m.GetParameters().Length == 2
			&& m.GetParameters()[0].ParameterType == typeof(int)
			&& m.GetParameters()[1].ParameterType == typeof(int));
		Assert.NotNull(method);

		var pars = method.GetParameters();
		var args = new LuaValue[] { new LuaNumber(10), new LuaNumber(20) };
		var result = UserDataOverloadResolver.PrepareCallArguments(method, pars, args, argOffset: 0);

		Assert.Equal(2, result.Length);
		Assert.Equal(10, result[0]);
		Assert.Equal(20, result[1]);
	}

	[Fact]
	public void PrepareCallArguments_WithArgOffset()
	{
		var method = Methods.First(m => m.GetParameters().Length == 1
			&& m.GetParameters()[0].ParameterType == typeof(string));
		Assert.NotNull(method);

		var pars = method.GetParameters();
		// args[0] is the userdata (self), args[1] is the real arg
		var args = new LuaValue[] { new LuaUserData("ignored"), new LuaString("real") };
		var result = UserDataOverloadResolver.PrepareCallArguments(method, pars, args, argOffset: 1);

		Assert.Single(result);
		Assert.Equal("real", result[0]);
	}

	[Fact]
	public void PrepareCallArguments_ParamsArray()
	{
		var method = Methods.First(m => m.GetParameters().Any(p =>
			p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Any()));
		Assert.NotNull(method);

		var pars = method.GetParameters();
		var args = new LuaValue[] { new LuaNumber(1), new LuaNumber(2), new LuaNumber(3) };
		var result = UserDataOverloadResolver.PrepareCallArguments(method, pars, args, argOffset: 0);

		// First param is the params int[] — should contain all args
		var paramsArray = Assert.IsType<int[]>(result[0]);
		Assert.Equal(new[] { 1, 2, 3 }, paramsArray);
	}
}
