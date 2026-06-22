using System;
using System.Collections.Generic;
using System.Text;

namespace AsyncLua.Parsing.Statements
{
    /// <summary>
    /// Represents a <c>break</c> statement in Lua.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>break</c> terminates the innermost enclosing loop (<c>while</c>, <c>repeat</c>, or <c>for</c>).
    /// It is a compile-time error to use <c>break</c> outside a loop.
    /// </para>
    /// </remarks>
    public class BreakNode : StatementNode
    {
    }
}
