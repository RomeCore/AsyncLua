using AsyncLua.Compiling;
using AsyncLua.Interpreting;
using AsyncLua.Parsing;
using AsyncLua.Values;

namespace AsyncLua.Tests.Compiling;

public class AsyncCompilerIntegrationTests
{
    private static async Task<LuaTuple> CompileAndExecuteAsync(string code, LuaState? state = null)
    {
        var parser = new AsyncLuaParser();
        var block = parser.Parse(code);
        var prototype = Compiler.Compile(block, sourceName: "test");
        var ctx = (state ?? new LuaState()).CreateContext();
        return await Interpreter.CallAsync(prototype, ctx);
    }

    // ═══════════════════════════════════════════════════════════════
    // Async function declarations
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AsyncFunction_Declaration_CompilesAndRuns()
    {
        var result = await CompileAndExecuteAsync(@"
            async function compute()
                return 42
            end
            return await compute()
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public async Task AsyncFunction_WithParameters_Works()
    {
        var result = await CompileAndExecuteAsync(@"
            async function multiply(a, b)
                return a * b
            end
            return await multiply(6, 7)
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public async Task AsyncFunction_LocalDeclaration_Works()
    {
        var result = await CompileAndExecuteAsync(@"
            local async function double(x)
                return x * 2
            end
            return await double(21)
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ═══════════════════════════════════════════════════════════════
    // Non-blocking async calls (concurrent execution)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AsyncCall_NonBlocking_TwoFunctionsRunConcurrently()
    {
        // Two C# async functions with barriers for precise control.
        var barrier1 = new TaskCompletionSource<bool>();
        var barrier2 = new TaskCompletionSource<bool>();

        var state = new LuaState();
        state.Register("fetch1", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
            {
                await barrier1.Task;
                return new LuaTuple(new LuaNumber(100));
            })));
        state.Register("fetch2", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
            {
                await barrier2.Task;
                return new LuaTuple(new LuaNumber(200));
            })));

        var ctx = state.CreateContext();

        // Lua code: call both async functions without awaiting.
        var code = @"
            local task1 = fetch1()
            local task2 = fetch2()
            _G['t1'] = task1
            _G['t2'] = task2
            return await task1, await task2
        ";

        var parser = new AsyncLuaParser();
        var block = parser.Parse(code);
        var prototype = Compiler.Compile(block, "test");

        var executeTask = Interpreter.CallAsync(prototype, ctx);

        // Give interpreter time to hit the first await.
        await Task.Delay(50);

        // Both tasks should be pending — calls were non-blocking.
        var t1 = Assert.IsType<LuaTask>(ctx.Globals.Get(new LuaString("t1")));
        var t2 = Assert.IsType<LuaTask>(ctx.Globals.Get(new LuaString("t2")));
        Assert.Equal(LuaTaskStatus.Pending, t1.Status);
        Assert.Equal(LuaTaskStatus.Pending, t2.Status);

        // Release barriers in reverse order.
        barrier2.SetResult(true);
        await Task.Delay(20);
        Assert.Equal(LuaTaskStatus.Pending, t1.Status);
        Assert.Equal(LuaTaskStatus.Completed, t2.Status);

        barrier1.SetResult(true);
        await Task.Delay(20);

        var result = await executeTask;
        Assert.Equal(2, result.Count);
        Assert.Equal(100.0, Assert.IsType<LuaNumber>(result[0]).Value);
        Assert.Equal(200.0, Assert.IsType<LuaNumber>(result[1]).Value);
    }

    [Fact]
    public async Task Await_ReturnsSingleValue()
    {
        var state = new LuaState();
        state.Register("getNumber", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
            {
                return Task.FromResult(new LuaTuple(new LuaNumber(42)));
            })));

        var result = await CompileAndExecuteAsync(@"
            return await getNumber()
        ", state);

        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ═══════════════════════════════════════════════════════════════
    // Await expression
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AwaitExpression_InAssignment_Works()
    {
        var state = new LuaState();
        state.Register("delayed", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
            {
                await Task.Delay(10);
                return new LuaTuple(new LuaNumber(77));
            })));

        var result = await CompileAndExecuteAsync(@"
            local x = await delayed()
            return x
        ", state);

        Assert.Equal(77.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public async Task AwaitExpression_InArithmetic_Works()
    {
        var state = new LuaState();
        state.Register("getValue", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(21))))));

