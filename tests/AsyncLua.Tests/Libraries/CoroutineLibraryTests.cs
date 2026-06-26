using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// Tests for <see cref="Libraries.CoroutineLibrary"/> and <see cref="LuaThread"/>:
/// create, resume, yield, status, wrap, running.
/// </summary>
/// <remarks>
/// All coroutine tests must be executed via <c>ExecuteAsync</c> because
/// <c>yield</c> and <c>resume</c> use async handshake internally.
/// </remarks>
public class CoroutineLibraryTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState();
		state.LoadDefaultLibraries();
		return state;
	}

	// ═══════════════════════════════════════════════════════════════════
	// BASIC
	// ═══════════════════════════════════════════════════════════════════

	[Fact]
	public async Task Create_AndResume_SyncFunction_ReturnsResult()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(function(n)
				return n * 2
			end)
			local ok, val = await coroutine.resume_async(co, 21)
			assert(ok, 'resume failed')
			assert(val == 42, 'expected 42, got ' .. val)
			return ok, val
		");
		Assert.Equal(LuaBoolean.True, result[0]);
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public async Task Create_AndResume_AsyncFunction_ReturnsResult()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(async function(n)
				return n * 2
			end)
			local ok, val = await coroutine.resume_async(co, 21)
			assert(ok, 'resume failed')
			assert(val == 42, 'expected 42, got ' .. val)
			return ok, val
		");
		Assert.Equal(LuaBoolean.True, result[0]);
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public async Task YieldOnce_SyncFunction_ReturnsResult()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(async function()
				local x = coroutine.yield(10)
				return x + 20
			end)
			local ok1, v1 = coroutine.resume(co)
			assert(ok1, 'first resume')
			assert(v1 == 10, 'expected 10, got ' .. v1)

			local ok2, v2 = coroutine.resume(co, 100)
			assert(ok2, 'second resume')
			assert(v2 == 120, 'expected 120, got ' .. v2)

			return ok1 and ok2, v1, v2
		");
		Assert.Equal(LuaBoolean.True, result[0]);
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(120.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public async Task MultipleYields_PassesValuesCorrectly()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(async function()
				local a = await coroutine.yield_async('a')
				local b = coroutine.yield('b')
				return a .. b
			end)
			local _, v1 = coroutine.resume(co)
			local _, v2 = await coroutine.resume_async(co, 'HELLO')
			local _, v3 = coroutine.resume(co, 'WORLD')
			return v1, v2, v3
		");
		// v1 = first yield value = 'a'
		Assert.Equal("a", Assert.IsType<LuaString>(result[0]).Value);
		// v2 = second yield value = 'b'
		Assert.Equal("b", Assert.IsType<LuaString>(result[1]).Value);
		// v3 = final result = 'HELLO' .. 'WORLD' = 'HELLOWORLD'
		Assert.Equal("HELLOWORLD", Assert.IsType<LuaString>(result[2]).Value);
	}

	// ═══════════════════════════════════════════════════════════════════
	// STATUS
	// ═══════════════════════════════════════════════════════════════════

	[Fact]
	public async Task StatusFlow_Suspended_Running_Dead()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(async function()
				await coroutine.yield_async('inside')
				return 'done'
			end)

			local s1 = coroutine.status(co)

			local _, y = await coroutine.resume_async(co)
			local s2 = coroutine.status(co)

			local _, r = await coroutine.resume_async(co)
			local s3 = coroutine.status(co)

			return s1, s2, s3, y, r
		");
		Assert.Equal("suspended", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("suspended", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("dead", Assert.IsType<LuaString>(result[2]).Value);
		Assert.Equal("inside", Assert.IsType<LuaString>(result[3]).Value);
		Assert.Equal("done", Assert.IsType<LuaString>(result[4]).Value);
	}

	[Fact]
	public async Task ResumeDeadCoroutine_ReturnsError()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(async function() return 42 end)
			local ok1, r1 = await coroutine.resume_async(co)
			local ok2, r2 = await coroutine.resume_async(co)
			return ok1, r1, ok2, r2
		");
		Assert.Equal(LuaBoolean.True, result[0]);
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(LuaBoolean.False, result[2]);
		Assert.Equal("cannot resume dead coroutine",
			Assert.IsType<LuaString>(result[3]).Value);
	}

	// ═══════════════════════════════════════════════════════════════════
	// ERROR HANDLING
	// ═══════════════════════════════════════════════════════════════════

	[Fact]
	public async Task ErrorInCoroutine_ReturnsError()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(async function()
				throw 'something broke'
			end)
			local ok, err = coroutine.resume(co)
			return ok, err
		");
		Assert.Equal(LuaBoolean.False, result[0]);
		Assert.NotNull(result[1]);
	}

	[Fact]
	public async Task ErrorAfterYield_ReportsCorrectly()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(async function()
				local x = coroutine.yield('ready')
				throw 'fail after: ' .. x
			end)
			local ok1, v1 = await coroutine.resume_async(co)
			local ok2, v2 = await coroutine.resume_async(co, 'test')
			return ok1, v1, ok2, v2
		");
		Assert.Equal(LuaBoolean.True, result[0]);
		Assert.Equal("ready", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal(LuaBoolean.False, result[2]);
		Assert.Contains("fail after", Assert.IsType<LuaString>(result[3]).Value);
	}

	[Fact]
	public async Task Create_InvalidArg_ReturnsError()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co, err = coroutine.create(42)
			return co, err
		");
		Assert.IsType<LuaNil>(result[0]);
		Assert.Contains("bad argument #1", Assert.IsType<LuaString>(result[1]).Value);
	}

	[Fact]
	public async Task Resume_InvalidArg_ReturnsError()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local ok, err = await coroutine.resume_async('not_a_thread')
			return ok, err
		");
		Assert.IsType<LuaNil>(result[0]);
		Assert.Contains("bad argument #1", Assert.IsType<LuaString>(result[1]).Value);
	}

	// ═══════════════════════════════════════════════════════════════════
	// COROUTINE.WRAP
	// ═══════════════════════════════════════════════════════════════════

	[Fact]
	public async Task Wrap_ReturnsCallableFunction()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local f = await coroutine.wrap(async function()
				local x = coroutine.yield(1)
				local y = await coroutine.yield_async(2)
				return x + y
			end)
			local r1 = await f()
			local r2 = await f(10)
			local r3 = await f(20)
			return r1, r2, r3
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public async Task Wrap_Error_Propagates()
	{
		var state = CreateState();
		var ex = await Assert.ThrowsAsync<LuaRuntimeException>(async () =>
			await state.ExecuteAsync(@"
				local f = await coroutine.wrap(function()
					throw 'wrap failure'
				end)
				await f()
			"));
		Assert.Contains("wrap failure", ex.Message);
	}

	// ═══════════════════════════════════════════════════════════════════
	// COROUTINE.RUNNING
	// ═══════════════════════════════════════════════════════════════════

	[Fact]
	public async Task Running_OutsideCoroutine_ReturnsNil()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync("return coroutine.running()");
		Assert.IsType<LuaNil>(result[0]);
	}

	[Fact]
	public async Task Running_InsideCoroutine_ReturnsThread()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(async function()
				local t, isMain = coroutine.running()
				return type(t), tostring(isMain)
			end)
			local _, tType, isMain = await coroutine.resume_async(co)
			return tType, isMain
		");
		Assert.Equal("thread", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("false", Assert.IsType<LuaString>(result[1]).Value);
	}

	// ═══════════════════════════════════════════════════════════════════
	// PIPELINE / MULTIPLE RESUMES
	// ═══════════════════════════════════════════════════════════════════

	[Fact]
	public async Task Pipeline_ProducesAndConsumes()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(async function(count)
				local sum = 0
				for i = 1, count do
					sum = sum + i
					if i < count then
						await coroutine.yield_async(i)
					end
				end
				return sum
			end)

			local ok1, y1 = await coroutine.resume_async(co, 5)
			local ok2, y2 = coroutine.resume(co)
			local ok3, y3 = await coroutine.resume_async(co)
			local ok4, y4 = coroutine.resume(co)
			local ok5, sum = await coroutine.resume_async(co)

			return ok1 and ok2 and ok3 and ok4 and ok5,
			       y1, y2, y3, y4, sum
		");
		Assert.Equal(LuaBoolean.True, result[0]);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result[5]).Value);
	}

	[Fact]
	public async Task MultipleYields_ManyIterations()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			local co = coroutine.create(async function()
				for i = 1, 20 do
					if i < 20 then
						await coroutine.yield_async(i)
					end
				end
				return 'finished'
			end)

			local results = {}
			for i = 1, 19 do
				local ok, val = await coroutine.resume_async(co)
				assert(ok, 'failed at step ' .. i)
				results[i] = val
			end

			local ok, final = await coroutine.resume_async(co)
			assert(ok, 'final resume failed')
			assert(final == 'finished')
			return 'all_ok', #results, results[1], results[19]
		");
		Assert.Equal("all_ok", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal(19.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(19.0, Assert.IsType<LuaNumber>(result[3]).Value);
	}

	// ═══════════════════════════════════════════════════════════════════
	// YIELD IN SYNC FUNCTION
	// ═══════════════════════════════════════════════════════════════════

	[Fact]
	public async Task SyncFunction_CannotYield_CompletesWithTask()
	{
		var state = CreateState();
		var result = await state.ExecuteAsync(@"
			-- A sync function inside a coroutine CAN call yield, but since
			-- yield is async and the function doesn't await it, the yield
			-- handshake is skipped. The function returns a LuaTask.
			local co = coroutine.create(function()
				local t = coroutine.yield_async('test')
				return type(t), tostring(t)
			end)
			local ok, r1, r2 = await coroutine.resume_async(co)
			return ok, r1, r2
		");
		Assert.Equal(LuaBoolean.True, result[0]);
		Assert.Equal("task", Assert.IsType<LuaString>(result[1]).Value);
	}
}
