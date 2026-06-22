namespace AsyncLua.Parsing.Expressions
{
    /// <summary>
    /// Represents a vararg expression (<c>...</c>) in Lua.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The vararg expression is only valid inside a function that has a vararg parameter.
    /// It evaluates to the extra arguments passed to the function.
    /// </para>
    /// </remarks>
    public class VarArgumentNode : ExpressionNode
    {
    }
}
