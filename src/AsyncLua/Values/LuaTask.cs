using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AsyncLua.Values
{
	/// <summary>
	/// Represents an asynchronous Lua operation that may complete in the future.
	/// Analogous to <see cref="Task{TResult}"/> in .NET, but integrated into the Lua type system.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A <see cref="LuaTask"/> is a <see cref="LuaValue"/> and can be stored in tables, passed to
	/// functions, and awaited from both Lua and C# code.
	/// </para>
	/// <para>
	/// <b>Thread safety:</b> completion and continuation registration are thread-safe.
	/// Multiple threads may call <see cref="SetResult"/> or <see cref="OnCompleted"/> concurrently.
	/// </para>
	/// <para>
	/// <b>C# await:</b> the task exposes a <see cref="GetAwaiter"/> method implementing the
	/// awaiter pattern, so you can write <c>await someLuaTask</c> in C#.
	/// </para>
	/// <para>
	/// <b>Lua await:</b> the Lua runtime (via <c>LuaScheduler</c>) uses <see cref="OnCompleted"/>
	/// to suspend a coroutine until the task finishes.
	/// </para>
	/// </remarks>
	public sealed class LuaTask : LuaValue
	{
		private readonly object _lock = new object();
		private readonly List<Action<LuaTask>>? _continuations;

		private LuaTaskStatus _status;
		private LuaTuple _result;
		private Exception? _exception;

		// ── Constructors ─────────────────────────────────────────────────

		/// <summary>
		/// Initialises a new pending <see cref="LuaTask"/>.
		/// </summary>
		public LuaTask()
		{
			_status = LuaTaskStatus.Pending;
			_result = LuaTuple.Empty;
			_continuations = new List<Action<LuaTask>>();
		}

		private LuaTask(LuaTuple result)
		{
			_status = LuaTaskStatus.Completed;
			_result = result ?? throw new ArgumentNullException(nameof(result));
			_continuations = null; // completed tasks need no continuations
		}

		private LuaTask(Exception exception)
		{
			_status = LuaTaskStatus.Faulted;
			_exception = exception ?? throw new ArgumentNullException(nameof(exception));
			_result = LuaTuple.Empty;
			_continuations = null;
		}

		// ── Static factories ─────────────────────────────────────────────

		/// <summary>
		/// Creates a <see cref="LuaTask"/> that is already completed with the specified results.
		/// </summary>
		/// <param name="results">The return values of the task.</param>
		/// <returns>A completed task.</returns>
		public static LuaTask FromResult(params LuaValue[] results) =>
			new LuaTask(new LuaTuple(results));

		/// <summary>
		/// Creates a <see cref="LuaTask"/> that is already completed with the specified <see cref="LuaTuple"/>.
		/// </summary>
		/// <param name="tuple">The return values as a tuple.</param>
		/// <returns>A completed task.</returns>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="tuple"/> is <see langword="null"/>.
		/// </exception>
		public static LuaTask FromResult(LuaTuple tuple) =>
			new LuaTask(tuple);

		/// <summary>
		/// Creates a <see cref="LuaTask"/> that is already faulted with the specified exception.
		/// </summary>
		/// <param name="exception">The exception that caused the failure.</param>
		/// <returns>A faulted task.</returns>
		public static LuaTask FromException(Exception exception) =>
			new LuaTask(exception);

		/// <summary>
		/// Wraps an existing .NET <see cref="Task{TResult}"/> into a <see cref="LuaTask"/>.
		/// When the .NET task completes, the Lua task is automatically completed.
		/// </summary>
		/// <param name="task">The .NET task to wrap. Must not be <see langword="null"/>.</param>
		/// <returns>
		/// A <see cref="LuaTask"/> that will complete when <paramref name="task"/> completes.
		/// If the .NET task is already completed, the returned task is already completed.
		/// </returns>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="task"/> is <see langword="null"/>.
		/// </exception>
		public static LuaTask FromTask(Task<LuaTuple> task)
		{
			if (task is null)
				throw new ArgumentNullException(nameof(task));

			if (task.Status == TaskStatus.RanToCompletion)
				return FromResult(task.Result);

			if (task.IsFaulted)
				return FromException(task.Exception?.InnerException ?? task.Exception!);

			if (task.IsCanceled)
				return Canceled();

			var luaTask = new LuaTask();
			task.ContinueWith(t =>
			{
				if (t.Status == TaskStatus.RanToCompletion)
					luaTask.SetResult(t.Result);
				else if (t.IsFaulted)
					luaTask.SetException(t.Exception?.InnerException ?? t.Exception!);
				else if (t.IsCanceled)
					luaTask.SetCanceled();
			}, TaskContinuationOptions.ExecuteSynchronously);

			return luaTask;
		}

		/// <summary>
		/// Returns a cancelled <see cref="LuaTask"/>.
		/// </summary>
		/// <returns>A cancelled task.</returns>
		public static LuaTask Canceled()
		{
			var task = new LuaTask();
			task.SetCanceled();
			return task;
		}

		// ── Status / Results ─────────────────────────────────────────────

		/// <summary>
		/// Gets the current status of the task.
		/// </summary>
		public LuaTaskStatus Status
		{
			get
			{
				lock (_lock)
					return _status;
			}
		}

		/// <summary>
		/// Gets whether the task has reached a terminal state
		/// (<see cref="LuaTaskStatus.Completed"/>, <see cref="LuaTaskStatus.Faulted"/>,
		/// or <see cref="LuaTaskStatus.Canceled"/>).
		/// </summary>
		public bool IsCompleted
		{
			get
			{
				lock (_lock)
					return _status != LuaTaskStatus.Pending;
			}
		}

		/// <summary>
		/// Gets whether the task completed successfully.
		/// </summary>
		public bool IsCompletedSuccessfully
		{
			get
			{
				lock (_lock)
					return _status == LuaTaskStatus.Completed;
			}
		}

		/// <summary>
		/// Gets whether the task is faulted.
		/// </summary>
		public bool IsFaulted
		{
			get
			{
				lock (_lock)
					return _status == LuaTaskStatus.Faulted;
			}
		}

		/// <summary>
		/// Gets whether the task is cancelled.
		/// </summary>
		public bool IsCanceled
		{
			get
			{
				lock (_lock)
					return _status == LuaTaskStatus.Canceled;
			}
		}

		/// <summary>
		/// Gets the result of the task as a <see cref="LuaTuple"/>.
		/// Throws if the task is not in the <see cref="LuaTaskStatus.Completed"/> state.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the task is not completed successfully.
		/// </exception>
		public LuaTuple Result
		{
			get
			{
				lock (_lock)
				{
					return _status switch
					{
						LuaTaskStatus.Completed => _result,
						LuaTaskStatus.Faulted =>
							throw new InvalidOperationException(
								"The task is faulted. Check the Exception property.",
								_exception),
						LuaTaskStatus.Canceled =>
							throw new InvalidOperationException("The task was cancelled."),
						_ => throw new InvalidOperationException(
							"The task is not yet completed.")
					};
				}
			}
		}

		/// <summary>
		/// Gets the exception that caused the task to fault, or <see langword="null"/>
		/// if the task is not faulted.
		/// </summary>
		public Exception? Exception
		{
			get
			{
				lock (_lock)
					return _exception;
			}
		}

		// ── Completion (called by producer) ──────────────────────────────

		/// <summary>
		/// Transitions the task to the <see cref="LuaTaskStatus.Completed"/> state
		/// with the specified values and invokes all registered continuations.
		/// </summary>
		/// <param name="results">The return values of the task.</param>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the task is already in a terminal state.
		/// </exception>
		public void SetResult(params LuaValue[] results)
		{
			SetResult(new LuaTuple(results));
		}

		/// <summary>
		/// Transitions the task to the <see cref="LuaTaskStatus.Completed"/> state
		/// with the specified <see cref="LuaTuple"/> and invokes all registered continuations.
		/// </summary>
		/// <param name="tuple">The return values as a tuple. Must not be <see langword="null"/>.</param>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="tuple"/> is <see langword="null"/>.
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the task is already in a terminal state.
		/// </exception>
		public void SetResult(LuaTuple tuple)
		{
			if (tuple is null)
				throw new ArgumentNullException(nameof(tuple));

			List<Action<LuaTask>>? continuations;
			lock (_lock)
			{
				ThrowIfTerminal();
				_status = LuaTaskStatus.Completed;
				_result = tuple;
				continuations = _continuations;
			}

			// Invoke continuations outside the lock to avoid re-entrancy deadlocks.
			continuations?.ForEach(c => c(this));
		}

		/// <summary>
		/// Transitions the task to the <see cref="LuaTaskStatus.Faulted"/> state
		/// and invokes all registered continuations.
		/// </summary>
		/// <param name="exception">The exception that caused the failure.</param>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the task is already in a terminal state.
		/// </exception>
		public void SetException(Exception exception)
		{
			if (exception is null)
				throw new ArgumentNullException(nameof(exception));

			List<Action<LuaTask>>? continuations;
			lock (_lock)
			{
				ThrowIfTerminal();
				_status = LuaTaskStatus.Faulted;
				_exception = exception;
				continuations = _continuations;
			}

			continuations?.ForEach(c => c(this));
		}

		/// <summary>
		/// Transitions the task to the <see cref="LuaTaskStatus.Canceled"/> state
		/// and invokes all registered continuations.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the task is already in a terminal state.
		/// </exception>
		public void SetCanceled()
		{
			List<Action<LuaTask>>? continuations;
			lock (_lock)
			{
				ThrowIfTerminal();
				_status = LuaTaskStatus.Canceled;
				continuations = _continuations;
			}

			continuations?.ForEach(c => c(this));
		}

		// ── Continuations (called by scheduler) ──────────────────────────

		/// <summary>
		/// Registers a continuation to be invoked when the task completes.
		/// If the task is already completed, the continuation is invoked immediately.
		/// </summary>
		/// <param name="continuation">
		/// The callback to invoke. Receives the completed task as its argument.
		/// </param>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="continuation"/> is <see langword="null"/>.
		/// </exception>
		/// <remarks>
		/// <para>
		/// The continuation may be invoked synchronously (if the task is already complete)
		/// or asynchronously (when <see cref="SetResult"/> etc. are called later).
		/// </para>
		/// </remarks>
		public void OnCompleted(Action<LuaTask> continuation)
		{
			if (continuation is null)
				throw new ArgumentNullException(nameof(continuation));

			bool invokeNow;
			lock (_lock)
			{
				invokeNow = _status != LuaTaskStatus.Pending;
				if (!invokeNow)
					_continuations!.Add(continuation);
			}

			if (invokeNow)
				continuation(this);
		}

		// ── C# Awaiter pattern ───────────────────────────────────────────

		/// <summary>
		/// Gets an awaiter that can be used to <c>await</c> this task in C#.
		/// </summary>
		/// <returns>A <see cref="LuaTaskAwaiter"/> for this task.</returns>
		public LuaTaskAwaiter GetAwaiter() => new LuaTaskAwaiter(this);

		// ── LuaValue overrides ───────────────────────────────────────────

		/// <inheritdoc />
		public override LuaType Type => LuaType.Task;

		/// <inheritdoc />
		public override string TypeName => "task";

		/// <inheritdoc />
		/// <returns>
		/// <see langword="true"/> — all tasks are truthy regardless of status.
		/// </returns>
		public override bool ToBoolean() => true;

		/// <inheritdoc />
		public override string ToString()
		{
			var status = Status;
			return status == LuaTaskStatus.Completed
				? $"task: completed ({_result.Count} result(s))"
				: $"task: {status.ToString().ToLowerInvariant()}";
		}

		/// <inheritdoc />
		public override bool Equals(LuaValue other) =>
			ReferenceEquals(this, other);

		/// <inheritdoc />
		public override int GetHashCode() =>
			RuntimeHelpers.GetHashCode(this);

		// ── Private helpers ──────────────────────────────────────────────

		private void ThrowIfTerminal()
		{
			if (_status != LuaTaskStatus.Pending)
			{
				throw new InvalidOperationException(
					$"The task is already in a terminal state: {_status}.");
			}
		}
	}
}
