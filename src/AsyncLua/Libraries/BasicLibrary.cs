using System;
using System.Linq;
using System.Threading.Tasks;
using AsyncLua.Parsing.Statements;
using AsyncLua.Values;
using AsyncLua.Compiling;
using AsyncLua.Interpreting;


namespace AsyncLua.Libraries
{
	/// <summary>
	/// Implements the standard Lua global functions: <c>print</c>, <c>type</c>,
	/// <c>tostring</c>, <c>tonumber</c>, <c>error</c>, <c>assert</c>, <c>ipairs</c>,
	/// <c>pairs</c>, <c>next</c>, <c>select</c>, <c>warn</c>, <c>getmetatable</c>,
	/// <c>setmetatable</c>, <c>rawequal</c>, <c>rawlen</c>, <c>rawget</c>, <c>rawset</c>,
	/// <c>pcall</c>, <c>xpcall</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Global helper functions added by AsyncLua: <c>is_async</c>, <c>pcall_async</c>,
	/// <c>xpcall_async</c>.
	/// </para>
	/// <para>
	/// The <c>delay</c> and <c>run</c> functions are now part of the <c>task</c> library.
	/// </para>
	/// </remarks>
	public sealed class BasicLibrary : LuaGlobalBaseLibrary
	{
		protected override void PopulateTable(LuaState state, LuaTable table)
		{
			state.SetGlobal("print", new LuaCallbackFunction(Print, "print"));
			state.SetGlobal("warn", new LuaCallbackFunction(Warn, "warn"));
			state.SetGlobal("error", new LuaCallbackFunction(Error, "error"));
			state.SetGlobal("type", new LuaCallbackFunction(Type, "type"));
			state.SetGlobal("tostring", new LuaCallbackFunction(ToString, "tostring", isAsync: false));
			state.SetGlobal("tonumber", new LuaCallbackFunction(ToNumber, "tonumber", isAsync: false));
			state.SetGlobal("is_async", new LuaCallbackFunction(IsAsync, "is_async"));
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
			state.SetGlobal("rawequal", new LuaCallbackFunction(RawEqual, "rawequal"));
			state.SetGlobal("rawlen", new LuaCallbackFunction(RawLen, "rawlen"));
			state.SetGlobal("rawget", new LuaCallbackFunction(RawGet, "rawget"));
			state.SetGlobal("rawset", new LuaCallbackFunction(RawSet, "rawset"));
			state.SetGlobal("load", new LuaCallbackFunction(Load, "load"));
			state.SetGlobal("dostring", new LuaCallbackFunction(DoString, "dostring"));
			state.SetGlobal("setfenv", new LuaCallbackFunction(SetFenv, "setfenv"));
			state.SetGlobal("getfenv", new LuaCallbackFunction(GetFenv, "getfenv"));

			// Set _VERSION global constant.
			var version = typeof(BasicLibrary).Assembly.GetName().Version.ToString();
			state.SetGlobal("_VERSION", new LuaString($"AsyncLua {version}"));
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

		private static LuaTuple Warn(LuaCallingContext ctx, LuaValue[] args)
		{
			var target = ctx.Warn ?? ctx.Print;
			if (target is null)
				return LuaTuple.Empty;
			var parts = new string[args.Length];
			for (int i = 0; i < args.Length; i++)
				parts[i] = args[i].ToString();
			target.Invoke(string.Join("\t", parts));
			return LuaTuple.Empty;
		}

		private static LuaTuple Error(LuaCallingContext ctx, LuaValue[] args)
		{
			throw new LuaRuntimeException(
				args.Length > 0 ? args[0].ToString() : "error");
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

		private static LuaTuple IsAsync(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaFunction func)
				return new LuaTuple(LuaNil.Instance);
			return new LuaTuple(LuaBoolean.FromBoolean(func.IsAsync));
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
					var idx = (int)((LuaNumber)args2[1]).Value + 1;
					var val = tbl.Get(idx);
					if (val is LuaNil)
						return LuaTuple.Empty;
					return new LuaTuple(new LuaNumber(idx), val);
				}, "ipairs_iter");

			return new LuaTuple(iter, t, LuaNumber.Zero);
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

			return new LuaTuple(
				ctx.State.GetGlobal("next"),
				t,
				LuaNil.Instance);
		}

		private static LuaTuple Next(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaTable tbl)
				return new LuaTuple(LuaNil.Instance);

			var key = args.Length > 1 ? args[1] : LuaNil.Instance;

			if (key is LuaNil)
			{
				// Find the first key.
				foreach (var kv in tbl)
				{
					if (kv.Key is LuaNil) continue;
					return new LuaTuple(kv.Key, kv.Value);
				}
				return new LuaTuple(LuaNil.Instance);
			}

