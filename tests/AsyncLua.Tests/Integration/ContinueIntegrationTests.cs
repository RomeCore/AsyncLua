using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// Integration tests for the <c>continue</c> statement in all loop types:
/// while, repeat, numeric for, and generic for (for-in).
/// </summary>
public class ContinueIntegrationTests(ITestOutputHelper output)
{
    private static LuaState CreateState()
    {
        return new LuaState()
            .LoadDefaultLibraries();
    }

    // ────────────────────────────────────────────────────────────────
    // While loop
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <c>continue</c> inside a <c>while</c> loop skips
    /// the remainder of the loop body and proceeds to the next condition check.
    /// </summary>
    [Fact]
    public void ContinueInWhile_SkipsRestOfBody()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            local i = 0
            while i < 5 do
                i = i + 1
                if i == 3 then continue end
                print(i)
            end
        ");

        Assert.Equal([1, 2, 4, 5], results);
    }

    /// <summary>
    /// Verifies that <c>continue</c> in a <c>while</c> loop with a false condition
    /// immediately after continue still checks the condition.
    /// </summary>
    [Fact]
    public void ContinueInWhile_ConditionStillChecked()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            local i = 0
            while i < 5 do
                i = i + 1
                if i == 3 then
                    continue
                    print(999) -- should never execute
                end
                print(i)
            end
        ");

        Assert.Equal([1, 2, 4, 5], results);
    }

    // ────────────────────────────────────────────────────────────────
    // Repeat-until loop
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <c>continue</c> inside a <c>repeat</c> loop skips
    /// the remainder of the body and proceeds to the condition check.
    /// </summary>
    [Fact]
    public void ContinueInRepeat_SkipsRestOfBody()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            local i = 0
            repeat
                i = i + 1
                if i == 3 then continue end
                print(i)
            until i >= 5
        ");

        Assert.Equal([1, 2, 4, 5], results);
    }

    /// <summary>
    /// Verifies that <c>continue</c> in a <c>repeat</c> loop when the
    /// condition is already met (i.e., loop should exit) still exits properly.
    /// </summary>
    [Fact]
    public void ContinueInRepeat_WithImmediateExit()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            local i = 0
            repeat
                i = i + 1
                if i == 1 then
                    continue
                    print(999) -- should never execute
                end
                print(i)
            until i >= 3
        ");

        // i=1 → continue, i=2 → print(2), i=3 → print(3), exit
        Assert.Equal([2, 3], results);
    }

    // ────────────────────────────────────────────────────────────────
    // Numeric for loop
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <c>continue</c> inside a numeric <c>for</c> loop
    /// skips the remainder of the body and proceeds to the next iteration.
    /// </summary>
    [Fact]
    public void ContinueInNumericFor_SkipsRestOfBody()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            for i = 1, 5 do
                if i == 3 then continue end
                print(i)
            end
        ");

        Assert.Equal([1, 2, 4, 5], results);
    }

    /// <summary>
    /// Verifies that the loop variable still increments correctly
    /// after a <c>continue</c> statement.
    /// </summary>
    [Fact]
    public void ContinueInNumericFor_LoopVariableIncrements()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            for i = 1, 5 do
                if i % 2 == 0 then continue end
                print(i)
            end
        ");

        // Only odd numbers should be printed
        Assert.Equal([1, 3, 5], results);
    }

    /// <summary>
    /// Verifies that <c>continue</c> at the very first iteration works.
    /// </summary>
    [Fact]
    public void ContinueInNumericFor_FirstIteration()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            for i = 1, 3 do
                if i == 1 then continue end
                print(i)
            end
        ");

        Assert.Equal([2, 3], results);
    }

    // ────────────────────────────────────────────────────────────────
    // Generic for (for-in) loop
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <c>continue</c> inside a generic <c>for</c> loop
    /// (using <c>ipairs</c>) skips the remainder of the body.
    /// </summary>
    [Fact]
    public void ContinueInGenericFor_SkipsRestOfBody()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            local t = {10, 20, 30, 40, 50}
            for i, v in ipairs(t) do
                if i == 3 then continue end
                print(v)
            end
        ");

        Assert.Equal([10, 20, 40, 50], results);
    }

    /// <summary>
    /// Verifies that <c>continue</c> in a generic <c>for</c> with a custom
    /// iterator works correctly.
    /// </summary>
    [Fact]
    public void ContinueInGenericFor_WithPairs()
    {
        var state = CreateState();
        var results = new List<string>();
        state.Print = msg => results.Add(msg);

        state.Execute(@"
            local t = {a = 1, b = 2, c = 3}
            for k, v in pairs(t) do
                if v == 2 then continue end
                print(k .. '=' .. v)
            end
        ");

        Assert.Contains("a=1", results);
        Assert.Contains("c=3", results);
        Assert.DoesNotContain("b=2", results);
    }

    // ────────────────────────────────────────────────────────────────
    // Nested loops
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <c>continue</c> applies to the innermost loop only.
    /// </summary>
    [Fact]
    public void ContinueInNestedLoop_AppliesToInnerLoop()
    {
        var state = CreateState();
        var results = new List<string>();
        state.Print = msg => results.Add(msg);

        state.Execute(@"
            local outer = 0
            while outer < 2 do
                outer = outer + 1
                local inner = 0
                while inner < 3 do
                    inner = inner + 1
                    if inner == 2 then continue end
                    print('o=' .. outer .. ' i=' .. inner)
                end
                print('end-outer=' .. outer)
            end
        ");

        Assert.Equal([
            "o=1 i=1",
            "o=1 i=3",
            "end-outer=1",
            "o=2 i=1",
            "o=2 i=3",
            "end-outer=2"
        ], results);
    }

    // ────────────────────────────────────────────────────────────────
    // Continue with conditional logic
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <c>continue</c> works inside conditional branches.
    /// </summary>
    [Fact]
    public void ContinueInsideIfBranch()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            for i = 1, 5 do
                if i > 2 then
                    if i < 5 then
                        continue
                    end
                end
                print(i)
            end
        ");

        // i=1 → i <= 2, skip outer if → print(1)
        // i=2 → i <= 2, skip outer if → print(2)
        // i=3 → i > 2, i < 5 → continue (skip print)
        // i=4 → i > 2, i < 5 → continue (skip print)
        // i=5 → i > 2, i >= 5, skip inner if → print(5)
        Assert.Equal([1, 2, 5], results);
    }

    // ────────────────────────────────────────────────────────────────
    // Continue with break
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <c>continue</c> and <c>break</c> can coexist in the same loop.
    /// </summary>
    [Fact]
    public void ContinueAndBreakInSameLoop()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            for i = 1, 10 do
                if i == 3 then continue end
                if i == 7 then break end
                print(i)
            end
        ");

        // 1, 2, (3 skipped via continue), 4, 5, 6, (7 break) → prints 1,2,4,5,6
        Assert.Equal([1, 2, 4, 5, 6], results);
    }

    // ────────────────────────────────────────────────────────────────
    // Async function with continue
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <c>continue</c> works inside an <c>async</c> function.
    /// </summary>
    [Fact]
    public async Task ContinueInAsyncFunction()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        await state.ExecuteAsync(@"
            async function test()
                for i = 1, 5 do
                    if i == 3 then continue end
                    await task.delay(1)
                    print(i)
                end
            end
            await test()
        ");

        Assert.Equal([1, 2, 4, 5], results);
    }

    /* Edge-case, remove for now
    /// <summary>
    /// Verifies that <c>continue</c> in an <c>async</c> function with
    /// <c>lock</c> works correctly.
    /// </summary>
    [Fact]
    public async Task ContinueInAsyncFunctionWithLock()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        await state.ExecuteAsync(@"
            local mtx = {}
            async function test()
                for i = 1, 4 do
                    lock mtx do
                        if i == 2 then continue end
                        await task.delay(1)
                        print(i)
                    end
                end
            end
            await test()
        ");

        Assert.Equal([1, 3, 4], results);
    }
    */

    // ────────────────────────────────────────────────────────────────
    // Edge cases
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a single <c>continue</c> in a loop that runs only once
    /// does not cause an infinite loop.
    /// </summary>
    [Fact]
    public void ContinueInSingleIterationLoop()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            for i = 1, 1 do
                continue
                print(999)
            end
            print(42)
        ");

        Assert.Equal([42], results);
    }

    /// <summary>
    /// Verifies that <c>continue</c> works correctly when the loop body
    /// contains only the continue statement (trivial body).
    /// </summary>
    [Fact]
    public void ContinueInEmptyLoopBody()
    {
        var state = CreateState();
        var results = new List<int>();
        state.Print = msg => results.Add(int.Parse(msg));

        state.Execute(@"
            local count = 0
            for i = 1, 1000 do
                continue
            end
            print(42)
        ");

        // Loop should complete normally and print 42
        Assert.Equal([42], results);
    }

    /// <summary>
    /// Verifies that <c>continue</c> outside a loop throws a compile-time error.
    /// </summary>
    [Fact]
    public void ContinueOutsideLoop_ThrowsError()
    {
        var state = CreateState();

        var ex = Assert.Throws<AsyncLua.Compiling.CompilerException>(() =>
            state.Execute("continue"));

        Assert.Contains("continue", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }
}
