using System;
using System.Collections.Generic;
using System.Text;

using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
    /// <summary>
    /// Represents a numeric <c>for</c> loop in Lua: <c>for var = start, end [, step] do block end</c>.
    /// </summary>
    public class ForNumericNode : StatementNode
    {
        /// <summary>
        /// Gets or sets the loop variable name (identifier, not an expression).
        /// </summary>
        public string Variable { get; set; } = null!;

        /// <summary>
        /// Gets or sets the initial value expression.
        /// </summary>
        public ExpressionNode Start { get; set; } = null!;

        /// <summary>
        /// Gets or sets the limit value expression.
        /// </summary>
        public ExpressionNode Limit { get; set; } = null!;

        /// <summary>
        /// Gets or sets the optional step expression.
        /// If <see langword="null"/>, the step defaults to <c>1</c>.
        /// </summary>
        public ExpressionNode? Step { get; set; }

        /// <summary>
        /// Gets or sets the loop body.
        /// </summary>
        public BlockNode Body { get; set; } = null!;
    }
}
