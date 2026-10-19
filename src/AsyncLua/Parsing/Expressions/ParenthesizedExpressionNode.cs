namespace AsyncLua.Parsing.Expressions
{
	/// <summary>
	/// Represents a parenthesized expression in Lua: <c>(expr)</c>.
	/// </summary>
	/// <remarks>
	/// Parentheses are semantically meaningful in Lua: they force the enclosed
	/// expression to produce exactly one value. The truncation is observable in
	/// every position that would otherwise accept multiple results - the last
	/// field of a table constructor, the last value of a return list, call
	/// arguments and the like - so the node is preserved in the AST instead of
	/// being flattened to its inner expression.
	/// </remarks>
	public class ParenthesizedExpressionNode : ExpressionNode
	{
		/// <summary>
		/// Gets or sets the expression inside the parentheses.
		/// </summary>
		public ExpressionNode Expression { get; set; } = null!;
	}
}
