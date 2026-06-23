using AsyncLua.Interpreting;
using AsyncLua.Values;

namespace AsyncLua.Tests.Interpreting;

public class ArithmeticInterpreterTests
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

    // ── MOVE ──────────────────────────────────────────────────────────

    [Fact]
    public void Move_RegisterToRegister_CopiesValue()
    {
        // R[0] = R[1]
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 2);

        var result = _interpreter.Call(proto, Context());
        // R[0] = R[1] (nil), returns R[0] = nil
        Assert.IsType<LuaNil>(result);
    }

    [Fact]
    public void Move_ConstantToRegister_LoadsConstant()
    {
        // R[0] = K[0]  (K[0] = 42)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42) });

        var result = _interpreter.Call(proto, Context());
        var num = Assert.IsType<LuaNumber>(result);
        Assert.Equal(42.0, num.Value);
    }

    // ── Arithmetic ────────────────────────────────────────────────────

    [Fact]
    public void Add_TwoRegisters_ReturnsSum()
    {
        // R[0] = K[0] + K[1]  (10 + 32 = 42)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.ADD, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(10), new LuaNumber(32) });

        var result = _interpreter.Call(proto, Context());
        var num = Assert.IsType<LuaNumber>(result);
        Assert.Equal(42.0, num.Value);
    }

    [Fact]
    public void Sub_TwoRegisters_ReturnsDifference()
    {
        // R[0] = K[0] - K[1]  (100 - 58 = 42)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.SUB, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(100), new LuaNumber(58) });

        var result = _interpreter.Call(proto, Context());
        var num = Assert.IsType<LuaNumber>(result);
        Assert.Equal(42.0, num.Value);
    }

    [Fact]
    public void Mul_TwoRegisters_ReturnsProduct()
    {
        // R[0] = K[0] * K[1]  (6 * 7 = 42)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MUL, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(6), new LuaNumber(7) });

        var result = _interpreter.Call(proto, Context());
        var num = Assert.IsType<LuaNumber>(result);
        Assert.Equal(42.0, num.Value);
    }

    [Fact]
    public void Div_TwoRegisters_ReturnsQuotient()
    {
        // R[0] = K[0] / K[1]  (84 / 2 = 42)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.DIV, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(84), new LuaNumber(2) });

        var result = _interpreter.Call(proto, Context());
        var num = Assert.IsType<LuaNumber>(result);
        Assert.Equal(42.0, num.Value);
    }

    [Fact]
    public void IDiv_TwoRegisters_ReturnsFlooredQuotient()
    {
        // R[0] = K[0] // K[1]  (85 // 2 = 42)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.IDIV, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(85), new LuaNumber(2) });

        var result = _interpreter.Call(proto, Context());
        var num = Assert.IsType<LuaNumber>(result);
        Assert.Equal(42.0, num.Value);
    }

    [Fact]
    public void IDiv_Negative_FloorsCorrectly()
    {
        // -85 // 2 = -43 (floor division: floor(-42.5) = -43)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.IDIV, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(-85), new LuaNumber(2) });

        var result = _interpreter.Call(proto, Context());
        var num = Assert.IsType<LuaNumber>(result);
        Assert.Equal(-43.0, num.Value);
    }

    // ── Multi-instruction function ────────────────────────────────────

    [Fact]
    public void MultiStep_ComputeExpression_ReturnsCorrectResult()
    {
        // Evaluates: return (10 + 5) * 3 - 2
        //
        // R[0] = K[0] (10)
        // R[1] = K[1] (5)
        // R[2] = R[0] + R[1]   → 15
        // R[2] = R[2] * K[2]   → 45
        // R[2] = R[2] - K[3]   → 43
        // return R[2]

        var proto = MakeProto(new[]
        {
            // Constants
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB), // R[0] = 10
            new Instruction(OpCode.MOVE, a: 1, b: 1, c: 0, flags: OpFlags.KB), // R[1] = 5
            // Calculations
            new Instruction(OpCode.ADD, a: 2, b: 0, c: 1, flags: OpFlags.None),  // R[2] = R[0] + R[1] = 15
            new Instruction(OpCode.MUL, a: 2, b: 2, c: 2, flags: OpFlags.KC),    // R[2] = R[2] * K[2] = 45
            new Instruction(OpCode.SUB, a: 2, b: 2, c: 3, flags: OpFlags.KC),    // R[2] = R[2] - K[3] = 43
            // Return
            new Instruction(OpCode.RETURN, a: 2, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 3, constants: new LuaValue[]
        {
            new LuaNumber(10),  // K[0]
            new LuaNumber(5),   // K[1]
            new LuaNumber(3),   // K[2]
            new LuaNumber(2),   // K[3]
        });

        var result = _interpreter.Call(proto, Context());
        var num = Assert.IsType<LuaNumber>(result);
        Assert.Equal(43.0, num.Value);
    }

    // ── Error cases ────────────────────────────────────────────────────

    [Fact]
    public void Arithmetic_OnNonNumber_Throws()
    {
        // R[0] = K[0] + K[1], but K[1] — string
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.ADD, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(10), new LuaString("hello") });

        Assert.Throws<LuaRuntimeException>(() => _interpreter.Call(proto, Context()));
    }

    [Fact]
    public void Return_ZeroResults_ReturnsNil()
    {
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.RETURN, a: 0, b: 0, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1);

        var result = _interpreter.Call(proto, Context());
        Assert.IsType<LuaNil>(result);
    }
}
