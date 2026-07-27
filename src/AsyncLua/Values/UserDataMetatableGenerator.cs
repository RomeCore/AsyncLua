using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace AsyncLua.Values
{
	/// <summary>
	/// Generates Lua metatables for CLR types dynamically via reflection, enabling
	/// seamless interop between Lua and .NET objects wrapped in <see cref="LuaUserData"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The generator creates a <see cref="LuaMetatable"/> that exposes public members
	/// (properties, fields, methods, events) of a .NET type to Lua. The metatable is
	/// cached per <see cref="Type"/> so that reflection is performed only once.
	/// </para>
	/// <para>
	/// Supported member kinds for <c>__index</c>:
	/// <list type="bullet">
	///   <item><description>Properties (read/write respect <c>__newindex</c>)</description></item>
	///   <item><description>Fields</description></item>
	///   <item><description>Methods (with overload resolution, <c>params</c>, <c>ref</c>/<c>out</c>)</description></item>
	///   <item><description>Events (returned as a callable that registers/unregisters handlers)</description></item>
	/// </list>
	/// </para>
	/// <para>
	/// Additional metamethods are generated automatically when the CLR type supports them:
	/// <c>__call</c> (for delegates / <c>Func&lt;&gt;</c> / <c>Action&lt;&gt;</c>),
	/// <c>__tostring</c>, <c>__len</c> (for <c>Count</c> / <c>Length</c> properties),
	/// <c>__pairs</c> (for <see cref="IEnumerable"/>), <c>__name</c>.
	/// </para>
	/// </remarks>
	public static class UserDataMetatableGenerator
	{
		private static readonly ConcurrentDictionary<Type, LuaMetatable> _cache = new();

		// ── Public API ────────────────────────────────────────────────────

		/// <summary>
		/// Gets or creates a cached metatable for the specified CLR type.
		/// </summary>
		/// <param name="type">The CLR type to generate a metatable for.</param>
		/// <returns>A <see cref="LuaMetatable"/> that exposes the type's public members.</returns>
		public static LuaMetatable GetOrCreate(Type type) =>
			_cache.GetOrAdd(type, CreateMetatable);

		/// <summary>
		/// Gets or creates a cached metatable for the type of the specified object.
		/// </summary>
		/// <param name="userData">The userdata whose CLR type is used for lookup.</param>
		/// <returns>A <see cref="LuaMetatable"/> that exposes the type's public members.</returns>
		public static LuaMetatable GetOrCreate(LuaUserData userData) =>
			GetOrCreate(userData.Target.GetType());

		/// <summary>
		/// Creates a metatable for the specified CLR type (always creates a new one; does not cache).
		/// </summary>
		/// <param name="type">The CLR type to generate a metatable for.</param>
		/// <returns>A <see cref="LuaMetatable"/> that exposes the type's public members.</returns>
		public static LuaMetatable CreateMetatable(Type type)
		{
			var mt = new LuaMetatable();

			mt.Set(LuaMetatableEvent.Name, new LuaString(type.FullName ?? type.Name));
			mt.Set(LuaMetatableEvent.ToString, CreateToStringFunction(type));
			mt.Set(LuaMetatableEvent.Index, CreateIndexFunction(type));
			mt.Set(LuaMetatableEvent.NewIndex, CreateNewIndexFunction(type));
			mt.Set(LuaMetatableEvent.MetaTable, new LuaString("This userdata metatable is protected."));

			var callMethod = FindCallMethod(type);
			if (callMethod != null)
				mt.Set(LuaMetatableEvent.Call, CreateCallFunction(callMethod));

			var lenFunc = CreateLenFunction(type);
			if (lenFunc != null)
				mt.Set(LuaMetatableEvent.Len, lenFunc);

			if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
				mt.Set(LuaMetatableEvent.Pairs, CreatePairsFunction());

			return mt;
		}

		/// <summary>
		/// Wraps a CLR object in a <see cref="LuaUserData"/> with an auto-generated metatable
		/// and attaches it to the specified global name in the Lua state.
		/// </summary>
		/// <param name="state">The Lua state to register into.</param>
		/// <param name="name">The global name under which to expose the object.</param>
		/// <param name="target">The CLR object to wrap.</param>
		/// <param name="type">Optional: the CLR type to use for metatable generation. If <see langword="null"/>, uses <c>target.GetType()</c>.</param>
		/// <returns>The created <see cref="LuaUserData"/> instance.</returns>
		public static LuaUserData RegisterObject(LuaState state, string name, object target, Type? type = null)
		{
			var ud = new LuaUserData(target, target.GetType().Name);
			ud.Metatable = GetOrCreate(type ?? target.GetType());
			state.SetGlobal(name, ud);
			return ud;
		}

		/// <summary>
		/// Wraps a CLR object in a <see cref="LuaUserData"/> with an auto-generated metatable
		/// and adds it to the specified parent table.
		/// </summary>
		/// <param name="table">The Lua table to add the object to.</param>
		/// <param name="key">The key under which to store the object.</param>
		/// <param name="target">The CLR object to wrap.</param>
		/// <param name="type">Optional: the CLR type to use for metatable generation. If <see langword="null"/>, uses <c>target.GetType()</c>.</param>
		/// <returns>The created <see cref="LuaUserData"/> instance.</returns>
		public static LuaUserData RegisterObject(LuaTable table, LuaValue key, object target, Type? type = null)
		{
			var ud = new LuaUserData(target, target.GetType().Name);
			ud.Metatable = GetOrCreate(type ?? target.GetType());
			table.Set(key, ud);
			return ud;
		}

		/// <summary>
		/// Clears the cached metatables, forcing regeneration on next access.
		/// </summary>
		public static void ClearCache() => _cache.Clear();

		// ── __tostring ────────────────────────────────────────────────────

		private static LuaValue CreateToStringFunction(Type type)
		{
			return new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length == 0 || args[0] is not LuaUserData ud)
						return new LuaTuple(new LuaString(type.FullName ?? type.Name));
					return new LuaTuple(new LuaString(ud.Target.ToString() ?? ""));
				},
				"__tostring");
		}

		// ── Member name mapping ─────────────────────────────────────────────

		/// <summary>
		/// Normalises a member name for fuzzy matching: strips <c>'_'</c> and <c>'-'</c>
		/// and converts to lowercase. This enables callers to use snake_case, kebab-case,
		/// PascalCase, camelCase, or any mix thereof — they all map to the same normalised key.
		/// </summary>
		/// <example>
		/// <c>"Rotate180"</c>, <c>"rotate_180"</c>, <c>"ROTATE_180"</c> all normalise to <c>"rotate180"</c>.
		/// </example>
		private static string NormaliseName(string name)
		{
			var sb = new System.Text.StringBuilder(name.Length);
			foreach (char c in name)
			{
				if (c != '_' && c != '-')
					sb.Append(char.ToLowerInvariant(c));
			}
			return sb.ToString();
		}

		/// <summary>
		/// Builds a case-insensitive member lookup dictionary keyed by normalised names
		/// (lowercased, with <c>'_'</c> and <c>'-'</c> stripped). Filters members by
		/// <see cref="LuaHiddenAttribute"/> and <see cref="LuaVisibleAttribute"/>.
		/// </summary>
		private static Dictionary<string, MemberInfo[]> BuildMemberLookup(Type type, bool includeNonPublic = false)
		{
			var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
			if (includeNonPublic)
				flags |= BindingFlags.NonPublic;

			var members = type.GetMembers(flags)
				.Where(m => IsMemberVisible(m))
				.ToArray();

			var lookup = new Dictionary<string, MemberInfo[]>(StringComparer.Ordinal);

			foreach (var member in members)
			{
				var normalised = NormaliseName(member.Name);

				if (lookup.TryGetValue(normalised, out var existing))
				{
					var newArr = new MemberInfo[existing.Length + 1];
					Array.Copy(existing, newArr, existing.Length);
					newArr[existing.Length] = member;
					lookup[normalised] = newArr;
				}
				else
				{
					lookup[normalised] = new[] { member };
				}
			}

			return lookup;
		}

		/// <summary>
		/// Determines whether a member is visible to Lua.
		/// A member is visible if:
		/// - It does NOT have <see cref="LuaHiddenAttribute"/>, AND
		/// - It either is public OR has <see cref="LuaVisibleAttribute"/>.
		/// </summary>
		private static bool IsMemberVisible(MemberInfo member)
		{
			if (member.GetCustomAttribute<LuaHiddenAttribute>() != null)
				return false;

			if (member is PropertyInfo prop)
			{
				// Properties: consider the getter/setter accessibility.
				var getMethod = prop.GetMethod;
				var setMethod = prop.SetMethod;
				bool hasPublicAccessor = (getMethod != null && getMethod.IsPublic)
				                      || (setMethod != null && setMethod.IsPublic);
				if (hasPublicAccessor)
					return true;
			}

			// For methods, fields, events — check public or [LuaVisible].
			if (member is MethodBase mb && mb.IsPublic)
				return true;
			if (member is FieldInfo fi && fi.IsPublic)
				return true;
			if (member is EventInfo ei && ei.AddMethod != null && ei.AddMethod.IsPublic)
				return true;

			// Non-public with [LuaVisible].
			if (member.GetCustomAttribute<LuaVisibleAttribute>() != null)
				return true;

			return false;
		}

		// ── __index ──────────────────────────────────────────────────────

		private static LuaValue CreateIndexFunction(Type type)
		{
			var lookup = BuildMemberLookup(type, includeNonPublic: true);

			return new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 2 || args[0] is not LuaUserData ud)
						return LuaTuple.Empty;

					var key = args[1];
					if (key is not LuaString keyStr)
						return LuaTuple.Empty;

					var luaName = keyStr.Value;
					if (!lookup.TryGetValue(NormaliseName(luaName), out var memberGroup))
						return LuaTuple.Empty;

					var target = ud.Target;

					// Properties (read).
					foreach (var member in memberGroup)
					{
						if (member is PropertyInfo prop && prop.CanRead)
						{
							try
							{
								var propValue = prop.GetValue(prop.GetMethod!.IsStatic ? null : target);
								var converted = LuaValueConverter.ToLuaValue(propValue);
								return converted is LuaTuple t ? t : new LuaTuple(converted);
							}
							catch (Exception ex)
							{
								throw new LuaRuntimeException($"Error reading property '{luaName}': {ex.Message}");
							}
						}
					}

					// Fields.
					foreach (var member in memberGroup)
					{
						if (member is FieldInfo field)
						{
							try
							{
								var fieldValue = field.GetValue(field.IsStatic ? null : target);
								var converted = LuaValueConverter.ToLuaValue(fieldValue);
								return converted is LuaTuple t ? t : new LuaTuple(converted);
							}
							catch (Exception ex)
							{
								throw new LuaRuntimeException($"Error reading field '{luaName}': {ex.Message}");
							}
						}
					}

					// Events.
					foreach (var member in memberGroup)
					{
						if (member is EventInfo evt)
							return new LuaTuple(CreateEventAccessor(evt, target));
					}

					// Methods.
					var methods = memberGroup.OfType<MethodInfo>().ToArray();
					if (methods.Length > 0)
						return new LuaTuple(CreateMethodDispatcher(methods, target));

					return LuaTuple.Empty;
				},
				"__index");
		}

		// ── __newindex ──────────────────────────────────────────────────

		private static LuaValue CreateNewIndexFunction(Type type)
		{
			var lookup = BuildMemberLookup(type, includeNonPublic: true);

			return new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 3 || args[0] is not LuaUserData ud)
						return LuaTuple.Empty;

					var key = args[1];
					if (key is not LuaString keyStr)
						return LuaTuple.Empty;

					var value = args[2];
					var luaName = keyStr.Value;

					if (!lookup.TryGetValue(NormaliseName(luaName), out var memberGroup))
						return LuaTuple.Empty;

					var target = ud.Target;

					foreach (var member in memberGroup)
					{
						if (member is PropertyInfo prop && prop.CanWrite)
						{
							try
							{
								var converted = LuaValueConverter.ToClrObject(value, prop.PropertyType);
								prop.SetValue(prop.SetMethod!.IsStatic ? null : target, converted);
								return LuaTuple.Empty;
							}
							catch (Exception ex)
							{
								throw new LuaRuntimeException($"Error setting property '{luaName}': {ex.Message}");
							}
						}

						if (member is FieldInfo field)
						{
							try
							{
								var converted = LuaValueConverter.ToClrObject(value, field.FieldType);
								field.SetValue(field.IsStatic ? null : target, converted);
								return LuaTuple.Empty;
							}
							catch (Exception ex)
							{
								throw new LuaRuntimeException($"Error setting field '{luaName}': {ex.Message}");
							}
						}
					}

					return LuaTuple.Empty;
				},
				"__newindex");
		}

		// ── __call ───────────────────────────────────────────────────────

		private static MethodInfo? FindCallMethod(Type type)
		{
			if (typeof(Delegate).IsAssignableFrom(type))
				return type.GetMethod("Invoke");

			var invoke = type.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
			if (invoke != null)
				return invoke;

			return null;
		}

		private static LuaValue CreateCallFunction(MethodInfo invokeMethod)
		{
			var parameters = invokeMethod.GetParameters();
			var returnType = invokeMethod.ReturnType;
			var isAsyncTask = returnType == typeof(Task)
				|| (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>));
			var returnsVoid = returnType == typeof(void) || returnType == typeof(Task);

			if (isAsyncTask)
			{
				return CreateAsyncCallFunction(invokeMethod, parameters, returnsVoid);
			}

			return CreateSyncCallFunction(invokeMethod, parameters, returnsVoid);
		}

		private static LuaValue CreateSyncCallFunction(MethodInfo method, ParameterInfo[] parameters, bool returnsVoid)
		{
			return new LuaCallbackFunction((ctx, args) =>
			{
				try
				{
					var ud = args.Length > 0 ? args[0] as LuaUserData : null;
					var target = ud?.Target;
					var callArgs = UserDataOverloadResolver.PrepareCallArguments(ctx, method, parameters, args, ud != null ? 1 : 0);

					var result = method.Invoke(target, callArgs);
					if (returnsVoid)
						return LuaTuple.Empty;

					var converted = LuaValueConverter.ToLuaValue(result);
					return converted is LuaTuple t ? t : new LuaTuple(converted);
				}
				catch (TargetInvocationException tie)
				{
					throw new LuaRuntimeException($"Call error: {tie.InnerException?.Message ?? tie.Message}");
				}
				catch (Exception ex)
				{
					throw new LuaRuntimeException($"Call error: {ex.Message}");
				}
			}, method.Name, isAsync: false);
		}

		private static LuaValue CreateAsyncCallFunction(MethodInfo method, ParameterInfo[] parameters, bool returnsVoid)
		{
			return new LuaCallbackFunction(async (ctx, args) =>
			{
				try
				{
					var ud = args.Length > 0 ? args[0] as LuaUserData : null;
					var target = ud?.Target;
					var callArgs = UserDataOverloadResolver.PrepareCallArguments(ctx, method, parameters, args, ud != null ? 1 : 0);

					var result = method.Invoke(target, callArgs);

					if (result is Task task)
					{
						await task.ConfigureAwait(false);
						if (returnsVoid)
							return LuaTuple.Empty;

						var resultType = result.GetType();
						if (resultType.IsGenericType)
						{
							var resultValue = resultType.GetProperty("Result")?.GetValue(result);
							var converted = LuaValueConverter.ToLuaValue(resultValue);
							return converted is LuaTuple t ? t : new LuaTuple(converted);
						}

						return LuaTuple.Empty;
					}

					return LuaTuple.Empty;
				}
				catch (TargetInvocationException tie)
				{
					throw new LuaRuntimeException($"Call error: {tie.InnerException?.Message ?? tie.Message}");
				}
				catch (Exception ex)
				{
					throw new LuaRuntimeException($"Call error: {ex.Message}");
				}
			}, method.Name, isAsync: true);
		}

		// ── Method dispatcher ────────────────────────────────────────────

		private static bool IsAsyncMethod(MethodInfo method)
		{
			var t = method.ReturnType;
			return t == typeof(Task) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>));
		}

		private static LuaValue CreateMethodDispatcher(MethodInfo[] methods, object? staticTarget)
		{
			if (methods.Length == 1)
				return CreateCallFunction(methods[0]);

			bool anyAsync = methods.Any(IsAsyncMethod);

			if (anyAsync)
			{
				return new LuaCallbackFunction(async (ctx, args) =>
				{
					var ud = args.Length > 0 ? args[0] as LuaUserData : null;
					var target = ud?.Target ?? staticTarget;

					int argOffset = (ud != null && args.Length > 0 && args[0] is LuaUserData) ? 1 : 0;
					var providedArgs = args.Skip(argOffset).ToArray();

					var match = UserDataOverloadResolver.ResolveOverload(methods, providedArgs, out _);
					if (match == null)
					{
						var sigs = string.Join(", ", methods.Select(m =>
							$"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})"));
						throw new LuaRuntimeException(
							$"No matching overload for '{methods[0].Name}' with {providedArgs.Length} argument(s).\n" +
							$"Available: {sigs}");
					}

					var callFunc = CreateCallFunction(match);
					if (callFunc is LuaCallbackFunction lcf)
					{
						if (lcf.IsAsync)
							return await lcf.InvokeAsync(ctx, args);
						else
							return lcf.Invoke(ctx, args);
					}

					return LuaTuple.Empty;
				},
				methods[0].Name,
				isAsync: true);
			}
			else
			{
				return new LuaCallbackFunction((ctx, args) =>
				{
					var ud = args.Length > 0 ? args[0] as LuaUserData : null;
					var target = ud?.Target ?? staticTarget;

					int argOffset = (ud != null && args.Length > 0 && args[0] is LuaUserData) ? 1 : 0;
					var providedArgs = args.Skip(argOffset).ToArray();

					var match = UserDataOverloadResolver.ResolveOverload(methods, providedArgs, out _);
					if (match == null)
					{
						var sigs = string.Join(", ", methods.Select(m =>
							$"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})"));
						throw new LuaRuntimeException(
							$"No matching overload for '{methods[0].Name}' with {providedArgs.Length} argument(s).\n" +
							$"Available: {sigs}");
					}

					var callFunc = CreateCallFunction(match);
					if (callFunc is LuaCallbackFunction lcf)
						return lcf.Invoke(ctx, args);

					return LuaTuple.Empty;
				},
				methods[0].Name);
			}
		}

		// ── Event accessor ───────────────────────────────────────────────

		private static LuaValue CreateEventAccessor(EventInfo evt, object target)
		{
			return new LuaCallbackFunction((ctx, args) =>
			{
				if (args.Length < 2 || args[1] is not LuaFunction handler)
					throw new LuaRuntimeException("Event handler must be a function.");

				bool add = args.Length < 3 || args[2].ToBoolean();
				var eventType = evt.EventHandlerType;
				Delegate d = CreateLuaDelegate(eventType, ctx, handler);

				try
				{
					if (add)
						evt.AddMethod!.Invoke(target, new object[] { d });
					else
						evt.RemoveMethod!.Invoke(target, new object[] { d });
				}
				catch (Exception ex)
				{
					throw new LuaRuntimeException($"Event error: {ex.Message}");
				}

				return LuaTuple.Empty;
			},
			evt.Name);
		}

		private static Delegate CreateLuaDelegate(Type delegateType, LuaCallingContext ctx, LuaFunction handler)
		{
			var method = delegateType.GetMethod("Invoke")!;
			return Delegate.CreateDelegate(
				delegateType,
				new LuaDelegateTarget(ctx, handler, method.ReturnType),
				nameof(LuaDelegateTarget.Invoke));
		}

		private sealed class LuaDelegateTarget
		{
			private readonly LuaCallingContext _ctx;
			private readonly LuaFunction _handler;

			public LuaDelegateTarget(LuaCallingContext ctx, LuaFunction handler, Type returnType)
			{
				_ctx = ctx;
				_handler = handler;
			}

#pragma warning disable IDE0060
			public void Invoke(params object?[] args)
#pragma warning restore IDE0060
			{
				var luaArgs = args.Select(a => LuaValueConverter.ToLuaValue(a)).ToArray();
				_handler.InvokeAsync(_ctx, luaArgs).GetAwaiter().GetResult();
			}
		}

		// ── __len ────────────────────────────────────────────────────────

		private static LuaValue? CreateLenFunction(Type type)
		{
			var countProp = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.FirstOrDefault(p => (p.Name == "Count" || p.Name == "Length") && p.CanRead && p.PropertyType == typeof(int));

			if (countProp == null)
				return null;

			return new LuaCallbackFunction((ctx, args) =>
			{
				if (args.Length == 0 || args[0] is not LuaUserData ud)
					return new LuaTuple(new LuaNumber(0));

				try
				{
					var val = countProp.GetValue(ud.Target);
					return new LuaTuple(new LuaNumber(val is int i ? i : 0));
				}
				catch
				{
					return new LuaTuple(new LuaNumber(0));
				}
			},
			"__len");
		}

		// ── __pairs ──────────────────────────────────────────────────────

		private static LuaValue CreatePairsFunction()
		{
			return new LuaCallbackFunction((ctx, args) =>
			{
				if (args.Length == 0 || args[0] is not LuaUserData ud)
					return LuaTuple.Empty;

				var enumerable = ud.Target as IEnumerable;
				if (enumerable == null)
					return LuaTuple.Empty;

				var iter = new LuaCallbackFunction(
					(ctx2, args2) =>
					{
						var ud2 = args2[0] as LuaUserData;
						var prevIdx = args2[1] as LuaNumber;

						if (ud2?.Target is not IEnumerable en)
							return new LuaTuple(LuaNil.Instance);

						var enumerator = en.GetEnumerator();

						int start = prevIdx != null ? (int)prevIdx.Value : 0;
						for (int i = 0; i < start; i++)
						{
							if (!enumerator.MoveNext())
							{
								TryDispose(enumerator);
								return new LuaTuple(LuaNil.Instance);
							}
						}

						if (enumerator.MoveNext())
						{
							var key = new LuaNumber(start + 1);
							var val = LuaValueConverter.ToLuaValue(enumerator.Current);
							TryDispose(enumerator);
							return new LuaTuple(key, val);
						}

						TryDispose(enumerator);
						return new LuaTuple(LuaNil.Instance);
					},
					"pairs_iter");

				return new LuaTuple(iter, ud, LuaNil.Instance);
			},
			"__pairs");
		}

		private static void TryDispose(IEnumerator enumerator)
		{
			if (enumerator is IDisposable disposable)
				disposable.Dispose();
		}
	}
}
