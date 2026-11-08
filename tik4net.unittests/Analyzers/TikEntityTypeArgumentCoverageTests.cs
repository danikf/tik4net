using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Analyzers;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests.Analyzers
{
    /// <summary>
    /// Holds the shipping assemblies to what TIK004 keys on: every public generic method that reaches the entity
    /// metadata must take its entity as a type parameter named <see cref="TikEntityTypeArgumentAnalyzer.EntityTypeParameterName"/>
    /// in a namespace <see cref="TikEntityTypeArgumentAnalyzer.IsMapperNamespace"/> accepts, or the analyzer does not
    /// see a call to it.
    /// </summary>
    /// <remarks>
    /// "Reaches" is read from the IL: a method that calls, directly or through other tik4net code (lambdas, local
    /// functions, async and iterator state machines included), into <see cref="TikEntityMetadataCache"/>, the
    /// <see cref="TikEntityMetadata"/> constructor, or <c>GetCustomAttribute&lt;TikEntityAttribute&gt;</c>.
    /// </remarks>
    [TestClass]
    public class TikEntityTypeArgumentCoverageTests
    {
        private static readonly Assembly[] Shipping =
        {
            typeof(ITikConnection).Assembly,          // tik4net
            typeof(TikEntityMetadataCache).Assembly,  // tik4net.objects
            typeof(TikFakeConnection).Assembly,       // tik4net.testing
            typeof(tik4net.Ssh.SshConnection).Assembly,
        };

        [TestMethod]
        public void EveryPublicGenericThatReachesTheMetadataIsCoveredByTik004()
        {
            var reaching = PublicGenericMethods().Where(m => new Reachability().Reaches(m)).ToList();

            // Not vacuous: the IL walk finds the mapper's well-known entry points, each by a different route.
            foreach (var expected in new[] { "LoadAll", "LoadAllAsync", "EntityToString", "EnsureStrict", "Has",
                                             "WithEntities", "GetMetadata", "CreateMerge", "DescribeMenu", "LoadListenAsync" })
                Assert.IsTrue(reaching.Any(m => m.Name == expected), "the IL walk no longer finds " + expected);

            var uncovered = reaching
                .Where(m => !TikEntityTypeArgumentAnalyzer.IsMapperNamespace(m.DeclaringType!.Namespace)
                         || !m.GetGenericArguments().Any(a => a.Name == TikEntityTypeArgumentAnalyzer.EntityTypeParameterName))
                .Select(m => m.DeclaringType!.FullName + "." + m.Name + "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">")
                .Distinct()
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            Assert.AreEqual(0, uncovered.Count,
                "These public generics reach the entity metadata but TIK004 does not key on them; name the entity type "
                + "parameter '" + TikEntityTypeArgumentAnalyzer.EntityTypeParameterName + "' (or widen the analyzer):\n"
                + string.Join("\n", uncovered));
        }

        [TestMethod]
        public void AGenericHandedTheMetadataDoesNotReachIt()
        {
            // TIK004 skips a method with a TikEntityMetadata parameter (the change-tracker verbs) because the caller supplies
            // the metadata; that holds only while such a method never resolves it from its type argument.
            var handed = PublicGenericMethods()
                .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(TikEntityMetadata)))
                .ToList();

            Assert.IsTrue(handed.Any(m => m.Name == "HasChanges"), "the change tracker's verbs are no longer found");
            var reaching = handed.Where(m => new Reachability().Reaches(m))
                .Select(m => m.DeclaringType!.FullName + "." + m.Name).ToList();
            Assert.AreEqual(0, reaching.Count, "These take a TikEntityMetadata yet resolve one from their type argument, so "
                + "TIK004 must not skip them:\n" + string.Join("\n", reaching));
        }

        private static IEnumerable<MethodInfo> PublicGenericMethods()
            => Shipping.SelectMany(a => a.GetExportedTypes())
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => m.IsGenericMethodDefinition);

        /// <summary>A walk over the IL of tik4net's own methods, from one public method to the metadata sinks.</summary>
        private sealed class Reachability
        {
            private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (OpCode)f.GetValue(null)!)
                .ToDictionary(o => o.Value);

            private readonly HashSet<(Module, int)> _visited = new HashSet<(Module, int)>();

            public bool Reaches(MethodBase start)
            {
                var pending = new Stack<MethodBase>();
                pending.Push(start);
                while (pending.Count > 0)
                {
                    var method = pending.Pop();
                    if (!_visited.Add((method.Module, method.MetadataToken)))
                        continue;
                    foreach (var callee in Callees(method))
                    {
                        if (IsSink(callee))
                            return true;
                        if (IsOurs(callee))
                            pending.Push(callee);
                    }
                }
                return false;
            }

            private static bool IsOurs(MethodBase method)
                => method.Module.Assembly.GetName().Name!.StartsWith("tik4net", StringComparison.Ordinal);

            private static bool IsSink(MethodBase method)
            {
                var type = method.DeclaringType;
                if (type == typeof(TikEntityMetadataCache) || (type == typeof(TikEntityMetadata) && method.IsConstructor))
                    return true;
                return method is MethodInfo info && info.IsGenericMethod && info.Name == "GetCustomAttribute"
                    && info.GetGenericArguments().Contains(typeof(TikEntityAttribute));
            }

            private static IEnumerable<MethodBase> Callees(MethodBase method)
            {
                // An async or iterator method's body is its state machine's MoveNext.
                foreach (var machine in method.GetCustomAttributes<StateMachineAttribute>())
                {
                    var moveNext = machine.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (moveNext != null)
                        yield return moveNext;
                }

                byte[]? il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                catch (Exception e) when (e is InvalidOperationException || e is NotSupportedException) { il = null; }
                if (il == null)
                    yield break;

                Type[]? typeArgs = method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
                Type[]? methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

                int i = 0;
                while (i < il.Length)
                {
                    short value = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
                    i += il[i] == 0xFE ? 2 : 1;
                    var opCode = OpCodesByValue[value];
                    if (opCode.OperandType == OperandType.InlineMethod)
                    {
                        MethodBase? callee = null;
                        try { callee = method.Module.ResolveMethod(BitConverter.ToInt32(il, i), typeArgs, methodArgs); }
                        catch (ArgumentException) { }
                        if (callee != null)
                            yield return Definition(callee);
                    }
                    i += OperandSize(opCode, il, i);
                }
            }

            /// <summary>The definition behind a constructed generic: its body is the one to read.</summary>
            private static MethodBase Definition(MethodBase method)
            {
                if (IsSink(method) || !IsOurs(method))
                    return method;
                return method.Module.ResolveMethod(method.MetadataToken) ?? method;
            }

            private static int OperandSize(OpCode opCode, byte[] il, int at)
            {
                switch (opCode.OperandType)
                {
                    case OperandType.InlineNone: return 0;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: return 1;
                    case OperandType.InlineVar: return 2;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: return 8;
                    case OperandType.InlineSwitch: return 4 + 4 * BitConverter.ToInt32(il, at);
                    default: return 4;
                }
            }
        }
    }
}
