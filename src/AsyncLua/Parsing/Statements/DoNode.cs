namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents a <c>do</c> block in Lua: <c>do block end</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A <c>do</c> block creates a new scope for local variables.
	/// It is useful for limiting the visibility of variables and for creating
	/// blocks with <c>goto</c> labels.
	/// </para>
	/// </remarks>
	public class DoNode : StatementNode
	{
		/// <summary>
		/// Gets or sets the body of the <c>do</c> block.
		/// </summary>
		public BlockNode Body { get; set; } = null!;
	}
}
