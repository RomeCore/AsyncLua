using System;
using System.Collections;
using System.Collections.Concurrent;
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
	/// </remarks>
	public sealed class LuaMetatable : IEnumerable<KeyValuePair<LuaMetatableEvent, LuaValue>>
	{
		/// <summary>
		/// Reverse lookup table mapping <see cref="LuaMetatableEvent"/> values to their string names.
		/// Initialised first because <see cref="EventNameLookup"/> depends on it.
		/// </summary>
		private static readonly string[] EventNames = BuildEventNames();

		/// <summary>
		/// Lookup table mapping event-name strings to their <see cref="LuaMetatableEvent"/> values.
		/// </summary>
		private static readonly ConcurrentDictionary<string, LuaMetatableEvent> EventNameLookup = BuildEventNameLookup();

		private readonly LuaValue[] _handlers;

		/// <summary>
		/// The Lua table this metatable wraps, when it was created by <c>setmetatable</c>.
		/// When set, the table is the single source of truth: handlers present in the array
		/// win, everything else is read from - and written back to - the table. This is what
		/// makes <c>getmetatable(t) == mt</c> hold and lets metamethods added after the call
		/// take effect, exactly like Lua.
		/// </summary>
		private readonly LuaTable? _source;

		/// <summary>
		/// Initialises a new, empty metatable. All events default to <c>nil</c>.
		/// </summary>
		public LuaMetatable()
		{
			_handlers = new LuaValue[EventNames.Length];
			for (int i = 0; i < _handlers.Length; i++)
				_handlers[i] = LuaNil.Instance;
		}

		/// <summary>
		/// Initialises a metatable backed by a Lua table, so the handlers are looked up in
		/// that table on every access instead of being copied out of it once.
		/// </summary>
		/// <param name="source">The table holding the <c>__name</c>-keyed handlers.</param>
		private LuaMetatable(LuaTable source)
		{
			_handlers = new LuaValue[EventNames.Length];
			for (int i = 0; i < _handlers.Length; i++)
				_handlers[i] = LuaNil.Instance;
			_source = source;
		}

		/// <summary>
		/// Initialises a new metatable by shallow-copying the handlers from another metatable.
		/// </summary>
		/// <param name="other">The metatable to copy from. Must not be <see langword="null"/>.</param>
		public LuaMetatable(LuaMetatable other)
		{
			if (other is null)
				throw new ArgumentNullException(nameof(other));

			_handlers = new LuaValue[EventNames.Length];
			if (other._source is null)
			{
				Array.Copy(other._handlers, _handlers, EventNames.Length);
			}
			else
			{
				// A clone must not stay attached to the original's table.
				for (int i = 0; i < _handlers.Length; i++)
					_handlers[i] = other.Get((LuaMetatableEvent)i);
			}
		}

		/// <summary>
		/// Creates a <see cref="LuaMetatable"/> backed by a Lua table whose string-keyed
		/// metamethod handlers (e.g., <c>t["__add"]</c> → <see cref="LuaMetatableEvent.Add"/>)
		/// are read on every access. Keys that are not recognised metamethod names are ignored.
		/// </summary>
		/// <remarks>
		/// The table is not copied: it stays the metatable, so mutating it afterwards changes
		/// the behaviour of every value it is attached to, and <see cref="ToTable"/> hands the
		/// very same table back.
		/// </remarks>
		/// <param name="table">The table to wrap. Must not be <see langword="null"/>.</param>
		/// <returns>A new <see cref="LuaMetatable"/> reading its handlers from the table.</returns>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="table"/> is <see langword="null"/>.
		/// </exception>
		public static LuaMetatable FromTable(LuaTable table)
		{
			if (table is null)
				throw new ArgumentNullException(nameof(table));

			return new LuaMetatable(table);
		}

		/// <summary>
		/// Converts this metatable to a Lua table, with string keys corresponding
		/// to the metamethod names and values corresponding to the handlers.
		/// </summary>
		/// <returns>
		/// The backing table when this metatable wraps one (so <c>getmetatable(t)</c> returns
		/// the table that was passed to <c>setmetatable</c>, as in Lua); otherwise a new
		/// <see cref="LuaTable"/> populated with a snapshot of the handlers.
		/// </returns>
		public LuaTable ToTable()
		{
			if (_source is not null)
				return _source;

			var table = new LuaTable();

			for (int i = 0; i < _handlers.Length; i++)
			{
				var handler = _handlers[i];
				if (handler != LuaNil.Instance)
					table.Set(EventNames[i], handler);
			}

			return table;
		}

		/// <summary>
		/// Gets or sets the metamethod handler for the specified event.
		/// </summary>
		/// <param name="evt">The metamethod event.</param>
		/// <returns>The handler <see cref="LuaValue"/>, or <see cref="LuaNil.Instance"/> if none is set.</returns>
		public LuaValue this[LuaMetatableEvent evt]
		{
			get => Get(evt);
			set => Set(evt, value);
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
		public LuaValue Get(LuaMetatableEvent evt)
		{
			var handler = _handlers[(int)evt];
			if (handler.Type != LuaType.Nil || _source is null)
				return handler;

			return _source.Get(EventNames[(int)evt]);
		}

		/// <summary>
		/// Sets the metamethod handler for the specified event.
		/// Passing <see langword="null"/> resets the event to <c>nil</c>.
		/// </summary>
		/// <param name="evt">The metamethod event.</param>
		/// <param name="handler">The handler value to set, or <see langword="null"/> to clear.</param>
		public void Set(LuaMetatableEvent evt, LuaValue handler)
		{
			if (_source is not null)
			{
				_source.Set(EventNames[(int)evt], handler ?? LuaNil.Instance);
				return;
			}

			_handlers[(int)evt] = handler ?? LuaNil.Instance;
		}

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
		public bool HasEvent(LuaMetatableEvent evt) => Get(evt).Type != LuaType.Nil;

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
		public Enumerator GetEnumerator() => new Enumerator(this);

		IEnumerator<KeyValuePair<LuaMetatableEvent, LuaValue>> IEnumerable<KeyValuePair<LuaMetatableEvent, LuaValue>>.GetEnumerator()
			=> new BoxedEnumerator(this);

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		/// <summary>
		/// A struct enumerator that iterates over all non-nil metamethod events in a metatable.
		/// </summary>
		public struct Enumerator : IEnumerator<KeyValuePair<LuaMetatableEvent, LuaValue>>
		{
			private readonly LuaMetatable _owner;
			private int _index;

			internal Enumerator(LuaMetatable owner)
			{
				_owner = owner;
				_index = -1;
			}

			/// <inheritdoc />
			public KeyValuePair<LuaMetatableEvent, LuaValue> Current { get; private set; }

			object IEnumerator.Current => Current;

			/// <inheritdoc />
			public bool MoveNext()
			{
				while (++_index < EventNames.Length)
				{
					var handler = _owner.Get((LuaMetatableEvent)_index);
					if (handler.Type != LuaType.Nil)
					{
						Current = new KeyValuePair<LuaMetatableEvent, LuaValue>(
							(LuaMetatableEvent)_index,
							handler);
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

			public BoxedEnumerator(LuaMetatable owner) => _inner = new Enumerator(owner);

			public KeyValuePair<LuaMetatableEvent, LuaValue> Current => _inner.Current;
			object IEnumerator.Current => _inner.Current;
			public bool MoveNext() => _inner.MoveNext();
			public void Reset() => _inner.Reset();
			public void Dispose() => _inner.Dispose();
		}

		private static ConcurrentDictionary<string, LuaMetatableEvent> BuildEventNameLookup()
		{
			var dict = new ConcurrentDictionary<string, LuaMetatableEvent>();
			for (int i = 0; i < EventNames.Length; i++)
			{
				dict[EventNames[i]] = (LuaMetatableEvent)i;
			}
			return dict;
		}

		private static string[] BuildEventNames()
		{
			var result = new List<string>();
			foreach (LuaMetatableEvent evt in Enum.GetValues(typeof(LuaMetatableEvent)))
			{
				result.Add($"__{evt.ToString().ToLowerInvariant()}");
			}
			return result.ToArray();
		}
	}
}
