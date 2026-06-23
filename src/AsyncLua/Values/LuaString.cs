using System;
using System.Globalization;

namespace AsyncLua.Values
{
	/// <summary>
	/// Represents the Lua <c>string</c> type. Immutable and thread-safe.
	/// </summary>
	/// <remarks>
	/// In Lua, strings are interned by the runtime for memory efficiency. Interning is handled
	/// at the <c>LuaState</c> level, not by this value type directly.
	/// An empty string (<c>""</c>) is truthy in Lua boolean context (only <c>nil</c> and <c>false</c> are falsy).
	/// </remarks>
	public sealed class LuaString : LuaValue, IEquatable<LuaString>, IComparable<LuaString>
	{
		/// <summary>
		/// The empty Lua string singleton.
		/// </summary>
		public static readonly LuaString Empty = new LuaString(string.Empty);

		/// <summary>
		/// Gets the underlying .NET <see cref="string"/> value.
		/// </summary>
		public string Value { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="LuaString"/> class.
		/// </summary>
		/// <param name="value">The underlying .NET string. Must not be <see langword="null"/>.</param>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="value"/> is <see langword="null"/>.</exception>
		public LuaString(string value)
		{
			Value = value ?? throw new ArgumentNullException(nameof(value));
		}

		/// <summary>
		/// Gets the length of the string in bytes (raw byte count, not character count).
		/// </summary>
		/// <remarks>
		/// Lua strings can contain arbitrary binary data, so the length is the byte count
		/// of the underlying .NET string (which uses UTF-16 encoding).
		/// </remarks>
		public int Length => Value.Length;

		/// <inheritdoc />
		public override LuaType Type => LuaType.String;

		/// <inheritdoc />
		public override string TypeName => "string";

		/// <inheritdoc />
		public override string ToString() => Value;

		/// <inheritdoc />
		public override bool Equals(LuaValue other)
		{
			return other is LuaString s && Value == s.Value;
		}

		/// <inheritdoc />
		public bool Equals(LuaString other)
		{
			return other is not null && Value == other.Value;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return obj is LuaString s && Value == s.Value;
		}

		/// <inheritdoc />
		public override int GetHashCode() => Value.GetHashCode();

		/// <inheritdoc />
		/// <returns><see langword="true"/> — all strings are truthy in Lua, including the empty string.</returns>
		public override bool ToBoolean() => true;

		/// <inheritdoc />
		public override bool TryToString(out string value)
		{
			value = Value;
			return true;
		}

		/// <inheritdoc />
		/// <remarks>
		/// If the string cannot be parsed as a number, the conversion fails.
		/// Supports optional leading whitespace, sign, decimal point, and exponent notation.
		/// </remarks>
		public override bool TryToNumber(out double value)
		{
			// Lua's tonumber is more lenient than .NET — it supports leading/trailing whitespace
			var trimmed = Value.Trim();
			if (string.IsNullOrEmpty(trimmed))
			{
				value = default;
				return false;
			}

			// Handle hex (Lua 5.2+)
			if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
				trimmed.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
			{
				if (long.TryParse(trimmed.Substring(2),
						NumberStyles.HexNumber,
						CultureInfo.InvariantCulture,
						out var hexValue))
				{
					value = hexValue;
					return true;
				}
				value = default;
				return false;
			}

			return double.TryParse(trimmed,
				NumberStyles.Float | NumberStyles.AllowThousands,
				CultureInfo.InvariantCulture,
				out value);
		}

		/// <inheritdoc />
		public int CompareTo(LuaString other)
		{
			if (other is null)
				return 1;
			return string.CompareOrdinal(Value, other.Value);
		}

		/// <summary>
		/// Implicitly converts a .NET <see cref="string"/> to a <see cref="LuaString"/>.
		/// </summary>
		/// <param name="value">The .NET string. If <see langword="null"/>, <see cref="Empty"/> is returned.</param>
		public static implicit operator LuaString(string value) =>
			string.IsNullOrEmpty(value) ? Empty : new LuaString(value);
	}
}
