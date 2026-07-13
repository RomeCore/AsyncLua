using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// Async/await integration tests: concurrent execution patterns, lock usage,
/// task composition, and mixed sync/async scenarios.
/// </summary>
public class AsyncIntegrationTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState();

		// sleep(ms) — returns a task that completes after the delay.
		state.SetGlobal("sleep", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
			{
				var ms = args.Length > 0 ? (int)((LuaNumber)args[0]).Value : 0;
				await Task.Delay(ms);
				return LuaTuple.Empty;
			}), "sleep"));

		// fetch(id, delay, result) — simulates an async HTTP call.
		state.SetGlobal("fetch", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
			{
				var delay = args.Length > 1 ? (int)((LuaNumber)args[1]).Value : 50;
				await Task.Delay(delay);
				var result = args.Length > 2 ? args[2] : new LuaString("data-" + args[0].ToString());
				return new LuaTuple(result);
			}), "fetch"));

		// counter — an async function that atomically increments a counter.
		state.SetGlobal("increment", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
			{
				await Task.Delay(5);
				var counter = (LuaTable)args[0];
				var current = ((LuaNumber)counter.Get(new LuaString("value"))).Value;
				counter.Set(new LuaString("value"), new LuaNumber(current + 1));
				return new LuaTuple(new LuaNumber(current + 1));
			}), "increment"));

		// assert
		state.SetGlobal("assert", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length > 0 && !args[0].ToBoolean())
				{
					var msg = args.Length > 1 ? args[1].ToString() : "assertion failed!";
					throw new LuaRuntimeException(msg);
				}
				return args.Length > 0 ? new LuaTuple(args) : LuaTuple.Empty;
			}, "assert"));

		// error(message)
		state.SetGlobal("error", new LuaCallbackFunction(new LuaCallbackFunction.CallbackDelegate(
			(ctx, args) =>
				throw new LuaRuntimeException(
					args.Length > 0 ? args[0].ToString() : "error")),
			"error"));

		// type / tostring / tonumber
		state.SetGlobal("type", new LuaCallbackFunction(
			(ctx, args) =>
				args.Length == 0
					? new LuaTuple(new LuaString("nil"))
					: new LuaTuple(new LuaString(args[0].TypeName)),
			"type"));

		state.SetGlobal("tostring", new LuaCallbackFunction(
			(ctx, args) =>
				args.Length == 0
					? new LuaTuple(new LuaString("nil"))
					: new LuaTuple(new LuaString(args[0].ToString())),
			"tostring"));

		state.SetGlobal("tonumber", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0 || !args[0].TryToNumber(out var n))
					return new LuaTuple(LuaNil.Instance);
				return new LuaTuple(new LuaNumber(n));
			}, "tonumber"));

		// string library
		var strLib = new LuaTable();
		state.Globals.Set(new LuaString("string"), strLib);
			strLib.Set(new LuaString("sub"), new LuaCallbackFunction(
			(ctx, args) =>
			{
				var s = args[0].ToString();
				var start = Math.Max(1, (int)((LuaNumber)args[1]).Value) - 1;
				var endIdx = args.Length > 2 ? (int)((LuaNumber)args[2]).Value : s.Length;
				var count = Math.Min(endIdx, s.Length) - start;
				return new LuaTuple(new LuaString(s.Substring(start, count)));
			}, "string.sub"));

		return state;
	}

	private static async Task<LuaTuple> ExecuteAsync(LuaState state, string code)
	{
		return await state.ExecuteAsync(code);
	}

	// ═══════════════════════════════════════════════════════════════
	// BASIC ASYNC/AWAIT PATTERNS
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Await_SingleTask_ReturnsResult()
	{
		var state = CreateState();

		var result = await ExecuteAsync(state, @"
			async function getData()
				local data = await fetch('item-42', 10)
				return 'got: ' .. data
			end

			return await getData()
		");

		Assert.Equal("got: data-item-42", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public async Task Await_MultipleSequential_Works()
	{
		var state = CreateState();

		var result = await ExecuteAsync(state, @"
			async function loadAll()
				local a = await fetch('a', 5, 'alpha')
				local b = await fetch('b', 5, 'beta')
				local c = await fetch('c', 5, 'gamma')
				return a, b, c
			end

			return await loadAll()
		");

		Assert.Equal(3, result.Count);
		Assert.Equal("alpha", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("beta", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("gamma", Assert.IsType<LuaString>(result[2]).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// CONCURRENT EXECUTION
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Await_ConcurrentExecution_TasksRunInParallel()
	{
		var state = CreateState();

		// Prove concurrency by measuring wall-clock time.
		// Three tasks each take 100ms. Sequential = 300ms. Concurrent ≈ 100ms.
		var sw = System.Diagnostics.Stopwatch.StartNew();

		var result = await ExecuteAsync(state, @"
			async function loadConcurrently()
				-- Launch all tasks first (non-blocking).
				local task1 = fetch('1', 100, 'one')
				local task2 = fetch('2', 100, 'two')
				local task3 = fetch('3', 100, 'three')

				-- Await all.
				local r1 = await task1
				local r2 = await task2
				local r3 = await task3
				return r1, r2, r3
			end

			return await loadConcurrently()
		");

		sw.Stop();

		Assert.Equal(3, result.Count);
		Assert.Equal("one", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("two", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("three", Assert.IsType<LuaString>(result[2]).Value);

		// Concurrency proven: total < 200ms (3 × 100ms sequential would be 300ms).
		Assert.True(sw.ElapsedMilliseconds < 250,
			$"Expected concurrent execution (< 250ms), took {sw.ElapsedMilliseconds}ms");
	}

	[Fact]
	public async Task Await_ConcurrentExecution_TasksRunInParallel_Alternative()
	{
		var state = CreateState();

		// Prove concurrency by measuring wall-clock time.
		// Three tasks each take 100ms. Sequential = 300ms. Concurrent ≈ 100ms.
		var sw = System.Diagnostics.Stopwatch.StartNew();

		var result = await ExecuteAsync(state, @"
			async function loadConcurrently()
				-- Launch all tasks first (non-blocking).
				local task1 = fetch('1', 100, 'one')
				local task2 = fetch('2', 100, 'two')
				local task3 = fetch('3', 100, 'three')

				-- Await all.
				return await task1, await task2, await task3
			end

			return await loadConcurrently()
		");

		sw.Stop();

		Assert.Equal(3, result.Count);
		Assert.Equal("one", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("two", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("three", Assert.IsType<LuaString>(result[2]).Value);

		// Concurrency proven: total < 200ms (3 × 100ms sequential would be 300ms).
		Assert.True(sw.ElapsedMilliseconds < 250,
			$"Expected concurrent execution (< 250ms), took {sw.ElapsedMilliseconds}ms");
	}

	[Fact]
	public async Task Await_FanOutPattern_ManyConcurrentTasks()
	{
		if (Utils.IsRunningCI())
			return;

		var state = CreateState();

		var sw = System.Diagnostics.Stopwatch.StartNew();

		var result = await ExecuteAsync(state, @"
			async function fanOut(count)
				local tasks = {}
				for i = 1, count do
					tasks[i] = fetch(i, 80, i * 10)
				end

				-- Await all tasks.
				local results = {}
				for i = 1, #tasks do
					results[i] = await tasks[i]
				end
				return results
			end

			local results = await fanOut(8)

			-- Sum all results: 10 + 20 + ... + 80 = 360
			local sum = 0
			for i = 1, #results do
				sum = sum + tonumber(results[i])
			end
			return #results, sum
		");

		sw.Stop();

		Assert.Equal(8.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(360.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.True(sw.ElapsedMilliseconds < 250,
			$"Fan-out concurrent execution expected < 250ms, took {sw.ElapsedMilliseconds}ms");
	}

	[Fact]
	public async Task Await_TaskComposition_WhenAllPattern()
	{
		var state = CreateState();

		// Simulate Task.WhenAll by launching all then awaiting all.
		var result = await ExecuteAsync(state, @"
			async function whenAll(tasks)
				local results = {}
				for i = 1, #tasks do
					results[i] = await tasks[i]
				end
				return results
			end

			async function fetchAll(ids)
				local tasks = {}
				for i = 1, #ids do
					tasks[i] = fetch(ids[i], 30, 'result-' .. ids[i])
				end
				return await whenAll(tasks)
			end

			local results = await fetchAll({'x', 'y', 'z', 'w'})
			-- Concatenate results.
			local combined = ''
			for i = 1, #results do
				combined = combined .. results[i] .. ';'
			end
			return combined, #results
		");

		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Contains("result-x", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Contains("result-z", Assert.IsType<LuaString>(result[0]).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// LOCK USAGE
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Lock_ProtectsSharedState_NoRaces()
	{
		var state = CreateState();

		// Multiple concurrent increments on a shared counter, protected by lock.
		var result = await ExecuteAsync(state, @"
			async function concurrentIncrement(counter, mutex, count)
				for i = 1, count do
					lock mutex do
						local val = counter.value
						await sleep(1)  -- simulate work while holding lock
						counter.value = val + 1
					end
				end
			end

			local counter = { value = 0 }
			local mutex = {}

			-- Launch multiple concurrent incrementors.
			local t1 = concurrentIncrement(counter, mutex, 20)
			local t2 = concurrentIncrement(counter, mutex, 20)
			local t3 = concurrentIncrement(counter, mutex, 20)

			-- Wait for all.
			await t1
			await t2
			await t3

			return counter.value
		");

		Assert.Equal(60.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public async Task Lock_NestedLocks_ReleasedCorrectly()
	{
		var state = CreateState();

		var result = await ExecuteAsync(state, @"
			async function transfer(from, to, amount, mtx)
				lock mtx do
					from.balance = from.balance - amount
					await sleep(5)
					to.balance = to.balance + amount
				end
			end

			local acc1 = { balance = 1000 }
			local acc2 = { balance = 500 }
			local m = {}

			local t1 = transfer(acc1, acc2, 300, m)
			local t2 = transfer(acc2, acc1, 100, m)
			await t1
			await t2

			-- acc1: 1000 - 300 + 100 = 800
			-- acc2: 500 + 300 - 100 = 700
			-- total conserved: 1500
			return acc1.balance, acc2.balance, acc1.balance + acc2.balance
		");

		Assert.Equal(800.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(700.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(1500.0, Assert.IsType<LuaNumber>(result[2]).Value);  // conservation
	}

	// ═══════════════════════════════════════════════════════════════
	// MIXED SYNC/ASYNC
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Mixed_SyncAndAsync_ComposeTogether()
	{
		var state = CreateState();

		state.SetGlobal("double", new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(((LuaNumber)args[0]).Value * 2)),
			"double"));

		var result = await ExecuteAsync(state, @"
			async function processValue(x)
				local syncResult = double(x)        -- sync C# call
				local asyncResult = await fetch('x', 10, syncResult)  -- async C# call
				return tonumber(asyncResult) + 1
			end

			return await processValue(21)
		");

		// double(21) = 42, fetch returns "42", tonumber("42") + 1 = 43.
		Assert.Equal(43.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public async Task Mixed_AsyncFunctionInsideSyncLoop_Works()
	{
		var state = CreateState();

		var result = await ExecuteAsync(state, @"
			async function sumAsyncRange(n)
				local total = 0
				for i = 1, n do
					local val = await fetch(i, 3, i * 2)
					total = total + tonumber(val)
				end
				return total
			end

			return await sumAsyncRange(10)
		");

		// Sum of 2*i for i=1..10 = 2 * 55 = 110.
		Assert.Equal(110.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// ERROR HANDLING IN ASYNC CONTEXT
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Async_TryCatchInsideAsyncFunction_Works()
	{
		var state = CreateState();

		state.SetGlobal("riskyFetch", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
			{
				await Task.Delay(10);
				var id = args[0].ToString();
				if (id == "fail")
					throw new InvalidOperationException("fetch failed for " + id);
				return new LuaTuple(new LuaString("ok-" + id));
			}), "riskyFetch"));

		var result = await ExecuteAsync(state, @"
			async function safeFetch(id)
				try
					local data = await riskyFetch(id)
					return 'success: ' .. data
				catch e do
					return 'caught: ' .. e
				end
			end

			local r1 = await safeFetch('good')
			local r2 = await safeFetch('fail')
			return r1, r2
		");

		Assert.Equal("success: ok-good", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Contains("caught:", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Contains("fail", Assert.IsType<LuaString>(result[1]).Value);
	}

	[Fact]
	public async Task Async_TryCatchWithConcurrentTasks_HandlesErrorsInParallel()
	{
		var state = CreateState();

		state.SetGlobal("maybeFail", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
			{
				await Task.Delay(20);
				var id = args[0].ToString();
				if (id == "2" || id == "4")
					throw new InvalidOperationException("fail-" + id);
				return new LuaTuple(new LuaString("ok-" + id));
			}), "maybeFail"));

		var result = await ExecuteAsync(state, @"
			async function fetchSafe(id)
				try
					return await maybeFail(id)
				catch e do
					return 'ERROR: ' .. e
				end
			end

			-- Launch all, then await all.
			async function collect(count)
				local tasks = {}
				for i = 1, count do
					tasks[i] = fetchSafe(tostring(i))
				end
				local results = {}
				for i = 1, #tasks do
					results[i] = await tasks[i]
				end
				return results
			end

			local results = await collect(5)
			local okCount, errCount = 0, 0
			for i = 1, #results do
				local r = results[i]
				if string.sub(r, 1, 2) == 'ok' then
					okCount = okCount + 1
				else
					errCount = errCount + 1
				end
			end
			return okCount, errCount
		");

		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[0]).Value);  // 3 succeeded
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);  // 2 failed
	}

	// ═══════════════════════════════════════════════════════════════
	// COMPLEX ASYNC PATTERNS
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Async_PipelinePattern_ChainedAsyncOperations()
	{
		var state = CreateState();

		// Simulate: fetch → transform → store pipeline.
		var results = new System.Collections.Concurrent.ConcurrentBag<string>();

		state.SetGlobal("store", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
			{
				await Task.Delay(5);
				var val = args[0].ToString();
				results.Add(val);
				return new LuaTuple(new LuaString("stored:" + val));
			}), "store"));

		var result = await ExecuteAsync(state, @"
			async function pipeline(id, multiplier)
				local raw = await fetch(id, 10, id * 10)
				local transformed = tonumber(raw) * multiplier
				local stored = await store(tostring(transformed))
				return stored
			end

			-- Run pipeline for 6 items concurrently.
			async function runPipeline(count)
				local tasks = {}
				for i = 1, count do
					tasks[i] = pipeline(i, i)
				end
				local results = {}
				for i = 1, #tasks do
					results[i] = await tasks[i]
				end
				return results
			end

			local outputs = await runPipeline(6)
			return #outputs
		");

		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result.First).Value);
		Assert.Equal(6, results.Count);
	}

	[Fact]
	public async Task Async_RecursiveAsyncFunction_Works()
	{
		var state = CreateState();

		var result = await ExecuteAsync(state, @"
			async function asyncFibonacci(n)
				if n <= 1 then return n end
				local a = await asyncFibonacci(n - 1)
				local b = await asyncFibonacci(n - 2)
				return a + b
			end

			-- Test small values (async overhead limits recursion depth).
			local r7 = await asyncFibonacci(7)
			local r10 = await asyncFibonacci(10)
			return r7, r10
		");

		Assert.Equal(13.0, Assert.IsType<LuaNumber>(result[0]).Value);   // F(7)
		Assert.Equal(55.0, Assert.IsType<LuaNumber>(result[1]).Value);   // F(10)
	}

	[Fact]
	public async Task Async_AwaitStatement_SideEffectOnly()
	{
		var state = CreateState();

		var sideEffects = new System.Collections.Concurrent.ConcurrentBag<string>();

		state.SetGlobal("notify", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
			{
				await Task.Delay(10);
				var msg = args[0].ToString();
				sideEffects.Add(msg);
				return LuaTuple.Empty;
			}), "notify"));

		var result = await ExecuteAsync(state, @"
			async function fireAndForget()
				await notify('step1')   -- await statement, discard result
				await notify('step2')
				await notify('step3')
				return 'done'
			end

			return await fireAndForget()
		");

		Assert.Equal("done", Assert.IsType<LuaString>(result.First).Value);
		Assert.Equal(3, sideEffects.Count);
		Assert.Contains("step1", sideEffects);
		Assert.Contains("step2", sideEffects);
		Assert.Contains("step3", sideEffects);
	}

	[Fact]
	public async Task Async_MultipleAwaitExpressions_SingleStatement()
	{
		var state = CreateState();

		var result = await ExecuteAsync(state, @"
			async function parallel()
				-- Launch multiple tasks, then await multiple in one expression.
				local a = fetch('a', 15, 10)
				local b = fetch('b', 15, 20)
				local c = fetch('c', 15, 30)
				return await a, await b, await c
			end

			return await parallel()
		");

		Assert.Equal(3, result.Count);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}
}
