﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using Rougamo.Fody.Enhances;
using Rougamo.Fody.Enhances.Async;
using System;
using System.Collections.Generic;
using System.Linq;
using static Mono.Cecil.Cil.Instruction;

namespace Rougamo.Fody
{
    partial class ModuleWeaver
    {
        private void AsyncTaskMethodWeave(RouMethod rouMethod)
        {
            var stateMachineTypeDef = rouMethod.MethodDef.ResolveStateMachine(Constants.TYPE_AsyncStateMachineAttribute);
            var moveNextMethodDef = stateMachineTypeDef.Methods.Single(m => m.Name == Constants.METHOD_MoveNext);
            var moveNextMethodName = stateMachineTypeDef.DeclaringType.FullName;

            var fields = AsyncResolveFields(rouMethod, stateMachineTypeDef);
            var returnBoxTypeRef = AsyncResolveReturnBoxTypeRef(rouMethod.MethodDef.ReturnType, fields.Builder);
            var variables = AsyncResolveVariables(rouMethod.MethodDef, moveNextMethodDef, stateMachineTypeDef, returnBoxTypeRef);
            var anchors = AsyncCreateAnchors(rouMethod.MethodDef, moveNextMethodDef, variables);
            AsyncSetAnchors(rouMethod.MethodDef, moveNextMethodDef, anchors);

            rouMethod.MethodDef.Body.Instructions.InsertAfter(anchors.InitMos, StateMachineInitMos(rouMethod, fields, variables));
            rouMethod.MethodDef.Body.Instructions.InsertAfter(anchors.InitContext, StateMachineInitMethodContext(rouMethod, fields, variables));

            var setResultMethodRef = variables.ReplacedReturn == null ? AsyncGetSetResult(fields.Builder.FieldType) : AsyncGetGenericSetResult(fields.Builder.FieldType);

            var instructions = moveNextMethodDef.Body.Instructions;
            instructions.InsertAfter(anchors.IfFirstTimeEntry, StateMachineIfFirstTimeEntry(-1, anchors.Retry, fields));
            instructions.InsertAfter(anchors.OnEntry, StateMachineOnEntry(rouMethod, moveNextMethodDef, anchors.IfEntryReplaced, fields));
            instructions.InsertAfter(anchors.IfEntryReplaced, AsyncIfOnEntryReplacedReturn(rouMethod, moveNextMethodDef, returnBoxTypeRef, setResultMethodRef, anchors.RewriteArg, fields, variables));
            instructions.InsertAfter(anchors.RewriteArg, StateMachineRewriteArguments(anchors.Retry, fields));

            instructions.InsertAfter(anchors.SaveException, StateMachineSaveException(moveNextMethodName, anchors.CatchStart, fields));
            instructions.InsertAfter(anchors.OnException, StateMachineOnException(rouMethod, moveNextMethodDef, anchors.IfExceptionRetry, fields));
            instructions.InsertAfter(anchors.IfExceptionRetry, AsyncIfExceptionRetry(anchors.Retry, anchors.ExceptionContextStash, fields));
            instructions.InsertAfter(anchors.ExceptionContextStash, AsyncExceptionContextStash(fields, variables));
            instructions.InsertAfter(anchors.OnExitAfterException, StateMachineOnExit(rouMethod, moveNextMethodDef, anchors.IfExceptionHandled, fields));
            instructions.InsertAfter(anchors.IfExceptionHandled, AsyncIfExceptionHandled(returnBoxTypeRef, setResultMethodRef, anchors.HostsSetException, anchors.HostsLeaveCatch, fields, variables));

            if (anchors.HostsSetResult != null)
            {
                var notVoid = anchors.HostsLdlocReturn != null;
                if (notVoid) instructions.InsertAfter(anchors.SaveReturnValue, AsyncSaveReturnValue(returnBoxTypeRef, anchors.HostsLdlocReturn!, fields));
                instructions.InsertAfter(anchors.OnSuccess, StateMachineOnSuccess(rouMethod, moveNextMethodDef, anchors.IfSuccessRetry, fields));
                instructions.InsertAfter(anchors.IfSuccessRetry, AsyncIfSuccessRetry(anchors.Retry, anchors.IfSuccessReplaced, fields));
                if (notVoid) instructions.InsertAfter(anchors.IfSuccessReplaced, AsyncIfSuccessReplacedReturn(moveNextMethodName, returnBoxTypeRef, anchors.HostsLdlocReturn!, anchors.OnExitAfterSuccess, fields));
                instructions.InsertAfter(anchors.OnExitAfterSuccess, StateMachineOnExit(rouMethod, moveNextMethodDef, anchors.HostsSetResult, fields));
            }

            moveNextMethodDef.Body.OptimizePlus();
            rouMethod.MethodDef.Body.OptimizePlus();
        }

