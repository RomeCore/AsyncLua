using AsyncLua.Compiling;
using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// Stress tests for register allocation: verifies that the compiler correctly reuses
/// temporary registers across statements and handles functions with many local variables
/// without exceeding the byte-sized register limit (255).
/// </summary>
public class RegisterStressTests
{
	private static LuaState CreateState()
	{
		return new LuaState().LoadDefaultLibraries();
	}

	private static LuaTuple Execute(LuaState state, string code)
	{
		return state.Execute(code);
	}

	private static async Task<LuaTuple> ExecuteAsync(LuaState state, string code)
	{
		return await state.ExecuteAsync(code);
	}

	// ═══════════════════════════════════════════════════════════════
	// REGISTER REUSE ACROSS MANY STATEMENTS
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Executes a function with 200 sequential arithmetic statements,
	/// each using temporary registers. Verifies that temporary registers
	/// are reused across statements (otherwise 200+ registers would overflow byte).
	/// </summary>
	[Fact]
	public void ManyArithmeticStatements_ReusesRegisters_ReturnsCorrectResult()
	{
		var state = CreateState();

		// Generate code with 200 statements, each a simple arithmetic operation.
		var code = new System.Text.StringBuilder();
		code.AppendLine("local x = 0");
		for (int i = 1; i <= 200; i++)
		{
			// Each statement uses temporaries: x = (x + i) * 2 - i
			code.AppendLine($"x = (x + {i}) * 2 - {i}");
		}
		code.AppendLine("return x");

		var result = Execute(state, code.ToString());

		// Expected: (((0+1)*2-1)+2)*2-2 ... let's compute
		// x = (x + i) * 2 - i = 2*x + 2*i - i = 2*x + i
		// So after n iterations starting from x=0:
		// x1 = 2*0 + 1 = 1
		// x2 = 2*1 + 2 = 4
		// x3 = 2*4 + 3 = 11
		// x4 = 2*11 + 4 = 26
		// This grows very fast! Let's do a simpler formula:
		// x = (x + i) * 2 - i = 2x + i
		// Sum(2^(n-i) * i) for i = 1..n
		// For n=200 this is astronomically large.
		// Let's just verify the code compiles and executes without errors.
		// The value will be checked against double precision.
		double expected = 0;
		for (int i = 1; i <= 200; i++)
			expected = 2 * expected + i;

		Assert.Single(result);
		// Allow tolerance for floating-point rounding on huge numbers.
		Assert.Equal(expected, Assert.IsType<LuaNumber>(result[0]).Value, expected * 1e-14);
	}

	// ═══════════════════════════════════════════════════════════════
	// MANY LOCAL VARIABLES
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Declares 200 local variables with initial values and sums them all.
	/// Each variable occupies its own register; verifies that 200 locals
	/// fit within the 255-register limit and don't collide with temporaries.
	/// </summary>
	[Fact]
	public void ManyLocalVariables_AllRegistersFitWithinByteLimit()
	{
		var state = CreateState();

		var code = new System.Text.StringBuilder();
		code.AppendLine("local sum = 0");

		// Declare 200 locals: v0..v199 each with a temporary-heavy initializer.
		for (int i = 0; i < 200; i++)
		{
			// Expression with temporaries: (i * 3 + 1) * 2
			code.AppendLine($"local v{i} = ({i} * 3 + 1) * 2");
			code.AppendLine($"sum = sum + v{i}");
		}
		code.AppendLine("return sum");

		var result = Execute(state, code.ToString());

		// v{i} = (i * 3 + 1) * 2 = 6*i + 2
		// sum = Σ(6*i + 2) for i = 0..199
		// = 6 * Σi + 2 * 200
		// = 6 * (199*200/2) + 400
		// = 6 * 19900 + 400 = 119400 + 400 = 119800
		double expected = 119800.0;

		Assert.Single(result);
		Assert.Equal(expected, Assert.IsType<LuaNumber>(result[0]).Value, 0.0);
	}

