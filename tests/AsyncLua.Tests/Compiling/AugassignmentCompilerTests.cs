using AsyncLua.Compiling;
using AsyncLua.Interpreting;
using AsyncLua.Parsing;
using AsyncLua.Values;

namespace AsyncLua.Tests.Compiling;

public class AugassignmentCompilerTests
{
	private static LuaTuple CompileAndExecute(string code, LuaCallingContext? context = null)
	{
		var parser = new AsyncLuaParser();
		var block = parser.Parse(code);
		var prototype = AsyncLuaCompiler.Compile(block, sourceName: "test");
		return AsyncLuaInterpreter.Call(prototype, context ?? new LuaState().CreateContext());
	}

	private static async Task<LuaTuple> CompileAndExecuteAsync(string code, LuaCallingContext? context = null)
	{
		var parser = new AsyncLuaParser();
		var block = parser.Parse(code);
		var prototype = AsyncLuaCompiler.Compile(block, sourceName: "test");
		return await AsyncLuaInterpreter.CallAsync(prototype, context ?? new LuaState().CreateContext());
	}

	// ═══════════════════════════════════════════════════════════════
	// Arithmetic augmented assignments
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void AddAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 10; x += 5; return x");
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void SubAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 10; x -= 3; return x");
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void MulAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 6; x *= 7; return x");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void DivAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 84; x /= 2; return x");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void IntegerDivAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 43; x //= 2; return x");
		Assert.Equal(21.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ModAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 10; x %= 3; return x");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void PowAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 2; x ^= 3; return x");
		Assert.Equal(8.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// String concatenation
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void ConcatAssign_LocalVariable()
	{
		var result = CompileAndExecute("local s = 'hello '; s ..= 'world'; return s");
		Assert.Equal("hello world", Assert.IsType<LuaString>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Bitwise augmented assignments
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void BitAndAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 0xFF; x &= 0x0F; return x");
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void BitOrAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 0xF0; x |= 0x0F; return x");
		Assert.Equal(255.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void BitXorAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 0xFF; x ~= 0x0F; return x");
		Assert.Equal(240.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ShiftLeftAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 1; x <<= 4; return x");
		Assert.Equal(16.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ShiftRightAssign_LocalVariable()
	{
		var result = CompileAndExecute("local x = 16; x >>= 2; return x");
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Global variables
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void AddAssign_GlobalVariable()
	{
		var result = CompileAndExecute("x = 10; x += 5; return x");
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ConcatAssign_GlobalVariable()
	{
		var result = CompileAndExecute("s = 'hello '; s ..= 'world'; return s");
		Assert.Equal("hello world", Assert.IsType<LuaString>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Table index augmented assignments
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void AddAssign_TableIndex_Bracket()
	{
		var result = CompileAndExecute("local t = {}; t[1] = 10; t[1] += 5; return t[1]");
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void AddAssign_TableIndex_Dot()
	{
		var result = CompileAndExecute("local t = {}; t.x = 10; t.x += 5; return t.x");
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ConcatAssign_TableIndex()
	{
		var result = CompileAndExecute("local t = {}; t[1] = 'hello '; t[1] ..= 'world'; return t[1]");
		Assert.Equal("hello world", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void MulAssign_TableIndex_Dot()
	{
		var result = CompileAndExecute("local t = {}; t.value = 6; t.value *= 7; return t.value");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Nested table indices
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void AddAssign_NestedTableIndex()
	{
		var result = CompileAndExecute("local t = { inner = {} }; t.inner[1] = 10; t.inner[1] += 5; return t.inner[1]");
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Chained augmented assignments
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void MultipleAugassignments_Chain()
	{
		var result = CompileAndExecute("local x = 2; x += 3; x *= 2; x -= 1; return x");
		// ((2 + 3) * 2) - 1 = 9
		Assert.Equal(9.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Augmented assignment with expression as right-hand side
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void AddAssign_WithComplexExpression()
	{
		var result = CompileAndExecute("local x = 5; x += 3 * 2; return x");
		Assert.Equal(11.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void MulAssign_WithExpression()
	{
		var result = CompileAndExecute("local x = 2; x *= 3 + 4; return x");
		Assert.Equal(14.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Upvalue augmented assignments (closures)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void AddAssign_Upvalue()
	{
		var result = CompileAndExecute(@"
            local x = 10
            local function increment()
                x += 5
            end
            increment()
            return x
        ");
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ConcatAssign_Upvalue()
	{
		var result = CompileAndExecute(@"
            local s = 'hello '
            local function append()
                s ..= 'world'
            end
            append()
            return s
        ");
		Assert.Equal("hello world", Assert.IsType<LuaString>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Async augmented assignments
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task AddAssign_WithAsyncExpression()
	{
		var state = new LuaState();
		state.Register("getValue", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(
				(ctx, args) => Task.FromResult(new LuaTuple(new LuaNumber(5))))));

		var result = await CompileAndExecuteAsync(@"
            local x = 10
            x += await getValue()
            return x
        ", state.CreateContext());

		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public async Task MulAssign_WithAsyncExpression_OnTable()
	{
		var state = new LuaState();
		state.Register("getValue", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(
				(ctx, args) => Task.FromResult(new LuaTuple(new LuaNumber(7))))));

		var result = await CompileAndExecuteAsync(@"
            local t = {}
            t.x = 6
            t.x *= await getValue()
            return t.x
        ", state.CreateContext());

		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Edge cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Augassignment_SelfReferential_UsesOldValue()
	{
		// x += x should use the old value of x for both operands.
		var result = CompileAndExecute("local x = 10; x += x; return x");
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void AddAssign_Zero()
	{
		var result = CompileAndExecute("local x = 42; x += 0; return x");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void SubAssign_Self()
	{
		var result = CompileAndExecute("local x = 42; x -= x; return x");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Augassignment_AfterRegularAssignment()
	{
		var result = CompileAndExecute(@"
            local x = 1
            x = 10
            x += 5
            return x
        ");
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Augassignment_WithTableLiteralIndex()
	{
		var result = CompileAndExecute("local t = {[10] = 5}; t[10] += 3; return t[10]");
		Assert.Equal(8.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ConcatAssign_WithNumberToString()
	{
		var result = CompileAndExecute("local s = 'value: '; s ..= 42; return s");
		Assert.Equal("value: 42", Assert.IsType<LuaString>(result.First).Value);
	}
}
