using System;
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
						tbl.Set(tbl.Length + 1, args[1]);
					else
					{
						var pos = (int)((LuaNumber)args[1]).Value;
						int len = tbl.Length;
						for (int i = len; i >= pos; i--)
							tbl.Set(i + 1, tbl.Get(i));
						tbl.Set(pos, args[2]);
					}
					return LuaTuple.Empty;
				}, "table.insert"));

			table.Set(new LuaString("remove"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1 || args[0] is not LuaTable tbl)
						return new LuaTuple(LuaNil.Instance);

					int len = tbl.Length;
					int pos = args.Length > 1 ? (int)((LuaNumber)args[1]).Value : len;
					var removed = tbl.Get(pos);
					tbl.Set(pos, LuaNil.Instance);

					for (int i = pos + 1; i <= len; i++)
					{
						var next = tbl.Get(i);
						tbl.Set(i - 1, next);
						if (next is LuaNil) break;
					}
					tbl.Set(len, LuaNil.Instance);
					return new LuaTuple(removed);
				}, "table.remove"));

			table.Set(new LuaString("concat"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1 || args[0] is not LuaTable tbl)
						return new LuaTuple(new LuaString(""));

					var sep = args.Length > 1 ? args[1].ToString() : "";
					var parts = new System.Collections.Generic.List<string>();
					for (int i = 1; ; i++)
					{
						var v = tbl.Get(i);
						if (v is LuaNil) break;
						parts.Add(v.ToString());
					}
					return new LuaTuple(new LuaString(string.Join(sep, parts)));
				}, "table.concat"));

			table.Set(new LuaString("sort"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1 || args[0] is not LuaTable tbl)
						return LuaTuple.Empty;

					var list = new System.Collections.Generic.List<LuaValue>();
					for (int i = 1; ; i++)
					{
						var v = tbl.Get(i);
						if (v is LuaNil) break;
						list.Add(v);
					}

					if (args.Length > 1 && args[1] is LuaFunction comparer)
					{
						list.Sort((a, b) =>
						{
							var result = comparer.Invoke(ctx, new[] { a, b });
							return result.First.ToBoolean() ? -1 : 1;
						});
					}
					else
					{
						list.Sort((a, b) =>
						{
							if (a is LuaNumber na && b is LuaNumber nb)
								return na.Value.CompareTo(nb.Value);
							return a.ToString().CompareTo(b.ToString());
						});
					}

					// Clear and rebuild.
					for (int i = 1; i <= list.Count; i++)
						tbl.Set(i, list[i - 1]);
					for (int i = list.Count + 1; ; i++)
					{
						if (tbl.Get(i) is LuaNil) break;
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
		}
	}
}
