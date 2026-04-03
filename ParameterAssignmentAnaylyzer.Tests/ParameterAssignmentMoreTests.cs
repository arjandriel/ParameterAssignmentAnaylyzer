using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing.XUnit;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace ParameterAssignmentAnaylyzer.Tests
{
    using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
        ParameterAssignmentAnaylyzer.ParameterAssignmentAnalyzer,
        ParameterAssignmentAnaylyzer.ParameterAssignmentCodeFixProvider>;

    public class ParameterAssignmentMoreTests
    {
        [Fact]
        public async Task PrefixIncrement_ReplacedWithLocal()
        {
            var testCode = @"class C { void M(int x) { ++x; var y = x; } }";
            var fixedCode = @"class C { void M(int x) { var xLocal = x; ++xLocal; var y = xLocal; } }";

            var test = new CSharpCodeFixTest<ParameterAssignmentAnaylyzer.ParameterAssignmentAnalyzer, ParameterAssignmentAnaylyzer.ParameterAssignmentCodeFixProvider, XUnitVerifier>
            {
                TestCode = testCode,
                FixedCode = fixedCode,
            };

            await test.RunAsync();
        }

        [Fact]
        public async Task CompoundAssignment_ReplacedWithLocal()
        {
            var testCode = @"class C { void M(int x) { x += 2; var y = x; } }";
            var fixedCode = @"class C { void M(int x) { var xLocal = x; xLocal += 2; var y = xLocal; } }";

            var test = new CSharpCodeFixTest<ParameterAssignmentAnaylyzer.ParameterAssignmentAnalyzer, ParameterAssignmentAnaylyzer.ParameterAssignmentCodeFixProvider, XUnitVerifier>
            {
                TestCode = testCode,
                FixedCode = fixedCode,
            };

            await test.RunAsync();
        }

        [Fact]
        public async Task OutRefParameters_NotFixedOrReported()
        {
            var testCode = @"class C { void M(ref int x) { x = 1; } void N(out int y) { y = 2; } }";

            var test = new CSharpAnalyzerTest<ParameterAssignmentAnaylyzer.ParameterAssignmentAnalyzer, XUnitVerifier>
            {
                TestCode = testCode,
            };

            await test.RunAsync();
        }
    }
}
