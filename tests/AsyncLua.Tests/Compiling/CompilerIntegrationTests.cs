using AsyncLua.Compiling;
using AsyncLua.Interpreting;
using AsyncLua.Parsing;
using AsyncLua.Values;

namespace AsyncLua.Tests.Compiling;

public class CompilerIntegrationTests
{
    private static LuaTuple CompileAndExecute(string code, LuaCallingContext? context = null)
    {
        var parser = new AsyncLuaParser();
        var block = parser.Parse(code);
        var prototype = Compiler.Compile(block, sourceName: "test");
        return Interpreter.Call(prototype, context ?? new LuaState().CreateContext());
    }

    private static async Task<LuaTuple> CompileAndExecuteAsync(string code, LuaCallingContext? context = null)
    {
        var parser = new AsyncLuaParser();
        var block = parser.Parse(code);
        var prototype = Compiler.Compile(block, sourceName: "test");
        return await Interpreter.CallAsync(prototype, context ?? new LuaState().CreateContext());
    }

    // ── Literals ──────────────────────────────────────────────────────

    [Fact]
    public void NilLiteral_ReturnsNil()
    {
        var result = CompileAndExecute("return nil");
        Assert.IsType<LuaNil>(result.First);
    }

    [Fact]
    public void BooleanLiteral_ReturnsBoolean()
    {
        var result = CompileAndExecute("return true");
        Assert.Same(LuaBoolean.True, result.First);

        result = CompileAndExecute("return false");
        Assert.Same(LuaBoolean.False, result.First);
    }

