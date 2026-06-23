using System;
using System.Runtime.CompilerServices;

namespace AsyncLua.Values
{
	/// <summary>
	/// Provides the awaiter pattern for <see cref="LuaTask"/>, enabling C# <c>await</c> syntax.
	/// </summary>
	/// <remarks>
	/// This struct implements <see cref="INotifyCompletion"/> and exposes
	/// <see cref="IsCompleted"/>, <see cref="OnCompleted"/>, and <see cref="GetResult"/>
	/// as required by the C# compiler for the async/await pattern.
	/// </remarks>
	public readonly struct LuaTaskAwaiter : INotifyCompletion
	{
		private readonly LuaTask _task;

		/// <summary>
		/// Initialises a new instance of the <see cref="LuaTaskAwaiter"/> struct.
		/// </summary>
		/// <param name="task">The task to await.</param>
		internal LuaTaskAwaiter(LuaTask task)
		{
			_task = task ?? throw new ArgumentNullException(nameof(task));
		}

		/// <summary>
		/// Gets whether the awaited task has completed.
		/// </summary>
		public bool IsCompleted => _task.IsCompleted;

		/// <summary>
		/// Registers a continuation to be invoked when the task completes.
		/// </summary>
		/// <param name="continuation">The continuation action.</param>
		public void OnCompleted(Action continuation)
		{
			if (continuation is null)
				throw new ArgumentNullException(nameof(continuation));

			_task.OnCompleted(_ => continuation());
		}

		/// <summary>
		/// Gets the result of the completed task as a <see cref="LuaTuple"/>.
		/// </summary>
		/// <returns>The result values of the task.</returns>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the task is not completed successfully.
		/// </exception>
		public LuaTuple GetResult() => _task.Result;
	}
}
