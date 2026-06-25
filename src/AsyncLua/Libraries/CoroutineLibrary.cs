using System;
using System.Threading.Tasks;
using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/// <summary>
	/// Implements the standard Lua <c>coroutine</c> library with functions for
	/// creating, resuming, and yielding coroutines.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This library provides cooperative multitasking via async-handshake coroutines.
	/// Coroutines are <see cref="LuaThread"/> values that use
	/// <see cref="TaskCompletionSource{T}"/> pairs for yield/resume:
	/// </para>
	/// <list type="bullet">
	///   <item><description><c>coroutine.create(f)</c> — creates a suspended coroutine from a function.</description></item>
	///   <item><description><c>coroutine.resume(co, ...)</c> — starts or resumes a coroutine (must be awaited!).</description></item>
	///   <item><description><c>coroutine.yield(...)</c> — suspends the current coroutine (must be awaited!).</description></item>
	///   <item><description><c>coroutine.status(co)</c> — returns "suspended", "running", or "dead".</description></item>
	///   <item><description><c>coroutine.wrap(f)</c> — returns an async function that resumes the coroutine.</description></item>
	///   <item><description><c>coroutine.running()</c> — returns the currently running coroutine.</description></item>
	/// </list>
	/// <para>
	/// Because <c>yield</c> uses <c>await</c> internally, the coroutine function
	/// and all callers of <c>coroutine.resume</c> must be inside an async context.
	/// Use the <c>async</c> keyword on the Lua function and <c>await</c> for
	/// both <c>yield</c> and <c>resume</c>:
	/// <code>
	/// local co = coroutine.create(async function()
	///     local x = await coroutine.yield(10)
	///     return x + 20
	/// end)
	/// local ok, y = await coroutine.resume(co)    -- y = 10
	/// local ok, r = await coroutine.resume(co, 100) -- r = 120
	/// </code>
	/// </para>
	/// </remarks>
	public sealed class CoroutineLibrary : LuaTableBaseLibrary
	{
		/// <summary>
		/// Gets the namespace name <c>"coroutine"</c>.
		/// </summary>
		public override string Namespace => "coroutine";

		/// <summary>
		/// Populates the coroutine table with functions.
		/// </summary>
		protected override void PopulateTable(LuaState state, LuaTable table)
		{
			// All methods that interact with the yield/resume handshake
			// use AsyncCallbackDelegate because they call async methods.
			table.Set(new LuaString("create"), new LuaCallbackFunction(Create, "coroutine.create"));
			table.Set(new LuaString("resume"), new LuaCallbackFunction(
				new LuaCallbackFunction.AsyncCallbackDelegate(ResumeAsync), "coroutine.resume"));
			table.Set(new LuaString("yield"), new LuaCallbackFunction(
				new LuaCallbackFunction.AsyncCallbackDelegate(YieldAsync), "coroutine.yield"));
			table.Set(new LuaString("status"), new LuaCallbackFunction(Status, "coroutine.status"));
			table.Set(new LuaString("wrap"), new LuaCallbackFunction(
				new LuaCallbackFunction.AsyncCallbackDelegate(WrapAsync), "coroutine.wrap"));
			table.Set(new LuaString("running"), new LuaCallbackFunction(Running, "coroutine.running"));
		}

		private static LuaTuple Create(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaFunction func)
				return new LuaTuple(LuaNil.Instance, new LuaString(
					"bad argument #1 to 'create' (function expected)"));

			var thread = new LuaThread(func);
			return new LuaTuple(thread);
		}

		private static async Task<LuaTuple> ResumeAsync(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaThread thread)
				return new LuaTuple(LuaNil.Instance, new LuaString(
					"bad argument #1 to 'resume' (thread expected)"));

			// Collect extra arguments (after the thread).
			var resumeArgs = new LuaValue[args.Length - 1];
			for (int i = 1; i < args.Length; i++)
				resumeArgs[i - 1] = args[i];

			return await thread.ResumeAsync(ctx, resumeArgs);
		}

		private static async Task<LuaTuple> YieldAsync(LuaCallingContext ctx, LuaValue[] args)
		{
			var current = LuaThread.Current.Value;
			if (current == null)
				throw new LuaRuntimeException("no coroutine to yield");

			var nextArgs = await current.YieldAsync(new LuaTuple(args));
			return new LuaTuple(nextArgs);
		}

		private static LuaTuple Status(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaThread thread)
				return new LuaTuple(new LuaString("dead"));

			var status = thread.Status switch
			{
				LuaThreadStatus.Suspended => "suspended",
				LuaThreadStatus.Running => "running",
				LuaThreadStatus.Dead => "dead",
				_ => "dead"
			};

			return new LuaTuple(new LuaString(status));
		}

		private static async Task<LuaTuple> WrapAsync(LuaCallingContext ctx, LuaValue[] args)
		{
			if (args.Length == 0 || args[0] is not LuaFunction func)
				return new LuaTuple(LuaNil.Instance);

			var thread = new LuaThread(func);

			// Returns a callback function that resumes the thread.
			var wrapper = new LuaCallbackFunction(
				new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx2, wrapArgs) =>
				{
					var result = await thread.ResumeAsync(ctx2, wrapArgs);
					if (result.Count > 0 && result[0] is LuaBoolean ok && !ok.Value)
					{
						var err = result.Count > 1 ? result[1].ToString() : "unknown error";
						throw new LuaRuntimeException(err);
					}
					// Strip the boolean status; return only the values.
					var valueCount = result.Count - 1;
					var values = new LuaValue[valueCount];
					for (int i = 0; i < valueCount; i++)
						values[i] = result[i + 1];
					return new LuaTuple(values);
				}), "coroutine.wrap");

			return new LuaTuple(wrapper);
		}

		private static LuaTuple Running(LuaCallingContext ctx, LuaValue[] args)
		{
			var current = LuaThread.Current.Value;
			if (current == null)
				return new LuaTuple(LuaNil.Instance, LuaBoolean.False);
			return new LuaTuple(current, LuaBoolean.False);
		}
	}
}
