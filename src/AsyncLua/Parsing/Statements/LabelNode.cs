namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents a label in Lua: <c>::label::</c>.
	/// </summary>
	/// <remarks>
	/// Labels are targets for <see cref="GotoNode"/> statements.
	/// They must be unique within their enclosing block.
	/// </remarks>
	public class LabelNode : StatementNode
	{
		/// <summary>
		/// Gets or sets the label name.
		/// </summary>
		public string Name { get; set; } = null!;
	}
}
