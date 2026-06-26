# AsyncLua

[![Build](https://github.com/your-org/AsyncLua/actions/workflows/build.yml/badge.svg)](https://github.com/your-org/AsyncLua/actions/workflows/build.yml)
[![Tests](https://github.com/your-org/AsyncLua/actions/workflows/tests.yml/badge.svg)](https://github.com/your-org/AsyncLua/actions/workflows/tests.yml)
[![NuGet](https://img.shields.io/nuget/v/AsyncLua)](https://www.nuget.org/packages/AsyncLua)

An extended Lua interpreter for C#, optimized for concurrency with **async/await** patterns.

## Features

- **Full Lua 5.5 syntax** - parser, compiler, register-based VM
- **Async/await** - native `async`/`await` support in Lua scripts
- **Continue keyword, augmented assigments**
- **Coroutines** - cooperative multitasking with `coroutine.create`/`resume`/`yield`
- **UserData** - seamlessly expose C# objects to Lua with automatic metatable generation and method overload resolution
- **Locking primitives** - `lock` keyword for thread-safe critical sections
- **.NET Standard 2.0** - compatible with legacy frameworks

## Quick start

```csharp
using AsyncLua;

var state = new LuaState()
    .LoadDefaultLibraries();

// Synchronous execution
state.Execute("print('Hello from Lua!')");

// Asynchronous execution with async/await
await state.ExecuteAsync(@"
    async function fetchData()
        -- Simulate async I/O
        await delay(100)
        return 'data received'
    end

    local result = await fetchData()
    print(result)
");

// Critical sections and try-catch with throw
await state.ExecuteAsync(@"
    local mutex = {}
    
    async function doWork1()
        lock mutex do
            try
                await delay(200)
                throw 'some error occured'
            catch ex do
                return ex
            end
        end
    end
    
    async function doWork2()
        lock mutex do
            await delay(100)
            return 'data successfully received'
        end
    end

    local t1, t2 = doWork1(), doWork2()
    local r1, r2 = await t1, await t2
    print(r1, r2)
");
```

## Projects

| Project | Description |
|---------|-------------|
| `src/AsyncLua` | Core library (netstandard2.0) |
| `tests/AsyncLua.Tests` | Unit & integration tests (xUnit) |
| `benchmarks/AsyncLua.Benchmarks` | Performance benchmarks (BenchmarkDotNet) |

## License

MIT
