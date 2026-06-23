namespace AsyncLua.Parsing.Expressions
{
	/// <summary>
	/// Represents a table index access in Lua: <c>target[index]</c> or <c>target.key</c>
	/// (dot notation is syntactic sugar for <c>target["key"]</c>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// This node is also used as the left-hand side (l-value) of assignments to table elements,
	/// e.g., <c>t[key] = value</c>.
	/// </para>
	/// </remarks>
	public class IndexNode : ExpressionNode
	{
		/// <summary>
		/// Gets or sets the expression that evaluates to the table.
		/// </summary>
		public ExpressionNode Target { get; set; } = null!;

		/// <summary>
		/// Gets or sets the expression that evaluates to the index key.
		/// </summary>
		public ExpressionNode Index { get; set; } = null!;
	}
}
