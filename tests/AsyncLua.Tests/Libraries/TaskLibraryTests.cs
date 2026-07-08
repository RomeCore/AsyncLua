using AsyncLua.Values;

namespace AsyncLua.Tests.Libraries;

/// <summary>
/// Tests for the <c>task</c> library functions, including
/// <c>task.pararun</c> with tables and concurrency limits.
/// </summary>
public class TaskLibraryTests
{
    private static LuaState CreateState()
    {
        var state = new LuaState()
            .LoadDefaultLibraries();

        state.SetGlobal("assert", new LuaCallbackFunction(
            (ctx, args) =>
            {
                if (args.Length > 0 && !args[0].ToBoolean())
                {
                    var msg = args.Length > 1 ? args[1].ToString() : "assertion failed!";
                    throw new LuaRuntimeException(msg);
                }
                return args.Length > 0 ? new LuaTuple(args) : LuaTuple.Empty;
            }, "assert"));

        return state;
    }

    // ═════════════════════════════════════════════════════
    //  BASIC TABLE ITERATION
    // ═════════════════════════════════════════════════════

    [Fact]
    public async Task Pararun_WithTable_ReturnsMappedResults()
    {
        var state = CreateState();

        var result = await state.ExecuteAsync(@"
            local tbl = {a = 1, b = 2, c = 3}
            local results = await task.pararun(tbl, function(v, k)
                return k .. '=' .. tostring(v)
            end)
            return results
        ");

        var resultTable = Assert.IsType<LuaTable>(result.First);
        Assert.Equal("a=1", Assert.IsType<LuaString>(resultTable.Get("a")).Value);
        Assert.Equal("b=2", Assert.IsType<LuaString>(resultTable.Get("b")).Value);
        Assert.Equal("c=3", Assert.IsType<LuaString>(resultTable.Get("c")).Value);
    }

    [Fact]
    public async Task Pararun_WithArray_ReturnsByIndex()
    {
        var state = CreateState();

        var result = await state.ExecuteAsync(@"
            local tbl = {10, 20, 30}
            local results = await task.pararun(tbl, function(v, k)
                return v * 2
            end)
            return results
        ");

        var resultTable = Assert.IsType<LuaTable>(result.First);
        Assert.Equal(20.0, Assert.IsType<LuaNumber>(resultTable.Get(1.0)).Value);
        Assert.Equal(40.0, Assert.IsType<LuaNumber>(resultTable.Get(2.0)).Value);
        Assert.Equal(60.0, Assert.IsType<LuaNumber>(resultTable.Get(3.0)).Value);
    }

    // ═════════════════════════════════════════════════════
    //  ASYNC CALLBACKS
    // ═════════════════════════════════════════════════════

    [Fact]
    public async Task Pararun_WithAsyncCallback_RunsConcurrently()
    {
        var state = CreateState();

        var result = await state.ExecuteAsync(@"
            local tbl = {1, 2, 3, 4, 5}
            local results = await task.pararun(tbl, async function(v, k)
                await task.delay(5)
                return v * 2
            end)
            return results
        ");

        var resultTable = Assert.IsType<LuaTable>(result.First);
        Assert.Equal(5, resultTable.Length);
        Assert.Equal(2.0, Assert.IsType<LuaNumber>(resultTable.Get(1.0)).Value);
        Assert.Equal(4.0, Assert.IsType<LuaNumber>(resultTable.Get(2.0)).Value);
        Assert.Equal(6.0, Assert.IsType<LuaNumber>(resultTable.Get(3.0)).Value);
        Assert.Equal(8.0, Assert.IsType<LuaNumber>(resultTable.Get(4.0)).Value);
        Assert.Equal(10.0, Assert.IsType<LuaNumber>(resultTable.Get(5.0)).Value);
    }

    // ═════════════════════════════════════════════════════
    //  EDGE CASES
    // ═════════════════════════════════════════════════════

    [Fact]
    public async Task Pararun_WithEmptyTable_ReturnsEmptyTable()
    {
        var state = CreateState();

        var result = await state.ExecuteAsync(@"
            local results = await task.pararun({}, function(v, k)
                return v
            end)
            return results
        ");

        var resultTable = Assert.IsType<LuaTable>(result.First);
        Assert.Empty(resultTable);
    }

    [Fact]
    public async Task Pararun_CallbackReceivesKeyAndValue()
    {
        var state = CreateState();

        var result = await state.ExecuteAsync(@"
            local tbl = {10, 20, 30}
            local results = await task.pararun(tbl, function(v, k)
                return {key = k, value = v}
            end)
            return results
        ");

        var resultTable = Assert.IsType<LuaTable>(result.First);

        var item1 = Assert.IsType<LuaTable>(resultTable.Get(1.0));
        Assert.Equal(1.0, Assert.IsType<LuaNumber>(item1.Get("key")).Value);
        Assert.Equal(10.0, Assert.IsType<LuaNumber>(item1.Get("value")).Value);

        var item2 = Assert.IsType<LuaTable>(resultTable.Get(2.0));
        Assert.Equal(2.0, Assert.IsType<LuaNumber>(item2.Get("key")).Value);
        Assert.Equal(20.0, Assert.IsType<LuaNumber>(item2.Get("value")).Value);
    }

    [Fact]
    public async Task Pararun_ThrowsWhenFirstArgNotTable()
    {
        var state = CreateState();

        var ex = await Assert.ThrowsAsync<LuaRuntimeException>(() =>
            state.ExecuteAsync(@"
                local ok, err = pcall(task.pararun, 42, function(v, k) return v end)
                if not ok then error(err) end
                return nil
            "));

        Assert.Contains("pararun", ex.OriginalMessage);
    }

    [Fact]
    public async Task Pararun_ThrowsWhenNoCallback()
    {
        var state = CreateState();

        var ex = await Assert.ThrowsAsync<LuaRuntimeException>(() =>
            state.ExecuteAsync(@"
                local ok, err = pcall(task.pararun, {1, 2}, 42)
                if not ok then error(err) end
                return nil
            "));

        Assert.Contains("pararun", ex.OriginalMessage);
    }

    [Fact]
    public async Task Pararun_ThrowsWhenNotEnoughArgs()
    {
        var state = CreateState();

        var ex = await Assert.ThrowsAsync<LuaRuntimeException>(() =>
            state.ExecuteAsync(@"
                local ok, err = pcall(task.pararun, {1, 2})
                if not ok then error(err) end
                return nil
            "));

        Assert.Contains("pararun", ex.OriginalMessage);
    }

    [Fact]
    public async Task Pararun_ThrowsWhenConcurrencyLimitInvalid()
    {
        var state = CreateState();

        var ex = await Assert.ThrowsAsync<LuaRuntimeException>(() =>
            state.ExecuteAsync(@"
                local ok, err = pcall(task.pararun, {1, 2}, function(v, k) return v end, 0)
                if not ok then error(err) end
                return nil
            "));

        Assert.Contains("pararun", ex.OriginalMessage);
    }

    // ═════════════════════════════════════════════════════
    //  MULTIPLE RETURN VALUES
    // ═════════════════════════════════════════════════════

    [Fact]
    public async Task Pararun_MultipleReturnValues_WrappedInTuple()
    {
        var state = CreateState();

        var result = await state.ExecuteAsync(@"
            local tbl = {1, 2}
            local results = await task.pararun(tbl, function(v, k)
                return v, k, v * 10
            end)
            return results
        ");

        var resultTable = Assert.IsType<LuaTable>(result.First);

        var item1 = Assert.IsType<LuaTuple>(resultTable.Get(1.0));
        Assert.Equal(3, item1.Count);
        Assert.Equal(1.0, Assert.IsType<LuaNumber>(item1[0]).Value);
        Assert.Equal(1.0, Assert.IsType<LuaNumber>(item1[1]).Value);
        Assert.Equal(10.0, Assert.IsType<LuaNumber>(item1[2]).Value);

        var item2 = Assert.IsType<LuaTuple>(resultTable.Get(2.0));
        Assert.Equal(2.0, Assert.IsType<LuaNumber>(item2[0]).Value);
        Assert.Equal(2.0, Assert.IsType<LuaNumber>(item2[1]).Value);
        Assert.Equal(20.0, Assert.IsType<LuaNumber>(item2[2]).Value);
    }

    // ═════════════════════════════════════════════════════
    //  COMPOSITION
    // ═════════════════════════════════════════════════════

    [Fact]
    public async Task Pararun_WithNamedAsyncFunction_Works()
    {
        var state = CreateState();

        var result = await state.ExecuteAsync(@"
            local async function processItem(item)
                await task.delay(5)
                return item * 2
            end

            local tbl = {1, 2, 3}
            local results = await task.pararun(tbl, processItem)
            return results
        ");

        var resultTable = Assert.IsType<LuaTable>(result.First);
        Assert.Equal(3, resultTable.Length);
        Assert.Equal(2.0, Assert.IsType<LuaNumber>(resultTable.Get(1.0)).Value);
        Assert.Equal(4.0, Assert.IsType<LuaNumber>(resultTable.Get(2.0)).Value);
        Assert.Equal(6.0, Assert.IsType<LuaNumber>(resultTable.Get(3.0)).Value);
    }

    [Fact]
    public async Task Pararun_NestedPararun_Works()
    {
        var state = CreateState();

        var result = await state.ExecuteAsync(@"
            local groups = {
                {1, 2},
                {3, 4},
                {5, 6}
            }
            local results = await task.pararun(groups, async function(group, idx)
                local doubled = await task.pararun(group, function(v, k)
                    return v * 2
                end)
                return doubled
            end)
            return results
        ");

        var resultTable = Assert.IsType<LuaTable>(result.First);

        var group1 = Assert.IsType<LuaTable>(resultTable.Get(1.0));
        Assert.Equal(2.0, Assert.IsType<LuaNumber>(group1.Get(1.0)).Value);
        Assert.Equal(4.0, Assert.IsType<LuaNumber>(group1.Get(2.0)).Value);

        var group2 = Assert.IsType<LuaTable>(resultTable.Get(2.0));
        Assert.Equal(6.0, Assert.IsType<LuaNumber>(group2.Get(1.0)).Value);
        Assert.Equal(8.0, Assert.IsType<LuaNumber>(group2.Get(2.0)).Value);

        var group3 = Assert.IsType<LuaTable>(resultTable.Get(3.0));
        Assert.Equal(10.0, Assert.IsType<LuaNumber>(group3.Get(1.0)).Value);
        Assert.Equal(12.0, Assert.IsType<LuaNumber>(group3.Get(2.0)).Value);
    }
}
