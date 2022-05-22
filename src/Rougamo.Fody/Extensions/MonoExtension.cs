﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CallSite = Mono.Cecil.CallSite;

namespace Rougamo.Fody
{
    internal static class MonoExtension
    {
        public static bool Is(this TypeReference typeRef, string fullName)
        {
            return typeRef.Resolve()?.FullName == fullName;
        }

        public static bool Is(this CustomAttribute attribute, string fullName)
        {
            return attribute.AttributeType.Is(fullName);
        }

        public static bool Is(this MethodReference methodRef, string fullName)
        {
            return methodRef.FullName == fullName;
        }

        public static bool IsOrDerivesFrom(this TypeReference typeRef, string className)
        {
            return Is(typeRef, className) || DerivesFrom(typeRef, className);
        }

        public static bool Implement(this TypeDefinition typeDef, string @interface)
        {
            do
            {
                if (typeDef.Interfaces.Any(x => x.InterfaceType.FullName == @interface)) return true;
                typeDef = typeDef.BaseType?.Resolve();
            } while (typeDef != null);

            return false;
        }

        public static bool DerivesFrom(this TypeReference typeRef, string baseClass)
        {
            do
            {
                if ((typeRef = typeRef.Resolve()?.BaseType)?.FullName == baseClass) return true;
            } while (typeRef != null);

            return false;
        }

        public static bool DerivesFromAny(this TypeReference typeRef, params string[] baseClasses)
        {
            foreach (var baseClass in baseClasses)
            {
                if (typeRef.DerivesFrom(baseClass)) return true;
            }

            return false;
        }

        public static bool DerivesFrom(this CustomAttribute attribute, string baseClass)
        {
            return attribute.AttributeType.DerivesFrom(baseClass);
        }

        public static bool IsDelegate(this TypeReference typeRef)
        {
            return DerivesFrom(typeRef, Constants.TYPE_MulticastDelegate);
        }

        public static bool IsArray(this TypeReference typeRef, out TypeReference elementType)
        {
            elementType = null;
            if (!typeRef.IsArray)
                return false;

            elementType = ((ArrayType) typeRef).ElementType;
            return true;
        }

        public static bool IsEnum(this TypeReference typeRef, out TypeReference underlyingType)
        {
            var typeDef = typeRef.Resolve();
            if (typeDef != null && typeDef.IsEnum)
            {
                underlyingType = typeDef.Fields.First(f => f.Name == "value__").FieldType;
                return true;
            }

            underlyingType = null;
            return false;
        }

        public static bool IsLdtoken(this Instruction instruction, string @interface, out TypeDefinition typeDef)
        {
            typeDef = null;
            if (instruction.OpCode != OpCodes.Ldtoken) return false;

            typeDef = instruction.Operand as TypeDefinition;
            if (typeDef == null && instruction.Operand is TypeReference typeRef)
            {
                typeDef = typeRef.Resolve();
            }

            if (typeDef == null) return false;
            return typeDef.Implement(@interface);
        }

        public static bool IsStfld(this Instruction instruction, string fieldName, string fieldType)
        {
            if (instruction.OpCode != OpCodes.Stfld) return false;

            var def = instruction.Operand as FieldDefinition;
            if (def == null && instruction.Operand is FieldReference @ref)
            {
                def = @ref.Resolve();
            }

            return def != null && def.Name == fieldName && def.FieldType.Is(fieldType);
        }

        public static OpCode GetStElemCode(this TypeReference typeRef)
        {
            var typeDef = typeRef.Resolve();
            if (typeDef.IsEnum(out TypeReference underlying))
                return underlying.MetadataType.GetStElemCode();
            if (typeRef.IsValueType)
                return typeRef.MetadataType.GetStElemCode();
            return OpCodes.Stelem_Ref;
        }

