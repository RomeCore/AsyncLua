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
	/// <remarks>
	/// <para>
	/// This library closely follows the Lua 5.5 reference implementation with
	/// the following known differences and limitations:
	/// </para>
	///
	/// <list type="bullet">
	///
	/// <item>
	/// <term>string.dump</term>
	/// <description>Not implemented. This function serialises a Lua function into
	/// binary bytecode, which is specific to the Lua VM and has no equivalent
	/// in the managed C# runtime.</description>
	/// </item>
	///
	/// <item>
	/// <term>string.format %p</term>
	/// <description>In Lua, <c>%p</c> formats a pointer address (e.g.,
	/// <c>"0x7ff1234"</c>). In this implementation it returns the runtime
	/// type name of the argument (e.g., <c>"String"</c>), as managed pointers
	/// are opaque and not meaningful.</description>
	/// </item>
	///
	/// <item>
	/// <term>string.format %a / %A</term>
	/// <description>Hexadecimal floating-point formatting is approximated by
	/// extracting the IEEE 754 bit representation. The result may differ from
	/// the Lua reference implementation in edge cases (subnormals, NaN
	/// payloads, etc.).</description>
	/// </item>
	///
	/// <item>
	/// <term>string.byte out-of-bounds</term>
	/// <description>The original Lua implementation exhibits undefined
	/// behaviour when <c>string.byte</c> is called with an index beyond the
	/// string length. This implementation safely returns <c>0</c> for out-of-
	/// range positions.</description>
	/// </item>
	///
	/// <item>
	/// <term>Patterns with embedded NUL bytes</term>
	/// <description>Lua patterns may contain <c>'\0'</c> characters in the
	/// pattern string. The <c>NoSpecials</c> optimisation used in
	/// <c>string.find</c> with <c>plain = true</c> does not account for
	/// embedded NUL bytes. Patterns containing literal NUL bytes will always
	/// be processed via the full pattern-matching engine (which handles them
	/// correctly).</description>
	/// </item>
	///
	/// <item>
	/// <term>string.gmatch state</term>
	/// <description>The iterator returned by <c>string.gmatch</c> stores its
	/// state in a C# closure rather than Lua upvalues. Behaviour is
	/// identical to the reference implementation.</description>
	/// </item>
	///
	/// </list>
	/// </remarks>
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
						throw new LuaRuntimeException("bad argument #1 to 'len' (string expected, got no value)");

					return new LuaTuple(new LuaNumber(args[0].ToString().Length));
				},
				"string.len"));

			table.Set(new LuaString("sub"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("bad argument #1 to 'sub' (string expected, got no value)");

					var s = args[0].ToString();
					var len = s.Length;

					int start = NormaliseIndex((int)((LuaNumber)args[1]).Value, len);
					int endIdx = args.Length > 2
						? NormaliseEndIndex((int)((LuaNumber)args[2]).Value, len)
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
						throw new LuaRuntimeException("bad argument #1 to 'byte' (string expected, got no value)");

					var s = args[0].ToString();
					var len = s.Length;

					int pi = args.Length > 1 ? (int)((LuaNumber)args[1]).Value : 1;
					int posi = NormaliseIndex(pi, len);
					int pose;

					if (args.Length > 2)
					{
						pose = NormaliseEndIndex((int)((LuaNumber)args[2]).Value, len);
					}
					else
					{
						pose = posi;
					}

					if (posi > pose)
						return new LuaTuple();

					if (pose - posi + 1 > 1000000)
						throw new LuaRuntimeException("string slice too long");

					var results = new List<LuaValue>();
					for (int i = posi; i <= pose; i++)
					{
						results.Add(new LuaNumber(
							i >= 1 && i <= len ? (int)s[i - 1] : 0));
					}
					return new LuaTuple(results.ToArray());
				}, "string.byte"));

			table.Set(new LuaString("char"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("bad argument #1 to 'char' (value expected, got no value)");

					var chars = new char[args.Length];
					for (int i = 0; i < args.Length; i++)
					{
						int c = (int)((LuaNumber)args[i]).Value;
						if (c < 0 || c > 255)
							throw new LuaRuntimeException($"bad argument #{i + 1} to 'char' (value out of range)");
						chars[i] = (char)c;
					}
					return new LuaTuple(new LuaString(new string(chars)));
				}, "string.char"));

			table.Set(new LuaString("upper"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("bad argument #1 to 'upper' (string expected, got no value)");

					return new LuaTuple(new LuaString(args[0].ToString().ToUpperInvariant()));
				},
				"string.upper"));

			table.Set(new LuaString("lower"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("bad argument #1 to 'lower' (string expected, got no value)");

					return new LuaTuple(new LuaString(args[0].ToString().ToLowerInvariant()));
				},
				"string.lower"));

			table.Set(new LuaString("reverse"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("bad argument #1 to 'reverse' (string expected, got no value)");

					var s = args[0].ToString();
					var chars = s.ToCharArray();
					Array.Reverse(chars);
					return new LuaTuple(new LuaString(new string(chars)));
				}, "string.reverse"));

		table.Set(new LuaString("rep"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 2)
						throw new LuaRuntimeException("bad argument #1 to 'rep' (string expected, got no value)");

					var s = args[0].ToString();
					var n = (int)((LuaNumber)args[1]).Value;
					string sep = args.Length > 2 ? args[2].ToString() : "";

					if (n <= 0 || (s.Length == 0 && sep.Length == 0))
						return new LuaTuple(LuaString.Empty);

				// If s is empty but sep is not, result is sep repeated (n-1) times
					if (s.Length == 0)
					{
						if (n <= 1)
							return new LuaTuple(LuaString.Empty);
						long sepLen = (long)(n - 1) * sep.Length;
						if (sepLen > int.MaxValue)
							throw new LuaRuntimeException("resulting string too large");
						var sb = new StringBuilder((int)sepLen);
						for (int i = 1; i < n; i++)
							sb.Append(sep);
						return new LuaTuple(new LuaString(sb.ToString()));
					}

					long totalLen = (long)n * s.Length + (long)(n - 1) * sep.Length;
					if (totalLen > int.MaxValue)
						throw new LuaRuntimeException("resulting string too large");

					var result = new StringBuilder((int)totalLen);
					result.Append(s);
					for (int i = 1; i < n; i++)
					{
						result.Append(sep);
						result.Append(s);
					}
					return new LuaTuple(new LuaString(result.ToString()));
				}, "string.rep"));

			// ── Pattern-matching functions ────────────────────────────

			table.Set(new LuaString("find"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 2)
						throw new LuaRuntimeException("bad argument #1 to 'find' (string expected, got no value)");

					var s = args[0].ToString();
					var pattern = args[1].ToString();
					int init = args.Length > 2 ? (int)((LuaNumber)args[2]).Value : 1;
					bool plain = args.Length > 3 && args[3].ToBoolean();

					return LuaPatternMatcher.Find(s, pattern, init, plain);
				}, "string.find"));

			table.Set(new LuaString("match"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 2)
						throw new LuaRuntimeException("bad argument #1 to 'match' (string expected, got no value)");

					var s = args[0].ToString();
					var pattern = args[1].ToString();
					int init = args.Length > 2 ? (int)((LuaNumber)args[2]).Value : 1;

					return LuaPatternMatcher.Match(s, pattern, init);
				}, "string.match"));

			table.Set(new LuaString("gmatch"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 2)
						throw new LuaRuntimeException("bad argument #1 to 'gmatch' (string expected, got no value)");

					var s = args[0].ToString();
					var pattern = args[1].ToString();
					int init = args.Length > 2 ? (int)((LuaNumber)args[2]).Value : 1;

					return new LuaTuple(LuaPatternMatcher.GMatch(s, pattern, init));
				}, "string.gmatch"));

			table.Set(new LuaString("gsub"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 3)
						throw new LuaRuntimeException("bad argument #1 to 'gsub' (string expected, got no value)");

					var s = args[0].ToString();
					var pattern = args[1].ToString();
					var repl = args[2];
					int maxReplacements = args.Length > 3 ? (int)((LuaNumber)args[3]).Value : int.MaxValue;

					return LuaPatternMatcher.GSub(s, pattern, repl, maxReplacements, ctx);
				}, "string.gsub"));

			// ── format ────────────────────────────────────────────────

			table.Set(new LuaString("format"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("bad argument #1 to 'format' (string expected, got no value)");

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

			// ── pack / unpack ─────────────────────────────────────────

			table.Set(new LuaString("pack"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("bad argument #1 to 'pack' (string expected, got no value)");

					var fmt = args[0].ToString();
					var packArgs = new object[args.Length - 1];
					for (int i = 1; i < args.Length; i++)
					{
						if (args[i] is LuaNumber num)
							packArgs[i - 1] = num.Value;
						else if (args[i] is LuaString str)
							packArgs[i - 1] = str.Value;
						else
							throw new LuaRuntimeException($"bad argument #{i + 1} to 'pack' (number or string expected)");
					}
					return new LuaTuple(new LuaString(StringPack(fmt, packArgs)));
				}, "string.pack"));

			table.Set(new LuaString("packsize"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 1)
						throw new LuaRuntimeException("bad argument #1 to 'packsize' (string expected, got no value)");

					var fmt = args[0].ToString();
					return new LuaTuple(new LuaNumber(StringPackSize(fmt)));
				}, "string.packsize"));

			table.Set(new LuaString("unpack"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length < 2)
						throw new LuaRuntimeException("bad argument #1 to 'unpack' (string expected, got no value)");

					var fmt = args[0].ToString();
					var data = args[1].ToString();
					int pos = args.Length > 2 ? (int)((LuaNumber)args[2]).Value : 1;

					return StringUnpack(fmt, data, pos);
				}, "string.unpack"));

			// ── String metatable ──────────────────────────────────────

			state.TypeMetatables[LuaType.String] = new LuaMetatable
			{
				[LuaMetatableEvent.Index] = table
			};
		}

		// ──────────────────────────────────────────────────────────────
		//  string.format implementation (extends C printf syntax)
		// ──────────────────────────────────────────────────────────────

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
					string flags = "";
					while (i < format.Length && "+-#0 ".IndexOf(format[i]) >= 0)
					{
						flags += format[i];
						i++;
					}

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

					// Length modifier (skip — we don't need it in .NET)
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
						result.Append("%?");
						continue;
					}

					var arg = args[argIndex++];

					// Special handling for 'q' — no modifiers allowed
					if (spec == 'q')
					{
						if (flags.Length > 0 || width.Length > 0 || precision.Length > 0)
							throw new LuaRuntimeException("specifier '%q' cannot have modifiers");
						AddQuoted(result, arg);
						continue;
					}

					string formatted;
					switch (spec)
					{
						case 's':
							{
								string s = arg?.ToString() ?? "";
								if (!string.IsNullOrEmpty(precision) && int.TryParse(precision, out int p))
								{
									if (p < s.Length)
										s = s.Substring(0, p);
								}
								formatted = s;
								break;
							}

						case 'd':
						case 'i':
							{
								long intVal = ConvertToInt64(arg);
								formatted = FormatInt(intVal, flags, width, false);
								break;
							}

						case 'u':
							{
								long intVal = ConvertToInt64(arg);
								formatted = FormatInt(intVal, flags, width, true);
								break;
							}

						case 'o':
							{
								long intVal = ConvertToInt64(arg);
								formatted = FormatOctal(intVal, flags, width);
								break;
							}

						case 'x':
							{
								long intVal = ConvertToInt64(arg);
								formatted = FormatHex(intVal, flags, width, false);
								break;
							}

						case 'X':
							{
								long intVal = ConvertToInt64(arg);
								formatted = FormatHex(intVal, flags, width, true);
								break;
							}

						case 'f':
						case 'F':
							{
								double d = Convert.ToDouble(arg);
								formatted = FormatFloat(d, flags, width, precision, spec);
								break;
							}

						case 'e':
						case 'E':
							{
								double d = Convert.ToDouble(arg);
								formatted = FormatFloat(d, flags, width, precision, spec);
								break;
							}

						case 'g':
						case 'G':
							{
								double d = Convert.ToDouble(arg);
								formatted = FormatFloat(d, flags, width, precision, spec);
								break;
							}

						case 'a':
						case 'A':
							{
								double d = Convert.ToDouble(arg);
								formatted = FormatHexFloat(d, spec == 'A');
								break;
							}

						case 'c':
							{
								long intVal = ConvertToInt64(arg);
								formatted = ((char)intVal).ToString();
								break;
							}

						case 'p':
							{
								// Use Marshal.PtrToStringAnsi(nativeIntPtr) later
								formatted = arg?.GetType().Name ?? "(null)";
								break;
							}

						default:
							formatted = $"%{spec}";
							break;
					}

					// Apply width padding
					if (!string.IsNullOrEmpty(width) && int.TryParse(width, out int w) && formatted.Length < w)
					{
						bool leftAlign = flags.Contains("-");
						char padChar = flags.Contains("0") && !leftAlign ? '0' : ' ';
						if (leftAlign)
							formatted = formatted.PadRight(w, padChar);
						else
							formatted = formatted.PadLeft(w, padChar);
					}

					result.Append(formatted);
				}
				else
				{
					result.Append(format[i]);
				}
			}
			return result.ToString();
		}

		private static long ConvertToInt64(object arg)
		{
			if (arg is double d)
				return (long)d;
			if (arg is int i) return i;
			if (arg is long l) return l;
			if (arg is string s && long.TryParse(s, out long parsed))
				return parsed;
			return (long)Convert.ToDouble(arg);
		}

		private static string FormatInt(long value, string flags, string width, bool unsigned)
		{
			string result;
			if (unsigned)
				result = ((ulong)value).ToString(CultureInfo.InvariantCulture);
			else
				result = value.ToString(CultureInfo.InvariantCulture);

			bool showSign = flags.Contains("+") || flags.Contains(" ");
			if (showSign && value >= 0)
				result = (flags.Contains("+") ? "+" : " ") + result;

			if (!string.IsNullOrEmpty(width) && int.TryParse(width, out int w) && result.Length < w)
			{
				bool leftAlign = flags.Contains("-");
				char padChar = flags.Contains("0") && !leftAlign ? '0' : ' ';
				if (leftAlign)
					result = result.PadRight(w, padChar);
				else
					result = result.PadLeft(w, padChar);
			}

			return result;
		}

		private static string FormatOctal(long value, string flags, string width)
		{
			string result = Convert.ToString(value, 8);
			if (flags.Contains("#") && value != 0)
				result = "0" + result;

			if (!string.IsNullOrEmpty(width) && int.TryParse(width, out int w) && result.Length < w)
			{
				bool leftAlign = flags.Contains("-");
				char padChar = flags.Contains("0") && !leftAlign ? '0' : ' ';
				if (leftAlign)
					result = result.PadRight(w, padChar);
				else
					result = result.PadLeft(w, padChar);
			}
			return result;
		}

		private static string FormatHex(long value, string flags, string width, bool upper)
		{
			string formatStr = upper ? "X" : "x";
			string result = value.ToString(formatStr);
			if (flags.Contains("#") && value != 0)
				result = upper ? "0X" + result : "0x" + result;

			if (!string.IsNullOrEmpty(width) && int.TryParse(width, out int w) && result.Length < w)
			{
				bool leftAlign = flags.Contains("-");
				char padChar = flags.Contains("0") && !leftAlign ? '0' : ' ';
				if (leftAlign)
					result = result.PadRight(w, padChar);
				else
					result = result.PadLeft(w, padChar);
			}
			return result;
		}

		private static string FormatFloat(double value, string flags, string width, string precision, char spec)
		{
			string result;

			if (double.IsNaN(value))
				result = "nan";
			else if (double.IsPositiveInfinity(value))
				result = "inf";
			else if (double.IsNegativeInfinity(value))
				result = "-inf";
			else
			{
				string fmtSpec = spec.ToString();
				if (!string.IsNullOrEmpty(precision))
					fmtSpec = fmtSpec + precision;

				result = value.ToString(fmtSpec, CultureInfo.InvariantCulture);
			}

			bool showSign = flags.Contains("+") || flags.Contains(" ");
			if (showSign && value >= 0 && !double.IsNaN(value))
				result = (flags.Contains("+") ? "+" : " ") + result;

			if (!string.IsNullOrEmpty(width) && int.TryParse(width, out int w) && result.Length < w)
			{
				bool leftAlign = flags.Contains("-");
				char padChar = flags.Contains("0") && !leftAlign ? '0' : ' ';
				if (leftAlign)
					result = result.PadRight(w, padChar);
				else
					result = result.PadLeft(w, padChar);
			}

			return result;
		}

		private static string FormatHexFloat(double value, bool upper)
		{
			if (double.IsNaN(value))
				return "nan";
			if (double.IsPositiveInfinity(value))
				return "inf";
			if (double.IsNegativeInfinity(value))
				return "-inf";
			if (value == 0.0)
				return upper ? "0X0P+0" : "0x0p+0";

			// Extract IEEE 754 representation
			byte[] bytes = BitConverter.GetBytes(value);
			if (!BitConverter.IsLittleEndian)
				Array.Reverse(bytes);

			long bits = BitConverter.ToInt64(bytes, 0);
			bool negative = (bits & 0x7FFFFFFFFFFFFFFF) != bits; // check sign bit
			int exponent = (int)((bits >> 52) & 0x7FFL);
			long mantissa = bits & 0xFFFFFFFFFFFFFL;

			if (exponent == 0)
			{
				// Subnormal
				exponent = -1022;
			}
			else
			{
				mantissa |= 0x10000000000000L; // implicit 1
				exponent -= 1023;
			}

			var sb = new StringBuilder();
			if (negative)
				sb.Append('-');
			sb.Append("0x");
			sb.Append(mantissa.ToString(upper ? "X" : "x"));
			sb.Append(upper ? 'P' : 'p');
			sb.Append(exponent >= 0 ? "+" : "");
			sb.Append(exponent);
			return sb.ToString();
		}

		private static void AddQuoted(StringBuilder result, object arg)
		{
			string s = arg?.ToString() ?? "";
			result.Append('"');
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if (c == '"' || c == '\\' || c == '\n')
				{
					result.Append('\\');
					result.Append(c);
				}
				else if (char.IsControl(c))
				{
					if (i + 1 < s.Length && !char.IsDigit(s[i + 1]))
						result.Append("\\" + ((int)c).ToString());
					else
						result.Append("\\" + ((int)c).ToString("D3"));
				}
				else
				{
					result.Append(c);
				}
			}
			result.Append('"');
		}

		// ──────────────────────────────────────────────────────────────
		//  string.pack / packsize / unpack implementation
		// ──────────────────────────────────────────────────────────────

		private static string StringPack(string fmt, object[] args)
		{
			int argIndex = 0;
			var result = new List<byte>();
			bool isLittle = BitConverter.IsLittleEndian;
			int maxAlign = 1;
			int i = 0;

			while (i < fmt.Length)
			{
				// Skip spaces
				if (fmt[i] == ' ')
				{
					i++;
					continue;
				}

				// Alignment options
				if (fmt[i] == '<') { isLittle = true; i++; continue; }
				if (fmt[i] == '>') { isLittle = false; i++; continue; }
				if (fmt[i] == '=') { isLittle = BitConverter.IsLittleEndian; i++; continue; }
				if (fmt[i] == '!')
				{
					i++;
					maxAlign = (int)ReadNumber(fmt, ref i, 1);
					continue;
				}
				if (fmt[i] == 'X')
				{
					i++;
					if (i >= fmt.Length)
						throw new LuaRuntimeException("invalid next option for option 'X'");
					int savedI = i;
					int nextSize = GetPackSize(fmt, ref i, isLittle, maxAlign);
					if (nextSize <= 1)
						throw new LuaRuntimeException("invalid next option for option 'X'");
					int align = Math.Min(nextSize, maxAlign);
					int mod = result.Count & (align - 1);
					int pad = (align - mod) & (align - 1);
					for (int p = 0; p < pad; p++)
						result.Add(0);
					i = savedI;
					continue;
				}

				char opt = fmt[i];
				int size = GetPackSize(fmt, ref i, isLittle, maxAlign);
				if (size == 0 && opt == 'x')
				{
					// 'x' padding
					result.Add(0);
					argIndex--; // doesn't consume an argument
				}
				else
				{
					if (argIndex >= args.Length)
						throw new LuaRuntimeException($"bad argument #{argIndex + 1} to 'pack' (value expected)");
					var arg = args[argIndex++];
					PackOne(result, opt, size, arg, isLittle);
				}
			}

			return BytesToString(result.ToArray());
		}

		private static int StringPackSize(string fmt)
		{
			int totalSize = 0;
			bool isLittle = BitConverter.IsLittleEndian;
			int maxAlign = 1;
			int i = 0;

			while (i < fmt.Length)
			{
				if (fmt[i] == ' ') { i++; continue; }
				if (fmt[i] == '<') { isLittle = true; i++; continue; }
				if (fmt[i] == '>') { isLittle = false; i++; continue; }
				if (fmt[i] == '=') { isLittle = BitConverter.IsLittleEndian; i++; continue; }
				if (fmt[i] == '!')
				{
					i++;
					maxAlign = (int)ReadNumber(fmt, ref i, 1);
					continue;
				}
				if (fmt[i] == 'X')
				{
					i++;
					if (i >= fmt.Length)
						throw new LuaRuntimeException("invalid next option for option 'X'");
					int savedI = i;
					int nextSize = GetPackSize(fmt, ref i, isLittle, maxAlign);
					int align = Math.Min(nextSize, maxAlign);
					int mod = totalSize & (align - 1);
					int pad = (align - mod) & (align - 1);
					totalSize += pad;
					i = savedI;
					continue;
				}

				char opt = fmt[i];
				if (opt == 's' || opt == 'z')
					throw new LuaRuntimeException("variable-length format");

				int size = GetPackSize(fmt, ref i, isLittle, maxAlign);
				if (size == 0 && opt == 'x')
				{
					totalSize += 1;
				}
				else
				{
					totalSize += size;
				}
			}

			return totalSize;
		}

		private static LuaTuple StringUnpack(string fmt, string data, int pos)
		{
			pos = NormaliseIndex(pos, data.Length) - 1; // 0-based
			if (pos < 0 || pos > data.Length)
				throw new LuaRuntimeException("initial position out of string");

			bool isLittle = BitConverter.IsLittleEndian;
			int maxAlign = 1;
			int i = 0;
			var results = new List<LuaValue>();

			while (i < fmt.Length)
			{
				if (fmt[i] == ' ') { i++; continue; }
				if (fmt[i] == '<') { isLittle = true; i++; continue; }
				if (fmt[i] == '>') { isLittle = false; i++; continue; }
				if (fmt[i] == '=') { isLittle = BitConverter.IsLittleEndian; i++; continue; }
				if (fmt[i] == '!')
				{
					i++;
					maxAlign = (int)ReadNumber(fmt, ref i, 1);
					continue;
				}
				if (fmt[i] == 'X')
				{
					i++;
					if (i >= fmt.Length)
						throw new LuaRuntimeException("invalid next option for option 'X'");
					int savedI = i;
					int nextSize = GetPackSize(fmt, ref i, isLittle, maxAlign);
					int align = Math.Min(nextSize, maxAlign);
					int mod = pos & (align - 1);
					int pad = (align - mod) & (align - 1);
					pos += pad;
					if (pos > data.Length)
						throw new LuaRuntimeException("data string too short");
					i = savedI;
					continue;
				}

				char opt = fmt[i];
				int size = GetPackSize(fmt, ref i, isLittle, maxAlign);

				if (opt == 'x')
				{
					pos++;
					continue;
				}

				if (opt == 's')
				{
					if (pos + size > data.Length)
						throw new LuaRuntimeException("data string too short");
					int len = 0;
					for (int b = 0; b < size; b++)
						len |= (data[pos + b] & 0xFF) << (b * 8);
					pos += size;
					if (pos + len > data.Length)
						throw new LuaRuntimeException("data string too short");
					results.Add(new LuaString(data.Substring(pos, len)));
					pos += len;
					continue;
				}

				if (opt == 'z')
				{
					int end = data.IndexOf('\0', pos);
					if (end < 0)
						throw new LuaRuntimeException("unfinished string for format 'z'");
					results.Add(new LuaString(data.Substring(pos, end - pos)));
					pos = end + 1;
					continue;
				}

				if (opt == 'c')
				{
					if (pos + size > data.Length)
						throw new LuaRuntimeException("data string too short");
					results.Add(new LuaString(data.Substring(pos, size)));
					pos += size;
					continue;
				}

				// Number unpacking
				if (pos + size > data.Length)
					throw new LuaRuntimeException("data string too short");

				byte[] bytes = StringToBytes(data.Substring(pos, size));
				if (!isLittle)
					Array.Reverse(bytes);

				switch (opt)
				{
					case 'b':
						results.Add(new LuaNumber((sbyte)bytes[0]));
						break;
					case 'B':
						results.Add(new LuaNumber(bytes[0]));
						break;
					case 'h':
						results.Add(new LuaNumber(BitConverter.ToInt16(bytes, 0)));
						break;
					case 'H':
						results.Add(new LuaNumber(BitConverter.ToUInt16(bytes, 0)));
						break;
					case 'i':
					case 'l':
					{
						if (size <= 2)
							results.Add(new LuaNumber(BitConverter.ToInt16(bytes, 0)));
						else if (size <= 4)
							results.Add(new LuaNumber(BitConverter.ToInt32(bytes, 0)));
						else
							results.Add(new LuaNumber(BitConverter.ToInt64(bytes, 0)));
						break;
					}
					case 'I':
					case 'L':
					{
						if (size <= 2)
							results.Add(new LuaNumber(BitConverter.ToUInt16(bytes, 0)));
						else if (size <= 4)
							results.Add(new LuaNumber(BitConverter.ToUInt32(bytes, 0)));
						else
							results.Add(new LuaNumber(BitConverter.ToUInt64(bytes, 0)));
						break;
					}
					case 'j':
						results.Add(new LuaNumber(BitConverter.ToInt64(bytes, 0)));
						break;
					case 'J':
						results.Add(new LuaNumber(BitConverter.ToUInt64(bytes, 0)));
						break;
					case 'T':
						results.Add(new LuaNumber(BitConverter.ToUInt64(bytes, 0)));
						break;
					case 'f':
						results.Add(new LuaNumber(BitConverter.ToSingle(bytes, 0)));
						break;
					case 'd':
						results.Add(new LuaNumber(BitConverter.ToDouble(bytes, 0)));
						break;
					case 'n':
						results.Add(new LuaNumber(BitConverter.ToDouble(bytes, 0)));
						break;
				}

				pos += size;
			}

			results.Add(new LuaNumber(pos + 1)); // next position (1-based)
			return new LuaTuple(results.ToArray());
		}

		private static int GetPackSize(string fmt, ref int i, bool isLittle, int maxAlign)
		{
			if (i >= fmt.Length)
				return 0;

			char opt = fmt[i];
			i++;
			int size;

			switch (opt)
			{
				case 'b': case 'B': case 'x': size = 1; break;
				case 'h': case 'H': size = 2; break;
				case 'l': case 'L': size = 4; break;
				case 'j': case 'J': case 'T': case 'n': size = 8; break;
				case 'f': size = 4; break;
				case 'd': size = 8; break;
				case 'i': case 'I':
					{
						int defaultSize = 4; // sizeof(int)
						size = (int)ReadNumber(fmt, ref i, (uint)defaultSize);
						if (size < 1 || size > 16)
							throw new LuaRuntimeException($"integral size ({size}) out of limits [1,16]");
						break;
					}
				case 's':
					{
						size = (int)ReadNumber(fmt, ref i, 8); // sizeof(size_t) == 8 on 64-bit
						break;
					}
				case 'c':
					{
						uint usize = ReadNumber(fmt, ref i, uint.MaxValue);
						if (usize == uint.MaxValue)
							throw new LuaRuntimeException("missing size for format option 'c'");
						size = (int)usize;
						break;
					}
				case 'z':
					size = 0;
					break;
				default:
					throw new LuaRuntimeException($"invalid format option '{opt}'");
			}

			return size;
		}

		private static void PackOne(List<byte> buffer, char opt, int size, object arg, bool isLittle)
		{

			switch (opt)
			{
				case 'b':
				{
					long v = ConvertToInt64(arg);
					buffer.Add((byte)(sbyte)v);
					break;
				}
				case 'B':
				{
					long v = ConvertToInt64(arg);
					buffer.Add((byte)v);
					break;
				}
				case 'h':
				{
					short v = (short)ConvertToInt64(arg);
					var bytes = BitConverter.GetBytes(v);
					if (!isLittle) Array.Reverse(bytes);
					buffer.AddRange(bytes);
					break;
				}
				case 'H':
				{
					ushort v = (ushort)ConvertToInt64(arg);
					var bytes = BitConverter.GetBytes(v);
					if (!isLittle) Array.Reverse(bytes);
					buffer.AddRange(bytes);
					break;
				}
				case 'i': case 'l':
				{
					long v = ConvertToInt64(arg);
					var bytes = new byte[size];
					for (int b = 0; b < size; b++)
					{
						bytes[b] = (byte)(v & 0xFF);
						v >>= 8;
					}
					if (!isLittle) Array.Reverse(bytes);
					buffer.AddRange(bytes);
					break;
				}
				case 'I': case 'L':
				{
					ulong v = (ulong)ConvertToInt64(arg);
					var bytes = new byte[size];
					for (int b = 0; b < size; b++)
					{
						bytes[b] = (byte)(v & 0xFF);
						v >>= 8;
					}
					if (!isLittle) Array.Reverse(bytes);
					buffer.AddRange(bytes);
					break;
				}
				case 'j':
				{
					long v = ConvertToInt64(arg);
					var bytes = BitConverter.GetBytes(v);
					if (!isLittle) Array.Reverse(bytes);
					buffer.AddRange(bytes);
					break;
				}
				case 'J': case 'T':
				{
					ulong v = (ulong)ConvertToInt64(arg);
					var bytes = BitConverter.GetBytes(v);
					if (!isLittle) Array.Reverse(bytes);
					buffer.AddRange(bytes);
					break;
				}
				case 'f':
				{
					float v = (float)Convert.ToDouble(arg);
					var bytes = BitConverter.GetBytes(v);
					if (!isLittle) Array.Reverse(bytes);
					buffer.AddRange(bytes);
					break;
				}
				case 'd':
				{
					double v = Convert.ToDouble(arg);
					var bytes = BitConverter.GetBytes(v);
					if (!isLittle) Array.Reverse(bytes);
					buffer.AddRange(bytes);
					break;
				}
				case 'n':
				{
					double v = Convert.ToDouble(arg);
					var bytes = BitConverter.GetBytes(v);
					if (!isLittle) Array.Reverse(bytes);
					buffer.AddRange(bytes);
					break;
				}
				case 'c':
				{
					string s = arg.ToString() ?? "";
					if (s.Length > size)
						throw new LuaRuntimeException("string longer than given size");
					byte[] strBytes = StringToBytes(s);
					buffer.AddRange(strBytes);
					for (int p = strBytes.Length; p < size; p++)
						buffer.Add(0);
					break;
				}
				case 's':
				{
					string s = arg.ToString() ?? "";
					byte[] strBytes = StringToBytes(s);
					ulong len = (ulong)strBytes.Length;
					var lenBytes = new byte[size];
					for (int b = 0; b < size; b++)
					{
						lenBytes[b] = (byte)(len & 0xFF);
						len >>= 8;
					}
					if (!isLittle) Array.Reverse(lenBytes);
					buffer.AddRange(lenBytes);
					buffer.AddRange(strBytes);
					break;
				}
				case 'z':
				{
					string s = arg.ToString() ?? "";
					byte[] strBytes = StringToBytes(s);
					buffer.AddRange(strBytes);
					buffer.Add(0);
					break;
				}
				default:
					throw new LuaRuntimeException($"invalid format option '{opt}'");
			}
		}

		private static uint ReadNumber(string fmt, ref int i, uint defaultVal)
		{
			if (i >= fmt.Length || !char.IsDigit(fmt[i]))
				return defaultVal;

			uint val = 0;
			while (i < fmt.Length && char.IsDigit(fmt[i]))
			{
				val = val * 10 + (uint)(fmt[i] - '0');
				i++;
			}
			return val;
		}

		private static string BytesToString(byte[] bytes)
		{
			char[] chars = new char[bytes.Length];
			for (int i = 0; i < bytes.Length; i++)
				chars[i] = (char)bytes[i];
			return new string(chars);
		}

		private static byte[] StringToBytes(string str)
		{
			byte[] bytes = new byte[str.Length];
			for (int i = 0; i < str.Length; i++)
				bytes[i] = (byte)str[i];
			return bytes;
		}

		private static int NormaliseIndex(int idx, int length)
		{
			if (idx < 0)
				idx = length + idx + 1;
			return idx;
		}

		private static int NormaliseEndIndex(int idx, int length)
		{
			if (idx < 0)
				idx = length + idx + 1;
			if (idx > length)
				idx = length;
			return idx;
		}
	}
}
