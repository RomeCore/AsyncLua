using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AsyncLua.Values
{
	/// <summary>
	/// A Lua function implemented as a C# callback. Supports both synchronous and
	/// asynchronous delegates, single and multiple return values.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Use the static factory methods (<see cref="From"/>, <see cref="FromAsync"/>, etc.)
	/// or the constructor overloads to create instances from lambdas or method references.
	/// </para>
	/// </remarks>
	public sealed class LuaCallbackFunction : LuaFunction
	{
		/// <summary>
		/// Delegate signature for C#-implemented Lua functions.
		/// Receives a calling context and arguments, and returns results.
		/// </summary>
		/// <param name="context">The calling context, providing access to the Lua runtime.</param>
		/// <param name="args">The arguments passed to the function.</param>
		/// <returns>A value or array of values returned by the function.</returns>
		public delegate LuaTuple CallbackDelegate(LuaCallingContext context, LuaValue[] args);

		/// <summary>
		/// Delegate signature for C#-implemented Lua async functions.
		/// Receives a calling context and arguments, and returns results asynchronously.
		/// </summary>
		/// <param name="context">The calling context, providing access to the Lua runtime.</param>
		/// <param name="args">The arguments passed to the function.</param>
		/// <returns>A task that resolves to the function's return values.</returns>
		public delegate Task<LuaTuple> AsyncCallbackDelegate(LuaCallingContext context, LuaValue[] args);

		private readonly AsyncCallbackDelegate _callback;
		private readonly string? _name;

		public override bool IsAsync { get; }

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaCallbackFunction"/> class.
		/// </summary>
		/// <param name="callback">The delegate to invoke when this function is called.</param>
		/// <param name="name">An optional display name for debugging.</param>
		/// <param name="isAsync">Indicates whether the function is asynchronous.</param>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="callback"/> is <see langword="null"/>.
		/// </exception>
		public LuaCallbackFunction(CallbackDelegate callback, string? name = null, bool isAsync = false)
		{
			_callback = callback is null ? throw new ArgumentNullException(nameof(callback)) :
				new AsyncCallbackDelegate((ctx, args) => Task.FromResult(callback(ctx, args)));
			_name = name;
			IsAsync = isAsync;

		}

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaCallbackFunction"/> class.
		/// </summary>
		/// <param name="callback">The delegate to invoke when this function is called.</param>
		/// <param name="name">An optional display name for debugging.</param>
		/// <param name="isAsync">Indicates whether the function is asynchronous.</param>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="callback"/> is <see langword="null"/>.
		/// </exception>
		public LuaCallbackFunction(AsyncCallbackDelegate callback, string? name = null, bool isAsync = true)
		{
			_callback = callback ?? throw new ArgumentNullException(nameof(callback));
			_name = name;
			IsAsync = isAsync;
		}

		/// <inheritdoc />
		/// <remarks>
		/// A callback is host code, and a .NET exception thrown by it is what Lua calls a runtime
		/// error, so it is converted here - at the single synchronous point every library function,
		/// host binding and generated userdata method passes through. Only synchronous throws are
		/// handled: a fault that surfaces later belongs to the task the caller awaits, and the
		/// interpreter converts it there. Wrapping the task here would add an await layer between
		/// a coroutine and its resume handshake.
		/// </remarks>
		public override Task<LuaTuple> InvokeAsync(LuaCallingContext context, params LuaValue[] args)
		{
			try
			{
				return _callback(context, args);
			}
			catch (Exception ex) when (!IsPassThrough(ex))
			{
				throw new LuaRuntimeException(ex.Message, ex);
			}
		}

		/// <summary>
		/// Reports exceptions that must keep travelling as themselves: Lua errors already carry a
		/// value, and cancellation is the host's decision rather than a Lua failure.
		/// </summary>
		private static bool IsPassThrough(Exception ex) =>
			ex is LuaRuntimeException || ex is OperationCanceledException;

		/// <inheritdoc />
		public override string ToString()
		{
			return _name is not null
				? $"function: {_name}"
				: $"function: {RuntimeHelpers.GetHashCode(this):X}";
		}

		// ── Static factories ─────────────────────────────────────────────

		/// <summary>
		/// Creates a <see cref="LuaCallbackFunction"/> from a synchronous delegate
		/// that returns a single <see cref="LuaValue"/>.
		/// </summary>
		/// <param name="func">The synchronous callback.</param>
		/// <param name="name">An optional display name for debugging.</param>
		/// <returns>A new <see cref="LuaCallbackFunction"/>.</returns>
		public static LuaCallbackFunction From(
			Func<LuaValue[], LuaValue> func,
			string? name = null)
		{
			if (func is null)
				throw new ArgumentNullException(nameof(func));

			return new LuaCallbackFunction(
				(ctx, args) => new LuaTuple(func(args)),
				name);
		}

		/// <summary>
		/// Creates a <see cref="LuaCallbackFunction"/> from a synchronous delegate
		/// that returns multiple <see cref="LuaValue"/> objects as a <see cref="LuaTuple"/>.
		/// </summary>
		/// <param name="func">The synchronous callback.</param>
		/// <param name="name">An optional display name for debugging.</param>
		/// <returns>A new <see cref="LuaCallbackFunction"/>.</returns>
		public static LuaCallbackFunction From(
			Func<LuaValue[], LuaTuple> func,
			string? name = null)
		{
			if (func is null)
				throw new ArgumentNullException(nameof(func));

			return new LuaCallbackFunction(
				(ctx, args) => func(args),
				name);
		}

		/// <summary>
		/// Creates a <see cref="LuaCallbackFunction"/> from an asynchronous delegate
		/// that returns a single <see cref="LuaValue"/>.
		/// </summary>
		/// <param name="func">The asynchronous callback.</param>
		/// <param name="name">An optional display name for debugging.</param>
		/// <returns>A new <see cref="LuaCallbackFunction"/>.</returns>
		public static LuaCallbackFunction FromAsync(
			Func<LuaValue[], Task<LuaValue>> func,
			string? name = null)
		{
			if (func is null)
				throw new ArgumentNullException(nameof(func));

			return new LuaCallbackFunction(
				async (ctx, args) => new LuaTuple(await func(args)),
				name);
		}

		/// <summary>
		/// Creates a <see cref="LuaCallbackFunction"/> from an asynchronous delegate
		/// that returns multiple <see cref="LuaValue"/> objects as a <see cref="LuaTuple"/>.
		/// </summary>
		/// <param name="func">The asynchronous callback.</param>
		/// <param name="name">An optional display name for debugging.</param>
		/// <returns>A new <see cref="LuaCallbackFunction"/>.</returns>
		public static LuaCallbackFunction FromAsync(
			Func<LuaValue[], Task<LuaTuple>> func,
			string? name = null)
		{
			if (func is null)
				throw new ArgumentNullException(nameof(func));

			return new LuaCallbackFunction(
				new AsyncCallbackDelegate((ctx, args) => func(args)),
				name);
		}

		/// <summary>
		/// Creates a <see cref="LuaCallbackFunction"/> from a parameterless synchronous delegate.
		/// </summary>
		/// <param name="func">The synchronous callback that takes no arguments.</param>
		/// <param name="name">An optional display name for debugging.</param>
		/// <returns>A new <see cref="LuaCallbackFunction"/>.</returns>
		public static LuaCallbackFunction From(
			Func<LuaValue> func,
			string? name = null)
		{
			if (func is null)
				throw new ArgumentNullException(nameof(func));

			return new LuaCallbackFunction(
				(ctx, args) => new LuaTuple(func()),
				name);
		}

		/// <summary>
		/// Creates a <see cref="LuaCallbackFunction"/> from a parameterless asynchronous delegate.
		/// </summary>
		/// <param name="func">The asynchronous callback that takes no arguments.</param>
		/// <param name="name">An optional display name for debugging.</param>
		/// <returns>A new <see cref="LuaCallbackFunction"/>.</returns>
		public static LuaCallbackFunction FromAsync(
			Func<Task<LuaValue>> func,
			string? name = null)
		{
			if (func is null)
				throw new ArgumentNullException(nameof(func));

			return new LuaCallbackFunction(
				async (ctx, args) => new LuaTuple(await func()),
				name);
		}
	}
}
