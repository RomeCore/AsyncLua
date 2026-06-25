using System;
using AsyncLua;

var state = new LuaState();

void Run(string title, string code)
{
    Console.Write($"{title}... ");
    try { var r = state.Execute(code); Console.Write("OK: "); for (int i = 0; i < r.Count; i++) Console.Write($"[{i}]={r[i]} "); Console.WriteLine(); }
    catch (Exception ex) { Console.WriteLine($"FAIL: {ex.Message}"); }
}

Run("For 1..5 + inner 1..5", @"
    local out = {}
    for i = 1, 5 do
        out[i] = {}
        for j = 1, 5 do
            out[i][j] = i * 10 + j
        end
    end
    return out[1][1], out[1][5], out[5][1], out[5][5]
");

Run("For 1..10 with goto skip when i==j", @"
    local matches = {}
    for i = 1, 10 do
        for j = 1, 10 do
            if i == j then goto skip_j end
            if i + j == 15 then
                matches[#matches + 1] = i
                matches[#matches + 1] = j
                break
            end
            ::skip_j::
        end
    end
    return #matches, matches[1], matches[2], matches[3], matches[4]
");
