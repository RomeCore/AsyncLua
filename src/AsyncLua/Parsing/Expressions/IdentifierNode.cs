namespace AsyncLua.Parsing.Expressions
{
	/// <summary>
	/// Represents a named variable reference in Lua (e.g., <c>x</c>, <c>math</c>, <c>print</c>).
	/// </summary>
	/// <remarks>
	/// An <see cref="IdentifierNode"/> may refer to a local variable, an upvalue,
	/// or a global variable, depending on the scope resolution performed during
	/// semantic analysis.
	/// </remarks>
	public class IdentifierNode : ExpressionNode
	{
		/// <summary>
		/// Gets or sets the identifier name.
		/// </summary>
		public string Name { get; set; } = null!;
	}
}
