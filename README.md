# AsyncLua

[![Build](https://github.com/RomeCore/AsyncLua/actions/workflows/build.yml/badge.svg)](https://github.com/your-org/AsyncLua/actions/workflows/build.yml)
[![Tests](https://github.com/RomeCore/AsyncLua/actions/workflows/tests.yml/badge.svg)](https://github.com/your-org/AsyncLua/actions/workflows/tests.yml)
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

// 1. Synchronous execution
state.Execute("print('Hello from Lua!')");

// 2. Asynchronous execution with async/await
await state.ExecuteAsync(@"
    async function fetchData()
        -- Simulate async I/O
        await delay(100)
        return 'data received'
    end

    local result = await fetchData()
    print(result)
");

// 3. Critical sections and try-catch with throw
// You can compile code for later and repeated execution
var compiled = state.Compile(@"
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
            await delay(150)
            return 'data successfully received'
        end
    end

    async function doWork3()
        await delay(250)
        return 'another data successfully received'
    end

    local t1, t2, t3 = doWork1(), doWork2(), doWork3()
    local r1, r2, r3 = await t1, await t2, await t3 -- will take at least 350 ms because of locks
    print(r1, r2, r3)
");

// Then execute it!
await compiled.ExecuteAsync();
```

## Projects

| Project | Description |
|---------|-------------|
| `src/AsyncLua` | Core library (netstandard2.0) |
| `tests/AsyncLua.Tests` | Unit & integration tests (xUnit) |
| `benchmarks/AsyncLua.Benchmarks` | Performance benchmarks (BenchmarkDotNet) |

## License

MIT
