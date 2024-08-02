﻿using Mono.Cecil.Cil;
using Rougamo.Fody.Enhances.Sync;
using Rougamo.Fody.Simulations;
using Rougamo.Fody.Simulations.Operations;
using Rougamo.Fody.Simulations.PlainValues;
using Rougamo.Fody.Simulations.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using static Mono.Cecil.Cil.Instruction;

namespace Rougamo.Fody
{
    partial class ModuleWeaver
    {
        private void WeavingSyncMethod(RouMethod rouMethod)
        {
            var actualMethodDef = rouMethod.MethodDef.Clone($"$Rougamo_{rouMethod.MethodDef.Name}");
            rouMethod.MethodDef.DeclaringType.Methods.Add(actualMethodDef);
            actualMethodDef.CustomAttributes.Clear();

            var tWeavingTarget = rouMethod.MethodDef.DeclaringType.Simulate<TsWeavingTarget>(this).SetMethods(actualMethodDef, rouMethod.MethodDef);
            SyncBuildProxyMethod(rouMethod, tWeavingTarget);
        }

        private void SyncBuildProxyMethod(RouMethod rouMethod, TsWeavingTarget tWeavingTarget)
        {
            tWeavingTarget.M_Proxy.Def.Clear();
            tWeavingTarget.M_Proxy.Def.DebuggerStepThrough(_methodDebuggerStepThroughCtorRef);

            SyncBuildProxyMethodInternal(rouMethod, tWeavingTarget);

            tWeavingTarget.M_Proxy.Def.Body.InitLocals = true;
            tWeavingTarget.M_Proxy.Def.Body.OptimizePlus(EmptyInstructions);
        }

