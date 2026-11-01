using System.Diagnostics;
using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// AWAIT must observe the calling context's cancellation token: a pending await is interrupted as
/// soon as the host cancels, instead of running to completion behind a dead script. Cancellation is
/// not a Lua error either - neither <c>pcall</c> nor <c>try</c>/<c>catch</c> may swallow it.
/// </summary>
public class CancellationTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState().LoadDefaultLibraries();

		// Deliberately ignores the token: only the AWAIT instruction can interrupt this wait.
		state.SetGlobal("slow", new LuaCallbackFunction(
			new LuaCallbackFunction.AsyncCallbackDelegate(async (ctx, args) =>
			{
				await Task.Delay(TimeSpan.FromSeconds(5));
				return LuaTuple.Empty;
			}), "slow"));

		return state;
	}

	private static string Text(LuaValue value) => Assert.IsType<LuaString>(value).Value;

	// ── A pending await is interrupted ──────────────────────────────

	[Fact]
	public async Task Await_IsInterrupted_WhenTheTokenIsCancelled()
	{
		var state = CreateState();
		using var cts = new CancellationTokenSource();
		cts.CancelAfter(TimeSpan.FromMilliseconds(100));

		var stopwatch = Stopwatch.StartNew();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			state.ExecuteAsync("await slow() return 'done'", null, null, cts.Token));
		stopwatch.Stop();

		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
			$"the await ran to completion instead of being cancelled: {stopwatch.Elapsed}");
	}

	[Fact]
	public async Task Await_WithAlreadyCancelledToken_Aborts()
	{
		var state = CreateState();
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			state.ExecuteAsync("await slow() return 'done'", null, null, cts.Token));
	}

	[Fact]
	public async Task Await_WithoutCancellation_CompletesNormally()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync("local t = task.delay(1) await t return 'done'");

		Assert.Equal("done", Text(result[0]));
	}

	/// <summary>
	/// A live token is not a cancelled one: racing it must not disturb a wait that finishes first.
	/// </summary>
	[Fact]
	public async Task Await_WithLiveToken_CompletesNormally()
	{
		var state = CreateState();
		using var cts = new CancellationTokenSource();

		var result = await state.ExecuteAsync("local t = task.delay(1) await t return 'done'", null, null, cts.Token);

		Assert.Equal("done", Text(result[0]));
	}

	// ── Cancellation is not a Lua error ─────────────────────────────

	[Fact]
	public async Task Pcall_CannotSwallowCancellation()
	{
		var state = CreateState();
		using var cts = new CancellationTokenSource();
		cts.CancelAfter(TimeSpan.FromMilliseconds(100));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.ExecuteAsync("""
			local ok, err = pcall(function() await slow() end)
			return 'reached', ok, err
			""", null, null, cts.Token));
	}

	[Fact]
	public async Task TryCatch_CannotSwallowCancellation()
	{
		var state = CreateState();
		using var cts = new CancellationTokenSource();
		cts.CancelAfter(TimeSpan.FromMilliseconds(100));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.ExecuteAsync("""
			try
				await slow()
				return 'reached'
			catch e do
				return 'caught: ' .. tostring(e)
			end
			""", null, null, cts.Token));
	}

	// ── The coroutine handshake survives the race ───────────────────

	[Fact]
	public async Task Coroutine_StillResumes_WithALiveToken()
	{
		var state = CreateState();
		using var cts = new CancellationTokenSource();

		var result = await state.ExecuteAsync("""
			local co = coroutine.create(async function()
				await coroutine.yield_async(1)
				await coroutine.yield_async(2)
				return 'done'
			end)

			local ok1, v1 = await coroutine.resume_async(co)
			local ok2, v2 = await coroutine.resume_async(co)
			local ok3, final = await coroutine.resume_async(co)
			return v1, v2, final
			""", null, null, cts.Token);

		Assert.Equal(3, result.Count);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal("done", Text(result[2]));
	}
}
