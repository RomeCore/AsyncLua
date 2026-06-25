using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// Stress integration tests: deep recursion, large data structures,
/// high iteration counts, edge-case compositions, and robustness checks.
/// </summary>
public class StressIntegrationTests
{
	private static LuaState CreateState()
	{
		var state = new LuaState();
		RegisterLibrary(state);
		return state;
	}

	private static void RegisterLibrary(LuaState state)
	{
		state.Register("print", LuaCallbackFunction.From((LuaValue[] _) => LuaTuple.Empty, "print"));
		state.Register("type", new LuaCallbackFunction(
			(ctx, args) => args.Length == 0
				? new LuaTuple(new LuaString("nil"))
				: new LuaTuple(new LuaString(args[0].TypeName)), "type"));
		state.Register("tostring", new LuaCallbackFunction(
			(ctx, args) => args.Length == 0
				? new LuaTuple(new LuaString("nil"))
				: new LuaTuple(new LuaString(args[0].ToString())), "tostring"));
		state.Register("tonumber", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0 || !args[0].TryToNumber(out var n))
					return new LuaTuple(LuaNil.Instance);
				return new LuaTuple(new LuaNumber(n));
			}, "tonumber"));
		state.Register("assert", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length > 0 && !args[0].ToBoolean())
					throw new LuaRuntimeException(args.Length > 1 ? args[1].ToString() : "assertion failed!");
				return args.Length > 0 ? new LuaTuple(args) : LuaTuple.Empty;
			}, "assert"));
		state.Register("error", new LuaCallbackFunction(
			new LuaCallbackFunction.CallbackDelegate((ctx, args) =>
				throw new LuaRuntimeException(args.Length > 0 ? args[0].ToString() : "error")), "error"));

		var math = new LuaTable();
		state.Globals.Set(new LuaString("math"), math);
		math.Set(new LuaString("sqrt"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(Math.Sqrt(((LuaNumber)args[0]).Value))), "math.sqrt"));
		math.Set(new LuaString("floor"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(Math.Floor(((LuaNumber)args[0]).Value))), "math.floor"));
		math.Set(new LuaString("abs"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(Math.Abs(((LuaNumber)args[0]).Value))), "math.abs"));
		math.Set(new LuaString("max"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(
				Math.Max(((LuaNumber)args[0]).Value, ((LuaNumber)args[1]).Value))), "math.max"));
		math.Set(new LuaString("min"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(
				Math.Min(((LuaNumber)args[0]).Value, ((LuaNumber)args[1]).Value))), "math.min"));

		state.Register("ipairs", new LuaCallbackFunction(
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

		state.Register("pairs", new LuaCallbackFunction(
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

		// string functions
		var strLib = new LuaTable();
		state.Globals.Set(new LuaString("string"), strLib);
			strLib.Set(new LuaString("sub"), new LuaCallbackFunction(
			(ctx, args) =>
			{
				var s = args[0].ToString();
				var start = Math.Max(1, (int)((LuaNumber)args[1]).Value) - 1;
				var endIdx = args.Length > 2 ? (int)((LuaNumber)args[2]).Value : s.Length;
				var count = Math.Min(endIdx, s.Length) - start;
				return new LuaTuple(new LuaString(s.Substring(start, count)));
			}, "string.sub"));
		strLib.Set(new LuaString("len"), new LuaCallbackFunction(
			(ctx, args) => new LuaTuple(new LuaNumber(args[0].ToString().Length)), "string.len"));

		// table.concat
		state.Register("table_concat", new LuaCallbackFunction(
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

		// table.insert
		state.Register("table_insert", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 2 || args[0] is not LuaTable tbl) return LuaTuple.Empty;
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

		// Metatable functions
		state.Register("setmetatable", new LuaCallbackFunction(
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
		state.Register("getmetatable", new LuaCallbackFunction(
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
		state.Register("rawget", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 2 || args[0] is not LuaTable table)
					return new LuaTuple(LuaNil.Instance);
				return new LuaTuple(table.Get(args[1]));
			}, "rawget"));
		state.Register("rawset", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 3 || args[0] is not LuaTable table)
					return new LuaTuple(LuaNil.Instance);
				table.Set(args[1], args[2]);
				return new LuaTuple(table);
			}, "rawset"));
	}

	private static LuaTuple Execute(LuaState state, string code) => state.Execute(code);

	[Fact]
	public void LargeTable_BuildTableWith1000Entries_IteratesCorrectly()
	{
		var state = CreateState();
		var result = Execute(state, @"
			local n = 1000
			local t = {}
			for i = 1, n do t[i] = i * i end
			assert(t[1] == 1); assert(t[500] == 250000); assert(t[1000] == 1000000)
			local sum = 0
			for _, v in ipairs(t) do sum = sum + v end
			local expected = n * (n + 1) * (2 * n + 1) / 6
			assert(sum == expected)
			local sum2 = 0
			for i = 1, #t do sum2 = sum2 + t[i] end
			return #t, sum, sum2
		");
		Assert.Equal(1000.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(333833500.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(333833500.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void LargeTable_StringKeysWith500Entries_Works()
	{
		var state = CreateState();
		var result = Execute(state, @"
			local n = 500; local t = {}
			for i = 1, n do t['key_' .. i] = i * 3 end
			assert(t['key_1'] == 3); assert(t['key_100'] == 300); assert(t['key_500'] == 1500)
			local sum, count = 0, 0
			for k, v in pairs(t) do sum = sum + v; count = count + 1 end
			return count, sum
		");
		Assert.Equal(500.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(375750.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void NestedTable_DeepStructure_AccessWorks()
	{
		var state = CreateState();
		var result = Execute(state, @"
			local function buildDeep(depth, breadth)
				if depth <= 0 then return { value = depth } end
				local node = { value = depth, children = {} }
				for i = 1, breadth do node.children[i] = buildDeep(depth - 1, breadth) end
				return node
			end
			local root = buildDeep(5, 3)
			local leafCount = 0
			local function countLeaves(node)
				if node.children == nil then leafCount = leafCount + 1; return end
				for i = 1, #node.children do countLeaves(node.children[i]) end
			end
			countLeaves(root)
			return leafCount, root.value, root.children[1].value
		");
		Assert.Equal(243.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(4.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Recursion_DeepTailRecursion_Factorial()
	{
		var state = CreateState();
		var result = Execute(state, @"
			function factorialTail(n, acc)
				if n <= 1 then return acc end
				return factorialTail(n - 1, n * acc)
			end
			return factorialTail(5, 1), factorialTail(10, 1), factorialTail(15, 1)
		");
		Assert.Equal(120.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(3628800.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(1307674368000.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Recursion_MutualRecursion_EvenOdd()
	{
		var state = CreateState();
		var result = Execute(state, @"
			local isEven, isOdd
			function isEven(n)
				if n == 0 then return true end
				return isOdd(n - 1)
			end
			function isOdd(n)
				if n == 0 then return false end
				return isEven(n - 1)
			end
			return isEven(10) and 1 or 0,
			       isOdd(10) and 1 or 0,
			       isEven(11) and 1 or 0,
			       isOdd(11) and 1 or 0
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[3]).Value);
	}

	[Fact]
	public void Recursion_AckermannFunction_SmallInputs()
	{
		var state = CreateState();
		var result = Execute(state, @"
			function ack(m, n)
				if m == 0 then return n + 1 end
				if n == 0 then return ack(m - 1, 1) end
				return ack(m - 1, ack(m, n - 1))
			end
			return ack(0, 5), ack(1, 3), ack(2, 2), ack(3, 1)
		");
		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(13.0, Assert.IsType<LuaNumber>(result[3]).Value);
	}

	[Fact]
	public void ControlFlow_NestedLoopsWithBreakAndGoto()
	{
		var state = CreateState();
		var result = Execute(state, @"
			local found = {}
			for i = 1, 10 do
				for j = 1, 10 do
					if i == j then goto continue_outer end
					if i + j == 15 then
						found[#found + 1] = { i = i, j = j }
						break
					end
				end
				::continue_outer::
			end
			local sumI = 0
			for idx = 1, #found do sumI = sumI + found[idx].i end
			return #found, found[1].i, found[1].j, sumI
		");
		Assert.True(((LuaNumber)result[0]).Value >= 2);
		Assert.Equal(8.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void ControlFlow_GotoStateMachine_ParsesAbc()
	{
		var state = CreateState();
		var result = Execute(state, @"
			function parse(input)
				local pos, acc = 1, {}
				::start::
				if pos > string.len(input) then goto done end
				local ch = string.sub(input, pos, pos)
				if ch == 'a' then acc[#acc + 1] = 'got_a'; pos = pos + 1; goto state_b end
				goto error_state
				::state_b::
				if pos > string.len(input) then goto done end
				ch = string.sub(input, pos, pos)
				if ch == 'b' then acc[#acc + 1] = 'got_b'; pos = pos + 1; goto state_c end
				goto error_state
				::state_c::
				if pos > string.len(input) then goto done end
				ch = string.sub(input, pos, pos)
				if ch == 'c' then acc[#acc + 1] = 'got_c'; pos = pos + 1; goto start end
				goto error_state
				::error_state::
				return false, 'unexpected'
				::done::
				return true, acc
			end
			local ok1, acc1 = parse('abcabc')
			local ok2, _ = parse('abcx')
			return ok1 and 1 or 0, #acc1, ok2 and 1 or 0
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(6.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Vararg_WrapperPassesVarargThrough()
	{
		var state = CreateState();
		var result = Execute(state, @"
			function wrap(fn, ...)
				return fn(...)
			end
			function sum3(a, b, c) return a + b + c end
			return wrap(sum3, 10, 20, 30), wrap(sum3, 5, 5, 5)
		");
		Assert.Equal(60.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void MultipleReturn_TableCapture_GetsAll()
	{
		var state = CreateState();
		var result = Execute(state, @"
			function multi() return 1, 2, 3, 4, 5 end
			local a, b, c = multi()
			local t = { multi() }
			return a, b, c, #t, t[1], t[5]
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(2.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[5]).Value);
	}

	[Fact]
	public void EdgeCase_EmptyBlocksAndZeroIterations()
	{
		var state = CreateState();
		var result = Execute(state, @"
			local emptyFn = function() end
			local r1 = emptyFn()
			local c1, c2, c3, c4 = 0, 0, 0, 0
			for i = 10, 1, 1 do c1 = c1 + 1 end
			for i = 1, 10, -1 do c2 = c2 + 1 end
			while false do c3 = c3 + 1 end
			repeat c4 = c4 + 1 until true
			return type(r1), c1, c2, c3, c4
		");
		Assert.Equal("nil", Assert.IsType<LuaString>(result[0]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[3]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[4]).Value);
	}

	[Fact]
	public void EdgeCase_NilAndBooleanIdentity()
	{
		var state = CreateState();
		var result = Execute(state, @"
			return (nil == nil) and 1 or 0,
			       (nil ~= nil) and 1 or 0,
			       (nil == false) and 1 or 0,
			       (true == true) and 1 or 0,
			       (false == false) and 1 or 0,
			       (true == false) and 1 or 0,
			       (0 and true or false) and 1 or 0,
			       ('' and true or false) and 1 or 0
		");
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[0]).Value); Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[1]).Value); Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[3]).Value); Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[4]).Value); Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[5]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[6]).Value); Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[7]).Value);
	}

	[Fact]
	public void EdgeCase_NaN_Behavior()
	{
		var state = CreateState();
		var result = Execute(state, @"
			local a = 0 / 0
			return (a ~= a) and 1 or 0,
			       (a == a) and 1 or 0,
			       (a < 0) and 1 or 0,
			       (a > 0) and 1 or 0
		");
		// NaN == NaN should always be false in Lua.
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[3]).Value);
	}

	[Fact]
	public void EdgeCase_OperatorPrecedence_FullTest()
	{
		var state = CreateState();
		var result = Execute(state, @"
			local r1 = 2 + 3 * 4
			local r2 = 2 ^ 3 ^ 2
			local r3 = not false and true
			local r4 = -2 ^ 2
			local r5 = 10 - 5 - 2
			local r6 = 'a' .. 'b' .. 'c'
			local r7 = 10 // 3
			return r1, r2, r3 and 1 or 0, r4, r5, string.len(r6), r7
		");
		Assert.Equal(14.0, Assert.IsType<LuaNumber>(result[0]).Value); Assert.Equal(512.0, Assert.IsType<LuaNumber>(result[1]).Value); Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(-4.0, Assert.IsType<LuaNumber>(result[3]).Value); Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[4]).Value); Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[5]).Value); Assert.Equal(3.0, Assert.IsType<LuaNumber>(result[6]).Value);
	}

	[Fact]
	public void EdgeCase_BitwiseFullSuite()
	{
		var state = CreateState();
		var result = Execute(state, @"
			return 0xFF & 0x0F,
			       0x0F | 0xF0,
			       0xFF ~ 0x0F,
			       1 << 4,
			       128 >> 3,
			       0xFFFF_FFFF & 0x1234_5678,
			       1 << 70,
			       1 >> 70
		");
		Assert.Equal(15.0, Assert.IsType<LuaNumber>(result[0]).Value); Assert.Equal(255.0, Assert.IsType<LuaNumber>(result[1]).Value); Assert.Equal(240.0, Assert.IsType<LuaNumber>(result[2]).Value);
		Assert.Equal(16.0, Assert.IsType<LuaNumber>(result[3]).Value); Assert.Equal(16.0, Assert.IsType<LuaNumber>(result[4]).Value);
		Assert.Equal(305419896.0, Assert.IsType<LuaNumber>(result[5]).Value);
		Assert.Equal(64.0, Assert.IsType<LuaNumber>(result[6]).Value); Assert.Equal(0.0, Assert.IsType<LuaNumber>(result[7]).Value);
	}

	[Fact]
	public void Composition_MetatablePlusClosures_Memoization()
	{
		var state = CreateState();
		var result = Execute(state, @"
			function memoize(fn)
				local cache = {}
				local mt = { __index = function(tbl, key)
					local result = fn(key); rawset(tbl, key, result); return result
				end }
				setmetatable(cache, mt)
				return cache
			end
			local fibCache = memoize(function(n)
				if n <= 1 then return n end
				return fibCache[n - 1] + fibCache[n - 2]
			end)
			return fibCache[10], fibCache[20], fibCache[30], fibCache[10]
		");
		Assert.Equal(55.0, Assert.IsType<LuaNumber>(result[0]).Value); Assert.Equal(6765.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(832040.0, Assert.IsType<LuaNumber>(result[2]).Value); Assert.Equal(55.0, Assert.IsType<LuaNumber>(result[3]).Value);
	}

	[Fact]
	public void Composition_EventBusWithCallMetamethod()
	{
		var state = CreateState();
		var result = Execute(state, @"
			function createEventBus()
				local bus = {}; local handlers = {}
				local mt = { __call = function(self, event, ...)
					local list = handlers[event]
					if list then for i = 1, #list do list[i](...) end end
				end }
				function bus:on(event, handler)
					if not handlers[event] then handlers[event] = {} end
					handlers[event][#handlers[event] + 1] = handler
				end
				setmetatable(bus, mt); return bus
			end
			local bus = createEventBus(); local results = {}
			bus:on('data', function(x) results[#results + 1] = 'got:' .. x end)
			bus:on('data', function(x) results[#results + 1] = 'also:' .. x end)
			bus:on('error', function(msg) results[#results + 1] = 'err:' .. msg end)
			bus('data', 'hello'); bus('data', 'world'); bus('error', 'boom')
			return #results, results[1], results[#results]
		");
		Assert.Equal(5.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal("got:hello", Assert.IsType<LuaString>(result[1]).Value);
		Assert.Equal("err:boom", Assert.IsType<LuaString>(result[2]).Value);
	}

	[Fact]
	public void Composition_TowerOfHanoi_6Disks()
	{
		var state = CreateState();
		var result = Execute(state, @"
			local moves = {}
			function hanoi(n, from, to, aux)
				if n == 0 then return end
				hanoi(n - 1, from, aux, to)
				moves[#moves + 1] = from .. '->' .. to
				hanoi(n - 1, aux, to, from)
			end
			hanoi(6, 'A', 'C', 'B')
			local expected = (2 ^ 6) - 1
			return #moves, expected, moves[1], moves[expected]
		");
		Assert.Equal(63.0, Assert.IsType<LuaNumber>(result[0]).Value); Assert.Equal(63.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal("A->B", Assert.IsType<LuaString>(result[2]).Value);
		Assert.Equal("B->C", Assert.IsType<LuaString>(result[3]).Value);
	}
}
