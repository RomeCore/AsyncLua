using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncLua.Interpreting
{
	public static class LuaMonitor
	{
		private static readonly ConcurrentDictionary<object, SemaphoreSlim> _locks = [];

		public static void Enter(object obj)
		{
			var semaphore = _locks.GetOrAdd(obj, _ => new SemaphoreSlim(1, 1));
			semaphore.Wait();
		}

		public static async Task EnterAsync(object obj)
		{
			var semaphore = _locks.GetOrAdd(obj, _ => new SemaphoreSlim(1, 1));
			await semaphore.WaitAsync();
		}

		public static void Exit(object obj)
		{
			if (_locks.TryGetValue(obj, out var semaphore))
			{
				semaphore.Release();
				if (semaphore.CurrentCount == 1)
					_locks.TryRemove(obj, out _);
			}
		}
	}
}
