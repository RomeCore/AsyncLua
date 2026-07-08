using System;
using System.Collections.Generic;
using System.Text;

namespace AsyncLua.Tests
{
	public class Utils
	{
		/// <summary>
		/// Determines if the current environment is a Continuous Integration (CI) server.
		/// Used to avoid time-dependent tests (GitHub using potatoes lol).
		/// </summary>
		/// <returns><see langword="true"/> if the current environment is a CI server; otherwise, <see langword="false"/>.</returns>
		public static bool IsRunningCI()
		{
			return Environment.GetEnvironmentVariable("CI") == "true";
		}
	}
}
