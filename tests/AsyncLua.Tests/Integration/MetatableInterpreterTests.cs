using AsyncLua.Compiling;
using AsyncLua.Interpreting;
using AsyncLua.Parsing;
using AsyncLua.Values;

namespace AsyncLua.Tests.Integration;

/// <summary>
/// Tests for metatable support in Default (Relaxed) and Aggressive modes.
/// </summary>
public class MetatableInterpreterTests
{
	/// <summary>
	/// Creates a LuaState with standard library helpers (setmetatable, getmetatable, type, rawget, rawset).
	/// </summary>
	private static LuaState CreateState()
	{
		var state = new LuaState();

		// setmetatable(value, mt) → value
		state.SetGlobal("setmetatable", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 1)
					return new LuaTuple(new LuaString("setmetatable: expected at least 1 argument"));
				var target = args[0];
				if (args.Length < 2 || args[1] is LuaNil)
					target.Metatable = null;
				else if (args[1] is LuaTable mtTable)
					target.Metatable = LuaMetatable.FromTable(mtTable);
				return new LuaTuple(target);
			}, "setmetatable"));

		// getmetatable(value) → metatable as table, or nil
		state.SetGlobal("getmetatable", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0)
					return new LuaTuple(LuaNil.Instance);
				var mt = args[0].Metatable;
				if (mt == null)
					return new LuaTuple(LuaNil.Instance);
				// Convert LuaMetatable back to a LuaTable for the caller.
				var table = new LuaTable();
				foreach (var kvp in mt)
				{
					var name = LuaMetatable.GetEventName(kvp.Key);
					table.Set(new LuaString(name), kvp.Value);
				}
				return new LuaTuple(table);
			}, "getmetatable"));

		// type(value) → string
		state.SetGlobal("type", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0)
					return new LuaTuple(new LuaString("nil"));
				return new LuaTuple(new LuaString(args[0].TypeName));
			}, "type"));

		// tostring(value) → string
		state.SetGlobal("tostring", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0)
					return new LuaTuple(new LuaString("nil"));
				return new LuaTuple(new LuaString(args[0].ToString()));
			}, "tostring"));

		// rawget(table, key) → value
		state.SetGlobal("rawget", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 2 || args[0] is not LuaTable table)
					return new LuaTuple(LuaNil.Instance);
				return new LuaTuple(table.Get(args[1]));
			}, "rawget"));

		// rawset(table, key, value) → table
		state.SetGlobal("rawset", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 3 || args[0] is not LuaTable table)
					return new LuaTuple(LuaNil.Instance);
				table.Set(args[1], args[2]);
				return new LuaTuple(table);
			}, "rawset"));

		// Helper: make_metatable(table) — sets a C#-backed __len that always returns 42
		state.SetGlobal("make_callback_mt", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 1 || args[0] is not LuaTable target)
					return new LuaTuple(LuaNil.Instance);
				var mt = new LuaMetatable();
				mt.Set(LuaMetatableEvent.Len, new LuaCallbackFunction(
					(ctx2, args2) => new LuaTuple(new LuaNumber(42)), "cb_len"));
				target.Metatable = mt;
				return new LuaTuple(target);
			}, "make_callback_mt"));

		return state;
	}


	private static LuaCallingContext CreateContext(MetatableMode mode = MetatableMode.Default)
	{
		var state = CreateState();
		return state.CreateContext(settings: new InterpreterSettings { MetatableMode = mode });
	}

	private static LuaTuple CompileAndExecute(string code, MetatableMode mode = MetatableMode.Default)
	{
		var parser = new AsyncLuaParser();
		var block = parser.Parse(code);
		var prototype = AsyncLuaCompiler.Compile(block, sourceName: "test");
		var context = CreateContext(mode);
		return AsyncLuaInterpreter.Call(prototype, context);
	}

	// ═══════════════════════════════════════════════════════════════
	// Default mode tests (standard Lua behaviour)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Default_Add_MetatableOnTable()
	{
		// __add on tables should work in Default mode.
		var result = CompileAndExecute(@"
			local t1 = { value = 10 }
			local t2 = { value = 20 }
			local mt = {
				__add = function(a, b)
					return { value = a.value + b.value }
				end
			}
			setmetatable(t1, mt)
			setmetatable(t2, mt)
			local t3 = t1 + t2
			return t3.value
		");
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Default_Add_NumberWithoutMetatable_Works()
	{
		// Standard Lua: numbers have intrinsic arithmetic, no metatable needed.
		var result = CompileAndExecute("return 10 + 20");
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Default_Index_MetatableOnTable()
	{
		// __index on tables for missing keys.
		var result = CompileAndExecute(@"
			local t = {}
			local mt = {
				__index = function(tbl, key)
					return 'fallback:' .. key
				end
			}
			setmetatable(t, mt)
			return t.missing_key
		");
		Assert.Equal("fallback:missing_key", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Default_Index_MetatableTable()
	{
		// __index as a table.
		var result = CompileAndExecute(@"
			local t = {}
			local fallback = { x = 42, y = 99 }
			local mt = { __index = fallback }
			setmetatable(t, mt)
			return t.x, t.y, t.z
		");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(99.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(LuaNil.Instance, result[2]);
	}

	[Fact]
	public void Default_NewIndex_MetatableOnTable()
	{
		// __newindex for new keys.
		var result = CompileAndExecute(@"
			local t = {}
			local stored = {}
			local mt = {
				__newindex = function(tbl, key, value)
					stored[key] = value
				end
			}
			setmetatable(t, mt)
			t.new_key = 'hello'
			return rawget(t, 'new_key'), stored.new_key
		");
		Assert.Equal(LuaNil.Instance, result[0]); // not stored in original table
		Assert.Equal("hello", Assert.IsType<LuaString>(result[1]).Value); // stored in fallback
	}

	[Fact]
	public void Default_NewIndex_ExistingKey_DoesNotTrigger()
	{
		// __newindex should NOT be called for existing keys in Default mode.
		var result = CompileAndExecute(@"
			local t = { existing = 1 }
			local mt = {
				__newindex = function(tbl, key, value)
					error('should not be called')
				end
			}
			setmetatable(t, mt)
			t.existing = 42
			return t.existing
		");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Default_Call_MetatableOnTable()
	{
		// __call on table.
		var result = CompileAndExecute(@"
			local t = {}
			local mt = {
				__call = function(tbl, a, b)
					return a + b
				end
			}
			setmetatable(t, mt)
			return t(10, 20)
		");
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Default_Len_MetatableOnTable()
	{
		// __len on table.
		var result = CompileAndExecute(@"
			local t = {}
			local lenFunc = function(tbl)
				return 42
			end
			local mt = { __len = lenFunc }
			setmetatable(t, mt)
			return #t
		");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Default_Eq_SameMetamethod_Works()
	{
		// __eq: both operands must share the same metamethod.
		var result = CompileAndExecute(@"
			local t1 = { id = 1 }
			local t2 = { id = 1 }
			local mt = {
				__eq = function(a, b)
					return a.id == b.id
				end
			}
			setmetatable(t1, mt)
			setmetatable(t2, mt)
			return t1 == t2, t1 ~= t2
		");
		Assert.Equal(LuaBoolean.True, result[0]);
		Assert.Equal(LuaBoolean.False, result[1]);
	}

	[Fact]
	public void Default_Eq_DifferentMetamethod_FallbackToReference()
	{
		// __eq: different metamethods — should fall back to reference equality.
		var result = CompileAndExecute(@"
			local t1 = {}
			local t2 = {}
			local mt1 = { __eq = function(a, b) return true end }
			local mt2 = { __eq = function(a, b) return true end }
			setmetatable(t1, mt1)
			setmetatable(t2, mt2)
			return t1 == t2
		");
		// Different metatables, so __eq is NOT called; reference equality → false.
		Assert.Equal(LuaBoolean.False, result.First);
	}

	[Fact]
	public void Default_Lt_MetatableOnTable()
	{
		// __lt on tables.
		var result = CompileAndExecute(@"
			local t1 = { value = 10 }
			local t2 = { value = 20 }
			local mt = {
				__lt = function(a, b) return a.value < b.value end
			}
			setmetatable(t1, mt)
			setmetatable(t2, mt)
			return t1 < t2, t2 < t1, t1 > t2
		");
		Assert.Equal(LuaBoolean.True, result[0]);  // t1 < t2 → true
		Assert.Equal(LuaBoolean.False, result[1]); // t2 < t1 → false
		Assert.Equal(LuaBoolean.False, result[2]); // t1 > t2 → false (via __lt swap)
	}

	[Fact]
	public void Default_Le_MetatableOnTable()
	{
		// __le on tables.
		var result = CompileAndExecute(@"
			local t1 = { value = 10 }
			local t2 = { value = 10 }
			local mt = {
				__le = function(a, b) return a.value <= b.value end
			}
			setmetatable(t1, mt)
			setmetatable(t2, mt)
			return t1 <= t2, t1 >= t2, t2 < t1
		");
		Assert.Equal(LuaBoolean.True, result[0]);  // t1 <= t2 → true
		Assert.Equal(LuaBoolean.True, result[1]);  // t1 >= t2 → true (via __le swap)
		Assert.Equal(LuaBoolean.False, result[2]); // t2 < t1 → false
	}

	[Fact]
	public void Default_Concat_MetatableOnTable()
	{
		// __concat on tables.
		var result = CompileAndExecute(@"
			local t1 = { text = 'hello' }
			local t2 = { text = ' world' }
			local mt = {
				__concat = function(a, b) return { text = a.text .. b.text } end
			}
			setmetatable(t1, mt)
			setmetatable(t2, mt)
			local t3 = t1 .. t2
			return t3.text
		");
		Assert.Equal("hello world", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Default_Unm_MetatableOnTable()
	{
		// __unm (unary minus) on table.
		var result = CompileAndExecute(@"
			local t = { value = 42 }
			local mt = {
				__unm = function(a) return { value = -a.value } end
			}
			setmetatable(t, mt)
			local t2 = -t
			return t2.value
		");
		Assert.Equal(-42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Default_Len_Number_Throws()
	{
		// # on a number should throw (not a table/string).
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			CompileAndExecute("return #42"));
		Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Default_Index_OnString_Throws()
	{
		// Indexing a string should throw in Default mode (strings are not tables).
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			CompileAndExecute("return 'hello'[1]"));
	}

	[Fact]
	public void Default_BitwiseOnTable()
	{
		// __band on tables.
		var result = CompileAndExecute(@"
			local t1 = { v = 0xFF }
			local t2 = { v = 0x0F }
			local mt = {
				__band = function(a, b) return { v = a.v & b.v } end
			}
			setmetatable(t1, mt)
			setmetatable(t2, mt)
			local t3 = t1 & t2
			return t3.v
		");
		Assert.Equal(0x0F, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// C# callback metamethod tests (to isolate LuaNativeFunction issues)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Callback_Len_Works()
	{
		// Uses make_callback_mt which sets a C#-backed __len.
		var result = CompileAndExecute(@"
			local t = {}
			make_callback_mt(t)
			return #t
		");
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Callback_Add_Works()
	{
		// Register a C# callback for __add on a table.
		var state = CreateState();
		state.SetGlobal("setup_add_mt", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length < 1 || args[0] is not LuaTable target)
					return new LuaTuple(LuaNil.Instance);
				var mt = new LuaMetatable();
				mt.Set(LuaMetatableEvent.Add, new LuaCallbackFunction(
					(ctx2, args2) =>
					{
						var v1 = (args2[0] as LuaTable)?.Get(new LuaString("v")) is LuaNumber ln1 ? ln1.Value : 0.0;
						var v2 = (args2[1] as LuaTable)?.Get(new LuaString("v")) is LuaNumber ln2 ? ln2.Value : 0.0;
						var resultTable = new LuaTable();
						resultTable.Set(new LuaString("v"), new LuaNumber(v1 + v2));
						return new LuaTuple(resultTable);
					},
					"cb_add"));
				target.Metatable = mt;
				return new LuaTuple(target);
			}, "setup_add_mt"));

		var context = state.CreateContext(settings: new InterpreterSettings { MetatableMode = MetatableMode.Default });

		var parser = new AsyncLuaParser();
		var block = parser.Parse(@"
			local t1 = { v = 10 }
			local t2 = { v = 20 }
			setup_add_mt(t1)
			setup_add_mt(t2)
			local t3 = t1 + t2
			return t3.v
		");
		var prototype = AsyncLuaCompiler.Compile(block, sourceName: "test");
		var result = AsyncLuaInterpreter.Call(prototype, context);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result.First).Value);
	}


	// ═══════════════════════════════════════════════════════════════
	// Aggressive mode tests
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Aggressive_Add_MetatableOnNumber()
	{
		// In Aggressive mode, __add on numbers should work via metatable.
		var result = CompileAndExecute(@"
			local mt = {
				__add = function(a, b)
					return a + b + 100  -- custom behaviour
				end
			}
			local num = 10
			setmetatable(num, mt)
			return num + 20
		", MetatableMode.Aggressive);
		// With metatable on number 0, __add should be called.
		Assert.Equal(130.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Aggressive_Index_OnString()
	{
		// In Aggressive mode, strings can have __index.
		var result = CompileAndExecute(@"
			local proto = { length = function(s) return #s end }
			local mt = { __index = proto }
			local s = 'hello'
			setmetatable(s, mt)
			return s.length
		", MetatableMode.Aggressive);
		// __index on string should return proto.length (a function).
		Assert.IsAssignableFrom<LuaFunction>(result.First);
	}

	[Fact]
	public void Aggressive_Call_OnString()
	{
		// In Aggressive mode, __call on strings.
		var result = CompileAndExecute(@"
			local mt = {
				__call = function(s, a, b) return a + b + #s end
			}
			setmetatable('test', mt)
			return ('test')(10, 20)
		", MetatableMode.Aggressive);
		// "test" has length 4, so 10 + 20 + 4 = 34
		Assert.Equal(34.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Aggressive_Len_OnString()
	{
		// In Aggressive mode, __len on strings can override.
		var result = CompileAndExecute(@"
			local mt = {
				__len = function(s) return 99 end
			}
			local s = 'hello'
			setmetatable(s, mt)
			return #s
		", MetatableMode.Aggressive);
		// Should use __len from metatable, not actual string length.
		Assert.Equal(99.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Aggressive_Eq_DifferentTypes()
	{
		// In Aggressive mode, __eq can be used even for different types.
		var result = CompileAndExecute(@"
			local mt_string = {
				__eq = function(a, b) return tostring(a) == tostring(b) end
			}
			setmetatable('42', mt_string)
			-- Compare string '42' with number 42 with Aggressive metatable.
			local s = '42'
			local n = 42
			-- In Aggressive mode, the string's __eq is called.
			return s == n
		", MetatableMode.Aggressive);
		// '42' == 42 via __eq → true
		Assert.Equal(LuaBoolean.True, result.First);
	}

	[Fact]
	public void Aggressive_Eq_DifferentTypes_False()
	{
		var result = CompileAndExecute(@"
			local mt_string = {
				__eq = function(a, b)
					if type(a) == type(b) then return a == b
					else return tostring(a) == tostring(b) end
				end
			}
			setmetatable('hello', mt_string)
			return 'hello' == 'world', 'hello' == 42
		", MetatableMode.Aggressive);
		Assert.Equal(LuaBoolean.False, result[0]);
		Assert.Equal(LuaBoolean.False, result[1]); // 'hello' != '42'
	}

	[Fact]
	public void Aggressive_NewIndex_OnString()
	{
		// In Aggressive mode, __newindex can be set on strings.
		var result = CompileAndExecute(@"
			local storage = {}
			local mt = {
				__newindex = function(tbl, key, value)
					storage[key] = value
				end
			}
			local s = 'some string'
			setmetatable(s, mt)
			s.fake_key = 42
			return storage.fake_key
		", MetatableMode.Aggressive);
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Aggressive_Concat_OnNumbers()
	{
		// In Aggressive mode, __concat on numbers.
		var result = CompileAndExecute(@"
			local mt = {
				__concat = function(a, b) return (a * 10) + b end
			}
			local n = 1
			setmetatable(n, mt)
			return n .. 2
		", MetatableMode.Aggressive);
		// 1*10 + 2 = 12
		Assert.Equal(12.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Aggressive_DefaultMode_Fallthrough()
	{
		// If Aggressive mode is set but no metamethod exists, should fall back to normal behaviour.
		var result = CompileAndExecute(@"
			return 10 + 20
		", MetatableMode.Aggressive);
		// No metatables involved, should work normally.
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Aggregate_Unm_OnNumber()
	{
		// In Aggressive mode, __unm on numbers.
		var result = CompileAndExecute(@"
			local mt = {
				__unm = function(a) return -(a + 1) end
			}
			local n = 5
			setmetatable(n, mt)
			return -n
		", MetatableMode.Aggressive);
		// -(5 + 1) = -6
		Assert.Equal(-6.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	// ═══════════════════════════════════════════════════════════════
	// Advanced metatable tests
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void Advanced_MetatableChain_IndexFallback()
	{
		// __index with a table: accessing a missing key on t1
		// should look up in the __index table.
		var result = CompileAndExecute(@"
			local fallback = { y = 42, z = 999 }
			local t = { x = 10 }
			setmetatable(t, { __index = fallback })
			return t.x, t.y, t.z, t.w
		");
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);   // own key
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result[1]).Value);   // from fallback
		Assert.Equal(999.0, Assert.IsType<LuaNumber>(result[2]).Value);  // from fallback
		Assert.Equal(LuaNil.Instance, result[3]);                         // not found
	}

	[Fact]
	public void Advanced_NewIndex_With_Index_Combined()
	{
		// __index provides defaults, __newindex captures writes to a shadow table.
		var result = CompileAndExecute(@"
			local defaults = { hp = 100, mp = 50 }
			local storage = {}
			local mt = {
				__index = defaults,
				__newindex = function(t, k, v) storage[k] = v end
			}
			local player = {}
			setmetatable(player, mt)
			-- Read defaults.
			local a = player.hp
			-- Write triggers __newindex.
			player.hp = 200
			player.name = 'mage'
			return a, player.hp, player.name, storage.hp, storage.name
		");
		Assert.Equal(100.0, Assert.IsType<LuaNumber>(result[0]).Value);  // a = defaults.hp captured before write
		Assert.Equal(100.0, Assert.IsType<LuaNumber>(result[1]).Value);  // player.hp → __index returns defaults.hp (unchanged)
		Assert.Equal(LuaNil.Instance, result[2]);                        // player.name → not in defaults, returns nil
		Assert.Equal(200.0, Assert.IsType<LuaNumber>(result[3]).Value);  // storage.hp captured by __newindex
		Assert.Equal("mage", Assert.IsType<LuaString>(result[4]).Value); // storage.name captured by __newindex
	}

	[Fact]
	public void Advanced_Tostring_Metamethod()
	{
		// __tostring should be called by the tostring() function.
		var state = CreateState();
		state.SetGlobal("tostring", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0) return new LuaTuple(new LuaString("nil"));
				var v = args[0];
				var mm = v.Metatable;
				if (mm != null && mm.HasEvent(LuaMetatableEvent.ToString))
				{
					var handler = mm.Get(LuaMetatableEvent.ToString);
					if (handler is LuaFunction func)
						return func.Invoke(ctx, new[] { v });
				}
				return new LuaTuple(new LuaString(v.ToString()));
			}, "tostring"));

		var context = state.CreateContext();
		var parser = new AsyncLuaParser();
		var block = parser.Parse(@"
			local mt = { __tostring = function(t) return 'Point(' .. t.x .. ', ' .. t.y .. ')' end }
			local p = { x = 3, y = 7 }
			setmetatable(p, mt)
			return tostring(p)
		");
		var prototype = AsyncLuaCompiler.Compile(block, sourceName: "test");
		var result = AsyncLuaInterpreter.Call(prototype, context);
		Assert.Equal("Point(3, 7)", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Advanced_Metatable_GetmetatableProtection()
	{
		// __metatable: when set, getmetatable returns this value instead of the real metatable.
		var state = CreateState();
		// Override getmetatable to respect __metatable protection.
		state.SetGlobal("getmetatable", new LuaCallbackFunction(
			(ctx, args) =>
			{
				if (args.Length == 0) return new LuaTuple(LuaNil.Instance);
				var v = args[0];
				var mt = v.Metatable;
				if (mt == null) return new LuaTuple(LuaNil.Instance);
				// __metatable protection.
				if (mt.HasEvent(LuaMetatableEvent.MetaTable))
					return new LuaTuple(mt.Get(LuaMetatableEvent.MetaTable));
				var table = new LuaTable();
				foreach (var kvp in mt)
					table.Set(new LuaString(LuaMetatable.GetEventName(kvp.Key)), kvp.Value);
				return new LuaTuple(table);
			}, "getmetatable"));

		var context = state.CreateContext();
		var parser = new AsyncLuaParser();
		var block = parser.Parse(@"
			local t = {}
			local realMt = { __add = function(a, b) return 100 end, __metatable = 'protected' }
			setmetatable(t, realMt)
			local protected = getmetatable(t)
			return protected
		");
		var prototype = AsyncLuaCompiler.Compile(block, sourceName: "test");
		var result = AsyncLuaInterpreter.Call(prototype, context);
		Assert.Equal("protected", Assert.IsType<LuaString>(result.First).Value); // __metatable guard returned
	}

	[Fact]
	public void Advanced_MetatableReplacement()
	{
		// Changing metatable at runtime should take effect immediately.
		var result = CompileAndExecute(@"
			local t = {}
			local mt1 = { __add = function(a, b) return 10 end }
			local mt2 = { __add = function(a, b) return 20 end }
			setmetatable(t, mt1)
			local v1 = t + 0
			setmetatable(t, mt2)
			local v2 = t + 0
			setmetatable(t, nil)
			-- After removing metatable, addition should throw or fall through.
			-- But table + table without metatable is a runtime error.
			-- Just return the values from the first two.
			return v1, v2
		");
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(20.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Advanced_DefaultMode_Lt_Without_Le()
	{
		// If __lt is present but __le is absent, a <= b should use not (b < a).
		var result = CompileAndExecute(@"
			local t1 = { v = 5 }
			local t2 = { v = 10 }
			local mt = {
				__lt = function(a, b) return a.v < b.v end
			}
			setmetatable(t1, mt)
			setmetatable(t2, mt)
			return t1 < t2, t1 <= t2, t2 <= t1
		");
		Assert.Equal(LuaBoolean.True, result[0]);   // 5 < 10 → true
		Assert.Equal(LuaBoolean.True, result[1]);   // 5 <= 10 → not (10 < 5) = not false = true
		Assert.Equal(LuaBoolean.False, result[2]);  // 10 <= 5 → not (5 < 10) = not true = false
	}

	[Fact]
	public void Advanced_DefaultMode_Le_Without_Lt()
	{
		// If __le is present but __lt is absent, a < b should use not (b <= a).
		var result = CompileAndExecute(@"
			local t1 = { v = 5 }
			local t2 = { v = 10 }
			local mt = {
				__le = function(a, b) return a.v <= b.v end
			}
			setmetatable(t1, mt)
			setmetatable(t2, mt)
			return t1 <= t2, t1 < t2, t2 < t1
		");
		Assert.Equal(LuaBoolean.True, result[0]);   // 5 <= 10 → true
		Assert.Equal(LuaBoolean.True, result[1]);   // 5 < 10 → not (10 <= 5) = not false = true
		Assert.Equal(LuaBoolean.False, result[2]);  // 10 < 5 → not (5 <= 10) = not true = false
	}

	[Fact]
	public void Advanced_Call_MultipleReturns()
	{
		// __call metamethod that returns multiple values.
		var result = CompileAndExecute(@"
			local mt = {
				__call = function(t, a, b, c)
					return a + 1, b + 2, c + 3
				end
			}
			local t = {}
			setmetatable(t, mt)
			return t(10, 20, 30)
		");
		Assert.Equal(11.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(22.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(33.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Advanced_Aggressive_MetatableOnBoolean_Throws()
	{
		// Literal types cannot have metatables
		Assert.Throws<LuaRuntimeException>(() =>
		{
			CompileAndExecute(@"
				local mt = {
					__add = function(a, b) return 'bool_add' end
				}
				setmetatable(true, mt)
				return true + false
			", MetatableMode.Aggressive);
		});
	}

	[Fact]
	public void Advanced_Aggressive_MetatableOnFunction_Call()
	{
		// In Aggressive mode, __call on a function that is NOT callable by default...
		// Actually functions ARE callable. __call on a function would be a fallback
		// if the function itself is called. But since it's already a function,
		// __call is not needed. Let's test __add on a function instead.
		var result = CompileAndExecute(@"
			local mt = {
				__add = function(a, b) return 'func_add' end
			}
			local f = function() return 1 end
			setmetatable(f, mt)
			return f + 0
		", MetatableMode.Aggressive);
		Assert.Equal("func_add", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Advanced_RecursionPrevention_BinaryMetamethod()
	{
		// Verify that a binary metamethod does NOT recurse infinitely
		// when the metamethod itself uses the same operator on the operands.
		var result = CompileAndExecute(@"
			local calls = 0
			local mt = {
				__add = function(a, b)
					calls = calls + 1
					-- This a + b should use RAW number addition, not __add again.
					return a + b + 1
				end
			}
			local n = 10
			setmetatable(n, mt)
			local r = n + 20
			return r, calls
		", MetatableMode.Aggressive);
		// __add(10, 20) → a + b + 1 = 10 + 20 + 1 = 31, calls = 1.
		Assert.Equal(31.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value); // called exactly once
	}

	[Fact]
	public void Advanced_RecursionPrevention_UnaryMetamethod()
	{
		// Verify that a unary metamethod does NOT recurse when it uses
		// the same operator on the operand.
		var result = CompileAndExecute(@"
			local calls = 0
			local mt = {
				__unm = function(a)
					calls = calls + 1
					return -(a + 1)
				end
			}
			local n = 5
			setmetatable(n, mt)
			local r = -n
			return r, calls
		", MetatableMode.Aggressive);
		// __unm(5) → -(5 + 1) = -6, calls = 1.
		Assert.Equal(-6.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Advanced_RecursionPrevention_ComparisonMetamethod()
	{
		// Verify that __eq does NOT recurse when it uses == on the operands.
		var result = CompileAndExecute(@"
			local calls = 0
			local mt = {
				__eq = function(a, b)
					calls = calls + 1
					-- This a == b should use raw comparison, not __eq again.
					return a == b
				end
			}
			local s = 'hello'
			setmetatable(s, mt)
			local r = s == 'hello'
			return r, calls
		", MetatableMode.Aggressive);
		// __eq('hello', 'hello') → a == b (raw) → true, calls = 1.
		Assert.Equal(LuaBoolean.True, result[0]);
		Assert.Equal(1.0, Assert.IsType<LuaNumber>(result[1]).Value);
	}

	[Fact]
	public void Advanced_MultipleMetamethods_SameTable()
	{
		// One metatable with __add, __sub, __mul, __div all at once.
		var result = CompileAndExecute(@"
			local mt = {
				__add = function(a, b) return a.v + b.v end,
				__sub = function(a, b) return a.v - b.v end,
				__mul = function(a, b) return a.v * b.v end,
			}
			local t1 = { v = 10 }
			local t2 = { v = 3 }
			setmetatable(t1, mt)
			setmetatable(t2, mt)
			return t1 + t2, t1 - t2, t1 * t2
		");
		Assert.Equal(13.0, Assert.IsType<LuaNumber>(result[0]).Value);
		Assert.Equal(7.0, Assert.IsType<LuaNumber>(result[1]).Value);
		Assert.Equal(30.0, Assert.IsType<LuaNumber>(result[2]).Value);
	}

	[Fact]
	public void Advanced_Len_Overrides_IntrinsicLength()
	{
		// __len on a table should override the intrinsic # operator,
		// even when the table has array elements.
		var result = CompileAndExecute(@"
			local t = { 10, 20, 30, 40, 50 }  -- array with 5 elements
			local mt = { __len = function(tbl) return 99 end }
			setmetatable(t, mt)
			return #t
		");
		Assert.Equal(99.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Advanced_Aggressive_Boolean_ThrowsOnChange()
	{
		// Literal types cannot have metatables
		Assert.Throws<LuaRuntimeException>(() =>
		{
			var result = CompileAndExecute(@"
				local mtAll = {
					__eq = function(a, b) return tostring(a) == tostring(b) end
				}
				local s = 'true'
				local b = true
				setmetatable(s, mtAll)
				setmetatable(b, mtAll)
				return s == b, s == 'true', b == true
			", MetatableMode.Aggressive);
		});
	}

	[Fact]
	public void Advanced_Concat_MultipleMixed()
	{
		// __concat on a table: concatenating a string with a table that has __concat.
		var result = CompileAndExecute(@"
			local mt = {
				__concat = function(a, b) return tostring(a) .. '_' .. tostring(b) end
			}
			local t = {}
			setmetatable(t, mt)
			return 'hello' .. t
		");
		// Result is 'hello_' + tostring(t) which looks like 'hello_table: 0x...'
		Assert.StartsWith("hello_table:", Assert.IsType<LuaString>(result.First).Value);
	}

	[Fact]
	public void Advanced_Index_Function_Works()
	{
		// __index as a function: the function receives the table and key.
		var result = CompileAndExecute(@"
			local mt = {
				__index = function(t, k)
					return 'key_' .. k
				end
			}
			local t = { x = 10 }
			setmetatable(t, mt)
			return t.x, t.y
		");
		Assert.Equal(10.0, Assert.IsType<LuaNumber>(result[0]).Value);     // own key
		Assert.Equal("key_y", Assert.IsType<LuaString>(result[1]).Value);  // via __index function
	}

	[Fact]
	public void Advanced_ErrorInMetamethod_Propagates()
	{
		// When a metamethod throws, the error should propagate to the caller.
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			CompileAndExecute(@"
				local mt = {
					__add = function(a, b) throw 'boom from __add' end
				}
				local t = {}
				setmetatable(t, mt)
				return t + 0
			"));
		Assert.Contains("boom from __add", ex.Message);
	}

	[Fact]
	public void Advanced_DefaultMode_NoMetamethod_ThrowsOnTable()
	{
		// In Default mode, tables without metamethods should throw on arithmetic.
		var ex = Assert.Throws<LuaRuntimeException>(() =>
			CompileAndExecute("local t = {}; return t + 0"));
		Assert.Contains("non-number", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Advanced_AggressiveMode_NoMetamethod_FallsThrough()
	{
		// In Aggressive mode, if no metamethod is set, normal operation proceeds.
		// Numbers add normally. Tables without __add still fail (because there's
		// no intrinsic table addition).
		var result = CompileAndExecute("return 40 + 2", MetatableMode.Aggressive);
		Assert.Equal(42.0, Assert.IsType<LuaNumber>(result.First).Value);
	}

	[Fact]
	public void Aggressive_Bitwise_OnNumber()
	{
		// In Aggressive mode, bitwise ops on numbers with metatable.
		var result = CompileAndExecute(@"
			local mt = {
				__bor = function(a, b) return a + b end  -- reinterpret OR as ADD
			}
			setmetatable(0, mt)
			return 1 | 2
		", MetatableMode.Aggressive);
		// 1 + 2 = 3
		Assert.Equal(3.0, Assert.IsType<LuaNumber>(result.First).Value);
	}
}
