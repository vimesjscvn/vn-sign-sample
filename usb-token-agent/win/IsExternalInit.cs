// Polyfill required for C# 9 records and init-only setters on .NET < 5.
// The compiler emits references to this type; it must exist in the assembly.
namespace System.Runtime.CompilerServices
{
    internal sealed class IsExternalInit { }
}