        var result = await CompileAndExecuteAsync(@"
            return await getValue() * 2
        ", state);

        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ═══════════════════════════════════════════════════════════════
    // Lock statement
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Lock_Statement_CompilesAndRuns()
    {
        var state = new LuaState();

        var result = await CompileAndExecuteAsync(@"
            local obj = {}
            local value = 0
            lock obj do
                value = 42
            end
            return value
        ", state);

        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public async Task Lock_Nested_Works()
    {
        var result = await CompileAndExecuteAsync(@"
            local a = {}
            local b = {}
            local x = 0
            lock a do
                lock b do
                    x = 99
                end
            end
            return x
        ");
        Assert.Equal(99.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public async Task Lock_ReleasesOnReturn()
    {
        // Lock should be released even if return happens inside the block.
        var result = await CompileAndExecuteAsync(@"
            local obj = {}
            lock obj do
                return 42
            end
            return 0
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ═══════════════════════════════════════════════════════════════
    // Mixed sync + async
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Mixed_SyncAndAsyncFunctions_WorkTogether()
    {
        var state = new LuaState();

        // Sync function.
        state.Register("add", LuaCallbackFunction.From(
            (LuaValue[] args) => new LuaNumber(
                ((LuaNumber)args[0]).Value + ((LuaNumber)args[1]).Value)));

        // Async function.
        state.Register("multiply", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(
                    ((LuaNumber)args[0]).Value * ((LuaNumber)args[1]).Value))))));

        var result = await CompileAndExecuteAsync(@"
            local sum = add(10, 32)
            local product = await multiply(6, 7)
            return sum, product
        ", state);

        Assert.Equal(2, result.Count);
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[0]).Value);  // 10 + 32
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[1]).Value);  // 6 * 7
    }

    // ═══════════════════════════════════════════════════════════════
    // Concurrent timing test
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AsyncCall_ConcurrentExecution_OverlapsInTime()
    {
        async Task Execute(string code)
		{
			var barrier = new TaskCompletionSource<bool>();

			async Task<LuaTuple> DelayedFunc(LuaCallingContext ctx, LuaValue[] args)
			{
				await barrier.Task;
				await Task.Delay(100);
				return new LuaTuple(new LuaNumber(((LuaNumber)args[0]).Value * 2));
			}

			var state = new LuaState();
			state.Register("delayed", new LuaCallbackFunction(
				new LuaCallbackFunction.AsyncCallbackDelegate(DelayedFunc)));

			var ctx = state.CreateContext();

			var parser = new AsyncLuaParser();
			var block = parser.Parse(code);
			var prototype = Compiler.Compile(block, "test");

			var executeTask = Interpreter.CallAsync(prototype, ctx);
			await Task.Delay(50);

			// Both tasks pending — non-blocking calls.
			var t1 = Assert.IsType<LuaTask>(ctx.Globals.Get(new LuaString("t1")));
			var t2 = Assert.IsType<LuaTask>(ctx.Globals.Get(new LuaString("t2")));
			Assert.Equal(LuaTaskStatus.Pending, t1.Status);
			Assert.Equal(LuaTaskStatus.Pending, t2.Status);

			// Release barrier — both 100ms delays run concurrently.
			var sw = System.Diagnostics.Stopwatch.StartNew();
			barrier.SetResult(true);

			var result = await executeTask;
			sw.Stop();

			Assert.Equal(2, result.Count);
			Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[0]).Value);
			Assert.Equal(40.0, Assert.IsType<LuaNumber>(result[1]).Value);

			// Concurrent: total < 200ms (would be 200ms+ if sequential).
			Assert.True(sw.ElapsedMilliseconds < 200,
				$"Expected concurrent execution (< 200ms), took {sw.ElapsedMilliseconds}ms");
		}

        await Execute(@"
            local t1 = delayed(10)
            local t2 = delayed(20)
            _G['t1'] = t1
            _G['t2'] = t2
            local r1 = await t1
            local r2 = await t2
            return r1, r2
        ");

		await Execute(@"
            local t1 = delayed(10)
            local t2 = delayed(20)
            _G['t1'] = t1
            _G['t2'] = t2
            local a1, a2 = await t1, t2
            return a1, a2
        ");
	}

    // ═══════════════════════════════════════════════════════════════
    // Complex scenario: async + tables (lock omitted due to thread-affinity)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Complex_AsyncWithTable_Works()
    {
        var state = new LuaState();

        state.Register("fetchUser", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
            {
                await Task.Delay(10);
                var t = new LuaTable();
                t.Set(new LuaString("name"), new LuaString("Alice"));
                t.Set(new LuaString("age"), new LuaNumber(30));
                return new LuaTuple(t);
            })));

