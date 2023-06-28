﻿using Fody;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Rougamo.Fody.Signature;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace Rougamo.Fody.Tests
{
    [Collection("SignatureUsage")]
    public class ExecutionMatchTests
    {
        private readonly ModuleDefinition _moduleDef;
        private readonly MethodDefinition[] _methodDefs;

        public ExecutionMatchTests()
        {
            var parameters = new ReaderParameters
            {
                AssemblyResolver = new TestAssemblyResolver()
            };
            _moduleDef = ModuleDefinition.ReadModule("SignatureUsage.dll", parameters);
            _methodDefs = _moduleDef.GetTypes().SelectMany(x => x.GetMethods()).ToArray();
        }

        [Fact]
        public void AnyMatchTest()
        {
            var matcher1 = PatternParser.Parse("execution(* *(..))");
            var matcher2 = PatternParser.Parse("execution(* *.*(..))");
            var matcher3 = PatternParser.Parse("execution(* *..*.*(..))");

            var actual1 = new List<MethodDefinition>();
            var actual2 = new List<MethodDefinition>();
            var actual3 = new List<MethodDefinition>();
            foreach (var methodDef in _methodDefs)
            {
                var signature = SignatureParser.ParseMethod(methodDef);
                if (matcher1.IsMatch(signature)) actual1.Add(methodDef);
                if (matcher2.IsMatch(signature)) actual2.Add(methodDef);
                if (matcher3.IsMatch(signature)) actual3.Add(methodDef);
            }

            Assert.Equal(_methodDefs, actual1);
            Assert.Equal(_methodDefs, actual2);
            Assert.Equal(_methodDefs, actual3);
        }

        [Fact]
        public void ModifierMatchTest()
        {
            var publicMatcher = PatternParser.Parse("execution(public * *..*(..))");
            var protectedMatcher = PatternParser.Parse("execution(protected * *..*.*(..))");
            var internalMatcher = PatternParser.Parse("execution(internal * *(..))");
            var privateMatcher = PatternParser.Parse("execution(private * *.*(..))");
            var protectedInternalMatcher = PatternParser.Parse("execution(protectedinternal * *(..))");
            var privateProtectedMatcher = PatternParser.Parse("execution(privateprotected * *(..))");
            var nonPublicMatcher = PatternParser.Parse("execution(!public * *(..))");
            var privateOrProtectedMatcher = PatternParser.Parse("execution(private protected * *(..))");
            var nonPublicProtectedMatcher = PatternParser.Parse("execution(!public !protected * *(..))");
            var publicStaticMatcher = PatternParser.Parse("execution(public static * *(..))");
            var nonStaticMatcher = PatternParser.Parse("execution(!static * *(..))");

            // Modifier signature parser had been pass the tests in SignatureBasicTests, so we can use SignatureParser to get modifier here
            var methods = _methodDefs.Select<MethodDefinition, (MethodDefinition def, Modifier modifier)>(x => (x, SignatureParser.ParseMethod(x).Modifiers)).ToArray();
            var expectedPublics = methods.Where(x => x.modifier == Modifier.Public || x.modifier == Modifier.PublicStatic).Select(x => x.def).ToArray();
            var expectedProtecteds = methods.Where(x => x.modifier == Modifier.Protected || x.modifier == Modifier.ProtectedStatic).Select(x => x.def).ToArray();
            var expectedInternals = methods.Where(x => x.modifier == Modifier.Internal || x.modifier == Modifier.InternalStatic).Select(x => x.def).ToArray();
            var expectedPrivates = methods.Where(x => x.modifier == Modifier.Private || x.modifier == Modifier.PrivateStatic).Select(x => x.def).ToArray();
            var expectedProtectedInternals = methods.Where(x => x.modifier == Modifier.ProtectedInternal || x.modifier == Modifier.ProtectedInternalStatic).Select(x => x.def).ToArray();
            var expectedPrivateProtecteds = methods.Where(x => x.modifier == Modifier.PrivateProtected || x.modifier == Modifier.PrivateProtectedStatic).Select(x => x.def).ToArray();
            var expectedNonPublics = methods.Where(x => x.modifier != Modifier.Public && x.modifier != Modifier.PublicStatic).Select(x => x.def).ToArray();
            var expectedPrivateOrProtecteds = methods.Where(x => x.modifier == Modifier.Private || x.modifier == Modifier.PrivateStatic || x.modifier == Modifier.Protected || x.modifier == Modifier.ProtectedStatic).Select(x => x.def).ToArray();
            var expectedNonPublicProtecteds = methods.Where(x => x.modifier != Modifier.Public && x.modifier != Modifier.PublicStatic && x.modifier != Modifier.Protected && x.modifier != Modifier.ProtectedStatic).Select(x => x.def).ToArray();
            var expectedPublicStatics = methods.Where(x => x.modifier == Modifier.PublicStatic).Select(x => x.def).ToArray();
            var expectedNonStatics = methods.Where(x => (x.modifier & Modifier.Static) == 0).Select(x => x.def).ToArray();

            var actualPublics = new List<MethodDefinition>();
            var actualProtecteds = new List<MethodDefinition>();
            var actualInternals = new List<MethodDefinition>();
            var actualPrivates = new List<MethodDefinition>();
            var actualProtectedInternals = new List<MethodDefinition>();
            var actualPrivateProtecteds = new List<MethodDefinition>();
            var actualNonPublics = new List<MethodDefinition>();
            var actualPrivateOrProtecteds = new List<MethodDefinition>();
            var actualNonPublicProtecteds = new List<MethodDefinition>();
            var actualPublicStatics = new List<MethodDefinition>();
            var actualNonStatics = new List<MethodDefinition>();
            foreach (var methodDef in _methodDefs)
            {
                var signature = SignatureParser.ParseMethod(methodDef);
                if (publicMatcher.IsMatch(signature)) actualPublics.Add(methodDef);
                if (protectedMatcher.IsMatch(signature)) actualProtecteds.Add(methodDef);
                if (internalMatcher.IsMatch(signature)) actualInternals.Add(methodDef);
                if (privateMatcher.IsMatch(signature)) actualPrivates.Add(methodDef);
                if (protectedInternalMatcher.IsMatch(signature)) actualProtectedInternals.Add(methodDef);
                if (privateProtectedMatcher.IsMatch(signature)) actualPrivateProtecteds.Add(methodDef);
                if (nonPublicMatcher.IsMatch(signature)) actualNonPublics.Add(methodDef);
                if (privateOrProtectedMatcher.IsMatch(signature)) actualPrivateOrProtecteds.Add(methodDef);
                if (nonPublicProtectedMatcher.IsMatch(signature)) actualNonPublicProtecteds.Add(methodDef);
                if (publicStaticMatcher.IsMatch(signature)) actualPublicStatics.Add(methodDef);
                if (nonStaticMatcher.IsMatch(signature)) actualNonStatics.Add(methodDef);
            }

            Assert.Equal(expectedPublics, actualPublics);
            Assert.Equal(expectedProtecteds, actualProtecteds);
            Assert.Equal(expectedInternals, actualInternals);
            Assert.Equal(expectedPrivates, actualPrivates);
            Assert.Equal(expectedProtectedInternals, actualProtectedInternals);
            Assert.Equal(expectedPrivateProtecteds, actualPrivateProtecteds);
            Assert.Equal(expectedNonPublics, actualNonPublics);
            Assert.Equal(expectedPrivateOrProtecteds, actualPrivateOrProtecteds);
            Assert.Equal(expectedNonPublicProtecteds, actualNonPublicProtecteds);
            Assert.Equal(expectedPublicStatics, actualPublicStatics);
            Assert.Equal(expectedNonStatics, actualNonStatics);
        }

        [Fact]
        public void MethodGenericMatchTest()
        {
            var anyMatcher = PatternParser.Parse("execution(* Public(..))");
            var withGenericMatcher = PatternParser.Parse("execution(* *..Public<..>(..))");
            var withoutGenericMatcher = PatternParser.Parse("execution(* Public<!>(..))");
            var withTwoGenericMatcher = PatternParser.Parse("execution(* *..*.Public<,>(..))");
            var partSpecifiedGenericMatcher = PatternParser.Parse("execution(* *..*.*<T,>(T,*))");
            var allSpecifiedGenericMatcher = PatternParser.Parse("execution(* *..*.*<TA,TB>(TA,Nullable<TB>))");

            var expectedAnyGenerics = _methodDefs.Where(x => x.Name == "Public").ToArray();
            var expectedWithGenerics = _methodDefs.Where(x => x.Name == "Public" && x.HasGenericParameters).ToArray();
            var expectedWithoutGenerics = _methodDefs.Where(x => x.Name == "Public" && !x.HasGenericParameters).ToArray();
            var expectedWithTwoGenerics = _methodDefs.Where(x => x.Name == "Public" && x.GenericParameters.Count == 2).ToArray();
            var expectedPartSpecifiedGenerics = _methodDefs.Where(x => x.GenericParameters.Count == 2 && x.Parameters.Count == 2 && x.Parameters[0].ParameterType == x.GenericParameters[0]).ToArray();
            var expectedAllSpecifiedGenerics = expectedPartSpecifiedGenerics.Where(x => x.Parameters[1].ParameterType is GenericInstanceType git && git.GenericArguments.Count == 1 && git.GenericArguments[0] == x.GenericParameters[1]).ToArray();

            var actualAnyGenerics = new List<MethodDefinition>();
            var actualWithGenerics = new List<MethodDefinition>();
            var actualWithoutGenerics = new List<MethodDefinition>();
            var actualWithTwoGenerics = new List<MethodDefinition>();
            var actualPartSpecifiedGenerics = new List<MethodDefinition>();
            var actualAllSpecifiedGenerics = new List<MethodDefinition>();
            foreach (var methodDef in _methodDefs)
            {
                var signature = SignatureParser.ParseMethod(methodDef);
                if (anyMatcher.IsMatch(signature)) actualAnyGenerics.Add(methodDef);
                if (withGenericMatcher.IsMatch(signature)) actualWithGenerics.Add(methodDef);
                if (withoutGenericMatcher.IsMatch(signature)) actualWithoutGenerics.Add(methodDef);
                if (withTwoGenericMatcher.IsMatch(signature)) actualWithTwoGenerics.Add(methodDef);
                if (partSpecifiedGenericMatcher.IsMatch(signature)) actualPartSpecifiedGenerics.Add(methodDef);
                if (allSpecifiedGenericMatcher.IsMatch(signature)) actualAllSpecifiedGenerics.Add(methodDef);
            }

            Assert.Equal(expectedAnyGenerics, actualAnyGenerics);
            Assert.Equal(expectedWithGenerics, actualWithGenerics);
            Assert.Equal(expectedWithoutGenerics, actualWithoutGenerics);
            Assert.Equal(expectedWithTwoGenerics, actualWithTwoGenerics);
            Assert.Equal(expectedPartSpecifiedGenerics, actualPartSpecifiedGenerics);
            Assert.Equal(expectedAllSpecifiedGenerics, actualAllSpecifiedGenerics);
        }

        [Fact]
        public void NestedTypeMatchTest()
        {
            var deep1Matcher = PatternParser.Parse("execution(public * PublicCls/PublicICls.*(..))");
            var deep2Matcher = PatternParser.Parse("execution(!private * PublicCls/*/PublicDeepCls.*(..))");
            var deep31Matcher = PatternParser.Parse("execution(* */PublicICls/PublicDeepCls/*.*(..))");
            var deep32Matcher = PatternParser.Parse("execution(* */*/*/*.*(..))");

            var expectedDeep1 = _methodDefs.Where(x => x.IsPublic && x.DeclaringType.Name == "PublicICls" && x.DeclaringType.DeclaringType?.Name == "PublicCls").ToArray();
            var expectedDeep2 = _methodDefs.Where(x => !x.IsPrivate && x.DeclaringType.Name == "PublicDeepCls" && x.DeclaringType.DeclaringType?.DeclaringType?.Name == "PublicCls").ToArray();
            var expectedDeep31 = _methodDefs.Where(x => x.DeclaringType.DeclaringType?.Name == "PublicDeepCls" && x.DeclaringType.DeclaringType?.DeclaringType?.Name == "PublicICls" && x.DeclaringType.DeclaringType?.DeclaringType?.DeclaringType != null).ToArray();
            var expectedDeep32 = _methodDefs.Where(x => x.DeclaringType.DeclaringType?.DeclaringType?.DeclaringType != null).ToArray();

            var actual1 = new List<MethodDefinition>();
            var actual2 = new List<MethodDefinition>();
            var actual31 = new List<MethodDefinition>();
            var actual32 = new List<MethodDefinition>();
            foreach (var methodDef in _methodDefs)
            {
                var signature = SignatureParser.ParseMethod(methodDef);
                if (deep1Matcher.IsMatch(signature)) actual1.Add(methodDef);
                if (deep2Matcher.IsMatch(signature)) actual2.Add(methodDef);
                if (deep31Matcher.IsMatch(signature)) actual31.Add(methodDef);
                if (deep32Matcher.IsMatch(signature)) actual32.Add(methodDef);
            }

            Assert.Equal(expectedDeep1, actual1);
            Assert.Equal(expectedDeep2, actual2);
            Assert.Equal(expectedDeep31, actual31);
            Assert.Equal(expectedDeep32, actual32);
        }

        [Fact]
        public void AssignableMatchTest()
        {
            var returnInterfaceMatcher = PatternParser.Parse("execution(SignatureUsage.Assignables.Interface+ *(..))");
            var returnAbstractMatcher = PatternParser.Parse("execution(*..Assignables.AbstractClass+ *(..))");
            var returnBaseMatcher = PatternParser.Parse("execution(*..BaseClass+ *(..))");
            var declaringInterfaceMatcher = PatternParser.Parse("execution(* Int*ace+.*(..))");
            var declaringAbstractMatcher = PatternParser.Parse("execution(* AbstractCl*+.*(..))");
            var declaringBaseMatcher = PatternParser.Parse("execution(* *seClass+.*(..))");

            var expectedReturnInterfaces = _methodDefs.Where(x => !x.IsAbstract && GetInterfaceBaseTypes(x.ReturnType).Any(y => y == "SignatureUsage.Assignables.Interface")).ToArray();
            var expectedReturnAbstracts = _methodDefs.Where(x => !x.IsAbstract && GetInterfaceBaseTypes(x.ReturnType).Any(y => y.EndsWith("Assignables.AbstractClass"))).ToArray();
            var expectedReturnBases = _methodDefs.Where(x => !x.IsAbstract && GetInterfaceBaseTypes(x.ReturnType).Any(y => y.EndsWith("BaseClass"))).ToArray();
            var expectedDeclaringInterfaces = _methodDefs.Where(x => !x.IsAbstract && GetInterfaceBaseTypes(x.DeclaringType).Any(y => StartEndWith(y.Split(new[] { '.' }).Last(), "Int", "ace"))).ToArray();
            var expectedDeclaringAbstracts = _methodDefs.Where(x => !x.IsAbstract && GetInterfaceBaseTypes(x.DeclaringType).Any(y => y.Split(new[] { '.' }).Last().StartsWith("AbstractCl"))).ToArray();
            var expectedDeclaringBases = _methodDefs.Where(x => !x.IsAbstract && GetInterfaceBaseTypes(x.DeclaringType).Any(y => y.Split(new[] { '.' }).Last().EndsWith("seClass"))).ToArray();

            var actualReturnInterfaces = new List<MethodDefinition>();
            var actualReturnAbstracts = new List<MethodDefinition>();
            var actualReturnBases = new List<MethodDefinition>();
            var actualDeclaringInterfaces = new List<MethodDefinition>();
            var actualDeclaringAbstracts = new List<MethodDefinition>();
            var actualDeclaringBases = new List<MethodDefinition>();
            foreach (var methodDef in _methodDefs)
            {
                var signature = SignatureParser.ParseMethod(methodDef);
                if (returnInterfaceMatcher.IsMatch(signature)) actualReturnInterfaces.Add(methodDef);
                if (returnAbstractMatcher.IsMatch(signature)) actualReturnAbstracts.Add(methodDef);
                if (returnBaseMatcher.IsMatch(signature)) actualReturnBases.Add(methodDef);
                if (declaringInterfaceMatcher.IsMatch(signature)) actualDeclaringInterfaces.Add(methodDef);
                if (declaringAbstractMatcher.IsMatch(signature)) actualDeclaringAbstracts.Add(methodDef);
                if (declaringBaseMatcher.IsMatch(signature)) actualDeclaringBases.Add(methodDef);
            }

            Assert.Equal(expectedReturnInterfaces, actualReturnInterfaces);
            Assert.Equal(expectedReturnAbstracts, actualReturnAbstracts);
            Assert.Equal(expectedReturnBases, actualReturnBases);
            Assert.Equal(expectedDeclaringInterfaces, actualDeclaringInterfaces);
            Assert.Equal(expectedDeclaringAbstracts, actualDeclaringAbstracts);
            Assert.Equal(expectedDeclaringBases, actualDeclaringBases);
        }

        [Fact]
        public void NotMatchTest()
        {
            var notReturnNsMatcher = PatternParser.Parse("execution(!*..Assignables..* *(..))");
            var notDeclaringNsMatcher = PatternParser.Parse("execution(* !*..Assignables.*.*(..))");
            var notSpecialReturnMatcher = PatternParser.Parse("execution(!SignatureUsage.DefaultCls *(..))");
            var notNameSpecialDeclaringMatcher = PatternParser.Parse("execution(* !GenericCls.*(..))");

            var expectedNotReturnNses = _methodDefs.Where(x => !x.ReturnType.Namespace.StartsWith("Assignables.") && !x.ReturnType.Namespace.Contains(".Assignables.") && !x.ReturnType.Namespace.EndsWith(".Assignables")).ToArray();
            var expectedNotDeclaringNses = _methodDefs.Where(x => !x.DeclaringType.Namespace.EndsWith(".Assignables") && x.DeclaringType.Namespace != "Assignables").ToArray();
            var expectedNotSpecialReturns = _methodDefs.Where(x => x.ReturnType.FullName != "SignatureUsage.DefaultCls" && !x.ReturnType.FullName.StartsWith("SignatureUsage.DefaultCls`")).ToArray();
            var expectedNotNameSpecialDeclarings = _methodDefs.Where(x => x.DeclaringType.Name != "GenericCls" && !x.DeclaringType.Name.StartsWith("GenericCls`")).ToArray();

            var actualNotReturnNses = new List<MethodDefinition>();
            var actualNotDeclaringNses = new List<MethodDefinition>();
            var actualNotSpecialReturns = new List<MethodDefinition>();
            var actualNotNameSpecialDeclarings = new List<MethodDefinition>();
            foreach (var methodDef in _methodDefs)
            {
                var signature = SignatureParser.ParseMethod(methodDef);
                if (notReturnNsMatcher.IsMatch(signature)) actualNotReturnNses.Add(methodDef);
                if (notDeclaringNsMatcher.IsMatch(signature)) actualNotDeclaringNses.Add(methodDef);
                if (notSpecialReturnMatcher.IsMatch(signature)) actualNotSpecialReturns.Add(methodDef);
                if (notNameSpecialDeclaringMatcher.IsMatch(signature)) actualNotNameSpecialDeclarings.Add(methodDef);
            }

            Assert.Equal(expectedNotReturnNses, actualNotReturnNses);
            Assert.Equal(expectedNotDeclaringNses, actualNotDeclaringNses);
            Assert.Equal(expectedNotSpecialReturns, actualNotSpecialReturns);
            Assert.Equal(expectedNotNameSpecialDeclarings, actualNotNameSpecialDeclarings);
        }

        [Fact]
        public void NotDeclaringMethodTest()
        {
            var notAnyMatcher1 = PatternParser.Parse("execution(* !*(..))");
            var notAnyMatcher2 = PatternParser.Parse("execution(* !*.*(..))");
            var notAnyMatcher3 = PatternParser.Parse("execution(* !*..*.*(..))");
            var notDeclaringMatcher1 = PatternParser.Parse("execution(* !SignatureUsage.DefaultCls.*(..))");
            var notDeclaringMatcher2 = PatternParser.Parse("execution(* !SignatureUsage.Assignables.Interface+.*(..))");
            var notMethodMatcher1 = PatternParser.Parse("execution(* !*.Public(..))");
            var notMethodMatcher2 = PatternParser.Parse("execution(* !*..*.Protected(..))");
            var notSpecialMatcher = PatternParser.Parse("execution(* !SignatureUsage.Assignables.ImplInterfaceAbstractClass.GetInterface(..))");

            var expectedNotAny1 = _methodDefs;
            var expectedNotAny2 = _methodDefs;
            var expectedNotAny3 = _methodDefs;
            var expectedNotDeclaring1 = _methodDefs.Where(x => x.DeclaringType.FullName != "SignatureUsage.DefaultCls").ToArray();
            var expectedNotDeclaring2 = _methodDefs.Where(x => !GetInterfaceBaseTypes(x.DeclaringType).Any(y => y == "SignatureUsage.Assignables.Interface")).ToArray();
            var expectedNotMethod1 = _methodDefs.Where(x => x.Name != "Public").ToArray();
            var expectedNotMethod2 = _methodDefs.Where(x => x.Name != "Protected").ToArray();
            var expectedNotSpecial = _methodDefs.Where(x => x.DeclaringType.FullName != "SignatureUsage.Assignables.ImplInterfaceAbstractClass" || x.Name != "GetInterface").ToArray();

            var actualNotAny1 = new List<MethodDefinition>();
            var actualNotAny2 = new List<MethodDefinition>();
            var actualNotAny3 = new List<MethodDefinition>();
            var actualNotDeclaring1 = new List<MethodDefinition>();
            var actualNotDeclaring2 = new List<MethodDefinition>();
            var actualNotMethod1 = new List<MethodDefinition>();
            var actualNotMethod2 = new List<MethodDefinition>();
            var actualNotSpecial = new List<MethodDefinition>();
            foreach (var methodDef in _methodDefs)
            {
                var signature = SignatureParser.ParseMethod(methodDef);
                if (notAnyMatcher1.IsMatch(signature)) actualNotAny1.Add(methodDef);
                if (notAnyMatcher2.IsMatch(signature)) actualNotAny2.Add(methodDef);
                if (notAnyMatcher3.IsMatch(signature)) actualNotAny3.Add(methodDef);
                if (notDeclaringMatcher1.IsMatch(signature)) actualNotDeclaring1.Add(methodDef);
                if (notDeclaringMatcher2.IsMatch(signature)) actualNotDeclaring2.Add(methodDef);
                if (notMethodMatcher1.IsMatch(signature)) actualNotMethod1.Add(methodDef);
                if (notMethodMatcher2.IsMatch(signature)) actualNotMethod2.Add(methodDef);
                if (notSpecialMatcher.IsMatch(signature)) actualNotSpecial.Add(methodDef);
            }

            Assert.Equal(expectedNotAny1, actualNotAny1);
            Assert.Equal(expectedNotAny2, actualNotAny2);
            Assert.Equal(expectedNotAny3, actualNotAny3);
            Assert.Equal(expectedNotDeclaring1, actualNotDeclaring1);
            Assert.Equal(expectedNotDeclaring2, actualNotDeclaring2);
            Assert.Equal(expectedNotMethod1, actualNotMethod1);
            Assert.Equal(expectedNotMethod2, actualNotMethod2);
            Assert.Equal(expectedNotSpecial, actualNotSpecial);
        }

        [Fact]
        public void OrMatchTest()
        {
            var returnStringOrVoidMatcher = PatternParser.Parse("execution(string||void *(..))");
            var returnIntDoubleOrDecimalMatcher = PatternParser.Parse("execution(int||double||decimal *(..))");
            var declaringDefaultOrPublicMatcher = PatternParser.Parse("execution(* SignatureUsage.DefaultCls||SignatureUsage.PublicCls.*(..))");
            var declaringAssignableInterfaceOrAbstractClassMatcher = PatternParser.Parse("execution(* SignatureUsage.Assignables.Interface+||SignatureUsage.Assignables.AbstractClass+.*(..))");

            var expectedReturnStringOrVoids = _methodDefs.Where(x => new[] { typeof(string).FullName, typeof(void).FullName }.Contains(x.ReturnType.FullName)).ToArray();
            var expectedReturnIntDoubleOrDecimals = _methodDefs.Where(x => new[] { typeof(int).FullName, typeof(double).FullName, typeof(decimal).FullName }.Contains(x.ReturnType.FullName)).ToArray();
            var expectedDeclaringDefaultOrPublics = _methodDefs.Where(x => new[] { "SignatureUsage.DefaultCls", "SignatureUsage.PublicCls" }.Contains(x.DeclaringType.FullName)).ToArray();
            var expectedDeclaringAssignableInterfaceOrAbstractClasses = _methodDefs.Where(x => GetInterfaceBaseTypes(x.DeclaringType).Any(x => x == "SignatureUsage.Assignables.Interface" || x == "SignatureUsage.Assignables.AbstractClass")).ToArray();

            var actualReturnStringOrVoids = new List<MethodDefinition>();
            var actualReturnIntDoubleOrDecimals = new List<MethodDefinition>();
            var actualDeclaringDefaultOrPublics = new List<MethodDefinition>();
            var actualDeclaringAssignableInterfaceOrAbstractClasses = new List<MethodDefinition>();
            foreach (var methodDef in _methodDefs)
            {
                var signature = SignatureParser.ParseMethod(methodDef);
                if (returnStringOrVoidMatcher.IsMatch(signature)) actualReturnStringOrVoids.Add(methodDef);
                if (returnIntDoubleOrDecimalMatcher.IsMatch(signature)) actualReturnIntDoubleOrDecimals.Add(methodDef);
                if (declaringDefaultOrPublicMatcher.IsMatch(signature)) actualDeclaringDefaultOrPublics.Add(methodDef);
                if (declaringAssignableInterfaceOrAbstractClassMatcher.IsMatch(signature)) actualDeclaringAssignableInterfaceOrAbstractClasses.Add(methodDef);
            }

            Assert.Equal(expectedReturnStringOrVoids, actualReturnStringOrVoids);
            Assert.Equal(expectedReturnIntDoubleOrDecimals, actualReturnIntDoubleOrDecimals);
            Assert.Equal(expectedDeclaringDefaultOrPublics, actualDeclaringDefaultOrPublics);
            Assert.Equal(expectedDeclaringAssignableInterfaceOrAbstractClasses, actualDeclaringAssignableInterfaceOrAbstractClasses);
        }

        [Fact]
        public void TupleMatchTest()
        {
            var returnMatcher1 = PatternParser.Parse("execution((int, string) *(..))");
            var returnMatcher2 = PatternParser.Parse("execution((*,decimal) *(..))");
            var argumentMatcher1 = PatternParser.Parse("execution(* *((*, *)))");
            var argumentMatcher2 = PatternParser.Parse("execution(* *((System.Numerics.BigInteger,*)))");

            var expectedReturns1 = _methodDefs.Where(x => GetTupleFullNames(typeof(int).FullName, typeof(string).FullName).Contains(x.ReturnType.FullName)).ToArray();
            var expectedReturns2 = _methodDefs.Where(x => x.ReturnType.Namespace == "System" && GetTupleNames(2).Contains(x.ReturnType.Name) && ((GenericInstanceType)x.ReturnType).GenericArguments[1].FullName == typeof(decimal).FullName).ToArray();
            var expectedArguments1 = _methodDefs.Where(x => x.Parameters.Count == 1 && x.Parameters[0].ParameterType.Namespace == "System" && GetTupleNames(2).Contains(x.Parameters[0].ParameterType.Name)).ToArray();
            var expectedArguments2 = expectedArguments1.Where(x => ((GenericInstanceType)x.Parameters[0].ParameterType).GenericArguments[0].FullName == typeof(BigInteger).FullName).ToArray();

            var actualReturns1 = new List<MethodDefinition>();
            var actualReturns2 = new List<MethodDefinition>();
            var actualArguments1 = new List<MethodDefinition>();
            var actualArguments2 = new List<MethodDefinition>();
            foreach (var methodDef in _methodDefs)
            {
                var signature = SignatureParser.ParseMethod(methodDef);
                if (returnMatcher1.IsMatch(signature)) actualReturns1.Add(methodDef);
                if (returnMatcher2.IsMatch(signature)) actualReturns2.Add(methodDef);
                if (argumentMatcher1.IsMatch(signature)) actualArguments1.Add(methodDef);
                if (argumentMatcher2.IsMatch(signature)) actualArguments2.Add(methodDef);
            }

            Assert.Equal(expectedReturns1, actualReturns1);
            Assert.Equal(expectedReturns2, actualReturns2);
            Assert.Equal(expectedArguments1, actualArguments1);
            Assert.Equal(expectedArguments2, actualArguments2);
        }

        [Fact]
        public void NullableMatchTest()
        {
            var returnMatcher1 = PatternParser.Parse("execution(int? *(..))");
            var parameterMatcher1 = PatternParser.Parse("execution(* *(Xyz?,*))");
            var parameterMatcher2 = PatternParser.Parse("execution(* *((double?,*)))");

            var expectedReturns1 = _methodDefs.Where(x => x.ReturnType.FullName == "System.Nullable`1<System.Int32>").ToArray();
            var expectedParameters1 = _methodDefs.Where(x => x.Parameters.Count == 2 && IsMatch(x.Parameters[0].ParameterType, y => y.Namespace == "System" && y.Name == "Nullable`1" && ((GenericInstanceType)y).GenericArguments[0].Name == "Xyz")).ToArray();
            var expectedParameters2 = _methodDefs.Where(x => x.Parameters.Count == 1 && IsMatch(x.Parameters[0].ParameterType, y => y.Namespace == "System" && GetTupleNames(2).Contains(y.Name) && IsMatch(((GenericInstanceType)y).GenericArguments[0], z => z.FullName == "System.Nullable`1<System.Double>"))).ToArray();

            var actualReturns1 = new List<MethodDefinition>();
            var actualParameters1 = new List<MethodDefinition>();
            var actualParameters2 = new List<MethodDefinition>();
            foreach (var methodDef in _methodDefs)
            {
                var signature = SignatureParser.ParseMethod(methodDef);
                if (returnMatcher1.IsMatch(signature)) actualReturns1.Add(methodDef);
                if (parameterMatcher1.IsMatch(signature)) actualParameters1.Add(methodDef);
                if (parameterMatcher2.IsMatch(signature)) actualParameters2.Add(methodDef);
            }

            Assert.Equal(expectedReturns1, actualReturns1);
            Assert.Equal(expectedParameters1, actualParameters1);
            Assert.Equal(expectedParameters2, actualParameters2);
        }

        private string[] GetInterfaceBaseTypes(TypeReference typeRef)
        {
            var typeDef = typeRef.Resolve();
            if (typeDef == null) return Array.Empty<string>();

            var list = new List<string>();

            foreach (var item in typeDef.Interfaces)
            {
                list.Add(item.InterfaceType.FullName);
            }

            do
            {
                typeDef = typeRef.Resolve();
                list.Add(typeDef.FullName);
            } while ((typeRef = typeDef.BaseType) != null);

            return list.ToArray();
        }

        private bool StartEndWith(string str, string starts, string ends) => str.StartsWith(starts) && str.EndsWith(ends);

        private bool IsMatch(TypeReference typeRef, Func<TypeReference, bool> predicate) => predicate(typeRef);

        private string[] GetTupleNames(int count) => new[] { $"Tuple`{count}", $"ValueTuple`{count}" };

        private string[] GetTupleFullNames(params string[] items) => new[] { $"System.Tuple`{items.Length}<{string.Join(",", items)}>", $"System.ValueTuple`{items.Length}<{string.Join(",", items)}>" };
    }
}
