# IL2CPPCall: ILCall library for Unity.IL2CPP

This is IL2CPP managed function calling library. 

Based on the great idea of [Meetem/ILCall](https://github.com/Meetem/ILCall)

## Verified Version

Unity 2022.3.13

## Prerequisites

Requires two modifications in `Unity.IL2CPP.dll` in `{Your Edior Location}\Editor\Data\il2cpp\build\deploy`.

You can do following steps to make it work.

1. Download [dnspy](https://github.com/dnSpy/dnSpy/releases)
2. Use dnspy to open `Unity.IL2CPP.dll`
3. Find `MethodBodyWriter.ProcessInstruction` and replace the code in block `case Code.Calli` with following.

```c#
case Code.Calli:
{
    MethodBodyWriter.FunctionPointerCallType functionPointerCallType = MethodBodyWriter.FunctionPointerCallType.Native;
    ResolvedCallSiteInfo callSiteInfo = ((CallSiteInfoResolvedInstruction)ins).CallSiteInfo;
    bool flag = this._methodReference.DeclaringType.Name == "FuncIL2CPP";
    if (callSiteInfo.HasThis && !flag)
    {
        this._writer.WriteStatement(Emit.RaiseManagedException("il2cpp_codegen_get_not_supported_exception(\"Calli is not supported on instance methods\")", null));
    }
    string callConvStr = "";
    switch (callSiteInfo.CallingConvention)
    {
    case MethodCallingConvention.C:
        callConvStr = "CDECL";
        break;
    case MethodCallingConvention.StdCall:
        callConvStr = "STDCALL";
        break;
    case MethodCallingConvention.ThisCall:
        callConvStr = "THISCALL";
        break;
    case MethodCallingConvention.FastCall:
        callConvStr = "FASTCALL";
        break;
    default:
        if (!this._context.Global.Parameters.UsingTinyBackend)
        {
            functionPointerCallType = MethodBodyWriter.FunctionPointerCallType.Managed;
        }
        break;
    }
    if (flag)
    {
        functionPointerCallType = MethodBodyWriter.FunctionPointerCallType.Managed;
    }
    string text = this._valueStack.Pop().Expression;
    string text2 = null;
    if (functionPointerCallType != MethodBodyWriter.FunctionPointerCallType.Native)
    {
        text2 = "(const RuntimeMethod*)" + text;
        text = "il2cpp_codegen_get_direct_method_pointer(" + text2 + ")";
    }
    if (flag)
    {
        ResolvedTypeInfo parameterType = this._resolvedMethodContext.Parameters[0].ParameterType;
        this.WriteCallViaMethodPointer(ins, text, text2, MethodBodyWriter.PopItemsFromStack(callSiteInfo.Parameters.Count + 1, this._valueStack), parameterType, functionPointerCallType, callSiteInfo, callConvStr);
        return;
    }
    this.WriteCallViaMethodPointer(ins, text, text2, MethodBodyWriter.PopItemsFromStack(callSiteInfo.Parameters.Count, this._valueStack), null, functionPointerCallType, callSiteInfo, callConvStr);
    return;
}
```

2. Find `MethodBodyWriter.WriteCallViaMethodPointer` and replace the code in block `using (Returnable<StringBuilder> returnable = this._context.Global.Services.Factory.CheckoutStringBuilder())` with following.
3. Save and overwrite dll. (Do not forget to backup the original one!)

```c#
StringBuilder value = returnable.Value;
bool flag = false;
if (callType == MethodBodyWriter.FunctionPointerCallType.Invoker)
{
    funcPointerExp = this._writer.VirtualCallInvokeMethod(methodSig.UnresolvedMethodSignature, this._typeResolver, VirtualMethodCallType.InvokerCall, false, array);
    value.Append(funcPointerExp);
    value.Append("(il2cpp_codegen_get_direct_method_pointer(");
    value.Append(methodInfoExp);
    value.Append("),");
    value.Append(methodInfoExp);
    if (thisType == null)
    {
        value.Append(",NULL");
    }
    flag = true;
}
```

## Usage

Calling IL2CPP generated functions with lower overhead when using reflection.

```c#
public class MyClass
{
    private int addValue = 1;

    public int AddOne(int inputValue)
    {
        return inputValue + addValue;
    }
}
int input = 5;
MyClass target = new MyClass();
var functionPtr = (void*)typeof(MyClass).GetMethod("AddOne").MethodHandle.Value;
var result = FuncIL2CPP.Generic<int, MyClass, Tint1>(target, input, functionPtr);
Debug.Log(result); // Console: 6
```

## Theory

IL support [Calli](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.calli?view=net-9.0) instruction. It can be used to call both managed and unmanaged function. 

We can do following managed call on static method in C# directly.

```c#
public class MyClass
{
    public static int StaticAddOne(int arg1)
    {
        return arg1 + 1;
    }
}
var functionPtr = (delegate* <int, int>)&MyClass.StaticAddOne;
var result = ((delegate* <int, TintR>)functionPtr)(arg1);
```

However, C# does not provide a way to get function pointer from managed instance function, see issues in [Calling instance-based methods with function pointers is not supported
](https://github.com/dotnet/runtime/issues/110061).

```c#
public class MyClass
{
    private int addValue = 1;

    public int AddOne(int inputValue)
    {
        return inputValue + addValue;
    }
}
int input = 5;
MyClass target = new MyClass();
// Not Allowed!
var functionPtr = (delegate* <int, MyClass, int>)&MyClass.AddOne;
```
The original repo [Meetem/ILCall](https://github.com/Meetem/ILCall) uses the umanaged cdecl convention, 
which is a rather clever and hacky way to directly call the functions generated by IL2CPP. 
But it is not supported in [Full Generic Sharing](https://unity.com/cn/blog/engine-platform/il2cpp-full-generic-sharing-in-unity-2022-1-beta) 
generated code since compiler can not know the pointer type.

So `IL2CPPCall` let you can call managed instance function with function pointer in IL2CPP.

However, `Unity.IL2CPP.dll` which is the aot compiler does not support managed instance (with `CallingConventions.HasThis` flag) calli instruction.
So we need to modify the `Unity.IL2CPP.dll` to reach our goal.

The final IL2CPP generated code will like following.

```c++
/* FuncIL2CPP.Generic<TR, TThisType, T1>(TThisType thisType, T1 arg1, void* methodPtr); */
IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR void FuncIL2CPP_Generic_gshared (
    Il2CppFullySharedGenericAny thisType, 
    Il2CppFullySharedGenericAny arg1, 
    void* methodPtr, 
    Il2CppFullySharedGenericAny* il2cppRetVal, 
    const RuntimeMethod* method
) 
{
	il2cpp_rgctx_method_init(method);
	const uint32_t SizeOf_TThis = il2cpp_codegen_sizeof(il2cpp_rgctx_data_no_init(method->rgctx_data, 0));
	const uint32_t SizeOf_T1 = il2cpp_codegen_sizeof(il2cpp_rgctx_data_no_init(method->rgctx_data, 1));
	const uint32_t SizeOf_TReturn = il2cpp_codegen_sizeof(il2cpp_rgctx_data_no_init(method->rgctx_data, 2));
	const Il2CppFullySharedGenericAny L_3 = alloca(SizeOf_TReturn);
	const Il2CppFullySharedGenericAny L_0 = alloca(SizeOf_TThis);
	const Il2CppFullySharedGenericAny L_1 = alloca(SizeOf_T1);
	{
		il2cpp_codegen_memcpy(L_0, (il2cpp_codegen_class_is_value_type(il2cpp_rgctx_data_no_init(method->rgctx_data, 0)) ? ___0_thisType : &___0_thisType), SizeOf_TThis);
		il2cpp_codegen_memcpy(L_1, (il2cpp_codegen_class_is_value_type(il2cpp_rgctx_data_no_init(method->rgctx_data, 1)) ? ___1_arg1 : &___1_arg1), SizeOf_T1);
		void* L_2 = ___2_methodPtr;
		InvokerActionInvoker2< Il2CppFullySharedGenericAny, Il2CppFullySharedGenericAny* >::Invoke(
            il2cpp_codegen_get_direct_method_pointer((const RuntimeMethod*)L_2),
            (const RuntimeMethod*)L_2,
            (il2cpp_codegen_class_is_value_type(il2cpp_rgctx_data_no_init(method->rgctx_data, 0)) ? L_0: *(void**)L_0),
            (il2cpp_codegen_class_is_value_type(il2cpp_rgctx_data_no_init(method->rgctx_data, 1)) ? L_1: *(void**)L_1),
            (Il2CppFullySharedGenericAny*)L_3
        );
		il2cpp_codegen_memcpy(il2cppRetVal, L_3, SizeOf_TReturn);
		return;
	}
}
```