using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace AsyncLua.Tests.Integration
{
	public class CodeExamplesTests(ITestOutputHelper output)
	{
		[Fact]
		public async Task ReadmeExample()
		{
			var state = new LuaState()
				.LoadDefaultLibraries();

			var prints = new List<string>();
			state.Print = (message) => prints.Add(message);

			// Synchronous execution
			state.Execute("print('Hello from Lua!')");

			// Asynchronous execution with async/await
			await state.ExecuteAsync(@"
				async function fetchData()
					-- Simulate async I/O
					await task.delay(100)
					return 'data received'
				end

				local result = await fetchData()
				print(result)
			");

			// Critical sections and try-catch with throw
			var compiled = state.Compile(@"
				local mutex = {}
	
				async function doWork1()
					lock mutex do
						try
							await task.delay(200)
							throw 'some error occured'
						catch ex do
							return ex
						end
					end
				end
	
				async function doWork2()
					lock mutex do
						await task.delay(150)
						return 'data successfully received'
					end
				end

				async function doWork3()
					await task.delay(250)
					return 'another data successfully received'
				end

				local t1, t2, t3 = doWork1(), doWork2(), doWork3()
				local r1, r2, r3 = await t1, await t2, await t3
				print(r1, r2, r3)
			");

			var sw = new Stopwatch();
			sw.Start();

			await compiled.ExecuteAsync();

			sw.Stop();
			var elapsed = sw.ElapsedMilliseconds;

			Assert.Contains("Hello from Lua!", prints);
			Assert.Contains("data received", prints);
			Assert.Contains("some error occured\tdata successfully received\tanother data successfully received", prints);
			output.WriteLine($"Elapsed time: {elapsed} ms for executing critical sections and try-catch with throw.");
			Assert.True(elapsed >= 350);

			// CI environments can be slower
			if (Environment.GetEnvironmentVariable("CI") != "true")
				Assert.True(elapsed < 600);
		}
	}
}
