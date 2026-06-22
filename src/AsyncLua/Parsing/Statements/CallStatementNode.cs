using System;
using System.Collections.Generic;
using System.Text;
using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
    /// <summary>
    /// Represents a function call used as a statement in Lua
    /// (e.g., <c>print("hello")</c> or <c>t:method()</c>).
    /// </summary>
    /// <remarks>
    /// In Lua, any function call can be a standalone statement. The return values
    /// of the call are discarded when used this way.
    /// </remarks>
    public class CallStatementNode : StatementNode
    {
        /// <summary>
        /// Gets or sets the function call expression.
        /// </summary>
        public FunctionCallNode Call { get; set; } = null!;
    }
}
