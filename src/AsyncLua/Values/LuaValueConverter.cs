using System;
using System.Collections;
using System.Threading.Tasks;

namespace AsyncLua.Values
{
	/// <summary>
	/// Provides bidirectional conversion between CLR objects and <see cref="LuaValue"/> types.
	/// </summary>
	public static class LuaValueConverter
	{
		/// <summary>
		/// Converts a CLR <see cref="object"/> to its nearest <see cref="LuaValue"/> equivalent.
		/// </summary>
		public static LuaValue ToLuaValue(object? value)
		{
			if (value is null)
				return LuaNil.Instance;

			if (value is LuaValue lv)
				return lv;

			var type = value.GetType();

			if (value is bool b)
				return LuaBoolean.FromBoolean(b);
			if (value is sbyte sb)
				return new LuaNumber(sb);
			if (value is byte ub)
				return new LuaNumber(ub);
			if (value is short s)
				return new LuaNumber(s);
			if (value is ushort us)
				return new LuaNumber(us);
			if (value is int i)
				return new LuaNumber(i);
			if (value is uint ui)
				return new LuaNumber(ui);
			if (value is long l)
				return new LuaNumber(l);
			if (value is ulong ul)
				return new LuaNumber(ul);
			if (value is float f)
				return new LuaNumber(f);
			if (value is double d)
				return new LuaNumber(d);
			if (value is decimal m)
				return new LuaNumber((double)m);
			if (value is char c)
				return new LuaString(c.ToString());
			if (value is string str)
				return new LuaString(str);
			if (value is Enum)
				return new LuaString(value.ToString());

			if (value is LuaTuple tuple)
				return tuple;

			if (value is Task task)
				return WrapTask(task);

			if (type.IsArray)
			{
				var arr = (Array)value;
				var table = new LuaTable(arr.Length);
				for (int idx = 0; idx < arr.Length; idx++)
					table.Set(idx + 1, ToLuaValue(arr.GetValue(idx)));
				return table;
			}

			if (value is IDictionary dict)
			{
				var table = new LuaTable(dict.Count);
				foreach (DictionaryEntry entry in dict)
					table.Set(ToLuaValue(entry.Key), ToLuaValue(entry.Value));
				return table;
			}

			if (value is IEnumerable enumerable && type != typeof(string))
			{
				var table = new LuaTable();
				int idx = 1;
				foreach (var item in enumerable)
					table.Set(idx++, ToLuaValue(item));
				return table;
			}

			var ud = new LuaUserData(value, type.Name);
			ud.Metatable = UserDataMetatableGenerator.GetOrCreate(ud);
			return ud;
		}

		/// <summary>
		/// Converts a <see cref="LuaValue"/> back to a CLR <typeparamref name="T"/>.
		/// </summary>
		public static T? ToClrObject<T>(this LuaValue value)
		{
			if (ToClrObject(value, typeof(T)) is T result)
				return result;
			return default;
		}

		/// <summary>
		/// Converts a <see cref="LuaValue"/> back to a CLR <see cref="object"/> of the specified target type.
		/// </summary>
		public static object? ToClrObject(this LuaValue value, Type targetType)
		{
			if (value is LuaNil)
				return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

			// Unwrap Nullable<T> to its underlying type for conversion.
			var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;

			if (value is LuaBoolean b)
				return Convert.ChangeType(b.Value, actualType);
			if (value is LuaNumber num)
				return Convert.ChangeType(num.Value, actualType);
			if (value is LuaString str)
				return Convert.ChangeType(str.Value, actualType);

			if (value is LuaTable table)
			{
				if (targetType.IsArray)
				{
					var elementType = targetType.GetElementType()!;
					var count = table.Length;
					var array = Array.CreateInstance(elementType, count);
					for (int i = 0; i < count; i++)
						array.SetValue(ToClrObject(table.Get(i + 1), elementType), i);
					return array;
				}

				if (typeof(IDictionary).IsAssignableFrom(targetType))
					throw new NotSupportedException("Dictionary conversion from LuaTable not yet supported.");

				return table;
			}

			if (value is LuaUserData ud)
				return ud.Target;

			return value;
		}

		/// <summary>
		/// Wraps a <see cref="Task"/> or <see cref="Task{TResult}"/> into a <see cref="LuaTask"/>.
		/// </summary>
		private static LuaTask WrapTask(Task task)
		{
			var taskType = task.GetType();
			if (taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>))
			{
				return LuaTask.FromTask(TransformTask(task));
			}

			return LuaTask.FromTask(Task.Run(async () =>
			{
				await task.ConfigureAwait(false);
				return LuaTuple.Empty;
			}));
		}

		private static async Task<LuaTuple> TransformTask(Task task)
		{
			await task.ConfigureAwait(false);
			var resultProp = task.GetType().GetProperty("Result");
			var result = resultProp?.GetValue(task);
			var converted = ToLuaValue(result);
			return converted is LuaTuple t ? t : new LuaTuple(converted);
		}
	}
}
