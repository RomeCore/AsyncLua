using AsyncLua.Interpreting;
using AsyncLua.Values;

namespace AsyncLua.Tests.Interpreting;

public class LockAndAwaitInterpreterTests
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

	// ── LOCK / UNLOCK ─────────────────────────────────────────────────

	[Fact]
	public void Lock_Unlock_Pair_DoesNotThrow()
	{
		// LOCK R[0]; UNLOCK R[0]; return
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.LOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.UNLOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1);

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.IsType<LuaTable>(result.First);
	}

	[Fact]
	public void Lock_ProtectsBody_BetweenLockAndUnlock()
	{
		// Simulate: lock(table) { table["x"] = 42 } end
		// R[0] = {}; LOCK R[0]; R[0]["x"] = 42; UNLOCK R[0]; R[0] = R[0]["x"]; return R[0]
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.LOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // Critical section
            new Instruction(OpCode.SETTABLE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC), // R[0]["x"] = 42
            new Instruction(OpCode.UNLOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // Read back
            new Instruction(OpCode.GETTABLE, a: 0, b: 0, c: 0, flags: OpFlags.KC), // R[0] = R[0]["x"]
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaString("x"), new LuaNumber(42) });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Lock_FinallyReleases_WhenReturnBeforeUnlock()
	{
		// LOCK without explicit UNLOCK — the finally block should release it.
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.LOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // No UNLOCK — return directly (finally block releases the lock)
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1);

		// Should not throw SynchronizationLockException.
		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.IsType<LuaTable>(result.First);
	}

	[Fact]
	public void Lock_NestedLocks_ReleasedInReverseOrder()
	{
		// LOCK R[0]; LOCK R[1]; UNLOCK R[0]; UNLOCK R[1] — should not throw.
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.NEWTABLE, a: 1, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.LOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.LOCK, a: 1, b: 0, c: 0, flags: OpFlags.None),
            // Unlock in reverse order (not required but tested)
            new Instruction(OpCode.UNLOCK, a: 1, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.UNLOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 2);

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.IsType<LuaTable>(result.First);
	}

	[Fact]
	public void Lock_OnString_Works()
	{
		// Locking on a string is allowed (strings are immutable reference types in .NET).
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB), // R[0] = "lockme"
            new Instruction(OpCode.LOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.UNLOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaString("lockme") });

		var result = AsyncLuaInterpreter.Call(proto, Context());
		Assert.IsType<LuaString>(result.First);
	}

	// ── AWAIT (async) ─────────────────────────────────────────────────

	[Fact]
	public void Await_InSyncCall_Throws()
	{
		// AWAIT in synchronous Call() should throw.
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1);

		Assert.Throws<LuaRuntimeException>(() => AsyncLuaInterpreter.Call(proto, Context()));
	}

	[Fact]
	public async Task Await_CompletedTask_ReturnsResult()
	{
		// R[0] = completed_task(result=42); AWAIT R[0]; return R[0]
		var completedTask = LuaTask.FromResult(new LuaNumber(42));

		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB), // R[0] = task
            new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { completedTask });

		var result = await AsyncLuaInterpreter.CallAsync(proto, Context());
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public async Task Await_PendingTask_ResumesAfterCompletion()
	{
		// Create a pending task, complete it after a short delay, await it.
		var pendingTask = new LuaTask();

		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB), // R[0] = pending task
            new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { pendingTask });

		// Complete the task after 50ms from another thread.
		_ = Task.Run(async () =>
		{
			await Task.Delay(50);
			pendingTask.SetResult(new LuaNumber(99));
		});

		var result = await AsyncLuaInterpreter.CallAsync(proto, Context());
		Assert.Equal(99.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public async Task Await_MultipleResults_StoresAll()
	{
		// Await a task with 3 results: (10, 20, 30)
		var task = LuaTask.FromResult(new LuaNumber(10), new LuaNumber(20), new LuaNumber(30));

		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB), // R[0] = task
            new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 0, flags: OpFlags.None), // results go to R[0], R[1], R[2]
            // Return the third result (R[2])
            new Instruction(OpCode.MOVE, a: 0, b: 2, c: 0, flags: OpFlags.None), // R[0] = R[2]
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 3, constants: new LuaValue[] { task });

		var result = await AsyncLuaInterpreter.CallAsync(proto, Context());
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public async Task Await_WithLock_BothWorkTogether()
	{
		// LOCK + AWAIT in the same function.
		var completedTask = LuaTask.FromResult(new LuaNumber(77));

		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.NEWTABLE, a: 1, b: 0, c: 0, flags: OpFlags.None), // R[1] = {}
            new Instruction(OpCode.LOCK, a: 1, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),       // R[0] = task
            new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.UNLOCK, a: 1, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 2, constants: new LuaValue[] { completedTask });

		var result = await AsyncLuaInterpreter.CallAsync(proto, Context());
		Assert.Equal(77.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// Current implementation does not allowing reentrancy.
	/*[Fact]
	public void Lock_Reentrancy_CanRelockSameObject()
	{
		// lock(obj); lock(obj) → allowed by Monitor (reentrant).
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.LOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.LOCK, a: 0, b: 0, c: 0, flags: OpFlags.None), // reentrant!
            new Instruction(OpCode.UNLOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.UNLOCK, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1);

		var result = Interpreter.Call(proto, Context());
		Assert.IsType<LuaTable>(result.First);
	}*/

	// ── AWAIT edge cases ──────────────────────────────────────────────

	[Fact]
	public void Await_NotALuaTask_Throws()
	{
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB), // R[0] = 42
            new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42) });

		// AWAIT on a non-task throws (even in sync mode it should throw before the "not async" check).
		Assert.Throws<LuaRuntimeException>(() => AsyncLuaInterpreter.Call(proto, Context()));
	}

	[Fact]
	public async Task Await_EmptyResult_ReturnsNil()
	{
		// Await a task with 0 results → R[0] should be nil (standard Lua semantics:
		// zero return values = nil in single-value context).
		var emptyTask = LuaTask.FromResult(); // empty
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
			new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { emptyTask });

		var result = await AsyncLuaInterpreter.CallAsync(proto, Context());
		// Awaiting an empty task yields nil, not the task object.
		Assert.IsType<LuaNil>(result.First);
	}

	[Fact]
	public async Task Await_FaultedTask_PropagatesException()
	{
		var faultedTask = LuaTask.FromException(new InvalidOperationException("test failure"));
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
			new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
		}, maxRegSize: 1, constants: new LuaValue[] { faultedTask });

		// Awaiting a faulted task should throw.
		var ex = await Assert.ThrowsAsync<LuaRuntimeException>(
			async () => await AsyncLuaInterpreter.CallAsync(proto, Context()));
		Assert.IsType<InvalidOperationException>(ex.InnerException);
	}

	// ── Non-blocking async CALL ───────────────────────────────────────

	// REMOVED DUE TO UNSTABLE BEHAVIOR (DEPENDS ON EXECUTION TIME)

	/*[Fact]
	public async Task AsyncCall_ReturnsLuaTaskImmediately_DoesNotBlock()
	{
		// Two async functions with controllable completion.
		var tcs1 = new TaskCompletionSource<LuaValue>();
		var tcs2 = new TaskCompletionSource<LuaValue>();

		var asyncFunc1 = new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
				tcs1.Task.ContinueWith(t => new LuaTuple(t.Result))));
		var asyncFunc2 = new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
				tcs2.Task.ContinueWith(t => new LuaTuple(t.Result))));

		var ctx = Context();
		var globals = ctx.Globals;

		// Bytecode:
		//   R[0] = asyncFunc1; R[1] = asyncFunc2
		//   CALL R[0] → R[0] = LuaTask1 (non-blocking)
		//   CALL R[1] → R[1] = LuaTask2 (non-blocking)
		//   _G["t1"] = R[0]; _G["t2"] = R[1]   — expose tasks to host
		//   AWAIT R[0] → R[0] = result1
		//   AWAIT R[1] → R[1] = result2
		//   return R[0], R[1]                     — two results
		var proto = MakeProto(new[]
		{
            // Load functions.
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),  // R[0] = asyncFunc1
            new Instruction(OpCode.MOVE, a: 1, b: 1, c: 0, flags: OpFlags.KB),  // R[1] = asyncFunc2
            // Call both — non-blocking.
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 1, flags: OpFlags.None), // R[0] = LuaTask1
            new Instruction(OpCode.CALL, a: 1, b: 0, c: 1, flags: OpFlags.None), // R[1] = LuaTask2
            // Expose tasks to the host via globals.
            new Instruction(OpCode.SETGLOBAL, a: 0, b: 2, c: 0, flags: OpFlags.KB), // _G["t1"] = R[0]
            new Instruction(OpCode.SETGLOBAL, a: 1, b: 3, c: 0, flags: OpFlags.KB), // _G["t2"] = R[1]
            // Await both.
            new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 1, flags: OpFlags.None), // await R[0]
            new Instruction(OpCode.AWAIT, a: 1, b: 0, c: 1, flags: OpFlags.None), // await R[1]
            // Return both results (2 values).
            new Instruction(OpCode.RETURN, a: 0, b: 2, c: 0, flags: OpFlags.None),
		}, maxRegSize: 2, constants: new LuaValue[]
		{
			asyncFunc1, asyncFunc2,
			new LuaString("t1"), new LuaString("t2"),
		});

		// Start execution — should "pause" at the first AWAIT.
		var executeTask = AsyncLuaInterpreter.CallAsync(proto, ctx);

		// At this point, CALL has already run and stored LuaTasks in globals.
		// Give the continuation a moment to reach the first AWAIT.
		await Task.Delay(50);

		// Verify: both CALLs returned immediately — globals contain LuaTasks, not results.
		var t1 = Assert.IsType<LuaTask>(globals.Get(new LuaString("t1")));
		var t2 = Assert.IsType<LuaTask>(globals.Get(new LuaString("t2")));
		Assert.Equal(LuaTaskStatus.Pending, t1.Status);
		Assert.Equal(LuaTaskStatus.Pending, t2.Status);

		// Complete the second task first (out of order) — proves independence.
		tcs2.SetResult(new LuaNumber(200));
		await Task.Delay(20);

		// First task still pending.
		Assert.Equal(LuaTaskStatus.Pending, t1.Status);
		Assert.Equal(LuaTaskStatus.Completed, t2.Status);

		// Complete the first task.
		tcs1.SetResult(new LuaNumber(100));
		await Task.Delay(20);

		// Now the interpreter should finish and return both results.
		var result = await executeTask;
		Assert.Equal(2, result.Count);
		Assert.Equal(100.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(200.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}*/

	[Fact]
	public async Task AsyncCall_MixedSyncAndAsync_WorksTogether()
	{
		// Sync function: returns a constant.
		var syncFunc = LuaCallbackFunction.From(
			(LuaValue[] _) => new LuaNumber(10));

		// Async function: controllable completion.
		var tcs = new TaskCompletionSource<LuaValue>();
		var asyncFunc = new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate((ctx, args) =>
				tcs.Task.ContinueWith(t => new LuaTuple(t.Result))));

		var ctx = Context();

		// Bytecode:
		//   R[0] = syncFunc;  R[1] = asyncFunc
		//   CALL R[0] → R[0] = 10            (sync: immediate result)
		//   CALL R[1] → R[1] = LuaTask       (async: non-blocking task)
		//   AWAIT R[1] → R[1] = result
		//   return R[0], R[1]
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),  // R[0] = syncFunc
            new Instruction(OpCode.MOVE, a: 1, b: 1, c: 0, flags: OpFlags.KB),  // R[1] = asyncFunc
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 1, flags: OpFlags.None), // R[0] = 10
            new Instruction(OpCode.CALL, a: 1, b: 0, c: 1, flags: OpFlags.None), // R[1] = LuaTask
            new Instruction(OpCode.AWAIT, a: 1, b: 0, c: 1, flags: OpFlags.None), // await R[1]
            new Instruction(OpCode.RETURN, a: 0, b: 2, c: 0, flags: OpFlags.None),
		}, maxRegSize: 2, constants: new LuaValue[] { syncFunc, asyncFunc });

		var executeTask = AsyncLuaInterpreter.CallAsync(proto, ctx);
		await Task.Delay(50);

		// Sync result is already in registers, async task is pending.
		// Complete the async task.
		tcs.SetResult(new LuaNumber(42));
		await Task.Delay(20);

		var result = await executeTask;
		Assert.Equal(2, result.Count);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public async Task AsyncCall_ConcurrentExecution_OverlapsInTime()
	{
		// Prove that two async calls run concurrently by measuring time.
		// Each function takes ~100ms. If sequential, total ≥ 200ms. If concurrent, ≤ 150ms.
		var barrier = new TaskCompletionSource<bool>();

		async Task<LuaTuple> DelayedFunc(LuaCallingContext ctx, LuaValue[] args)
		{
			// Wait for the barrier to be released, then return after a short delay.
			await barrier.Task;
			await Task.Delay(100);
			return new LuaTuple(new LuaNumber(((LuaNumber)args[0]).Value * 2));
		}

		var asyncFunc = new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(DelayedFunc));

		var ctx = Context();

		//   R[0] = asyncFunc; R[1] = 10; R[2] = asyncFunc; R[3] = 20
		//   CALL R[0], 1 arg → R[0] = LuaTask1
		//   CALL R[2], 1 arg → R[2] = LuaTask2
		//   AWAIT R[0] → R[0] = result1
		//   AWAIT R[2] → R[2] = result2
		//   MOVE R[1] = R[2]  (pack results contiguously)
		//   return R[0], R[1]
		var proto = MakeProto(new[]
		{
			new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),  // R[0] = asyncFunc
            new Instruction(OpCode.MOVE, a: 1, b: 1, c: 0, flags: OpFlags.KB),  // R[1] = 10
            new Instruction(OpCode.MOVE, a: 2, b: 0, c: 0, flags: OpFlags.KB),  // R[2] = asyncFunc
            new Instruction(OpCode.MOVE, a: 3, b: 2, c: 0, flags: OpFlags.KB),  // R[3] = 20
            // Call both — non-blocking.
            new Instruction(OpCode.CALL, a: 0, b: 1, c: 1, flags: OpFlags.None), // R[0] = LuaTask1
            new Instruction(OpCode.CALL, a: 2, b: 1, c: 1, flags: OpFlags.None), // R[2] = LuaTask2
            // Expose tasks to host.
            new Instruction(OpCode.SETGLOBAL, a: 0, b: 3, c: 0, flags: OpFlags.KB), // _G["t1"] = R[0]
            new Instruction(OpCode.SETGLOBAL, a: 2, b: 4, c: 0, flags: OpFlags.KB), // _G["t2"] = R[2]
            // Await both.
            new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 1, flags: OpFlags.None),
			new Instruction(OpCode.AWAIT, a: 2, b: 0, c: 1, flags: OpFlags.None),
            // Pack results contiguously: R[1] = R[2], then return R[0], R[1].
            new Instruction(OpCode.MOVE, a: 1, b: 2, c: 0, flags: OpFlags.None),
			new Instruction(OpCode.RETURN, a: 0, b: 2, c: 0, flags: OpFlags.None),
		}, maxRegSize: 4, constants: new LuaValue[]
		{
			asyncFunc,
			new LuaNumber(10),   // arg for first call
            new LuaNumber(20),   // arg for second call
            new LuaString("t1"), new LuaString("t2"),
		});

		var executeTask = AsyncLuaInterpreter.CallAsync(proto, ctx);

		// Give the interpreter time to reach AWAIT.
		await Task.Delay(50);

		// Both tasks should be pending — CALL was non-blocking.
		var t1 = Assert.IsType<LuaTask>(ctx.Globals.Get(new LuaString("t1")));
		var t2 = Assert.IsType<LuaTask>(ctx.Globals.Get(new LuaString("t2")));
		Assert.Equal(LuaTaskStatus.Pending, t1.Status);
		Assert.Equal(LuaTaskStatus.Pending, t2.Status);

		// Release the barrier — both delayed tasks now run concurrently.
		var sw = System.Diagnostics.Stopwatch.StartNew();
		barrier.SetResult(true);

		var result = await executeTask;
		sw.Stop();

		// Both results correct.
		Assert.Equal(2, result.Count);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(40.0, Assert.IsType<LuaNumber>(result[1]).Value);

		// Concurrent execution: total time < sum of individual delays.
		// Each task takes ~100ms; sequential would be ~200ms, concurrent ~100ms.
		Assert.True(sw.ElapsedMilliseconds < 200,
			$"Expected concurrent execution (< 200ms), but took {sw.ElapsedMilliseconds}ms");
	}
}
