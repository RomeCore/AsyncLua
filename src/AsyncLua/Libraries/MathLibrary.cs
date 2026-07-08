using System;
using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/// <summary>
	/// Implements the standard Lua <c>math</c> library with trigonometric, rounding,
	/// conversion, pseudo-random and utility functions.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This library follows Lua 5.4 semantics where applicable. Because AsyncLua
	/// currently uses a single <see cref="double"/> representation for all numbers
	/// (no separate integer sub-type), functions such as <c>math.tointeger</c> and
	/// <c>math.type</c> detect integer-valued floats heuristically.
	/// </para>
	/// </remarks>
	public sealed class MathLibrary : LuaTableBaseLibrary
	{
		private static readonly LuaNumber MaxInteger = new LuaNumber(long.MaxValue);
		private static readonly LuaNumber MinInteger = new LuaNumber(long.MinValue);

		/// <summary>
		/// Gets the namespace name <c>"math"</c>.
		/// </summary>
		public override string Namespace => "math";

		/// <summary>
		/// Populates the math table with constants and functions.
		/// </summary>
		protected override void PopulateTable(LuaState state, LuaTable table)
		{
			// ── Constants ──────────────────────────────────────────────
			table.Set(new LuaString("pi"), new LuaNumber(Math.PI));
			table.Set(new LuaString("huge"), new LuaNumber(double.PositiveInfinity));
			table.Set(new LuaString("maxinteger"), MaxInteger);
			table.Set(new LuaString("mininteger"), MinInteger);

			// ── Rounding and absolute value ────────────────────────────
			table.Set(new LuaString("abs"), new LuaCallbackFunction(MathAbs, "math.abs"));
			table.Set(new LuaString("floor"), new LuaCallbackFunction(MathFloor, "math.floor"));
			table.Set(new LuaString("ceil"), new LuaCallbackFunction(MathCeil, "math.ceil"));
			table.Set(new LuaString("modf"), new LuaCallbackFunction(MathModf, "math.modf"));
			table.Set(new LuaString("tointeger"), new LuaCallbackFunction(MathToInteger, "math.tointeger"));

			// ── Square root ────────────────────────────────────────────
			table.Set(new LuaString("sqrt"), Unary(Math.Sqrt, "math.sqrt"));

			// ── Trigonometric ──────────────────────────────────────────
			table.Set(new LuaString("sin"), Unary(Math.Sin, "math.sin"));
			table.Set(new LuaString("cos"), Unary(Math.Cos, "math.cos"));
			table.Set(new LuaString("tan"), Unary(Math.Tan, "math.tan"));
			table.Set(new LuaString("asin"), Unary(Math.Asin, "math.asin"));
			table.Set(new LuaString("acos"), Unary(Math.Acos, "math.acos"));
			table.Set(new LuaString("atan"), new LuaCallbackFunction(MathAtan, "math.atan"));

			// ── Hyperbolic ─────────────────────────────────────────────
			table.Set(new LuaString("sinh"), Unary(Math.Sinh, "math.sinh"));
			table.Set(new LuaString("cosh"), Unary(Math.Cosh, "math.cosh"));
			table.Set(new LuaString("tanh"), Unary(Math.Tanh, "math.tanh"));

			// ── Logarithmic / exponential / power ──────────────────────
			table.Set(new LuaString("log"), new LuaCallbackFunction(MathLog, "math.log"));
			table.Set(new LuaString("log10"), Unary(Math.Log10, "math.log10"));
			table.Set(new LuaString("exp"), Unary(Math.Exp, "math.exp"));
			table.Set(new LuaString("ldexp"), new LuaCallbackFunction(MathLdexp, "math.ldexp"));
			table.Set(new LuaString("frexp"), new LuaCallbackFunction(MathFrexp, "math.frexp"));

			// ── Remainder / modulo ─────────────────────────────────────
			table.Set(new LuaString("fmod"), new LuaCallbackFunction(MathFmod, "math.fmod"));

			// ── Min / max (variable arguments) ─────────────────────────
			table.Set(new LuaString("min"), new LuaCallbackFunction(MathMin, "math.min"));
			table.Set(new LuaString("max"), new LuaCallbackFunction(MathMax, "math.max"));

			// ── Random ─────────────────────────────────────────────────
			var random = new Random();
			table.Set(new LuaString("random"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length == 0)
						return new LuaTuple(new LuaNumber(random.NextDouble()));

					if (!args[0].TryToNumber(out var mVal) || mVal != Math.Truncate(mVal))
						throw new LuaRuntimeException("math.random: argument #1 must be an integer");

					var m = (int)mVal;

					if (args.Length == 1)
					{
						if (m == 0)
							return new LuaTuple(new LuaNumber((double)(long)(random.Next())));
						return new LuaTuple(new LuaNumber(random.Next(1, m + 1)));
					}

					if (!args[1].TryToNumber(out var nVal) || nVal != Math.Truncate(nVal))
						throw new LuaRuntimeException("math.random: argument #2 must be an integer");

					var n = (int)nVal;

					if (m > n)
						throw new LuaRuntimeException("math.random: interval is empty");

					return new LuaTuple(new LuaNumber(random.Next(m, n + 1)));
				}, "math.random"));

			table.Set(new LuaString("randomseed"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length == 0)
					{
						random = new Random();
						return LuaTuple.Empty;
					}

					if (!args[0].TryToNumber(out var seed))
						throw new LuaRuntimeException("math.randomseed: argument #1 must be a number");

					random = new Random((int)seed);
					return LuaTuple.Empty;
				}, "math.randomseed"));

			// ── Conversion ─────────────────────────────────────────────
			table.Set(new LuaString("deg"), new LuaCallbackFunction(MathDeg, "math.deg"));
			table.Set(new LuaString("rad"), new LuaCallbackFunction(MathRad, "math.rad"));

			// ── Type and comparison ────────────────────────────────────
			table.Set(new LuaString("type"), new LuaCallbackFunction(MathType, "math.type"));
			table.Set(new LuaString("ult"), new LuaCallbackFunction(MathUlt, "math.ult"));

			// ── Deprecated (for compatibility) ──────────────────────────────
			table.Set(new LuaString("atan2"), new LuaCallbackFunction(MathAtan, "math.atan2"));
			table.Set(new LuaString("pow"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					var x = CheckNumber(args, 0, "pow");
					var y = CheckNumber(args, 1, "pow");
					return new LuaTuple(new LuaNumber(Math.Pow(x, y)));
				}, "math.pow"));


			// ── AsyncLua extensions ────────────────────────────────────
			table.Set(new LuaString("sign"), new LuaCallbackFunction(MathSign, "math.sign"));
			table.Set(new LuaString("clamp"), new LuaCallbackFunction(MathClamp, "math.clamp"));
		}

		// ═══════════════════════════════════════════════════════════════════
		//  Callback implementations
		// ═══════════════════════════════════════════════════════════════════

		private static LuaTuple MathAbs(LuaCallingContext ctx, LuaValue[] args)
		{
			var x = CheckNumber(args, 0, "abs");
			return new LuaTuple(new LuaNumber(Math.Abs(x)));
		}

		private static LuaTuple MathFloor(LuaCallingContext ctx, LuaValue[] args)
		{
			var x = CheckNumber(args, 0, "floor");
			return new LuaTuple(PushNumInt(Math.Floor(x)));
		}

		private static LuaTuple MathCeil(LuaCallingContext ctx, LuaValue[] args)
		{
			var x = CheckNumber(args, 0, "ceil");
			return new LuaTuple(PushNumInt(Math.Ceiling(x)));
		}

		private static LuaTuple MathModf(LuaCallingContext ctx, LuaValue[] args)
		{
			var x = CheckNumber(args, 0, "modf");

			// Check for integer value
			if (x == Math.Truncate(x) && !double.IsInfinity(x))
			{
				// Integer: integer part is itself, fractional part is 0
				return new LuaTuple(PushNumInt(x), new LuaNumber(0.0));
			}

			// Floating-point: use truncation toward zero
			double intPart = x < 0 ? Math.Ceiling(x) : Math.Floor(x);
			double fracPart = x == intPart ? 0.0 : x - intPart;

			return new LuaTuple(PushNumInt(intPart), new LuaNumber(fracPart));
		}

		private static LuaTuple MathToInteger(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				return new LuaTuple(LuaNil.Instance);

			if (!args[0].TryToNumber(out var x))
			{
				// Not a number at all
				return new LuaTuple(LuaNil.Instance);
			}

			// Check if it's an integer value within long range
			if (x == Math.Truncate(x) && x >= long.MinValue && x <= long.MaxValue)
				return new LuaTuple(new LuaNumber(x));

			return new LuaTuple(LuaNil.Instance);
		}

		private static LuaTuple MathAtan(LuaCallingContext ctx, LuaValue[] args)
		{
			var y = CheckNumber(args, 0, "atan");

			if (args.Length < 2 || args[1] is LuaNil)
			{
				return new LuaTuple(new LuaNumber(Math.Atan(y)));
			}

			var x = CheckNumber(args, 1, "atan");
			return new LuaTuple(new LuaNumber(Math.Atan2(y, x)));
		}

		private static LuaTuple MathLog(LuaCallingContext ctx, LuaValue[] args)
		{
			var x = CheckNumber(args, 0, "log");

			if (args.Length < 2 || args[1] is LuaNil)
			{
				return new LuaTuple(new LuaNumber(Math.Log(x)));
			}

			var baseVal = CheckNumber(args, 1, "log");

			if (baseVal == 2.0)
				return new LuaTuple(new LuaNumber(Math.Log(x, 2.0)));
			if (baseVal == 10.0)
				return new LuaTuple(new LuaNumber(Math.Log10(x)));

			return new LuaTuple(new LuaNumber(Math.Log(x, baseVal)));
		}

		private static LuaTuple MathLdexp(LuaCallingContext ctx, LuaValue[] args)
		{
			var m = CheckNumber(args, 0, "ldexp");
			if (!args[1].TryToNumber(out var eRaw) || eRaw != Math.Truncate(eRaw))
				throw new LuaRuntimeException("math.ldexp: argument #2 must be an integer");

			int e = (int)eRaw;
			return new LuaTuple(new LuaNumber(m * Math.Pow(2.0, e)));
		}

		private static LuaTuple MathFrexp(LuaCallingContext ctx, LuaValue[] args)
		{
			var x = CheckNumber(args, 0, "frexp");

			if (double.IsNaN(x) || double.IsInfinity(x) || x == 0.0)
				return new LuaTuple(new LuaNumber(x), LuaNumber.Zero);

			ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(x));
			int exponent = (int)((bits >> 52) & 0x7FF) - 1022;

			// Clear exponent bits and set to 1022 (unbiased exponent = -1)
			// so the resulting value is in [0.5, 1.0).
			bits &= 0x800FFFFFFFFFFFFFUL; // sign + mantissa
			bits |= 0x3FE0000000000000UL; // exponent = 1022
			double mantissa = BitConverter.Int64BitsToDouble(unchecked((long)bits));

			return new LuaTuple(new LuaNumber(mantissa), new LuaNumber(exponent));
		}

		private static LuaTuple MathFmod(LuaCallingContext ctx, LuaValue[] args)
		{
			var x = CheckNumber(args, 0, "fmod");
			var y = CheckNumber(args, 1, "fmod");

			if (y == 0.0)
				throw new LuaRuntimeException("math.fmod: division by zero");

			return new LuaTuple(new LuaNumber(x % y));
		}

		private static LuaTuple MathMin(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				throw new LuaRuntimeException("math.min: value expected");

			double minVal = double.PositiveInfinity;
			int minIdx = 0;

			for (int i = 0; i < args.Length; i++)
			{
				if (!args[i].TryToNumber(out var val))
					throw new LuaRuntimeException($"math.min: argument #{i + 1} is not a number");
				if (val < minVal || (i == 0))
				{
					minVal = val;
					minIdx = i;
				}
			}

			return new LuaTuple(new LuaNumber(minVal));
		}

		private static LuaTuple MathMax(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0)
				throw new LuaRuntimeException("math.max: value expected");

			double maxVal = double.NegativeInfinity;

			for (int i = 0; i < args.Length; i++)
			{
				if (!args[i].TryToNumber(out var val))
					throw new LuaRuntimeException($"math.max: argument #{i + 1} is not a number");
				if (val > maxVal || (i == 0))
					maxVal = val;
			}

			return new LuaTuple(new LuaNumber(maxVal));
		}

		private static LuaTuple MathDeg(LuaCallingContext ctx, LuaValue[] args)
		{
			var x = CheckNumber(args, 0, "deg");
			return new LuaTuple(new LuaNumber(x * 180.0 / Math.PI));
		}

		private static LuaTuple MathRad(LuaCallingContext ctx, LuaValue[] args)
		{
			var x = CheckNumber(args, 0, "rad");
			return new LuaTuple(new LuaNumber(x * Math.PI / 180.0));
		}

		private static LuaTuple MathType(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || !args[0].TryToNumber(out var x))
				return new LuaTuple(LuaNil.Instance);

			// Heuristic: if the value is an exact integer, call it "integer"
			if (x == Math.Truncate(x) && !double.IsInfinity(x))
				return new LuaTuple(new LuaString("integer"));

			return new LuaTuple(new LuaString("float"));
		}

		private static LuaTuple MathUlt(LuaCallingContext ctx, LuaValue[] args)
		{
			if (!args[0].TryToNumber(out var a) || a != Math.Truncate(a))
				throw new LuaRuntimeException("math.ult: argument #1 must be an integer");

			if (!args[1].TryToNumber(out var b) || b != Math.Truncate(b))
				throw new LuaRuntimeException("math.ult: argument #2 must be an integer");

			unchecked
			{
				bool result = (ulong)(long)a < (ulong)(long)b;
				return new LuaTuple(LuaBoolean.FromBoolean(result));
			}
		}

		private static LuaTuple MathSign(LuaCallingContext ctx, LuaValue[] args)
		{
			var x = CheckNumber(args, 0, "sign");
			return new LuaTuple(new LuaNumber(Math.Sign(x)));
		}

		private static LuaTuple MathClamp(LuaCallingContext ctx, LuaValue[] args)
		{
			var value = CheckNumber(args, 0, "clamp");
			var min = CheckNumber(args, 1, "clamp");
			var max = CheckNumber(args, 2, "clamp");

			if (min > max)
				throw new LuaRuntimeException("math.clamp: min must not exceed max");

			double clamped = value < min ? min : value > max ? max : value;
			return new LuaTuple(new LuaNumber(clamped));
		}

		// ═══════════════════════════════════════════════════════════════════
		//  Helper methods
		// ═══════════════════════════════════════════════════════════════════

		/// <summary>
		/// Extracts a <see cref="double"/> from the argument at <paramref name="index"/>,
		/// throwing a descriptive <see cref="LuaRuntimeException"/> if it is not a number.
		/// </summary>
		private static double CheckNumber(LuaValue[] args, int index, string fnName)
		{
			if (index >= args.Length)
				throw new LuaRuntimeException($"math.{fnName}: argument #{index + 1} expected");

			if (!args[index].TryToNumber(out var val))
				throw new LuaRuntimeException($"math.{fnName}: argument #{index + 1} must be a number");

			return val;
		}

		/// <summary>
		/// Returns a <see cref="LuaNumber"/> from a <see cref="double"/>,
		/// using the shared constants for common values.
		/// </summary>
		private static LuaValue PushNumInt(double d)
		{
			if (d == 0.0)
				return LuaNumber.Zero;
			if (d == 1.0)
				return LuaNumber.One;
			return new LuaNumber(d);
		}

		/// <summary>
		/// Creates a unary math callback that checks its argument and applies <paramref name="fn"/>.
		/// </summary>
		private static LuaCallbackFunction Unary(Func<double, double> fn, string name)
		{
			return new LuaCallbackFunction(
				(ctx, args) =>
				{
					var x = CheckNumber(args, 0, name);
					return new LuaTuple(new LuaNumber(fn(x)));
				},
				name);
		}
	}
}