        private AsyncFields AsyncResolveFields(RouMethod rouMethod, TypeDefinition stateMachineTypeDef)
        {
            var mosFieldDef = new FieldDefinition(Constants.FIELD_RougamoMos, FieldAttributes.Public, _typeIMoArrayRef);
            var contextFieldDef = new FieldDefinition(Constants.FIELD_RougamoContext, FieldAttributes.Public, _typeMethodContextRef);
            var builderFieldDef = stateMachineTypeDef.Fields.Single(x => x.Name == Constants.FIELD_Builder);
            var stateFieldDef = stateMachineTypeDef.Fields.Single(x => x.Name == Constants.FIELD_State);
            var parameterFieldDefs = StateMachineParameterFields(rouMethod);

            stateMachineTypeDef.Fields.Add(mosFieldDef);
            stateMachineTypeDef.Fields.Add(contextFieldDef);

            return new AsyncFields(
                        stateMachineTypeDef: stateMachineTypeDef,
                        mos: mosFieldDef, methodContext: contextFieldDef,
                        state: stateFieldDef, builder: builderFieldDef,
                        parameters: parameterFieldDefs);
        }

        private BoxTypeReference AsyncResolveReturnBoxTypeRef(TypeReference returnTypeRef, FieldReference builderField)
        {
            return new BoxTypeReference(returnTypeRef.IsTask() || returnTypeRef.IsValueTask() || returnTypeRef.IsVoid() ? _typeVoidRef : ((GenericInstanceType)builderField.FieldType).GenericArguments[0]);
        }

        private AsyncVariables AsyncResolveVariables(MethodDefinition methodDef, MethodDefinition moveNextMethodDef, TypeDefinition stateMachineTypeDef, BoxTypeReference returnBoxTypeRef)
        {
            var stateMachineVariable = methodDef.Body.Variables.Single(x => x.VariableType.Resolve() == stateMachineTypeDef);
            var replacedReturnVariable = (TypeReference)returnBoxTypeRef == _typeVoidRef ? null : moveNextMethodDef.Body.CreateVariable(_typeObjectRef);
            var exceptionHandledVariable = moveNextMethodDef.Body.CreateVariable(_typeBoolRef);

            return new AsyncVariables(stateMachineVariable, replacedReturnVariable, exceptionHandledVariable);
        }

        private AsyncAnchors AsyncCreateAnchors(MethodDefinition methodDef, MethodDefinition moveNextMethodDef, AsyncVariables variables)
        {
            var exceptionHandler = GetOuterExceptionHandler(moveNextMethodDef);

            var hostsBuilderCreate = AsyncGetBuilderCreateStartAnchor(methodDef, out var builderTypeDef);
            var catchStart = exceptionHandler.HandlerStart;
            var hostsSetException = AsyncGetSetExceptionStartAnchor(moveNextMethodDef, exceptionHandler);
            var hostsLeaveCatch = AsyncGetLeaveCatchAnchor(exceptionHandler);
            var setResult = moveNextMethodDef.Body.Instructions.SingleOrDefault(x => x.Operand is MethodReference methodRef && methodRef.DeclaringType.Resolve() == builderTypeDef && methodRef.Name == Constants.METHOD_SetResult);
            Instruction? hostsSetResult, hostsLdlocReturn;
            if (setResult == null)
            {
                hostsSetResult = null;
                hostsLdlocReturn = null;
            }
            else if (variables.ReplacedReturn == null)
            {
                hostsSetResult = setResult.Previous.Previous;
                hostsLdlocReturn = null;
            }
            else
            {
                hostsSetResult = setResult.Previous.Previous.Previous;
                hostsLdlocReturn = setResult.Previous;
            }

            return new AsyncAnchors(hostsBuilderCreate, catchStart, hostsSetException, hostsLeaveCatch, hostsSetResult, hostsLdlocReturn);
        }

