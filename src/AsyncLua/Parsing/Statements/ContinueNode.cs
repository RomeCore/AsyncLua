namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents a <c>continue</c> statement in Lua.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A <c>continue</c> skips the remainder of the current loop and proceeds with the next iteration.
	/// It is a compile-time error to use <c>continue</c> outside a loop.
	/// </para>
	/// </remarks>
	public class ContinueNode : StatementNode
	{
	}
}
