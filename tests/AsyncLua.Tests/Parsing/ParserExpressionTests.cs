using AsyncLua.Parsing;
using AsyncLua.Parsing.Expressions;
using AsyncLua.Parsing.Statements;
using AsyncLua.Values;

namespace AsyncLua.Tests.Parsing;

public class ParserExpressionTests
{
	private static readonly AsyncLuaParser Parser = new();

	private static ExpressionNode ParseExpression(string source)
	{
		var result = Parser.Parser.ParseRule("expression", source);
		return result.GetValue<ExpressionNode>();
	}

	private static StatementNode ParseStatement(string source)
	{
		var result = Parser.Parser.ParseRule("statement", source);
		return result.GetValue<StatementNode>();
	}

	private static BlockNode ParseBlock(string source)
	{
		var result = Parser.Parser.ParseRule("block", source);
		return result.GetValue<BlockNode>();
	}

	// ── Literals ─────────────────────────────────────────────────────

	[Fact]
	public void Parse_NilLiteral_ReturnsLiteralNode()
	{
		var expr = ParseExpression("nil");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.IsType<LuaNil>(literal.Literal);
	}

	[Fact]
	public void Parse_TrueLiteral_ReturnsLiteralNode()
	{
		var expr = ParseExpression("true");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.IsType<LuaBoolean>(literal.Literal);
		Assert.True(((LuaBoolean)literal.Literal).Value);
	}

	[Fact]
	public void Parse_FalseLiteral_ReturnsLiteralNode()
	{
		var expr = ParseExpression("false");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.IsType<LuaBoolean>(literal.Literal);
		Assert.False(((LuaBoolean)literal.Literal).Value);
	}

	[Fact]
	public void Parse_NumberLiteral_ReturnsLiteralNode()
	{
		var expr = ParseExpression("42");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.IsType<LuaNumber>(literal.Literal);
		Assert.Equal(42.0, ((LuaNumber)literal.Literal).Value);
	}

	[Fact]
	public void Parse_FloatNumber_ReturnsLiteralNode()
	{
		var expr = ParseExpression("3.14");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.Equal(3.14, ((LuaNumber)literal.Literal).Value);
	}

	[Fact]
	public void Parse_ScientificNumber_ReturnsLiteralNode()
	{
		var expr = ParseExpression("1.5e-2");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.Equal(0.015, ((LuaNumber)literal.Literal).Value);
	}

	[Fact]
	public void Parse_StringDoubleQuotes_ReturnsLiteralNode()
	{
		var expr = ParseExpression("\"hello\"");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.IsType<LuaString>(literal.Literal);
		Assert.Equal("hello", ((LuaString)literal.Literal).Value);
	}

	[Fact]
	public void Parse_StringSingleQuotes_ReturnsLiteralNode()
	{
		var expr = ParseExpression("'world'");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.Equal("world", ((LuaString)literal.Literal).Value);
	}

	[Fact]
	public void Parse_StringLongBrackets_ReturnsLiteralNode()
	{
		var expr = ParseExpression("[[raw text]]");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.Equal("raw text", ((LuaString)literal.Literal).Value);
	}

	// ── Identifiers ──────────────────────────────────────────────────

	[Fact]
	public void Parse_Identifier_ReturnsIdentifierNode()
	{
		var expr = ParseExpression("x");
		var id = Assert.IsType<IdentifierNode>(expr);
		Assert.Equal("x", id.Name);
	}

	[Fact]
	public void Parse_UnderscoreIdentifier_ReturnsIdentifierNode()
	{
		var expr = ParseExpression("_myVar1");
		var id = Assert.IsType<IdentifierNode>(expr);
		Assert.Equal("_myVar1", id.Name);
	}

	// ── Vararg ───────────────────────────────────────────────────────

	[Fact]
	public void Parse_Vararg_ReturnsVarArgumentNode()
	{
		var expr = ParseExpression("...");
		Assert.IsType<VarArgumentNode>(expr);
	}

	// ── Parenthesized expressions ────────────────────────────────────

	[Fact]
	public void Parse_ParenthesizedExpression_ReturnsInner()
	{
		var expr = ParseExpression("(42)");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.Equal(42.0, ((LuaNumber)literal.Literal).Value);
	}

	[Fact]
	public void Parse_NestedParentheses_ReturnsInner()
	{
		var expr = ParseExpression("((true))");
		var literal = Assert.IsType<LiteralNode>(expr);
		Assert.IsType<LuaBoolean>(literal.Literal);
		Assert.True(((LuaBoolean)literal.Literal).Value);
	}

	// ── Unary operators ──────────────────────────────────────────────

	[Fact]
	public void Parse_UnaryMinus_ReturnsUnaryOperatorNode()
	{
		var expr = ParseExpression("-42");
		var unary = Assert.IsType<UnaryOperatorNode>(expr);
		Assert.Equal(UnaryOperatorType.Minus, unary.Type);
		var literal = Assert.IsType<LiteralNode>(unary.Operand);
		Assert.Equal(42.0, ((LuaNumber)literal.Literal).Value);
	}

	[Fact]
	public void Parse_NotOperator_ReturnsUnaryOperatorNode()
	{
		var expr = ParseExpression("not true");
		var unary = Assert.IsType<UnaryOperatorNode>(expr);
		Assert.Equal(UnaryOperatorType.LogicalNot, unary.Type);
	}

