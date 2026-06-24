using AsyncLua.Interpreting;
using AsyncLua.Values;

namespace AsyncLua.Tests.Interpreting;

public class ComparisonInterpreterTests
{
	/// <summary>
	/// Creates a simple function prototype with the given instructions, constants, and register count.
	/// </summary>
	private static FunctionPrototype MakeProto(Instruction[] instructions, int maxRegSize = 1, LuaValue[]? constants = null)
	{
		return new FunctionPrototype(
			instructions,
			maxRegSize,
			false,
			constants ?? Array.Empty<LuaValue>(),
			Array.Empty<FunctionPrototype>());
	}

	private static LuaCallingContext Context() => new LuaState().CreateContext();

	// ── Equality ──────────────────────────────────────────────────────

	[Fact]
	public void Eq_TwoEqualNumbers_ReturnsTrue()
	{
		// R[0] = (K[0] == K[1])  — 42 == 42
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.EQ, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42), new LuaNumber(42) });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.True, result.First);
	}

	[Fact]
	public void Eq_TwoDifferentNumbers_ReturnsFalse()
	{
		// R[0] = (K[0] == K[1])  — 42 == 99
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.EQ, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42), new LuaNumber(99) });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.False, result.First);
	}

	[Fact]
	public void Eq_TwoEqualStrings_ReturnsTrue()
	{
		// R[0] = (K[0] == K[1])  — "hello" == "hello"
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.EQ, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaString("hello"), new LuaString("hello") });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.True, result.First);
	}

	[Fact]
	public void Eq_TwoDifferentStrings_ReturnsFalse()
	{
		// R[0] = (K[0] == K[1])  — "hello" == "world"
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.EQ, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaString("hello"), new LuaString("world") });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.False, result.First);
	}

	[Fact]
	public void Eq_DifferentTypes_ReturnsFalse()
	{
		// R[0] = (K[0] == K[1])  — 42 == "42"
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.EQ, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42), new LuaString("42") });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.False, result.First);
	}

	[Fact]
	public void Eq_NilEqualsNil_ReturnsTrue()
	{
		// Compare two register values that are both nil (default-initialised).
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.EQ, a: 0, b: 1, c: 2, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 3);

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.True, result.First);
	}

	[Fact]
	public void Eq_BooleanTrueEqualsTrue_ReturnsTrue()
	{
		// true == true
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.EQ, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { LuaBoolean.True, LuaBoolean.True });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.True, result.First);
	}

	// ── Less-than ─────────────────────────────────────────────────────

	[Fact]
	public void Lt_Numbers_LeftSmaller_ReturnsTrue()
	{
		// R[0] = (10 < 42)
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LT, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(10), new LuaNumber(42) });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.True, result.First);
	}

	[Fact]
	public void Lt_Numbers_LeftGreater_ReturnsFalse()
	{
		// R[0] = (99 < 42)
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LT, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(99), new LuaNumber(42) });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.False, result.First);
	}

	[Fact]
	public void Lt_Numbers_Equal_ReturnsFalse()
	{
		// R[0] = (42 < 42)
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LT, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42), new LuaNumber(42) });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.False, result.First);
	}

	[Fact]
	public void Lt_Strings_LexicographicallySmaller_ReturnsTrue()
	{
		// "abc" < "abd"
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LT, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaString("abc"), new LuaString("abd") });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.True, result.First);
	}

	[Fact]
	public void Lt_Strings_Equal_ReturnsFalse()
	{
		// "abc" < "abc"
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LT, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaString("abc"), new LuaString("abc") });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.False, result.First);
	}

	[Fact]
	public void Lt_DifferentTypes_Throws()
	{
		// 10 < "hello" — should throw
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LT, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(10), new LuaString("hello") });

		Assert.Throws<LuaRuntimeException>(() => AsyncLuaInterpreter.Call(proto, Context()));
	}

	// ── Less-than-or-equal ────────────────────────────────────────────

	[Fact]
	public void Le_Numbers_LeftSmaller_ReturnsTrue()
	{
		// 10 <= 42
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(10), new LuaNumber(42) });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.True, result.First);
	}

	[Fact]
	public void Le_Numbers_Equal_ReturnsTrue()
	{
		// 42 <= 42
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42), new LuaNumber(42) });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.True, result.First);
	}

	[Fact]
	public void Le_Numbers_LeftGreater_ReturnsFalse()
	{
		// 99 <= 42
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(99), new LuaNumber(42) });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.False, result.First);
	}

	[Fact]
	public void Le_Strings_Equal_ReturnsTrue()
	{
		// "abc" <= "abc"
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaString("abc"), new LuaString("abc") });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(LuaBoolean.True, result.First);
	}

	[Fact]
	public void Le_DifferentTypes_Throws()
	{
		// 10 <= "hello" — should throw
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.LE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(10), new LuaString("hello") });

		Assert.Throws<LuaRuntimeException>(() => AsyncLuaInterpreter.Call(proto, Context()));
	}

	// ── Boolean result type ───────────────────────────────────────────

	[Fact]
	public void Eq_ReturnsLuaBoolean_NotNil()
	{
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.EQ, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(1), new LuaNumber(2) });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.IsType<LuaBoolean>(result.First);
	}
}
