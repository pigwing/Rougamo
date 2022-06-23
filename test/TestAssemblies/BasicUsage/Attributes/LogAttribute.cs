﻿using Rougamo.Context;
using System;

namespace BasicUsage.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class LogAttribute : Rougamo.MoAttribute
    {
        public override void OnEntry(MethodContext context)
        {
        }

        public override void OnException(MethodContext context)
        {
            context.HandledException(this, null);
        }

        public override void OnExit(MethodContext context)
        {
        }

        public override void OnSuccess(MethodContext context)
        {
        }
    }
}
