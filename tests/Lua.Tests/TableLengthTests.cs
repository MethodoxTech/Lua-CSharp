using System.Diagnostics;
using Lua.Standard;

namespace Lua.Tests;

public class TableLengthTests
{
    static bool IsBorder(LuaTable table, int n)
    {
        return (n == 0 || table[n].Type is not LuaValueType.Nil)
            && table[n + 1].Type is LuaValueType.Nil;
    }

    static async Task<LuaValue[]> RunAsync(string source)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return await state.DoStringAsync(source);
    }

    [Test]
    public void Test_ArrayLength_WithoutHoles()
    {
        for (var n = 0; n <= 100; n++)
        {
            var table = new LuaTable();
            for (var i = 1; i <= n; i++)
            {
                table[i] = i;
            }

            Assert.That(table.ArrayLength, Is.EqualTo(n), $"n = {n}");
        }
    }

    [Test]
    public void Test_ArrayLength_WithHoles_IsABorder()
    {
        var random = new Random(1234);
        for (var trial = 0; trial < 2000; trial++)
        {
            var size = random.Next(0, 70);
            var table = new LuaTable();
            for (var i = 1; i <= size; i++)
            {
                table[i] = i;
            }

            var holes = random.Next(0, 6);
            for (var h = 0; h < holes && size > 0; h++)
            {
                table[random.Next(1, size + 1)] = LuaValue.Nil;
            }

            var n = table.ArrayLength;
            Assert.That(IsBorder(table, n), Is.True, $"trial {trial}: {n} is not a border");
        }
    }

    [Test]
    public void Test_ArrayLength_FirstSlotNil()
    {
        var table = new LuaTable();
        table[2] = 2;
        table[3] = 3;
        Assert.That(table.ArrayLength, Is.EqualTo(0));
    }

    [Test]
    public void Test_Insert_MovesOnlyUpToTheBorder()
    {
        // { 1, nil, 3 }: the border the search finds is 1, so inserting at 1 moves t[1] alone,
        // as Lua 5.2's table.insert does, and leaves t[3] where it was.
        var table = new LuaTable();
        table[1] = 1;
        table[3] = 3;
        Assert.That(table.ArrayLength, Is.EqualTo(1));

        table.Insert(1, 0);
        Assert.That(table[1], Is.EqualTo(new LuaValue(0)));
        Assert.That(table[2], Is.EqualTo(new LuaValue(1)));
        Assert.That(table[3], Is.EqualTo(new LuaValue(3)));
        Assert.That(IsBorder(table, table.ArrayLength), Is.True);
        Assert.Throws<IndexOutOfRangeException>(() => table.Insert(table.ArrayLength + 2, 9));
    }

    [Test]
    public void Test_Insert_IntoAFullArrayKeepsKeysPulledFromTheHashPart()
    {
        var table = new LuaTable(0, 8);
        table[4097] = "x"; // too far past the empty array part, so it lands in the hash part
        for (var i = 1; i <= 4096; i++)
        {
            table[i] = i;
        }

        Assert.That(table.GetArraySpan().Length, Is.EqualTo(4096));
        table.Insert(1, 0);

        Assert.That(table[1], Is.EqualTo(new LuaValue(0)));
        Assert.That(table[2], Is.EqualTo(new LuaValue(1)));
        Assert.That(table[4097], Is.EqualTo(new LuaValue(4096)));
        Assert.That(table[4098], Is.EqualTo(new LuaValue("x")));
        Assert.That(table.ArrayLength, Is.EqualTo(4098));
    }

    [Test]
    public void Test_RemoveAt_ClearsTheVacatedSlot()
    {
        var table = new LuaTable();
        for (var i = 1; i <= 5; i++)
        {
            table[i] = i;
        }

        Assert.That(table.RemoveAt(2), Is.EqualTo(new LuaValue(2)));
        Assert.That(table[2], Is.EqualTo(new LuaValue(3)));
        Assert.That(table[4], Is.EqualTo(new LuaValue(5)));
        Assert.That(table[5], Is.EqualTo(LuaValue.Nil));
        Assert.That(table.ArrayLength, Is.EqualTo(4));

        Assert.That(table.RemoveAt(4), Is.EqualTo(new LuaValue(5)));
        Assert.That(table.ArrayLength, Is.EqualTo(3));
    }

    [Test]
    public async Task Test_Length_AfterAppendsRemovesAndInserts()
    {
        var result = await RunAsync(
            @"
local t = {}
local lengths = {}
for i = 1, 10 do t[#t + 1] = i end
lengths[#lengths + 1] = #t
t[#t] = nil
t[#t] = nil
lengths[#lengths + 1] = #t
table.insert(t, 1, 'a')
table.insert(t, 5, 'b')
table.insert(t, 'c')
lengths[#lengths + 1] = #t
table.remove(t, 1)
table.remove(t)
lengths[#lengths + 1] = #t
return table.concat(lengths, ','), table.concat(t, ',')
"
        );

        Assert.That(result[0], Is.EqualTo(new LuaValue("10,8,11,9")));
        Assert.That(result[1], Is.EqualTo(new LuaValue("1,2,3,b,4,5,6,7,8")));
    }

    [Test]
    public async Task Test_InsertAndRemove_AtPositions()
    {
        var result = await RunAsync(
            @"
local t = {}
for i = 1, 5 do table.insert(t, i) end
table.insert(t, 1, 0)
table.insert(t, 4, 'x')
table.insert(t, #t + 1, 'end')
local a = table.concat(t, ',')
local r1, r2, r3 = table.remove(t), table.remove(t, 1), table.remove(t, 3)
local b = table.concat(t, ',')
local ok = pcall(table.insert, t, #t + 2, 'y')
return a, r1, r2, r3, b, #t, table.remove({}), ok
"
        );

        Assert.That(result[0], Is.EqualTo(new LuaValue("0,1,2,x,3,4,5,end")));
        Assert.That(result[1], Is.EqualTo(new LuaValue("end")));
        Assert.That(result[2], Is.EqualTo(new LuaValue(0)));
        Assert.That(result[3], Is.EqualTo(new LuaValue("x")));
        Assert.That(result[4], Is.EqualTo(new LuaValue("1,2,3,4,5")));
        Assert.That(result[5], Is.EqualTo(new LuaValue(5)));
        Assert.That(result[6], Is.EqualTo(LuaValue.Nil));
        Assert.That(result[7], Is.EqualTo(new LuaValue(false)));
    }

    // Scaling, not a stopwatch threshold: the same loop at 8x the size should take about 8x as
    // long. A length that scans from the first slot, or a shift over the whole capacity, makes
    // these loops quadratic and the ratio about 64x. Arms alternate and the median is compared,
    // so a busy machine slows both sides alike.
    const int Small = 20_000;
    const int Large = 160_000;
    const double MaxRatio = 24;

    static async Task<double> ScalingRatioAsync(string body)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        var function = (await state.DoStringAsync($"return function(n) {body} end"))[0];

        async Task<double> TimeAsync(int n)
        {
            var stopwatch = Stopwatch.StartNew();
            await state.CallAsync(function, [n]);
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        await TimeAsync(Small); // warm-up
        var small = new List<double>();
        var large = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            small.Add(await TimeAsync(Small));
            large.Add(await TimeAsync(Large));
        }

        small.Sort();
        large.Sort();
        return large[1] / Math.Max(small[1], 0.01);
    }

    [Test]
    public async Task Test_Scaling_LengthAppend()
    {
        var ratio = await ScalingRatioAsync("local t = {} for i = 1, n do t[#t + 1] = i end");
        Assert.That(ratio, Is.LessThan(MaxRatio), "t[#t + 1] = v grows faster than linearly");
    }

    [Test]
    public async Task Test_Scaling_TableInsert()
    {
        var ratio = await ScalingRatioAsync("local t = {} for i = 1, n do table.insert(t, i) end");
        Assert.That(ratio, Is.LessThan(MaxRatio), "table.insert grows faster than linearly");
    }

    [Test]
    public async Task Test_Scaling_TableRemove()
    {
        var ratio = await ScalingRatioAsync(
            "local t = {} for i = 1, n do t[i] = i end for i = 1, n do table.remove(t) end"
        );
        Assert.That(ratio, Is.LessThan(MaxRatio), "table.remove grows faster than linearly");
    }
}
