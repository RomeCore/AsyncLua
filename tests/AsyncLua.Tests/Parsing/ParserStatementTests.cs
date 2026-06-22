using AsyncLua.Parsing;
using AsyncLua.Parsing.Expressions;
using AsyncLua.Parsing.Statements;
using AsyncLua.Values;

namespace AsyncLua.Tests.Parsing;

public class ParserStatementTests
{
    private static readonly AsyncLuaParser Parser = new();

    private static T ParseStatement<T>(string source) where T : StatementNode
    {
        var result = Parser.Parser.ParseRule("statement", source);
        return Assert.IsType<T>(result.GetValue<StatementNode>());
    }

    private static BlockNode ParseBlock(string source)
    {
        var result = Parser.Parser.ParseRule("block", source);
        return result.GetValue<BlockNode>();
    }

    // ── Assignment ───────────────────────────────────────────────────

    [Fact]
    public void Parse_SimpleAssignment_ReturnsAssignmentNode()
    {
        var stmt = ParseStatement<AssignmentNode>("x = 42");
        Assert.Null(stmt.Scope);
        Assert.Single(stmt.Targets);
        Assert.IsType<IdentifierNode>(stmt.Targets[0]);
        Assert.Equal("x", ((IdentifierNode)stmt.Targets[0]).Name);
        Assert.Single(stmt.Values);
        Assert.Equal(42.0, ((LuaNumber)((LiteralNode)stmt.Values[0]).Literal).Value);
    }

    [Fact]
    public void Parse_MultipleAssignment_ReturnsAssignmentNode()
    {
        var stmt = ParseStatement<AssignmentNode>("a, b = 1, 2");
        Assert.Equal(2, stmt.Targets.Length);
        Assert.Equal("a", ((IdentifierNode)stmt.Targets[0]).Name);
        Assert.Equal("b", ((IdentifierNode)stmt.Targets[1]).Name);
        Assert.Equal(2, stmt.Values.Length);
    }

    [Fact]
    public void Parse_TableFieldAssignment_ReturnsAssignmentNode()
    {
        var stmt = ParseStatement<AssignmentNode>("t.x = 10");
        var target = Assert.IsType<IndexNode>(stmt.Targets[0]);
        Assert.IsType<IdentifierNode>(target.Target);
        Assert.Equal("t", ((IdentifierNode)target.Target).Name);
        Assert.Equal("x", ((LuaString)((LiteralNode)target.Index).Literal).Value);
    }

    [Fact]
    public void Parse_TableIndexAssignment_ReturnsAssignmentNode()
    {
        var stmt = ParseStatement<AssignmentNode>("t[1] = 10");
        var target = Assert.IsType<IndexNode>(stmt.Targets[0]);
        Assert.Equal(1.0, ((LuaNumber)((LiteralNode)target.Index).Literal).Value);
    }

    // ── Local declaration ────────────────────────────────────────────

    [Fact]
    public void Parse_LocalDeclaration_ReturnsAssignmentNodeWithLocalScope()
    {
        var stmt = ParseStatement<AssignmentNode>("local x");
        Assert.Equal(VariableScope.Local, stmt.Scope);
        Assert.Single(stmt.Targets);
        Assert.Empty(stmt.Values);
    }

    [Fact]
    public void Parse_LocalDeclarationWithValue_ReturnsAssignmentNode()
    {
        var stmt = ParseStatement<AssignmentNode>("local x = 5");
        Assert.Equal(VariableScope.Local, stmt.Scope);
        Assert.Single(stmt.Values);
    }

    [Fact]
    public void Parse_LocalMultipleDeclarationWithValues_ReturnsAssignmentNode()
    {
        var stmt = ParseStatement<AssignmentNode>("local a, b = 1, 2");
        Assert.Equal(VariableScope.Local, stmt.Scope);
        Assert.Equal(2, stmt.Targets.Length);
        Assert.Equal(2, stmt.Values.Length);
    }

    // ── If statement ─────────────────────────────────────────────────

    [Fact]
    public void Parse_IfStatement_ReturnsIfNode()
    {
        var stmt = ParseStatement<IfNode>("if true then x = 1 end");
        Assert.IsType<LiteralNode>(stmt.Condition);
        Assert.Single(stmt.Body.Statements);
        Assert.Empty(stmt.ElseIfClauses);
        Assert.Null(stmt.ElseBlock);
    }