			// Find the key after the given one.
			bool found = false;
			foreach (var kv in tbl)
			{
				if (kv.Key is LuaNil) continue;
				if (found)
					return new LuaTuple(kv.Key, kv.Value);
				if (kv.Key.Equals(key))
					found = true;
			}

			return new LuaTuple(LuaNil.Instance);
		}

		private static LuaTuple Select(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				return LuaTuple.Empty;

			if (args.Length > 0 && args[0] is LuaString str && str.Value == "#")
			{
				return new LuaTuple(new LuaNumber(args.Length - 1));
			}

			var index = (int)((LuaNumber)args[0]).Value;
			int startIndex = index >= 0 ? index : args.Length + index;
			if (startIndex < 1)
				startIndex = 1;
			if (startIndex > args.Length)
				startIndex = args.Length;

			int count = args.Length - startIndex;
			var values = new LuaValue[count];
			for (int i = 0; i < count; i++)
				values[i] = args[startIndex + i];

			return new LuaTuple(values);
		}

		private static LuaTuple GetMetatable(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				return new LuaTuple(LuaNil.Instance);

			var mt = args[0].Metatable;
			if (mt is null)
				return new LuaTuple(LuaNil.Instance);

			// Check for __metatable field.
			if (mt.HasEvent(LuaMetatableEvent.MetaTable))
			{
				var protectedMt = mt.Get(LuaMetatableEvent.MetaTable);
				return new LuaTuple(protectedMt);
			}

			return new LuaTuple(mt.ToTable());
		}

		private static LuaTuple SetMetatable(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length < 2)
				throw new LuaRuntimeException("setmetatable: expected at least 2 arguments, got " + args.Length);

			var obj = args[0];

			// Check if the object has a metatable (or can have one).
			if (obj.Metatable is null)
				throw new LuaRuntimeException("setmetatable: first argument must have a metatable, got " + obj.TypeName);

			// Check if metatable is protected.
			if (obj.Metatable.HasEvent(LuaMetatableEvent.MetaTable))
				throw new LuaRuntimeException("cannot change a protected metatable");

			if (args[1] is LuaNil)
			{
				obj.Metatable = null;
				return new LuaTuple(obj);
			}

			if (args[1] is not LuaTable mtTable)
				throw new LuaRuntimeException("setmetatable: second argument must be a table or nil, got " + args[1].TypeName);

			obj.Metatable = LuaMetatable.FromTable(mtTable);
			return new LuaTuple(obj);
		}

		private static LuaTuple RawEqual(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length < 2)
				return new LuaTuple(LuaBoolean.False);

			return new LuaTuple(LuaBoolean.FromBoolean(args[0].Equals(args[1])));
		}

		private static LuaTuple RawLen(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				throw new LuaRuntimeException("rawlen: expected 1 argument, got 0");

			var value = args[0];

			if (value is LuaTable tbl)
				return new LuaTuple(new LuaNumber(tbl.Length));

			if (value is LuaString str)
				return new LuaTuple(new LuaNumber(str.Value.Length));

			throw new LuaRuntimeException("rawlen: expected a table or string, got " + value.TypeName);
		}

		private static LuaTuple RawGet(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length < 2 || args[0] is not LuaTable tbl)
				throw new LuaRuntimeException("rawget: expected a table as first argument");

			return new LuaTuple(tbl.Get(args[1]));
		}

		private static LuaTuple Load(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				return new LuaTuple(LuaNil.Instance, new LuaString(
					"bad argument #1 to 'load' (string or function expected, got no value)"));

			string chunk;
			if (args[0] is LuaString str)
			{
				chunk = str.Value;
			}
			else if (args[0] is LuaFunction readerFunc)
			{
				// Read chunks from the function until it returns nil.
				var sb = new System.Text.StringBuilder();
				while (true)
				{
					var chunkResult = readerFunc.Invoke(ctx);
					if (chunkResult.Count == 0 || chunkResult[0] is LuaNil)
						break;
					sb.Append(chunkResult[0].ToString());
				}
				chunk = sb.ToString();
			}
			else
			{
				return new LuaTuple(LuaNil.Instance, new LuaString(
					"bad argument #1 to 'load' (string or function expected, got " + args[0].TypeName + ")"));
			}

			// Optional arguments.
			string sourceName = args.Length > 1 ? args[1].ToString() : "=(load)";
			string mode = args.Length > 2 ? args[2].ToString() ?? "bt" : "bt";
			LuaTable? env = args.Length > 3 && args[3] is LuaTable envTable ? envTable : null;

			bool allowBinary = mode.Contains('b');
			bool allowText = mode.Contains('t');

			try
			{
				// Try binary first if allowed and the data looks like bytecode.
				if (allowBinary && chunk.Length >= 4 &&
					chunk[0] == '\x1b' && chunk[1] == 'A' && chunk[2] == 's' && chunk[3] == 'L')
				{
					byte[] data = System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(chunk);
				var prototype = PrototypeSerializer.Deserialize(data);
					var state = ctx.State;

					// Create fresh upvalues (nil) for any upvalue descriptions in the prototype.
					var upvalues = prototype.UpvalueDescriptions.Length > 0
						? Array.ConvertAll(prototype.UpvalueDescriptions, _ => Upvalue.CreateClosed(LuaNil.Instance))
						: Array.Empty<Upvalue>();

					var func = new LuaNativeFunction(prototype, upvalues: upvalues, environment: env);

					return new LuaTuple(func);
				}

				if (!allowText)
					return new LuaTuple(LuaNil.Instance, new LuaString(
						"attempt to load a text chunk (mode is 'b')"));

				// Parse and compile as Lua source code.
				var state2 = ctx.State;
				var block = state2.Parser.Parse(chunk);
				var textProto = AsyncLuaCompiler.Compile(block, state2.CompilerSettings, sourceName: sourceName);
				var func2 = new LuaNativeFunction(textProto, environment: env);

				if (env is not null)
					func2.Environment = env;

				return new LuaTuple(func2);
			}
			catch (Exception ex)
			{
				return new LuaTuple(LuaNil.Instance, new LuaString(ex.Message));
			}
		}

		private static LuaTuple DoString(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				throw new LuaRuntimeException("bad argument #1 to 'dostring' (string expected, got no value)");

			if (args[0] is not LuaString codeStr)
				throw new LuaRuntimeException("bad argument #1 to 'dostring' (string expected, got " + args[0].TypeName + ")");

			string code = codeStr.Value;
			var state = ctx.State;

			try
			{
				var block = state.Parser.Parse(code);
				var prototype = AsyncLuaCompiler.Compile(block, state.CompilerSettings, sourceName: "=(dostring)");
				return AsyncLuaInterpreter.Call(prototype, ctx);
			}
			catch (Exception ex)
			{
				throw new LuaRuntimeException($"dostring: {ex.Message}");
			}
		}

		private static LuaTuple SetFenv(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length < 2)
				throw new LuaRuntimeException("bad argument #1 to 'setfenv' (function or number expected, got no value)");

			if (args[1] is not LuaTable envTable)
				throw new LuaRuntimeException("bad argument #2 to 'setfenv' (table expected, got " + args[1].TypeName + ")");

			if (args[0] is LuaNumber level)
			{
				int stackLevel = (int)level.Value;
				if (stackLevel == 0)
				{
					ctx.Globals = envTable;
					return new LuaTuple(LuaBoolean.True);
				}
				// Stack levels > 0 are not currently supported.
				throw new LuaRuntimeException("setfenv: stack levels above 0 are not supported");
			}
			else if (args[0] is LuaNativeFunction nativeFunc)
			{
				nativeFunc.Environment = envTable;
				return new LuaTuple(LuaBoolean.True);
			}
			else if (args[0] is LuaCallbackFunction)
			{
				throw new LuaRuntimeException("setfenv: cannot change environment of a C function");
			}
			else
			{
				throw new LuaRuntimeException("bad argument #1 to 'setfenv' (function or number expected, got " + args[0].TypeName + ")");
			}
		}

		private static LuaTuple GetFenv(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
			{
				// getfenv() defaults to level 1 (calling function) — return current context globals.
				return new LuaTuple(ctx.Globals);
			}

			if (args[0] is LuaNumber level)
			{
				int stackLevel = (int)level.Value;
				if (stackLevel == 0)
				{
					return new LuaTuple(ctx.Globals);
				}
				// Stack levels > 0 are not currently supported.
				throw new LuaRuntimeException("getfenv: stack levels above 0 are not supported");
			}
			else if (args[0] is LuaNativeFunction nativeFunc)
			{
				return new LuaTuple(nativeFunc.Environment ?? ctx.Globals);
			}
			else if (args[0] is LuaCallbackFunction)
			{
				return new LuaTuple(ctx.Globals);
			}
			else
			{
				throw new LuaRuntimeException("bad argument #1 to 'getfenv' (function or number expected, got " + args[0].TypeName + ")");
			}
		}


		private static LuaTuple RawSet(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length < 3 || args[0] is not LuaTable tbl)
				throw new LuaRuntimeException("rawset: expected a table as first argument");

			tbl.Set(args[1], args[2]);
			return new LuaTuple(tbl);
		}
	}
}
