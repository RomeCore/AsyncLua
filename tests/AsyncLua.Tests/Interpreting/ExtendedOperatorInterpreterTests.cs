using AsyncLua.Interpreting;
using AsyncLua.Values;

namespace AsyncLua.Tests.Interpreting;

public class ExtendedOperatorInterpreterTests
{
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

    // ── POW ───────────────────────────────────────────────────────────

    [Fact]
    public void Pow_TwoNumbers_ReturnsPower()
    {
        // R[0] = K[0] ^ K[1]  (2 ^ 3 = 8)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.POW, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(2), new LuaNumber(3) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(8.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Pow_ZeroExponent_ReturnsOne()
    {
        // R[0] = K[0] ^ K[1]  (5 ^ 0 = 1)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.POW, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(5), new LuaNumber(0) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── MOD ───────────────────────────────────────────────────────────

    [Fact]
    public void Mod_PositivePositive_ReturnsFloorModulo()
    {
        // R[0] = K[0] % K[1]  (10 % 3 = 1)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOD, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(10), new LuaNumber(3) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Mod_NegativePositive_ReturnsFloorModulo()
    {
        // R[0] = K[0] % K[1]  (-10 % 3 = 2)  — Lua floor-modulo
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOD, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(-10), new LuaNumber(3) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(2.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Mod_PositiveNegative_ReturnsFloorModulo()
    {
        // R[0] = K[0] % K[1]  (10 % -3 = -2)  — Lua floor-modulo
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOD, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(10), new LuaNumber(-3) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(-2.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Mod_ByZero_Throws()
    {
        // R[0] = K[0] % K[1]  (10 % 0 → error)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOD, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(10), new LuaNumber(0) });

        Assert.Throws<LuaRuntimeException>(() => Interpreter.Call(proto, Context()));
    }

    // ── CONCAT ────────────────────────────────────────────────────────

    [Fact]
    public void Concat_TwoStrings_ReturnsConcatenated()
    {
        // R[0] = K[0] .. K[1]  ("hello" .. " world" = "hello world")
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.CONCAT, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("hello"), new LuaString(" world") });

        var result = Interpreter.Call(proto, Context());
        var str = Assert.IsType<LuaString>(result.First);
        Assert.Equal("hello world", str.Value);
    }

    [Fact]
    public void Concat_NumberAndString_ReturnsConcatenated()
    {
        // R[0] = K[0] .. K[1]  (42 .. " is the answer")
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.CONCAT, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42), new LuaString(" is the answer") });

        var result = Interpreter.Call(proto, Context());
        var str = Assert.IsType<LuaString>(result.First);
        Assert.Equal("42 is the answer", str.Value);
    }

    [Fact]
    public void Concat_NonStringOrNumber_Throws()
    {
        // R[0] = true .. "text"  → error
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.CONCAT, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { LuaBoolean.True, new LuaString("text") });

        Assert.Throws<LuaRuntimeException>(() => Interpreter.Call(proto, Context()));
    }

    // ── UNM ───────────────────────────────────────────────────────────

    [Fact]
    public void Unm_PositiveNumber_ReturnsNegative()
    {
        // R[0] = -K[0]  (-42 = -42)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.UNM, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(-42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Unm_NegativeNumber_ReturnsPositive()
    {
        // R[0] = -K[0]  (-(-42) = 42)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.UNM, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(-42) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Unm_NonNumber_Throws()
    {
        // R[0] = -"hello"  → error
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.UNM, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("hello") });

        Assert.Throws<LuaRuntimeException>(() => Interpreter.Call(proto, Context()));
    }

    // ── NOT ───────────────────────────────────────────────────────────

    [Fact]
    public void Not_True_ReturnsFalse()
    {
        // R[0] = not K[0]  (not true → false)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NOT, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { LuaBoolean.True });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(LuaBoolean.False, result.First);
    }

    [Fact]
    public void Not_False_ReturnsTrue()
    {
        // R[0] = not K[0]  (not false → true)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NOT, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { LuaBoolean.False });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(LuaBoolean.True, result.First);
    }

    [Fact]
    public void Not_Nil_ReturnsTrue()
    {
        // R[0] = not K[0]  (not nil → true)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NOT, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { LuaNil.Instance });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(LuaBoolean.True, result.First);
    }

    [Fact]
    public void Not_Number_ReturnsFalse()
    {
        // R[0] = not K[0]  (not 0 → false, because 0 is truthy in Lua)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NOT, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(0) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(LuaBoolean.False, result.First);
    }

    // ── LEN ───────────────────────────────────────────────────────────

    [Fact]
    public void Len_String_ReturnsLength()
    {
        // R[0] = #K[0]  (#"hello" = 5)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.LEN, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("hello") });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(5.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Len_EmptyString_ReturnsZero()
    {
        // R[0] = #K[0]  (#"" = 0)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.LEN, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { LuaString.Empty });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(0.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Len_Table_ReturnsArrayLength()
    {
        // Build table with 3 array values, then get #table
        // R[0] = {}; R[0][1]="a"; R[0][2]="b"; R[0][3]="c"; R[1] = #R[0]; return R[1]
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.SETTABLE, a: 0, b: 1, c: 0, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.SETTABLE, a: 0, b: 2, c: 4, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.SETTABLE, a: 0, b: 3, c: 5, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.LEN, a: 1, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 1, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 2, constants: new LuaValue[]
        {
            new LuaString("a"),   // K[0]
            new LuaNumber(1),     // K[1]
            new LuaNumber(2),     // K[2]
            new LuaNumber(3),     // K[3]
            new LuaString("b"),   // K[4]
            new LuaString("c"),   // K[5]
        });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(3.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Len_Nil_Throws()
    {
        // R[0] = #K[0]  (#nil → error)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.LEN, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { LuaNil.Instance });

        Assert.Throws<LuaRuntimeException>(() => Interpreter.Call(proto, Context()));
    }

    // ── NE ────────────────────────────────────────────────────────────

    [Fact]
    public void Ne_TwoEqualNumbers_ReturnsFalse()
    {
        // R[0] = (K[0] ~= K[1])  — 42 ~= 42 → false
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42), new LuaNumber(42) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(LuaBoolean.False, result.First);
    }

    [Fact]
    public void Ne_TwoDifferentNumbers_ReturnsTrue()
    {
        // R[0] = (K[0] ~= K[1])  — 42 ~= 99 → true
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42), new LuaNumber(99) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(LuaBoolean.True, result.First);
    }

    [Fact]
    public void Ne_TwoEqualStrings_ReturnsFalse()
    {
        // R[0] = (K[0] ~= K[1])  — "abc" ~= "abc" → false
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("abc"), new LuaString("abc") });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(LuaBoolean.False, result.First);
    }

