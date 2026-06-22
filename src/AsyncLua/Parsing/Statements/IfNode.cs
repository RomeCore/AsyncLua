using System;
using System.Collections.Generic;
using System.Text;

using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
    /// <summary>
    /// Represents an <c>if</c> statement in Lua, optionally with <c>elseif</c> and <c>else</c> branches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each <see cref="ElseIfClause"/> contains a condition expression and a body block.
    /// The optional <see cref="ElseBlock"/> is executed when none of the conditions are truthy.
    /// </para>
    /// </remarks>
    public class IfNode : StatementNode
    {
        /// <summary>
        /// Gets or sets the condition for the <c>if</c> branch.
        /// </summary>
        public ExpressionNode Condition { get; set; } = null!;

        /// <summary>
        /// Gets or sets the body executed when <see cref="Condition"/> is truthy.
        /// </summary>
        public BlockNode Body { get; set; } = null!;

        /// <summary>
        /// Gets or sets the list of <c>elseif</c> clauses.
        /// </summary>
        public ElseIfClause[] ElseIfClauses { get; set; } = [];

        /// <summary>
        /// Gets or sets the optional <c>else</c> block.
        /// Can be <see langword="null"/> if there is no <c>else</c> branch.
        /// </summary>
        public BlockNode? ElseBlock { get; set; }
    }

    /// <summary>
    /// Represents a single <c>elseif</c> clause in an <see cref="IfNode"/>.
    /// </summary>
    public class ElseIfClause
    {
        /// <summary>
        /// Gets or sets the condition for this <c>elseif</c> clause.
        /// </summary>
        public ExpressionNode Condition { get; set; } = null!;

        /// <summary>
        /// Gets or sets the body executed when the condition is truthy.
        /// </summary>
        public BlockNode Body { get; set; } = null!;
    }
}
