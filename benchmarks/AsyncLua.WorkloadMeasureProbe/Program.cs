using System.Diagnostics;
using AsyncLua;
using AsyncLua.Values;

var state = new LuaState().LoadDefaultLibraries();
var stopwatch = Stopwatch.StartNew();

// Host-side clock so every run reports its own timings, whatever launched it.
state.SetGlobal("tick", new LuaCallbackFunction(
	(ctx, args) =>
	{
		double iteration = 0;
		if (args.Length > 0)
			args[0].TryToNumber(out iteration);
		Console.WriteLine($"Iteration: {iteration}  at {stopwatch.Elapsed.TotalSeconds:F2} s");
		return new LuaTuple(new LuaNumber(stopwatch.Elapsed.TotalSeconds));
	}, "tick"));

await state.ExecuteAsync("""
	local iterations = 10000000
	local t = {}

	for i = 1, iterations do
		t[#t + 1] = i
		if i % 1000000 == 0 then
			tick(i)
		end
	end

	print("Finished")
	""");

stopwatch.Stop();
Console.WriteLine($"TOTAL: {stopwatch.Elapsed.TotalSeconds:F2} s");
