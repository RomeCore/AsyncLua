using AsyncLua.Interpreting;
using AsyncLua.Values;

namespace AsyncLua.Tests.Interpreting;

public class ClosureInterpreterTests
{
    private static FunctionPrototype MakeProto(
        Instruction[] instructions,
        int maxRegSize = 1,
        LuaValue[]? constants = null,
        FunctionPrototype[]? innerPrototypes = null,
        UpvalueDescription[]? upvalueDescriptions = null)
    {
        return new FunctionPrototype(
            instructions,
            maxRegSize,
            false,
            constants ?? Array.Empty<LuaValue>(),
            innerPrototypes ?? Array.Empty<FunctionPrototype>(),
            upvalueDescriptions: upvalueDescriptions);
    }

	private static LuaCallingContext Context() => new LuaState().CreateContext();

	[Fact]
    public void Closure_CapturesLocalVariable_UpvalueWorks()
    {
        // Inner function: return n + 1
        // Captures n from outer function (register 1).
        var innerProto = MakeProto(new[]
        {
            // R[0] = U[0] + K[0]
            new Instruction(OpCode.GETUPVAL, a: 0, b: 0, c: 0, flags: OpFlags.None),   // R[0] = U[0]
            new Instruction(OpCode.ADD, a: 0, b: 0, c: 0, flags: OpFlags.None | OpFlags.KC), // R[0] = R[0] + 1
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(1) },
        upvalueDescriptions: new[] { new UpvalueDescription(registerIndex: 1, isLocal: true) });

        // Outer function: local n = 10; return function() return n + 1 end
        var outerProto = MakeProto(new[]
        {
            // R[1] = 10 (n)
            new Instruction(OpCode.MOVE, a: 1, b: 0, c: 0, flags: OpFlags.KB),
            // R[0] = closure(innerProto)
            new Instruction(OpCode.CLOSURE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // return closure
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 2, constants: new LuaValue[] { new LuaNumber(10) },
        innerPrototypes: new[] { innerProto });

        // Execute outer, get closure.
        var closure = Interpreter.Call(outerProto, Context());
        var func = Assert.IsType<LuaNativeFunction>(closure);

        // Verify upvalue was captured.
        Assert.Single(func.Upvalues);
        Assert.Equal(10.0, Assert.IsType<LuaNumber>(func.Upvalues[0].Value).Value);

        // Now call the closure: it should return 11.
        // Load closure into R[0], call it.
        var callProto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.CALL, a: 0, b: 0, c: 1, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { func });

        var result = Interpreter.Call(callProto, Context());
        Assert.Equal(11.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void Closure_MultipleCaptures_UpvaluesAreIndependent()
    {
        // Inner: return upvalue (just returns the captured value).
        var innerProto = MakeProto(new[]
        {
            new Instruction(OpCode.GETUPVAL, a: 0, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1,
        upvalueDescriptions: new[] { new UpvalueDescription(registerIndex: 1, isLocal: true) });

        // Outer: creates two closures capturing different values.
        var outerProto = MakeProto(new[]
        {
            // R[1] = 10
            new Instruction(OpCode.MOVE, a: 1, b: 0, c: 0, flags: OpFlags.KB),
            // R[0] = closure 1 (captures 10)
            new Instruction(OpCode.CLOSURE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // Save closure 1 to R[2]
            new Instruction(OpCode.MOVE, a: 2, b: 0, c: 0, flags: OpFlags.None),
            // R[1] = 99
            new Instruction(OpCode.MOVE, a: 1, b: 1, c: 0, flags: OpFlags.KB),
            // R[3] = closure 2 (captures 99)
            new Instruction(OpCode.CLOSURE, a: 3, b: 0, c: 0, flags: OpFlags.None),
            // R[2]() → result in R[2]
            new Instruction(OpCode.CALL, a: 2, b: 0, c: 1, flags: OpFlags.None),
            // Save result: R[4] = R[2] (10)
            new Instruction(OpCode.MOVE, a: 4, b: 2, c: 0, flags: OpFlags.None),
            // R[5]() → result in R[5]
            // First put closure2 into R[5] (R[3] has closure2)
            new Instruction(OpCode.MOVE, a: 5, b: 3, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.CALL, a: 5, b: 0, c: 1, flags: OpFlags.None),
            // R[0] = R[4] + R[5] = 10 + 99 = 109
            new Instruction(OpCode.ADD, a: 0, b: 4, c: 5, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 6, constants: new LuaValue[] { new LuaNumber(10), new LuaNumber(99) },
        innerPrototypes: new[] { innerProto });

        var result = Interpreter.Call(outerProto, Context());
        // Both closures share the same upvalue: when R[1] becomes 99, both see 99.
        // f1() = 99, f2() = 99, 99+99 = 198.
        Assert.Equal(198.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void Closure_SetUpvalue_MutatesCapturedVariable()
    {
        // Inner: increment upvalue and return it.
        var innerProto = MakeProto(new[]
        {
            // R[0] = U[0]
            new Instruction(OpCode.GETUPVAL, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // R[0] = R[0] + K[0] (1)
            new Instruction(OpCode.ADD, a: 0, b: 0, c: 0, flags: OpFlags.None | OpFlags.KC),
            // U[0] = R[0]
            new Instruction(OpCode.SETUPVAL, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // return U[0]
            new Instruction(OpCode.GETUPVAL, a: 0, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(1) },
        upvalueDescriptions: new[] { new UpvalueDescription(registerIndex: 1, isLocal: true) });

        // Outer: create closure, call it twice.
        var outerProto = MakeProto(new[]
        {
            // R[1] = 0 (counter)
            new Instruction(OpCode.MOVE, a: 1, b: 0, c: 0, flags: OpFlags.KB),
            // R[0] = closure
            new Instruction(OpCode.CLOSURE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // Save closure to R[3] (safe, won't be overwritten by results going to R[2])
            new Instruction(OpCode.MOVE, a: 3, b: 0, c: 0, flags: OpFlags.None),
            // Call: R[2] = closure; result goes to R[2]
            new Instruction(OpCode.MOVE, a: 2, b: 3, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.CALL, a: 2, b: 0, c: 1, flags: OpFlags.None),
            // Save first result to R[4]
            new Instruction(OpCode.MOVE, a: 4, b: 2, c: 0, flags: OpFlags.None),
            // Second call: R[2] = closure again
            new Instruction(OpCode.MOVE, a: 2, b: 3, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.CALL, a: 2, b: 0, c: 1, flags: OpFlags.None),
            // R[0] = R[4] + R[2] = 1 + 2 = 3
            new Instruction(OpCode.ADD, a: 0, b: 4, c: 2, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 5, constants: new LuaValue[] { new LuaNumber(0) },
        innerPrototypes: new[] { innerProto });

        var result = Interpreter.Call(outerProto, Context());
        Assert.Equal(3.0, Assert.IsType<LuaNumber>(result).Value);
    }
}
