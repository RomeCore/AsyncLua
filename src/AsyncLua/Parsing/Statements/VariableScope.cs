namespace AsyncLua.Parsing.Statements
{
	/// <summary>
	/// Represents a variable scope that can be either local or global.
	/// </summary>
	public enum VariableScope
	{
		/// <summary>
		/// Local scope. The variable is only accessible within the block it was declared in.
		/// </summary>
		Local,

		/// <summary>
		/// Global scope. The variable is accessible from anywhere within a <see cref="LuaState"/>.
		/// </summary>
		Global
	}
}
