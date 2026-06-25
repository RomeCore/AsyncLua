using System.Collections.Generic;

namespace AsyncLua.Parsing
{
	public abstract class ASTNode
	{
		public CodePositionalInfo Position { get; set; }
	}
}
