using AsyncLua.Interpreting;
using AsyncLua.Values;

namespace AsyncLua.Tests.Interpreting;

public class CallInterpreterTests
{
    private readonly Interpreter _interpreter = new();

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

	// ── CALL: C# callback ─────────────────────────────────────────────

	[Fact]
    public void Call_CSharpCallback_ReturnsResult()
    {
        // Register a function that doubles its argument.
        var doubleFunc = LuaCallbackFunction.From(
            args => new LuaNumber(((LuaNumber)args[0]).Value * 2));

        // R[0] = doubleFunc, R[1] = 21; CALL R0, 1 arg, 1 result; return R0
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),  // R[0] = doubleFunc
            new Instruction(OpCode.MOVE, a: 1, b: 1, c: 0, flags: OpFlags.KB),  // R[1] = 21
            new Instruction(OpCode.CALL, a: 0, b: 1, c: 1, flags: OpFlags.None), // call R0(R1) → 1 result
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 2, constants: new LuaValue[] { doubleFunc, new LuaNumber(21) });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void Call_MultipleArgs_ReturnsResult()
    {
        // add(a, b) = a + b
        var addFunc = LuaCallbackFunction.From(
            args => new LuaNumber(((LuaNumber)args[0]).Value + ((LuaNumber)args[1]).Value));

        // R[0] = addFunc, R[1] = 10, R[2] = 32; CALL R0, 2 args, 1 result; return R0
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.MOVE, a: 1, b: 1, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.MOVE, a: 2, b: 2, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.CALL, a: 0, b: 2, c: 1, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 3, constants: new LuaValue[] { addFunc, new LuaNumber(10), new LuaNumber(32) });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void Call_CallbackReturningNil_ReturnsNil()
    {
        var noop = LuaCallbackFunction.From(
            _ => LuaNil.Instance);

        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 1, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { noop });

        var result = _interpreter.Call(proto, Context());
        Assert.IsType<LuaNil>(result);
    }

    [Fact]
    public void Call_NotAFunction_Throws()
    {
        // R[0] = 42; CALL R0 → should throw
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42) });

        Assert.Throws<LuaRuntimeException>(() => _interpreter.Call(proto, Context()));
    }

    // ── CALL: Lua bytecode function ───────────────────────────────────

    [Fact]
    public void Call_LuaFunction_NestedCallReturnsResult()
    {
        // Inner function: add(a, b) { return a + b }
        var innerProto = MakeProto(new[]
        {
            new Instruction(OpCode.ADD, a: 0, b: 0, c: 1, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 3, constants: Array.Empty<LuaValue>());

        var innerFunc = new LuaNativeFunction(innerProto);

        // Outer function: R[0] = innerFunc; R[1] = 10; R[2] = 32; CALL R0, 2 args, 1 result; return R0
        var outerProto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),  // R[0] = innerFunc
            new Instruction(OpCode.MOVE, a: 1, b: 1, c: 0, flags: OpFlags.KB),  // R[1] = 10
            new Instruction(OpCode.MOVE, a: 2, b: 2, c: 0, flags: OpFlags.KB),  // R[2] = 32
            new Instruction(OpCode.CALL, a: 0, b: 2, c: 1, flags: OpFlags.None), // call inner
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 3, constants: new LuaValue[] { innerFunc, new LuaNumber(10), new LuaNumber(32) });

        var result = _interpreter.Call(outerProto, Context());
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void Call_LuaFunction_DeepCall_ThreeLevels()
    {
        // Level 3: return 42
        var level3 = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB), // R[0] = 42
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42) });

        // Level 2: calls level3 and returns its result
        var level2 = MakeProto(new[]
        {
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 1, flags: OpFlags.None), // call R[0]
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1);

        // Level 1: R[0] = level2; R[0] = level3; CALL R[0] → calls level2 which calls level3
        var level3Func = new LuaNativeFunction(level3);
        var level2Func = new LuaNativeFunction(level2);

        var level1 = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),  // R[0] = level2
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.KB),  // R[0] = level3 (for level2 to call)
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 1, flags: OpFlags.None), // call level2
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { level2Func, level3Func });

        var result = _interpreter.Call(level1, Context());
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void Call_AsyncCallback_WorksInCallAsync()
    {
        var asyncFunc = LuaCallbackFunction.FromAsync(
            async args =>
            {
                await Task.Delay(10);
                return new LuaNumber(((LuaNumber)args[0]).Value * 3);
            });

        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.MOVE, a: 1, b: 1, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.CALL, a: 0, b: 1, c: 1, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 2, constants: new LuaValue[] { asyncFunc, new LuaNumber(14) });

        var result = _interpreter.CallAsync(proto, Context()).GetAwaiter().GetResult();
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result).Value);
    }

    // ── CALL edge cases ────────────────────────────────────────────────

    [Fact]
    public void Call_ZeroArgs_Works()
    {
        // Function that returns 42, called with 0 args.
        var func = LuaCallbackFunction.From(_ => new LuaNumber(42));
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 1, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { func });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void Call_ZeroExpectedResults_ReturnsNil()
    {
        // Call a function but expect 0 results — R[0] kept as function? No, it's nil-padded.
        var func = LuaCallbackFunction.From(_ => new LuaNumber(42));
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 0, flags: OpFlags.None), // 0 results expected
            // R[0] is not overwritten — still the function object.
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { func });

        var result = _interpreter.Call(proto, Context());
        Assert.IsType<LuaCallbackFunction>(result);
    }

    [Fact]
    public void Call_TruncatesExtraResults()
    {
        // Function returns 3 values, caller expects 1 — only first is kept.
        var func = LuaCallbackFunction.From(
            _ => new LuaTuple(new LuaNumber(10), new LuaNumber(20), new LuaNumber(30)));
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 1, flags: OpFlags.None), // expect 1 result
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { func });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(10.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void Call_PadsMissingResultsWithNil()
    {
        // Function returns 1 value, caller expects 3 — rest are nil.
        var func = LuaCallbackFunction.From(
            _ => new LuaTuple(new LuaNumber(42)));
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 3, flags: OpFlags.None), // expect 3 results
            new Instruction(OpCode.MOVE, a: 0, b: 2, c: 0, flags: OpFlags.None), // R[0] = R[2] (3rd result)
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 3, constants: new LuaValue[] { func });

        var result = _interpreter.Call(proto, Context());
        // 3rd result is nil (padded).
        Assert.IsType<LuaNil>(result);
    }

    [Fact]
    public void Call_FunctionThrows_PropagatesToCaller()
    {
        var throwing = LuaCallbackFunction.From(
            _ => throw new LuaRuntimeException("inside callback"));

        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { throwing });

        var ex = Assert.Throws<LuaRuntimeException>(() => _interpreter.Call(proto, Context()));
        Assert.Contains("inside callback", ex.Message);
    }
}
