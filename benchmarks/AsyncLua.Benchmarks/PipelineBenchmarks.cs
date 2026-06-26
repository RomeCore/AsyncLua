using AsyncLua;
using AsyncLua.Compiling;
using AsyncLua.Interpreting;
using AsyncLua.Parsing;
using AsyncLua.Parsing.Statements;
using AsyncLua.Values;
using BenchmarkDotNet.Attributes;

namespace AsyncLua.Benchmarks;

/// <summary>
/// Benchmarks that measure each stage of the AsyncLua pipeline separately:
/// parsing, compilation, and execution.
/// </summary>
[MemoryDiagnoser]
public class PipelineBenchmarks
{
    private AsyncLuaParser _parser = null!;
    private LuaState _state = null!;
    private LuaCallingContext _callingContext = null!;

    private const string Code = "return 2 + 3 * 4";

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
    /// Only the PEG parsing stage (lexer + grammar rules).
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
    /// Only the execution stage (register-based VM).
    /// Requires a pre-compiled function prototype.
    /// </summary>
    [Benchmark]
    public LuaTuple ExecuteOnly()
    {
        return AsyncLuaInterpreter.Call(_preCompiledPrototype, _callingContext);
    }

    /// <summary>
    /// Full pipeline: parse → compile → execute (as in LuaState.Execute).
    /// </summary>
    [Benchmark]
    public LuaTuple FullPipeline()
    {
        var block = _parser.Parse(Code);
        var proto = AsyncLuaCompiler.Compile(block);
        return AsyncLuaInterpreter.Call(proto, _callingContext);
    }
}
