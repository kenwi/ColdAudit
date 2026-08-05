// Compiler polyfill for Unity's .NET Standard 2.1 profile (same pattern as IsExternalInit).
// UnmanagedCallersOnlyAttribute shipped in .NET 5; Roslyn and IL2CPP/Mono match it by its
// metadata name (System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute), so a
// source-defined copy enables `&Method` -> `delegate* unmanaged` conversions under Unity.
// Compiled out on real .NET 5+ (e.g. the out-of-Unity test harness) where the BCL owns it.

#if !NET5_0_OR_GREATER
// ReSharper disable once CheckNamespace
namespace System.Runtime.InteropServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class UnmanagedCallersOnlyAttribute : Attribute
    {
        /// <summary>Calling-convention types (e.g. typeof(CallConvCdecl)).</summary>
        public Type[] CallConvs;

        /// <summary>Optional export name (unused by Unity runtimes).</summary>
        public string EntryPoint;
    }
}
#endif