	// ═══════════════════════════════════════════════════════════════
	// MIXED LOCALS AND TEMPORARIES
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Defines 100 local variables, then performs 100 mixed arithmetic
	/// operations using various pairs of locals. This stresses both
	/// local variable register reservation and temporary register reuse.
	/// </summary>
	[Fact]
	public void ManyLocalsWithComplexExpressions_AllRegistersFitWithinByteLimit()
	{
		var state = CreateState();

		var code = new System.Text.StringBuilder();

		// Declare 100 locals with values 0..99.
		for (int i = 0; i < 100; i++)
			code.AppendLine($"local a{i} = {i}");

		// Compute pairwise sums into a result accumulator.
		code.AppendLine("local result = 0");
		for (int i = 0; i < 100; i += 2)
		{
			// Complex expression with multiple temporaries:
			// result = result + (a{i} + a{i+1}) * (a{i} - a{i+1}) / 2
			code.AppendLine($"result = result + (a{i} + a{i + 1}) * (a{i} - a{i + 1}) / 2");
		}
		code.AppendLine("return result");

		var result = Execute(state, code.ToString());

		// a{i} = i, a{i+1} = i+1
		// (i + (i+1)) * (i - (i+1)) / 2
		// = (2i+1) * (-1) / 2
		// = -(2i+1) / 2
		// Sum for i = 0, 2, 4, ..., 98: Σ -(2i+1)/2
		// = -1/2 * Σ(2i+1) for i in {0,2,4,...,98}
		// 50 terms: sum = -0.5 * (2*(0+2+...+98) + 50)
		// 0+2+...+98 = 2*(0+1+...+49) = 2*49*50/2 = 2450
		// sum = -0.5 * (2*2450 + 50) = -0.5 * (4900 + 50) = -0.5 * 4950 = -2475
		double expected = -2475.0;

		Assert.Single(result);
		Assert.Equal(expected, Assert.IsType<LuaNumber>(result[0]).Value, 0.0);
	}

	// ═══════════════════════════════════════════════════════════════
	// ASYNC STRESS
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Async variant: many locals and temporaries in an async context,
	/// exercising the async function compilation path.
	/// </summary>
	[Fact]
	public async Task AsyncManyLocals_AllRegistersFitWithinByteLimit()
	{
		var state = CreateState();

		var code = new System.Text.StringBuilder();
		code.AppendLine("local sum = 0");
		for (int i = 0; i < 200; i++)
		{
			code.AppendLine($"local v{i} = ({i} * 3 + 1) * 2");
			code.AppendLine($"sum = sum + v{i}");
		}
		code.AppendLine("return sum");

		var result = await ExecuteAsync(state, code.ToString());

		double expected = 119800.0;
		Assert.Single(result);
		Assert.Equal(expected, Assert.IsType<LuaNumber>(result[0]).Value, 0.0);
	}

	// ═══════════════════════════════════════════════════════════════
	// NESTED BLOCKS WITH TEMPORARIES
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Deeply nested do-blocks each allocating temporary registers.
	/// Verifies that scope-based register reset works correctly at all nesting levels.
	/// </summary>
	[Fact]
	public void NestedBlocks_TemporariesReusedAcrossScopes()
	{
		var state = CreateState();

		// Build: do local x = (1+2)*3; do local y = (x+4)*5; ... end end
		// With many nesting levels.
		const int depth = 100;
		var code = new System.Text.StringBuilder();
		code.AppendLine("local result = 1");

		for (int i = 0; i < depth; i++)
		{
			code.AppendLine($"do");
			code.AppendLine($"  local t{i} = (result + {i}) * 2");
			code.AppendLine($"  result = t{i}");
		}
		for (int i = 0; i < depth; i++)
			code.AppendLine("end");

		code.AppendLine("return result");

		var result = Execute(state, code.ToString());

		// result starts at 1. For i=0..99:
		// result = (result + i) * 2
		// This grows exponentially. Let's just verify it compiles and runs.
		// We can compute expected value iteratively.
		double expected = 1;
		for (int i = 0; i < depth; i++)
			expected = (expected + i) * 2;

		Assert.Single(result);
		Assert.Equal(expected, Assert.IsType<LuaNumber>(result[0]).Value, 0.0);
	}

	// ═══════════════════════════════════════════════════════════════
	// CALL STRESS — MANY FUNCTION CALLS WITH TEMPORARIES
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Calls a function 300 times in a loop, each call using temporary registers.
	/// Verifies that the register allocator does not overflow when compiling
	/// many sequential call statements.
	/// </summary>
	[Fact]
	public void ManySequentialFunctionCalls_TemporariesReused()
	{
		var state = CreateState();

		var code = new System.Text.StringBuilder();
		code.AppendLine("local function add(a, b) return a + b end");
		code.AppendLine("local sum = 0");
		for (int i = 0; i < 300; i++)
		{
			code.AppendLine($"sum = add(sum, {i})");
		}
		code.AppendLine("return sum");

		var result = Execute(state, code.ToString());

		// Sum of 0..299 = 299*300/2 = 44850
		double expected = 44850.0;
		Assert.Single(result);
		Assert.Equal(expected, Assert.IsType<LuaNumber>(result[0]).Value, 0.0);
	}

