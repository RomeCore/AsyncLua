using AsyncLua;
using AsyncLua.Compiling;
using AsyncLua.Interpreting;
using AsyncLua.Parsing;
using AsyncLua.Parsing.Statements;
using AsyncLua.Values;
using BenchmarkDotNet.Attributes;

namespace AsyncLua.Benchmarks;

/// <summary>
/// Benchmarks that measure each stage of the AsyncLua pipeline separately
/// with a heavier script (100k loop iterations).
/// </summary>
[MemoryDiagnoser]
public class PipelineHeavyBenchmarks
{
    private AsyncLuaParser _parser = null!;
    private LuaState _state = null!;
    private LuaCallingContext _callingContext = null!;

    private const string Code = @"
local sum = 0
for i = 1, 100000 do
    sum = sum + i
end
return sum
";

    // Pre-parsed block used for Compile-only benchmark.
    private BlockNode _preParsedBlock = null!;

    // Pre-parsed + pre-compiled prototype used for Execute-only benchmark.
    private FunctionPrototype _preCompiledPrototype = null!;

    [GlobalSetup]
    public void Setup()
    {
        _parser = new AsyncLuaParser();
        _state = new LuaState();
        _callingContext = _state.CreateContext();

        _preParsedBlock = _parser.Parse(Code);
        _preCompiledPrototype = AsyncLuaCompiler.Compile(_preParsedBlock);
    }

    /// <summary>
    /// Only the PEG parsing stage (lexer + grammar rules) for the heavy script.
    /// </summary>
    [Benchmark]
    public BlockNode Parse()
    {
        return _parser.Parse(Code);
    }

    /// <summary>
    /// Only the compilation stage (AST → bytecode).
    /// Requires a pre-parsed AST block.
    /// </summary>
    [Benchmark]
    public FunctionPrototype CompileOnly()
    {
        return AsyncLuaCompiler.Compile(_preParsedBlock);
    }

    /// <summary>
    /// Only the execution stage (register-based VM) — runs the 100k loop.
    /// Requires a pre-compiled function prototype.
    /// </summary>
    [Benchmark]
    public LuaTuple ExecuteOnly()
    {
        return AsyncLuaInterpreter.Call(_preCompiledPrototype, _callingContext);
    }

    /// <summary>
    /// Full pipeline: parse → compile → execute.
    /// </summary>
    [Benchmark]
    public LuaTuple FullPipeline()
    {
        var block = _parser.Parse(Code);
        var proto = AsyncLuaCompiler.Compile(block);
        return AsyncLuaInterpreter.Call(proto, _callingContext);
    }
}
