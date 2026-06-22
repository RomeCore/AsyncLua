using System;
using System.Collections.Generic;
using System.Text;
using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
    /// <summary>
    /// Represents a <c>lock</c> statement (AsyncLua extension):
    /// <c>lock expr do block end</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>lock</c> statement acquires an exclusive lock on the given object
    /// using <see cref="System.Threading.Monitor"/> for the duration of the block.
    /// The lock is automatically released when the block exits (including via
    /// exceptions or <c>return</c>).
    /// </para>
    /// <para>
    /// This is an AsyncLua-specific extension and is not part of standard Lua.
    /// </para>
    /// </remarks>
    public class LockNode : StatementNode
    {
        /// <summary>
        /// Gets or sets the expression that evaluates to the object to lock on.
        /// </summary>
        public ExpressionNode Target { get; set; } = null!;

        /// <summary>
        /// Gets or sets the body executed while the lock is held.
        /// </summary>
        public BlockNode Body { get; set; } = null!;
    }
}
