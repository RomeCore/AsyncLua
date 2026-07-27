using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace AsyncLua.Values
{
	/// <summary>
	/// Resolves CLR method overloads based on Lua argument types.
	/// </summary>
	public static class UserDataOverloadResolver
	{
		/// <summary>
		/// Types that are injected automatically from <see cref="LuaCallingContext"/>
		/// and should be ignored during overload resolution argument matching.
		/// </summary>
		private static readonly HashSet<Type> HiddenParameterTypes = new()
		{
			typeof(LuaCallingContext),
			typeof(CancellationToken)
		};

		/// <summary>
		/// Determines whether the specified CLR type is a hidden parameter type
		/// that is injected automatically from <see cref="LuaCallingContext"/>.
		/// </summary>
		/// <param name="type">The CLR type to check.</param>
		/// <returns>
		/// <see langword="true"/> if <paramref name="type"/> is a hidden parameter type;
		/// otherwise, <see langword="false"/>.
		/// </returns>
		public static bool IsHiddenParameter(Type type)
		{
			return HiddenParameterTypes.Contains(type);
		}

		/// <summary>
		/// Selects the best matching overload from a set of methods, given the provided Lua arguments.
		/// Hidden parameters (<see cref="LuaCallingContext"/> and <see cref="CancellationToken"/>)
		/// are automatically excluded from matching.
		/// </summary>
		/// <param name="methods">The candidate methods (same name, different signatures).</param>
		/// <param name="args">The Lua arguments passed from the script.</param>
		/// <param name="bestScore">The compatibility score of the best match (lower is better).</param>
		/// <returns>The best matching method, or <see langword="null"/> if none is compatible.</returns>
		public static MethodInfo? ResolveOverload(MethodInfo[] methods, LuaValue[] args, out int bestScore)
		{
			bestScore = int.MaxValue;
			MethodInfo? best = null;

			foreach (var method in methods)
			{
				var allPars = method.GetParameters();
				var explicitParams = allPars.Where(p => !IsHiddenParameter(p.ParameterType)).ToArray();
				int score = 0;

				int explicitParamCount = explicitParams.Length;
				bool hasParams = false;
				if (explicitParams.Length > 0 && explicitParams[explicitParams.Length - 1]
					.GetCustomAttributes(typeof(ParamArrayAttribute), false).Any())
				{
					hasParams = true;
					explicitParamCount = explicitParams.Length - 1;
				}

				var required = explicitParams.Count(p => !p.IsOptional
					&& !p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Any());

				if (args.Length < required)
					continue;
				if (!hasParams && args.Length > explicitParams.Length)
					continue;

				bool compatible = true;
				for (int i = 0; i < args.Length; i++)
				{
					Type targetType;

					if (i < explicitParamCount)
					{
						targetType = explicitParams[i].ParameterType;
					}
					else if (hasParams)
					{
						targetType = explicitParams[explicitParams.Length - 1].ParameterType.GetElementType()!;
					}
					else
					{
						compatible = false;
						break;
					}

					if (!IsCompatible(args[i], targetType, out var matchCost))
					{
						compatible = false;
						break;
					}

					score += matchCost;

					// Small penalty for params-mapped arguments.
					if (i >= explicitParamCount)
						score += 1;
				}

				if (!compatible)
					continue;

				// Penalty for unused optional explicit parameters.
				if (args.Length < explicitParams.Length && !hasParams)
					score += (explicitParams.Length - args.Length) * 2;

				// Bonus for methods accepting hidden parameters (prefer methods
				// that can receive LuaCallingContext / CancellationToken).
				int hiddenCount = allPars.Length - explicitParams.Length;
				score -= hiddenCount * 2;

				if (score < bestScore)
				{
					bestScore = score;
					best = method;
				}
			}

			return best;
		}

		/// <summary>
		/// Determines whether a Lua value is compatible with a target CLR parameter type,
		/// and assigns a relative cost (lower = better match).
		/// </summary>
		public static bool IsCompatible(LuaValue value, Type targetType, out int cost)
		{
			cost = 10;

			// Hidden parameters are compatible with any Lua value (or none at all)
			// since they are injected automatically.
			if (IsHiddenParameter(targetType))
			{
				cost = 0;
				return true;
			}

			if (value is LuaNil)
			{
				cost = !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null ? 1 : 100;
				return cost < 50;
			}

			if (value is LuaBoolean)
			{
				if (targetType == typeof(bool) || targetType == typeof(object)) { cost = 1; return true; }
				if (targetType == typeof(string)) { cost = 5; return true; }
				return false;
			}

			if (value is LuaNumber)
			{
				if (targetType == typeof(double) || targetType == typeof(float) || targetType == typeof(object)) { cost = 1; return true; }
				if (targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(short)) { cost = 2; return true; }
				if (targetType == typeof(decimal)) { cost = 3; return true; }
				if (targetType == typeof(string)) { cost = 5; return true; }
				if (targetType.IsEnum) { cost = 3; return true; }
				return false;
			}

			if (value is LuaString)
			{
				if (targetType == typeof(string) || targetType == typeof(object)) { cost = 1; return true; }
				if (targetType.IsEnum) { cost = 3; return true; }
				if (targetType == typeof(char)) { cost = 2; return true; }
				if (targetType == typeof(double) || targetType == typeof(int) || targetType == typeof(long)) { cost = 5; return true; }
				return false;
			}

			if (value is LuaUserData ud)
			{
				var actualType = ud.Target.GetType();
				if (targetType.IsAssignableFrom(actualType)) { cost = 1; return true; }
				if (targetType == typeof(object)) { cost = 2; return true; }
				return false;
			}

			if (value is LuaTable)
			{
				if (targetType == typeof(object) || targetType.IsArray) { cost = 3; return true; }
				if (targetType == typeof(LuaTable)) { cost = 1; return true; }
				if (targetType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(targetType)) { cost = 5; return true; }
				return false;
			}

			if (value is LuaFunction)
			{
				if (targetType == typeof(object) || targetType == typeof(LuaFunction)) { cost = 1; return true; }
				if (typeof(Delegate).IsAssignableFrom(targetType)) { cost = 5; return true; }
				return false;
			}

			return targetType == typeof(object);
		}

		/// <summary>
		/// Prepares the CLR argument array for a method invocation,
		/// converting Lua values to the appropriate parameter types.
		/// Hidden parameters (<see cref="LuaCallingContext"/> and <see cref="CancellationToken"/>)
		/// are injected automatically and do not consume Lua arguments.
		/// </summary>
		/// <param name="ctx">The Lua calling context.</param>
		/// <param name="method">The method to prepare arguments for.</param>
		/// <param name="parameters">The parameter info array for the method.</param>
		/// <param name="args">The raw Lua arguments.</param>
		/// <param name="argOffset">
		/// The offset into <paramref name="args"/> at which the explicit arguments start
		/// (e.g., 1 if the first argument is the UserData instance for instance methods).
		/// </param>
		/// <returns>An array of CLR objects suitable for <see cref="MethodInfo.Invoke"/>.</returns>
		public static object?[] PrepareCallArguments(LuaCallingContext ctx, MethodInfo method, ParameterInfo[] parameters, LuaValue[] args, int argOffset)
		{
			var hasParams = parameters.Length > 0
				&& parameters[parameters.Length - 1].GetCustomAttributes(typeof(ParamArrayAttribute), false).Any();
			var callArgs = new object?[parameters.Length];

			int luaArgIndex = 0;

			for (int i = 0; i < parameters.Length; i++)
			{
				var paramType = parameters[i].ParameterType;

				if (IsHiddenParameter(paramType))
				{
					if (paramType == typeof(LuaCallingContext))
						callArgs[i] = ctx;
					else if (paramType == typeof(CancellationToken))
						callArgs[i] = ctx.CancellationToken;
				}
				else if (hasParams && i == parameters.Length - 1)
				{
					// params — gather remaining Lua arguments.
					var elementType = paramType.GetElementType()!;
					var remainingCount = Math.Max(0, args.Length - argOffset - luaArgIndex);
					var paramsArray = Array.CreateInstance(elementType, remainingCount);
					for (int j = 0; j < remainingCount; j++)
					{
						var val = args[argOffset + luaArgIndex + j];
						paramsArray.SetValue(LuaValueConverter.ToClrObject(val, elementType), j);
					}
					callArgs[i] = paramsArray;
				}
				else if (argOffset + luaArgIndex < args.Length)
				{
					callArgs[i] = LuaValueConverter.ToClrObject(args[argOffset + luaArgIndex], paramType);
					if (parameters[i].IsOut)
						callArgs[i] = null;
					luaArgIndex++;
				}
				else
				{
					callArgs[i] = parameters[i].DefaultValue;
				}
			}

			return callArgs;
		}
	}
}
