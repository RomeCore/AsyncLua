using System;
using System.Collections.Generic;
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
			var result = new LuaTable();
			foreach (var kvp in table)
				result.Set(kvp.Key, kvp.Value);
			return result;
		}

		public static LuaTable DeepClone(this LuaTable table)
		{
			var result = new LuaTable();
			foreach (var kvp in table)
				if (kvp.Value is LuaTable vtable)
					result.Set(kvp.Key, vtable.DeepClone());
				else
					result.Set(kvp.Key, kvp.Value);
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
	}
}
