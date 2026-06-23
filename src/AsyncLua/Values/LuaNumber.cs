using System;
using System.Globalization;

namespace AsyncLua.Values
{
	/// <summary>
	/// Represents the Lua <c>number</c> type as an IEEE 754 double-precision floating point value.
	/// </summary>
	/// <remarks>
	/// In Lua 5.3+, numbers have two internal representations: integer (64-bit signed) and float (double).
	/// This class currently uses a <see cref="double"/> for simplicity; integer subtype support may be added later.
	/// All numbers except <c>NaN</c> are truthy in Lua boolean context.
	/// </remarks>
	public sealed class LuaNumber : LuaValue, IEquatable<LuaNumber>, IComparable<LuaNumber>
	{
		/// <summary>
		/// The Lua number representing zero (0.0).
		/// </summary>
		public static readonly LuaNumber Zero = new LuaNumber(0.0);

		/// <summary>
		/// The Lua number representing one (1.0).
		/// </summary>
		public static readonly LuaNumber One = new LuaNumber(1.0);

		/// <summary>
		/// Gets the underlying .NET <see cref="double"/> value.
		/// </summary>
		public double Value { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="LuaNumber"/> class with the specified value.
		/// </summary>
		/// <param name="value">The double-precision floating point value.</param>
		public LuaNumber(double value)
		{
			Value = value;
		}

		/// <inheritdoc />
		public override LuaType Type => LuaType.Number;

		/// <inheritdoc />
		public override string TypeName => "number";

		/// <inheritdoc />
		public override string ToString()
		{
			// Lua prints numbers in a specific format:
			// integers without decimal point, NaN as "nan", Infinity as "inf"
			if (double.IsNaN(Value))
				return "nan";
			if (double.IsPositiveInfinity(Value))
				return "inf";
			if (double.IsNegativeInfinity(Value))
				return "-inf";

			// If the value is an exact integer, format without decimal point
			if (Value == Math.Floor(Value))
				return ((long)Value).ToString(CultureInfo.InvariantCulture);

			return Value.ToString("G", CultureInfo.InvariantCulture);
		}

		/// <inheritdoc />
		public override bool Equals(LuaValue other)
		{
			return other is LuaNumber n && Equals(n);
		}

		/// <inheritdoc />
		public bool Equals(LuaNumber other)
		{
			return other is not null && Value.Equals(other.Value);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return obj is LuaNumber n && Value.Equals(n.Value);
		}

		/// <inheritdoc />
		public override int GetHashCode() => Value.GetHashCode();

		/// <inheritdoc />
		/// <returns><see langword="false"/> if this value is <c>NaN</c>; otherwise, <see langword="true"/>.</returns>
		public override bool ToBoolean() => !double.IsNaN(Value);

		/// <inheritdoc />
		public override bool TryToNumber(out double value)
		{
			value = Value;
			return true;
		}

		/// <inheritdoc />
		public int CompareTo(LuaNumber other)
		{
			if (other is null)
				return 1;
			return Value.CompareTo(other.Value);
		}

		/// <summary>
		/// Implicitly converts a .NET <see cref="double"/> to a <see cref="LuaNumber"/>.
		/// </summary>
		public static implicit operator LuaNumber(double value) => new LuaNumber(value);

		/// <summary>
		/// Explicitly converts a <see cref="LuaNumber"/> to a .NET <see cref="double"/>.
		/// </summary>
		public static explicit operator double(LuaNumber number)
		{
			if (number is null)
				throw new ArgumentNullException(nameof(number));
			return number.Value;
		}

		/// <summary>
		/// Implicitly converts a .NET <see cref="int"/> to a <see cref="LuaNumber"/>.
		/// </summary>
		public static implicit operator LuaNumber(int value) => new LuaNumber(value);

		/// <summary>
		/// Implicitly converts a .NET <see cref="long"/> to a <see cref="LuaNumber"/>.
		/// </summary>
		public static implicit operator LuaNumber(long value) => new LuaNumber(value);
	}
}
