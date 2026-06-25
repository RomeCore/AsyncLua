using System;
using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/// <summary>
	/// Implements the standard Lua <c>math</c> library with trigonometric, rounding,
	/// and utility functions.
	/// </summary>
	public sealed class MathLibrary : LuaTableBaseLibrary
	{
		/// <summary>
		/// Gets the namespace name <c>"math"</c>.
		/// </summary>
		public override string Namespace => "math";

		/// <summary>
		/// Populates the math table with constants and functions.
		/// </summary>
		protected override void PopulateTable(LuaState state, LuaTable table)
		{
			// Constants
			table.Set(new LuaString("pi"), new LuaNumber(Math.PI));
			table.Set(new LuaString("huge"), new LuaNumber(double.PositiveInfinity));

			// Rounding
			table.Set(new LuaString("floor"), Unary(Math.Floor, "math.floor"));
			table.Set(new LuaString("ceil"), Unary(Math.Ceiling, "math.ceil"));
			table.Set(new LuaString("abs"), Unary(Math.Abs, "math.abs"));

			// Square root
			table.Set(new LuaString("sqrt"), Unary(Math.Sqrt, "math.sqrt"));

			// Trigonometric
			table.Set(new LuaString("sin"), Unary(Math.Sin, "math.sin"));
			table.Set(new LuaString("cos"), Unary(Math.Cos, "math.cos"));
			table.Set(new LuaString("tan"), Unary(Math.Tan, "math.tan"));
			table.Set(new LuaString("asin"), Unary(Math.Asin, "math.asin"));
			table.Set(new LuaString("acos"), Unary(Math.Acos, "math.acos"));
			table.Set(new LuaString("atan"), Unary(Math.Atan, "math.atan"));
			table.Set(new LuaString("atan2"), Binary(Math.Atan2, "math.atan2"));

			// Hyperbolic
			table.Set(new LuaString("sinh"), Unary(Math.Sinh, "math.sinh"));
			table.Set(new LuaString("cosh"), Unary(Math.Cosh, "math.cosh"));
			table.Set(new LuaString("tanh"), Unary(Math.Tanh, "math.tanh"));

			// Logarithmic / exponential
			table.Set(new LuaString("log"), Unary(Math.Log, "math.log"));
			table.Set(new LuaString("log10"), Unary(Math.Log10, "math.log10"));
			table.Set(new LuaString("exp"), Unary(Math.Exp, "math.exp"));

			// Min / max (variable arguments)
			table.Set(new LuaString("min"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					double min = double.PositiveInfinity;
					for (int i = 0; i < args.Length; i++)
					{
						if (args[i].TryToNumber(out var val))
							min = Math.Min(min, val);
					}
					return new LuaTuple(new LuaNumber(min));
				}, "math.min"));
			table.Set(new LuaString("max"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					double max = double.NegativeInfinity;
					for (int i = 0; i < args.Length; i++)
					{
						if (args[i].TryToNumber(out var val))
							max = Math.Max(max, val);
					}
					return new LuaTuple(new LuaNumber(max));
				}, "math.max"));

			// Random
			var random = new Random();
			table.Set(new LuaString("random"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length == 0)
						return new LuaTuple(new LuaNumber(random.NextDouble()));

					var m = (int)((LuaNumber)args[0]).Value;
					if (args.Length == 1)
						return new LuaTuple(new LuaNumber(random.Next(1, m + 1)));

					var n = (int)((LuaNumber)args[1]).Value;
					return new LuaTuple(new LuaNumber(random.Next(m, n + 1)));
				}, "math.random"));

			table.Set(new LuaString("randomseed"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length > 0)
						random = new Random((int)((LuaNumber)args[0]).Value);
					return LuaTuple.Empty;
				}, "math.randomseed"));

			// Utility
			table.Set(new LuaString("deg"), Unary(v => v * 180.0 / Math.PI, "math.deg"));
			table.Set(new LuaString("rad"), Unary(v => v * Math.PI / 180.0, "math.rad"));
			table.Set(new LuaString("sign"), Unary(v => Math.Sign(v), "math.sign"));
			table.Set(new LuaString("clamp"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					var value = ((LuaNumber)args[0]).Value;
					var min = ((LuaNumber)args[1]).Value;
					var max = ((LuaNumber)args[2]).Value;
					var clamped = value < min ? min : value > max ? max : value;
					return new LuaTuple(new LuaNumber(clamped));
				}, "math.clamp"));
		}

		private static LuaCallbackFunction Unary(Func<double, double> fn, string name)
		{
			return new LuaCallbackFunction(
				(ctx, args) => new LuaTuple(new LuaNumber(fn(((LuaNumber)args[0]).Value))),
				name);
		}

		private static LuaCallbackFunction Binary(Func<double, double, double> fn, string name)
		{
			return new LuaCallbackFunction(
				(ctx, args) => new LuaTuple(new LuaNumber(
					fn(((LuaNumber)args[0]).Value, ((LuaNumber)args[1]).Value))),
				name);
		}
	}
}
