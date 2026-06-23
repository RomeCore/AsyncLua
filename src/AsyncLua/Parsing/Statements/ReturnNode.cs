using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents a <c>return</c> statement in Lua.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A <c>return</c> statement can return zero or more values.
	/// If <see cref="Values"/> is empty, the function returns no values (<c>return</c> or <c>return;</c>).
	/// </para>
	/// <para>
	/// In Lua, <c>return</c> must be the last statement in a block (or followed by an <c>end</c>).
	/// The compiler should enforce this constraint.
	/// </para>
	/// </remarks>
	public class ReturnNode : StatementNode
	{
		/// <summary>
		/// Gets or sets the expressions whose values are returned.
		/// An empty array means no return values.
		/// </summary>
		public ExpressionNode[] Values { get; set; } = [];
	}
}
