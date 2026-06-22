using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AsyncLua.Values
{
    /// <summary>
    /// Represents an immutable, ordered sequence of Lua values — the result of a
    /// Lua function call, vararg expression (<c>...</c>), or explicit tuple expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In standard Lua, functions can return zero or more values and these multiple
    /// results are implicitly expanded or truncated depending on the context.
    /// <see cref="LuaTuple"/> provides an explicit container for such multi-value
    /// sequences within the C# runtime.
    /// </para>
    /// <para>
    /// A <see cref="LuaTuple"/> is itself a <see cref="LuaValue"/> — it can be stored
    /// in tables, passed to functions, etc. When used in a context that expects a single
    /// value, the first element is taken (matching Lua's "first result only" rule).
    /// </para>
    /// <para>
    /// An empty tuple (<see cref="Empty"/>) behaves like <c>nil</c> in single-value contexts
    /// (i.e., <c>f()</c> returning nothing used as <c>local x = f()</c> yields <c>nil</c>).
    /// </para>
    /// </remarks>
    public sealed class LuaTuple : LuaValue, IReadOnlyList<LuaValue>, IEquatable<LuaTuple>
    {
        /// <summary>
        /// The singleton empty tuple, representing zero return values.
        /// </summary>
        public static readonly LuaTuple Empty = new LuaTuple(Array.Empty<LuaValue>());

        private readonly LuaValue[] _values;

        // ── Constructors ─────────────────────────────────────────────────

        /// <summary>
        /// Initialises a new instance of the <see cref="LuaTuple"/> class.
        /// </summary>
        /// <param name="values">The values to include. Must not be <see langword="null"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="values"/> is <see langword="null"/>.
        /// </exception>
        public LuaTuple(params LuaValue[] values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="LuaTuple"/> class
        /// from an enumerable of values.
        /// </summary>
        /// <param name="values">The values to include. Must not be <see langword="null"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="values"/> is <see langword="null"/>.
        /// </exception>
        public LuaTuple(IEnumerable<LuaValue> values)
        {
            if (values is null)
                throw new ArgumentNullException(nameof(values));
            _values = values.ToArray();
        }

        // ── Factory ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates a <see cref="LuaTuple"/> from a single value.
        /// If the value is already a <see cref="LuaTuple"/>, it is returned as-is.
        /// </summary>
        /// <param name="value">The value to wrap.</param>
        /// <returns>
        /// <paramref name="value"/> if it is a <see cref="LuaTuple"/>; otherwise,
        /// a new tuple containing just that value.
        /// </returns>
        public static LuaTuple FromSingle(LuaValue value)
        {
            if (value is LuaTuple t)
                return t;
            return new LuaTuple(value);
        }

        /// <summary>
        /// Creates a <see cref="LuaTuple"/> from a single value, treating <c>nil</c>
        /// as an empty tuple (zero results).
        /// </summary>
        /// <param name="value">The value to wrap.</param>
        /// <returns>
        /// <see cref="Empty"/> if <paramref name="value"/> is <c>nil</c>;
        /// a single-element tuple otherwise.
        /// </returns>
        public static LuaTuple FromSingleOrNil(LuaValue value)
        {
            if (value is LuaNil)
                return Empty;
            return new LuaTuple(value);
        }

        // ── Accessors ────────────────────────────────────────────────────

        /// <summary>
        /// Gets the number of values in this tuple.
        /// </summary>
        public int Count => _values.Length;

        /// <summary>
        /// Gets the value at the specified zero-based index.
        /// </summary>
        /// <param name="index">The zero-based index.</param>
        /// <returns>The value at the specified position.</returns>
        /// <exception cref="IndexOutOfRangeException">
        /// Thrown if <paramref name="index"/> is out of range.
        /// </exception>
        public LuaValue this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_values.Length)
                    throw new IndexOutOfRangeException(
                        $"Index {index} is out of range. Tuple has {_values.Length} element(s).");
                return _values[index];
            }
        }

        /// <summary>
        /// Gets the first value in the tuple, or <c>nil</c> if the tuple is empty.
        /// </summary>
        public LuaValue First => _values.Length > 0 ? _values[0] : LuaNil.Instance;

        /// <summary>
        /// Gets the values as a read-only span.
        /// </summary>
        public ReadOnlySpan<LuaValue> AsSpan() => _values.AsSpan();

        // ── Deconstruct (C# pattern matching) ────────────────────────────

        /// <summary>
        /// Deconstructs the tuple into individual variables (C# 7+ tuple deconstruction).
        /// </summary>
        public void Deconstruct(out LuaValue v1)
        {
            v1 = _values.Length > 0 ? _values[0] : LuaNil.Instance;
        }

        /// <summary>
        /// Deconstructs the tuple into two variables.
        /// </summary>
        public void Deconstruct(out LuaValue v1, out LuaValue v2)
        {
            v1 = _values.Length > 0 ? _values[0] : LuaNil.Instance;
            v2 = _values.Length > 1 ? _values[1] : LuaNil.Instance;
        }

        /// <summary>
        /// Deconstructs the tuple into three variables.
        /// </summary>
        public void Deconstruct(out LuaValue v1, out LuaValue v2, out LuaValue v3)
        {
            v1 = _values.Length > 0 ? _values[0] : LuaNil.Instance;
            v2 = _values.Length > 1 ? _values[1] : LuaNil.Instance;
            v3 = _values.Length > 2 ? _values[2] : LuaNil.Instance;
        }

        /// <summary>
        /// Deconstructs the tuple into four variables.
        /// </summary>
        public void Deconstruct(out LuaValue v1, out LuaValue v2, out LuaValue v3, out LuaValue v4)
        {
            v1 = _values.Length > 0 ? _values[0] : LuaNil.Instance;
            v2 = _values.Length > 1 ? _values[1] : LuaNil.Instance;
            v3 = _values.Length > 2 ? _values[2] : LuaNil.Instance;
            v4 = _values.Length > 3 ? _values[3] : LuaNil.Instance;
        }

        // ── Equality ─────────────────────────────────────────────────────

        /// <inheritdoc />
        public override bool Equals(LuaValue other)
        {
            return other is LuaTuple t && Equals(t);
        }

        /// <inheritdoc />
        public bool Equals(LuaTuple other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            if (_values.Length != other._values.Length)
                return false;

            for (int i = 0; i < _values.Length; i++)
            {
                if (!_values[i].Equals(other._values[i]))
                    return false;
            }
            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is LuaTuple t && Equals(t);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            int hash = 17;
            for (int i = 0; i < _values.Length; i++)
                hash = hash * 31 + _values[i].GetHashCode();
            return hash;
        }

        // ── LuaValue overrides ───────────────────────────────────────────

        /// <inheritdoc />
        public override LuaType Type => LuaType.Tuple;

        /// <inheritdoc />
        public override string TypeName => "tuple";

        /// <inheritdoc />
        /// <returns><see langword="true"/> — all tuples are truthy, including empty ones.</returns>
        public override bool ToBoolean() => true;

        /// <inheritdoc />
        public override string ToString()
        {
            if (_values.Length == 0)
                return "()";
            if (_values.Length == 1)
                return $"({_values[0]})";

            var parts = new string[_values.Length];
            for (int i = 0; i < _values.Length; i++)
                parts[i] = _values[i].ToString();
            return $"({string.Join(", ", parts)})";
        }

        // ── Conversion ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the underlying array of values. This is a reference to the internal
        /// array and should be treated as read-only.
        /// </summary>
        /// <returns>The array of values.</returns>
        public LuaValue[] ToArray() => _values;

        // ── Enumeration ──────────────────────────────────────────────────

        /// <inheritdoc />
        public IEnumerator<LuaValue> GetEnumerator()
        {
            return ((IEnumerable<LuaValue>)_values).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
    }
}
