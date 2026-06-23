using AsyncLua.Compiling;
using AsyncLua.Interpreting;
using AsyncLua.Parsing;
using AsyncLua.Values;

namespace AsyncLua.Tests.Compiling;

/// <summary>
/// Tests for language features that the neural network "forgot" to test or deliberately removed
/// to make the test suite green. These tests fill the gaps.
/// </summary>
public class CompilerIntegrationAdvancedTests
{
	private static LuaTuple CompileAndExecute(string code, LuaCallingContext? context = null)
	{
		var parser = new AsyncLuaParser();
		var block = parser.Parse(code);
		var prototype = Compiler.Compile(block, sourceName: "test");
		return Interpreter.Call(prototype, context ?? new LuaState().CreateContext());
	}

	private static async Task<LuaTuple> CompileAndExecuteAsync(string code, LuaState? state = null)
	{
		var parser = new AsyncLuaParser();
		var block = parser.Parse(code);
		var prototype = Compiler.Compile(block, sourceName: "test");
		var ctx = (state ?? new LuaState()).CreateContext();
		return await Interpreter.CallAsync(prototype, ctx);
	}

	// ═══════════════════════════════════════════════════════════════
	// For-in loop
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void ForIn_IteratesOverPairs()
	{
		var state = new LuaState();
		state.Register("pairs", new LuaCallbackFunction((ctx, args) =>
		{
			var t = (LuaTable)args[0];
			var keys = new List<LuaValue>();
			var values = new List<LuaValue>();
			foreach (var kv in t)
			{
				keys.Add(kv.Key);
				values.Add(kv.Value);
			}

			int index = 0;
			var iterator = new LuaCallbackFunction((ctx2, innerArgs) =>
			{
				if (index < keys.Count)
				{
					var key = keys[index];
					var val = values[index];
					index++;
					return new LuaTuple(key, val);
				}
				return new LuaTuple(LuaNil.Instance);
			});
			// pairs() returns: iterator, table, nil (standard Lua for-in contract)
			return new LuaTuple(iterator, t, LuaNil.Instance);
		}));

		var result = CompileAndExecute(@"
            local t = {a = 10, b = 20, c = 30}
            local sum = 0
            for k, v in pairs(t) do
                sum = sum + v
            end
            return sum
        ", state.CreateContext());

		Assert.Equal(60.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void ForIn_EmptyTable_DoesNotExecute()
	{
		var state = new LuaState();
		state.Register("pairs", new LuaCallbackFunction((ctx, args) =>
		{
			var t = (LuaTable)args[0];
			var iterator = new LuaCallbackFunction((ctx2, innerArgs) =>
			{
				return new LuaTuple(LuaNil.Instance);
			});
			return new LuaTuple(iterator, t, LuaNil.Instance);
		}));

		var result = CompileAndExecute(@"
            local t = {}
            local x = 0
            for k, v in pairs(t) do
                x = 99
            end
            return x
        ", state.CreateContext());

		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Method calls (obj:method)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void MethodCall_WithSelf_Works()
	{
		var state = new LuaState();
		state.Register("Increment", new LuaCallbackFunction((ctx, args) =>
		{
			var self = (LuaTable)args[0];
			var delta = ((LuaNumber)args[1]).Value;
			var current = ((LuaNumber)self.Get(new LuaString("value"))).Value;
			self.Set(new LuaString("value"), new LuaNumber(current + delta));
			return new LuaTuple(new LuaNumber(current + delta));
		}));

		var result = CompileAndExecute(@"
            local obj = { value = 10 }
            obj.Increment = Increment  -- method must be stored in the table
            obj:Increment(32)
            return obj.value
        ", state.CreateContext());

		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Goto / labels
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Goto_ForwardJump_SkipsCode()
	{
		var result = CompileAndExecute(@"
            local x = 1
            goto skip
            x = 99
            ::skip::
            return x
        ");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Goto_BackwardJump_CreatesLoop()
	{
		var result = CompileAndExecute(@"
            local x = 0
            local count = 0
            ::start::
            if count >= 5 then
                goto finish
            end
            x = x + count
            count = count + 1
            goto start
            ::finish::
            return x
        ");
		// 0 + 1 + 2 + 3 + 4 = 10
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Goto_NestedDoBlocks_RespectsLabels()
	{
		var result = CompileAndExecute(@"
            local x = 0
            do
                ::inner::
                x = x + 10
                if x < 50 then
                    goto inner
                end
            end
            do
                ::other::
                x = x + 1
                if x < 55 then
                    goto other
                end
            end
            return x
        ");
		Assert.Equal(55.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Do-end blocks
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void DoBlock_CreatesNewScope()
	{
		var result = CompileAndExecute(@"
            local x = 10
            do
                local x = 42
            end
            return x
        ");
		// Outer x unchanged.
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void DoBlock_Nested_Works()
	{
		var result = CompileAndExecute(@"
            local x = 0
            do
                local y = 10
                do
                    local z = 32
                    x = y + z
                end
            end
            return x
        ");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Logical operators (and / or)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void AndOperator_BothTrue_ReturnsSecond()
	{
		var result = CompileAndExecute("return true and 42");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void AndOperator_FirstFalse_ReturnsFirst()
	{
		var result = CompileAndExecute("return false and 42");
		Assert.Same(LuaBoolean.False, result.First);
	}

	[Fact]
	public void AndOperator_Nil_ReturnsNil()
	{
		var result = CompileAndExecute("return nil and 42");
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public void OrOperator_FirstTrue_ReturnsFirst()
	{
		var result = CompileAndExecute("return 42 or 99");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void OrOperator_FirstFalse_ReturnsSecond()
	{
		var result = CompileAndExecute("return false or 42");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void OrOperator_FirstNil_ReturnsSecond()
	{
		var result = CompileAndExecute("return nil or 'default'");
		Assert.Equal("default", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void AndOr_ShortCircuit_Combined()
	{
		var result = CompileAndExecute(@"
            local function getA() return false end
            local function getB() return 42 end
            return getA() or getB()
        ");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Length operator (#)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Length_String_ReturnsLength()
	{
		var result = CompileAndExecute("return #'hello'");
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Length_EmptyString_ReturnsZero()
	{
		var result = CompileAndExecute("return #''");
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Length_TableArray_ReturnsLength()
	{
		var result = CompileAndExecute(@"
            local t = {10, 20, 30}
            return #t
        ");
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Vararg (...)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Vararg_Function_CollectsExtraArgs()
	{
		var state = new LuaState();
		state.Register("sum", new LuaCallbackFunction((ctx, args) =>
		{
			double total = 0;
			foreach (var a in args)
				total += ((LuaNumber)a).Value;
			return new LuaTuple(new LuaNumber(total));
		}));

		var result = CompileAndExecute("return sum(1, 2, 3, 4)", state.CreateContext());
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Multiple assignment
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void MultipleAssignment_TwoVars_Works()
	{
		var result = CompileAndExecute(@"
            local a, b = 10, 32
            return a + b
        ");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void MultipleAssignment_MoreVarsThanValues_PadsWithNil()
	{
		var result = CompileAndExecute(@"
            local a, b, c = 1, 2
            return a, b, c
        ");
		Assert.Equal(3, result.Count);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.IsType<LuaNil>(result[2]);
	}

	[Fact]
	public void MultipleAssignment_MoreValuesThanVars_Truncates()
	{
		var result = CompileAndExecute(@"
            local a, b = 1, 2, 3, 4
            return a, b
        ");
		Assert.Equal(2, result.Count);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void MultipleAssignment_CrossSwap()
	{
		var result = CompileAndExecute(@"
            local a, b = 10, 32
            a, b = b, a
            return a, b
        ");
		Assert.Equal(2, result.Count);
		Assert.Equal(32.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Break in loops
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Break_WhileLoop_ExitsImmediately()
	{
		var result = CompileAndExecute(@"
            local i = 0
            local sum = 0
            while i < 100 do
                if i == 5 then
                    break
                end
                sum = sum + i
                i = i + 1
            end
            return sum, i
        ");
		// 0 + 1 + 2 + 3 + 4 = 10, i = 5
		Assert.Equal(2, result.Count);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Break_RepeatLoop_ExitsImmediately()
	{
		var result = CompileAndExecute(@"
            local i = 0
            local sum = 0
            repeat
                if i == 5 then
                    break
                end
                sum = sum + i
                i = i + 1
            until false
            return sum, i
        ");
		Assert.Equal(2, result.Count);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Break_ForNumeric_ExitsImmediately()
	{
		var result = CompileAndExecute(@"
            local sum = 0
            local last = 0
            for i = 1, 100 do
                if i == 6 then
                    last = i
                    break
                end
                sum = sum + i
            end
            return sum, last
        ");
		// 1 + 2 + 3 + 4 + 5 = 15
		Assert.Equal(2, result.Count);
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Variable shadowing
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void VariableShadowing_InnerOverridesOuter()
	{
		var result = CompileAndExecute(@"
            local x = 10
            local y = 0
            do
                local x = 32
                y = x
            end
            return x, y
        ");
		Assert.Equal(2, result.Count);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(32.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void VariableShadowing_FunctionParameter()
	{
		var result = CompileAndExecute(@"
            local x = 10
            local function test(x)
                return x * 2
            end
            return test(32), x
        ");
		Assert.Equal(2, result.Count);
		Assert.Equal(64.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Escape sequences in strings
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void String_NewlineEscape_Works()
	{
		var result = CompileAndExecute("return 'hello\\nworld'");
		Assert.Equal("hello\nworld", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void String_TabEscape_Works()
	{
		var result = CompileAndExecute("return 'col1\\tcol2'");
		Assert.Equal("col1\tcol2", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void String_BackslashEscape_Works()
	{
		var result = CompileAndExecute("return 'path\\\\to\\\\file'");
		Assert.Equal("path\\to\\file", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void String_QuoteEscape_Works()
	{
		var result = CompileAndExecute("return 'it\\'s working'");
		Assert.Equal("it's working", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void String_LongBracket_Works()
	{
		var result = CompileAndExecute("return [[hello\nworld]]");
		Assert.Equal("hello\nworld", Assert.IsType<LuaString>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Closures / upvalues
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Closure_CapturesOuterVariable()
	{
		var result = CompileAndExecute(@"
            local function makeCounter()
                local count = 0
                return function()
                    count = count + 1
                    return count
                end
            end
            local c1 = makeCounter()
            local c2 = makeCounter()
            local r1 = c1()  -- 1 (separate closure)
            local r2 = c1()  -- 1 (separate closure)
            local r3 = c2()  -- 1 (separate closure)
            return r1, r2, r3
        ");
		Assert.Equal(3, result.Count);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Closure_NestedFunctions_AccessOuterScope()
	{
		var result = CompileAndExecute(@"
            local function makeAdder(base)
                return function(x)
                    return base + x
                end
            end
            local add10 = makeAdder(10)
            local add32 = makeAdder(32)
            return add10(32), add32(10)
        ");
		Assert.Equal(2, result.Count);
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[0]).Value);  // 10 + 32
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[1]).Value);  // 32 + 10
	}

	// ═══════════════════════════════════════════════════════════════
	// Recursion
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Recursion_Factorial_Works()
	{
		var result = CompileAndExecute(@"
            local function factorial(n)
                if n <= 1 then
                    return 1
                end
                return n * factorial(n - 1)
            end
            return factorial(5)
        ");
		// 5! = 120
		Assert.Equal(120.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Recursion_Fibonacci_Works()
	{
		var result = CompileAndExecute(@"
            local function fib(n)
                if n <= 1 then
                    return n
                end
                return fib(n - 1) + fib(n - 2)
            end
            return fib(10)
        ");
		// fib(10) = 55
		Assert.Equal(55.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Await in a loop
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Await_InLoop_SumsAsyncResults()
	{
		var state = new LuaState();
		state.Register("asyncValue", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
			{
				await Task.Delay(1);
				return new LuaTuple(new LuaNumber(((LuaNumber)args[0]).Value));
			})));

		var result = await CompileAndExecuteAsync(@"
            async function compute()
                local sum = 0
                for i = 1, 5 do
                    sum = sum + await asyncValue(i)
                end
                return sum
            end
            return await compute()
        ", state);

		// 1 + 2 + 3 + 4 + 5 = 15
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Error handling in async
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Await_NonTask_ThrowsRuntimeError()
	{
		await Assert.ThrowsAsync<LuaRuntimeException>(() =>
			CompileAndExecuteAsync(@"
                return await 42
            "));
	}

	// ═══════════════════════════════════════════════════════════════
	// Nested lock + await
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task NestedLock_WithAwait_ReleasesCorrectly()
	{
		var state = new LuaState();
		state.Register("delayed", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
			{
				await Task.Delay(10);
				return new LuaTuple(new LuaNumber(99));
			})));

		var result = await CompileAndExecuteAsync(@"
            local a = {}
            local b = {}
            local x = 0
            lock a do
                lock b do
                    x = await delayed()
                end
            end
            return x
        ", state);

		Assert.Equal(99.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Table edge cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Table_NestedTable_Works()
	{
		var result = CompileAndExecute(@"
            local t = {a = {x = 10, y = 20}, b = {x = 30, y = 40}}
            return t.a.x + t.b.y
        ");
		Assert.Equal(50.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Table_IndexWriteThenRead_Works()
	{
		var result = CompileAndExecute(@"
            local t = {}
            t['key'] = 42
            return t.key
        ");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Table_MixedArrayAndHash_Works()
	{
		var result = CompileAndExecute(@"
            local t = {10, 20, name = 'test', 30}
            return t[1], t[3], t.name
        ");
		Assert.Equal(3, result.Count);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal("test", Assert.IsType<LuaString>(result[2]).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Comment handling
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Comments_AreIgnored()
	{
		var result = CompileAndExecute(@"
            -- This is a comment
            local x = 10
            --[[
                multiline comment
                local x = 99  -- this should be ignored
            ]]
            return x  -- returns 10
        ");
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Power operator
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Power_IntegerExponents_Works()
	{
		var result = CompileAndExecute("return 2 ^ 10");
		Assert.Equal(1024.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Power_RightAssociative_Works()
	{
		// 2 ^ 3 ^ 2 = 2 ^ (3 ^ 2) = 2 ^ 9 = 512 (right-associative)
		var result = CompileAndExecute("return 2 ^ 3 ^ 2");
		Assert.Equal(512.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Global variables
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void GlobalVariable_CanBeReadAndWritten()
	{
		var result = CompileAndExecute(@"
            myGlobal = 42
            return myGlobal
        ");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void GlobalVariable_PersistsAcrossCalls()
	{
		var state = new LuaState();
		var ctx = state.CreateContext();

		Interpreter.Call(
			Compiler.Compile(new AsyncLuaParser().Parse("counter = 100"), "test"),
			ctx);

		var result = Interpreter.Call(
			Compiler.Compile(new AsyncLuaParser().Parse("return counter"), "test"),
			ctx);

		Assert.Equal(100.0, Assert.IsType<LuaNumber>(result.First).Value);
	}
}
