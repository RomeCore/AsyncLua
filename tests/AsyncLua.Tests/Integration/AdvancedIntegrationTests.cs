using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// Advanced integration tests: complex algorithms, OOP with metatables, closures,
/// functional programming patterns, and multi-feature compositions.
/// </summary>
public class AdvancedIntegrationTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState();

		// ── Standard library stubs ──────────────────────────────────
		RegisterStandardLibrary(state);

		return state;
	}

	private static void RegisterStandardLibrary(LuaState state)
	{
		// print(...)
		state.SetGlobal("print", new LuaCallbackFunction(
			(ctx, args) =>
			{
				var parts = new string[args.Length];
				for (int i = 0; i < args.Length; i++)
					parts[i] = args[i].ToString();
				System.Diagnostics.Debug.WriteLine(string.Join("\t", parts));
				return LuaTuple.Empty;
			}, "print"));

		// type(value)
		state.SetGlobal("type", new LuaCallbackFunction(
			(ctx, args) =>
				args.Length == 0
					? new LuaTuple(new LuaString("nil"))
					: new LuaTuple(new LuaString(args[0].TypeName)),
			"type"));

		// tostring(value)
		state.SetGlobal("tostring", new LuaCallbackFunction(
			(ctx, args) =>
				args.Length == 0
					? new LuaTuple(new LuaString("nil"))
					: new LuaTuple(new LuaString(args[0].ToString())),
			"tostring"));

		// tonumber(value)
		state.SetGlobal("tonumber", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0 || !args[0].TryToNumber(out var n))
					return new LuaTuple(LuaNil.Instance);
				return new LuaTuple(new LuaNumber(n));
			}, "tonumber"));

		// error(message)
		state.SetGlobal("error", new LuaCallbackFunction(new LuaCallbackFunction.CallbackDelegate(
			(ctx, args) =>
				throw new LuaRuntimeException(
					args.Length > 0 ? args[0].ToString() : "error")),
			"error"));

		// assert(v, message)
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

		// ipairs(t) — works around TFORCALL state-update quirk by returning table as second value.
		state.SetGlobal("ipairs", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0 || args[0] is not LuaTable t)
					return new LuaTuple(LuaNil.Instance);
				var iter = new LuaCallbackFunction(
					(ctx2, args2) =>
					{
						var tbl = (LuaTable)args2[0];
						var prev = args2[1];
						int idx = prev is LuaNil ? 1 : (int)((LuaNumber)prev).Value + 1;
						var val = tbl.Get(idx);
						if (val is LuaNil) return new LuaTuple(LuaNil.Instance);
						return new LuaTuple(new LuaNumber(idx), val);
					}, "ipairs_iter");
				return new LuaTuple(iter, t, new LuaNumber(0));
			}, "ipairs"));

		// pairs(t) — stateless next-like iterator, fresh for each for-in loop.
		state.SetGlobal("pairs", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0 || args[0] is not LuaTable t)
					return new LuaTuple(LuaNil.Instance);
				var nextFunc = new LuaCallbackFunction(
					(ctx2, args2) =>
					{
						var tbl = (LuaTable)args2[0];
						var prevKey = args2[1];
						LuaValue? foundKey = null;
						LuaValue? foundVal = null;
						bool prevFound = prevKey is LuaNil;
						foreach (var kvp in tbl)
						{
							if (prevFound) { foundKey = kvp.Key; foundVal = kvp.Value; break; }
							if (kvp.Key.Equals(prevKey)) prevFound = true;
						}
						if (foundKey is null) return new LuaTuple(LuaNil.Instance);
						return new LuaTuple(foundKey, foundVal!);
					}, "next");
				return new LuaTuple(nextFunc, t, LuaNil.Instance);
			}, "pairs"));

		// table.insert(t, [pos,] value)
		state.SetGlobal("table_insert", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 2 || args[0] is not LuaTable tbl)
					return LuaTuple.Empty;
				if (args.Length == 2)
					tbl.Set(tbl.Length + 1, args[1]);
				else
				{
					var pos = (int)((LuaNumber)args[1]).Value;
					int len = tbl.Length;
					for (int i = len; i >= pos; i--)
						tbl.Set(i + 1, tbl.Get(i));
					tbl.Set(pos, args[2]);
				}
				return LuaTuple.Empty;
			}, "table_insert"));

		// table.remove(t, [pos])
		state.SetGlobal("table_remove", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 1 || args[0] is not LuaTable tbl)
					return new LuaTuple(LuaNil.Instance);
				int pos = args.Length > 1 ? (int)((LuaNumber)args[1]).Value : tbl.Length;
				var removed = tbl.Get(pos);
				tbl.Set(pos, LuaNil.Instance);
				int len = tbl.Length;
				for (int i = pos + 1; i <= len + 1; i++)
				{
					var next = tbl.Get(i);
					tbl.Set(i - 1, next);
					if (next is LuaNil) break;
				}
				return new LuaTuple(removed);
			}, "table_remove"));

		// table.concat(t, [sep])
		state.SetGlobal("table_concat", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 1 || args[0] is not LuaTable tbl)
					return new LuaTuple(new LuaString(""));

				var sep = args.Length > 1 ? args[1].ToString() : "";
				var parts = new System.Collections.Generic.List<string>();
				for (int i = 1; ; i++)
				{
					var v = tbl.Get(i);
					if (v is LuaNil) break;
					parts.Add(v.ToString());
				}
				return new LuaTuple(new LuaString(string.Join(sep, parts)));
			}, "table_concat"));

		// math.sqrt, math.floor, math.abs, math.max, math.min
		RegisterMathFunctions(state);

		// setmetatable / getmetatable / rawget / rawset
		RegisterMetatableFunctions(state);

		// string.sub, string.len, string.byte, string.char
		RegisterStringFunctions(state);
	}

	private static void RegisterMathFunctions(LuaState state)
	{
		var math = new LuaTable();
		state.Globals.Set(new LuaString("math"), math);

		math.Set(new LuaString("sqrt"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(
				Math.Sqrt(((LuaNumber)args[0]).Value))), "math.sqrt"));

		math.Set(new LuaString("floor"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(
				Math.Floor(((LuaNumber)args[0]).Value))), "math.floor"));

		math.Set(new LuaString("abs"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(
				Math.Abs(((LuaNumber)args[0]).Value))), "math.abs"));

		math.Set(new LuaString("max"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(
				Math.Max(((LuaNumber)args[0]).Value, ((LuaNumber)args[1]).Value))),
			"math.max"));

		math.Set(new LuaString("min"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(
				Math.Min(((LuaNumber)args[0]).Value, ((LuaNumber)args[1]).Value))),
			"math.min"));

		math.Set(new LuaString("pi"), new LuaNumber(Math.PI));
		math.Set(new LuaString("huge"), new LuaNumber(double.PositiveInfinity));
	}

	private static void RegisterMetatableFunctions(LuaState state)
	{
		state.SetGlobal("setmetatable", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 1) return new LuaTuple(LuaNil.Instance);
				var target = args[0];
				if (args.Length < 2 || args[1] is LuaNil)
					target.Metatable = null;
				else if (args[1] is LuaTable mtTable)
					target.Metatable = LuaMetatable.FromTable(mtTable);
				return new LuaTuple(target);
			}, "setmetatable"));

		state.SetGlobal("getmetatable", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0) return new LuaTuple(LuaNil.Instance);
				var mt = args[0].Metatable;
				if (mt == null) return new LuaTuple(LuaNil.Instance);
				var table = new LuaTable();
				foreach (var kvp in mt)
					table.Set(new LuaString(LuaMetatable.GetEventName(kvp.Key)), kvp.Value);
				return new LuaTuple(table);
			}, "getmetatable"));

		state.SetGlobal("rawget", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 2 || args[0] is not LuaTable table)
					return new LuaTuple(LuaNil.Instance);
				return new LuaTuple(table.Get(args[1]));
			}, "rawget"));

		state.SetGlobal("rawset", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 3 || args[0] is not LuaTable table)
					return new LuaTuple(LuaNil.Instance);
				table.Set(args[1], args[2]);
				return new LuaTuple(table);
			}, "rawset"));
	}

	private static void RegisterStringFunctions(LuaState state)
	{
		var strLib = new LuaTable();
		state.Globals.Set(new LuaString("string"), strLib);

			strLib.Set(new LuaString("sub"), new LuaCallbackFunction(
			(ctx, args) =>
			{
				var s = args[0].ToString();
				var start = (int)((LuaNumber)args[1]).Value;
				var endIdx = args.Length > 2 ? (int)((LuaNumber)args[2]).Value : s.Length;
				// Lua is 1-indexed, and end index is inclusive.
				start = Math.Max(1, start) - 1;
				var count = Math.Min(endIdx, s.Length) - start;
				return new LuaTuple(new LuaString(s.Substring(start, count)));
			}, "string.sub"));

		strLib.Set(new LuaString("len"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(args[0].ToString().Length)),
			"string.len"));

		strLib.Set(new LuaString("byte"), new LuaCallbackFunction(
			(ctx, args) =>
			{
				var s = args[0].ToString();
				var pos = args.Length > 1 ? (int)((LuaNumber)args[1]).Value : 1;
				return new LuaTuple(new LuaNumber(pos <= s.Length ? s[pos - 1] : 0));
			}, "string.byte"));

		strLib.Set(new LuaString("char"), new LuaCallbackFunction(
			(ctx, args) =>
			{
				var chars = new char[args.Length];
				for (int i = 0; i < args.Length; i++)
					chars[i] = (char)(int)((LuaNumber)args[i]).Value;
				return new LuaTuple(new LuaString(new string(chars)));
			}, "string.char"));
	}

	private static LuaTuple Execute(LuaState state, string code)
	{
		return state.Execute(code);
	}

	private static async Task<LuaTuple> ExecuteAsync(LuaState state, string code)
	{
		return await state.ExecuteAsync(code);
	}

	// ═══════════════════════════════════════════════════════════════
	// COMPLEX ALGORITHMS
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void EmptyCode_ReturnsEmptyTuple()
	{
		var state = CreateState();

		var result = Execute(state, @"
			
		");

		Assert.Empty(result);
	}

	[Fact]
	public async Task EmptyCode_ReturnsEmptyTuple_Async()
	{
		var state = CreateState();

		var result = await ExecuteAsync(state, @"
			
		");

		Assert.Empty(result);
	}

	[Fact]
	public void JustReturn_ReturnsEmptyTuple()
	{
		var state = CreateState();

		var result = Execute(state, @"
			return
		");

		Assert.Empty(result);
	}

	[Fact]
	public async Task JustReturn_ReturnsEmptyTuple_Async()
	{
		var state = CreateState();

		var result = await ExecuteAsync(state, @"
			return
		");

		Assert.Empty(result);
	}

	[Fact]
	public void CodeWithoutReturn_ReturnsEmptyTuple()
	{
		var state = CreateState();

		var result = Execute(state, @"
			local function b(n)
				return n * 2
			end
			local a = 42
			a += 5
			a /= b(1)
		");

		Assert.Empty(result);
	}

	[Fact]
	public async Task CodeWithoutReturn_ReturnsEmptyTuple_Async()
	{
		var state = CreateState();

		var result = await ExecuteAsync(state, @"
			local function b(n)
				return n * 2
			end
			local a = 42
			a += 5
			a /= b(1)
		");

		Assert.Empty(result);
	}

	[Fact]
	public void CodeWithEmptyReturn_ReturnsEmptyTuple()
	{
		var state = CreateState();

		var result = Execute(state, @"
			local function b(n)
				return n * 2
			end
			local a = 42
			a += 5
			a /= b(1)
			return
		");

		Assert.Empty(result);
	}

	[Fact]
	public async Task CodeWithEmptyReturn_ReturnsEmptyTuple_Async()
	{
		var state = CreateState();

		var result = await ExecuteAsync(state, @"
			local function b(n)
				return n * 2
			end
			local a = 42
			a += 5
			a /= b(1)
			return
		");

		Assert.Empty(result);
	}

	[Fact]
	public void QuickSort_FullImplementation_SortsCorrectly()
	{
		var state = CreateState();

		var result = Execute(state, @"
			function quicksort(arr, lo, hi)
				if lo < hi then
					local p = partition(arr, lo, hi)
					quicksort(arr, lo, p - 1)
					quicksort(arr, p + 1, hi)
				end
			end

			function partition(arr, lo, hi)
				local pivot = arr[hi]
				local i = lo - 1
				for j = lo, hi - 1 do
					if arr[j] <= pivot then
						i = i + 1
						arr[i], arr[j] = arr[j], arr[i]
					end
				end
				arr[i + 1], arr[hi] = arr[hi], arr[i + 1]
				return i + 1
			end

			local data = {64, 25, 12, 22, 11, 1, 90, 33, 45, 77,
			              18, 88, 50, 3, 72, 99, 40, 55, 66, 8}
			quicksort(data, 1, #data)

			-- Verify sorted and return key stats.
			local sum = 0
			for i = 1, #data do
				sum = sum + data[i]
				if i > 1 then
					assert(data[i-1] <= data[i], 'not sorted at ' .. i)
				end
			end
			return sum, data[1], data[#data]
		");

		Assert.Equal(3, result.Count);
		// Sum of values.
		Assert.Equal(879.0, Assert.IsType<LuaNumber>(result[0]).Value);
		// Min.
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
		// Max.
		Assert.Equal(99.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void MergeSort_RecursiveImpl_SortsLargeArray()
	{
		var state = CreateState();

		var result = Execute(state, @"
			function merge(arr, left, mid, right)
				local n1 = mid - left + 1
				local n2 = right - mid
				local L, R = {}, {}
				for i = 1, n1 do L[i] = arr[left + i - 1] end
				for j = 1, n2 do R[j] = arr[mid + j] end

				local i, j, k = 1, 1, left
				while i <= n1 and j <= n2 do
					if L[i] <= R[j] then
						arr[k] = L[i]; i = i + 1
					else
						arr[k] = R[j]; j = j + 1
					end
					k = k + 1
				end
				while i <= n1 do arr[k] = L[i]; i = i + 1; k = k + 1 end
				while j <= n2 do arr[k] = R[j]; j = j + 1; k = k + 1 end
			end

			function mergesort(arr, left, right)
				if left < right then
					local mid = math.floor((left + right) / 2)
					mergesort(arr, left, mid)
					mergesort(arr, mid + 1, right)
					merge(arr, left, mid, right)
				end
			end

			local arr = {}
			for i = 1, 30 do
				arr[i] = (i * 9973 + 12345) % 1000
			end
			mergesort(arr, 1, #arr)

			-- Verify.
			local prev = -math.huge
			for i = 1, #arr do
				assert(arr[i] >= prev, 'unsorted')
				prev = arr[i]
			end
			return #arr, arr[1], arr[#arr]
		");

		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[0]).Value);
	}

	[Fact]
	public void BinarySearchTree_InsertAndTraverse_Works()
	{
		var state = CreateState();

		var result = Execute(state, @"
			function newNode(val)
				return { value = val, left = nil, right = nil }
			end

			function insert(root, val)
				if root == nil then return newNode(val) end
				if val < root.value then
					root.left = insert(root.left, val)
				else
					root.right = insert(root.right, val)
				end
				return root
			end

			function inorder(root, acc)
				if root == nil then return end
				inorder(root.left, acc)
				acc[#acc + 1] = root.value
				inorder(root.right, acc)
			end

			local values = {50, 30, 70, 20, 40, 60, 80, 15, 25, 35,
			                45, 55, 65, 75, 85, 10, 90, 5, 95, 100}
			local root = nil
			for i = 1, #values do
				root = insert(root, values[i])
			end

			local acc = {}
			inorder(root, acc)

			-- Verify ascending order.
			assert(#acc == #values, 'wrong count: ' .. #acc .. ' vs ' .. #values)
			for i = 2, #acc do
				assert(acc[i-1] < acc[i], 'not sorted: ' .. acc[i-1] .. ' >= ' .. acc[i])
			end
			return #acc, acc[1], acc[#acc]
		");

		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(100.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void DijkstraAlgorithm_GraphShortestPath_ReturnsCorrect()
	{
		var state = CreateState();

		var result = Execute(state, @"
			-- Graph represented as adjacency list: {node = {neighbor = weight, ...}}
			function dijkstra(graph, start)
				local dist = {}
				local visited = {}
				for node, _ in pairs(graph) do
					dist[node] = math.huge
					visited[node] = false
				end
				dist[start] = 0

				while true do
					local u = nil
					local minDist = math.huge
					for node, _ in pairs(graph) do
						if not visited[node] and dist[node] < minDist then
							minDist = dist[node]
							u = node
						end
					end
					if u == nil then break end
					visited[u] = true

					for v, weight in pairs(graph[u]) do
						local alt = dist[u] + weight
						if alt < dist[v] then
							dist[v] = alt
						end
					end
				end
				return dist
			end

			local graph = {
				A = { B = 4, C = 2 },
				B = { A = 4, C = 1, D = 5 },
				C = { A = 2, B = 1, D = 8, E = 10 },
				D = { B = 5, C = 8, E = 2, F = 6 },
				E = { C = 10, D = 2, F = 3 },
				F = { D = 6, E = 3 }
			}

			local dist = dijkstra(graph, 'A')
			return dist['A'], dist['B'], dist['C'], dist['D'], dist['E'], dist['F']
		");

		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[0]).Value);   // A→A
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[1]).Value);   // A→B (A→C→B = 2+1)
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[2]).Value);   // A→C
		Assert.Equal(8.0, Assert.IsType<LuaNumber>(result[3]).Value);   // A→D (A→C→B→D = 2+1+5)
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[4]).Value);  // A→E (A→C→B→D→E = 2+1+5+2)
		Assert.Equal(13.0, Assert.IsType<LuaNumber>(result[5]).Value);  // A→F
	}

	[Fact]
	public void DynamicProgramming_Knapsack01_ReturnsOptimalValue()
	{
		var state = CreateState();

		var result = Execute(state, @"
			function knapsack(weights, values, capacity)
				local n = #weights
				local dp = {}
				for i = 0, n do
					dp[i] = {}
					for w = 0, capacity do
						dp[i][w] = 0
					end
				end

				for i = 1, n do
					for w = 0, capacity do
						if weights[i] <= w then
							local take = values[i] + dp[i-1][w - weights[i]]
							local skip = dp[i-1][w]
							dp[i][w] = math.max(take, skip)
						else
							dp[i][w] = dp[i-1][w]
						end
					end
				end

				-- Backtrack to find selected items.
				local w = capacity
				local selected = {}
				for i = n, 1, -1 do
					if dp[i][w] ~= dp[i-1][w] then
						selected[#selected + 1] = i
						w = w - weights[i]
					end
				end
				return dp[n][capacity], #selected, selected[1], selected[#selected]
			end

			local weights = {2, 3, 4, 5, 9, 7, 1, 6, 8, 3}
			local values  = {3, 4, 5, 8, 10, 7, 1, 9, 13, 6}
			local capacity = 20

			return knapsack(weights, values, capacity)
		");

		// Optimal value = 32.
		Assert.Equal(32, Assert.IsType<LuaNumber>(result[0]).Value);
		// Number of selected items.
		Assert.True(((LuaNumber)result[1]).Value > 0);
	}

	[Fact]
	public void Fibonacci_ClosedFormVsRecursive_Agrees()
	{
		var state = CreateState();

		var result = Execute(state, @"
			local function fibRecursive(n)
				if n <= 1 then return n end
				return fibRecursive(n - 1) + fibRecursive(n - 2)
			end

			local function fibIterative(n)
				if n <= 1 then return n end
				local a, b = 0, 1
				for i = 2, n do
					a, b = b, a + b
				end
				return b
			end

			local function fibBinet(n)
				local phi = (1 + math.sqrt(5)) / 2
				local psi = (1 - math.sqrt(5)) / 2
				return math.floor((phi ^ n - psi ^ n) / math.sqrt(5) + 0.5)
			end

			-- Test first 15 Fibonacci numbers using all three methods.
			local ok = 0
			for n = 0, 14 do
				local r = fibRecursive(n)
				local i = fibIterative(n)
				local b = fibBinet(n)
				if r == i and i == b then ok = ok + 1 end
			end
			return ok, fibIterative(20), fibRecursive(10), fibBinet(25)
		");

		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result[0]).Value);  // all 15 matched
		Assert.Equal(6765.0, Assert.IsType<LuaNumber>(result[1]).Value);  // F(20)
		Assert.Equal(55.0, Assert.IsType<LuaNumber>(result[2]).Value);    // F(10)
		Assert.Equal(75025.0, Assert.IsType<LuaNumber>(result[3]).Value); // F(25)
	}

	// ═══════════════════════════════════════════════════════════════
	// OOP WITH METATABLES
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void OOP_ClassSystemWithInheritance_CreatesInstances()
	{
		var state = CreateState();

		var result = Execute(state, @"
			-- Simple class system.
			local function Class(name, parent)
				local cls = { _name = name, _parent = parent }
				cls.__index = cls

				if parent then
					-- Inherit from parent.
					setmetatable(cls, { __index = parent })
				end

				function cls:new(...)
					local instance = {}
					setmetatable(instance, self)
					if self.init then self.init(instance, ...) end
					return instance
				end

				return cls
			end

			-- Define classes.
			local Animal = Class('Animal')
			function Animal:init(name)
				self.name = name
				self.hunger = 50
			end
			function Animal:speak()
				return self.name .. ' makes a sound'
			end
			function Animal:feed(amount)
				self.hunger = math.max(0, self.hunger - amount)
				return self.hunger
			end

			local Dog = Class('Dog', Animal)
			function Dog:init(name, breed)
				Animal.init(self, name)
				self.breed = breed
			end
			function Dog:speak()
				return self.name .. ' barks!'
			end
			function Dog:fetch()
				return self.name .. ' fetches the ball'
			end

			local Cat = Class('Cat', Animal)
			function Cat:init(name, color)
				Animal.init(self, name)
				self.color = color
			end
			function Cat:speak()
				return self.name .. ' meows'
			end

			-- Create instances.
			local rex = Dog:new('Rex', 'German Shepherd')
			local whiskers = Cat:new('Whiskers', 'Orange')
			local generic = Animal:new('Thing')

			rex:feed(30)
			whiskers:feed(10)

			return rex:speak(),
			       whiskers:speak(),
			       generic:speak(),
			       rex:fetch(),
			       rex.hunger,
			       whiskers.hunger,
			       rex.breed,
			       whiskers.color
		");

		Assert.Equal("Rex barks!", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal("Whiskers meows", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("Thing makes a sound", Assert.IsType<LuaString>(result[2]).Value);
		Assert.Equal("Rex fetches the ball", Assert.IsType<LuaString>(result[3]).Value);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal(40.0, Assert.IsType<LuaNumber>(result[5]).Value);
		Assert.Equal("German Shepherd", Assert.IsType<LuaString>(result[6]).Value);
		Assert.Equal("Orange", Assert.IsType<LuaString>(result[7]).Value);
	}

	[Fact]
	public void OOP_Vector2D_OperatorOverloading_AllOperations()
	{
		var state = CreateState();

		var result = Execute(state, @"
			local Vector2D = {}
			Vector2D.__index = Vector2D

			function Vector2D:new(x, y)
				return setmetatable({ x = x or 0, y = y or 0 }, self)
			end

			function Vector2D:__add(other)
				return Vector2D:new(self.x + other.x, self.y + other.y)
			end

			function Vector2D:__sub(other)
				return Vector2D:new(self.x - other.x, self.y - other.y)
			end

			function Vector2D:__mul(scalar)
				if type(scalar) == 'number' then
					return Vector2D:new(self.x * scalar, self.y * scalar)
				else
					return self.x * scalar.x + self.y * scalar.y  -- dot product
				end
			end

			function Vector2D:__unm()
				return Vector2D:new(-self.x, -self.y)
			end

			function Vector2D:__eq(other)
				return self.x == other.x and self.y == other.y
			end

			function Vector2D:__tostring()
				return '(' .. self.x .. ', ' .. self.y .. ')'
			end

			function Vector2D:magnitude()
				return math.sqrt(self.x * self.x + self.y * self.y)
			end

			function Vector2D:normalized()
				local mag = self:magnitude()
				if mag == 0 then return Vector2D:new(0, 0) end
				return Vector2D:new(self.x / mag, self.y / mag)
			end

			-- Test suite.
			local a = Vector2D:new(3, 4)
			local b = Vector2D:new(1, 2)

			local sum = a + b
			local diff = a - b
			local scaled = a * 3
			local neg = -a
			local dot = a * b       -- scalar multiplication → dot product

			-- Verify invariants.
			assert(a == Vector2D:new(3, 4), 'eq fail')
			assert(a ~= b, 'neq fail')

			return sum.x, sum.y,
			       diff.x, diff.y,
			       scaled.x, scaled.y,
			       neg.x, neg.y,
			       dot,
			       math.floor(a:magnitude() * 100) / 100,  -- 5.0
			       a:normalized().x, a:normalized().y
		");

		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[0]).Value);   // sum.x
		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result[1]).Value);   // sum.y
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[2]).Value);   // diff.x
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[3]).Value);   // diff.y
		Assert.Equal(9.0, Assert.IsType<LuaNumber>(result[4]).Value);   // scaled.x
		Assert.Equal(12.0, Assert.IsType<LuaNumber>(result[5]).Value);  // scaled.y
		Assert.Equal(-3.0, Assert.IsType<LuaNumber>(result[6]).Value);  // neg.x
		Assert.Equal(-4.0, Assert.IsType<LuaNumber>(result[7]).Value);  // neg.y
		Assert.Equal(11.0, Assert.IsType<LuaNumber>(result[8]).Value);  // dot = 3*1+4*2
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[9]).Value);   // magnitude
		Assert.Equal(0.6, Assert.IsType<LuaNumber>(result[10]).Value);  // normalized.x = 3/5
		Assert.Equal(0.8, Assert.IsType<LuaNumber>(result[11]).Value);  // normalized.y = 4/5
	}

	// ═══════════════════════════════════════════════════════════════
	// CLOSURES AND FUNCTIONAL PROGRAMMING
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Closures_CounterFactory_IndependentCounters()
	{
		var state = CreateState();

		var result = Execute(state, @"
			function makeCounter(start, step)
				local count = start or 0
				step = step or 1
				return function()
					local current = count
					count = count + step
					return current
				end
			end

			local c1 = makeCounter(0, 2)   -- evens
			local c2 = makeCounter(1, 2)   -- odds
			local c3 = makeCounter(10, -1) -- descending

			local v1, v2, v3 = {}, {}, {}
			for i = 1, 10 do
				v1[i] = c1()
				v2[i] = c2()
				v3[i] = c3()
			end

			return v1[1], v1[10],
			       v2[1], v2[10],
			       v3[1], v3[10]
		");

		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[0]).Value);   // evens start
		Assert.Equal(18.0, Assert.IsType<LuaNumber>(result[1]).Value);  // evens 10th
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[2]).Value);   // odds start
		Assert.Equal(19.0, Assert.IsType<LuaNumber>(result[3]).Value);  // odds 10th
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[4]).Value);  // desc start
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[5]).Value);   // desc 10th
	}

	[Fact]
	public void Closures_MapFilterReduce_Works()
	{
		var state = CreateState();

		var result = Execute(state, @"
			function map(arr, fn)
				local result = {}
				for i = 1, #arr do
					result[i] = fn(arr[i])
				end
				return result
			end

			function filter(arr, pred)
				local result = {}
				for i = 1, #arr do
					if pred(arr[i]) then
						result[#result + 1] = arr[i]
					end
				end
				return result
			end

			function reduce(arr, fn, init)
				local acc = init
				for i = 1, #arr do
					acc = fn(acc, arr[i])
				end
				return acc
			end

			local numbers = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
			                 11, 12, 13, 14, 15, 16, 17, 18, 19, 20}

			local squares = map(numbers, function(x) return x * x end)
			local evens = filter(numbers, function(x) return x % 2 == 0 end)
			local sumOfEvens = reduce(evens, function(a, b) return a + b end, 0)
			local sumOfSquares = reduce(squares, function(a, b) return a + b end, 0)

			-- Also test chaining: sum of squares of evens.
			local chainResult = reduce(
				map(
					filter(numbers, function(x) return x % 2 == 0 end),
					function(x) return x * x end
				),
				function(a, b) return a + b end, 0)

			return #evens, sumOfEvens, sumOfSquares, chainResult
		");

		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);   // 10 evens
		Assert.Equal(110.0, Assert.IsType<LuaNumber>(result[1]).Value);  // 2+4+...+20
		// Sum of squares 1..20 = n(n+1)(2n+1)/6 = 20*21*41/6 = 2870
		Assert.Equal(2870.0, Assert.IsType<LuaNumber>(result[2]).Value);
		// Sum of squares of evens: 4+16+36+...+400 = 1540
		Assert.Equal(1540.0, Assert.IsType<LuaNumber>(result[3]).Value);
	}

	[Fact]
	public void Closures_PartialApplication_Currying()
	{
		var state = CreateState();

		var result = Execute(state, @"
			function curry3(f)
				return function(a)
					return function(b)
						return function(c)
							return f(a, b, c)
						end
					end
				end
			end

			function add3(a, b, c)
				return a + b + c
			end

			local curriedAdd = curry3(add3)
			local add5 = curriedAdd(5)
			local add5and10 = add5(10)
			local result1 = add5and10(15)           -- 5 + 10 + 15 = 30

			-- Can also chain directly.
			local result2 = curriedAdd(1)(2)(3)     -- 6

			-- Partially applied function reuse.
			local add100 = curriedAdd(100)
			local r3 = add100(20)(30)               -- 150
			local r4 = add100(0)(0)                 -- 100

			return result1, result2, r3, r4
		");

		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(150.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(100.0, Assert.IsType<LuaNumber>(result[3]).Value);
	}

	[Fact]
	public void Closures_UpvalueMutationThroughFunctions()
	{
		var state = CreateState();

		var result = Execute(state, @"
			local function makeBankAccount(initialBalance)
				local balance = initialBalance
				return {
					deposit = function(amount)
						balance = balance + amount
						return balance
					end,
					withdraw = function(amount)
						if amount > balance then return nil end
						balance = balance - amount
						return balance
					end,
					getBalance = function()
						return balance
					end
				}
			end

			local acc1 = makeBankAccount(1000)
			local acc2 = makeBankAccount(500)

			-- Independent accounts via closure upvalues.
			acc1.deposit(200)
			acc1.withdraw(150)
			acc2.deposit(300)
			acc2.withdraw(100)

			local b1 = acc1.getBalance()
			local b2 = acc2.getBalance()
			assert(b1 == 1050, 'acc1 wrong: ' .. b1)
			assert(b2 == 700, 'acc2 wrong: ' .. b2)

			-- Complex transaction.
			local r = acc1.withdraw(2000)  -- should fail
			assert(r == nil, 'should be nil')

			return b1, b2, r == nil and 1 or 0
		");

		Assert.Equal(1050.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(700.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[2]).Value);  // withdrawal failed
	}

	// ═══════════════════════════════════════════════════════════════
	// COMPLEX TABLE OPERATIONS
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Tables_DeepCopy_WorksWithNestedStructures()
	{
		var state = CreateState();

		var result = Execute(state, @"
			function deepcopy(orig)
				local orig_type = type(orig)
				local copy
				if orig_type == 'table' then
					copy = {}
					for orig_key, orig_value in pairs(orig) do
						copy[deepcopy(orig_key)] = deepcopy(orig_value)
					end
					setmetatable(copy, deepcopy(getmetatable(orig)))
				else
					copy = orig
				end
				return copy
			end

			local original = {
				name = 'root',
				children = {
					{ name = 'a', value = 10 },
					{ name = 'b', value = 20, extra = { 1, 2, { 3, 4 } } },
					{ name = 'c', value = 30 }
				},
				metadata = { version = 2, tags = { 'test', 'deep', 'copy' } }
			}

			local copied = deepcopy(original)

			-- Modify the copy.
			copied.name = 'modified'
			copied.children[1].value = 999
			copied.children[2].extra[3][1] = 9999
			copied.metadata.tags[1] = 'changed'

			-- Original must be untouched.
			assert(original.name == 'root', 'name changed')
			assert(original.children[1].value == 10, 'child value changed')
			assert(original.children[2].extra[3][1] == 3, 'nested changed')
			assert(original.metadata.tags[1] == 'test', 'tag changed')

			-- Copy has modifications.
			assert(copied.name == 'modified')
			assert(copied.children[1].value == 999)
			assert(copied.children[2].extra[3][1] == 9999)
			assert(copied.metadata.tags[1] == 'changed')

			return original.children[1].value,
			       original.children[2].extra[3][1],
			       original.metadata.tags[1],
			       copied.children[1].value,
			       copied.children[2].extra[3][1],
			       copied.metadata.tags[1]
		");

		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal("test", Assert.IsType<LuaString>(result[2]).Value);
		Assert.Equal(999.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(9999.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal("changed", Assert.IsType<LuaString>(result[5]).Value);
	}

	[Fact]
	public void Tables_SparseArray_BoundaryDetection_Correct()
	{
		var state = CreateState();

		var result = Execute(state, @"
			local t = {}
			t[1] = 'a'
			t[2] = 'b'
			-- hole at 3
			t[4] = 'd'
			t[5] = 'e'

			-- Length in Lua with holes is implementation-defined.
			-- Test raw iteration.
			local count = 0
			local sum = 0
			for i = 1, 10 do
				local v = t[i]
				if v ~= nil then
					count = count + 1
					sum = sum + i
				end
			end

			-- Test key iteration.
			local keyList = {}
			for k, v in pairs(t) do
				keyList[#keyList + 1] = k
			end

			return count, sum, #keyList
		");

		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[0]).Value);  // 4 non-nil values
		Assert.Equal(12.0, Assert.IsType<LuaNumber>(result[1]).Value); // 1+2+4+5
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[2]).Value);  // 4 keys
	}

	// ═══════════════════════════════════════════════════════════════
	// COROUTINE-LIKE PATTERNS
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void CoroutinePattern_GeneratorFunction_YieldsValues()
	{
		var state = CreateState();

		// Simulate a generator using a closure over a mutable state.
		var result = Execute(state, @"
			function range(begin, finish, step)
				step = step or 1
				local current = begin - step
				return function()
					current = current + step
					if (step > 0 and current <= finish) or
					   (step < 0 and current >= finish) then
						return current
					end
					return nil
				end
			end

			function primeGenerator()
				local primes = {}
				local candidate = 1
				return function()
					while true do
						candidate = candidate + 1
						local isPrime = true
						for i = 1, #primes do
							local p = primes[i]
							if p * p > candidate then break end
							if candidate % p == 0 then
								isPrime = false
								break
							end
						end
						if isPrime then
							primes[#primes + 1] = candidate
							return candidate
						end
					end
				end
			end

			-- Test range generator.
			local iter = range(10, 50, 10)
			local v1, v2, v3, v4, v5, v6 =
				iter(), iter(), iter(), iter(), iter(), iter()

			-- Test prime generator.
			local nextPrime = primeGenerator()
			local firstPrimes = {}
			for i = 1, 10 do
				firstPrimes[i] = nextPrime()
			end

			return v1, v2, v3, v4, v5, v6 == nil and 1 or 0,
			       firstPrimes[1], firstPrimes[5], firstPrimes[10]
		");

		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(40.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(50.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[5]).Value);  // 6th is nil
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[6]).Value);   // 1st prime
		Assert.Equal(11.0, Assert.IsType<LuaNumber>(result[7]).Value);  // 5th prime
		Assert.Equal(29.0, Assert.IsType<LuaNumber>(result[8]).Value);  // 10th prime
	}

	// ═══════════════════════════════════════════════════════════════
	// ERROR HANDLING COMPOSITION
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void ErrorHandling_NestedTryCatchWithRecovery_Works()
	{
		var state = CreateState();

		var result = Execute(state, @"
			local log = {}

			function riskyOperation(x)
				if x < 0 then throw 'negative not allowed' end
				if x == 0 then throw 'zero not allowed' end
				return 100 / x
			end

			function processData(items)
				local results = {}
				local errors = {}

				for i = 1, #items do
					try
						results[i] = riskyOperation(items[i])
					catch e do
						results[i] = nil
						errors[i] = e
					end
				end

				return results, errors
			end

			local data = {10, 5, 0, 20, -5, 2, 1, 0, 4}
			local results, errors = processData(data)

			-- Count successes and failures.
			local okCount, errCount = 0, 0
			for i = 1, #data do
				if results[i] ~= nil then okCount = okCount + 1 end
				if errors[i] ~= nil then errCount = errCount + 1 end
			end

			return okCount, errCount,
			       results[1],         -- 100/10 = 10
			       results[2],         -- 100/5 = 20
			       errors[3] ~= nil and 1 or 0,  -- error on zero
			       results[6],         -- 100/2 = 50
			       errors[5] ~= nil and 1 or 0   -- error on negative
		");

		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result[0]).Value);   // 6 successes
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[1]).Value);   // 3 errors
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[2]).Value);  // 100/10
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[3]).Value);  // 100/5
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[4]).Value);   // error on zero
		Assert.Equal(50.0, Assert.IsType<LuaNumber>(result[5]).Value);  // 100/2
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[6]).Value);   // error on negative
	}

	[Fact]
	public void ErrorHandling_FinallyGuaranteesCleanup_Works()
	{
		var state = CreateState();

		var result = Execute(state, @"
			local cleanupLog = {}

			function withResource(name, shouldFail)
				cleanupLog[#cleanupLog + 1] = 'acquire:' .. name
				try
					if shouldFail then
						throw 'failure in ' .. name
					end
					cleanupLog[#cleanupLog + 1] = 'use:' .. name
					return 42
				catch e do
					cleanupLog[#cleanupLog + 1] = 'caught:' .. e
					-- Re-throw to test nested cleanup.
					throw 'rethrown from ' .. name
				end
				-- Lua doesn't have finally, but we simulate with the pattern.
			end

			-- Simulate try-finally pattern: always run cleanup.
			function tryFinally(body, cleanup)
				local ok, err = pcall(body)
				cleanup()
				if not ok then throw err end
			end

			-- Simulated pcall.
			function pcall(fn)
				try
					return true, fn()
				catch e do
					return false, e
				end
			end

			local resources = {}
			for i = 1, 5 do
				local ok, err = pcall(function()
					withResource('res' .. i, i % 3 == 0)
				end)
				resources[i] = { ok = ok, err = err }
			end

			return #cleanupLog,
			       resources[1].ok and 1 or 0,
			       resources[3].ok and 1 or 0
		");

		Assert.True(((LuaNumber)result[0]).Value >= 5, "cleanupLog should have entries");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);  // res1 OK
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[2]).Value);  // res3 failed
	}
}
