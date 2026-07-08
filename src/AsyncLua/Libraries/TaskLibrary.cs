using System.Linq;
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
		}
	}
}
