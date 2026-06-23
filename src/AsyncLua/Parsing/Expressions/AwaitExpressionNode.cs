using System;
using System.Collections.Generic;
using System.Text;

namespace AsyncLua.Parsing.Expressions
{
    /// <summary>
    /// Represents an <c>await</c> expression (AsyncLua extension):
    /// <c>await task</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>await</c> expression suspends execution of the current Lua function
    /// until the <see cref="AsyncLua.Values.LuaTask"/> returned by <see cref="Expression"/>
    /// completes. The result(s) of the task become the value of this expression.
    /// </para>
    /// <para>
    /// This is an AsyncLua-specific extension and is not part of standard Lua.
    /// </para>
    /// <para>
    /// <c>await</c> can be used both as a statement (via <c>AwaitStatementNode</c>)
    /// and as an expression, e.g. <c>local x = await getData()</c>.
    /// </para>
    /// </remarks>
    public class AwaitExpressionNode : ExpressionNode
    {
        /// <summary>
        /// Gets or sets the expression that must evaluate to a <see cref="AsyncLua.Values.LuaTask"/>.
        /// </summary>
        public ExpressionNode[] Expressions { get; set; } = [];
    }
}
