namespace AsyncLua.Parsing.Expressions
{
	/// <summary>
	/// Represents a binary operator expression in Lua
	/// (e.g., <c>a + b</c>, <c>x and y</c>, <c>s1 .. s2</c>).
	/// </summary>
	public class BinaryOperatorNode : ExpressionNode
	{
		/// <summary>
		/// Gets or sets the operator kind.
		/// </summary>
		public BinaryOperatorType Operator { get; set; } = BinaryOperatorType.Add;

		/// <summary>
		/// Gets or sets the left-hand side operand.
		/// </summary>
		public ExpressionNode Left { get; set; } = null!;

		/// <summary>
		/// Gets or sets the right-hand side operand.
		/// </summary>
		public ExpressionNode Right { get; set; } = null!;
	}
}
