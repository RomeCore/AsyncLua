using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents an <c>await</c> statement (AsyncLua extension):
	/// <c>await expr</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The <c>await</c> statement suspends execution of the current Lua function
	/// until the <see cref="AsyncLua.Values.LuaTask"/> returned by the expression
	/// completes. This is only valid inside an <c>async</c> function context.
	/// </para>
	/// <para>
	/// This is an AsyncLua-specific extension and is not part of standard Lua.
	/// </para>
	/// </remarks>
	public class AwaitStatementNode : StatementNode
	{
		/// <summary>
		/// Gets or sets the <c>await</c> expression.
		/// </summary>
		public AwaitExpressionNode AwaitExpression { get; set; } = null!;
	}
}
