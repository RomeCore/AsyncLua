using AsyncLua.Parsing.Statements;

namespace AsyncLua.Parsing.Expressions
{
	/// <summary>
	/// Represents a function expression in Lua (anonymous function):
	/// <c>function(params) body end</c>.
	/// </summary>
	public class FunctionDeclExpressionNode : ExpressionNode
	{
		/// <summary>
		/// Gets or sets a value indicating whether this function is declared as async.
		/// </summary>
		public bool IsAsync { get; set; }

		/// <summary>
		/// Gets or sets the parameter list. Each string is a parameter name.
		/// A <see langword="null"/> entry represents a vararg (<c>...</c>) parameter.
		/// </summary>
		public ParameterNode[] Parameters { get; set; } = [];

		/// <summary>
		/// Gets or sets a value indicating whether this function has a vararg (<c>...</c>) parameter.
		/// </summary>
		public bool HasVarArg { get; set; }

		/// <summary>
		/// Gets or sets the function body.
		/// </summary>
		public BlockNode Body { get; set; } = null!;
	}
}

