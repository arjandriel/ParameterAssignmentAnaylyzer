using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing.XUnit;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;

namespace ParameterAssignmentAnaylyzer.Tests
{
    public class ParameterAssignmentTests
    {
        [Fact]
        public async Task ReportsDiagnostic_ForSimpleAssignment()
        {
            var testCode = @"class C { void M(int x) { [|x|] = 1; } }";

            var test = new CSharpAnalyzerTest<ParameterAssignmentAnaylyzer.ParameterAssignmentAnalyzer, XUnitVerifier>
            {
                TestCode = testCode,
            };

            await test.RunAsync();
        }

        [Fact]
        public async Task ReportsDiagnostic_ForPrefixIncrement()
        {
            var testCode = @"class C { void M(int x) { ++[|x|]; var y = x; } }";

            var test = new CSharpAnalyzerTest<ParameterAssignmentAnaylyzer.ParameterAssignmentAnalyzer, XUnitVerifier>
            {
                TestCode = testCode,
            };

            await test.RunAsync();
        }

        [Fact]
        public async Task ReportsDiagnostic_ForCompoundAssignment()
        {
            var testCode = @"class C { void M(int x) { [|x|] += 2; var y = x; } }";

            var test = new CSharpAnalyzerTest<ParameterAssignmentAnaylyzer.ParameterAssignmentAnalyzer, XUnitVerifier>
            {
                TestCode = testCode,
            };

            await test.RunAsync();
        }

        [Fact]
        public async Task NoDiagnostics_ForRefOutParameters()
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