        public static OpCode GetStElemCode(this MetadataType type)
        {
            switch (type)
            {
                case MetadataType.Boolean:
                case MetadataType.Int32:
                case MetadataType.UInt32:
                    return OpCodes.Stelem_I4;
                case MetadataType.Byte:
                case MetadataType.SByte:
                    return OpCodes.Stelem_I1;
                case MetadataType.Char:
                case MetadataType.Int16:
                case MetadataType.UInt16:
                    return OpCodes.Stelem_I2;
                case MetadataType.Double:
                    return OpCodes.Stelem_R8;
                case MetadataType.Int64:
                case MetadataType.UInt64:
                    return OpCodes.Stelem_I8;
                case MetadataType.Single:
                    return OpCodes.Stelem_R4;
                default:
                    return OpCodes.Stelem_Ref;
            }
        }

        public static MethodDefinition GetZeroArgsCtor(this TypeDefinition typeDef)
        {
            var zeroCtor = typeDef.GetConstructors().FirstOrDefault(ctor => !ctor.HasParameters);
            if (zeroCtor == null)
                throw new RougamoException($"could not found zero arguments constructor from {typeDef.FullName}");
            return zeroCtor;
        }

        public static MethodReference RecursionImportPropertySet(this CustomAttribute attribute,
            ModuleDefinition moduleDef, string propertyName)
        {
            return RecursionImportPropertySet(attribute.AttributeType.Resolve(), moduleDef, propertyName);
        }

        public static MethodReference RecursionImportPropertySet(this TypeDefinition typeDef,
            ModuleDefinition moduleDef, string propertyName)
        {
            var propertyDef = typeDef.Properties.FirstOrDefault(pd => pd.Name == propertyName);
            if (propertyDef != null) return moduleDef.ImportReference(propertyDef.SetMethod);

            var baseTypeDef = typeDef.BaseType.Resolve();
            if (baseTypeDef.FullName == typeof(object).FullName)
                throw new RougamoException($"can not find property({propertyName}) from {typeDef.FullName}");
            return RecursionImportPropertySet(baseTypeDef, moduleDef, propertyName);
        }

        public static MethodReference RecursionImportPropertyGet(this TypeDefinition typeDef,
            ModuleDefinition moduleDef, string propertyName)
        {
            var propertyDef = typeDef.Properties.FirstOrDefault(pd => pd.Name == propertyName);
            if (propertyDef != null) return moduleDef.ImportReference(propertyDef.GetMethod);

            var baseTypeDef = typeDef.BaseType.Resolve();
            if (baseTypeDef.FullName == typeof(object).FullName)
                throw new RougamoException($"can not find property({propertyName}) from {typeDef.FullName}");
            return RecursionImportPropertyGet(baseTypeDef, moduleDef, propertyName);
        }

        public static MethodReference RecursionImportMethod(this CustomAttribute attribute, ModuleDefinition moduleDef,
            string methodName, Func<MethodDefinition, bool> predicate)
        {
            return RecursionImportMethod(attribute.AttributeType.Resolve(), moduleDef, methodName, predicate);
        }

        public static MethodReference RecursionImportMethod(this TypeDefinition typeDef, ModuleDefinition moduleDef,
            string methodName, Func<MethodDefinition, bool> predicate)
        {
            var methodDef = typeDef.Methods.FirstOrDefault(md => md.Name == methodName && predicate(md));
            if (methodDef != null) return moduleDef.ImportReference(methodDef);

            var baseTypeDef = typeDef.BaseType.Resolve();
            if (baseTypeDef.FullName == typeof(object).FullName)
                throw new RougamoException($"can not find method({methodName}) from {typeDef.FullName}");
            return RecursionImportMethod(baseTypeDef, moduleDef, methodName, predicate);
        }

        public static VariableDefinition CreateVariable(this MethodBody body, TypeReference variableTypeReference)
        {
            var variable = new VariableDefinition(variableTypeReference);
            body.Variables.Add(variable);
            return variable;
        }

