using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace ConsoleApplication2
{
    internal class Program
    {
        public static T Get<T>()
            where T : unmanaged
        {
            return default;
        }

        public struct ModDefinition
        {
            public string name;
            public object callingConvention;
            public bool hasThis;
            public bool isUnity;
            public bool il2cpp;
            public bool IsManaged => callingConvention is CallingConventions;
        }

        private static Type[] funcPtrType = new Type[] { typeof(IntPtr) };
        private static ModuleBuilder module;
        private static AssemblyBuilder dynamicAssembly;
        private static TypeBuilder refReturnType;

        private static Type[] returnTypes = new Type[]
        {
            typeof(byte),
            typeof(ushort),
            typeof(short),
            typeof(uint),
            typeof(int),
            typeof(long),
            typeof(ulong),
            typeof(string),
            typeof(object),
            typeof(IntPtr),
            typeof(float),
            typeof(double),
        };

        public static OpCode[] returnTypeOpcodes = new OpCode[]
        {
            OpCodes.Conv_U1,
            OpCodes.Conv_U2,
            OpCodes.Conv_I2,
            OpCodes.Conv_U4,
            OpCodes.Conv_I4,
            OpCodes.Conv_I8,
            OpCodes.Conv_U8,
            OpCodes.Conv_U,
            OpCodes.Conv_U,
            OpCodes.Conv_I,
            OpCodes.Conv_R4,
            OpCodes.Conv_R8,
        };
        
        public static ModStruct AddModType(ModDefinition definition)
        {
            var type = module.DefineType($"Func{definition.name}",
                TypeAttributes.Public | TypeAttributes.SequentialLayout);
            type.SetParent(typeof(ValueType));

            FieldBuilder funcPtrField = null;
            if (definition.isUnity && !definition.il2cpp)
                funcPtrField = type.DefineField("methodPtr", typeof(ulong), FieldAttributes.Public);
            else
                funcPtrField = type.DefineField("methodPtr", typeof(IntPtr), FieldAttributes.Public);

            const int maxNumArgs = 6;
            for (int i = 0; i <= maxNumArgs; i++)
            {
                var method = AddMethod(funcPtrField, definition, type, null, i);
                AddReferenceStubMethod(definition, type, method, i);

                for (int typeId = 0; typeId < returnTypes.Length; typeId++)
                    AddTypeForward(returnTypes[typeId], definition, type, method, i);
            }

            for (int i = 0; i <= maxNumArgs; i++)
            {
                AddMethod(funcPtrField, definition, type, typeof(void), i);
            }

            if (!definition.isUnity || definition.il2cpp)
            {
                var initMethod = type.DefineMethod("FromPointer", MethodAttributes.Public | MethodAttributes.Static);
                initMethod.SetReturnType(type);
                initMethod.SetParameters(typeof(IntPtr));
                initMethod.SetImplementationFlags(initMethod.GetMethodImplementationFlags() |
                                                  MethodImplAttributes.AggressiveInlining);

                var il = initMethod.GetILGenerator();
                il.DeclareLocal(type);
                il.Emit(OpCodes.Ldloca_S, 0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stfld, funcPtrField);
                il.Emit(OpCodes.Ldloc_S, 0);
                il.Emit(OpCodes.Ret);

                var initMethod2 = type.DefineMethod("FromPointer", MethodAttributes.Public | MethodAttributes.Static);
                initMethod2.SetReturnType(type);
                initMethod2.SetParameters(typeof(void*));
                initMethod2.SetImplementationFlags(initMethod2.GetMethodImplementationFlags() |
                                                   MethodImplAttributes.AggressiveInlining);

                il = initMethod2.GetILGenerator();
                il.DeclareLocal(type);
                il.Emit(OpCodes.Ldloca_S, 0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stfld, funcPtrField);
                il.Emit(OpCodes.Ldloc_S, 0);
                il.Emit(OpCodes.Ret);
            }
            else if (definition.isUnity && !definition.il2cpp)
            {
                var initMethod = type.DefineMethod("FromPointer", MethodAttributes.Public | MethodAttributes.Static);
                initMethod.SetReturnType(type);

                initMethod.SetParameters(typeof(IntPtr), typeof(bool));
                initMethod.DefineParameter(1, ParameterAttributes.None, "funcPtr");
                var p = initMethod.DefineParameter(2, ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                    "isIL2CPPDirect");
                p.SetConstant(false);

                initMethod.SetImplementationFlags(initMethod.GetMethodImplementationFlags() |
                                                  MethodImplAttributes.AggressiveInlining);

                var il = initMethod.GetILGenerator();
                il.DeclareLocal(typeof(ulong));
                var label = il.DefineLabel();
                var skip4byteTransform = il.DefineLabel();

                il.DeclareLocal(type);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Conv_U8);
                il.Emit(OpCodes.Stloc_0);

                il.Emit(OpCodes.Sizeof, typeof(void*));
                il.Emit(OpCodes.Ldc_I4_4);
                il.Emit(OpCodes.Bne_Un_S, skip4byteTransform);

                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Ldc_I4_M1);
                il.Emit(OpCodes.Conv_U8);
                il.Emit(OpCodes.And);

                il.Emit(OpCodes.Stloc_0);

                il.MarkLabel(skip4byteTransform);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Brtrue_S, label);

                il.Emit(OpCodes.Ldloca_S, 1);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Stfld, funcPtrField);
                il.Emit(OpCodes.Ldloc_S, 1);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(label);

                il.Emit(OpCodes.Ldloca_S, 1);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Ldc_I8, -9223372036854775808L);
                //il.Emit(OpCodes.Conv_U8);
                il.Emit(OpCodes.Or);
                il.Emit(OpCodes.Stfld, funcPtrField);
                il.Emit(OpCodes.Ldloc_S, 1);
                il.Emit(OpCodes.Ret);
            }

            //type.CreateType();
            return new ModStruct()
            {
                type = type,
                definition = definition,
                methodPtr = funcPtrField
            };
        }

        private static void AddTypeForward(Type returnType, ModDefinition definition, TypeBuilder type,
            MethodBuilder method,
            int numArgs)
        {
            var strName = GetMethodNameForType(returnType);
            var newMethod = type.DefineMethod(strName, MethodAttributes.Public);
            
            List<string> paramNames = new List<string>();

            //paramNames.Add("TReturn");

            if (definition.hasThis)
                paramNames.Add("TThis");

            for (int arg = 0; arg < numArgs; arg++)
                paramNames.Add($"T{arg + 1}");

            var genericParameters = new GenericTypeParameterBuilder[0];

            if (paramNames.Count > 0)
                genericParameters = newMethod.DefineGenericParameters(paramNames.ToArray());

            var myArgs = genericParameters.Cast<Type>();
            if (definition.isUnity)
                myArgs = myArgs.Append(typeof(void*));

            newMethod.SetParameters(myArgs.ToArray());
            newMethod.SetReturnType(returnType);

            if (definition.isUnity)
            {
                var p = newMethod.DefineParameter(myArgs.Count(),
                    ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                    "runtimeMethodHandle");

                p.SetConstant(null);
            }

            var callArgs = (new Type[] { returnType }).Concat(genericParameters.Select(x => (Type)x));
            if (definition.isUnity)
                callArgs = callArgs.Append(typeof(void*));

            var il = newMethod.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);

            var na = paramNames.Count;
            if (definition.isUnity)
                na++;

            for (int i = 0; i < na; i++)
                il.Emit(OpCodes.Ldarg_S, i + 1);

            il.Emit(OpCodes.Call, method.MakeGenericMethod(callArgs.ToArray()));
            //il.Emit(OpCodes.Conv_U);
            il.Emit(OpCodes.Ret);
        }

        private static void AddReferenceStubMethod(ModDefinition definition, TypeBuilder type, MethodBuilder method,
            int numArgs)
        {
            var newMethod = type.DefineMethod($"Ref", MethodAttributes.Public);
            
            List<string> paramNames = new List<string>();

            paramNames.Add("TReturn");

            if (definition.hasThis)
                paramNames.Add("TThis");

            for (int arg = 0; arg < numArgs; arg++)
                paramNames.Add($"T{arg + 1}");

            var genericParameters = new GenericTypeParameterBuilder[0];

            if (paramNames.Count > 0)
                genericParameters = newMethod.DefineGenericParameters(paramNames.ToArray());

            genericParameters[0].SetGenericParameterAttributes(
                genericParameters[0].GenericParameterAttributes
                | GenericParameterAttributes.NotNullableValueTypeConstraint
            );

            var myArgs = genericParameters.Skip(1).Cast<Type>();
            if (definition.isUnity)
                myArgs = myArgs.Append(typeof(void*));

            newMethod.SetParameters(myArgs.ToArray());
            newMethod.SetReturnType(genericParameters[0].MakeByRefType());

            if (definition.isUnity)
            {
                var p = newMethod.DefineParameter(myArgs.Count(),
                    ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                    "runtimeMethodHandle");

                p.SetConstant(null);
            }

            var callArgs = (new Type[] { typeof(IntPtr) }).Concat(genericParameters.Skip(1).Select(x => (Type)x));
            if (definition.isUnity)
                callArgs = callArgs.Append(typeof(void*));

            var il = newMethod.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);

            var na = paramNames.Count - 1;
            if (definition.isUnity)
                na++;

            for (int i = 0; i < na; i++)
                il.Emit(OpCodes.Ldarg_S, i + 1);

            il.Emit(OpCodes.Call, method.MakeGenericMethod(callArgs.ToArray()));
            //il.Emit(OpCodes.Conv_U);
            il.Emit(OpCodes.Ret);
        }

        private static unsafe MethodBuilder AddMethod(FieldBuilder field, ModDefinition definition, TypeBuilder type,
            Type returnType, int numArgs)
        {
            string methodName = null;
            bool hasReturn = returnType != typeof(void);
            bool hasGenericReturn = hasReturn && returnType == null;

            if (returnType == null)
                methodName = "Generic";
            else if (returnType == typeof(void))
                methodName = "Void";
            else
                methodName = GetMethodNameForType(returnType); // + "Call";

            var callMethod = type.DefineMethod(methodName, MethodAttributes.Public);

            List<string> paramNames = new List<string>();

            if (hasGenericReturn)
                paramNames.Add("TReturn");

            var argsStart = paramNames.Count;

            if (definition.hasThis)
                paramNames.Add("TThis");

            for (int arg = 0; arg < numArgs; arg++)
                paramNames.Add($"T{arg + 1}");

            var genericParameters = new GenericTypeParameterBuilder[0];

            if (paramNames.Count > 0)
                genericParameters = callMethod.DefineGenericParameters(paramNames.ToArray());

            Type returnTypeOfCall = typeof(void);
            Type returnTypeOfFunc = typeof(void);

            if (hasReturn)
            {
                if (hasGenericReturn)
                {
                    returnTypeOfCall = genericParameters[0];
                    returnTypeOfFunc = genericParameters[0];
                }
                else
                {
                    returnTypeOfCall = returnType;
                    returnTypeOfFunc = returnType;
                }
            }

            var genParamTypes = genericParameters.Select(x => (Type)x).ToArray();
            if (hasGenericReturn)
                genParamTypes[0] = returnTypeOfFunc;

            var allArguments = genParamTypes.Length > 0 ? genParamTypes.Skip(argsStart).ToArray() : Type.EmptyTypes;
            if (definition.isUnity)
                allArguments = allArguments.Append(typeof(void*)).ToArray();

            callMethod.SetReturnType(returnTypeOfFunc);
            callMethod.SetParameters(allArguments);
            callMethod.SetImplementationFlags(callMethod.GetMethodImplementationFlags() |
                                              MethodImplAttributes.AggressiveInlining);

            for (int i = 0; i < allArguments.Length; i++)
            {
                var id = i + 1 - (definition.hasThis ? 1 : 0);
                if (i == 0 && definition.hasThis)
                {
                    callMethod.DefineParameter(1, ParameterAttributes.None, "_this");
                }
                else
                {
                    if (i == allArguments.Length - 1 && definition.isUnity)
                    {
                        var p = callMethod.DefineParameter(i + 1,
                            ParameterAttributes.None | ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                            "runtimeHandleIL2CPP");
                        p.SetConstant(null);
                    }
                    else
                    {
                        callMethod.DefineParameter(i + 1, ParameterAttributes.None, $"arg{id}");
                    }
                }
            }

            var generator = callMethod.GetILGenerator();

            if (definition.isUnity && !definition.il2cpp)
            {
                generator.DeclareLocal(typeof(ulong));
            }

            // Load this and funcptr field
            if (definition.isUnity && !definition.il2cpp)
            {
                var label1 = generator.DefineLabel();
                generator.Emit(OpCodes.Ldc_I4_1);
                generator.Emit(OpCodes.Conv_I8);
                generator.Emit(OpCodes.Ldc_I4, 63);
                generator.Emit(OpCodes.Shl);
                generator.Emit(OpCodes.Conv_U8);
                generator.Emit(OpCodes.Stloc_0);

                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Ldfld, field);
                generator.Emit(OpCodes.Ldloc_0);
                generator.Emit(OpCodes.And);
                generator.Emit(OpCodes.Brtrue_S, label1);

                // Load all arguments
                for (int argId = 0; argId < allArguments.Length - 1; argId++)
                {
                    if (argId == 0)
                        generator.Emit(OpCodes.Ldarg_1);
                    else if (argId == 1)
                        generator.Emit(OpCodes.Ldarg_2);
                    else if (argId == 2)
                        generator.Emit(OpCodes.Ldarg_3);
                    else
                        generator.Emit(OpCodes.Ldarg_S, (ushort)(argId + 1));
                }

                var managedArgs = allArguments.Take(allArguments.Length - 1).ToArray();

                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Ldfld, field);
                generator.Emit(OpCodes.Conv_U);

                generator.EmitCalli(OpCodes.Calli, CallingConventions.Standard, returnTypeOfFunc, managedArgs, null);
                generator.Emit(OpCodes.Ret);

                generator.MarkLabel(label1);

                // Load all arguments
                for (int argId = 0; argId < allArguments.Length; argId++)
                {
                    if (argId == 0)
                        generator.Emit(OpCodes.Ldarg_1);
                    else if (argId == 1)
                        generator.Emit(OpCodes.Ldarg_2);
                    else if (argId == 2)
                        generator.Emit(OpCodes.Ldarg_3);
                    else
                        generator.Emit(OpCodes.Ldarg_S, (ushort)(argId + 1));
                }

                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Ldfld, field);
                generator.Emit(OpCodes.Ldloc_0);
                generator.Emit(OpCodes.Not);
                generator.Emit(OpCodes.And);
                generator.Emit(OpCodes.Conv_U);

                generator.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, returnTypeOfCall, allArguments);
                generator.Emit(OpCodes.Ret);
            }
            else if (definition.isUnity && definition.il2cpp)
            {
                // Load all arguments
                for (int argId = 0; argId < allArguments.Length; argId++)
                {
                    if (argId == 0)
                        generator.Emit(OpCodes.Ldarg_1);
                    else if (argId == 1)
                        generator.Emit(OpCodes.Ldarg_2);
                    else if (argId == 2)
                        generator.Emit(OpCodes.Ldarg_3);
                    else
                        generator.Emit(OpCodes.Ldarg_S, (ushort)(argId + 1));
                }

                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Ldfld, field);

                generator.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, returnTypeOfCall, allArguments);
                generator.Emit(OpCodes.Ret);
            }
            else
            {
                // Load all arguments
                for (int argId = 0; argId < allArguments.Length; argId++)
                {
                    if (argId == 0)
                        generator.Emit(OpCodes.Ldarg_1);
                    else if (argId == 1)
                        generator.Emit(OpCodes.Ldarg_2);
                    else if (argId == 2)
                        generator.Emit(OpCodes.Ldarg_3);
                    else
                        generator.Emit(OpCodes.Ldarg_S, (ushort)(argId + 1));
                }

                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Ldfld, field);

                if (definition.IsManaged)
                {
                    var cconv = (CallingConventions)definition.callingConvention;
                    generator.EmitCalli(OpCodes.Calli, cconv, returnTypeOfFunc, allArguments, null);
                }
                else
                {
                    var cconv = (CallingConvention)definition.callingConvention;
                    generator.EmitCalli(OpCodes.Calli, cconv, returnTypeOfCall, allArguments);
                }

                generator.Emit(OpCodes.Ret);
            }

            return callMethod;
        }

        private static string GetMethodNameForType(Type returnType)
        {
            if (returnType == typeof(sbyte))
                return "Char";

            if (returnType == typeof(byte))
                return "Byte";

            if (returnType == typeof(short))
                return "Int16";

            if (returnType == typeof(ushort))
                return "UInt16";

            if (returnType == typeof(int))
                return "Int32";

            if (returnType == typeof(uint))
                return "UInt32";

            if (returnType == typeof(long))
                return "Int64";

            if (returnType == typeof(ulong))
                return "UInt64";

            if (returnType == typeof(IntPtr) || returnType == typeof(UIntPtr) || returnType.IsPointer)
                return "IntPtr";

            if (returnType == typeof(string))
                return "String";

            if (returnType == typeof(object))
                return "Object";

            if (returnType == typeof(float))
                return "Float";
            
            if (returnType == typeof(double))
                return "Double";

            throw new KeyNotFoundException($"Can't find method name for type {returnType}");
        }

        public class ModStruct
        {
            public TypeBuilder type;
            public FieldBuilder methodPtr;
            public ModDefinition definition;
        }

        public static void Main(string[] args)
        {
            var an = new AssemblyName("ILCall");
            dynamicAssembly = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave);
            module = dynamicAssembly.DefineDynamicModule("ILCall.dll", true);
            an.Version = new Version(1, 0, 0);

            //GenerateRefReturnType();

            var managed = AddModType(new ModDefinition()
            {
                hasThis = true,
                callingConvention = CallingConventions.Standard,
                name = "Managed"
            });

            var managedStatic = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = CallingConventions.Standard,
                name = "Static"
            });

            var native = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = CallingConvention.Winapi,
                name = "Native"
            });

            var cdecl = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = CallingConvention.Cdecl,
                name = "Cdecl"
            });

            var stdcall = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = CallingConvention.StdCall,
                name = "StdCall"
            });

            var thiscall = AddModType(new ModDefinition()
            {
                hasThis = true,
                callingConvention = CallingConvention.ThisCall,
                name = "ThisCall"
            });

            var unity = AddModType(new ModDefinition()
            {
                hasThis = true,
                callingConvention = CallingConvention.Cdecl,
                isUnity = true,
                name = "Unity"
            });

            var il2cpp = AddModType(new ModDefinition()
            {
                hasThis = true,
                callingConvention = CallingConvention.Cdecl,
                isUnity = true,
                il2cpp = true,
                name = "IL2CPP"
            });

            var il2cppStatic = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = CallingConvention.Cdecl,
                isUnity = true,
                il2cpp = true,
                name = "IL2CPPStatic"
            });

            var unityStatic = AddModType(new ModDefinition()
            {
                hasThis = false,
                callingConvention = CallingConvention.Cdecl,
                isUnity = true,
                name = "UnityStatic"
            });

            var allTypes = new ModStruct[]
            {
                managed, managedStatic, native, unity, unityStatic, cdecl, stdcall, thiscall, il2cpp, il2cppStatic
            };

            GenerateToIL2CPP(unity, il2cpp);
            GenerateToIL2CPP(unityStatic, il2cppStatic);
            GenerateToManaged(unity, il2cpp);
            GenerateToManaged(unityStatic, il2cppStatic);

            foreach (var current in allTypes)
            {
                foreach (var other in allTypes)
                {
                    if (other == current)
                        continue;

                    if (current == unity
                        && other != unityStatic)
                        continue;

                    if (current == unityStatic
                        && other != unity)
                        continue;

                    if (current == il2cpp
                        && other != il2cppStatic)
                        continue;

                    if (current == il2cppStatic
                        && other != il2cpp)
                        continue;

                    //TODO: Add type conversion.
                    var convMethod =
                        current.type.DefineMethod($"As{other.type.Name.Substring(4)}", MethodAttributes.Public);
                    convMethod.SetReturnType(other.type);
                    convMethod.InitLocals = false;

                    var gen = convMethod.GetILGenerator();

                    gen.DeclareLocal(other.type);
                    gen.Emit(OpCodes.Ldloca_S, 0);
                    gen.Emit(OpCodes.Ldarg_0);
                    gen.Emit(OpCodes.Ldfld, current.methodPtr);
                    gen.Emit(OpCodes.Stfld, other.methodPtr);

                    gen.Emit(OpCodes.Ldloc_0, 0);
                    gen.Emit(OpCodes.Ret);
                }
            }

            foreach (var typeBuilder in allTypes)
                typeBuilder.type.CreateType();

            dynamicAssembly.Save("ILCall.dll");
        }

        private static unsafe void GenerateToIL2CPP(ModStruct addToType, ModStruct toType)
        {
            var asil2cpp = addToType.type.DefineMethod("AsIL2CPP", MethodAttributes.Public);
            asil2cpp.SetReturnType(toType.type);
            var g = asil2cpp.GetILGenerator();
            var m1 = g.DeclareLocal(typeof(ulong));
            var m2 = g.DeclareLocal(typeof(IntPtr));
            var newStruct = g.DeclareLocal(toType.type);
            var label = g.DefineLabel();

            g.Emit(OpCodes.Ldc_I4_1);
            g.Emit(OpCodes.Conv_I8);
            g.Emit(OpCodes.Ldc_I4_S, 63);
            g.Emit(OpCodes.Shl);
            g.Emit(OpCodes.Conv_U8);
            g.Emit(OpCodes.Stloc_0);
            g.Emit(OpCodes.Ldloc_0);
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldfld, addToType.methodPtr);
            g.Emit(OpCodes.And);
            g.Emit(OpCodes.Brtrue_S, label);
            g.Emit(OpCodes.Ldstr, "This is not IL2CPP direct call.");
            g.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(new Type[] { typeof(string) }));
            g.Emit(OpCodes.Throw);
            g.MarkLabel(label);
            //g.Emit(OpCodes.Ldloca_S, newStruct);
            //g.Emit(OpCodes.Initobj, toType.type);

            // mask
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldfld, addToType.methodPtr);
            g.Emit(OpCodes.Ldloc_0);
            g.Emit(OpCodes.Not);
            g.Emit(OpCodes.And);
            g.Emit(OpCodes.Conv_I);
            g.Emit(OpCodes.Stloc_1);

            g.Emit(OpCodes.Ldloca_S, newStruct);
            g.Emit(OpCodes.Ldloc_1);
            g.Emit(OpCodes.Stfld, toType.methodPtr);
            g.Emit(OpCodes.Ldloc_S, newStruct);
            g.Emit(OpCodes.Ret);
        }

        private static unsafe void GenerateToManaged(ModStruct addToType, ModStruct toType)
        {
            var asil2cpp = addToType.type.DefineMethod("AsManaged", MethodAttributes.Public);
            asil2cpp.SetReturnType(toType.type);
            var g = asil2cpp.GetILGenerator();
            var m1 = g.DeclareLocal(typeof(ulong));
            var m2 = g.DeclareLocal(typeof(IntPtr));
            var newStruct = g.DeclareLocal(toType.type);
            var label = g.DefineLabel();

            g.Emit(OpCodes.Ldc_I4_1);
            g.Emit(OpCodes.Conv_I8);
            g.Emit(OpCodes.Ldc_I4_S, 63);
            g.Emit(OpCodes.Shl);
            g.Emit(OpCodes.Conv_U8);
            g.Emit(OpCodes.Stloc_0);
            g.Emit(OpCodes.Ldloc_0);
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldfld, addToType.methodPtr);
            g.Emit(OpCodes.And);
            g.Emit(OpCodes.Brfalse, label);
            g.Emit(OpCodes.Ldstr, "This is IL2CPP direct call.");
            g.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(new Type[] { typeof(string) }));
            g.Emit(OpCodes.Throw);
            g.MarkLabel(label);
            //g.Emit(OpCodes.Ldloca_S, newStruct);
            //g.Emit(OpCodes.Initobj, toType.type);

            // mask
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldfld, addToType.methodPtr);
            g.Emit(OpCodes.Ldloc_0);
            g.Emit(OpCodes.Not);
            g.Emit(OpCodes.And);
            g.Emit(OpCodes.Conv_I);
            g.Emit(OpCodes.Stloc_1);

            g.Emit(OpCodes.Ldloca_S, newStruct);
            g.Emit(OpCodes.Ldloc_1);
            g.Emit(OpCodes.Stfld, toType.methodPtr);
            g.Emit(OpCodes.Ldloc_S, newStruct);
            g.Emit(OpCodes.Ret);
        }

        private static void GenerateRefReturnType()
        {
            refReturnType = module.DefineType("RefReturn", TypeAttributes.Public | TypeAttributes.SequentialLayout,
                typeof(ValueType));
            var rfield = refReturnType.DefineField("_ptr", typeof(IntPtr), FieldAttributes.Private);

            var asMethod = refReturnType.DefineMethod("As", MethodAttributes.Public);
            var tret = asMethod.DefineGenericParameters("T");

            asMethod.SetReturnType(tret[0].MakeByRefType());
            var gt = asMethod.GetILGenerator();
            gt.Emit(OpCodes.Ldarg_0);
            gt.Emit(OpCodes.Ldfld, rfield);
            gt.Emit(OpCodes.Ret);
            refReturnType.CreateType();
        }

        private static void GenerateFunctionWithReturnType(TypeBuilder type, Type retType, Type[] argTypes, int numArgs)
        {
        }
    }
}