	// ═══════════════════════════════════════════════════════════════
	// MAXIMUM LOCAL VARIABLES
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Declares 240 local variables (leaving room for ~15 temporary registers below the byte limit of 255),
	/// verifying that the register allocator can handle near-maximum
	/// local usage with temporaries still functioning.
	/// </summary>
	[Fact]
	public void MaximumLocalVariables_DoesNotOverflow()
	{
		var state = CreateState();

		var code = new System.Text.StringBuilder();

		// Declare 240 locals (leaves headroom for temporaries within the 255-register limit).
		for (int i = 0; i < 240; i++)
			code.AppendLine($"local v{i} = {i}");

		// Use them in a temporary-heavy expression.
		code.AppendLine("local result = 0");
		code.AppendLine("result = (v0 + v1 + v2 + v3 + v4) * (v5 + v6 + v7 + v8 + v9) / 5");

		// Also access a late-bound local to ensure high register indices work.
		code.AppendLine("result = result + v239");
		code.AppendLine("return result");

		var result = Execute(state, code.ToString());

		// v0..v4 = 0,1,2,3,4 -> sum = 10
		// v5..v9 = 5,6,7,8,9 -> sum = 35
		// 10 * 35 / 5 = 70
		// + v239 = 70 + 239 = 309
		double expected = 309.0;
		Assert.Single(result);
		Assert.Equal(expected, Assert.IsType<LuaNumber>(result[0]).Value, 0.0);
	}

	// ═══════════════════════════════════════════════════════════════
	// REGISTER OVERFLOW DETECTION
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Declares 256 local variables, exceeding the byte-sized register limit (255).
	/// Verifies that <see cref="LuaCompilerException"/> is thrown with a descriptive message
	/// instead of silent register wrap-around.
	/// </summary>
	[Fact]
	public void TooManyLocals_ThrowsRegisterOverflowException()
	{
		var state = CreateState();

		var code = new System.Text.StringBuilder();
		// 256 locals will overflow: registers 0..255 = 256 slots, but max index is 255 (byte).
		for (int i = 0; i < 256; i++)
			code.AppendLine($"local v{i} = {i}");
		code.AppendLine("return 0");

		var ex = Assert.Throws<LuaCompilerException>(() => Execute(state, code.ToString()));
		Assert.Contains("Register allocation overflow", ex.Message);
	}

	/// <summary>
	/// Declares 250 local variables, then uses a complex expression that requires
	/// more than 5 temporary registers, exceeding the 255 limit.
	/// Verifies that temporaries also trigger the overflow exception.
	/// </summary>
	[Fact]
	public void ManyLocalsPlusComplexExpression_ThrowsRegisterOverflowException()
	{
		var state = CreateState();

		var code = new System.Text.StringBuilder();
		// 250 locals consume registers 0..249.
		for (int i = 0; i < 250; i++)
			code.AppendLine($"local v{i} = {i}");

		// A large arithmetic expression that requires many temporary registers.
		// (v0+v1+v2+...+v19) * (v20+v21+...+v39) uses ~18+18 temp regs → overflows.
		code.Append("local result = (");
		for (int i = 0; i < 20; i++)
		{
			if (i > 0) code.Append(" + ");
			code.Append($"v{i}");
		}
		code.Append(") * (");
		for (int i = 20; i < 40; i++)
		{
			if (i > 20) code.Append(" + ");
			code.Append($"v{i}");
		}
		code.AppendLine(")");
		code.AppendLine("return result");

		var ex = Assert.Throws<LuaCompilerException>(() => Execute(state, code.ToString()));
		Assert.Contains("Register allocation overflow", ex.Message);
	}

	/// <summary>
	/// Deeply nested expression tree forces many simultaneous temporary registers,
	/// exceeding the 255 limit even with fewer locals.
	/// </summary>
	[Fact]
	public void DeeplyNestedExpression_ThrowsRegisterOverflowException()
	{
		var state = CreateState();

		// Build: ((((1+2)+(3+4))+((5+6)+(7+8)))+...)
		// A balanced binary tree of depth ~9 has ~512 leaves → > 255 intermediate nodes.
		// But we can just chain 256 additions: v0 + v1 + v2 + ... + v255
		var code = new System.Text.StringBuilder();
		code.Append("local result = 0");
		for (int i = 1; i <= 256; i++)
			code.Append($" + {i}");
		code.AppendLine();
		code.AppendLine("return result");

		var ex = Assert.Throws<LuaCompilerException>(() => Execute(state, code.ToString()));
		Assert.Contains("Register allocation overflow", ex.Message);
	}
}