        public static List<GenericInstanceType> GetGenericInterfaces(this TypeDefinition typeDef, string interfaceName)
        {
            var interfaces = new List<GenericInstanceType>();
            do
            {
                var titf = typeDef.Interfaces.Select(itf => itf.InterfaceType)
                    .Where(itfRef => itfRef.FullName.StartsWith(interfaceName + "<"));
                interfaces.AddRange(titf.Cast<GenericInstanceType>());
                typeDef = typeDef.BaseType?.Resolve();
            } while (typeDef != null);

            return interfaces;
        }

        public static TypeDefinition GetInterfaceDefinition(this TypeDefinition typeDef, string interfaceName)
        {
            do
            {
                var interfaceRef = typeDef.Interfaces.Select(itf => itf.InterfaceType)
                    .Where(itfRef => itfRef.FullName == interfaceName);
                if (interfaceRef.Any()) return interfaceRef.First().Resolve();
                typeDef = typeDef.BaseType.Resolve();
            } while (typeDef != null);

            return null;
        }

        #region Import

        public static TypeReference ImportInto(this TypeReference typeRef, ModuleDefinition moduleDef) =>
            moduleDef.ImportReference(typeRef);

        public static FieldReference ImportInto(this FieldReference fieldRef, ModuleDefinition moduleDef) =>
            moduleDef.ImportReference(fieldRef);

        public static MethodReference ImportInto(this MethodReference methodRef, ModuleDefinition moduleDef) =>
            moduleDef.ImportReference(methodRef);

        #endregion Import

        public static Instruction Copy(this Instruction instruction)
        {
            if (instruction.Operand == null) return Instruction.Create(instruction.OpCode);
            if (instruction.Operand is sbyte sbyteValue) return Instruction.Create(instruction.OpCode, sbyteValue);
            if (instruction.Operand is byte byteValue) return Instruction.Create(instruction.OpCode, byteValue);
            if (instruction.Operand is int intValue) return Instruction.Create(instruction.OpCode, intValue);
            if (instruction.Operand is long longValue) return Instruction.Create(instruction.OpCode, longValue);
            if (instruction.Operand is float floatValue) return Instruction.Create(instruction.OpCode, floatValue);
            if (instruction.Operand is double doubleValue) return Instruction.Create(instruction.OpCode, doubleValue);
            if (instruction.Operand is string stringValue) return Instruction.Create(instruction.OpCode, stringValue);
            if (instruction.Operand is FieldReference fieldReference)
                return Instruction.Create(instruction.OpCode, fieldReference);
            if (instruction.Operand is TypeReference typeReference)
                return Instruction.Create(instruction.OpCode, typeReference);
            if (instruction.Operand is MethodReference methodReference)
                return Instruction.Create(instruction.OpCode, methodReference);
            if (instruction.Operand is ParameterDefinition parameterDefinition)
                return Instruction.Create(instruction.OpCode, parameterDefinition);
            if (instruction.Operand is VariableDefinition variableDefinition)
                return Instruction.Create(instruction.OpCode, variableDefinition);
            if (instruction.Operand is Instruction instruction1)
                return Instruction.Create(instruction.OpCode, instruction1);
            if (instruction.Operand is Instruction[] instructions)
                return Instruction.Create(instruction.OpCode, instructions);
            if (instruction.Operand is CallSite callSite) return Instruction.Create(instruction.OpCode, callSite);
            throw new RougamoException(
                $"not support instruction Operand copy type: {instruction.Operand.GetType().FullName}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TypeDefinition ResolveAsyncStateMachine(this MethodDefinition methodDef)
        {
            return methodDef.ResolveStateMachine(Constants.TYPE_AsyncStateMachineAttribute);
        }

        public static TypeDefinition ResolveStateMachine(this MethodDefinition methodDef, string stateMachineAttributeName)
        {
            var stateMachineAttr = methodDef.CustomAttributes.Single(attr => attr.Is(stateMachineAttributeName));
            var obj = stateMachineAttr.ConstructorArguments[0].Value;
            return obj as TypeDefinition ?? (obj as TypeReference).Resolve();
        }

        public static Instruction ClosePreviousLdarg0(this Instruction instruction, MethodDefinition methodDef)
        {
            while ((instruction = instruction.Previous) != null && instruction.OpCode.Code != Code.Ldarg_0) { }
            return instruction != null && instruction.OpCode.Code == Code.Ldarg_0 ? instruction : throw new RougamoException($"[{methodDef.FullName}] cannot find ldarg.0 from previouses");
        }

        public static Instruction ClosePreviousOffset(this Instruction instruction, MethodDefinition methodDef)
        {
            while ((instruction = instruction.Previous) != null && instruction.Offset == 0) { }
            return instruction;
        }

        public static Instruction Stloc2Ldloc(this Instruction instruction, string exceptionMessage)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Stloc_0:
                    return Instruction.Create(OpCodes.Ldloc_0);
                case Code.Stloc_1:
                    return Instruction.Create(OpCodes.Ldloc_1);
                case Code.Stloc_2:
                    return Instruction.Create(OpCodes.Ldloc_2);
                case Code.Stloc_3:
                    return Instruction.Create(OpCodes.Ldloc_3);
                case Code.Stloc:
                    return Instruction.Create(OpCodes.Ldloc, (VariableDefinition)instruction.Operand);
                case Code.Stloc_S:
                    return Instruction.Create(OpCodes.Ldloc_S, (VariableDefinition)instruction.Operand);
                default:
                    throw new RougamoException(exceptionMessage);
            }
        }

