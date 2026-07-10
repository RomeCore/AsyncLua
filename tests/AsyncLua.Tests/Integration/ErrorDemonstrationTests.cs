using System;
using AsyncLua.Compiling;
using Xunit;
using Xunit.Abstractions;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// Demonstrates various runtime and compile-time errors with visual position indicators
/// using <see cref="ITestOutputHelper"/>.
/// </summary>
public class ErrorDemonstrationTests(ITestOutputHelper output)
{
	private static LuaState CreateState()
	{
		return new LuaState().LoadDefaultLibraries();
	}

	/// <summary>
	/// Writes the error details to the test output, including the visual position indicator
	/// from the source code if available.
	/// </summary>
	private void WriteError(Exception ex, string code)
	{
		output.WriteLine("╔═══════════════════════════════════════════");
		output.WriteLine($"║ Exception: {ex.GetType().Name}");
		output.WriteLine($"║ Message:   {ex.Message.Split('\n')[0]}");
		output.WriteLine("╚═══════════════════════════════════════════");
		output.WriteLine("");

		// Try to extract positional info and show visual code pointer
		if (ex is LuaCompilerException compilerEx && compilerEx.HasPosition)
		{
			output.WriteLine("Position in source code:");
			output.WriteLine(compilerEx.Position.ToString());
		}
		else if (ex is LuaRuntimeException rtEx && rtEx.HasPosition)
		{
			output.WriteLine("Position in source code:");
			output.WriteLine(rtEx.Position.ToString());
		}
		else
		{
			// Fallback: show the full exception message (may include position)
			output.WriteLine("Full error:");
			output.WriteLine(ex.ToString());
		}

		output.WriteLine("");
		output.WriteLine("─────────────────────────────────────────────");
	}

	// ═══════════════════════════════════════════════════════════════
	// Compile-time errors (LuaCompilerException)
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Verifies that <c>break</c> outside any loop produces a compile-time error
	/// with visual source position.
	/// </summary>
	[Fact]
	public void BreakOutsideLoop_ShowsVisualPosition()
	{
		var code = "local x = 10\nbreak\nreturn x";

		var ex = Assert.Throws<LuaCompilerException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
		Assert.Contains("break", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Verifies that <c>continue</c> outside any loop produces a compile-time error
	/// with visual source position.
	/// </summary>
	[Fact]
	public void ContinueOutsideLoop_ShowsVisualPosition()
	{
		var code = "local i = 0\nwhile i < 5 do\n    i = i + 1\nend\ncontinue";

		var ex = Assert.Throws<LuaCompilerException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
		Assert.Contains("continue", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Verifies that <c>break</c> outside a loop inside a function produces a compile-time error.
	/// </summary>
	[Fact]
	public void BreakOutsideLoop_InFunction_ShowsVisualPosition()
	{
		var code = "function foo()\n    break\nend";

		var ex = Assert.Throws<LuaCompilerException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
		Assert.Contains("break", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	// ═══════════════════════════════════════════════════════════════
	// Runtime errors (LuaRuntimeException)
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Demonstrates a runtime error when performing arithmetic on <c>nil</c>.
	/// </summary>
	[Fact]
	public void ArithmeticOnNil_ShowsVisualPosition()
	{
		var code = "local x = nil\nreturn x + 1";

		var ex = Assert.Throws<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
	}

	/// <summary>
	/// Demonstrates a runtime error when calling a number as a function.
	/// </summary>
	[Fact]
	public void CallNonFunction_Number_ShowsVisualPosition()
	{
		var code = "return 42()";

		var ex = Assert.Throws<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
	}

	/// <summary>
	/// Demonstrates a runtime error when calling <c>nil</c> as a function.
	/// </summary>
	[Fact]
	public void CallNil_ShowsVisualPosition()
	{
		var code = "local f = nil\nf()";

		var ex = Assert.Throws<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
	}

	/// <summary>
	/// Demonstrates a runtime error when indexing <c>nil</c>.
	/// </summary>
	[Fact]
	public void IndexNil_ShowsVisualPosition()
	{
		var code = "local t = nil\nreturn t.x";

		var ex = Assert.Throws<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
	}

	/// <summary>
	/// Demonstrates a runtime error when trying to concatenate a table.
	/// </summary>
	[Fact]
	public void ConcatTable_ShowsVisualPosition()
	{
		var code = "local t = {}\nreturn 'hello' .. t";

		var ex = Assert.Throws<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
	}

	/// <summary>
	/// Demonstrates a runtime error when using <c>#</c> (length operator) on a non-table, non-string value.
	/// </summary>
	[Fact]
	public void LengthOnNumber_ShowsVisualPosition()
	{
		var code = "return #42";

		var ex = Assert.Throws<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
	}

	/// <summary>
	/// Demonstrates a runtime error from the <c>math</c> library with invalid arguments.
	/// </summary>
	[Fact]
	public void MathSqrtOnString_ShowsVisualPosition()
	{
		var code = "return math.sqrt('hello')";

		var ex = Assert.Throws<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
	}

	/// <summary>
	/// Demonstrates a runtime error from <c>string</c> library with missing arguments.
	/// </summary>
	[Fact]
	public void StringFormatNoArgs_ShowsVisualPosition()
	{
		var code = "return string.format()";

		var ex = Assert.Throws<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
	}

	/// <summary>
	/// Demonstrates a runtime error from <c>math</c> library: <c>math.random(10, 5)</c>
	/// where min > max.
	/// </summary>
	[Fact]
	public void MathRandomInvalidRange_ShowsVisualPosition()
	{
		var code = "return math.random(10, 5)";

		var ex = Assert.Throws<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
	}

	// ═══════════════════════════════════════════════════════════════
	// Async runtime errors
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Demonstrates a runtime error when awaiting a non-task value.
	/// </summary>
	[Fact]
	public async Task AwaitNonTask_ShowsVisualPosition()
	{
		var code = "return await 42";

		var ex = await Assert.ThrowsAsync<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			return state.ExecuteAsync(code);
		});

		WriteError(ex, code);
	}

	/// <summary>
	/// Demonstrates an uncaught exception inside an async function.
	/// </summary>
	[Fact]
	public async Task UncaughtExceptionInAsync_ShowsVisualPosition()
	{
		var code = @"
            async function risky()
                error('something went wrong')
            end
            await risky()";

		var ex = await Assert.ThrowsAsync<LuaRuntimeException>(() =>
		{
			var state = CreateState();
			return state.ExecuteAsync(code);
		});

		WriteError(ex, code);
	}

	// ═══════════════════════════════════════════════════════════════
	// Mixed: compile-time error in async code
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Demonstrates that compile-time errors (like <c>break</c> outside a loop)
	/// are caught even in async code before execution.
	/// </summary>
	[Fact]
	public void BreakOutsideLoop_InAsyncCode_ShowsVisualPosition()
	{
		var code = @"
            async function test()
                break
            end";

		var ex = Assert.Throws<LuaCompilerException>(() =>
		{
			var state = CreateState();
			state.Execute(code);
		});

		WriteError(ex, code);
		Assert.Contains("break", ex.Message, StringComparison.OrdinalIgnoreCase);
	}
}