    [Fact]
    public void Parse_IfElseStatement_ReturnsIfNode()
    {
        var stmt = ParseStatement<IfNode>("if true then x = 1 else x = 2 end");
        Assert.NotNull(stmt.ElseBlock);
        Assert.Single(stmt.ElseBlock.Statements);
    }

    [Fact]
    public void Parse_IfElseIfStatement_ReturnsIfNode()
    {
        var stmt = ParseStatement<IfNode>("if true then x = 1 elseif false then x = 2 end");
        Assert.Single(stmt.ElseIfClauses);
        Assert.Null(stmt.ElseBlock);
    }

    [Fact]
    public void Parse_IfElseIfElseStatement_ReturnsIfNode()
    {
        var stmt = ParseStatement<IfNode>("if true then x = 1 elseif false then x = 2 else x = 3 end");
        Assert.Single(stmt.ElseIfClauses);
        Assert.NotNull(stmt.ElseBlock);
    }

    // ── While loop ───────────────────────────────────────────────────

    [Fact]
    public void Parse_WhileStatement_ReturnsWhileNode()
    {
        var stmt = ParseStatement<WhileNode>("while x < 10 do x = x + 1 end");
        Assert.IsType<BinaryOperatorNode>(stmt.Condition);
        Assert.Single(stmt.Body.Statements);
    }

    // ── Repeat loop ──────────────────────────────────────────────────

    [Fact]
    public void Parse_RepeatStatement_ReturnsRepeatNode()
    {
        var stmt = ParseStatement<RepeatNode>("repeat x = x + 1 until x > 10");
        Assert.Single(stmt.Body.Statements);
        Assert.IsType<BinaryOperatorNode>(stmt.Condition);
    }

    // ── Numeric for loop ─────────────────────────────────────────────

    [Fact]
    public void Parse_NumericFor_ReturnsForNumericNode()
    {
        var stmt = ParseStatement<ForNumericNode>("for i = 1, 10 do print(i) end");
        Assert.Equal("i", stmt.Variable);
        Assert.NotNull(stmt.Start);
        Assert.NotNull(stmt.Limit);
        Assert.Null(stmt.Step);
        Assert.Single(stmt.Body.Statements);
    }

    [Fact]
    public void Parse_NumericForWithStep_ReturnsForNumericNode()
    {
        var stmt = ParseStatement<ForNumericNode>("for i = 1, 10, 2 do print(i) end");
        Assert.Equal("i", stmt.Variable);
        Assert.NotNull(stmt.Start);
        Assert.NotNull(stmt.Limit);
        Assert.NotNull(stmt.Step);
    }

    // ── Generic for loop ─────────────────────────────────────────────

    [Fact]
    public void Parse_GenericFor_ReturnsForInNode()
    {
        var stmt = ParseStatement<ForInNode>("for k, v in pairs(t) do print(k) end");
        Assert.Equal(2, stmt.Variables.Length);
        Assert.Equal("k", stmt.Variables[0]);
        Assert.Equal("v", stmt.Variables[1]);
        Assert.Single(stmt.Expressions);
        Assert.Single(stmt.Body.Statements);
    }

    // ── Function declaration ─────────────────────────────────────────

    [Fact]
    public void Parse_FunctionDeclaration_ReturnsFunctionDeclStatementNode()
    {
        var stmt = ParseStatement<FunctionDeclStatementNode>("function foo() end");
        Assert.Equal("foo", stmt.Name);
        Assert.Null(stmt.TargetObject);
        Assert.Null(stmt.Scope);
        Assert.Empty(stmt.Body.Statements);
    }

    [Fact]
    public void Parse_FunctionDeclarationWithParams_ReturnsFunctionDeclStatementNode()
    {
        var stmt = ParseStatement<FunctionDeclStatementNode>("function add(a, b) return a + b end");
        Assert.Equal("add", stmt.Name);
        Assert.Equal(2, stmt.Parameters.Length);
        Assert.Equal("a", stmt.Parameters[0].Name);
        Assert.Equal("b", stmt.Parameters[1].Name);
        Assert.Single(stmt.Body.Statements);
    }

