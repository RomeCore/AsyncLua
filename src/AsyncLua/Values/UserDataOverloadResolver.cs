using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace AsyncLua.Values
{
	/// <summary>
	/// Resolves CLR method overloads based on Lua argument types.
	/// </summary>
	public static class UserDataOverloadResolver
	{
		/// <summary>
		/// Selects the best matching overload from a set of methods, given the provided Lua arguments.
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
				var pars = method.GetParameters();
				int score = 0;

				int explicitParamCount = pars.Length;
				bool hasParams = false;
				if (pars.Length > 0 && pars[pars.Length - 1].GetCustomAttributes(typeof(ParamArrayAttribute), false).Any())
				{
					hasParams = true;
					explicitParamCount = pars.Length - 1;
				}

				var required = pars.Count(p => !p.IsOptional && !p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Any());

				if (args.Length < required)
					continue;
				if (!hasParams && args.Length > pars.Length)
					continue;

				bool compatible = true;
				for (int i = 0; i < args.Length; i++)
				{
					Type targetType;

					if (i < explicitParamCount)
					{
						targetType = pars[i].ParameterType;
					}
					else if (hasParams)
					{
						targetType = pars[pars.Length - 1].ParameterType.GetElementType()!;
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

				if (args.Length < pars.Length && !hasParams)
					score += (pars.Length - args.Length) * 2;

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
		/// </summary>
		public static object?[] PrepareCallArguments(MethodInfo method, ParameterInfo[] parameters, LuaValue[] args, int argOffset)
		{
			var hasParams = parameters.Length > 0
				&& parameters[parameters.Length - 1].GetCustomAttributes(typeof(ParamArrayAttribute), false).Any();
			var explicitParamCount = hasParams ? parameters.Length - 1 : parameters.Length;
			var callArgs = new object?[parameters.Length];

			for (int i = 0; i < parameters.Length; i++)
			{
				var argIndex = i + argOffset;
				var paramType = parameters[i].ParameterType;

				if (hasParams && i == parameters.Length - 1)
				{
					var elementType = paramType.GetElementType()!;
					var remainingCount = Math.Max(0, args.Length - argOffset - explicitParamCount);
					var paramsArray = Array.CreateInstance(elementType, remainingCount);
					for (int j = 0; j < remainingCount; j++)
					{
						var val = args[argOffset + explicitParamCount + j];
						paramsArray.SetValue(LuaValueConverter.ToClrObject(val, elementType), j);
					}
					callArgs[i] = paramsArray;
				}
				else if (argIndex < args.Length)
				{
					callArgs[i] = LuaValueConverter.ToClrObject(args[argIndex], paramType);
					if (parameters[i].IsOut)
						callArgs[i] = null;
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
