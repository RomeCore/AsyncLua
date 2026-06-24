namespace AsyncLua.Parsing.Expressions
{
	/// <summary>
	/// Enumerates the available unary operators in Lua.
	/// </summary>
	public enum UnaryOperatorType
	{
		/// <summary>Arithmetic negation (<c>-x</c>).</summary>
		Minus,

		/// <summary>Bitwise NOT operator (<c>~x</c>).</summary>
		BitInvert,

		/// <summary>Logical negation (<c>not x</c>).</summary>
		LogicalNot,

		/// <summary>Length operator (<c>#x</c>).</summary>
		LengthOf,
	}
}
