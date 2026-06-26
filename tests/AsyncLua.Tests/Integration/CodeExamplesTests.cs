using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace AsyncLua.Tests.Integration
{
	public class CodeExamplesTests
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
					await delay(100)
					return 'data received'
				end

				local result = await fetchData()
				print(result)
			");

			var sw = new Stopwatch();
			sw.Start();

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

				async function doWork3()
					await delay(350)
					return 'another data successfully received'
				end

				local t1, t2, t3 = doWork1(), doWork2(), doWork3()
				local r1, r2, r3 = await t1, await t2, await t3
				print(r1, r2, r3)
			");

			sw.Stop();
			var elapsed = sw.ElapsedMilliseconds;

			Assert.Contains("Hello from Lua!", prints);
			Assert.Contains("data received", prints);
			Assert.Contains("some error occured\tdata successfully received\tanother data successfully received", prints);
			Assert.True(elapsed > 350);
			Assert.True(elapsed < 650);
		}
	}
}