	[Fact]
	public void Parse_LengthOperator_ReturnsUnaryOperatorNode()
	{
		var expr = ParseExpression("#\"hello\"");
		var unary = Assert.IsType<UnaryOperatorNode>(expr);
		Assert.Equal(UnaryOperatorType.LengthOf, unary.Type);
	}

	[Fact]
	public void Parse_DoubleUnaryMinus_ReturnsChained()
	{
		var expr = ParseExpression("- -5");
		var outer = Assert.IsType<UnaryOperatorNode>(expr);
		Assert.Equal(UnaryOperatorType.Minus, outer.Type);
		var inner = Assert.IsType<UnaryOperatorNode>(outer.Operand);
		Assert.Equal(UnaryOperatorType.Minus, inner.Type);
		Assert.Equal(5.0, ((LuaNumber)((LiteralNode)inner.Operand).Literal).Value);
	}

	// ── Binary operators (arithmetic) ────────────────────────────────

	[Fact]
	public void Parse_Addition_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("1 + 2");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Add, bin.Operator);
		Assert.Equal(1.0, ((LuaNumber)((LiteralNode)bin.Left).Literal).Value);
		Assert.Equal(2.0, ((LuaNumber)((LiteralNode)bin.Right).Literal).Value);
	}

	[Fact]
	public void Parse_Subtraction_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("10 - 3");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Substract, bin.Operator);
	}

	[Fact]
	public void Parse_Multiplication_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("3 * 4");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Multiply, bin.Operator);
	}

	[Fact]
	public void Parse_Division_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("10 / 2");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Divide, bin.Operator);
	}

	[Fact]
	public void Parse_IntegerDivision_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("10 // 3");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.IntegerDivide, bin.Operator);
	}

	[Fact]
	public void Parse_Modulus_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("10 % 3");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Modulus, bin.Operator);
	}

	[Fact]
	public void Parse_Exponentiation_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("2 ^ 3");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Exponentiate, bin.Operator);
	}

	// ── Operator precedence ──────────────────────────────────────────

	[Fact]
	public void Parse_MultiplicationBeforeAddition_RespectsPrecedence()
	{
		// 1 + 2 * 3  =>  1 + (2 * 3)
		var expr = ParseExpression("1 + 2 * 3");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Add, bin.Operator);
		var right = Assert.IsType<BinaryOperatorNode>(bin.Right);
		Assert.Equal(BinaryOperatorType.Multiply, right.Operator);
	}

	[Fact]
	public void Parse_ParenthesesOverridePrecedence()
	{
		// (1 + 2) * 3
		var expr = ParseExpression("(1 + 2) * 3");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Multiply, bin.Operator);
		var left = Assert.IsType<BinaryOperatorNode>(bin.Left);
		Assert.Equal(BinaryOperatorType.Add, left.Operator);
	}

	[Fact]
	public void Parse_UnaryMinusBeforeExponent_AppliesToBase()
	{
		// -2 ^ 3 => -(2 ^ 3)
		var expr = ParseExpression("-2 ^ 3");
		var unary = Assert.IsType<UnaryOperatorNode>(expr);
		Assert.Equal(UnaryOperatorType.Minus, unary.Type);
		var power = Assert.IsType<BinaryOperatorNode>(unary.Operand);
		Assert.Equal(BinaryOperatorType.Exponentiate, power.Operator);
	}

	// ── Relational operators ─────────────────────────────────────────

	[Fact]
	public void Parse_Equals_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("1 == 2");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Equals, bin.Operator);
	}

	[Fact]
	public void Parse_NotEquals_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("1 ~= 2");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.NotEquals, bin.Operator);
	}

	[Fact]
	public void Parse_LessThan_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("1 < 2");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.LessThan, bin.Operator);
	}

	[Fact]
	public void Parse_GreaterThan_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("1 > 2");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.GreaterThan, bin.Operator);
	}

	// ── Logical operators ────────────────────────────────────────────

	[Fact]
	public void Parse_LogicalAnd_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("true and false");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.LogicalAnd, bin.Operator);
	}

	[Fact]
	public void Parse_LogicalOr_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("true or false");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.LogicalOr, bin.Operator);
	}

	[Fact]
	public void Parse_AndBeforeOr_RespectsPrecedence()
	{
		// true or false and false => true or (false and false)
		var expr = ParseExpression("true or false and false");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.LogicalOr, bin.Operator);
		var right = Assert.IsType<BinaryOperatorNode>(bin.Right);
		Assert.Equal(BinaryOperatorType.LogicalAnd, right.Operator);
	}

	// ── Concatenation ────────────────────────────────────────────────

	[Fact]
	public void Parse_Concat_ReturnsBinaryOperatorNode()
	{
		var expr = ParseExpression("\"a\" .. \"b\"");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Concatenate, bin.Operator);
	}

	[Fact]
	public void Parse_ConcatRightAssociative()
	{
		// "a" .. "b" .. "c"  =>  "a" .. ("b" .. "c")
		var expr = ParseExpression("\"a\" .. \"b\" .. \"c\"");
		var bin = Assert.IsType<BinaryOperatorNode>(expr);
		Assert.Equal(BinaryOperatorType.Concatenate, bin.Operator);
		var left = Assert.IsType<LiteralNode>(bin.Left);
		Assert.Equal("a", ((LuaString)left.Literal).Value);
		var right = Assert.IsType<BinaryOperatorNode>(bin.Right);
		Assert.Equal(BinaryOperatorType.Concatenate, right.Operator);
	}
}
