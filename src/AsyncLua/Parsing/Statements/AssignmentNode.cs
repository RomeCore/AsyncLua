using System;
using System.Collections.Generic;
using System.Text;
using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
    /// <summary>
    /// Represents an assignment statement in Lua, supporting both simple variables
    /// and indexed elements (e.g., <c>a = 1</c>, <c>t.key = 2</c>, <c>a, b[key] = 1, 2</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each element in <see cref="Targets"/> is either:
    /// <list type="bullet">
    ///   <item><description>An <see cref="IdentifierNode"/> for a simple variable assignment.</description></item>
    ///   <item><description>An <see cref="IndexNode"/> for a table-element assignment (<c>t[key]</c> or <c>t.key</c>).</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The <see cref="Scope"/> property determines whether the variable is declared as <c>local</c>
    /// or assigned to the global environment (default). When <see cref="Scope"/> is <see langword="null"/>,
    /// the assignment targets an existing variable (reassignment).
    /// </para>
    /// </remarks>
    public class AssignmentNode : StatementNode
    {
        /// <summary>
        /// Gets or sets the assignment scope.
        /// <see langword="null"/> means a plain reassignment (no <c>local</c> keyword).
        /// </summary>
        public VariableScope? Scope { get; set; }

        /// <summary>
        /// Gets or sets the target expressions (l-values) of the assignment.
        /// Each target must be an <see cref="IdentifierNode"/> or <see cref="IndexNode"/>.
        /// </summary>
        public ExpressionNode[] Targets { get; set; } = [];

        /// <summary>
        /// Gets or sets the source expressions (r-values) whose values are assigned to the targets.
        /// </summary>
        public ExpressionNode[] Values { get; set; } = [];
    }
}