        private Instruction AsyncGetBuilderCreateStartAnchor(MethodDefinition methodDef, out TypeDefinition builderTypeDef)
        {
            foreach (var instruction in methodDef.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference methodRef &&
                    methodRef.DeclaringType.FullName.StartsWithAny(Constants.TYPE_AsyncTaskMethodBuilder, Constants.TYPE_AsyncValueTaskMethodBuilder, Constants.TYPE_AsyncVoidMethodBuilder) &&
                    methodRef.Name == Constants.METHOD_Create)
                {
                    builderTypeDef = methodRef.DeclaringType.Resolve();

                    return instruction.Previous;
                }
            }

            throw new RougamoException($"[{methodDef.FullName}] Unable find call AsyncTaskMethodBuilder.Create instruction");
        }

        private Instruction AsyncGetSetExceptionStartAnchor(MethodDefinition moveNextMethodDef, ExceptionHandler exceptionHandler)
        {
            var instruction = exceptionHandler.HandlerStart;
            while (true)
            {
                if (instruction.OpCode.Code == Code.Call && instruction.Operand is MethodReference setException &&
                    setException.Name == Constants.METHOD_SetException &&
                    setException.DeclaringType.FullName.StartsWithAny(
                        Constants.TYPE_AsyncTaskMethodBuilder,
                        Constants.TYPE_AsyncValueTaskMethodBuilder,
                        Constants.TYPE_AsyncVoidMethodBuilder,
                        Constants.TYPE_ManualResetValueTaskSourceCore))
                {
                    var setExceptionStart = instruction.Previous.Previous.Previous;
                    if (setExceptionStart.OpCode.Code != Code.Ldarg_0) throw new RougamoException($"Offset {setExceptionStart.Offset} of {moveNextMethodDef.FullName} is {setExceptionStart.OpCode.Code}, it should be Ldarg0 of SetResult which offset is {instruction.Offset}");

                    return setExceptionStart;
                }
                instruction = instruction.Next;
                if (instruction == null || instruction == exceptionHandler.HandlerEnd) throw new InvalidOperationException($"[{moveNextMethodDef.DeclaringType.FullName}] SetException instruction not found");
            }
        }

        private Instruction AsyncGetLeaveCatchAnchor(ExceptionHandler exceptionHandler)
        {
            var leaveCatch = exceptionHandler.HandlerEnd.Previous;
            while (leaveCatch.OpCode.Code != Code.Leave && leaveCatch.OpCode.Code != Code.Leave_S)
            {
                if (leaveCatch == exceptionHandler.HandlerStart) throw new RougamoException($"Cannot find leave or leave_s from exception handler range[{exceptionHandler.HandlerStart.Offset} - {exceptionHandler.HandlerEnd.Offset}]");

                leaveCatch = leaveCatch.Previous;
            }

            return leaveCatch;
        }

        private void AsyncSetAnchors(MethodDefinition methodDef, MethodDefinition moveNextMethodDef, AsyncAnchors anchors)
        {
            methodDef.Body.Instructions.InsertBefore(anchors.HostsBuilderCreate, new[]
            {
                anchors.InitMos,
                anchors.InitContext
            });

            AsyncSetMoveNextAnchors(moveNextMethodDef, anchors);
        }

