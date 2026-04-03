using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ParameterAssignmentAnaylyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ParameterAssignmentAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "PA0001";

        private static readonly LocalizableString Title = "Parameter reassignment";
        private static readonly LocalizableString MessageFormat = "Parameter '{0}' is assigned to. Avoid modifying method parameters.";
        private static readonly LocalizableString Description = "Assigning to method parameters can be confusing; prefer using a local variable instead.";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            // Conservative settings for performance and correctness
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Watch for assignment expressions (including +=, -=, etc.)
            context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression, SyntaxKind.AddAssignmentExpression,
                SyntaxKind.SubtractAssignmentExpression, SyntaxKind.MultiplyAssignmentExpression, SyntaxKind.DivideAssignmentExpression,
                SyntaxKind.ModuloAssignmentExpression, SyntaxKind.AndAssignmentExpression, SyntaxKind.ExclusiveOrAssignmentExpression,
                SyntaxKind.LeftShiftAssignmentExpression, SyntaxKind.RightShiftAssignmentExpression);

            // Watch for ++ and -- operators (prefix and postfix)
            context.RegisterSyntaxNodeAction(AnalyzePrefixUnary, SyntaxKind.PreIncrementExpression, SyntaxKind.PreDecrementExpression);
            context.RegisterSyntaxNodeAction(AnalyzePostfixUnary, SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression);
        }

        private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
        {
            var assignment = (AssignmentExpressionSyntax)context.Node;
            var left = assignment.Left;

            // We only care about simple identifier names (parameters are referenced by identifier)
            if (left is IdentifierNameSyntax identifier)
            {
                var symbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;

                if (symbol is IParameterSymbol parameterSymbol)
                {
                    // Do not report for ref or out parameters
                    if (parameterSymbol.RefKind != RefKind.None)
                        return;
                // Report diagnostic on the identifier token (usage site)
                var diag = Diagnostic.Create(Rule, identifier.GetLocation(), parameterSymbol.Name);
                    context.ReportDiagnostic(diag);
                }
            }
        }

        private static void AnalyzePrefixUnary(SyntaxNodeAnalysisContext context)
        {
            var expr = (PrefixUnaryExpressionSyntax)context.Node;
            if (expr.Operand is IdentifierNameSyntax identifier)
            {
                var symbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol is IParameterSymbol parameterSymbol)
                {
                    // Do not report for ref or out parameters
                    if (parameterSymbol.RefKind != RefKind.None)
                        return;
                    var diag = Diagnostic.Create(Rule, identifier.GetLocation(), parameterSymbol.Name);
                    context.ReportDiagnostic(diag);
                }
            }
        }

        private static void AnalyzePostfixUnary(SyntaxNodeAnalysisContext context)
        {
            var expr = (PostfixUnaryExpressionSyntax)context.Node;
            if (expr.Operand is IdentifierNameSyntax identifier)
            {
                var symbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol is IParameterSymbol parameterSymbol)
                {
                    // Do not report for ref or out parameters
                    if (parameterSymbol.RefKind != RefKind.None)
                        return;
                    var diag = Diagnostic.Create(Rule, identifier.GetLocation(), parameterSymbol.Name);
                    context.ReportDiagnostic(diag);
                }
            }
        }
    }
}