        private void SyncBuildProxyMethodInternal(RouMethod rouMethod, TsWeavingTarget tWeavingTarget)
        {
            var instructions = tWeavingTarget.M_Proxy.Def.Body.Instructions;

            var context = new SyncContext();

            var args = tWeavingTarget.M_Proxy.Def.Parameters.Select(x => x.Simulate(this)).ToArray();
            var vException = rouMethod.Features.HasIntersection(Feature.OnException | Feature.OnExit) ? tWeavingTarget.M_Proxy.CreateVariable(_typeExceptionRef) : null;
            var vResult = tWeavingTarget.M_Proxy.Result.Ref.IsVoid() ? null : tWeavingTarget.M_Proxy.CreateVariable(tWeavingTarget.M_Proxy.Result);
            var vContext = tWeavingTarget.M_Proxy.CreateVariable<TsMethodContext>(_typeMethodContextRef);
            VariableSimulation<TsMo>[]? vMos = null;
            VariableSimulation<TsArray<TsMo>>? vMoArray = null;
            if (rouMethod.Mos.Length > _config.MoArrayThreshold)
            {
                vMoArray = tWeavingTarget.M_Proxy.CreateVariable<TsArray<TsMo>>(_typeIMoArrayRef);
            }
            else
            {
                vMos = rouMethod.Mos.Select(x => tWeavingTarget.M_Proxy.CreateVariable<TsMo>(x.MoTypeRef)).ToArray();
            }

            Instruction? tryStart = null, catchStart = null, catchEnd = Create(OpCodes.Nop);

            // .var mo = new Mo1Attributue(...);
            instructions.Add(SyncInitMos(rouMethod, tWeavingTarget, vMos, vMoArray));
            // .var context = new MethodContext(...);
            instructions.Add(SyncInitMethodContext(rouMethod, tWeavingTarget, args, vContext, vMos, vMoArray));
            // .mo.OnEntry(context);
            instructions.Add(SyncMosOn(rouMethod, tWeavingTarget, vContext, vMos, vMoArray, Feature.OnEntry, mo => mo.M_OnEntry));
            // .if (context.ReturnValueReplaced) { .. }
            instructions.Add(SyncIfOnEntryReplacedReturn(rouMethod, tWeavingTarget, vContext, vMos, vMoArray, vResult));
            // .if (_context.RewriteArguments) { ... }
            instructions.Add(SyncIfRewriteArguments(rouMethod, tWeavingTarget, args, vContext));

            // .RETRY:
            instructions.Add(context.AnchorRetry);
            // .try
            {
                if (vResult == null)
                {
                    // .actualMethod(...);
                    tryStart = instructions.AddGetFirst(tWeavingTarget.M_Actual.Call(tWeavingTarget.M_Proxy, args));
                }
                else
                {
                    // .result = actualMethod(...);
                    tryStart = instructions.AddGetFirst(vResult.Assign(target => tWeavingTarget.M_Actual.Call(tWeavingTarget.M_Proxy, args)));
                }
                // .context.ReturnValue = result;
                SyncSaveResult(rouMethod, tWeavingTarget, vContext, vResult);

                if (vException != null)
                {
                    instructions.Add(Create(OpCodes.Leave, catchEnd));
                }
            }
            // .catch
            if (vException != null)
            {
                catchStart = instructions.AddGetFirst(vException.Assign(target => []));
                // .context.Exception = exception;
                instructions.Add(vContext.Value.P_Exception.Assign(vException));
                // .mo.OnException(context); ...
                instructions.Add(SyncMosOn(rouMethod, tWeavingTarget, vContext, vMos, vMoArray, Feature.OnException, mo => mo.M_OnException));
                // .arg_i = context.Arguments[i];
                instructions.Add(SyncRefreshArguments(rouMethod, tWeavingTarget, args, vContext, Feature.OnException));
                // .if (context.RetryCount > 0) goto RETRY;
                instructions.Add(SynCheckRetry(rouMethod, tWeavingTarget, vContext, context, Feature.OnException, true));
                // .if (exceptionHandled) result = context.ReturnValue;
                instructions.Add(SyncSaveResultIfExceptionHandled(rouMethod, tWeavingTarget, vContext, vResult, out var vExceptionHandled));
                // .mo.OnExit(context);
                instructions.Add(SyncMosOn(rouMethod, tWeavingTarget, vContext, vMos, vMoArray, Feature.OnExit, mo => mo.M_OnExit));
                // .if (exceptionHandled) return result;
                instructions.Add(SyncReturnIfExceptionHandled(rouMethod, tWeavingTarget, vExceptionHandled, context));
                instructions.Add(Create(OpCodes.Rethrow));

                instructions.Add(catchEnd);
            }

            // .context.ReturnValue = result;
            instructions.Add(SyncSaveResult(rouMethod, vContext, vResult));
            // .mo.OnSuccess(context);
            instructions.Add(SyncMosOn(rouMethod, tWeavingTarget, vContext, vMos, vMoArray, Feature.OnSuccess, mo => mo.M_OnSuccess));
            // .arg_i = context.Arguments[i];
            instructions.Add(SyncRefreshArguments(rouMethod, tWeavingTarget, args, vContext, Feature.OnSuccess));
            // .if (context.RetryCount > 0) goto RETRY;
            instructions.Add(SynCheckRetry(rouMethod, tWeavingTarget, vContext, context, Feature.OnSuccess, false));
            // .if (returnValueReplaced) result = context.ReturnValue;
            instructions.Add(SyncSaveResultIfReturnValueReplaced(rouMethod, tWeavingTarget, vContext, vResult));
            // .mo.OnExit(context);
            instructions.Add(SyncMosOn(rouMethod, tWeavingTarget, vContext, vMos, vMoArray, Feature.OnExit, mo => mo.M_OnExit));
            // .return result;
            instructions.Add(context.AnchorReturnResult);
            if (vResult != null) instructions.Add(vResult.Load());
            instructions.Add(Create(OpCodes.Ret));

            if (tryStart != null && catchStart != null && catchEnd != null)
            {
                tWeavingTarget.M_Proxy.Def.Body.ExceptionHandlers.Add(new(ExceptionHandlerType.Catch)
                {
                    TryStart = tryStart,
                    TryEnd = catchStart,
                    HandlerStart = catchStart,
                    HandlerEnd = catchEnd,
                    CatchType = _typeExceptionRef
                });
            }
        }

