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

        var result = Interpreter.Call(proto, Context());
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

        var result = Interpreter.Call(proto, Context());
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
        var result = Interpreter.Call(proto, Context());
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

        var result = Interpreter.Call(proto, Context());
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

        var result = Interpreter.Call(proto, Context());
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

        Assert.Throws<LuaRuntimeException>(() => Interpreter.Call(proto, Context()));
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

        var result = await Interpreter.CallAsync(proto, Context());
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

        var result = await Interpreter.CallAsync(proto, Context());
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

        var result = await Interpreter.CallAsync(proto, Context());
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

        var result = await Interpreter.CallAsync(proto, Context());
        Assert.Equal(77.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
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
    }

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
        Assert.Throws<LuaRuntimeException>(() => Interpreter.Call(proto, Context()));
    }

    [Fact]
    public async Task Await_EmptyResult_StoresNothing()
    {
        // Await a task with 0 results.
        var emptyTask = LuaTask.FromResult(); // empty
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.AWAIT, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // R[0] is unchanged (still the task object itself, since no results were stored).
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { emptyTask });

        var result = await Interpreter.CallAsync(proto, Context());
        // R[0] is still the task because no results were copied.
        _ = Assert.IsType<LuaTask>(result.First);
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
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Interpreter.CallAsync(proto, Context()));
    }
}
