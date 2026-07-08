using System;
using System.Collections.Generic;
using System.Linq;
using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/// <summary>
	/// Implements the standard Lua <c>table</c> library with functions for
	/// manipulating arrays and tables.
	/// </summary>
	public sealed class TableLibrary : LuaTableBaseLibrary
	{
		/// <summary>
		/// Gets the namespace name <c>"table"</c>.
		/// </summary>
		public override string Namespace => "table";

		/// <summary>
		/// Populates the table library with functions.
		/// </summary>
		protected override void PopulateTable(LuaState state, LuaTable table)
		{
			table.Set(new LuaString("insert"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 2 || args[0] is not LuaTable tbl)
						return LuaTuple.Empty;

					if (args.Length == 2)
					{
						// table.insert(t, value) — insert at end
						int e = tbl.Length + 1; // first empty element
						tbl.Set(e, args[1]);
					}
					else if (args.Length == 3)
					{
						// table.insert(t, pos, value)
						int e = tbl.Length + 1; // first empty element
						int pos = (int)((LuaNumber)args[1]).Value;

						// Check pos in [1, e]
						if ((ulong)pos - 1u >= (ulong)e)
							throw new LuaRuntimeException("position out of bounds");

						for (int i = e; i > pos; i--)
							tbl.Set(i, tbl.Get(i - 1));
						tbl.Set(pos, args[2]);
					}
					else
					{
						throw new LuaRuntimeException("wrong number of arguments to 'insert'");
					}

					return LuaTuple.Empty;
				}, "table.insert"));

			table.Set(new LuaString("remove"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1 || args[0] is not LuaTable tbl)
						return new LuaTuple(LuaNil.Instance);

					int size = tbl.Length;
					int pos = args.Length > 1 ? (int)((LuaNumber)args[1]).Value : size;

					// If pos was explicitly given, validate it.
					if (args.Length > 1 && (args.Length < 2 || (ulong)pos - 1u > (ulong)size))
						throw new LuaRuntimeException("position out of bounds");

					// Get the value at pos (may be nil if pos > size).
					var removed = tbl.Get(pos);

					// Shift elements left: t[i] = t[i + 1] for i = pos .. size - 1
					// After the loop, 'i' ends at 'size' (or stays at 'pos' if no shift happened).
					int i = pos;
					for (; i < size; i++)
						tbl.Set(i, tbl.Get(i + 1));
					tbl.Set(i, LuaNil.Instance); // i == max(pos, size)

					return new LuaTuple(removed);
				}, "table.remove"));

			table.Set(new LuaString("concat"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1 || args[0] is not LuaTable tbl)
						return new LuaTuple(new LuaString(""));

					string sep = args.Length > 1 ? args[1].ToString() ?? "" : "";
					int i = args.Length > 2 ? (int)((LuaNumber)args[2]).Value : 1;
					int last = args.Length > 3 ? (int)((LuaNumber)args[3]).Value : tbl.Length;

					if (i > last)
						return new LuaTuple(new LuaString(""));

					var parts = new List<string>();
					for (int idx = i; idx < last; idx++)
					{
						AddField(tbl, idx, parts);
						parts.Add(sep);
					}
					if (i <= last)
						AddField(tbl, last, parts);

					return new LuaTuple(new LuaString(string.Concat(parts)));
				}, "table.concat"));

			table.Set(new LuaString("sort"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1 || args[0] is not LuaTable tbl)
						return LuaTuple.Empty;

					int n = tbl.Length;
					if (n > 1)
					{
						// Build list of values.
						var list = new List<LuaValue>(n);
						for (int i = 1; i <= n; i++)
							list.Add(tbl.Get(i));

						if (args.Length > 1 && args[1] is LuaFunction comparer)
						{
							// Use Lua function as comparator.
							// Lua comparator returns true if a < b.
							list.Sort((a, b) =>
							{
								var result = comparer.Invoke(ctx, new[] { a, b });
								return result.First.ToBoolean() ? -1 : 1;
							});
						}
						else
						{
							// Default sort using Lua < operator.
							list.Sort((a, b) => CompareLuaLess(a, b, ctx));
						}

						// Rebuild the table.
						for (int i = 1; i <= list.Count; i++)
							tbl.Set(i, list[i - 1]);
						for (int i = list.Count + 1; i <= n; i++)
							tbl.Set(i, LuaNil.Instance);
					}

					return LuaTuple.Empty;
				}, "table.sort"));

			table.Set(new LuaString("pack"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					var t = new LuaTable(args.Length);
					for (int i = 0; i < args.Length; i++)
						t.Set(i + 1, args[i]);
					t.Set(new LuaString("n"), new LuaNumber(args.Length));
					return new LuaTuple(t);
				}, "table.pack"));

			table.Set(new LuaString("unpack"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1 || args[0] is not LuaTable tbl)
						return LuaTuple.Empty;

					int len = tbl.Length;
					int i = args.Length > 1 ? (int)((LuaNumber)args[1]).Value : 1;
					int e = args.Length > 2 ? (int)((LuaNumber)args[2]).Value : len;

					if (i > e)
						return LuaTuple.Empty;

					int n = e - i + 1;
					if (n >= int.MaxValue)
						throw new LuaRuntimeException("too many results to unpack");

					var values = new LuaValue[n];
					for (int idx = i; idx <= e; idx++)
						values[idx - i] = tbl.Get(idx);

					return new LuaTuple(values);
				}, "table.unpack"));

			table.Set(new LuaString("move"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 4 || args[0] is not LuaTable src)
						return new LuaTuple(LuaNil.Instance);

					int f = (int)((LuaNumber)args[1]).Value;
					int e = (int)((LuaNumber)args[2]).Value;
					int t = (int)((LuaNumber)args[3]).Value;
					LuaTable dst = args.Length > 4 && args[4] is LuaTable dt ? dt : src;

					if (e >= f)
					{
						int n = e - f + 1;

						// Check for overflow (like original Lua).
						if (f <= 0 && e > int.MaxValue + f)
							throw new LuaRuntimeException("too many elements to move");
						if (t > int.MaxValue - n + 1)
							throw new LuaRuntimeException("destination wrap around");

						// Determine copy direction to avoid overwriting.
						if (t > e || t <= f || dst != src)
						{
							for (int i = 0; i < n; i++)
								dst.Set(t + i, src.Get(f + i));
						}
						else
						{
							for (int i = n - 1; i >= 0; i--)
								dst.Set(t + i, src.Get(f + i));
						}
					}

					return new LuaTuple(dst);
				}, "table.move"));

			table.Set(new LuaString("create"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("create: expected at least 1 argument, got 0");

					double sizeseqVal = ((LuaNumber)args[0]).Value;
					double sizerestVal = args.Length > 1 ? ((LuaNumber)args[1]).Value : 0;

					if (sizeseqVal < 0 || sizerestVal < 0 || sizeseqVal > int.MaxValue || sizerestVal > int.MaxValue)
						throw new LuaRuntimeException("size out of range");

					int sizeseq = (int)sizeseqVal;
					int sizerest = (int)sizerestVal;

					var t = new LuaTable(sizeseq + sizerest);
					return new LuaTuple(t);
				}, "table.create"));
		}

		/// <summary>
		/// Adds a field value to the parts list for <c>table.concat</c>.
		/// Throws an error if the value is not a string or number (like original Lua).
		/// </summary>
		private static void AddField(LuaTable tbl, int index, List<string> parts)
		{
			var v = tbl.Get(index);
			if (v is LuaNil)
				throw new LuaRuntimeException($"invalid value (nil) at index {index} in table for 'concat'");

			if (v is LuaString s)
			{
				parts.Add(s.Value);
			}
			else if (v is LuaNumber num)
			{
				parts.Add(num.ToString());
			}
			else
			{
				throw new LuaRuntimeException($"invalid value ({v.TypeName}) at index {index} in table for 'concat'");
			}
		}

		/// <summary>
		/// Compares two Lua values using the Lua <c>&lt;</c> operator semantics.
		/// Returns -1 if a &lt; b, 0 if equal, 1 if a &gt; b.
		/// </summary>
		private static int CompareLuaLess(LuaValue a, LuaValue b, LuaCallingContext ctx)
		{
			if (a is LuaNumber na && b is LuaNumber nb)
				return na.Value < nb.Value ? -1 : (na.Value > nb.Value ? 1 : 0);

			if (a is LuaString sa && b is LuaString sb)
				return string.Compare(sa.Value, sb.Value, StringComparison.Ordinal);

			if (a is LuaNumber && b is LuaString)
				return -1; // numbers before strings (Lua convention: number < string)

			if (a is LuaString && b is LuaNumber)
				return 1;

			// Fallback: compare by type name as string.
			return string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal);
		}
	}
}
