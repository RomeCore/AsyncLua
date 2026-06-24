using AsyncLua.Compiling;
using AsyncLua.Interpreting;
using AsyncLua.Parsing;
using AsyncLua.Values;

namespace AsyncLua.Tests.Compiling;

public class TryCatchCompilerTests
{
	private static LuaTuple CompileAndExecute(string code, LuaCallingContext? context = null)
	{
		var parser = new AsyncLuaParser();
		var block = parser.Parse(code);
		var prototype = AsyncLuaCompiler.Compile(block, sourceName: "test");
		return AsyncLuaInterpreter.Call(prototype, context ?? new LuaState().CreateContext());
	}

	// ═══════════════════════════════════════════════════════════════
	// Basic try-catch
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void TryCatch_NoException_ReturnsTryValue()
	{
		var result = CompileAndExecute(@"
            try
                result = 42
            catch e do
                result = 0
            end
            return result
        ");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void TryCatch_ExceptionCaught_ReturnsCatchValue()
	{
		var result = CompileAndExecute(@"
            try
                throw 'something went wrong'
                result = 0
            catch e do
                result = 99
            end
            return result
        ");
		Assert.Equal(99.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void TryCatch_ExceptionMessage_Captured()
	{
		var result = CompileAndExecute(@"
            try
                throw 'test error'
            catch e do
                result = e
            end
            return result
        ");
		Assert.Equal("test error", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void TryCatch_NoException_CatchBodySkipped()
	{
		var result = CompileAndExecute(@"
            try
                result = 'try'
            catch e do
                result = 'catch'
            end
            return result
        ");
		Assert.Equal("try", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void TryCatch_ThrowFromCatch_Propagates()
	{
		// Throw inside catch should propagate (not be caught by the same handler)
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			CompileAndExecute(@"
                try
                    throw 'first'
                catch e do
                    result = 'caught: ' .. e
                    throw 'from catch'
                end
                return result
            "));

		Assert.Contains("from catch", ex.Message);
	}

	// ═══════════════════════════════════════════════════════════════
	// Nested try-catch
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void NestedTryCatch_InnerCatchesFirst()
	{
		var result = CompileAndExecute(@"
            try
                try
                    throw 'inner'
                catch e do
                    result = 'inner: ' .. e
                end
                result = result .. ' + after'
            catch e do
                result = 'outer: ' .. e
            end
            return result
        ");
		Assert.Equal("inner: inner + after", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void NestedTryCatch_OuterCatchesIfThrowFromCatch()
	{
		var result = CompileAndExecute(@"
            try
                try
                    throw 'first'
                catch e do
                    throw 'from inner catch: ' .. e
                end
            catch e do
                result = 'caught: ' .. e
            end
            return result
        ");
		Assert.Equal("caught: from inner catch: first", Assert.IsType<LuaString>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Throw statement
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Throw_Basic()
	{
		var result = CompileAndExecute(@"
            try
                throw 'my error'
            catch e do
                result = e
            end
            return result
        ");
		Assert.Equal("my error", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Throw_WithExpression()
	{
		var result = CompileAndExecute(@"
            local x = 42
            try
                throw 'error #' .. x
            catch e do
                result = e
            end
            return result
        ");
		Assert.Equal("error #42", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Throw_Uncaught_PropagatesAsLuaRuntimeException()
	{
		var state = new LuaState();
		var ctx = state.CreateContext();

		var ex = Assert.Throws<LuaRuntimeException>(() =>
			CompileAndExecute("throw 'unhandled'", ctx));

		Assert.Contains("unhandled", ex.Message);
	}

	// ═══════════════════════════════════════════════════════════════
	// Try-catch without error variable (catch without identifier)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void TryCatch_WithoutVariable_Works()
	{
		var result = CompileAndExecute(@"
            try
                throw 'err'
            catch
                result = 42
            end
            return result
        ");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Try-catch in loop
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void TryCatch_InLoop_Works()
	{
		var result = CompileAndExecute(@"
            local sum = 0
            for i = 1, 3 do
                try
                    if i == 2 then
                        throw 'skip ' .. i
                    end
                    sum = sum + i
                catch e do
                    sum = sum - 1
                end
            end
            return sum
        ");
		// i=1: sum=1
		// i=2: error, sum=0
		// i=3: sum=3
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple try-catch in sequence
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void MultipleTryCatch_Sequence()
	{
		var result = CompileAndExecute(@"
            try
                throw 'first'
            catch e do
                result1 = e
            end

            try
                throw 'second'
            catch e do
                result2 = e
            end

            return result1, result2
        ");
		Assert.Equal(2, result.Count);
		Assert.Equal("first", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("second", Assert.IsType<LuaString>(result[1]).Value);
	}
}
