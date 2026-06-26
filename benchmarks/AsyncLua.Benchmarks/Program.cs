using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using AsyncLua;
using AsyncLua.Values;

namespace AsyncLua.Benchmarks;

/// <summary>
/// Entry point for the AsyncLua benchmark suite.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);
        switcher.Run(args);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Basic execution benchmarks
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Benchmarks for parsing + compiling + executing simple Lua chunks.
/// </summary>
[MemoryDiagnoser]
public class BasicExecutionBenchmarks
{
    private LuaState _asyncLuaState = null!;
    private NLua.Lua _nLuaState = null!;
    private MoonSharp.Interpreter.Script _moonSharpScript = null!;

    [GlobalSetup]
    public void Setup()
    {
        _asyncLuaState = new LuaState();

        _nLuaState = new NLua.Lua();
        _nLuaState.State.Encoding = System.Text.Encoding.UTF8;

        _moonSharpScript = new MoonSharp.Interpreter.Script();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _nLuaState?.Dispose();
    }

    [Benchmark]
    public LuaTuple AsyncLua_SimpleExpression()
    {
        return _asyncLuaState.Execute("return 2 + 3 * 4");
    }

    [Benchmark]
    public object[] NLua_SimpleExpression()
    {
		return _nLuaState.DoString("return 2 + 3 * 4");
    }

    [Benchmark]
    public MoonSharp.Interpreter.DynValue MoonSharp_SimpleExpression()
    {
        return _moonSharpScript.DoString("return 2 + 3 * 4");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Loop benchmarks
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Benchmarks for tight loops — measures bytecode dispatch efficiency.
/// </summary>
[MemoryDiagnoser]
public class LoopBenchmarks
{
    private LuaState _asyncLuaState = null!;
    private NLua.Lua _nLuaState = null!;
    private MoonSharp.Interpreter.Script _moonSharpScript = null!;

    private const string LoopScript = @"
local sum = 0
for i = 1, 100000 do
    sum = sum + i
end
return sum
";

    [GlobalSetup]
    public void Setup()
    {
        _asyncLuaState = new LuaState();
        _nLuaState = new NLua.Lua();
        _moonSharpScript = new MoonSharp.Interpreter.Script();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _nLuaState?.Dispose();
    }

    [Benchmark]
    public LuaTuple AsyncLua_SumLoop1000()
    {
        return _asyncLuaState.Execute(LoopScript);
    }

    [Benchmark]
    public object[] NLua_SumLoop1000()
    {
        return _nLuaState.DoString(LoopScript);
    }

    [Benchmark]
    public MoonSharp.Interpreter.DynValue MoonSharp_SumLoop1000()
    {
        return _moonSharpScript.DoString(LoopScript);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Function call benchmarks
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Benchmarks for calling C# functions from Lua.
/// </summary>
[MemoryDiagnoser]
public class FunctionCallBenchmarks
{
    private LuaState _asyncLuaState = null!;
    private NLua.Lua _nLuaState = null!;
    private MoonSharp.Interpreter.Script _moonSharpScript = null!;

    [GlobalSetup]
    public void Setup()
    {
        _asyncLuaState = new LuaState();
        _asyncLuaState.SetGlobal("add", new LuaCallbackFunction(
            (ctx, args) =>
            {
                double a = 0, b = 0;
                if (args.Length > 0) args[0].TryToNumber(out a);
                if (args.Length > 1) args[1].TryToNumber(out b);
                return new LuaTuple(new LuaNumber(a + b));
            }, "add"));

        _nLuaState = new NLua.Lua();
        _nLuaState.RegisterFunction("add", typeof(FunctionCallBenchmarks).GetMethod(nameof(AddCSharp))!);

        _moonSharpScript = new MoonSharp.Interpreter.Script();
        _moonSharpScript.Globals["add"] = (Func<double, double, double>)AddCSharp;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _nLuaState?.Dispose();
    }

    public static double AddCSharp(double a, double b) => a + b;

    [Benchmark]
    public LuaTuple AsyncLua_CallCSharpFunction()
    {
        return _asyncLuaState.Execute("return add(10, 20)");
    }

    [Benchmark]
    public object[] NLua_CallCSharpFunction()
    {
        return _nLuaState.DoString("return add(10, 20)");
    }

    [Benchmark]
    public MoonSharp.Interpreter.DynValue MoonSharp_CallCSharpFunction()
    {
        return _moonSharpScript.DoString("return add(10, 20)");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Table benchmarks
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Benchmarks for table creation and manipulation.
/// </summary>
[MemoryDiagnoser]
public class TableBenchmarks
{
    private LuaState _asyncLuaState = null!;
    private NLua.Lua _nLuaState = null!;
    private MoonSharp.Interpreter.Script _moonSharpScript = null!;

    private const string TableScript = @"
local t = {}
for i = 1, 100 do
    t[i] = i * 2
end
local sum = 0
for i = 1, 100 do
    sum = sum + t[i]
end
return sum
";

    [GlobalSetup]
    public void Setup()
    {
        _asyncLuaState = new LuaState();
        _nLuaState = new NLua.Lua();
        _moonSharpScript = new MoonSharp.Interpreter.Script();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _nLuaState?.Dispose();
    }

    [Benchmark]
    public LuaTuple AsyncLua_TableCreateAndSum()
    {
        return _asyncLuaState.Execute(TableScript);
    }

    [Benchmark]
    public object[] NLua_TableCreateAndSum()
    {
        return _nLuaState.DoString(TableScript);
    }

    [Benchmark]
    public MoonSharp.Interpreter.DynValue MoonSharp_TableCreateAndSum()
    {
        return _moonSharpScript.DoString(TableScript);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// State creation / teardown benchmarks
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Benchmarks for creating and destroying Lua states.
/// </summary>
[MemoryDiagnoser]
public class StateLifecycleBenchmarks
{
    [Benchmark]
    public LuaState AsyncLua_CreateState()
    {
        return new LuaState();
    }

    [Benchmark]
    public NLua.Lua NLua_CreateState()
    {
        var state = new NLua.Lua();
        state.Dispose();
        return state;
    }

    [Benchmark]
    public MoonSharp.Interpreter.Script MoonSharp_CreateState()
    {
        return new MoonSharp.Interpreter.Script();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Concurrency / async benchmarks (AsyncLua's killer feature!)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Benchmarks for async/await patterns — unique to AsyncLua.
/// </summary>
[MemoryDiagnoser]
public class ConcurrentBenchmarks
{
    private LuaState _asyncLuaState = null!;

    private const string AsyncScript = @"
local t1 = delay(35)
local t2 = delay(45)
local r1 = await t1
local r2 = await t2
return r1 + r2
";

    [GlobalSetup]
    public void Setup()
    {
        _asyncLuaState = new LuaState();

		// Register a delay function that returns a LuaTask
		_asyncLuaState.SetGlobal("delay", new LuaCallbackFunction(
			async (ctx, args) =>
			{
				double ms = 0;
				if (args.Length > 0) args[0].TryToNumber(out ms);
				var task = new LuaTask();
				await Task.Delay((int)ms);
				return new LuaTuple(new LuaNumber(ms));
			}, "delay"));
	}

    /// <summary>
    /// Executes two concurrent delays via async/await.
    /// NLua and MoonSharp have no native async support, so this is AsyncLua-only.
    /// </summary>
    [Benchmark]
    public async Task<LuaTuple> AsyncLua_ParallelAwait()
    {
        return await _asyncLuaState.ExecuteAsync(AsyncScript);
    }
}
