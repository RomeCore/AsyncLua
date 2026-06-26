using System;
using System.Linq;
using System.Threading.Tasks;
using AsyncLua.Parsing.Statements;
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
			state.SetGlobal("print", new LuaCallbackFunction(Print, "print"));
			state.SetGlobal("type", new LuaCallbackFunction(Type, "type"));
			state.SetGlobal("tostring", new LuaCallbackFunction(ToString, "tostring", isAsync: false));
			state.SetGlobal("tonumber", new LuaCallbackFunction(ToNumber, "tonumber", isAsync: false));
			state.SetGlobal("run", new LuaCallbackFunction(Run, "run"));
			state.SetGlobal("is_async", new LuaCallbackFunction(IsAsync, "is_async"));
			state.SetGlobal("error", new LuaCallbackFunction(Error, "error"));
			state.SetGlobal("assert", new LuaCallbackFunction(Assert, "assert"));
			state.SetGlobal("pcall", new LuaCallbackFunction(Pcall, "pcall", isAsync: false));
			state.SetGlobal("pcall_async", new LuaCallbackFunction(Pcall, "pcall_async", isAsync: true));
			state.SetGlobal("xpcall", new LuaCallbackFunction(Xpcall, "xpcall", isAsync: false));
			state.SetGlobal("xpcall_async", new LuaCallbackFunction(Xpcall, "xpcall_async", isAsync: true));
			state.SetGlobal("ipairs", new LuaCallbackFunction(Ipairs, "ipairs"));
			state.SetGlobal("pairs", new LuaCallbackFunction(Pairs, "pairs"));
			state.SetGlobal("next", new LuaCallbackFunction(Next, "next"));
			state.SetGlobal("select", new LuaCallbackFunction(Select, "select"));
			state.SetGlobal("getmetatable", new LuaCallbackFunction(GetMetatable, "getmetatable"));
			state.SetGlobal("setmetatable", new LuaCallbackFunction(SetMetatable, "setmetatable"));
		}

		private static LuaTuple Print(LuaCallingContext ctx, LuaValue[] args)
		{
			if (ctx.Print is null)
				return LuaTuple.Empty;
			var parts = new string[args.Length];
			for (int i = 0; i < args.Length; i++)
				parts[i] = args[i].ToString();
			ctx.Print?.Invoke(string.Join("\t", parts));
			return LuaTuple.Empty;
		}

		private static LuaTuple Type(LuaCallingContext ctx, LuaValue[] args)
		{
			var typeName = args.Length == 0 ? "nil" : args[0].TypeName;
			return new LuaTuple(new LuaString(typeName));
		}

		private static async Task<LuaTuple> ToString(LuaCallingContext ctx, LuaValue[] args)
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
					return await func.InvokeAsync(ctx, new[] { value });
			}

			if (ctx.State.TypeMetatables.TryGetValue(value.Type, out var typeMtToString)
				&& typeMtToString.HasEvent(LuaMetatableEvent.ToString))
			{
				var handler = typeMtToString.Get(LuaMetatableEvent.ToString);
				if (handler is LuaFunction func)
					return await func.InvokeAsync(ctx, new[] { value });
			}

			return new LuaTuple(new LuaString(value.ToString()));
		}

		private static async Task<LuaTuple> ToNumber(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				throw new LuaRuntimeException("tonumber: expected at least 1 argument, got 0");

			var value = args[0];

			// Check for __tonumber metamethod (individual metatable first).
			var mt = value.Metatable;
			if (mt != null && mt.HasEvent(LuaMetatableEvent.ToNumber))
			{
				var handler = mt.Get(LuaMetatableEvent.ToNumber);
				if (handler is LuaFunction func)
					return await func.InvokeAsync(ctx, new[] { value });
			}

			// Then type metatable.
			if (ctx.State.TypeMetatables.TryGetValue(value.Type, out var typeMtToNumber)
				&& typeMtToNumber.HasEvent(LuaMetatableEvent.ToNumber))
			{
				var handler = typeMtToNumber.Get(LuaMetatableEvent.ToNumber);
				if (handler is LuaFunction func)
					return await func.InvokeAsync(ctx, new[] { value });
			}

			if (value.TryToNumber(out var number))
				return new LuaTuple(new LuaNumber(number));

			throw new LuaRuntimeException("tonumber: cannot convert to number");
		}

		private static Task<LuaTuple> Run(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaFunction func)
				throw new LuaRuntimeException("run: expected a function as argument");

			return Task.Run(async () =>
			{
				return await func.InvokeAsync(ctx, args.Skip(1).ToArray());
			});
		}

		private static LuaTuple IsAsync(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaFunction func)
				return new LuaTuple(LuaNil.Instance);
			return new LuaTuple(LuaBoolean.FromBoolean(func.IsAsync));
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

		private static async Task<LuaTuple> Pcall(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaFunction func)
				throw new LuaRuntimeException("pcall: expected function as first argument, got " + (args.Length > 0 ? args[0].TypeName : "nil"));
			
			try
			{
				var result = await func.InvokeAsync(ctx, args.Skip(1).ToArray());
				return new LuaTuple(result.Prepend(LuaBoolean.True));
			}
			catch (LuaRuntimeException ex)
			{
				return new LuaTuple(LuaBoolean.False, new LuaString(ex.OriginalMessage));
			}
		}

		private static async Task<LuaTuple> Xpcall(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaFunction func)
				throw new LuaRuntimeException("pcall: expected function as first argument, got " + (args.Length > 0 ? args[0].TypeName : "nil"));
			if (args.Length == 1 || args[1] is not LuaFunction errHandlerFunc)
				throw new LuaRuntimeException("pcall: expected error handler function as second argument, got " + (args.Length > 1 ? args[1].TypeName : "nil"));

			try
			{
				var result = await func.InvokeAsync(ctx, args.Skip(2).ToArray());
				return new LuaTuple(result.Prepend(LuaBoolean.True));
			}
			catch (LuaRuntimeException ex)
			{
				var errHandlerResult = await errHandlerFunc.InvokeAsync(ctx, new LuaString(ex.OriginalMessage));
				return new LuaTuple(errHandlerResult.Prepend(LuaBoolean.False));
			}
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
			if (args.Length == 0)
				return new LuaTuple(LuaNil.Instance);

			var target = args[0];

			// Check for __pairs metamethod (Lua 5.2+ semantics).
			var mt = target.Metatable;
			if (mt != null && mt.HasEvent(LuaMetatableEvent.Pairs))
			{
				var handler = mt.Get(LuaMetatableEvent.Pairs);
				if (handler is LuaFunction func)
					return func.Invoke(ctx, new[] { target });
			}

			// Type metatables (per-type shared metatables).
			if (mt == null && ctx.State.TypeMetatables.TryGetValue(target.Type, out var typeMt)
				&& typeMt.HasEvent(LuaMetatableEvent.Pairs))
			{
				var handler = typeMt.Get(LuaMetatableEvent.Pairs);
				if (handler is LuaFunction func)
					return func.Invoke(ctx, new[] { target });
			}

			if (target is not LuaTable t)
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

		private static LuaTuple GetMetatable(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				throw new LuaRuntimeException("getmetatable: expected at least 1 argument, got 0");

			var obj = args[0];
			if (obj.Metatable != null)
			{
				if (obj.Metatable.HasEvent(LuaMetatableEvent.MetaTable))
					return new LuaTuple(obj.Metatable[LuaMetatableEvent.MetaTable]);
				return new LuaTuple(obj.Metatable.ToTable());
			}

			return new LuaTuple(LuaNil.Instance);
		}

		private static LuaTuple SetMetatable(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length < 2)
				throw new LuaRuntimeException("getmetatable: expected at least 2 arguments, got 0");

			var obj = args[0];
			if (obj.Metatable != null)
			{
				if (obj.Metatable.HasEvent(LuaMetatableEvent.MetaTable))
					throw new LuaRuntimeException("setmetatable: cannot change a protected metatable");
				if (args[1] is LuaNil)
				{
					obj.Metatable = null;
					return LuaTuple.Empty;
				}
				if (args[1] is not LuaTable table)
					throw new LuaRuntimeException("setmetatable: expected second argument to be a table, got " + args[1].TypeName);
				obj.Metatable = LuaMetatable.FromTable(table);
			}

			return LuaTuple.Empty;
		}
	}
}