    [Fact]
    public void Parse_LocalFunctionDeclaration_ReturnsFunctionDeclStatementNode()
    {
        var stmt = ParseStatement<FunctionDeclStatementNode>("local function helper() end");
        Assert.Equal("helper", stmt.Name);
        Assert.Equal(VariableScope.Local, stmt.Scope);
    }

    [Fact]
    public void Parse_MethodStyleFunctionDeclaration_ReturnsFunctionDeclStatementNode()
    {
        var stmt = ParseStatement<FunctionDeclStatementNode>("function t:method() end");
        Assert.NotNull(stmt.TargetObject);
        Assert.Equal("t", ((IdentifierNode)stmt.TargetObject).Name);
        Assert.Equal("method", stmt.Name);
    }

    // ── Return ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_ReturnNoValue_ReturnsReturnNode()
    {
        var stmt = ParseStatement<ReturnNode>("return");
        Assert.Empty(stmt.Values);
    }

    [Fact]
    public void Parse_ReturnSingleValue_ReturnsReturnNode()
    {
        var stmt = ParseStatement<ReturnNode>("return 42");
        Assert.Single(stmt.Values);
    }

    [Fact]
    public void Parse_ReturnMultipleValues_ReturnsReturnNode()
    {
        var stmt = ParseStatement<ReturnNode>("return 1, 2, 3");
        Assert.Equal(3, stmt.Values.Length);
    }

    // ── Break / Goto / Label ─────────────────────────────────────────

    [Fact]
    public void Parse_Break_ReturnsBreakNode()
    {
        ParseStatement<BreakNode>("break");
    }

    [Fact]
    public void Parse_Goto_ReturnsGotoNode()
    {
        var stmt = ParseStatement<GotoNode>("goto exit");
        Assert.Equal("exit", stmt.LabelName);
    }

    [Fact]
    public void Parse_Label_ReturnsLabelNode()
    {
        var stmt = ParseStatement<LabelNode>("::exit::");
        Assert.Equal("exit", stmt.Name);
    }

    // ── Do block ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_DoBlock_ReturnsDoNode()
    {
        var stmt = ParseStatement<DoNode>("do local x = 1 end");
        Assert.Single(stmt.Body.Statements);
    }

    // ── Call statement ───────────────────────────────────────────────

    [Fact]
    public void Parse_FunctionCallStatement_ReturnsCallStatementNode()
    {
        var stmt = ParseStatement<CallStatementNode>("print(\"hello\")");
        Assert.Equal("print", ((IdentifierNode)stmt.Call.Target).Name);
        Assert.Single(stmt.Call.Arguments);
    }

    [Fact]
    public void Parse_MethodCallStatement_ReturnsCallStatementNode()
    {
        var stmt = ParseStatement<CallStatementNode>("obj:method(1, 2)");
        Assert.Equal("method", stmt.Call.Method);
        Assert.Equal("obj", ((IdentifierNode)stmt.Call.Target).Name);
        Assert.Equal(2, stmt.Call.Arguments.Length);
    }

    // ── Lock / Await (AsyncLua extensions) ───────────────────────────

    [Fact]
    public void Parse_LockStatement_ReturnsLockNode()
    {
        var stmt = ParseStatement<LockNode>("lock obj do x = 1 end");
        Assert.IsType<IdentifierNode>(stmt.Target);
        Assert.Single(stmt.Body.Statements);
    }

    [Fact]
    public void Parse_AwaitStatement_ReturnsAwaitStatementNode()
    {
        var stmt = ParseStatement<AwaitStatementNode>("await task");
        Assert.IsType<IdentifierNode>(stmt.AwaitExpression.Expression);
        Assert.Equal("task", ((IdentifierNode)stmt.AwaitExpression.Expression).Name);
    }

    // ── Block (multiple statements) ──────────────────────────────────

    [Fact]
    public void Parse_Block_MultipleStatements()
    {
        var block = ParseBlock("x = 1\ny = 2\nz = 3\n");
        Assert.Equal(3, block.Statements.Length);
    }

    [Fact]
    public void Parse_Block_WithSemicolons()
    {
        var block = ParseBlock("x = 1; y = 2; z = 3;");
        Assert.Equal(3, block.Statements.Length);
    }

    [Fact]
    public void Parse_Block_MixedSeparators()
    {
        var block = ParseBlock("x = 1\ny = 2; z = 3\n");
        Assert.Equal(3, block.Statements.Length);
    }
}
