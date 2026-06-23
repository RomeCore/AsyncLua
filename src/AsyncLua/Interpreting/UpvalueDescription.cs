namespace AsyncLua.Interpreting
{
	/// <summary>
	/// Describes an upvalue that must be captured when creating a closure.
	/// Maps a local variable in the enclosing function to an upvalue slot.
	/// </summary>
	public readonly struct UpvalueDescription
	{
		/// <summary>
		/// Gets the register index in the enclosing frame that should be captured.
		/// </summary>
		public byte RegisterIndex { get; }

		/// <summary>
		/// Gets whether the upvalue is local to the immediately enclosing function
		/// (<see langword="true"/>) or comes from an outer scope (<see langword="false"/>).
		/// </summary>
		public bool IsLocal { get; }

		/// <summary>
		/// Initialises a new instance of the <see cref="UpvalueDescription"/> struct.
		/// </summary>
		/// <param name="registerIndex">The register index to capture.</param>
		/// <param name="isLocal">
		/// Whether the upvalue is in the immediately enclosing frame.
		/// </param>
		public UpvalueDescription(byte registerIndex, bool isLocal = true)
		{
			RegisterIndex = registerIndex;
			IsLocal = isLocal;
		}
	}
}
