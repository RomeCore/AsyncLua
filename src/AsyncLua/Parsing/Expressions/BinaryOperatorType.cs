namespace AsyncLua.Parsing.Expressions
{
    /// <summary>
    /// Enumerates the available binary operators in Lua.
    /// </summary>
    public enum BinaryOperatorType
    {
        /// <summary>Arithmetic addition (<c>+</c>).</summary>
        Add,

        /// <summary>Arithmetic subtraction (<c>-</c>).</summary>
        Substract,

        /// <summary>Arithmetic multiplication (<c>*</c>).</summary>
        Multiply,

        /// <summary>Arithmetic division (<c>/</c>).</summary>
        Divide,

        /// <summary>Integer division (<c>//</c>).</summary>
        IntegerDivide,

        /// <summary>Exponentiation (<c>^</c>).</summary>
        Exponentiate,

        /// <summary>Modulus (<c>%</c>).</summary>
        Modulus,

        /// <summary>Equality comparison (<c>==</c>).</summary>
        Equals,

        /// <summary>Inequality comparison (<c>~=</c>).</summary>
        NotEquals,

        /// <summary>Less-than comparison (<c>&lt;</c>).</summary>
        LessThan,

        /// <summary>Less-than-or-equal comparison (<c>&lt;=</c>).</summary>
        LessThanEqual,

        /// <summary>Logical AND (<c>and</c>).</summary>
        LogicalAnd,

        /// <summary>Logical OR (<c>or</c>).</summary>
        LogicalOr,

        /// <summary>String concatenation (<c>..</c>).</summary>
        Concatenate,
    }
}
