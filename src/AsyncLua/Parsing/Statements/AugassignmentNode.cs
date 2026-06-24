using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
	public class AugassignmentNode : StatementNode
	{
		public BinaryOperatorType Operator { get; set; } = default;

		public ExpressionNode Left { get; set; } = null!;

		public ExpressionNode Right { get; set; } = null!;
	}
}
