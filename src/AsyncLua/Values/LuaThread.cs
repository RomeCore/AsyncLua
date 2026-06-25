using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncLua.Values
{
	/// <summary>
	/// Represents a Lua coroutine (thread). Allows cooperative multitasking by
	/// suspending execution via <c>coroutine.yield()</c> and resuming via
	/// <c>coroutine.resume()</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Async handshake model:</b><br/>
	/// <c>coroutine.yield()</c> is an async C# callback that stores the yielded
	/// values and then asynchronously waits for the next <c>resume</c> call via
	/// a <see cref="TaskCompletionSource{T}"/>. Meanwhile, the resumer receives
	/// the yielded values. On the next <c>resume</c>, that TCS is completed with
	/// the new arguments, and the <c>yield</c> callback returns them to the Lua code.
	/// </para>
	/// <para>
	/// <b>Important:</b> The coroutine function and all code that calls
	/// <c>coroutine.yield</c> must be marked <c>async</c> (or called from an
	/// async context), because <c>yield</c> suspends via <c>await</c>.
	/// Example:
	/// <code>
	/// local co = coroutine.create(async function()
	///     local x = await coroutine.yield(10)
	///     return x + 20
	/// end)
	/// local ok, y = coroutine.resume(co)     -- y = 10
	/// local ok, r = coroutine.resume(co, 100) -- r = 120
	/// </code>
	/// </para>
	/// </remarks>
	public sealed class LuaThread : LuaValue
	{
		private readonly LuaFunction _function;
		private LuaTuple _yieldedValues = LuaTuple.Empty;

		// TCS handshake: _resumeTcs is completed when resume() is called,
		// providing the next arguments. _yieldTcs is completed when yield() is called.
		private TaskCompletionSource<LuaValue[]> _resumeTcs = new();
		private TaskCompletionSource<LuaTuple> _yieldTcs = new();

		// The running task for the function execution.
		private Task<LuaTuple>? _executionTask;

		/// <summary>
		/// Gets the thread-local <see cref="LuaThread"/> that is currently executing,
		/// or <see langword="null"/> if no coroutine is running.
		/// </summary>
		public static AsyncLocal<LuaThread?> Current { get; } = new();

		/// <summary>
		/// Initialises a new <see cref="LuaThread"/> that wraps the specified function.
		/// </summary>
		/// <param name="function">The function to execute as a coroutine.</param>
		/// <exception cref="ArgumentNullException">
		/// Thrown if <paramref name="function"/> is <see langword="null"/>.
		/// </exception>
		public LuaThread(LuaFunction function)
		{
			_function = function ?? throw new ArgumentNullException(nameof(function));
		}

		/// <inheritdoc />
		public override LuaType Type => LuaType.Thread;

		/// <inheritdoc />
		public override string TypeName => "thread";

		/// <summary>
		/// Gets the current status of this coroutine.
		/// </summary>
		public LuaThreadStatus Status { get; private set; } = LuaThreadStatus.Suspended;

		/// <summary>
		/// Gets whether this coroutine has finished execution.
		/// </summary>
		public bool IsDead => Status == LuaThreadStatus.Dead;

		/// <summary>
		/// Gets the yielded values from the most recent <c>yield</c> call.
		/// Only valid when <see cref="Status"/> is <see cref="LuaThreadStatus.Suspended"/>.
		/// </summary>
		public LuaTuple YieldedValues => _yieldedValues;

		/// <summary>
		/// Gets the function that this coroutine executes.
		/// </summary>
		public LuaFunction Function => _function;

		/// <summary>
		/// Resumes or starts this coroutine with the specified arguments.
		/// Must be called from an async context (or via <c>ExecuteAsync</c>).
		/// </summary>
		/// <param name="context">The calling context.</param>
		/// <param name="args">The arguments to pass.</param>
		/// <returns>
		/// A task that resolves to a <see cref="LuaTuple"/> with:
		/// <c>[0]</c> = <see langword="true"/> on success, <see langword="false"/> on error.
		/// <c>[1 ...]</c> = yielded or returned values, or error message if <c>[0]</c> is <see langword="false"/>.
		/// </returns>
		public async Task<LuaTuple> ResumeAsync(LuaCallingContext context, LuaValue[] args)
		{
			if (Status == LuaThreadStatus.Dead)
				return new LuaTuple(
					LuaBoolean.False,
					new LuaString("cannot resume dead coroutine"));

			if (Status == LuaThreadStatus.Running)
				return new LuaTuple(
					LuaBoolean.False,
					new LuaString("cannot resume running coroutine"));

			try
			{
				if (_executionTask == null)
				{
					// First invocation: start the function.
					Status = LuaThreadStatus.Running;
					var prev = Current.Value;
					Current.Value = this;
					try
					{
						_executionTask = _function.InvokeAsync(context, args);
					}
					finally
					{
						Current.Value = prev;
					}
				}
				else
				{
					// Resuming after a yield: signal the waiting yield callback
					// with the new arguments.
					// IMPORTANT: Replace _yieldTcs and _resumeTcs BEFORE completing
					// _resumeTcs, to prevent synchronous re-entrancy where the
					// continuation runs on this thread and calls YieldAsync again
					// before we've set up the new TCS objects.
					Status = LuaThreadStatus.Running;
					_yieldTcs = new TaskCompletionSource<LuaTuple>();
					var oldResumeTcs = _resumeTcs;
					_resumeTcs = new TaskCompletionSource<LuaValue[]>();
					oldResumeTcs.TrySetResult(args);
				}

				// Wait for either completion or yield.
				var completed = await Task.WhenAny(_executionTask!, _yieldTcs.Task);

				if (completed == _executionTask)
				{
					// Function completed normally.
					var result = await _executionTask;
					Status = LuaThreadStatus.Dead;
					_executionTask = null;
					return PackResult(LuaBoolean.True, result);
				}

				// Function yielded.
				_yieldTcs = new TaskCompletionSource<LuaTuple>();
				Status = LuaThreadStatus.Suspended;
				return PackResult(LuaBoolean.True, _yieldedValues);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Status = LuaThreadStatus.Dead;
				_executionTask = null;
				var message = ex is LuaRuntimeException luaEx
					? luaEx.OriginalMessage
					: ex.Message;
				return new LuaTuple(LuaBoolean.False, new LuaString(message));
			}
		}

		/// <summary>
		/// Called by the <c>coroutine.yield</c> C# callback to suspend the
		/// current coroutine and pass values back to the resumer.
		/// </summary>
		/// <param name="values">The values to yield to the resumer.</param>
		/// <returns>
		/// A task that resolves to the arguments passed to the next <c>resume</c> call.
		/// </returns>
		internal async Task<LuaValue[]> YieldAsync(LuaTuple values)
		{
			_yieldedValues = values;
			_yieldTcs.TrySetResult(values);
			return await _resumeTcs.Task;
		}

		/// <summary>
		/// Packs a status bool followed by the actual (spread) result values into a single tuple.
		/// </summary>
		private static LuaTuple PackResult(LuaValue status, LuaTuple inner)
		{
			var count = inner.Count;
			var result = new LuaValue[1 + count];
			result[0] = status;
			for (int i = 0; i < count; i++)
				result[1 + i] = inner[i];
			return new LuaTuple(result);
		}

		// ── LuaValue overrides ──────────────────────────────────────────

		/// <inheritdoc />
		public override string ToString()
		{
			return $"thread: {RuntimeHelpers.GetHashCode(this):X}";
		}

		/// <inheritdoc />
		public override bool Equals(LuaValue other) => ReferenceEquals(this, other);

		/// <inheritdoc />
		public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

		/// <inheritdoc />
		public override bool ToBoolean() => true;
	}
}
