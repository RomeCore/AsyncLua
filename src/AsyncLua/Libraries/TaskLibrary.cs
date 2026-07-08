using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncLua.Values;

namespace AsyncLua.Libraries
{
	/// <summary>
	/// Implements the <c>task</c> library for asynchronous task management.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Provides functions to create, schedule, and control asynchronous tasks
	/// within the Lua runtime. This library is an AsyncLua extension and is
	/// not present in standard Lua.
	/// </para>
	/// </remarks>
	public sealed class TaskLibrary : LuaTableBaseLibrary
	{
		/// <summary>
		/// Gets the namespace name <c>"task"</c>.
		/// </summary>
		public override string Namespace => "task";

		/// <summary>
		/// Populates the task library with functions.
		/// </summary>
		protected override void PopulateTable(LuaState state, LuaTable table)
		{
			table.Set(new LuaString("delay"), new LuaCallbackFunction(
				async (ctx, args) =>
				{
					if (args.Length == 0 || !args[0].TryToNumber(out var delayMs))
						throw new LuaRuntimeException("delay: expected a number as argument");

					await Task.Delay((int)delayMs);
					return LuaTuple.Empty;
				}, "task.delay"));

			table.Set(new LuaString("run"), new LuaCallbackFunction(
				(ctx, args) =>
				{
					if (args.Length == 0 || args[0] is not LuaFunction func)
						throw new LuaRuntimeException("run: expected a function as argument");

					return Task.Run(() =>
					{
						return func.InvokeAsync(ctx, args.Skip(1).ToArray());
					});
				}, "task.run", isAsync: true));

			table.Set(new LuaString("pararun"), new LuaCallbackFunction(
				async (ctx, args) =>
				{
					if (args.Length < 2)
						throw new LuaRuntimeException(
							"pararun: expected at least two arguments (table, callback, [concurrency])");

					if (args[0] is not LuaTable sourceTable)
						throw new LuaRuntimeException(
							"pararun: expected a table as first argument");

					if (args[1] is not LuaFunction callback)
						throw new LuaRuntimeException(
							"pararun: expected a function as second argument");

					// Collect all (key, value) pairs from the table.
					var pairs = new List<KeyValuePair<LuaValue, LuaValue>>();
					foreach (var kv in sourceTable)
					{
						if (kv.Key is LuaNil)
							continue;
						pairs.Add(kv);
					}

					if (pairs.Count == 0)
						return new LuaTuple(new LuaTable());

					// Optional third argument: concurrency limit.
					int? concurrencyLimit = null;
					if (args.Length > 2)
					{
						if (!args[2].TryToNumber(out var num) || num < 1)
							throw new LuaRuntimeException(
								"pararun: concurrency limit must be a positive number");

						concurrencyLimit = (int)num;
					}

					if (concurrencyLimit.HasValue)
					{
						using var semaphore = new SemaphoreSlim(
							concurrencyLimit.Value, concurrencyLimit.Value);

						var tasks = new Task<KeyValuePair<LuaValue, LuaValue>>[pairs.Count];
						for (int i = 0; i < pairs.Count; i++)
						{
							var kv = pairs[i];
							tasks[i] = InvokeCallbackWithThrottleAsync(
								ctx, callback, kv.Value, kv.Key, semaphore);
						}

						var results = await Task.WhenAll(tasks);
						return new LuaTuple(CollectResults(results));
					}

					// No concurrency limit — launch all at once.
					var allTasks = new Task<KeyValuePair<LuaValue, LuaValue>>[pairs.Count];
					for (int i = 0; i < pairs.Count; i++)
					{
						var kv = pairs[i];
						allTasks[i] = InvokeCallbackWithThrottleAsync(
							ctx, callback, kv.Value, kv.Key, null);
					}

					var allResults = await Task.WhenAll(allTasks);
					return new LuaTuple(CollectResults(allResults));
				}, "task.pararun", isAsync: true));
		}

		/// <summary>
		/// Invokes the callback with the given value and key, returning a key-value pair
		/// where the key is preserved and the value is the callback's result.
		/// If a <paramref name="semaphore"/> is provided, the callback is invoked only
		/// after acquiring a slot (used for throttling).
		/// If the callback returns multiple values, they are wrapped in a <see cref="LuaTuple"/>.
		/// </summary>
		private static async Task<KeyValuePair<LuaValue, LuaValue>> InvokeCallbackWithThrottleAsync(
			LuaCallingContext ctx, LuaFunction callback, LuaValue value, LuaValue key,
			SemaphoreSlim? semaphore)
		{
			if (semaphore is not null)
				await semaphore.WaitAsync();

			try
			{
				var result = await callback.InvokeAsync(ctx, value, key);

				var resultValue = result.Count switch
				{
					0 => LuaNil.Instance,
					1 => result[0],
					_ => result
				};

				return new KeyValuePair<LuaValue, LuaValue>(key, resultValue);
			}
			finally
			{
				semaphore?.Release();
			}
		}

		/// <summary>
		/// Collects results from completed parallel tasks into a <see cref="LuaTable"/>.
		/// </summary>
		private static LuaTable CollectResults(KeyValuePair<LuaValue, LuaValue>[] results)
		{
			var table = new LuaTable(results.Length);
			foreach (var r in results)
				table.Set(r.Key, r.Value);
			return table;
		}
	}
}
