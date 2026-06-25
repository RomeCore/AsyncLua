using System;
using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/// <summary>
	/// Implements the standard Lua global functions: <c>print</c>, <c>type</c>,
	/// <c>tostring</c>, <c>tonumber</c>, <c>error</c>, <c>assert</c>, <c>ipairs</c>,
	/// <c>pairs</c>, <c>next</c>, <c>select</c>.
	/// </summary>
	public sealed class BasicLibrary : LuaGlobalBaseLibrary
	{
		/// <summary>
		/// Registers all basic library functions into the specified Lua state.
		/// </summary>
		/// <param name="state">The Lua state to import into.</param>
		public override void Import(LuaState state)
		{
			state.Register("print", new LuaCallbackFunction(Print, "print"));
			state.Register("type", new LuaCallbackFunction(Type, "type"));
			state.Register("tostring", new LuaCallbackFunction(ToString, "tostring"));
			state.Register("tonumber", new LuaCallbackFunction(ToNumber, "tonumber"));
			state.Register("error", new LuaCallbackFunction(Error, "error"));
			state.Register("assert", new LuaCallbackFunction(Assert, "assert"));
			state.Register("ipairs", new LuaCallbackFunction(Ipairs, "ipairs"));
			state.Register("pairs", new LuaCallbackFunction(Pairs, "pairs"));
			state.Register("next", new LuaCallbackFunction(Next, "next"));
			state.Register("select", new LuaCallbackFunction(Select, "select"));
		}

		private static LuaTuple Print(LuaCallingContext ctx, LuaValue[] args)
		{
			var parts = new string[args.Length];
			for (int i = 0; i < args.Length; i++)
				parts[i] = args[i].ToString();
			System.Diagnostics.Debug.WriteLine(string.Join("\t", parts));
			return LuaTuple.Empty;
		}

		private static LuaTuple Type(LuaCallingContext ctx, LuaValue[] args)
		{
			var typeName = args.Length == 0 ? "nil" : args[0].TypeName;
			return new LuaTuple(new LuaString(typeName));
		}

		private static LuaTuple ToString(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				return new LuaTuple(new LuaString("nil"));

			var value = args[0];

			// Check for __tostring metamethod.
			var mt = value.Metatable;
			if (mt != null && mt.HasEvent(LuaMetatableEvent.ToString))
			{
				var handler = mt.Get(LuaMetatableEvent.ToString);
				if (handler is LuaFunction func)
					return func.Invoke(ctx, new[] { value });
			}

			return new LuaTuple(new LuaString(value.ToString()));
		}

		private static LuaTuple ToNumber(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || !args[0].TryToNumber(out var n))
				return new LuaTuple(LuaNil.Instance);
			return new LuaTuple(new LuaNumber(n));
		}

		private static LuaTuple Error(LuaCallingContext ctx, LuaValue[] args)
		{
			throw new LuaRuntimeException(
				args.Length > 0 ? args[0].ToString() : "error");
		}

		private static LuaTuple Assert(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length > 0 && !args[0].ToBoolean())
			{
				var msg = args.Length > 1 ? args[1].ToString() : "assertion failed!";
				throw new LuaRuntimeException(msg);
			}
			return args.Length > 0 ? new LuaTuple(args) : LuaTuple.Empty;
		}

		private static LuaTuple Ipairs(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaTable t)
				return new LuaTuple(LuaNil.Instance);

			var iter = new LuaCallbackFunction(
				(ctx2, args2) =>
				{
					var tbl = (LuaTable)args2[0];
					var prev = args2[1];
					int idx = prev is LuaNil ? 1 : (int)((LuaNumber)prev).Value + 1;
					var val = tbl.Get(idx);
					if (val is LuaNil)
						return new LuaTuple(LuaNil.Instance);
					return new LuaTuple(new LuaNumber(idx), val);
				}, "ipairs_iter");

			return new LuaTuple(iter, t, new LuaNumber(0));
		}

		private static LuaTuple Pairs(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaTable t)
				return new LuaTuple(LuaNil.Instance);

			var nextFunc = new LuaCallbackFunction(
				(ctx2, args2) =>
				{
					var tbl = (LuaTable)args2[0];
					var prevKey = args2[1];

					LuaValue? foundKey = null;
					LuaValue? foundVal = null;
					bool prevFound = prevKey is LuaNil;

					foreach (var kvp in tbl)
					{
						if (prevFound)
						{
							foundKey = kvp.Key;
							foundVal = kvp.Value;
							break;
						}
						if (kvp.Key.Equals(prevKey))
							prevFound = true;
					}

					if (foundKey is null)
						return new LuaTuple(LuaNil.Instance);
					return new LuaTuple(foundKey, foundVal!);
				}, "next");

			return new LuaTuple(nextFunc, t, LuaNil.Instance);
		}

		private static LuaTuple Next(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length < 1 || args[0] is not LuaTable tbl)
				return new LuaTuple(LuaNil.Instance);

			var prevKey = args.Length > 1 ? args[1] : LuaNil.Instance;

			LuaValue? foundKey = null;
			LuaValue? foundVal = null;
			bool prevFound = prevKey is LuaNil;

			foreach (var kvp in tbl)
			{
				if (prevFound)
				{
					foundKey = kvp.Key;
					foundVal = kvp.Value;
					break;
				}
				if (kvp.Key.Equals(prevKey))
					prevFound = true;
			}

			if (foundKey is null)
				return new LuaTuple(LuaNil.Instance);
			return new LuaTuple(foundKey, foundVal!);
		}

		private static LuaTuple Select(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				return LuaTuple.Empty;

			if (args[0] is LuaString selector && selector.Value == "#")
				return new LuaTuple(new LuaNumber(args.Length - 1));

			int startIndex;
			if (args[0] is LuaNumber num)
			{
				startIndex = (int)num.Value;
				if (startIndex < 0)
					startIndex = args.Length + startIndex;
			}
			else
			{
				return LuaTuple.Empty;
			}

			if (startIndex < 1 || startIndex > args.Length)
				return LuaTuple.Empty;

			var results = new LuaValue[args.Length - startIndex];
			Array.Copy(args, startIndex, results, 0, results.Length);
			return new LuaTuple(results);
		}
	}
}
