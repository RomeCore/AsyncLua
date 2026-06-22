using System;
using System.Collections.Generic;
using System.Text;
using AsyncLua.Parsing.Expressions;

namespace AsyncLua.Parsing.Statements
{
    /// <summary>
    /// Represents a function declaration statement in Lua:
    /// <c>function name(params) body end</c> or <c>local function name(params) body end</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Scope"/> property distinguishes between a local function declaration
    /// (<c>local function</c>) and a global one (<c>function</c>).
    /// </para>
    /// <para>
    /// For method-style declarations like <c>function t:method(...) end</c>,
    /// the <see cref="TargetObject"/> and <see cref="MethodName"/> properties are used.
    /// </para>
    /// </remarks>
    public class FunctionDeclStatementNode : StatementNode
    {
        /// <summary>
        /// Gets or sets the function name (simple identifier).
        /// For method declarations, this is the method name.
        /// </summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// Gets or sets the target object expression for method declarations
        /// (e.g., <c>t</c> in <c>function t:method(...) end</c>).
        /// <see langword="null"/> for regular function declarations.
        /// </summary>
        public ExpressionNode? TargetObject { get; set; }

        /// <summary>
        /// Gets or sets the method name used with the colon syntax.
        /// <see langword="null"/> for regular function declarations.
        /// </summary>
        public string? MethodName { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether this function is declared as async.
		/// </summary>
		public bool IsAsync { get; set; }

		/// <summary>
		/// Gets or sets the scope of the function declaration.
		/// <c>Local</c> for <c>local function</c>, <c>Global</c> or <see langword="null"/> for default.
		/// </summary>
		public VariableScope? Scope { get; set; }

        /// <summary>
        /// Gets or sets the parameter list.
        /// </summary>
        public ParameterNode[] Parameters { get; set; } = [];

        /// <summary>
        /// Gets or sets a value indicating whether this function has a vararg (<c>...</c>) parameter.
        /// </summary>
        public bool HasVarArg { get; set; }

        /// <summary>
        /// Gets or sets the function body (block of statements).
        /// </summary>
        public BlockNode Body { get; set; } = null!;
    }
}

