﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using Rougamo.Fody.Simulations.Parameters;
using System.Collections.Generic;

namespace Rougamo.Fody.Simulations
{
    internal class Eq(ILoadable value1, ILoadable value2) : ILoadable
    {
        public TypeReference TypeRef => GlobalRefs.TrBool;

        public IList<Instruction> Load()
        {
            return [.. value1.Load(), .. value2.Load(), Instruction.Create(OpCodes.Ceq)];
        }
    }

    internal static class EqExtensions
    {
        public static Eq Equals(this ILoadable value1, ILoadable value2) => new(value1, value2);

        public static Eq IsNull(this ILoadable value) => new(value, new Null());
    }
}