    [Fact]
    public void Ne_TwoDifferentStrings_ReturnsTrue()
    {
        // R[0] = (K[0] ~= K[1])  — "abc" ~= "xyz" → true
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("abc"), new LuaString("xyz") });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(LuaBoolean.True, result.First);
    }

    // ── BAND ──────────────────────────────────────────────────────────

    [Fact]
    public void Band_TwoIntegers_ReturnsBitwiseAnd()
    {
        // R[0] = K[0] & K[1]  (0xFF & 0x0F = 0x0F = 15)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.BAND, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(255), new LuaNumber(15) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Band_SimpleValues_ReturnsCorrect()
    {
        // R[0] = K[0] & K[1]  (5 & 3 = 1)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.BAND, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(5), new LuaNumber(3) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Band_NonInteger_Throws()
    {
        // R[0] = 3.5 & 2  → error
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.BAND, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(3.5), new LuaNumber(2) });

        Assert.Throws<LuaRuntimeException>(() => Interpreter.Call(proto, Context()));
    }

    // ── BOR ───────────────────────────────────────────────────────────

    [Fact]
    public void Bor_TwoIntegers_ReturnsBitwiseOr()
    {
        // R[0] = K[0] | K[1]  (0x0F | 0xF0 = 0xFF = 255)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.BOR, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(15), new LuaNumber(240) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(255.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Bor_SimpleValues_ReturnsCorrect()
    {
        // R[0] = K[0] | K[1]  (5 | 2 = 7)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.BOR, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(5), new LuaNumber(2) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(7.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── BXOR ──────────────────────────────────────────────────────────

    [Fact]
    public void Bxor_TwoIntegers_ReturnsBitwiseXor()
    {
        // R[0] = K[0] ~ K[1]  (0xFF ^ 0x0F = 0xF0 = 240)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.BXOR, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(255), new LuaNumber(15) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(240.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Bxor_SimpleValues_ReturnsCorrect()
    {
        // R[0] = K[0] ~ K[1]  (5 ^ 3 = 6)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.BXOR, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(5), new LuaNumber(3) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(6.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── SHL ───────────────────────────────────────────────────────────

    [Fact]
    public void Shl_PositiveShift_ReturnsShifted()
    {
        // R[0] = K[0] << K[1]  (1 << 3 = 8)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.SHL, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(1), new LuaNumber(3) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(8.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Shl_LargerShift_ReturnsCorrect()
    {
        // R[0] = K[0] << K[1]  (5 << 2 = 20)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.SHL, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(5), new LuaNumber(2) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(20.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── SHR ───────────────────────────────────────────────────────────

    [Fact]
    public void Shr_PositiveShift_ReturnsShifted()
    {
        // R[0] = K[0] >> K[1]  (8 >> 3 = 1)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.SHR, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(8), new LuaNumber(3) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(1.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Shr_LargerShift_ReturnsCorrect()
    {
        // R[0] = K[0] >> K[1]  (20 >> 2 = 5)
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.SHR, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(20), new LuaNumber(2) });

        var result = Interpreter.Call(proto, Context());
        Assert.Equal(5.0, Assert.IsType<LuaNumber>(result.First).Value);
    }
}
