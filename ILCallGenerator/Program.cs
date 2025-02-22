using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace ConsoleApplication2
{
    internal class Program
    {
        private struct EmitHelperDefinition
        {
            public string Name;

            public bool HasThis;
        }

        private static ModuleBuilder _module;

        private static AssemblyBuilder _dynamicAssembly;

        private static TypeBuilder EmitHelper(EmitHelperDefinition definition)
        {
            /* Emit a static helper class */
            var type = _module.DefineType($"Func{definition.Name}",
                TypeAttributes.Public 
                | TypeAttributes.Abstract 
                | TypeAttributes.Sealed);

            const int maxNumArgs = 6;
            for (int i = 0; i <= maxNumArgs; i++)
            {
                AddMethod(definition, type, null, i);
            }

            for (int i = 0; i <= maxNumArgs; i++)
            {
                AddMethod(definition, type, typeof(void), i);
            }
            
            return type;
        }
        
       private static MethodBuilder AddMethod(EmitHelperDefinition definition, TypeBuilder type,
            Type returnType, int numArgs)
        {
            string methodName;
            bool hasReturn = returnType != typeof(void);
            bool hasGenericReturn = hasReturn && returnType == null;

            if (returnType == null)
                methodName = "Generic";
            else
                methodName = "Void";

            var callMethod = type.DefineMethod(methodName, MethodAttributes.Public | MethodAttributes.Static);

            List<string> paramNames = new List<string>();

            if (hasGenericReturn)
                paramNames.Add("TReturn");

            var argsStart = paramNames.Count;

            if (definition.HasThis)
                paramNames.Add("TThis");

            for (int arg = 0; arg < numArgs; arg++)
                paramNames.Add($"T{arg + 1}");

            var genericParameters = Array.Empty<GenericTypeParameterBuilder>();

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
            allArguments = allArguments
                /* Method Ptr */
                .Append(typeof(IntPtr))
                /* Runtime method handle */
                .Append(typeof(void*)).ToArray();

            callMethod.SetReturnType(returnTypeOfFunc);
            callMethod.SetParameters(allArguments);
            callMethod.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);

            for (int i = 0; i < allArguments.Length; i++)
            {
                var id = i + 1 - (definition.HasThis ? 1 : 0);
                if (i == 0 && definition.HasThis)
                {
                    callMethod.DefineParameter(1, ParameterAttributes.None, "_this");
                }
                else
                {
                    if (i == allArguments.Length - 2)
                    {
                        callMethod.DefineParameter(i + 1, ParameterAttributes.None, "methodPtr");
                    }
                    else if (i == allArguments.Length - 1)
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

            // Load all arguments
            for (int argId = 0; argId < allArguments.Length - 2; argId++)
            {
                Ldarg(argId);
            }
            
            // Load MethodHandle
            Ldarg(allArguments.Length - 1);
            // Load MethodPtr
            Ldarg(allArguments.Length - 2);
            
            var calliArguments = allArguments.ToList();
            calliArguments.RemoveAt(calliArguments.Count - 2);
            generator.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, returnTypeOfCall, calliArguments.ToArray());
            generator.Emit(OpCodes.Ret);
           
            return callMethod;

            void Ldarg(int argId)
            {
                if (argId == 0)
                    generator.Emit(OpCodes.Ldarg_0);
                else if (argId == 1)
                    generator.Emit(OpCodes.Ldarg_1);
                else if (argId == 2)
                    generator.Emit(OpCodes.Ldarg_2);
                else if (argId == 3)
                    generator.Emit(OpCodes.Ldarg_3);
                else
                    generator.Emit(OpCodes.Ldarg_S, (ushort)(argId));
            }
        }

        public static void Main(string[] args)
        {
            var an = new AssemblyName("ILCall");
            _dynamicAssembly = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave);
            _module = _dynamicAssembly.DefineDynamicModule("ILCall.dll", true);
            an.Version = new Version(1, 0, 0);
            
            var il2Cpp = EmitHelper(new EmitHelperDefinition
            {
                HasThis = true,
                Name = "IL2CPP"
            });

            var il2CppStatic = EmitHelper(new EmitHelperDefinition
            {
                HasThis = false,
                Name = "IL2CPPStatic"
            });

            var allTypes = new[]
            {
                il2Cpp, il2CppStatic
            };
            
            foreach (var typeBuilder in allTypes)
                typeBuilder.CreateType();

            _dynamicAssembly.Save("ILCall.dll");
        }
    }
}