namespace AsyncLua.Compiling
{
	public class CompilerSettings
	{
		/// <summary>
		/// Whether local variables should be used by default (if no keyword provided). If false, global variables will be used instead.
		/// </summary>
		public bool IsLocalByDefault { get; set; } = false;

		public CompilerSettings Clone()
		{
			return new CompilerSettings
			{
				IsLocalByDefault = this.IsLocalByDefault
			};
		}
	}
}
