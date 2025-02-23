using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace IL2CPPCallGenerator
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
            var typeBuilder = _module.DefineType($"Func{definition.Name}",
                TypeAttributes.Public 
                | TypeAttributes.Abstract 
                | TypeAttributes.Sealed);

            if (definition.HasThis)
            {
                BuildManagedHelper(typeBuilder);
            }
            
            return typeBuilder;
        }
        
        private static void BuildManagedHelper(TypeBuilder typeBuilder)
        {
            const int maxNumArgs = 6;
            for (int i = 0; i <= maxNumArgs; i++)
            {
                EmitManagedCalilMethod(typeBuilder, null, i);
            }

            for (int i = 0; i <= maxNumArgs; i++)
            {
                EmitManagedCalilMethod(typeBuilder, typeof(void), i);
            }
        }
        
       private static void EmitManagedCalilMethod(TypeBuilder type, Type returnType, int numArgs)
        {
            string methodName;
            bool hasReturn = returnType != typeof(void);

            if (returnType == null)
                methodName = "Generic";
            else
                methodName = "Void";

            var callMethod = type.DefineMethod(methodName, MethodAttributes.Public | MethodAttributes.Static);

            List<string> paramNames = new List<string>();

            if (hasReturn)
                paramNames.Add("TReturn");

            var argsStart = paramNames.Count;
            
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
                returnTypeOfCall = genericParameters[0];
                returnTypeOfFunc = genericParameters[0];
            }

            var genParamTypes = genericParameters.Select(x => (Type)x).ToArray();
            if (hasReturn)
            {
                genParamTypes[0] = returnTypeOfFunc;
            }

            var allArguments = genParamTypes.Length > 0 ? genParamTypes.Skip(argsStart).ToArray() : Type.EmptyTypes;
            allArguments = allArguments
                .Append(typeof(void*))
                .ToArray();

            callMethod.SetReturnType(returnTypeOfFunc);
            callMethod.SetParameters(allArguments);
            callMethod.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);

            for (int i = 0; i < allArguments.Length; i++)
            {
                if (i == 0)
                {
                    callMethod.DefineParameter(1, ParameterAttributes.None, "thisType");
                }
                else
                {
                    if (i == allArguments.Length - 1)
                    {
                        callMethod.DefineParameter(i + 1, ParameterAttributes.None, "methodPtr");
                    }
                    else
                    {
                        callMethod.DefineParameter(i + 1, ParameterAttributes.None, $"arg{i}");
                    }
                }
            }

            var generator = callMethod.GetILGenerator();

            // Load all arguments
            for (int argId = 0; argId < allArguments.Length; argId++)
            {
                Ldarg(argId);
            }

            var calliArguments = allArguments.ToList();
            calliArguments.RemoveAt(calliArguments.Count - 1);
            /* Manage instance function call */
            calliArguments.RemoveAt(0);
            generator.EmitCalli(OpCodes.Calli, CallingConventions.Standard | CallingConventions.HasThis, returnTypeOfCall, calliArguments.ToArray(), null);
            generator.Emit(OpCodes.Ret);

            return;

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
            var an = new AssemblyName("IL2CPPCall");
            _dynamicAssembly = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave);
            _module = _dynamicAssembly.DefineDynamicModule("IL2CPPCall.dll", true);
            an.Version = new Version(1, 0, 0);
            
            var il2Cpp = EmitHelper(new EmitHelperDefinition
            {
                HasThis = true,
                Name = "IL2CPP"
            });

            // var il2CppStatic = EmitHelper(new EmitHelperDefinition
            // {
            //     HasThis = false,
            //     Name = "IL2CPPStatic"
            // });

            var allTypes = new[]
            {
                il2Cpp
            };
            
            foreach (var typeBuilder in allTypes)
                typeBuilder.CreateType();

            _dynamicAssembly.Save("IL2CPPCall.dll");
        }
    }
}