        var result = await CompileAndExecuteAsync(@"
            local cache = {}
            
            async function getUser(id)
                if cache[id] == nil then
                    cache[id] = await fetchUser(id)
                end
                return cache[id]
            end
            
            local user = await getUser(1)
            return user.name, user.age
        ", state);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alice", Assert.IsType<LuaString>(result[0]).Value);
        Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[1]).Value);
    }

    // ═══════════════════════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AsyncFunction_CallingAsyncCallback_Works()
    {
        var state = new LuaState();
        state.Register("getAnswer", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(42))))));

        // Call async C# callback directly (not wrapped in async Lua function).
        var result = await CompileAndExecuteAsync(@"
            return await getAnswer()
        ", state);

        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public async Task AsyncFunction_ChainedAwait_Works()
    {
        var state = new LuaState();
        state.Register("step1", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(10))))));
        state.Register("step2", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(
                    ((LuaNumber)args[0]).Value * 3))))));

        var result = await CompileAndExecuteAsync(@"
            async function pipeline()
                local a = await step1()
                local b = await step2(a)
                return b + 12
            end
            return await pipeline()
        ", state);

        // step1 → 10, step2(10) → 30, +12 → 42
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ═══════════════════════════════════════════════════════════════
    // Multi-await (await t1, t2)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Await_MultipleTasks_Statement_AwaitsAll()
    {
        var barrier1 = new TaskCompletionSource<bool>();
        var barrier2 = new TaskCompletionSource<bool>();

        var state = new LuaState();
        state.Register("task1", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
            {
                await barrier1.Task;
                return new LuaTuple(new LuaNumber(10));
            })));
        state.Register("task2", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
            {
                await barrier2.Task;
                return new LuaTuple(new LuaNumber(20));
            })));

        var ctx = state.CreateContext();
        var code = @"
            await task1(), task2()
            _G['done_t1'] = 'yes'
            return 42
        ";

        var parser = new AsyncLuaParser();
        var block = parser.Parse(code);
        var proto = Compiler.Compile(block, "test");

        var executeTask = Interpreter.CallAsync(proto, ctx);
        await Task.Delay(50);

        // Both tasks should be pending — CALL was non-blocking.
        // The AWAIT awaits both, and execution should not have reached _G['done_t1'] yet.
        Assert.IsType<LuaNil>(ctx.Globals.Get(new LuaString("done_t1")));

        // Release first task — should NOT unblock (still waiting on task2).
        barrier1.SetResult(true);
        await Task.Delay(20);
        Assert.IsType<LuaNil>(ctx.Globals.Get(new LuaString("done_t1")));

        // Release second task — now both done, execution proceeds.
        barrier2.SetResult(true);
        await Task.Delay(20);

        var result = await executeTask;
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
        Assert.Equal("yes", Assert.IsType<LuaString>(ctx.Globals.Get(new LuaString("done_t1"))).Value);
    }

    [Fact]
    public async Task Await_MultipleTasks_Return_FirstOfFirst_AllOfLast()
    {
        var state = new LuaState();
        state.Register("first", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(1), new LuaNumber(2), new LuaNumber(3))))));
        state.Register("second", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(4), new LuaNumber(5))))));

        // return await first(), await second() → returns first of first, all of second
        var result = await CompileAndExecuteAsync(@"
            return await first(), await second()
        ", state);

        // first() first result = 1, second() all results = 4, 5
        Assert.Equal(3, result.Count);
        Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
        Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[1]).Value);
        Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[2]).Value);
    }

    [Fact]
    public async Task Await_MultipleTasks_Assignment_EachGetsFirstResult()
    {
        var state = new LuaState();
        state.Register("getA", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(10), new LuaNumber(99))))));
        state.Register("getB", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(20), new LuaNumber(88))))));

        // Each await gives only its first value to the local.
        var result = await CompileAndExecuteAsync(@"
            local a = await getA()
            local b = await getB()
            return a, b
        ", state);

        Assert.Equal(2, result.Count);
        Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
        Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[1]).Value);
    }

    [Fact]
    public async Task Await_MultipleTasks_Concurrent_OverlapsInTime()
    {
        var barrier = new TaskCompletionSource<bool>();

        async Task<LuaTuple> DelayedTask(LuaCallingContext ctx, LuaValue[] args)
        {
            await barrier.Task;
            await Task.Delay(100);
            return new LuaTuple(new LuaNumber(((LuaNumber)args[0]).Value));
        }

        var state = new LuaState();
        state.Register("slow", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate(DelayedTask)));

        var ctx = state.CreateContext();
        var code = @"
            await slow(1), slow(2), slow(3)
            return 42
        ";

        var parser = new AsyncLuaParser();
        var block = parser.Parse(code);
        var proto = Compiler.Compile(block, "test");

        var executeTask = Interpreter.CallAsync(proto, ctx);
        await Task.Delay(50);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        barrier.SetResult(true);

        await executeTask;
        sw.Stop();

        // Three 100ms tasks run concurrently → total < 200ms
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Expected concurrent execution (< 200ms), took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Await_SingleTask_ThenMultiple_Works()
    {
        var state = new LuaState();
        state.Register("getX", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(10))))));
        state.Register("getY", new LuaCallbackFunction(
            new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
                Task.FromResult(new LuaTuple(new LuaNumber(20))))));

        // Single await first, then multi-await.
        var result = await CompileAndExecuteAsync(@"
            local x = await getX()
            await getX(), getY()
            return x
        ", state);

        Assert.Equal(10.0, Assert.IsType<LuaNumber>(result.First).Value);
    }
}
