using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/// <summary>
	/// Implements the standard Lua <c>string</c> library with manipulation,
	/// matching, and formatting functions.
	/// </summary>
	public sealed class StringLibrary : LuaTableBaseLibrary
	{
		/// <summary>
		/// Gets the namespace name <c>"string"</c>.
		/// </summary>
		public override string Namespace => "string";

		/// <summary>
		/// Populates the string table with functions.
		/// </summary>
		protected override void PopulateTable(LuaState state, LuaTable table)
		{
			table.Set(new LuaString("len"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("Expected at least one argument for string.len() function.");

					return new LuaTuple(new LuaNumber(args[0].ToString().Length));
				},
				"string.len"));

			table.Set(new LuaString("sub"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("Expected at least one argument for string.sub() function.");

					var s = args[0].ToString();
					var len = s.Length;

					// Normalise start (Lua: 1-based, negative = from end).
					int start = NormaliseIndex((int)((LuaNumber)args[1]).Value, len);
					int endIdx = args.Length > 2
						? NormaliseIndex((int)((LuaNumber)args[2]).Value, len)
						: len;

					if (start > endIdx || start > len || endIdx < 1)
						return new LuaTuple(LuaString.Empty);

					int count = endIdx - start + 1;
					if (start < 1) { count += start - 1; start = 1; }
					if (start + count - 1 > len) count = len - start + 1;
					if (count < 0) count = 0;

					return new LuaTuple(new LuaString(s.Substring(start - 1, count)));
				}, "string.sub"));

			table.Set(new LuaString("byte"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("Expected at least one argument for string.byte() function.");

					var s = args[0].ToString();
					var pos = args.Length > 1 ? (int)((LuaNumber)args[1]).Value : 1;
					if (pos < 0) pos = s.Length + pos + 1;
					return new LuaTuple(new LuaNumber(
						pos > 0 && pos <= s.Length ? s[pos - 1] : 0));
				}, "string.byte"));

			table.Set(new LuaString("char"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					var chars = new char[args.Length];
					for (int i = 0; i < args.Length; i++)
						chars[i] = (char)(int)((LuaNumber)args[i]).Value;
					return new LuaTuple(new LuaString(new string(chars)));
				}, "string.char"));

			table.Set(new LuaString("upper"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("Expected at least one argument for string.upper() function.");

					return new LuaTuple(new LuaString(args[0].ToString().ToUpperInvariant()));
				},
				"string.upper"));

			table.Set(new LuaString("lower"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("Expected at least one argument for string.lower() function.");

					return new LuaTuple(new LuaString(args[0].ToString().ToLowerInvariant()));
				},
				"string.lower"));

			table.Set(new LuaString("reverse"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("Expected at least one argument for string.reverse() function.");

					var s = args[0].ToString();
					var chars = s.ToCharArray();
					Array.Reverse(chars);
					return new LuaTuple(new LuaString(new string(chars)));
				}, "string.reverse"));

			table.Set(new LuaString("rep"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 2)
						throw new LuaRuntimeException("Expected at least two arguments for string.rep() function.");

					var s = args[0].ToString();
					var n = (int)((LuaNumber)args[1]).Value;
					var sb = new StringBuilder(s.Length * n);
					for (int i = 0; i < n; i++)
						sb.Append(s);
					return new LuaTuple(new LuaString(sb.ToString()));
				}, "string.rep"));

			table.Set(new LuaString("format"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("Expected at least one argument for string.format() function.");

					var format = args[0].ToString();
					var fmtArgs = new object[args.Length - 1];
					for (int i = 1; i < args.Length; i++)
					{
						if (args[i] is LuaNumber num)
							fmtArgs[i - 1] = num.Value;
						else
							fmtArgs[i - 1] = args[i].ToString();
					}
					return new LuaTuple(new LuaString(PrintfFormat(format, fmtArgs)));
				}, "string.format"));

			state.TypeMetatables[LuaType.String] = new LuaMetatable
			{
				[LuaMetatableEvent.Index] = table
			};
		}

		/// <summary>
		/// Implements a subset of the C printf format specifier syntax
		/// (<c>%s</c>, <c>%d</c>, <c>%f</c>, <c>%g</c>, <c>%e</c>, <c>%x</c>, <c>%X</c>)
		/// with optional width and precision. Uses <see cref="IFormattable"/> on the
		/// argument when available.
		/// </summary>
		private static string PrintfFormat(string format, object[] args)
		{
			int argIndex = 0;
			var result = new StringBuilder(format.Length);
			for (int i = 0; i < format.Length; i++)
			{
				if (format[i] == '%' && i + 1 < format.Length)
				{
					i++; // skip '%'

					// Parse flags
					while (i < format.Length && "+-#0 ".IndexOf(format[i]) >= 0)
						i++;

					string width = "";
					while (i < format.Length && char.IsDigit(format[i]))
						width += format[i++];

					string precision = "";
					if (i < format.Length && format[i] == '.')
					{
						i++;
						while (i < format.Length && char.IsDigit(format[i]))
							precision += format[i++];
					}

					// Length modifier (skip)
					if (i < format.Length && "hlLzjt".IndexOf(format[i]) >= 0)
						i++;

					if (i >= format.Length)
					{
						result.Append('%');
						continue;
					}

					char spec = format[i];
					if (spec == '%')
					{
						result.Append('%');
						continue;
					}

					if (argIndex >= args.Length)
					{
						result.Append("%?"); // not enough args
						continue;
					}

					var arg = args[argIndex++];
					string formatted;

					switch (spec)
					{
						case 's':
							formatted = arg?.ToString() ?? "";
							break;

						case 'd':
						case 'i':
						case 'u':
						{
							long intVal;
							if (arg is double d)
								intVal = (long)d;
							else
								long.TryParse(arg?.ToString(), out intVal);
							formatted = intVal.ToString();
							break;
						}

						case 'f':
						case 'F':
						{
							double d = Convert.ToDouble(arg);
							if (!string.IsNullOrEmpty(precision))
								formatted = d.ToString("F" + precision, CultureInfo.InvariantCulture);
							else
								formatted = d.ToString(CultureInfo.InvariantCulture);
							break;
						}

						case 'e':
						case 'E':
						{
							double d = Convert.ToDouble(arg);
							formatted = d.ToString(spec.ToString(), CultureInfo.InvariantCulture);
							break;
						}

						case 'g':
						case 'G':
						{
							double d = Convert.ToDouble(arg);
							formatted = d.ToString(spec.ToString(), CultureInfo.InvariantCulture);
							break;
						}

						case 'x':
						{
							long intVal = (long)Convert.ToDouble(arg);
							formatted = intVal.ToString("x");
							break;
						}

						case 'X':
						{
							long intVal = (long)Convert.ToDouble(arg);
							formatted = intVal.ToString("X");
							break;
						}

						case 'c':
						{
							formatted = ((char)(long)Convert.ToDouble(arg)).ToString();
							break;
						}

						default:
							formatted = $"%{spec}";
							break;
					}

					// Apply width padding
					if (!string.IsNullOrEmpty(width) && int.TryParse(width, out int w) && formatted.Length < w)
						formatted = formatted.PadLeft(w);

					result.Append(formatted);
				}
				else
				{
					result.Append(format[i]);
				}
			}
			return result.ToString();
		}

		/// <summary>
		/// Normalises a Lua 1-based index (supports negative indices).
		/// </summary>
		private static int NormaliseIndex(int idx, int length)
		{
			if (idx < 0)
				idx = length + idx + 1;
			return idx;
		}
	}
}
