using System;
using System.Globalization;

namespace AsyncLua.Values
{
    /// <summary>
    /// Represents an immutable Lua value. This is the abstract base class for all Lua types.
    /// All derived types must be thread-safe (immutable) to support concurrent execution scenarios.
    /// </summary>
    public abstract class LuaValue : IDisposable, IEquatable<LuaValue>
    {
        /// <summary>
        /// Gets the type identifier for this Lua value.
        /// </summary>
        public abstract LuaType Type { get; }

        /// <summary>
        /// Gets the Lua type name as returned by the <c>type()</c> function (e.g., "nil", "boolean", "number", "string").
        /// </summary>
        public abstract string TypeName { get; }

        /// <summary>
        /// Gets or sets the metatable associated with this value.
        /// </summary>
        /// <remarks>
        /// <para>
        /// In standard Lua, only tables, userdata, and threads can have individual metatables.
        /// Other types (<c>nil</c>, <c>boolean</c>, <c>number</c>, <c>string</c>, <c>function</c>)
        /// return <see langword="null"/> by default. Per-type shared metatables can be managed
        /// at the <c>LuaState</c> level via the debug library.
        /// </para>
        /// <para>
        /// Setting the metatable to <see langword="null"/> removes it.
        /// </para>
        /// </remarks>
        public virtual LuaMetatable? Metatable
        {
            get => null;
            set { }
        }

        /// <summary>
        /// Returns a Lua-compatible string representation of this value.
        /// </summary>
        /// <returns>A string suitable for debugging and <c>tostring()</c>-like output.</returns>
        public override abstract string ToString();

        /// <summary>
        /// Determines whether this Lua value is equal to another Lua value according to Lua equality rules.
        /// Types that support metamethod equality (<see cref="LuaType.Table"/>, <see cref="LuaType.UserData"/>)
        /// should delegate to the runtime for <c>__eq</c> handling.
        /// </summary>
        /// <param name="other">The other Lua value to compare with.</param>
        /// <returns><see langword="true"/> if the values are equal; otherwise, <see langword="false"/>.</returns>
        public abstract bool Equals(LuaValue other);

        /// <summary>
        /// Determines whether this Lua value is equal to the specified object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="LuaValue"/> and equal to this instance.</returns>
        public override bool Equals(object obj)
        {
            return obj is LuaValue other && Equals(other);
        }

        /// <summary>
        /// Returns a hash code for this Lua value.
        /// </summary>
        /// <returns>A hash code based on the value's identity and content.</returns>
        public override abstract int GetHashCode();

        /// <summary>
        /// Converts this Lua value to its .NET boolean representation.
        /// In Lua, <c>false</c> and <c>nil</c> are falsy; everything else is truthy.
        /// </summary>
        /// <returns><see langword="true"/> if this value is truthy; otherwise, <see langword="false"/>.</returns>
        public virtual bool ToBoolean()
        {
            return true;
        }

        /// <summary>
        /// Attempts to convert this Lua value to a .NET <see cref="double"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the converted value if successful.</param>
        /// <returns><see langword="true"/> if conversion succeeded; otherwise, <see langword="false"/>.</returns>
        public virtual bool TryToNumber(out double value)
        {
            value = default;
            return false;
        }

        /// <summary>
        /// Attempts to convert this Lua value to a .NET <see cref="string"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the converted value if successful.</param>
        /// <returns><see langword="true"/> if conversion succeeded; otherwise, <see langword="false"/>.</returns>
        public virtual bool TryToString(out string value)
        {
            value = ToString();
            return true;
        }

        ~LuaValue()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private bool disposed = false;
		protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            disposed = true;

			// TODO: Add __gc metamethod handling here
		}
	}
}
