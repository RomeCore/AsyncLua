using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AsyncLua.Values
{
	/// <summary>
	/// Represents a Lua table — the sole data-structuring mechanism in Lua.
	/// Tables serve as associative arrays, records, namespaces, and objects.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A Lua table maps <see cref="LuaValue"/> keys to <see cref="LuaValue"/> values.
	/// Both the key and the value may be any Lua type except <c>nil</c> (a <c>nil</c> key
	/// or value is equivalent to the absence of the key).
	/// </para>
	/// <para>
	/// Tables are compared by reference identity, not by content (unless a <c>__eq</c>
	/// metamethod is provided in the metatable).
	/// </para>
	/// </remarks>
	public sealed class LuaTable : LuaValue, IEnumerable<KeyValuePair<LuaValue, LuaValue>>
	{
		private const int DefaultConcurrencyLevel = 4;

		private readonly ConcurrentDictionary<LuaValue, LuaValue> _entries;

		// Cached array-boundary length for the # operator; invalidated on mutation.
		private int? _cachedLength = 0;

		/// <summary>
		/// Initialises a new, empty Lua table with no metatable.
		/// </summary>
		public LuaTable()
		{
			_entries = new ConcurrentDictionary<LuaValue, LuaValue>(DefaultConcurrencyLevel, [], LuaValueEqualityComparer.Instance);
		}

		/// <summary>
		/// Initialises a new, empty Lua table with the specified metatable.
		/// </summary>
		/// <param name="metatable">The metatable to attach, or <see langword="null"/>.</param>
		public LuaTable(LuaMetatable? metatable)
			: this()
		{
			Metatable = metatable;
		}

		/// <summary>
		/// Initialises a new Lua table with the specified array-part capacity.
		/// </summary>
		/// <param name="capacity">The initial capacity for the underlying storage.</param>
		public LuaTable(int capacity)
		{
			_entries = new ConcurrentDictionary<LuaValue, LuaValue>(DefaultConcurrencyLevel, capacity, LuaValueEqualityComparer.Instance);
		}

		// ── Indexer ──────────────────────────────────────────────────────

		/// <summary>
		/// Gets or sets the value at the specified integer index (1-based).
		/// This is a convenience wrapper around <see cref="this[LuaValue]"/>.
		/// </summary>
		/// <param name="index">The 1-based integer index.</param>
		/// <returns>The stored value, or <see cref="LuaNil.Instance"/> if the index is absent.</returns>
		public LuaValue this[string key]
		{
			get => Get(key);
			set => Set(key, value);
		}

		/// <summary>
		/// Gets or sets the value at the specified integer index (1-based).
		/// This is a convenience wrapper around <see cref="this[LuaValue]"/>.
		/// </summary>
		/// <param name="index">The 1-based integer index.</param>
		/// <returns>The stored value, or <see cref="LuaNil.Instance"/> if the index is absent.</returns>
		public LuaValue this[int index]
		{
			get => Get(index);
			set => Set(index, value);
		}

		/// <summary>
		/// Gets or sets the value associated with the specified key.
		/// Setting a <c>nil</c> value removes the key (equivalent to <see cref="Remove"/>).
		/// </summary>
		/// <param name="key">The key. Must not be <c>nil</c>.</param>
		/// <returns>The stored value, or <see cref="LuaNil.Instance"/> if the key is absent.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is <see langword="null"/>.</exception>
		public LuaValue this[LuaValue key]
		{
			get => Get(key);
			set => Set(key, value);
		}

		// ── Getters / Setters ────────────────────────────────────────────

		/// <summary>
		/// Retrieves the value by the specified object key, or <c>default</c> if absent.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <returns>The stored value, or <see langword="default"/> if not found.</returns>
		public LuaValue Get(object key) => Get(LuaValueConverter.ToLuaValue(key));

		/// <summary>
		/// Retrieves the value by the specified key, or <c>nil</c> if absent.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <returns>The stored value, or <see cref="LuaNil.Instance"/> if not found.</returns>
		public LuaValue Get(string key) => Get((LuaString)key);

		/// <summary>
		/// Retrieves the value at the specified integer index (1-based), or <c>nil</c> if absent.
		/// </summary>
		/// <param name="index">The 1-based integer index.</param>
		/// <returns>The stored value, or <see cref="LuaNil.Instance"/> if not found.</returns>
		public LuaValue Get(int index) => Get((LuaNumber)(double)index);

		/// <summary>
		/// Retrieves the value for the specified key, or <c>nil</c> if absent.
		/// </summary>
		/// <param name="key">The key to look up. Must not be <c>nil</c>.</param>
		/// <returns>The stored value, or <see cref="LuaNil.Instance"/> if not found.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is <see langword="null"/>.</exception>
		public LuaValue Get(LuaValue key)
		{
			ValidateKey(key);

			if (_entries.TryGetValue(key, out var value))
				return value;

			return LuaNil.Instance;
		}

		/// <summary>
		/// Retrieves the value by the specified CLR object key, or <c>default</c> if absent.
		/// </summary>
		/// <param name="key">The key to look up, will be converted to <see cref="LuaValue"/> the best possible way.</param>
		/// <returns>The stored value, or <see langword="default"/> if not found.</returns>
		public T? Get<T>(object key) => LuaValueConverter.ToClrObject<T>(Get(LuaValueConverter.ToLuaValue(key)));

		/// <summary>
		/// Retrieves the value by the specified string key, or <c>default</c> if absent.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <returns>The stored value, or <see langword="default"/> if not found.</returns>
		public T? Get<T>(string key) => LuaValueConverter.ToClrObject<T>(Get((LuaString)key));

		/// <summary>
		/// Retrieves the value at the specified integer index (1-based), or <c>default</c> if absent.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <returns>The stored value, or <see langword="default"/> if not found.</returns>
		public T? Get<T>(int index) => LuaValueConverter.ToClrObject<T>(Get((LuaNumber)index));

		/// <summary>
		/// Retrieves the value for the specified key, or <c>default</c> if absent.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <returns>The stored value, or <see langword="default"/> if not found.</returns>
		public T? Get<T>(LuaValue value) => LuaValueConverter.ToClrObject<T>(Get(value));

		/// <summary>
		/// Stores a value at the specified CLR object key. Passing a <c>null</c> value removes the key.
		/// </summary>
		/// <param name="key">The CLR object key, will be converted to <see cref="LuaValue"/> the best possible way.</param>
		/// <param name="value">The value to store, or <c>null</c> to remove.</param>
		public void Set<T>(object key, LuaValue value) => Set(LuaValueConverter.ToLuaValue(key), value);

		/// <summary>
		/// Stores a value at the specified string key.
		/// </summary>
		/// <param name="key">The string key.</param>
		/// <param name="value">The value to store, or <c>nil</c> to remove.</param>
		public void Set(string key, LuaValue value) => Set((LuaString)key, value);

		/// <summary>
		/// Stores a value at the specified integer index (1-based).
		/// </summary>
		/// <param name="index">The 1-based integer index.</param>
		/// <param name="value">The value to store, or <c>nil</c> to remove.</param>
		public void Set(int index, LuaValue value) => Set((LuaNumber)(double)index, value);

		/// <summary>
		/// Stores a value under the specified key. Passing a <c>nil</c> value removes the key.
		/// </summary>
		/// <param name="key">The key. Must not be <c>nil</c>.</param>
		/// <param name="value">The value to store, or <c>nil</c> to remove the entry.</param>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is <see langword="null"/>.</exception>
		public void Set(LuaValue key, LuaValue value)
		{
			ValidateKey(key);

			if (value is LuaNil)
			{
				if (key is LuaNumber number && _cachedLength != null)
				{
					if (_cachedLength == number.Value)
						_cachedLength = (int)number.Value - 1;
					else
						_cachedLength = null;
				}
				_entries.TryRemove(key, out _);
			}
			else
			{
				if (key is LuaNumber number && _cachedLength != null)
				{
					if (_cachedLength >= number.Value + 1 && !_entries.ContainsKey((LuaNumber)(number.Value + 1)))
						_cachedLength = (int)number.Value + 1;
					else
						_cachedLength = null;
				}
				_cachedLength = null;
				_entries[key] = value;
			}
		}

		/// <summary>
		/// Stores a value at the specified CLR object key. Passing a <c>null</c> value removes the key.
		/// </summary>
		/// <param name="key">The CLR object key.</param>
		/// <param name="value">The value to store, will be converted to <see cref="LuaValue"/> the best possible way, or <c>null</c> to remove.</param>
		public void Set<T>(object key, object value) => Set(LuaValueConverter.ToLuaValue(key), LuaValueConverter.ToLuaValue(value));

		/// <summary>
		/// Stores a value at the specified string key. Passing a <c>null</c> value removes the key.
		/// </summary>
		/// <param name="key">The string key.</param>
		/// <param name="value">The value to store, will be converted to <see cref="LuaValue"/> the best possible way, or <c>null</c> to remove.</param>
		public void Set<T>(string key, object value) => Set((LuaString)key, LuaValueConverter.ToLuaValue(value));

		/// <summary>
		/// Stores a value at the specified integer index (1-based). Passing a <c>null</c> value removes the key.
		/// </summary>
		/// <param name="index">The 1-based integer index.</param>
		/// <param name="value">The value to store, will be converted to <see cref="LuaValue"/> the best possible way, or <c>null</c> to remove.</param>
		public void Set<T>(int index, object value) => Set((LuaNumber)index, LuaValueConverter.ToLuaValue(value));

		/// <summary>
		/// Stores a value under the specified key. Passing a <c>null</c> value removes the key.
		/// </summary>
		/// <param name="key">The key. Must not be <c>nil</c>.</param>
		/// <param name="value">The value to store, will be converted to <see cref="LuaValue"/> the best possible way, or <c>null</c> to remove.</param>
		public void Set<T>(LuaValue key, object value) => Set(key, LuaValueConverter.ToLuaValue(value));

		/// <summary>
		/// Appends a value to the table, as if it were an array. The new index will be one greater than the current length.
		/// </summary>
		/// <param name="value">The value to append, will be converted to <see cref="LuaValue"/> the best possible way.</param>
		public void Append(object value)
		{
			Append(LuaValueConverter.ToLuaValue(value));
		}

		/// <summary>
		/// Appends a value to the table, as if it were an array. The new index will be one greater than the current length.
		/// </summary>
		/// <param name="value">The value to append.</param>
		public void Append(LuaValue value)
		{
			var length = Length;
			_entries[(LuaNumber)(length + 1)] = value;
			if (!_entries.ContainsKey((LuaNumber)(length + 2)))
				_cachedLength = length + 1;
			else
				_cachedLength = null;
		}

		/// <summary>
		/// Removes the entry at the specified CLR object key.
		/// </summary>
		/// <param name="key">The CLR object key.</param>
		/// <returns><see langword="true"/> if the key was present; otherwise, <see langword="false"/>.</returns>
		public bool Remove(object key) => Remove(LuaValueConverter.ToLuaValue(key));

		/// <summary>
		/// Removes the entry at the specified string key.
		/// </summary>
		/// <param name="key">The string key.</param>
		/// <returns><see langword="true"/> if the key was present; otherwise, <see langword="false"/>.</returns>
		public bool Remove(string key) => Remove((LuaString)key);

		/// <summary>
		/// Removes the entry at the specified integer index.
		/// </summary>
		/// <param name="index">The 1-based integer index.</param>
		/// <returns><see langword="true"/> if the index was present; otherwise, <see langword="false"/>.</returns>
		public bool Remove(int index) => Remove((LuaNumber)index);

		/// <summary>
		/// Removes the entry for the specified key (equivalent to setting it to <c>nil</c>).
		/// </summary>
		/// <param name="key">The key to remove. Must not be <c>nil</c>.</param>
		/// <returns><see langword="true"/> if the key was present; otherwise, <see langword="false"/>.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is <see langword="null"/>.</exception>
		public bool Remove(LuaValue key)
		{
			ValidateKey(key);
			_cachedLength = null;
			return _entries.TryRemove(key, out _);
		}

		/// <summary>
		/// Determines whether the table contains the specified key.
		/// </summary>
		/// <param name="key">The key to check. Must not be <c>nil</c>.</param>
		/// <returns><see langword="true"/> if the key exists and its value is not <c>nil</c>.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is <see langword="null"/>.</exception>
		public bool ContainsKey(LuaValue key)
		{
			ValidateKey(key);
			return _entries.ContainsKey(key);
		}

		/// <summary>
		/// Attempts to retrieve the value for the specified key.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <param name="value">When this method returns, contains the stored value if found.</param>
		/// <returns><see langword="true"/> if the key was found; otherwise, <see langword="false"/>.</returns>
		public bool TryGetValue(LuaValue key, out LuaValue value)
		{
			if (key is LuaNil)
			{
				value = LuaNil.Instance;
				return false;
			}
			return _entries.TryGetValue(key, out value);
		}

		// ── Length (#) ───────────────────────────────────────────────────

		/// <summary>
		/// Gets the length of the table as defined by the Lua <c>#</c> operator.
		/// This is the highest positive integer index <c>n</c> such that
		/// <c>t[n]</c> is not <c>nil</c> and <c>t[n+1]</c> is <c>nil</c>.
		/// </summary>
		/// <remarks>
		/// If the table has "holes" (nil values before non-nil values), the result
		/// is implementation-defined by the Lua specification; this implementation
		/// uses binary search to find the boundary.
		/// </remarks>
		public int Length
		{
			get
			{
				if (_cachedLength.HasValue)
					return _cachedLength.Value;

				_cachedLength = CalculateLength();
				return _cachedLength.Value;
			}
		}

		/// <summary>
		/// Attempts to get the length, returning <see langword="false"/> if the table is empty
		/// (length is 0, which is truthy in Lua, so this provides Lua-compatible semantics).
		/// </summary>
		/// <param name="length">When this method returns, contains the table length.</param>
		/// <returns><see langword="true"/> if the length is available (always <see langword="true"/>).</returns>
		public bool TryGetLength(out int length)
		{
			length = Length;
			return true;
		}

		// ── Properties ───────────────────────────────────────────────────

		/// <summary>
		/// Gets the number of entries in the table.
		/// </summary>
		public int Count => _entries.Count;

		/// <inheritdoc />
		public override LuaType Type => LuaType.Table;

		/// <inheritdoc />
		public override string TypeName => "table";

		// ── Equality ─────────────────────────────────────────────────────

		/// <inheritdoc />
		/// <remarks>
		/// Tables use reference equality by default. If a <c>__eq</c> metamethod is required,
		/// the runtime (LuaState) dispatches it before calling this method.
		/// </remarks>
		public override bool Equals(LuaValue other)
		{
			return ReferenceEquals(this, other);
		}

		/// <inheritdoc />
		public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

		// ── Conversion ───────────────────────────────────────────────────

		/// <inheritdoc />
		/// <returns>All tables are truthy, including empty tables.</returns>
		public override bool ToBoolean() => true;

		/// <inheritdoc />
		public override string ToString()
		{
			var mt = Metatable;
			if (mt is not null)
			{
				var nameHandler = mt.Get(LuaMetatableEvent.Name);
				if (nameHandler.TryToString(out var name))
					return name;
			}

			return $"table: {RuntimeHelpers.GetHashCode(this):X}";
		}

		// ── Enumeration ──────────────────────────────────────────────────

		/// <summary>
		/// Returns an enumerator over all key-value pairs in the table.
		/// </summary>
		/// <returns>A struct enumerator.</returns>
		public IEnumerator<KeyValuePair<LuaValue, LuaValue>> GetEnumerator() => _entries.GetEnumerator();

		IEnumerator<KeyValuePair<LuaValue, LuaValue>> IEnumerable<KeyValuePair<LuaValue, LuaValue>>.GetEnumerator()
			=> _entries.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();

		/// <summary>
		/// Returns an enumerable over all keys in the table.
		/// </summary>
		public IEnumerable<LuaValue> Keys => _entries.Keys.Where(k => k != LuaNil.Instance);

		/// <summary>
		/// Returns an enumerable over all values in the table.
		/// </summary>
		public IEnumerable<LuaValue> Values => _entries.Values;

		/// <summary>
		/// Returns an enumerable over all key-value pairs in the table.
		/// </summary>
		public IEnumerable<KeyValuePair<LuaValue, LuaValue>> Entries => _entries;

		// ── Private helpers ──────────────────────────────────────────────

		private int CalculateLength()
		{
			// Binary search for the boundary between non-nil and nil.
			// Find max j such that for all i in [1, j], t[i] is not nil.

			// If t[1] is nil, length is 0.
			if (!_entries.TryGetValue(LuaNumber.One, out _))
				return 0;

			// Find an upper bound: double until we hit nil.
			int low = 1;
			int high = 2;
			while (_entries.ContainsKey((LuaNumber)(double)high))
			{
				low = high;
				high *= 2;
			}

			// Binary search between low and high.
			while (low < high)
			{
				int mid = low + (high - low + 1) / 2;
				if (_entries.ContainsKey((LuaNumber)(double)mid))
					low = mid;
				else
					high = mid - 1;
			}

			return low;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void ValidateKey(LuaValue key)
		{
			if (key is null)
				throw new ArgumentNullException(nameof(key));
			if (key is LuaNil)
				throw new ArgumentException("A Lua table key cannot be nil.", nameof(key));
		}

		// ── EqualityComparer for dictionary ──────────────────────────────

		private sealed class LuaValueEqualityComparer : IEqualityComparer<LuaValue>
		{
			public static readonly LuaValueEqualityComparer Instance = new LuaValueEqualityComparer();

			private LuaValueEqualityComparer() { }

			public bool Equals(LuaValue x, LuaValue y)
			{
				if (ReferenceEquals(x, y))
					return true;
				if (x is null || y is null)
					return false;
				return x.Equals(y);
			}

			public int GetHashCode(LuaValue obj)
			{
				return obj?.GetHashCode() ?? 0;
			}
		}
	}
}
