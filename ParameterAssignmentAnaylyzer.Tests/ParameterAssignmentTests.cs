using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing.XUnit;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;

using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    ParameterAssignmentAnaylyzer.ParameterAssignmentAnalyzer,
    ParameterAssignmentAnaylyzer.ParameterAssignmentCodeFixProvider>;

namespace ParameterAssignmentAnaylyzer.Tests
{
    public class ParameterAssignmentTests
    {
        [Fact]
        public async Task ReportsDiagnosticAndAppliesFix_ForSimpleAssignment()
        {
            var testCode = @"class C { void M(int x) { x = 1; } }";
            var fixedCode = @"class C { void M(int x) { var xLocal = 1; } }";

            var test = new CSharpCodeFixTest<ParameterAssignmentAnaylyzer.ParameterAssignmentAnalyzer, ParameterAssignmentAnaylyzer.ParameterAssignmentCodeFixProvider, XUnitVerifier>
            {
                TestCode = testCode,
                FixedCode = fixedCode,
            };

            await test.RunAsync();
        }
    }
}
