namespace AsyncLua.Parsing.Expressions
{
	/// <summary>
	/// Abstract base class for all expression nodes in the Lua AST.
	/// </summary>
	/// <remarks>
	/// Expressions represent values in Lua: literals, variables, operators,
	/// function calls, table constructors, etc.
	/// </remarks>
	public abstract class ExpressionNode : ASTNode
	{
	}
}
