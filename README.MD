# ILCall library for .NET

This is cross-runtime native/managed function calling library.

## Usage

There are multiple structs:
`FuncCdecl, FuncStdCall, FuncManaged, FuncStatic, FuncNative (winapi), FuncThisCall`

Those callers are generic, you can create them from the function pointer by calling `Func*.FromPointer()`

### FuncUnity/FuncUnityStatic/FuncIL2CPP/FuncIL2CPP Types

Those types are used for unity calls, it works in the cross-runtime mode, both for Mono and for IL2CPP direct calling. 
It's possible to call functions in Unity by just using `FuncManaged/FuncStatic` but this won't use an ability to call function pointer directly, instead of that IL2CPP will generate thin wrapper which returns real function pointer which then called with __cdecl convention.
To override this behavior you can use `FuncUnity` caller which is created by `FuncUnity.FromPointer(IntPtr funcPtr, bool isIL2CPP)`, and if you know for sure that you are running in IL2CPP context you can then cast it to direct unchecked IL2CPP call to squeeze some extra performance `FuncUnity.AsIL2CPP()`.

### Functions

There's basic set of reinterpretting function pointer to the different calling conventions like `AsCdecl(), AsThisCall(), etc`.

Real calling functions are separated into three groups:

1. Returning by ref `ref TReturn Ref<(TThis), (T1..T4)>(args)`
2. Returning nothing `void Ref<(TThis), (T1..T4)>(args)`
2. Returning specific type `[Type=byte/int/long/etc] TypeName<(TThis), (T1..T4)>(args)`

The purpose of the third group is to omit generic arguments explicit declaration, like, cmon, you don't want to specify all the argument types, right?

### Side notes
For IL2CPP it's not possible to obtain function pointer with `MethodInfo.MethodHandle.GetFunctionPointer()`, it want you to use Marshalling to obtain an already known pointer! And you can avoid that actually, just take: 

`*(void*)MethodInfo.MethodHandle.Value` -- this is extactly where function pointer is stored, so now you are easy to create `FuncIL2CPP.FrontPointer()` or `FuncUnity.FromPointer(funcPtr, true)`.

### Return types precautions
1. Returning reference-type values like string/object/arrays/class-objects etc working perfectly, you can even return their pointer from unmanaged land freely.
2. Returning structs by `ref TStruct` works perfectly, even from native land.
3. Struct-returning calls might have some issues, especially when structure is larger than IntPtr.Size.

## TODO:
1. Make automatic cross-runtime function pointer resolving (kinda easy, actually). I already have working code in another library.
2. Make some function pointer wrappers and casting, so MethodInfo could be directly called via extension methods? Seems like a good solution.
3. Make untyped return with byte[]/Span<> for special cases. Not sure yet would it be usable or not.

