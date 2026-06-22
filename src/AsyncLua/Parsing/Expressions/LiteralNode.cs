using AsyncLua.Values;

namespace AsyncLua.Parsing.Expressions
{
    /// <summary>
    /// Represents a literal value in Lua: <c>nil</c>, <c>true</c>, <c>false</c>,
    /// a number, or a string.
    /// </summary>
    /// <remarks>
    /// The literal value is represented as an immutable <see cref="LuaValue"/> instance.
    /// </remarks>
    public class LiteralNode : ExpressionNode
    {
        /// <summary>
        /// Gets or sets the literal value.
        /// </summary>
        public LuaValue Literal { get; set; } = null!;
    }
}
