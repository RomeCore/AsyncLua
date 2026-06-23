namespace AsyncLua.Parsing.Expressions
{
	/// <summary>
	/// Represents a unary operator expression in Lua:
	/// <c>-x</c> (negation), <c>not x</c> (logical not), or <c>#x</c> (length).
	/// </summary>
	public class UnaryOperatorNode : ExpressionNode
	{
		/// <summary>
		/// Gets or sets the operator kind.
		/// </summary>
		public UnaryOperatorType Type { get; set; } = UnaryOperatorType.Minus;

		/// <summary>
		/// Gets or sets the operand expression.
		/// </summary>
		public ExpressionNode Operand { get; set; } = null!;
	}
}