        private IList<Instruction> SyncInitMos(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, VariableSimulation<TsMo>[]? vMos, VariableSimulation<TsArray<TsMo>>? vMoArray)
        {
            var instructions = new List<Instruction>();

            var loadables = new List<ILoadable>(rouMethod.Mos.Length);

            for (int i = 0; i < rouMethod.Mos.Length; i++)
            {
                var mo = rouMethod.Mos[i];

                if (vMos != null)
                {
                    var vMo = vMos[i];
                    instructions.Add(vMo.Assign(target => vMo.Value.New(tWeavingTarget.M_Proxy, mo)));
                }
                else if (vMoArray != null)
                {
                    var tMo = mo.MoTypeDef.Simulate<TsMo>(this);
                    loadables.Add(new RawValue(vMoArray.Value.ElementType, tMo.New(tWeavingTarget.M_Proxy, mo)));
                }
            }
            if (vMoArray != null)
            {
                instructions.Add(vMoArray.Assign(target => vMoArray.Value.New(loadables.ToArray())));
            }

            return instructions;
        }

        private IList<Instruction> SyncInitMethodContext(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, ArgumentSimulation[] args, VariableSimulation<TsMethodContext> vContext, VariableSimulation<TsMo>[]? vMos, VariableSimulation<TsArray<TsMo>>? vMoArray)
        {
            var tMoArray = _typeIMoArrayRef.Simulate<TsArray>(this);
            var tObjectArray = _typeObjectArrayRef.Simulate<TsArray>(this);
            IParameterSimulation[] arguments = [
                tWeavingTarget.M_Proxy.Def.IsStatic ? new Null(this) : new This(_simulations.Object),
                new SystemType(rouMethod.MethodDef.DeclaringType, this),
                new SystemMethodBase(rouMethod.MethodDef, this),
                new Bool(false, this),
                new Bool(false, this),
                new Bool(!_config.ReverseCallNonEntry, this),
                rouMethod.MethodContextOmits.Contains(Omit.Mos) ? tMoArray.Null() : vMoArray ?? (IParameterSimulation)tMoArray.NewAsPlainValue(vMos!),
                rouMethod.MethodContextOmits.Contains(Omit.Arguments) ? tObjectArray.Null() : tObjectArray.NewAsPlainValue(args)
            ];
            return vContext.AssignNew(arguments);
        }

        private IList<Instruction>? SyncIfOnEntryReplacedReturn(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, VariableSimulation<TsMethodContext> vContext, VariableSimulation<TsMo>[]? vMos, VariableSimulation<TsArray<TsMo>>? vMoArray, VariableSimulation? vResult)
        {
            if (!rouMethod.Features.Contains(Feature.EntryReplace) || rouMethod.MethodContextOmits.Contains(Omit.ReturnValue)) return null;

            // .if (context.ReturnValueReplaced)
            return vContext.Value.P_ReturnValueReplaced.If(anchor =>
            {
                var instructions = new List<Instruction>();

                if (vResult != null)
                {
                    // .result = context.ReturnValue;
                    instructions.Add(vResult.Assign(vContext.Value.P_ReturnValue));
                }
                // .mo.OnExit(context);
                instructions.Add(SyncMosOn(rouMethod, tWeavingTarget, vContext, vMos, vMoArray, Feature.OnExit, mo => mo.M_OnExit));
                // .return result;
                if (vResult != null)
                {
                    instructions.Add(vResult.Load());
                }
                instructions.Add(Create(OpCodes.Ret));

                return instructions;
            });
        }

        private IList<Instruction> SyncIfRewriteArguments(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, ArgumentSimulation[] args, VariableSimulation<TsMethodContext> vContext)
        {
            if (rouMethod.MethodDef.Parameters.Count == 0 || !rouMethod.Features.Contains(Feature.RewriteArgs) || rouMethod.MethodContextOmits.Contains(Omit.Arguments)) return [];

            return vContext.Value.P_RewriteArguments.If(anchor =>
            {
                var instructions = new List<Instruction>();
                var vArguments = tWeavingTarget.M_Proxy.CreateVariable<TsArray>(_typeObjectArrayRef);

                // .var args = _context.Arguments;
                instructions.Add(vArguments.Assign(vContext.Value.P_Arguments));
                for (var i = 0; i < args.Length; i++)
                {
                    // .if (args[i] == null)
                    instructions.Add(vArguments.Value[i].IsNull().If((a1, a2) =>
                    {
                        // ._parameters[i] = default;
                        return args[i].AssignDefault();
                    }, (a1, a2) =>
                    {// .else (args[i] != null)
                        // ._parameters[i] = args[i];
                        return args[i].Assign(vArguments.Value[i]);
                    }));
                }

                return instructions;
            });
        }

