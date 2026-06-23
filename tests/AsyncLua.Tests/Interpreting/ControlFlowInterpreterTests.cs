using AsyncLua.Interpreting;
using AsyncLua.Values;

namespace AsyncLua.Tests.Interpreting;

public class ControlFlowInterpreterTests
{
    private readonly Interpreter _interpreter = new();

    private static FunctionPrototype MakeProto(
        Instruction[] instructions,
        int maxRegSize = 1,
        LuaValue[]? constants = null,
        byte parameterCount = 0,
        bool isVararg = false,
        FunctionPrototype[]? innerPrototypes = null)
    {
        return new FunctionPrototype(
            instructions,
            maxRegSize,
			isAsync: false,
			constants ?? Array.Empty<LuaValue>(),
            innerPrototypes ?? Array.Empty<FunctionPrototype>(),
            parameterCount: parameterCount,
            isVararg: isVararg);
    }

    private static LuaCallingContext Context() => new LuaState().CreateContext();

    // ── FORPREP + FORLOOP (numeric for) ───────────────────────────────

    [Fact]
    public void NumericFor_PositiveStep_SumsValues()
    {
        // for i = 1, 5, 1 do sum = sum + i end  →  1+2+3+4+5 = 15
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE,    a: 0, b: 0, c: 0, flags: OpFlags.KB),                     // R0 = 1
            new Instruction(OpCode.MOVE,    a: 1, b: 1, c: 0, flags: OpFlags.KB),                     // R1 = 5
            new Instruction(OpCode.MOVE,    a: 2, b: 2, c: 0, flags: OpFlags.KB),                     // R2 = 1
            new Instruction(OpCode.MOVE,    a: 3, b: 3, c: 0, flags: OpFlags.KB),                     // R3 = 0
            new Instruction(OpCode.FORPREP, a: 0, b: 2, c: 0, flags: OpFlags.SignedBX),              // R0 -= R2; jump to FORLOOP
            new Instruction(OpCode.ADD,     a: 3, b: 3, c: 0, flags: OpFlags.None),                   // body: sum += i
            new Instruction(OpCode.FORLOOP, a: 0, b: unchecked((ushort)-1), c: 0, flags: OpFlags.SignedBX),  // R0+=R2; check
            new Instruction(OpCode.RETURN,  a: 3, b: 1, c: 0, flags: OpFlags.None),                   // return sum
        }, maxRegSize: 4, constants: new LuaValue[]
        {
            new LuaNumber(1),  // K[0] = start
            new LuaNumber(5),  // K[1] = limit
            new LuaNumber(1),  // K[2] = step
            new LuaNumber(0),  // K[3] = sum init
        });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(15.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void NumericFor_NegativeStep_SumsValues()
    {
        // for i = 5, 1, -1 do sum = sum + i end  →  5+4+3+2+1 = 15
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE,    a: 0, b: 0, c: 0, flags: OpFlags.KB),                     // R0 = 5
            new Instruction(OpCode.MOVE,    a: 1, b: 1, c: 0, flags: OpFlags.KB),                     // R1 = 1
            new Instruction(OpCode.MOVE,    a: 2, b: 2, c: 0, flags: OpFlags.KB),                     // R2 = -1
            new Instruction(OpCode.MOVE,    a: 3, b: 3, c: 0, flags: OpFlags.KB),                     // R3 = 0
            new Instruction(OpCode.FORPREP, a: 0, b: 2, c: 0, flags: OpFlags.SignedBX),
            new Instruction(OpCode.ADD,     a: 3, b: 3, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.FORLOOP, a: 0, b: unchecked((ushort)-1), c: 0, flags: OpFlags.SignedBX),
            new Instruction(OpCode.RETURN,  a: 3, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 4, constants: new LuaValue[]
        {
            new LuaNumber(5),   // K[0]
            new LuaNumber(1),   // K[1]
            new LuaNumber(-1),  // K[2]
            new LuaNumber(0),   // K[3]
        });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(15.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void NumericFor_ZeroIterations_DoesNotExecuteBody()
    {
        // for i = 5, 1, 1 do ... — step away from limit, body skipped
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE,    a: 0, b: 0, c: 0, flags: OpFlags.KB),                     // R0 = 5
            new Instruction(OpCode.MOVE,    a: 1, b: 1, c: 0, flags: OpFlags.KB),                     // R1 = 1
            new Instruction(OpCode.MOVE,    a: 2, b: 2, c: 0, flags: OpFlags.KB),                     // R2 = 1
            new Instruction(OpCode.MOVE,    a: 3, b: 3, c: 0, flags: OpFlags.KB),                     // R3 = 0
            new Instruction(OpCode.FORPREP, a: 0, b: 2, c: 0, flags: OpFlags.SignedBX),
            new Instruction(OpCode.ADD,     a: 3, b: 3, c: 0, flags: OpFlags.None),                   // skipped
            new Instruction(OpCode.FORLOOP, a: 0, b: unchecked((ushort)-1), c: 0, flags: OpFlags.SignedBX),
            new Instruction(OpCode.RETURN,  a: 3, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 4, constants: new LuaValue[]
        {
            new LuaNumber(5),   // K[0]
            new LuaNumber(1),   // K[1]
            new LuaNumber(1),   // K[2]
            new LuaNumber(0),   // K[3]
        });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(0.0, Assert.IsType<LuaNumber>(result).Value);
    }

    // ── TFORCALL + TFORLOOP (generic for-in) ──────────────────────────

    [Fact]
    public void GenericFor_SimpleIterator_SumsValues()
    {
        // Simulates: for v in iterator(limit) do sum = sum + v end
        // Iterator returns 1, 2, ..., limit.
        //
        // Register layout:
        //   R0 = iterator f,  R1 = state (limit),  R2 = initial var (0)
        //   R6 = sum accumulator (safe from TFORCALL backup at R3..R5)
        //
        // TFORCALL 0, 1: backup R0..R2 → R3..R5, call R3(R4,R5), result at R3
        // TFORLOOP 2, sBx: if R3 != nil then R2 = R3, jump to body

        var iteratorFunc = LuaCallbackFunction.From(args =>
        {
            double limit = ((LuaNumber)args[0]).Value;
            double prev = ((LuaNumber)args[1]).Value;
            double next = prev + 1;
            if (next <= limit)
                return new LuaTuple(new LuaValue[] { new LuaNumber(next) });
            else
                return new LuaTuple(LuaNil.Instance);
        });

        var proto = MakeProto(new[]
        {
            // Setup
            new Instruction(OpCode.MOVE,    a: 0, b: 0, c: 0, flags: OpFlags.KB),   // R0 = iterator
            new Instruction(OpCode.MOVE,    a: 1, b: 1, c: 0, flags: OpFlags.KB),   // R1 = 5
            new Instruction(OpCode.MOVE,    a: 2, b: 2, c: 0, flags: OpFlags.KB),   // R2 = 0
            new Instruction(OpCode.MOVE,    a: 6, b: 2, c: 0, flags: OpFlags.KB),   // R6 = 0 (sum)

            // Prime: first TFORCALL, then jump to check
            new Instruction(OpCode.TFORCALL, a: 0, b: 0, c: 1, flags: OpFlags.None),  // R3 = f(R1,R2)
            new Instruction(OpCode.JMP,      a: 0, b: 3, c: 0, flags: OpFlags.SignedBX), // goto TFORLOOP

            // Body (pc=6)
            new Instruction(OpCode.ADD,      a: 6, b: 6, c: 3, flags: OpFlags.None),   // R6 += R3

            // Loop: call iterator again
            new Instruction(OpCode.TFORCALL, a: 0, b: 0, c: 1, flags: OpFlags.None),   // R3 = f(R1,R2)

            // Check (pc=8)
            new Instruction(OpCode.TFORLOOP, a: 2, b: unchecked((ushort)-2), c: 0, flags: OpFlags.SignedBX), // if R3!=nil: goto body

            // Exit
            new Instruction(OpCode.RETURN,   a: 6, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 7, constants: new LuaValue[]
        {
            iteratorFunc,       // K[0]
            new LuaNumber(5),   // K[1]
            new LuaNumber(0),   // K[2]
        });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(15.0, Assert.IsType<LuaNumber>(result).Value);
    }

    // ── VARARG ────────────────────────────────────────────────────────

    [Fact]
    public void Vararg_CopiesAllExtraArgs()
    {
        // Function: function(a, ...) return ... end
        // Called as: f(10, 20, 30, 40) → returns 20 (first vararg)
        // Fixed param a=10, varargs = [20, 30, 40]

        var innerProto = new FunctionPrototype(
            instructions: new[]
            {
                new Instruction(OpCode.VARARG, a: 1, b: 3, c: 0, flags: OpFlags.None),
                new Instruction(OpCode.RETURN, a: 1, b: 1, c: 0, flags: OpFlags.None),
            },
            maxRegSize: 4,
			isAsync: false,
			constants: Array.Empty<LuaValue>(),
            innerPrototypes: Array.Empty<FunctionPrototype>(),
            parameterCount: 1,
            isVararg: true);

        var outerProto = MakeProto(new[]
        {
            new Instruction(OpCode.CLOSURE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.MOVE,    a: 1, b: 0, c: 0, flags: OpFlags.KB),  // R1 = 10
            new Instruction(OpCode.MOVE,    a: 2, b: 1, c: 0, flags: OpFlags.KB),  // R2 = 20
            new Instruction(OpCode.MOVE,    a: 3, b: 2, c: 0, flags: OpFlags.KB),  // R3 = 30
            new Instruction(OpCode.MOVE,    a: 4, b: 3, c: 0, flags: OpFlags.KB),  // R4 = 40
            new Instruction(OpCode.CALL,    a: 0, b: 4, c: 1, flags: OpFlags.None), // R0 = inner(R1..R4)
            new Instruction(OpCode.RETURN,  a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 5, constants: new LuaValue[]
        {
            new LuaNumber(10),  // K[0]
            new LuaNumber(20),  // K[1]
            new LuaNumber(30),  // K[2]
            new LuaNumber(40),  // K[3]
        }, innerPrototypes: new[] { innerProto });

        var result = _interpreter.Call(outerProto, Context());
        Assert.Equal(20.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void Vararg_NoExtraArgs_ReturnsNil()
    {
        // Function: function(a, ...) return ... end
        // Called as: f(1) → no varargs → returns nil

        var innerProto = new FunctionPrototype(
            instructions: new[]
            {
                new Instruction(OpCode.VARARG, a: 1, b: 1, c: 0, flags: OpFlags.None),
                new Instruction(OpCode.RETURN, a: 1, b: 1, c: 0, flags: OpFlags.None),
            },
            maxRegSize: 2,
			isAsync: false,
			constants: Array.Empty<LuaValue>(),
            innerPrototypes: Array.Empty<FunctionPrototype>(),
            parameterCount: 1,
            isVararg: true);

        var outerProto = MakeProto(new[]
        {
            new Instruction(OpCode.CLOSURE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.MOVE,    a: 1, b: 0, c: 0, flags: OpFlags.KB),  // R1 = 1
            new Instruction(OpCode.CALL,    a: 0, b: 1, c: 1, flags: OpFlags.None), // R0 = inner(R1)
            new Instruction(OpCode.RETURN,  a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 2, constants: new LuaValue[]
        {
            new LuaNumber(1),   // K[0]
        }, innerPrototypes: new[] { innerProto });

        var result = _interpreter.Call(outerProto, Context());
        Assert.IsType<LuaNil>(result);
    }

    [Fact]
    public void Vararg_RequestZero_CopiesAll()
    {
        // Function: function(...) local a,b,c = ... end
        // Called as: f(10, 20, 30) → VARARG B=0 copies all 3, return second vararg = 20

        var innerProto = new FunctionPrototype(
            instructions: new[]
            {
                new Instruction(OpCode.VARARG, a: 0, b: 0, c: 0, flags: OpFlags.None),
                new Instruction(OpCode.RETURN, a: 1, b: 1, c: 0, flags: OpFlags.None),
            },
            maxRegSize: 3,
			isAsync: false,
			constants: Array.Empty<LuaValue>(),
            innerPrototypes: Array.Empty<FunctionPrototype>(),
            parameterCount: 0,
            isVararg: true);

        var outerProto = MakeProto(new[]
        {
            new Instruction(OpCode.CLOSURE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.MOVE,    a: 1, b: 0, c: 0, flags: OpFlags.KB),  // R1 = 10
            new Instruction(OpCode.MOVE,    a: 2, b: 1, c: 0, flags: OpFlags.KB),  // R2 = 20
            new Instruction(OpCode.MOVE,    a: 3, b: 2, c: 0, flags: OpFlags.KB),  // R3 = 30
            new Instruction(OpCode.CALL,    a: 0, b: 3, c: 1, flags: OpFlags.None), // R0 = inner(10,20,30)
            new Instruction(OpCode.RETURN,  a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 4, constants: new LuaValue[]
        {
            new LuaNumber(10),  // K[0]
            new LuaNumber(20),  // K[1]
            new LuaNumber(30),  // K[2]
        }, innerPrototypes: new[] { innerProto });

        var result = _interpreter.Call(outerProto, Context());
        Assert.Equal(20.0, Assert.IsType<LuaNumber>(result).Value);
    }
}
