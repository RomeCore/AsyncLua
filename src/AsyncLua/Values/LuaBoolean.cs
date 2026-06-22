using System;

namespace AsyncLua.Values
{
    /// <summary>
    /// Represents the Lua <c>boolean</c> type.
    /// </summary>
    /// <remarks>
    /// The two singletons <see cref="True"/> and <see cref="False"/> cover all possible boolean values.
    /// Only <see cref="False"/> is falsy in Lua boolean context (together with <c>nil</c>).
    /// </remarks>
    public sealed class LuaBoolean : LuaValue, IEquatable<LuaBoolean>
    {
        /// <summary>
        /// The Lua <c>true</c> singleton.
        /// </summary>
        public static readonly LuaBoolean True = new LuaBoolean(true);

        /// <summary>
        /// The Lua <c>false</c> singleton.
        /// </summary>
        public static readonly LuaBoolean False = new LuaBoolean(false);

        /// <summary>
        /// Gets the underlying .NET <see cref="bool"/> value.
        /// </summary>
        public bool Value { get; }

        private LuaBoolean(bool value)
        {
            Value = value;
        }

        /// <summary>
        /// Returns the singleton instance for the given .NET <see cref="bool"/> value.
        /// </summary>
        /// <param name="value">The boolean value.</param>
        /// <returns><see cref="True"/> if <paramref name="value"/> is <see langword="true"/>; otherwise, <see cref="False"/>.</returns>
        public static LuaBoolean FromBoolean(bool value) => value ? True : False;

        /// <inheritdoc />
        public override LuaType Type => LuaType.Boolean;

        /// <inheritdoc />
        public override string TypeName => "boolean";

        /// <inheritdoc />
        public override string ToString() => Value ? "true" : "false";

        /// <inheritdoc />
        public override bool Equals(LuaValue other)
        {
            return other is LuaBoolean b && b.Value == Value;
        }

        /// <inheritdoc />
        public bool Equals(LuaBoolean other)
        {
            return other is not null && other.Value == Value;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is LuaBoolean b && b.Value == Value;
        }

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();

        /// <inheritdoc />
        /// <returns><see langword="true"/> if this is <see cref="True"/>; otherwise, <see langword="false"/>.</returns>
        public override bool ToBoolean() => Value;

        /// <summary>
        /// Implicitly converts a .NET <see cref="bool"/> to a <see cref="LuaBoolean"/>.
        /// </summary>
        public static implicit operator LuaBoolean(bool value) => FromBoolean(value);
    }
}
