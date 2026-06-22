namespace AsyncLua.Values
{
    /// <summary>
    /// Enumerates all Lua metamethod events. These correspond to the special keys
    /// in a metatable that define operator overloading and type behaviour.
    /// </summary>
    public enum LuaMetatableEvent
    {
        // Arithmetic

        /// <summary>The addition (<c>+</c>) metamethod: <c>__add</c>.</summary>
        Add,

        /// <summary>The subtraction (<c>-</c>) metamethod: <c>__sub</c>.</summary>
        Sub,

        /// <summary>The multiplication (<c>*</c>) metamethod: <c>__mul</c>.</summary>
        Mul,

        /// <summary>The division (<c>/</c>) metamethod: <c>__div</c>.</summary>
        Div,

        /// <summary>The modulo (<c>%</c>) metamethod: <c>__mod</c>.</summary>
        Mod,

        /// <summary>The exponentiation (<c>^</c>) metamethod: <c>__pow</c>.</summary>
        Pow,

        /// <summary>The negation (unary <c>-</c>) metamethod: <c>__unm</c>.</summary>
        Unm,

        /// <summary>The floor division (<c>//</c>) metamethod: <c>__idiv</c>.</summary>
        IDiv,

        // Bitwise

        /// <summary>The bitwise AND (<c>&amp;</c>) metamethod: <c>__band</c>.</summary>
        BAnd,

        /// <summary>The bitwise OR (<c>|</c>) metamethod: <c>__bor</c>.</summary>
        BOr,

        /// <summary>The bitwise XOR (<c>~</c>) metamethod: <c>__bxor</c>.</summary>
        BXor,

        /// <summary>The bitwise NOT (unary <c>~</c>) metamethod: <c>__bnot</c>.</summary>
        BNot,

        /// <summary>The left shift (<c>&lt;&lt;</c>) metamethod: <c>__shl</c>.</summary>
        ShL,

        /// <summary>The right shift (<c>&gt;&gt;</c>) metamethod: <c>__shr</c>.</summary>
        ShR,

        // Relational / Misc

        /// <summary>The concatenation (<c>..</c>) metamethod: <c>__concat</c>.</summary>
        Concat,

        /// <summary>The length (<c>#</c>) metamethod: <c>__len</c>.</summary>
        Len,

        /// <summary>The equality (<c>==</c>) metamethod: <c>__eq</c>.</summary>
        Eq,

        /// <summary>The less-than (<c>&lt;</c>) metamethod: <c>__lt</c>.</summary>
        Lt,

        /// <summary>The less-than-or-equal (<c>&lt;=</c>) metamethod: <c>__le</c>.</summary>
        Le,

        // ── Table / Object access ────────────────────

        /// <summary>The indexing (<c>t[k]</c>) metamethod: <c>__index</c>.</summary>
        Index,

        /// <summary>The new-index (<c>t[k] = v</c>) metamethod: <c>__newindex</c>.</summary>
        NewIndex,

        // Callable

        /// <summary>The call (<c>f(...)</c>) metamethod: <c>__call</c>.</summary>
        Call,

        // Introspection

        /// <summary>The <c>tostring()</c> metamethod: <c>__tostring</c>.</summary>
        ToString,

        /// <summary>The garbage-collector finalizer metamethod: <c>__gc</c>.</summary>
        GC,

        /// <summary>The weak-reference mode metamethod: <c>__mode</c>.</summary>
        Mode,

        /// <summary>The metatable protection metamethod: <c>__metatable</c>.</summary>
        MetaTable,

        /// <summary>The name metamethod (for error messages and introspection): <c>__name</c>.</summary>
        Name,

        /// <summary>The <c>pairs()</c> iterator metamethod: <c>__pairs</c>.</summary>
        Pairs,

        /// <summary>The to-be-closed variable metamethod: <c>__close</c>.</summary>
        Close,
    }
}
