namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents a <c>goto</c> statement in Lua: <c>goto label</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The target <c>label</c> must be defined within the same block or an enclosing block
	/// using a <see cref="LabelNode"/>. A <c>goto</c> cannot jump into the scope of a local variable.
	/// </para>
	/// </remarks>
	public class GotoNode : StatementNode
	{
		/// <summary>
		/// Gets or sets the name of the target label.
		/// </summary>
		public string LabelName { get; set; } = null!;
	}
}