        public static Instruction Ldloc2Stloc(this Instruction instruction, string exceptionMessage)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldloc_0:
                    return Instruction.Create(OpCodes.Stloc_0);
                case Code.Ldloc_1:
                    return Instruction.Create(OpCodes.Stloc_1);
                case Code.Ldloc_2:
                    return Instruction.Create(OpCodes.Stloc_2);
                case Code.Ldloc_3:
                    return Instruction.Create(OpCodes.Stloc_3);
                case Code.Ldloc:
                    return Instruction.Create(OpCodes.Stloc, (VariableDefinition)instruction.Operand);
                case Code.Ldloc_S:
                    return Instruction.Create(OpCodes.Stloc_S, (VariableDefinition)instruction.Operand);
                default:
                    throw new RougamoException(exceptionMessage);
            }
        }

        public static TypeReference GetVariableType(this Instruction ldlocIns, MethodBody body)
        {
            switch (ldlocIns.OpCode.Code)
            {
                case Code.Ldloc_0:
                    return body.Variables[0].VariableType;
                case Code.Ldloc_1:
                    return body.Variables[1].VariableType;
                case Code.Ldloc_2:
                    return body.Variables[2].VariableType;
                case Code.Ldloc_3:
                    return body.Variables[3].VariableType;
                case Code.Ldloc:
                case Code.Ldloc_S:
                    return ((VariableDefinition)ldlocIns.Operand).VariableType;
                case Code.Ldloca:
                case Code.Ldloca_S:
                    throw new RougamoException("need to take a research");
                default:
                    throw new RougamoException("can not get variable type from code: " + ldlocIns.OpCode.Code);

            }
        }

        public static Instruction Ldloc(this VariableDefinition variable)
        {
            return Instruction.Create(OpCodes.Ldloc, variable);
        }

        public static Instruction LdlocOrA(this VariableDefinition variable)
        {
            var variableTypeDef = variable.VariableType.Resolve();
            return variable.VariableType.IsGenericParameter || variableTypeDef.IsValueType && !variableTypeDef.IsEnum && !variableTypeDef.IsPrimitive ? Instruction.Create(OpCodes.Ldloca, variable) : Instruction.Create(OpCodes.Ldloc, variable);
        }

        public static Instruction Ldind(this TypeReference typeRef)
        {
            if (typeRef == null) throw new ArgumentNullException(nameof(typeRef), "Ldind argument null");
            var typeDef = typeRef.Resolve();
            if (!typeRef.IsValueType) return Instruction.Create(OpCodes.Ldind_Ref);
            if (typeDef.Is(typeof(byte).FullName)) return Instruction.Create(OpCodes.Ldind_I1);
            if (typeDef.Is(typeof(short).FullName)) return Instruction.Create(OpCodes.Ldind_I2);
            if (typeDef.Is(typeof(int).FullName)) return Instruction.Create(OpCodes.Ldind_I4);
            if (typeDef.Is(typeof(long).FullName)) return Instruction.Create(OpCodes.Ldind_I8);
            if (typeDef.Is(typeof(sbyte).FullName)) return Instruction.Create(OpCodes.Ldind_U1);
            if (typeDef.Is(typeof(ushort).FullName)) return Instruction.Create(OpCodes.Ldind_U2);
            if (typeDef.Is(typeof(uint).FullName)) return Instruction.Create(OpCodes.Ldind_U4);
            if (typeDef.Is(typeof(ulong).FullName)) return Instruction.Create(OpCodes.Ldind_I8);
            if (typeDef.Is(typeof(float).FullName)) return Instruction.Create(OpCodes.Ldind_R4);
            if (typeDef.Is(typeof(double).FullName)) return Instruction.Create(OpCodes.Ldind_R8);
            if (typeDef.IsEnum)
            {
                if (typeDef.Fields.Count == 0) return Instruction.Create(OpCodes.Ldind_I);
                return Ldind(typeDef.Fields[0].FieldType);
            }
            return Instruction.Create(OpCodes.Ldobj, typeRef); // struct
        }

        public static MethodReference GenericTypeMethodReference(this TypeReference typeRef, MethodReference methodRef, ModuleDefinition moduleDefinition)
        {
            var genericMethodRef = new MethodReference(methodRef.Name, methodRef.ReturnType, typeRef)
            {
                HasThis = methodRef.HasThis,
                ExplicitThis = methodRef.ExplicitThis,
                CallingConvention = methodRef.CallingConvention
            };
            foreach (var parameter in methodRef.Parameters)
            {
                genericMethodRef.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
            }
            foreach (var parameter in methodRef.GenericParameters)
            {
                genericMethodRef.GenericParameters.Add(new GenericParameter(parameter.Name, genericMethodRef));
            }

            return genericMethodRef.ImportInto(moduleDefinition);
        }

        private static Code[] _EmptyCodes = new[] {Code.Nop, Code.Ret};
        public static bool IsEmpty(this MethodDefinition methodDef)
        {
            foreach (var instruction in methodDef.Body.Instructions)
            {
                if (!_EmptyCodes.Contains(instruction.OpCode.Code)) return false;
            }

            return true;
        }

        private static readonly Dictionary<Code, OpCode> _OptimizeCodes = new Dictionary<Code, OpCode>
        {
            { Code.Leave_S, OpCodes.Leave }, { Code.Br_S, OpCodes.Br },
            { Code.Brfalse_S, OpCodes.Brfalse }, { Code.Brtrue_S, OpCodes.Brtrue },
            { Code.Beq_S, OpCodes.Beq }, { Code.Bne_Un_S, OpCodes.Bne_Un },
            { Code.Bge_S, OpCodes.Bge }, { Code.Bgt_S, OpCodes.Bgt },
            { Code.Ble_S, OpCodes.Ble }, { Code.Blt_S, OpCodes.Blt },
            { Code.Bge_Un_S, OpCodes.Bge_Un }, { Code.Bgt_Un_S, OpCodes.Bgt_Un },
            { Code.Ble_Un_S, OpCodes.Ble_Un }, { Code.Blt_Un_S, OpCodes.Blt_Un }
        };
        public static void OptimizePlus(this MethodBody body)
        {
            foreach (var instruction in body.Instructions)
            {
                if (_OptimizeCodes.TryGetValue(instruction.OpCode.Code, out var opcode))
                {
                    instruction.OpCode = opcode;
                }
            }
            body.Optimize();
        }
    }
}