using System;
using System.Threading.Tasks;
using AsyncLua;

var state = new LuaState();
state.LoadDefaultLibraries();

async Task RunAsync(string title, string code)
{
	Console.Write($"{title}... ");
	try
	{
		var r = await state.ExecuteAsync(code);
		Console.Write("OK: ");
		for (int i = 0; i < r.Count; i++)
			Console.Write($"[{i}]={r[i]} ");
		Console.WriteLine();
	}
	catch (Exception ex)
	{
		Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
	}
}

// Debug: single yield then return
await RunAsync("Y1", @"
    local co = coroutine.create(async function()
        local x = await coroutine.yield('y1')
        return x .. '_done'
    end)
    local ok1, v1 = await coroutine.resume(co)
    local ok2, v2 = await coroutine.resume(co, 'test')
    return ok1, v1, ok2, v2
");

// Debug: two yields
await RunAsync("Y2", @"
    local co = coroutine.create(async function()
        local a = await coroutine.yield('a')
        local b = await coroutine.yield('b')
        return a .. '_' .. b
    end)
    local ok1, v1 = await coroutine.resume(co)
    print('after r1: v1=' .. v1)
    local ok2, v2 = await coroutine.resume(co, 'HELLO')
    print('after r2: ok2=' .. tostring(ok2) .. ' v2=' .. tostring(v2))
    local ok3, v3 = await coroutine.resume(co, 'WORLD')
    print('after r3: ok3=' .. tostring(ok3) .. ' v3=' .. tostring(v3))
    return ok1 and ok2 and ok3, v1, v2, v3
");
