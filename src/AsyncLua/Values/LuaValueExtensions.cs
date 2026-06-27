using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AsyncLua.Values
{
	public static class LuaValueExtensions
	{
		/// <summary>
		/// Creates a shallow copy of the given Lua table.
		/// </summary>
		/// <param name="table">The Lua table to clone.</param>
		/// <returns>A shallow copy of the given Lua table.</returns>
		public static LuaTable ShallowClone(this LuaTable table)
		{
			var result = new LuaTable
			{
				Metatable = table.Metatable?.ShallowClone()
			};
			foreach (var kvp in table)
				result.Set(kvp.Key, kvp.Value);
			return result;
		}

		/// <summary>
		/// Creates a shallow copy of the given Lua table.
		/// </summary>
		/// <param name="table">The Lua table to clone.</param>
		/// <returns>A shallow copy of the given Lua table.</returns>
		public static LuaMetatable ShallowClone(this LuaMetatable metatable)
		{
			return new LuaMetatable(metatable);
		}

		/// <summary>
		/// Creates a deep copy of the given Lua table.
		/// </summary>
		/// <param name="table">The Lua table to clone.</param>
		/// <returns>A deep copy of the given Lua table.</returns>
		public static LuaTable DeepClone(this LuaTable table)
		{
			var result = new LuaTable
			{
				Metatable = table.Metatable?.DeepClone()
			};
			foreach (var kvp in table)
			{
				if (kvp.Key is LuaString stringKey && stringKey.Value == "_G")
					continue;
				result.Set(kvp.Key, kvp.Value.DeepClone());
			}
			return result;
		}

		/// <summary>
		/// Creates a deep copy of the given Lua value (even tables and metatables).
		/// </summary>
		/// <param name="table">The Lua value to clone.</param>
		/// <returns>A deep copy of the given Lua value.</returns>
		public static LuaValue DeepClone(this LuaValue value)
		{
			return value switch
			{
				LuaNil => LuaNil.Instance,
				LuaBoolean boolean => boolean,
				// Literal types (nil and boolean) cannot have metatables, but other can
				LuaNumber number => new LuaNumber(number.Value) { Metatable = value.Metatable?.DeepClone() },
				LuaString str => new LuaString(str.Value) { Metatable = value.Metatable?.DeepClone() },
				LuaTable table => table.DeepClone(),
				LuaTuple tuple => new LuaTuple(tuple.ToArray<LuaValue>()) { Metatable = value.Metatable?.DeepClone() },
				LuaUserData userdata => new LuaUserData(userdata.Target) { Metatable = value.Metatable?.DeepClone() },
				// Pass-through Task and Thread, they are complex to clone and not very useful in most cases
				_ => value
			};
		}

		/// <summary>
		/// Creates a deep copy of the given Lua metatable.
		/// </summary>
		/// <param name="table">The Lua metatable to clone.</param>
		/// <returns>A deep copy of the given Lua metatable.</returns>
		public static LuaMetatable DeepClone(this LuaMetatable metatable)
		{
			var result = new LuaMetatable();

			foreach (var kvp in metatable)
			{
				if (kvp.Value is LuaTable vtable)
					result[kvp.Key] = vtable.DeepClone();
				else
					result[kvp.Key] = kvp.Value;
			}

			return result;
		}

		/// <summary>
		/// Resolves a namespace within the given Lua table. If the namespace does not exist, it will be created.
		/// </summary>
		/// <param name="table">The Lua table to resolve the namespace within. Typically this is global table.</param>
		/// <param name="namespaceStr">The namespace to resolve. Can contain dots to denote sub-namespaces (e.g. "math.utils"). If null or empty, the global table is returned.</param>
		/// <returns></returns>
		public static LuaTable ResolveNamespace(this LuaTable table, string? namespaceStr)
		{
			if (string.IsNullOrEmpty(namespaceStr))
				return table;

			var parts = namespaceStr!.Split('.');
			for (int i = 0; i < parts.Length; i++)
			{
				var part = parts[i];
				var partStr = new LuaString(part);
				if (table.TryGetValue(partStr, out var value) && value is LuaTable next)
				{
					table = next;
				}
				else
				{
					next = new LuaTable();
					table[partStr] = next;
					table = next;
				}
			}

			return table;
		}

		/// <summary>
		/// Creates a deep clone of this table merged with another table (non-mutating).
		/// The original tables are not modified - a new table is always returned.
		/// For keys that exist in both tables:
		///   1) If both values are tables, they are merged recursively into a new table.
		///   2) Otherwise, the value from <paramref name="otherTable"/> is used.
		/// For keys only in <paramref name="otherTable"/>, they are added.
		/// Keys only in <paramref name="table"/> are preserved (deep-cloned).
		/// All nested tables are deep-cloned to avoid reference sharing.
		/// </summary>
		/// <param name="table">The source table (not modified).</param>
		/// <param name="otherTable">The table to merge in (not modified).</param>
		/// <param name="script">Optional script for creating new tables. Defaults to <paramref name="table"/>.OwnerScript.</param>
		/// <returns>A new table that is the merged result of both input tables.</returns>
		public static LuaTable DeepMergeWith(this LuaTable table, LuaTable otherTable)
		{
			if (table == null)
				return otherTable;
			if (otherTable == null)
				return table;

			var result = DeepClone(table);
			foreach (var kvp in otherTable.Entries)
			{
				if (kvp.Key is LuaString stringKey && stringKey.Value == "_G")
					continue;

				var existingValue = result.Get(kvp.Key);

				if (existingValue is LuaTable existingTable && kvp.Value is LuaTable newValueTable)
				{
					result.Set(kvp.Key, existingTable.DeepMergeWith(newValueTable));
				}
				else
				{
					result.Set(kvp.Key, kvp.Value.DeepClone());
				}
			}

			return result;
		}

		/// <summary>
		/// Creates a snapshot of the current Lua state, including all global variables and tables.
		/// Useful for concurrent execution.
		/// </summary>
		/// <param name="script">The Lua state to take a snapshot of.</param>
		/// <returns>A new Lua script that is a snapshot of the original.</returns>
		public static LuaState CreateSnapshot(this LuaState state)
		{
			var snapshotLua = new LuaState(state.Globals, state.Parser,
				state.CompilerSettings.Clone(), state.InterpreterSettings.Clone());

			foreach (var kvp in state.TypeMetatables)
				snapshotLua.TypeMetatables[kvp.Key] = kvp.Value.DeepClone();

			return snapshotLua;
		}
	}
}