        private IList<Instruction> SyncSaveResult(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, VariableSimulation<TsMethodContext> vContext, VariableSimulation? vResult)
        {
            if (vResult == null || !rouMethod.Features.HasIntersection(Feature.OnSuccess | Feature.OnExit) || rouMethod.MethodContextOmits.Contains(Omit.ReturnValue)) return [];

            // .context.ReturnValue  = result;
            return vContext.Value.P_ReturnValue.Assign(vResult);
        }

        private IList<Instruction> SyncRefreshArguments(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, ArgumentSimulation[] args, VariableSimulation<TsMethodContext> vContext, Feature feature)
        {
            if (!rouMethod.Features.Contains(feature | Feature.OnExit | Feature.FreshArgs) || rouMethod.MethodContextOmits.Contains(Omit.Arguments)) return [];

            var count = args.Count(x => x.IsByReference);

            if (count == 0) return [];

            var instructions = new List<Instruction>();

            VariableSimulation<TsArray>? vArguments = null;
            if (count > 1)
            {
                // var arguments = context.Arguments;
                vArguments = tWeavingTarget.M_Proxy.CreateVariable<TsArray>(_typeObjectArrayRef);
                instructions.Add(vArguments.Assign(vContext.Value.P_Arguments));
            }

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.IsByReference) continue;

                // .context.Arguments[i] = arg_i;
                if (vArguments == null)
                {
                    instructions.Add([
                        .. vContext.Value.P_Arguments.Getter!.Call(tWeavingTarget.M_Proxy),
                        .. vContext.Value.P_Arguments.Getter.Result[i].Assign(arg)
                    ]);
                }
                else
                {
                    instructions.Add(vArguments.Value[i].Assign(arg));
                }
            }

            return instructions;
        }

        private IList<Instruction> SynCheckRetry(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, VariableSimulation<TsMethodContext> vContext, SyncContext context, Feature action, bool insideBlock)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (!rouMethod.Features.Contains(Feature.RetryAny | action)) return [];
