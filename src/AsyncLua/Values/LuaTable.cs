using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

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
	/// The storage is split like the reference implementation: a dense <em>array part</em> for
	/// the continuous run of integer keys starting at 1, and an <em>hash part</em> for everything
	/// else. Unlike the reference implementation, the hash part keeps its <strong>insertion
	/// order</strong>: <see cref="Entries"/>, <see cref="Keys"/>, <see cref="Values"/> and the Lua
	/// <c>pairs</c>/<c>next</c> iterate the array part as 1..n and the hash part in the order its
	/// keys were first set. That order is stable across processes — no hash code participates in it.
	/// </para>
	/// <para>
	/// Tables are compared by reference identity, not by content (unless a <c>__eq</c>
	/// metamethod is provided in the metatable).
	/// </para>
	/// </remarks>
	public sealed class LuaTable : LuaValue, IEnumerable<KeyValuePair<LuaValue, LuaValue>>
	{
		/// <summary>
		/// One slot of the hash part. A removed key leaves a dead slot (a tombstone) in place:
		/// the key keeps its position until the next compaction, which is what lets a traversal
		/// survive <c>t[k] = nil</c> and keeps <c>next</c> O(1).
		/// </summary>
		private sealed class HashEntry
		{
			public LuaValue Key = LuaNil.Instance;
			public LuaValue Value = LuaNil.Instance;
			public bool Alive;
		}

#if NET9_0_OR_GREATER
		private readonly Lock _gate = new();
#else
		private readonly object _gate = new();
#endif

		// Array part: the slot at 0-based index i holds the value of the Lua index i + 1.
		// Holes are LuaNil.Instance. The physical length never shrinks (except Clear), so an
		// index stays addressable while a traversal is in flight.
		private readonly List<LuaValue> _array;

		// Hash part: insertion order. Dead entries stay in place until a compaction.
		private readonly List<HashEntry> _hash;
		private readonly Dictionary<LuaValue, int> _positions;

		// The # operator: 1.._densePrefix are all non-nil.
		private int _densePrefix;

		private int _liveArrayCount; // non-nil slots in the array part
		private int _liveHashCount;  // live entries in the hash part
		private int _deadHashCount;  // tombstones waiting for a compaction

		private const int MinTombstonesToCompact = 8;

		/// <summary>
		/// Initialises a new, empty Lua table with no metatable.
		/// </summary>
		public LuaTable()
		{
			_array = [];
			_hash = [];
			_positions = new Dictionary<LuaValue, int>(LuaValueEqualityComparer.Instance);
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
			: this()
		{
			if (capacity > 0)
			{
				_array.Capacity = capacity;
				_hash.Capacity = capacity;
			}
		}

		// ── Indexer ──────────────────────────────────────────────────────

		/// <summary>
		/// Gets or sets the value at the specified CLR object key.
		/// This is a convenience wrapper around <see cref="this[LuaValue]"/>.
		/// </summary>
		/// <param name="key">The CLR object key.</param>
		/// <returns>The stored value, or <see cref="LuaNil.Instance"/> if the index is absent.</returns>
		public LuaValue this[object key]
		{
			get => Get(key);
			set => Set(key, value);
		}

		/// <summary>
		/// Gets or sets the value at the specified string key.
		/// This is a convenience wrapper around <see cref="this[LuaValue]"/>.
		/// </summary>
		/// <param name="key">The string key.</param>
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
		/// Setting a <c>nil</c> value removes the key (equivalent to <see cref="Remove(LuaValue)"/>).
		/// </summary>
		/// <param name="key">The key. Must not be <c>nil</c>.</param>
		/// <returns>The stored value, or <see cref="LuaNil.Instance"/> if the key is absent.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is <see langword="null"/>.</exception>
		public LuaValue this[LuaValue key]
		{
			get => Get(key);
			set => Set(key, value);
		}

		// ── Getters ──────────────────────────────────────────────────────

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

			lock (_gate)
			{
				// An integer index beyond the array part is an ordinary hash key (it never grew
				// the array), so both halves have to be consulted.
				if (TryGetArrayIndex(key, out int index) && index <= _array.Count)
					return _array[index - 1];

				return _positions.TryGetValue(key, out int position) && _hash[position].Alive
					? _hash[position].Value
					: LuaNil.Instance;
			}
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
		/// <param name="index">The 1-based integer index.</param>
		/// <returns>The stored value, or <see langword="default"/> if not found.</returns>
		public T? Get<T>(int index) => LuaValueConverter.ToClrObject<T>(Get((LuaNumber)index));

		/// <summary>
		/// Retrieves the value for the specified key, or <c>default</c> if absent.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <returns>The stored value, or <see langword="default"/> if not found.</returns>
		public T? Get<T>(LuaValue key) => LuaValueConverter.ToClrObject<T>(Get(key));

		// ── Setters ──────────────────────────────────────────────────────

		/// <summary>
		/// Stores a value at the specified CLR object key. Passing a <c>null</c> value removes the key.
		/// </summary>
		/// <param name="key">The CLR object key, will be converted to <see cref="LuaValue"/> the best possible way.</param>
		/// <param name="value">The value to store, or <c>null</c> to remove.</param>
		public void Set(object key, LuaValue value) => Set(LuaValueConverter.ToLuaValue(key), value);

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

			lock (_gate)
			{
				if (TryGetArrayIndex(key, out int index))
					SetArray(key, index, value);
				else
					SetHash(key, value);
			}
		}

		/// <summary>
		/// Stores a value at the specified CLR object key. Passing a <c>null</c> value removes the key.
		/// </summary>
		/// <param name="key">The CLR object key.</param>
		/// <param name="value">The value to store, will be converted to <see cref="LuaValue"/> the best possible way, or <c>null</c> to remove.</param>
		public void Set(object key, object? value) => Set(LuaValueConverter.ToLuaValue(key), LuaValueConverter.ToLuaValue(value));

		/// <summary>
		/// Stores a value at the specified string key. Passing a <c>null</c> value removes the key.
		/// </summary>
		/// <param name="key">The string key.</param>
		/// <param name="value">The value to store, will be converted to <see cref="LuaValue"/> the best possible way, or <c>null</c> to remove.</param>
		public void Set(string key, object? value) => Set((LuaString)key, LuaValueConverter.ToLuaValue(value));

		/// <summary>
		/// Stores a value at the specified integer index (1-based). Passing a <c>null</c> value removes the key.
		/// </summary>
		/// <param name="index">The 1-based integer index.</param>
		/// <param name="value">The value to store, will be converted to <see cref="LuaValue"/> the best possible way, or <c>null</c> to remove.</param>
		public void Set(int index, object? value) => Set((LuaNumber)index, LuaValueConverter.ToLuaValue(value));

		/// <summary>
		/// Stores a value under the specified key. Passing a <c>null</c> value removes the key.
		/// </summary>
		/// <param name="key">The key. Must not be <c>nil</c>.</param>
		/// <param name="value">The value to store, will be converted to <see cref="LuaValue"/> the best possible way, or <c>null</c> to remove.</param>
		public void Set(LuaValue key, object? value) => Set(key, LuaValueConverter.ToLuaValue(value));

		/// <summary>
		/// Appends a value to the table, as if it were an array. The new index will be one greater than the current length.
		/// </summary>
		/// <param name="value">The value to append, will be converted to <see cref="LuaValue"/> the best possible way.</param>
		public void Append(object? value)
		{
			Append(LuaValueConverter.ToLuaValue(value));
		}

		/// <summary>
		/// Appends a value to the table, as if it were an array. The new index will be one greater than the current length.
		/// </summary>
		/// <param name="value">The value to append.</param>
		public void Append(LuaValue value)
		{
			lock (_gate)
			{
				// #t + 1 is either a hole inside the array part or the next free slot.
				int index = _densePrefix + 1;
				SetArray(new LuaNumber(index), index, value);
			}
		}

		// ── Removal / lookup ─────────────────────────────────────────────

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

			lock (_gate)
			{
				if (TryGetArrayIndex(key, out int index) && index <= _array.Count)
				{
					if (_array[index - 1] is LuaNil)
						return false;

					SetArray(key, index, LuaNil.Instance);
					return true;
				}

				if (!_positions.TryGetValue(key, out int position) || !_hash[position].Alive)
					return false;

				SetHash(key, LuaNil.Instance);
				return true;
			}
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

			lock (_gate)
			{
				if (TryGetArrayIndex(key, out int index) && index <= _array.Count)
					return _array[index - 1] is not LuaNil;

				return _positions.TryGetValue(key, out int position) && _hash[position].Alive;
			}
		}

		/// <summary>
		/// Attempts to retrieve the value for the specified key.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <param name="value">When this method returns, contains the stored value if found.</param>
		/// <returns><see langword="true"/> if the key was found; otherwise, <see langword="false"/>.</returns>
		public bool TryGetValue(LuaValue key, out LuaValue value)
		{
			if (key is null || key is LuaNil)
			{
				value = LuaNil.Instance;
				return false;
			}

			lock (_gate)
			{
				if (TryGetArrayIndex(key, out int index) && index <= _array.Count)
				{
					if (_array[index - 1] is not LuaNil)
					{
						value = _array[index - 1];
						return true;
					}

					value = LuaNil.Instance;
					return false;
				}

				if (_positions.TryGetValue(key, out int position) && _hash[position].Alive)
				{
					value = _hash[position].Value;
					return true;
				}

				value = LuaNil.Instance;
				return false;
			}
		}

		/// <summary>
		/// Clears all entries from the table.
		/// </summary>
		public void Clear()
		{
			lock (_gate)
			{
				_array.Clear();
				_hash.Clear();
				_positions.Clear();
				_densePrefix = 0;
				_liveArrayCount = 0;
				_liveHashCount = 0;
				_deadHashCount = 0;
			}
		}

		// ── Length (#) ───────────────────────────────────────────────────

		/// <summary>
		/// Gets the length of the table as defined by the Lua <c>#</c> operator: the length of the
		/// dense prefix, i.e. the largest <c>n</c> such that <c>t[1]..t[n]</c> are all non-nil.
		/// </summary>
		/// <remarks>
		/// Lua allows the length operator to return any border; this implementation always returns
		/// the dense prefix. For a table with holes that means <c>{1, nil, 3}</c> has length 1 —
		/// the same place where <c>ipairs</c> stops, and the place where <c>t[#t + 1]</c> writes.
		/// </remarks>
		public int Length
		{
			get
			{
				lock (_gate)
					return _densePrefix;
			}
		}

		// ── Properties ───────────────────────────────────────────────────

		/// <summary>
		/// Gets the number of live entries in the table (array slots with a non-nil value plus live
		/// hash entries). Tombstones and holes are not counted.
		/// </summary>
		public int Count
		{
			get
			{
				lock (_gate)
					return _liveArrayCount + _liveHashCount;
			}
		}

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
		/// Returns a snapshot of all live key-value pairs: the array part as 1..n, then the hash part
		/// in insertion order. The snapshot does not change when the table is mutated afterwards.
		/// </summary>
		public IReadOnlyList<KeyValuePair<LuaValue, LuaValue>> Entries
		{
			get
			{
				lock (_gate)
				{
					var result = new KeyValuePair<LuaValue, LuaValue>[_liveArrayCount + _liveHashCount];
					int index = 0;

					for (int i = 0; i < _array.Count; i++)
					{
						if (_array[i] is LuaNil)
							continue;
						result[index++] = new KeyValuePair<LuaValue, LuaValue>(new LuaNumber(i + 1), _array[i]);
					}

					for (int i = 0; i < _hash.Count; i++)
					{
						var entry = _hash[i];
						if (!entry.Alive)
							continue;
						result[index++] = new KeyValuePair<LuaValue, LuaValue>(entry.Key, entry.Value);
					}

					return result;
				}
			}
		}

		/// <summary>
		/// Returns a snapshot of all live keys: the array part as 1..n, then the hash part in
		/// insertion order.
		/// </summary>
		public IReadOnlyList<LuaValue> Keys
		{
			get
			{
				lock (_gate)
				{
					var result = new LuaValue[_liveArrayCount + _liveHashCount];
					int index = 0;

					for (int i = 0; i < _array.Count; i++)
					{
						if (_array[i] is LuaNil)
							continue;
						result[index++] = new LuaNumber(i + 1);
					}

					for (int i = 0; i < _hash.Count; i++)
					{
						if (_hash[i].Alive)
							result[index++] = _hash[i].Key;
					}

					return result;
				}
			}
		}

		/// <summary>
		/// Returns a snapshot of all live values, in the same order as <see cref="Keys"/>.
		/// </summary>
		public IReadOnlyList<LuaValue> Values
		{
			get
			{
				lock (_gate)
				{
					var result = new LuaValue[_liveArrayCount + _liveHashCount];
					int index = 0;

					for (int i = 0; i < _array.Count; i++)
					{
						if (_array[i] is LuaNil)
							continue;
						result[index++] = _array[i];
					}

					for (int i = 0; i < _hash.Count; i++)
					{
						if (_hash[i].Alive)
							result[index++] = _hash[i].Value;
					}

					return result;
				}
			}
		}

		/// <summary>
		/// Returns a snapshot enumerator over all key-value pairs in the table.
		/// </summary>
		public IEnumerator<KeyValuePair<LuaValue, LuaValue>> GetEnumerator()
			=> ((IEnumerable<KeyValuePair<LuaValue, LuaValue>>)Entries).GetEnumerator();

		IEnumerator<KeyValuePair<LuaValue, LuaValue>> IEnumerable<KeyValuePair<LuaValue, LuaValue>>.GetEnumerator()
			=> GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		// ── Lua next() ───────────────────────────────────────────────────

		/// <summary>
		/// Implements the Lua <c>next</c> function: returns the entry that follows <paramref name="after"/>
		/// in a full traversal (array part 1..n, then the hash part in insertion order).
		/// </summary>
		/// <param name="after">The key returned by the previous call, or <c>nil</c> to start.</param>
		/// <param name="key">The next key, or <c>nil</c> when the traversal is over.</param>
		/// <param name="value">The next value, or <c>nil</c> when the traversal is over.</param>
		/// <returns><see langword="true"/> while there are entries left; otherwise <see langword="false"/>.</returns>
		/// <exception cref="LuaRuntimeException">
		/// Thrown when <paramref name="after"/> is not a key of the table (the same error the reference
		/// implementation raises after a rehash invalidated an in-flight traversal).
		/// </exception>
		internal bool TryGetNext(LuaValue after, out LuaValue key, out LuaValue value)
		{
			lock (_gate)
			{
				if (after is LuaNil)
					return TryFindFirst(out key, out value);

				if (TryGetArrayIndex(after, out int index) && index <= _array.Count)
				{
					// Slots after the one that was returned; the requested key itself may have been
					// cleared during the traversal, the physical slot is what anchors the position.
					for (int i = index; i < _array.Count; i++)
					{
						if (_array[i] is LuaNil)
							continue;
						key = new LuaNumber(i + 1);
						value = _array[i];
						return true;
					}

					return TryFindFirstHash(out key, out value);
				}

				if (_positions.TryGetValue(after, out int position))
				{
					for (int i = position + 1; i < _hash.Count; i++)
					{
						if (!_hash[i].Alive)
							continue;
						key = _hash[i].Key;
						value = _hash[i].Value;
						return true;
					}

					key = LuaNil.Instance;
					value = LuaNil.Instance;
					return false;
				}

				throw new LuaRuntimeException("invalid key to 'next'");
			}
		}

		private bool TryFindFirst(out LuaValue key, out LuaValue value)
		{
			for (int i = 0; i < _array.Count; i++)
			{
				if (_array[i] is LuaNil)
					continue;
				key = new LuaNumber(i + 1);
				value = _array[i];
				return true;
			}

			return TryFindFirstHash(out key, out value);
		}

		private bool TryFindFirstHash(out LuaValue key, out LuaValue value)
		{
			for (int i = 0; i < _hash.Count; i++)
			{
				if (!_hash[i].Alive)
					continue;
				key = _hash[i].Key;
				value = _hash[i].Value;
				return true;
			}

			key = LuaNil.Instance;
			value = LuaNil.Instance;
			return false;
		}

		// ── Array part ───────────────────────────────────────────────────

		/// <summary>
		/// Stores (<paramref name="value"/> not nil) or removes (<paramref name="value"/> nil) the array
		/// slot <paramref name="index"/>. An index beyond <c>Count + 1</c> belongs to the hash part.
		/// </summary>
		private void SetArray(LuaValue key, int index, LuaValue value)
		{
			// A far index never grows the array part: it is a hash key until the array reaches it.
			if (index > _array.Count + 1)
			{
				SetHash(key, value);
				return;
			}

			if (value is LuaNil)
			{
				if (index > _array.Count || _array[index - 1] is LuaNil)
					return;

				_array[index - 1] = LuaNil.Instance;
				_liveArrayCount--;

				if (index <= _densePrefix)
					_densePrefix = index - 1;

				return;
			}

			bool appended = index > _array.Count;

			if (appended)
			{
				_array.Add(value);
				_liveArrayCount++;
			}
			else
			{
				if (_array[index - 1] is LuaNil)
					_liveArrayCount++;
				_array[index - 1] = value;
			}

			ExtendPrefix();

			if (appended)
				AbsorbFollowing();
		}

		/// <summary>
		/// Moves consecutive hash keys into the array part after the array grew at its end
		/// (<c>t[2] = b; t[1] = a</c> ends up with an array <c>1..2</c>).
		/// </summary>
		private void AbsorbFollowing()
		{
			while (true)
			{
				var nextKey = new LuaNumber(_array.Count + 1);
				if (!_positions.TryGetValue(nextKey, out int position))
					return;

				var entry = _hash[position];
				if (!entry.Alive)
					return;

				_array.Add(entry.Value);
				_liveArrayCount++;
				_positions.Remove(nextKey);

				entry.Alive = false;
				entry.Value = LuaNil.Instance;
				_liveHashCount--;
				_deadHashCount++;

				ExtendPrefix();
			}
		}

		/// <summary>
		/// Advances <see cref="_densePrefix"/> over the run of non-nil slots that starts right after it.
		/// </summary>
		private void ExtendPrefix()
		{
			while (_densePrefix < _array.Count && _array[_densePrefix] is not LuaNil)
				_densePrefix++;
		}

		// ── Hash part ────────────────────────────────────────────────────

		/// <summary>
		/// Stores (<paramref name="value"/> not nil) or removes (<paramref name="value"/> nil) a hash entry.
		/// A key that comes back after a removal becomes a new insertion, so it moves to the end.
		/// </summary>
		private void SetHash(LuaValue key, LuaValue value)
		{
			if (_positions.TryGetValue(key, out int position))
			{
				var entry = _hash[position];
				if (entry.Alive)
				{
					if (value is LuaNil)
					{
						entry.Alive = false;
						entry.Value = LuaNil.Instance;
						_liveHashCount--;
						_deadHashCount++;
					}
					else
					{
						entry.Value = value;
					}

					return;
				}

				// The position points at a tombstone: drop the stale mapping. Re-setting the key is
				// a fresh insertion (it goes to the end of the traversal order).
				_positions.Remove(key);

				if (value is LuaNil)
					return;
			}
			else if (value is LuaNil)
			{
				return; // removing a key that is not there is a no-op
			}

			CompactIfNeeded();

			_hash.Add(new HashEntry { Key = key, Value = value, Alive = true });
			_positions[key] = _hash.Count - 1;
			_liveHashCount++;
		}

		/// <summary>
		/// Drops tombstones once they outnumber the live entries. Compaction invalidates the positions
		/// of dead keys, which is exactly when Lua allows a traversal to break — it only runs from an
		/// insertion, and inserting during traversal is undefined by the language.
		/// </summary>
		private void CompactIfNeeded()
		{
			if (_deadHashCount < MinTombstonesToCompact || _deadHashCount <= _liveHashCount)
				return;

			int write = 0;
			for (int read = 0; read < _hash.Count; read++)
			{
				if (!_hash[read].Alive)
					continue;

				_hash[write++] = _hash[read];
			}

			if (write < _hash.Count)
				_hash.RemoveRange(write, _hash.Count - write);

			_positions.Clear();
			for (int i = 0; i < _hash.Count; i++)
				_positions[_hash[i].Key] = i;

			_deadHashCount = 0;
		}

		// ── Helpers ──────────────────────────────────────────────────────

		/// <summary>
		/// Recognises the keys served by the array part: integer numbers in <c>[1, int.MaxValue]</c>.
		/// <c>1</c> and <c>1.0</c> are the same key, so the check is on the value, not on the literal.
		/// </summary>
		private static bool TryGetArrayIndex(LuaValue key, out int index)
		{
			if (key is LuaNumber number)
			{
				double value = number.Value;
				if (value >= 1 && value <= int.MaxValue && value == Math.Floor(value))
				{
					index = (int)value;
					return true;
				}
			}

			index = 0;
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void ValidateKey(LuaValue key)
		{
			if (key is null)
				throw new ArgumentNullException(nameof(key));
			if (key is LuaNil)
				throw new ArgumentException("A Lua table key cannot be nil.", nameof(key));
		}

		// ── EqualityComparer for the position map ────────────────────────

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
