namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents a block of statements in Lua.
	/// </summary>
	public class BlockNode
	{
		/// <summary>
		/// Gets or sets the array of statements within this block.
		/// </summary>
		public StatementNode[] Statements { get; set; } = [];
	}
}