#pragma warning restore CS0618 // Type or member is obsolete

            // .if (context.RetryCount > 0)
            return vContext.Value.P_RetryCount.Gt(0).If(anchor =>
            {
                // .goto RETRY;
                return [Create(insideBlock ? OpCodes.Leave : OpCodes.Br, context.AnchorRetry)];
            });
        }

        private IList<Instruction> SyncSaveResultIfExceptionHandled(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, VariableSimulation<TsMethodContext> vContext, VariableSimulation? vResult, out VariableSimulation? vExceptionHandled)
        {
            vExceptionHandled = null;
            if (!rouMethod.Features.Contains(Feature.ExceptionHandle) || rouMethod.MethodContextOmits.Contains(Omit.ReturnValue)) return [];

            var instructions = new List<Instruction>();

            vExceptionHandled = tWeavingTarget.M_Proxy.CreateVariable(_typeBoolRef);

            // .exceptionHandled = context.ExceptionHandled;
            instructions.Add(vExceptionHandled.Assign(vContext.Value.P_ExceptionHandled));
            if (vResult != null)
            {
                // .if (exceptionHandled)
                instructions.Add(vExceptionHandled.If(anchor =>
                {
                    // .result = context.ReturnValue;
                    return vResult.Assign(vContext.Value.P_ReturnValue);
                }));
            }

            return instructions;
        }

        private IList<Instruction> SyncReturnIfExceptionHandled(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, VariableSimulation? vExceptionHandled, SyncContext context)
        {
            if (vExceptionHandled == null) return [];

            // .if (exceptionHandled)
            return vExceptionHandled.If(anchor =>
            {
                // return result;
                return [Create(OpCodes.Leave, context.AnchorReturnResult)];
            });
        }

        private IList<Instruction> SyncSaveResult(RouMethod rouMethod, VariableSimulation<TsMethodContext> vContext, VariableSimulation? vResult)
        {
            if (vResult == null || !rouMethod.Features.HasIntersection(Feature.OnSuccess | Feature.OnExit) || rouMethod.MethodContextOmits.Contains(Omit.ReturnValue)) return [];

            // .context.ReturnValue = result;
            return vContext.Value.P_ReturnValue.Assign(vResult);
        }

        private IList<Instruction> SyncSaveResultIfReturnValueReplaced(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, VariableSimulation<TsMethodContext> vContext, VariableSimulation? vResult)
        {
            if (vResult == null || !rouMethod.Features.Contains(Feature.SuccessReplace) || rouMethod.MethodContextOmits.Contains(Omit.ReturnValue)) return [];

            // .if (context.ReturnValueReplaced)
            return vContext.Value.P_ReturnValueReplaced.If(anchor =>
            {
                // .result = context.ReturnValue;
                return vResult.Assign(vContext.Value.P_ReturnValue);
            });
        }

        private IList<Instruction> SyncMosOn(RouMethod rouMethod, TsWeavingTarget tWeavingTarget, VariableSimulation<TsMethodContext> vContext, VariableSimulation<TsMo>[]? vMos, VariableSimulation<TsArray<TsMo>>? vMoArray, Feature feature, Func<TsMo, MethodSimulation> methodFactory)
        {
            return SyncMosOn(rouMethod, tWeavingTarget.M_Proxy, vContext, vMos?.Select(x => x.Value).ToArray(), vMoArray?.Value, feature, methodFactory);
        }

        private IList<Instruction> SyncMosOn(RouMethod rouMethod, MethodSimulation mHost, IParameterSimulation pContext, TsMo[]? tMos, TsArray<TsMo>? tMoArray, Feature feature, Func<TsMo, MethodSimulation> methodFactory)
        {
            if (!rouMethod.Features.Contains(feature)) return [];

            var instructions = new List<Instruction>();

            var reverseCall = feature != Feature.OnEntry && _config.ReverseCallNonEntry;
            var executeSequence = 0;

            if (tMoArray != null && rouMethod.Mos.Length > 31)
            {
                executeSequence = -1;
            }
            for (var i = 0; i < rouMethod.Mos.Length; i++)
            {
                var j = reverseCall ? rouMethod.Mos.Length - i - 1 : i;
                var mo = rouMethod.Mos[j];
                if (!mo.Features.Contains(feature)) continue;

                if (tMos != null)
                {
                    // .mo.OnXxx(context);
                    instructions.Add(methodFactory(tMos[j]).Call(mHost, pContext));
                }
                else if (executeSequence != -1)
                {
                    executeSequence |= 1 << j;
                }
            }

            if (tMoArray != null)
            {
                Eq featureMatch;
                var vFlag = mHost.CreateVariable(_typeIntRef);
                if (executeSequence == -1)
                {
                    // .(moArray[flag].Feature & feature) != 0
                    featureMatch = tMoArray[vFlag].Value.P_Features.And((int)feature).IsEqual(0);
                }
                else
                {
                    // .(executeSequence & flag) != 0
                    featureMatch = vFlag.And(executeSequence).IsEqual(0);
                }
                Instruction updateFlag = Create(OpCodes.Nop), loopFirst = Create(OpCodes.Nop);

                var forInstructions = featureMatch.IfNot(vFlag.Load().Single(), ldFlag =>
                {
                    return [
                        // .moArray[flag].OnXxx(context);
                        .. methodFactory(tMoArray[vFlag].Value).Call(mHost, pContext),
                        // .flag += 1; / flag -= 1;
                        .. vFlag.Assign(target => [ldFlag, Create(OpCodes.Ldc_I4_1), updateFlag]),
                        Create(OpCodes.Br, loopFirst)
                    ];
                });

                if (reverseCall)
                {
                    updateFlag.Set(OpCodes.Sub);

                    // .flag = MO_ARRAY_LENGTH - 1;
                    instructions.Add(vFlag.Assign(rouMethod.Mos.Length - 1));
                    instructions.Add(loopFirst);
                    // .if (flag >= 0) { .. }
                    instructions.Add(vFlag.Lt(0).IfNot(anchor => forInstructions));
                }
                else
                {
                    updateFlag.Set(OpCodes.Add);

                    // .flag = 0;
                    instructions.Add(vFlag.Assign(0));
                    instructions.Add(loopFirst);
                    // .if (flag < MO_ARRAY_LENGTH) { .. }
                    instructions.Add(vFlag.Lt(rouMethod.Mos.Length).If(anchor => forInstructions));
                }
            }

            return instructions;
        }
    }
}