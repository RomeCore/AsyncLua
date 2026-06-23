using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents a <c>while</c> loop in Lua: <c>while condition do block end</c>.
	/// </summary>
	public class WhileNode : StatementNode
	{
		/// <summary>
		/// Gets or sets the loop condition.
		/// </summary>
		public ExpressionNode Condition { get; set; } = null!;

		/// <summary>
		/// Gets or sets the loop body.
		/// </summary>
		public BlockNode Body { get; set; } = null!;
	}
}