    [Fact]
    public void NumberLiteral_ReturnsNumber()
    {
        var result = CompileAndExecute("return 42");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void StringLiteral_ReturnsString()
    {
        var result = CompileAndExecute("return 'hello'");
        Assert.Equal("hello", Assert.IsType<LuaString>(result.First).Value);
    }

    // ── Arithmetic ────────────────────────────────────────────────────

    [Fact]
    public void Addition_ReturnsSum()
    {
        var result = CompileAndExecute("return 10 + 32");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void MixedArithmetic_ReturnsCorrectResult()
    {
        var result = CompileAndExecute("return (100 - 58) * (6 / 2) + 2 ^ 3");
        // (42) * (3) + 8 = 126 + 8 = 134
        Assert.Equal(134.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── Comparisons ───────────────────────────────────────────────────

    [Fact]
    public void Equality_ReturnsBoolean()
    {
        var result = CompileAndExecute("return 1 == 1");
        Assert.Same(LuaBoolean.True, result.First);

        result = CompileAndExecute("return 1 ~= 1");
        Assert.Same(LuaBoolean.False, result.First);
    }

    [Fact]
    public void LessThan_ReturnsBoolean()
    {
        var result = CompileAndExecute("return 10 < 20");
        Assert.Same(LuaBoolean.True, result.First);

        result = CompileAndExecute("return 20 < 10");
        Assert.Same(LuaBoolean.False, result.First);
    }

    // ── Local variables ───────────────────────────────────────────────

    [Fact]
    public void LocalVariable_CanBeAssignedAndRead()
    {
        var result = CompileAndExecute(@"
            local x = 100
            local y = 58
            return x - y
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void LocalVariable_CanBeReassigned()
    {
        var result = CompileAndExecute(@"
            local x = 10
            x = x * 4
            return x + 2
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── If statements ─────────────────────────────────────────────────

    [Fact]
    public void If_TrueBranch_Executes()
    {
        var result = CompileAndExecute(@"
            local x = 0
            if true then
                x = 42
            end
            return x
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void If_FalseBranch_Skips()
    {
        var result = CompileAndExecute(@"
            local x = 10
            if false then
                x = 99
            end
            return x
        ");
        Assert.Equal(10.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void If_ElseIf_Works()
    {
        var result = CompileAndExecute(@"
            local x = 0
            local v = 20
            if v == 10 then
                x = 1
            elseif v == 20 then
                x = 42
            else
                x = 99
            end
            return x
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void If_Else_Works()
    {
        var result = CompileAndExecute(@"
            local x = 0
            if false then
                x = 1
            else
                x = 42
            end
            return x
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── While loops ───────────────────────────────────────────────────

    [Fact]
    public void While_Loop_Iterates()
    {
        var result = CompileAndExecute(@"
            local i = 0
            local sum = 0
            while i < 5 do
                sum = sum + i
                i = i + 1
            end
            return sum
        ");
        // 0 + 1 + 2 + 3 + 4 = 10
        Assert.Equal(10.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── Repeat loops ──────────────────────────────────────────────────

    [Fact]
    public void Repeat_Loop_Iterates()
    {
        var result = CompileAndExecute(@"
            local i = 0
            repeat
                i = i + 1
            until i == 5
            return i
        ");
        Assert.Equal(5.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── Numeric for loops ─────────────────────────────────────────────

    [Fact]
    public void ForNumeric_Basic_Works()
    {
        var result = CompileAndExecute(@"
            local sum = 0
            for i = 1, 5 do
                sum = sum + i
            end
            return sum
        ");
        // 1 + 2 + 3 + 4 + 5 = 15
        Assert.Equal(15.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void ForNumeric_WithStep_Works()
    {
        var result = CompileAndExecute(@"
            local sum = 0
            for i = 0, 10, 2 do
                sum = sum + i
            end
            return sum
        ");
        // 0 + 2 + 4 + 6 + 8 + 10 = 30
        Assert.Equal(30.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── Tables ────────────────────────────────────────────────────────

    [Fact]
    public void Table_Constructor_CreatesTable()
    {
        var result = CompileAndExecute("return {}");
        var table = Assert.IsType<LuaTable>(result.First);
        Assert.Equal(0, table.Length);
    }

    [Fact]
    public void Table_ArrayElements_Accessible()
    {
        var result = CompileAndExecute(@"
            local t = {10, 20, 30}
            return t[2]
        ");
        Assert.Equal(20.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void Table_NamedFields_Accessible()
    {
        var result = CompileAndExecute(@"
            local t = {x = 42, y = 99}
            return t.x
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── Functions ─────────────────────────────────────────────────────

    [Fact]
    public void FunctionCall_Simple_ReturnsResult()
    {
        var state = new LuaState();
        state.Register("add", LuaCallbackFunction.From(
            (LuaValue[] args) => new LuaNumber(
                ((LuaNumber)args[0]).Value + ((LuaNumber)args[1]).Value)));

        var result = CompileAndExecute("return add(10, 32)", state.CreateContext());
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void LocalFunction_CanBeDefinedAndCalled()
    {
        var result = CompileAndExecute(@"
            local function double(x)
                return x * 2
            end
            return double(21)
        ");
        Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    // ── Multiple returns ──────────────────────────────────────────────

    [Fact]
    public void MultipleReturns_AllCaptured()
    {
        var result = CompileAndExecute("return 1, 2, 3");
        Assert.Equal(3, result.Count);
        Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
        Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
        Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[2]).Value);
    }

    // ── Concatenation ─────────────────────────────────────────────────

    [Fact]
    public void Concatenation_JoinsStrings()
    {
        var result = CompileAndExecute("return 'Hello, ' .. 'World!'");
        Assert.Equal("Hello, World!", Assert.IsType<LuaString>(result.First).Value);
    }

    // ── Unary operators ───────────────────────────────────────────────

    [Fact]
    public void UnaryMinus_NegatesNumber()
    {
        var result = CompileAndExecute("return -42");
        Assert.Equal(-42.0, Assert.IsType<LuaNumber>(result.First).Value);
    }

    [Fact]
    public void LogicalNot_InvertsBoolean()
    {
        var result = CompileAndExecute("return not false");
        Assert.Same(LuaBoolean.True, result.First);

        result = CompileAndExecute("return not true");
        Assert.Same(LuaBoolean.False, result.First);
    }

    // ── Errors ────────────────────────────────────────────────────────

    [Fact]
    public void ArithmeticOnNil_ThrowsRuntimeError()
    {
        Assert.Throws<LuaRuntimeException>(() => CompileAndExecute("return nil + 1"));
    }

    [Fact]
    public void CallNonFunction_ThrowsRuntimeError()
    {
        Assert.Throws<LuaRuntimeException>(() => CompileAndExecute("return 42()"));
    }
}
