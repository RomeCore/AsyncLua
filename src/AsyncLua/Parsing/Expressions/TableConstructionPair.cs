namespace AsyncLua.Parsing.Expressions
{
	/// <summary>
	/// Represents a single key-value pair inside a <see cref="TableConstructionNode"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// If <see cref="Key"/> is <see langword="null"/>, the value is treated as an
	/// array element and automatically assigned an integer index (the current
	/// array length + 1).
	/// </para>
	/// </remarks>
	public class TableConstructionPair
	{
		/// <summary>
		/// Gets or sets the key expression.
		/// <see langword="null"/> for array-style elements (e.g., <c>{ 1, 2, 3 }</c>).
		/// </summary>
		public ExpressionNode? Key { get; set; }

		/// <summary>
		/// Gets or sets the value expression.
		/// </summary>
		public ExpressionNode Value { get; set; } = null!;
	}
}
