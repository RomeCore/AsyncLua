using System;
using System.Collections.Generic;
using System.Text;

namespace AsyncLua.Parsing.Expressions
{
    /// <summary>
    /// Represents a table constructor expression in Lua: <c>{ [key] = value, ... }</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each element in <see cref="Pairs"/> is a key-value mapping.
    /// If a pair has a <see langword="null"/> key, the value is appended to
    /// the array part of the table with an automatically assigned integer index.
    /// </para>
    /// </remarks>
    public class TableConstructionNode : ExpressionNode
    {
        /// <summary>
        /// Gets or sets the list of key-value pairs in the constructor.
        /// </summary>
        public TableConstructionPair[] Pairs { get; set; } = [];
    }
}
