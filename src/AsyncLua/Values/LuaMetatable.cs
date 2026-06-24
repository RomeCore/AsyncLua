using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AsyncLua.Values
{
	/// <summary>
	/// Represents a Lua metatable — a collection of metamethod handlers that define
	/// how a value behaves under operators, indexing, calling, and other operations.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Internally, metamethods are stored in a fixed-size array indexed by <see cref="LuaMetatableEvent"/>,
	/// providing O(1) access without dictionary overhead. Unset events default to <see cref="LuaNil.Instance"/>.
	/// </para>
	/// <para>
	/// This class is <b>not thread-safe</b> for writes. The owning <c>LuaState</c> (or the caller)
	/// is responsible for synchronisation when mutating metatables across threads.
	/// </para>
	/// </remarks>
	public sealed class LuaMetatable : IEnumerable<KeyValuePair<LuaMetatableEvent, LuaValue>>
	{
		/// <summary>
		/// Total number of defined metamethod events.
		/// </summary>
		internal const int EventCount = (int)LuaMetatableEvent.Close + 1;

		/// <summary>
		/// Reverse lookup table mapping <see cref="LuaMetatableEvent"/> values to their string names.
		/// Initialised first because <see cref="EventNameLookup"/> depends on it.
		/// </summary>
		private static readonly string[] EventNames = BuildEventNames();

		/// <summary>
		/// Lookup table mapping event-name strings to their <see cref="LuaMetatableEvent"/> values.
		/// </summary>
		private static readonly Dictionary<string, LuaMetatableEvent> EventNameLookup = BuildEventNameLookup();

		private readonly LuaValue[] _handlers;

		/// <summary>
		/// Initialises a new, empty metatable. All events default to <c>nil</c>.
		/// </summary>
		public LuaMetatable()
		{
			_handlers = new LuaValue[EventCount];
			for (int i = 0; i < _handlers.Length; i++)
				_handlers[i] = LuaNil.Instance;
		}

		/// <summary>
		/// Initialises a new metatable by shallow-copying the handlers from another metatable.
		/// </summary>
		/// <param name="other">The metatable to copy from. Must not be <see langword="null"/>.</param>
		public LuaMetatable(LuaMetatable other)
		{
			if (other is null)
				throw new ArgumentNullException(nameof(other));

			_handlers = new LuaValue[EventCount];
			Array.Copy(other._handlers, _handlers, EventCount);
		}

		/// <summary>
		/// Creates a <see cref="LuaMetatable"/> from a Lua table by reading its string-keyed
		/// metamethod handlers (e.g., <c>t["__add"]</c> → <see cref="LuaMetatableEvent.Add"/>).
		/// Keys that are not recognised metamethod names are silently ignored.
		/// </summary>
		/// <param name="table">The table to convert. Must not be <see langword="null"/>.</param>
		/// <returns>A new <see cref="LuaMetatable"/> populated from the table's entries.</returns>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="table"/> is <see langword="null"/>.
		/// </exception>
		public static LuaMetatable FromTable(LuaTable table)
		{
			if (table is null)
				throw new ArgumentNullException(nameof(table));

			var mt = new LuaMetatable();

			foreach (var kvp in table)
			{
				if (kvp.Key is LuaString keyStr && TryGetEventByName(keyStr.Value, out var evt))
				{
					mt[evt] = kvp.Value;
				}
			}

			return mt;
		}

		/// <summary>
		/// Gets or sets the metamethod handler for the specified event.
		/// </summary>
		/// <param name="evt">The metamethod event.</param>
		/// <returns>The handler <see cref="LuaValue"/>, or <see cref="LuaNil.Instance"/> if none is set.</returns>
		public LuaValue this[LuaMetatableEvent evt]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _handlers[(int)evt];

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			set => _handlers[(int)evt] = value ?? LuaNil.Instance;
		}

		/// <summary>
		/// Gets or sets the metamethod handler for the event identified by its Lua name
		/// (e.g., <c>"__add"</c>, <c>"__index"</c>).
		/// </summary>
		/// <param name="eventName">The Lua metamethod name. Must start with two underscores.</param>
		/// <returns>The handler <see cref="LuaValue"/>, or <see cref="LuaNil.Instance"/> if none is set.</returns>
		/// <exception cref="KeyNotFoundException">
		/// Thrown if <paramref name="eventName"/> is not a recognised metamethod name.
		/// </exception>
		public LuaValue this[string eventName]
		{
			get => this[GetEventByName(eventName)];
			set => this[GetEventByName(eventName)] = value;
		}

		/// <summary>
		/// Gets the metamethod handler for the specified event.
		/// </summary>
		/// <param name="evt">The metamethod event.</param>
		/// <returns>The handler <see cref="LuaValue"/>, or <see cref="LuaNil.Instance"/> if none is set.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public LuaValue Get(LuaMetatableEvent evt) => _handlers[(int)evt];

		/// <summary>
		/// Sets the metamethod handler for the specified event.
		/// Passing <see langword="null"/> resets the event to <c>nil</c>.
		/// </summary>
		/// <param name="evt">The metamethod event.</param>
		/// <param name="handler">The handler value to set, or <see langword="null"/> to clear.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Set(LuaMetatableEvent evt, LuaValue handler) =>
			_handlers[(int)evt] = handler ?? LuaNil.Instance;

		/// <summary>
		/// Gets the metamethod handler by its Lua string name.
		/// </summary>
		/// <param name="eventName">The metamethod name (e.g., <c>"__add"</c>).</param>
		/// <returns>The handler <see cref="LuaValue"/>, or <see cref="LuaNil.Instance"/> if none is set.</returns>
		/// <exception cref="KeyNotFoundException">
		/// Thrown if <paramref name="eventName"/> is not a recognised metamethod name.
		/// </exception>
		public LuaValue Get(string eventName) => this[GetEventByName(eventName)];

		/// <summary>
		/// Sets the metamethod handler by its Lua string name.
		/// </summary>
		/// <param name="eventName">The metamethod name (e.g., <c>"__add"</c>).</param>
		/// <param name="handler">The handler value to set, or <see langword="null"/> to clear.</param>
		/// <exception cref="KeyNotFoundException">
		/// Thrown if <paramref name="eventName"/> is not a recognised metamethod name.
		/// </exception>
		public void Set(string eventName, LuaValue handler) =>
			this[GetEventByName(eventName)] = handler;

		/// <summary>
		/// Determines whether any non-nil handler is set for the specified event.
		/// </summary>
		/// <param name="evt">The metamethod event.</param>
		/// <returns><see langword="true"/> if a handler (other than <c>nil</c>) is set.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool HasEvent(LuaMetatableEvent evt) => _handlers[(int)evt].Type != LuaType.Nil;

		/// <summary>
		/// Returns the Lua metatable event name for the given event (e.g., <c>"__add"</c>).
		/// </summary>
		/// <param name="evt">The metamethod event.</param>
		/// <returns>The Lua name string for the event.</returns>
		public static string GetEventName(LuaMetatableEvent evt) => EventNames[(int)evt];

		/// <summary>
		/// Returns the <see cref="LuaMetatableEvent"/> value for the given Lua metamethod name.
		/// </summary>
		/// <param name="eventName">
		/// The metamethod name (e.g., <c>"__add"</c>). Must include the leading underscores.
		/// </param>
		/// <returns>The corresponding <see cref="LuaMetatableEvent"/>.</returns>
		/// <exception cref="KeyNotFoundException">
		/// Thrown if <paramref name="eventName"/> is not a recognised metamethod name.
		/// </exception>
		public static LuaMetatableEvent GetEventByName(string eventName)
		{
			if (eventName is null)
				throw new ArgumentNullException(nameof(eventName));

			if (EventNameLookup.TryGetValue(eventName, out var evt))
				return evt;

			throw new KeyNotFoundException($"Unknown metamethod event: '{eventName}'.");
		}

		/// <summary>
		/// Attempts to get the <see cref="LuaMetatableEvent"/> for the given Lua metamethod name.
		/// </summary>
		/// <param name="eventName">The metamethod name (e.g., <c>"__add"</c>).</param>
		/// <param name="evt">When this method returns, contains the event if found.</param>
		/// <returns><see langword="true"/> if the name was recognised; otherwise, <see langword="false"/>.</returns>
		public static bool TryGetEventByName(string eventName, out LuaMetatableEvent evt)
		{
			if (eventName is null)
			{
				evt = default;
				return false;
			}
			return EventNameLookup.TryGetValue(eventName, out evt);
		}

		/// <summary>
		/// Returns an enumerator over all events that have non-nil handlers.
		/// </summary>
		/// <returns>An enumerator over (event, handler) pairs.</returns>
		public Enumerator GetEnumerator() => new Enumerator(_handlers);

		IEnumerator<KeyValuePair<LuaMetatableEvent, LuaValue>> IEnumerable<KeyValuePair<LuaMetatableEvent, LuaValue>>.GetEnumerator()
			=> new BoxedEnumerator(_handlers);

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		#region ── Enumerator ────────────────────────────────────────────

		/// <summary>
		/// A struct enumerator that iterates over all non-nil metamethod events in a metatable.
		/// </summary>
		public struct Enumerator : IEnumerator<KeyValuePair<LuaMetatableEvent, LuaValue>>
		{
			private readonly LuaValue[] _handlers;
			private int _index;

			internal Enumerator(LuaValue[] handlers)
			{
				_handlers = handlers;
				_index = -1;
			}

			/// <inheritdoc />
			public KeyValuePair<LuaMetatableEvent, LuaValue> Current { get; private set; }

			object IEnumerator.Current => Current;

			/// <inheritdoc />
			public bool MoveNext()
			{
				while (++_index < EventCount)
				{
					if (_handlers[_index].Type != LuaType.Nil)
					{
						Current = new KeyValuePair<LuaMetatableEvent, LuaValue>(
							(LuaMetatableEvent)_index,
							_handlers[_index]);
						return true;
					}
				}
				return false;
			}

			/// <inheritdoc />
			public void Reset() => _index = -1;

			/// <inheritdoc />
			public void Dispose() { }
		}

		private sealed class BoxedEnumerator : IEnumerator<KeyValuePair<LuaMetatableEvent, LuaValue>>
		{
			private Enumerator _inner;

			public BoxedEnumerator(LuaValue[] handlers) => _inner = new Enumerator(handlers);

			public KeyValuePair<LuaMetatableEvent, LuaValue> Current => _inner.Current;
			object IEnumerator.Current => _inner.Current;
			public bool MoveNext() => _inner.MoveNext();
			public void Reset() => _inner.Reset();
			public void Dispose() => _inner.Dispose();
		}

		#endregion

		#region ── Static initialisation ──────────────────────────────────

		private static Dictionary<string, LuaMetatableEvent> BuildEventNameLookup()
		{
			var dict = new Dictionary<string, LuaMetatableEvent>(EventCount);
			for (int i = 0; i < EventCount; i++)
			{
				dict[EventNames[i]] = (LuaMetatableEvent)i;
			}
			return dict;
		}

		private static string[] BuildEventNames()
		{
			return new string[EventCount]
			{
				"__add",      // Add
                "__sub",      // Sub
                "__mul",      // Mul
                "__div",      // Div
                "__mod",      // Mod
                "__pow",      // Pow
                "__unm",      // Unm
                "__idiv",     // IDiv
                "__band",     // BAnd
                "__bor",      // BOr
                "__bxor",     // BXor
                "__bnot",     // BNot
                "__shl",      // ShL
                "__shr",      // ShR
                "__concat",   // Concat
                "__len",      // Len
                "__eq",       // Eq
                "__lt",       // Lt
                "__le",       // Le
                "__index",    // Index
                "__newindex", // NewIndex
                "__call",     // Call
                "__tostring", // ToString
                "__gc",       // GC
                "__mode",     // Mode
                "__metatable",// MetaTable
                "__name",     // Name
                "__pairs",    // Pairs
                "__close",    // Close
            };
		}

		#endregion
	}
}
