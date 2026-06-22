using System;
using System.Collections.Generic;
using System.Text;

namespace AsyncLua.Parsing.Expressions
{
    /// <summary>
    /// Represents a single parameter in a Lua function definition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A parameter is identified by its <see cref="Name"/> and optionally has a default
    /// value expression (future Lua 5.4+ feature, currently unused).
    /// </para>
    /// <para>
    /// For vararg parameters (<c>...</c>), use <see cref="ParameterNode.IsVarArg"/>
    /// instead of creating a <see cref="ParameterNode"/> with a <c>null</c> name.
    /// </para>
    /// </remarks>
    public class ParameterNode
    {
        /// <summary>
        /// Gets or sets the parameter name.
        /// Must not be <see langword="null"/> for regular parameters.
        /// </summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// Gets or sets a value indicating whether this is a vararg (<c>...</c>) parameter.
        /// When <see langword="true"/>, <see cref="Name"/> may be <see langword="null"/>.
        /// </summary>
        public bool IsVarArg { get; set; }
    }
}
