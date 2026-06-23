using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents a <c>repeat</c> loop in Lua: <c>repeat block until condition</c>.
	/// </summary>
	/// <remarks>
	/// In Lua, the body of a <c>repeat</c> is executed at least once before the condition is tested.
	/// </remarks>
	public class RepeatNode : StatementNode
	{
		/// <summary>
		/// Gets or sets the loop body (executed before the condition check).
		/// </summary>
		public BlockNode Body { get; set; } = null!;

		/// <summary>
		/// Gets or sets the loop condition tested after each iteration.
		/// </summary>
		public ExpressionNode Condition { get; set; } = null!;
	}
}