        private void AsyncSetMoveNextAnchors(MethodDefinition moveNextMethodDef, AsyncAnchors anchors)
        {
            var instructions = moveNextMethodDef.Body.Instructions;

            instructions.Insert(0, new[]
            {
                anchors.IfFirstTimeEntry,
                anchors.OnEntry,
                anchors.IfEntryReplaced,
                anchors.RewriteArg,
                anchors.Retry
            });

            instructions.InsertBefore(anchors.HostsSetException, new[]
            {
                anchors.SaveException,
                anchors.OnException,
                anchors.IfExceptionRetry,
                anchors.ExceptionContextStash,
                anchors.OnExitAfterException,
                anchors.IfExceptionHandled
            });

            if (anchors.HostsSetResult != null)
            {
                instructions.InsertBefore(anchors.HostsSetResult, new[]
                {
                    anchors.SaveReturnValue,
                    anchors.OnSuccess,
                    anchors.IfSuccessRetry,
                    anchors.IfSuccessReplaced,
                    anchors.OnExitAfterSuccess
                });
            }
        }

        private IList<Instruction> StateMachineInitMos(RouMethod rouMethod, IStateMachineFields fields, IStateMachineVariables variables)
        {
            var mosFieldRef = new FieldReference(fields.Mos.Name, fields.Mos.FieldType, variables.StateMachine.VariableType);

            var instructions = new List<Instruction>
            {
                variables.StateMachine.LdlocOrA()
            };
            instructions.AddRange(InitMosArray(rouMethod.Mos));
            instructions.Add(Create(OpCodes.Stfld, mosFieldRef));

            return instructions;
        }

        private IList<Instruction> StateMachineInitMethodContext(RouMethod rouMethod, IStateMachineFields fields, IStateMachineVariables variables)
        {
            var mosFieldRef = new FieldReference(fields.Mos.Name, fields.Mos.FieldType, variables.StateMachine.VariableType);
            var contextFieldRef = new FieldReference(fields.MethodContext.Name, fields.MethodContext.FieldType, variables.StateMachine.VariableType);

            var instructions = new List<Instruction>
            {
                variables.StateMachine.LdlocOrA()
            };
            instructions.AddRange(InitMethodContext(rouMethod.MethodDef, true, false, null, variables.StateMachine, mosFieldRef));
            instructions.Add(Create(OpCodes.Stfld, contextFieldRef));

            return instructions;
        }

