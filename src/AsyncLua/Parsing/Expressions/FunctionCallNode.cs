using System;
using System.Collections.Generic;
using System.Text;

namespace AsyncLua.Parsing.Expressions
{
    /// <summary>
    /// Represents a function call expression in Lua:
    /// <c>f(args)</c>, <c>f.method(args)</c>, or <c>f:method(args)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="Method"/> is not <see langword="null"/>, the call uses
    /// the colon syntax (<c>obj:method(args)</c>). The <c>self</c> parameter
    /// is implicitly passed as the first argument.
    /// </para>
    /// <para>
    /// When <see cref="Method"/> is <see langword="null"/>, the call is either
    /// a regular function call or a dot-notation call (<c>obj.method(args)</c>),
    /// which is syntactic sugar for <c>obj["method"](args)</c>.
    /// </para>
    /// </remarks>
    public class FunctionCallNode : ExpressionNode
    {
        /// <summary>
        /// Gets or sets the method name used with the colon operator (<c>:</c>).
        /// <see langword="null"/> for regular calls and dot-notation calls.
        /// </summary>
        public string? Method { get; set; }

        /// <summary>
        /// Gets or sets the expression that evaluates to the callable value.
        /// For method calls this is the object before the colon.
        /// </summary>
        public ExpressionNode Target { get; set; } = null!;

        /// <summary>
        /// Gets or sets the argument expressions.
        /// </summary>
        public ExpressionNode[] Arguments { get; set; } = [];
    }
}
