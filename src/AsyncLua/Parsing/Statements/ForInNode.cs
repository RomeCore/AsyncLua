using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents a generic <c>for</c> loop in Lua: <c>for vars in exps do block end</c>.
	/// </summary>
	/// <remarks>
	/// The <c>for in</c> loop iterates over values returned by an iterator function.
	/// Examples include <c>pairs(t)</c>, <c>ipairs(t)</c>, and custom iterator functions.
	/// </remarks>
	public class ForInNode : StatementNode
	{
		/// <summary>
		/// Gets or sets the loop variable names (identifiers).
		/// </summary>
		public string[] Variables { get; set; } = [];

		/// <summary>
		/// Gets or sets the iterator expression (e.g., <c>pairs(t)</c> or <c>ipairs(t)</c>).
		/// </summary>
		public ExpressionNode[] Expressions { get; set; } = [];

		/// <summary>
		/// Gets or sets the loop body.
		/// </summary>
		public BlockNode Body { get; set; } = null!;
	}
}