        private IList<Instruction> StateMachineIfFirstTimeEntry(int initialState, Instruction ifNotFirstTimeGoto, IStateMachineFields fields)
        {
            return new[]
            {
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldfld, fields.State),
                LdcI4(initialState),
                Create(OpCodes.Bne_Un, ifNotFirstTimeGoto)
            };
        }

        private IList<Instruction> StateMachineOnEntry(RouMethod rouMethod, MethodDefinition moveNextMethodDef, Instruction endAnchor, IStateMachineFields fields)
        {
            return ExecuteMoMethod(Constants.METHOD_OnEntry, moveNextMethodDef, rouMethod.Mos.Count, endAnchor, null, null, fields.Mos, fields.MethodContext, false);
        }

        private IList<Instruction> AsyncIfOnEntryReplacedReturn(RouMethod rouMethod, MethodDefinition moveNextMethodDef, BoxTypeReference returnBoxTypeRef, MethodReference setResultMethodRef, Instruction endAnchor, AsyncFields fields, AsyncVariables variables)
        {
            var instructions = new List<Instruction>
            {
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldfld, fields.MethodContext),
                Create(OpCodes.Callvirt, _methodMethodContextGetReturnValueReplacedRef),
                Create(OpCodes.Brfalse_S, endAnchor),
            };
            if (variables.ReplacedReturn != null)
            {
                instructions.AddRange(new[]
                {
                    Create(OpCodes.Ldarg_0),
                    Create(OpCodes.Ldfld, fields.MethodContext),
                    Create(OpCodes.Callvirt, _methodMethodContextGetReturnValueRef),
                    Create(OpCodes.Stloc, variables.ReplacedReturn)
                });
            }
            instructions.AddRange(new[]
            {
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldc_I4, -2),
                Create(OpCodes.Stfld, fields.State)
            });
            var onExitEndAnchor = Create(OpCodes.Ldarg_0);
            instructions.AddRange(ExecuteMoMethod(Constants.METHOD_OnExit, moveNextMethodDef, rouMethod.Mos.Count, onExitEndAnchor, null, null, fields.Mos, fields.MethodContext, this.ConfigReverseCallEnding()));
            instructions.Add(onExitEndAnchor);
            instructions.Add(Create(OpCodes.Ldflda, fields.Builder));
            if (variables.ReplacedReturn != null)
            {
                instructions.Add(Create(OpCodes.Ldloc, variables.ReplacedReturn));
                if (returnBoxTypeRef)
                {
                    instructions.Add(Create(OpCodes.Unbox_Any, returnBoxTypeRef));
                }
            }
            instructions.Add(Create(OpCodes.Call, setResultMethodRef));
            instructions.Add(Create(OpCodes.Ret));

            return instructions;
        }

        private IList<Instruction> StateMachineRewriteArguments(Instruction endAnchor, IStateMachineFields fields)
        {
            var instructions = new List<Instruction>
            {
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldfld, fields.MethodContext),
                Create(OpCodes.Callvirt, _methodMethodContextGetRewriteArgumentsRef),
                Create(OpCodes.Brfalse_S, endAnchor)
            };
            for (var i = 0; i < fields.Parameters.Length; i++)
            {
                var parameterFieldRef = fields.Parameters[i];
                if (parameterFieldRef == null) continue;

                StateMachineRewriteArgument(i, fields.MethodContext, parameterFieldRef, instructions.Add);
            }

            return instructions;
        }

        private void StateMachineRewriteArgument(int index, FieldReference contextFieldRef, FieldReference parameterFieldRef, Action<Instruction> append)
        {
            var parameterTypeRef = parameterFieldRef.FieldType.ImportInto(ModuleDefinition);
            Instruction? afterNullNop = null;
            if (parameterTypeRef.MetadataType == MetadataType.Class ||
                parameterTypeRef.MetadataType == MetadataType.Array ||
                parameterTypeRef.IsGenericParameter ||
                parameterTypeRef.IsString() || parameterTypeRef.IsNullable())
            {
                var notNullNop = Create(OpCodes.Nop);
                afterNullNop = Create(OpCodes.Nop);
                append(Create(OpCodes.Ldarg_0));
                append(Create(OpCodes.Ldfld, contextFieldRef));
                append(Create(OpCodes.Callvirt, _methodMethodContextGetArgumentsRef));
                append(Create(OpCodes.Ldc_I4, index));
                append(Create(OpCodes.Ldelem_Ref));
                append(Create(OpCodes.Ldnull));
                append(Create(OpCodes.Ceq));
                append(Create(OpCodes.Brfalse_S, notNullNop));
                append(Create(OpCodes.Ldarg_0));
                if (parameterTypeRef.IsGenericParameter || parameterTypeRef.IsNullable())
                {
                    append(Create(OpCodes.Ldflda, parameterFieldRef));
                    append(Create(OpCodes.Initobj, parameterTypeRef));
                }
                else
                {
                    append(Create(OpCodes.Ldnull));
                    append(Create(OpCodes.Stfld, parameterFieldRef));
                }
                append(Create(OpCodes.Br_S, afterNullNop));
                append(notNullNop);
            }
            append(Create(OpCodes.Ldarg_0));
            append(Create(OpCodes.Ldarg_0));
            append(Create(OpCodes.Ldfld, contextFieldRef));
            append(Create(OpCodes.Callvirt, _methodMethodContextGetArgumentsRef));
            append(Create(OpCodes.Ldc_I4, index));
            append(Create(OpCodes.Ldelem_Ref));
            if (parameterTypeRef.IsUnboxable())
            {
                append(Create(OpCodes.Unbox_Any, parameterTypeRef));
            }
            else if (!parameterTypeRef.Is(typeof(object).FullName))
            {
                append(Create(OpCodes.Castclass, parameterTypeRef));
            }
            append(Create(OpCodes.Stfld, parameterFieldRef));
            if (afterNullNop != null)
            {
                append(afterNullNop);
            }
        }

        private IList<Instruction> StateMachineSaveException(string methodName, Instruction stlocException, IStateMachineFields fields)
        {
            var ldlocException = stlocException.Stloc2Ldloc($"{methodName} exception handler first instruction is not stloc.* exception");

            return new[]
            {
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldfld, fields.MethodContext),
                ldlocException,
                Create(OpCodes.Callvirt, _methodMethodContextSetExceptionRef)
            };
        }

        private IList<Instruction> StateMachineOnException(RouMethod rouMethod, MethodDefinition moveNextMethodDef, Instruction endAnchor, IStateMachineFields fields)
        {
            return ExecuteMoMethod(Constants.METHOD_OnException, moveNextMethodDef, rouMethod.Mos.Count, endAnchor, null, null, fields.Mos, fields.MethodContext, this.ConfigReverseCallEnding());
        }

        private IList<Instruction> AsyncIfExceptionRetry(Instruction retryStart, Instruction endAnchor, AsyncFields fields)
        {
            return new[]
            {
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldfld, fields.MethodContext),
                Create(OpCodes.Callvirt, _methodMethodContextGetRetryCountRef),
                Create(OpCodes.Ldc_I4_0),
                Create(OpCodes.Cgt),
                Create(OpCodes.Brfalse_S, endAnchor),
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldc_I4_M1),
                Create(OpCodes.Stfld, fields.State),
                Create(OpCodes.Leave_S, retryStart)
            };
        }

        private IList<Instruction> AsyncExceptionContextStash(AsyncFields fields, AsyncVariables variables)
        {
            var instructions = new List<Instruction>
            {
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldfld, fields.MethodContext),
                Create(OpCodes.Callvirt, _methodMethodContextGetExceptionHandledRef),
                Create(OpCodes.Stloc, variables.ExceptionHandled),
            };
            if (variables.ReplacedReturn != null)
            {
                instructions.AddRange(new[]
                {
                    Create(OpCodes.Ldarg_0),
                    Create(OpCodes.Ldfld, fields.MethodContext),
                    Create(OpCodes.Callvirt, _methodMethodContextGetReturnValueRef),
                    Create(OpCodes.Stloc, variables.ReplacedReturn)
                });
            }

            return instructions;
        }

        private IList<Instruction> StateMachineOnExit(RouMethod rouMethod, MethodDefinition moveNextMethodDef, Instruction endAnchor, IStateMachineFields fields)
        {
            return ExecuteMoMethod(Constants.METHOD_OnExit, moveNextMethodDef, rouMethod.Mos.Count, endAnchor, null, null, fields.Mos, fields.MethodContext, this.ConfigReverseCallEnding());
        }

        private IList<Instruction> AsyncIfExceptionHandled(BoxTypeReference returnBoxTypeRef, MethodReference setResultMethodRef, Instruction ifUnhandledBrTo, Instruction ifHandledBrTo, AsyncFields fields, AsyncVariables variables)
        {
            var instructions = new List<Instruction>
            {
                Create(OpCodes.Ldloc, variables.ExceptionHandled),
                Create(OpCodes.Brfalse_S, ifUnhandledBrTo),
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldflda, fields.Builder)
            };
            if (variables.ReplacedReturn != null)
            {
                instructions.Add(Create(OpCodes.Ldloc, variables.ReplacedReturn));
                if (returnBoxTypeRef)
                {
                    instructions.Add(Create(OpCodes.Unbox_Any, returnBoxTypeRef));
                }
            }
            instructions.Add(Create(OpCodes.Call, setResultMethodRef));
            instructions.Add(Create(OpCodes.Br_S, ifHandledBrTo));

            return instructions;
        }

        private IList<Instruction> AsyncSaveReturnValue(BoxTypeReference returnBoxTypeRef, Instruction ldlocReturn, AsyncFields fields)
        {
            var instructions = new List<Instruction>
            {
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldfld, fields.MethodContext),
                ldlocReturn.Copy(),
            };
            if (returnBoxTypeRef)
            {
                instructions.Add(Create(OpCodes.Box, returnBoxTypeRef));
            }
            instructions.Add(Create(OpCodes.Callvirt, _methodMethodContextSetReturnValueRef));

            return instructions;
        }

        private IList<Instruction> StateMachineOnSuccess(RouMethod rouMethod, MethodDefinition moveNextMethodDef, Instruction endAnchor, IStateMachineFields fields)
        {
            return ExecuteMoMethod(Constants.METHOD_OnSuccess, moveNextMethodDef, rouMethod.Mos.Count, endAnchor, null, null, fields.Mos, fields.MethodContext, this.ConfigReverseCallEnding());
        }

        private IList<Instruction> AsyncIfSuccessRetry(Instruction ifRetryGoTo, Instruction ifNotRetryGoTo, AsyncFields fields)
        {
            return new[]
            {
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldfld, fields.MethodContext),
                Create(OpCodes.Callvirt, _methodMethodContextGetRetryCountRef),
                Create(OpCodes.Ldc_I4_0),
                Create(OpCodes.Cgt),
                Create(OpCodes.Brfalse_S, ifNotRetryGoTo),
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldc_I4_M1),
                Create(OpCodes.Stfld, fields.State),
                Create(OpCodes.Leave_S, ifRetryGoTo),
            };
        }

        private IList<Instruction> AsyncIfSuccessReplacedReturn(string methodName, BoxTypeReference returnBoxTypeRef, Instruction ldlocReturn, Instruction ifNotReplacedGoTo, AsyncFields fields)
        {
            var instructions = new List<Instruction>
            {
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldfld, fields.MethodContext),
                Create(OpCodes.Callvirt, _methodMethodContextGetReturnValueReplacedRef),
                Create(OpCodes.Brfalse_S, ifNotReplacedGoTo),
                Create(OpCodes.Ldarg_0),
                Create(OpCodes.Ldfld, fields.MethodContext),
                Create(OpCodes.Callvirt, _methodMethodContextGetReturnValueRef),
            };
            if (returnBoxTypeRef)
            {
                instructions.Add(Create(OpCodes.Unbox_Any, returnBoxTypeRef));
            }
            instructions.Add(ldlocReturn.Ldloc2Stloc($"[{methodName}] offset: {ldlocReturn}, it should be ldloc"));

            return instructions;
        }

        private FieldDefinition?[] StateMachineParameterFields(RouMethod rouMethod)
        {
            var isStaticMethod = rouMethod.MethodDef.IsStatic;
            var parameterFieldDefs = new FieldDefinition?[rouMethod.MethodDef.Parameters.Count];
            foreach (var instruction in rouMethod.MethodDef.Body.Instructions)
            {
                var index = -1;
                var code = instruction.OpCode.Code;
                if (isStaticMethod && code == Code.Ldarg_0 || !isStaticMethod && code == Code.Ldarg_1)
                {
                    index = 0;
                }
                else if (isStaticMethod && code == Code.Ldarg_1 || !isStaticMethod && code == Code.Ldarg_2)
                {
                    index = 1;
                }
                else if (isStaticMethod && code == Code.Ldarg_2 || !isStaticMethod && code == Code.Ldarg_3)
                {
                    index = 2;
                }
                else if (isStaticMethod && code == Code.Ldarg_3)
                {
                    index = 3;
                }
                else if (code == Code.Ldarg || code == Code.Ldarg_S)
                {
                    index = rouMethod.MethodDef.Parameters.IndexOf((ParameterDefinition)instruction.Operand);
                    if (index == -1) throw new RougamoException($"{rouMethod.MethodDef.FullName} can not locate the index of parameter {((ParameterDefinition)instruction.Operand).Name}");
                }
                if (index != -1)
                {
                    parameterFieldDefs[index] = ((FieldReference)instruction.Next.Operand).Resolve();
                }
            }

            return parameterFieldDefs;
        }

        private MethodReference AsyncGetSetResult(TypeReference builderTypeRef)
        {
            return builderTypeRef.Resolve().Methods.Single(x => x.Name == "SetResult" && x.Parameters.Count == 0 && x.IsPublic).ImportInto(ModuleDefinition);
        }

        private MethodReference AsyncGetGenericSetResult(TypeReference builderTypeRef)
        {
            return builderTypeRef.GenericTypeMethodReference(builderTypeRef.Resolve().Methods.Single(x => x.Name == "SetResult" && x.Parameters.Count == 1 && x.IsPublic), ModuleDefinition);
        }
    }
}
