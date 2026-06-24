using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
	public class ThrowNode : StatementNode
	{
		/// <summary>
		/// Gets or sets the expression containing string to throw.
		/// </summary>
		public ExpressionNode Exception { get; set; } = null!;
	}
}
