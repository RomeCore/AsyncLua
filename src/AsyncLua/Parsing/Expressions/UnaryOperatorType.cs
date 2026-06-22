namespace AsyncLua.Parsing.Expressions
{
    /// <summary>
    /// Enumerates the available unary operators in Lua.
    /// </summary>
    public enum UnaryOperatorType
    {
        /// <summary>Arithmetic negation (<c>-x</c>).</summary>
        Minus,

        /// <summary>Logical negation (<c>not x</c>).</summary>
        LogicalNot,

        /// <summary>Length operator (<c>#x</c>).</summary>
        LengthOf,
    }
}
