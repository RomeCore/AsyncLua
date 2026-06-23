using AsyncLua.Interpreting;
using AsyncLua.Values;

namespace AsyncLua.Tests.Interpreting;

public class TableInterpreterTests
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

	// ── NEWTABLE ──────────────────────────────────────────────────────

	[Fact]
    public void NewTable_CreatesEmptyTable()
    {
        // R[0] = {}
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1);

        var result = _interpreter.Call(proto, Context());
        Assert.IsType<LuaTable>(result);
    }

    [Fact]
    public void NewTable_ResultIsEmpty()
    {
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1);

        var result = _interpreter.Call(proto, Context());
        var table = Assert.IsType<LuaTable>(result);
        Assert.Equal(0, table.Count);
    }

    // ── SETTABLE / GETTABLE ───────────────────────────────────────────

    [Fact]
    public void SetTable_ThenGetTable_ReturnsStoredValue()
    {
        // R[0] = {}; R[0]["key"] = 42; R[0] = R[0]["key"]; return R[0]
        var proto = MakeProto(new[]
        {
            // R[0] = {}
            new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // R[0]["key"] = 42  (key = K[0] = "key", value = K[1] = 42)
            new Instruction(OpCode.SETTABLE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            // R[0] = R[0]["key"]
            new Instruction(OpCode.GETTABLE, a: 0, b: 0, c: 0, flags: OpFlags.KC),
            // return R[0]
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("key"), new LuaNumber(42) });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void SetTable_OverwritesExistingKey()
    {
        // R[0] = {}; R[0]["k"] = 10; R[0]["k"] = 99; R[0] = R[0]["k"]; return R[0]
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // R[0]["k"] = 10
            new Instruction(OpCode.SETTABLE, a: 0, b: 0, c: 1, flags: OpFlags.KB | OpFlags.KC),
            // R[0]["k"] = 99
            new Instruction(OpCode.SETTABLE, a: 0, b: 0, c: 2, flags: OpFlags.KB | OpFlags.KC),
            // R[0] = R[0]["k"]
            new Instruction(OpCode.GETTABLE, a: 0, b: 0, c: 0, flags: OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("k"), new LuaNumber(10), new LuaNumber(99) });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(99.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void GetTable_MissingKey_ReturnsNil()
    {
        // R[0] = {}; R[0] = R[0]["nope"]; return R[0]
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // R[0] = R[0]["nope"]
            new Instruction(OpCode.GETTABLE, a: 0, b: 0, c: 0, flags: OpFlags.KC),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("nope") });

        var result = _interpreter.Call(proto, Context());
        Assert.IsType<LuaNil>(result);
    }

    [Fact]
    public void SetTable_WithRegisterKey_Works()
    {
        // R[0] = {}; R[1] = "key"; R[0][R[1]] = 42; R[0] = R[0][R[1]]; return R[0]
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.NEWTABLE, a: 0, b: 0, c: 0, flags: OpFlags.None),
            // R[1] = "key"
            new Instruction(OpCode.MOVE, a: 1, b: 0, c: 0, flags: OpFlags.KB),
            // R[0][R[1]] = 42  (key from register, value from constant)
            new Instruction(OpCode.SETTABLE, a: 0, b: 1, c: 1, flags: OpFlags.None | OpFlags.KC), // B=reg, C=const
            // R[0] = R[0][R[1]]
            new Instruction(OpCode.GETTABLE, a: 0, b: 0, c: 1, flags: OpFlags.None), // C from register
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 2, constants: new LuaValue[] { new LuaString("key"), new LuaNumber(42) });

        var result = _interpreter.Call(proto, Context());
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void GetTable_NotATable_Throws()
    {
        // R[0] = 42; GETTABLE on a number → should throw
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 0, c: 0, flags: OpFlags.KB),   // R[0] = 42
            new Instruction(OpCode.GETTABLE, a: 0, b: 0, c: 1, flags: OpFlags.KC), // try get R[0][K[1]]
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaNumber(42), new LuaString("key") });

        Assert.Throws<LuaRuntimeException>(() => _interpreter.Call(proto, Context()));
    }

    // ── GETGLOBAL / SETGLOBAL ─────────────────────────────────────────

    [Fact]
    public void SetGlobal_ThenGetGlobal_ReturnsStoredValue()
    {
        // _G["x"] = 42; R[0] = _G["x"]; return R[0]
        var g = Context();
        var proto = MakeProto(new[]
        {
            // _G["x"] = 42
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.KB),     // R[0] = 42
            new Instruction(OpCode.SETGLOBAL, a: 0, b: 0, c: 0, flags: OpFlags.KB),   // _G[K[0]] = R[0]
            // R[0] = _G["x"]
            new Instruction(OpCode.GETGLOBAL, a: 0, b: 0, c: 0, flags: OpFlags.KB),   // R[0] = _G[K[0]]
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("x"), new LuaNumber(42) });

        var result = _interpreter.Call(proto, g);
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void GetGlobal_MissingKey_ReturnsNil()
    {
        // R[0] = _G["does_not_exist"]; return R[0]
        var proto = MakeProto(new[]
        {
            new Instruction(OpCode.GETGLOBAL, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("does_not_exist") });

        var result = _interpreter.Call(proto, Context());
        Assert.IsType<LuaNil>(result);
    }

    [Fact]
    public void SetGlobal_OverwritesExistingValue()
    {
        // _G["x"] = 10; _G["x"] = 99; R[0] = _G["x"]; return R[0]
        var g = Context();
        var proto = MakeProto(new[]
        {
            // _G["x"] = 10
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.SETGLOBAL, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            // _G["x"] = 99
            new Instruction(OpCode.MOVE, a: 0, b: 2, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.SETGLOBAL, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            // R[0] = _G["x"]
            new Instruction(OpCode.GETGLOBAL, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("x"), new LuaNumber(10), new LuaNumber(99) });

        var result = _interpreter.Call(proto, g);
        Assert.Equal(99.0, Assert.IsType<LuaNumber>(result).Value);
    }

    [Fact]
    public void SetGlobal_PersistsBetweenCalls()
    {
        var g = Context();

        // First call: _G["shared"] = 77
        var proto1 = MakeProto(new[]
        {
            new Instruction(OpCode.MOVE, a: 0, b: 1, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.SETGLOBAL, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("shared"), new LuaNumber(77) });

        _interpreter.Call(proto1, g);

        // Second call: R[0] = _G["shared"]
        var proto2 = MakeProto(new[]
        {
            new Instruction(OpCode.GETGLOBAL, a: 0, b: 0, c: 0, flags: OpFlags.KB),
            new Instruction(OpCode.RETURN, a: 0, b: 1, c: 0, flags: OpFlags.None),
        }, maxRegSize: 1, constants: new LuaValue[] { new LuaString("shared") });

        var result = _interpreter.Call(proto2, g);
        Assert.Equal(77.0, Assert.IsType<LuaNumber>(result).Value);
    }
}
