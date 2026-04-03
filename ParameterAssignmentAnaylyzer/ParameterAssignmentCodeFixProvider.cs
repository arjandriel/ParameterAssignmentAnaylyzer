using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Formatting;

namespace ParameterAssignmentAnaylyzer
{
    public class ParameterAssignmentCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ParameterAssignmentAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null)
                return;

            var diagnostic = context.Diagnostics.First();

            // Always register the code fix; resolution of the parameter symbol will be done when the action runs
            context.RegisterCodeFix(
                CodeAction.Create("Introduce local and use it instead of modifying parameter",
                    c => ApplyFixAsync(context.Document, diagnostic, c), equivalenceKey: "IntroduceLocalAndRedirect"),
                diagnostic);
        }

        private async Task<Document> ApplyFixAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null)
                return document;

            var node = root.FindNode(diagnostic.Location.SourceSpan);

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel == null)
                return document;

            IParameterSymbol? symbol = null;
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax paramSyntax)
            {
                symbol = semanticModel.GetDeclaredSymbol(paramSyntax, cancellationToken) as IParameterSymbol;
            }
            else
            {
                symbol = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol as IParameterSymbol;
            }

            if (symbol == null)
                return document;

            return await ReplaceParameterWithLocalAsync(document, node, symbol, cancellationToken).ConfigureAwait(false);
        }

        private async Task<Document> ReplaceParameterWithLocalAsync(Document document, SyntaxNode node, IParameterSymbol parameterSymbol, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null)
                return document;

            // Debug output to help test runs show what the code fix is operating on
            try
            {
                var originalText = (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
                System.Console.WriteLine("[CodeFix] original document:\n" + originalText);
            }
            catch { }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel == null)
                return document;

            // Find containing method, constructor, or local function
            var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            var localFunc = node.FirstAncestorOrSelf<LocalFunctionStatementSyntax>();
            var ctor = node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>();

            BlockSyntax body = null;
            SyntaxNode container = null;

            if (method != null && method.Body != null) { body = method.Body; container = method; }
            else if (localFunc != null && localFunc.Body != null) { body = localFunc.Body; container = localFunc; }
            else if (ctor != null && ctor.Body != null) { body = ctor.Body; container = ctor; }
            else
            {
                // Not supported (e.g., expression-bodied), fallback to no-op
                return document;
            }

            // Collect identifier nodes in the body that refer to the parameter
            var identifiers = body.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(id =>
                {
                    var sym = semanticModel.GetSymbolInfo(id, cancellationToken).Symbol;
                    return SymbolEqualityComparer.Default.Equals(sym, parameterSymbol);
                })
                .ToList();

            if (!identifiers.Any())
                return document;

            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

            // Create unique local name
            var baseLocalName = parameterSymbol.Name + "Local";
            var localName = baseLocalName;
            int suffix = 1;
            while (semanticModel.LookupSymbols(container.SpanStart, name: localName).Any())
            {
                localName = baseLocalName + suffix.ToString();
                suffix++;
            }

            // Strategy:
            // - If there's a simple assignment statement that assigns directly to the parameter (e.g., "x = 1;"),
            //   replace that statement with a local declaration initialized with the RHS ("var xLocal = 1;")
            //   and replace other uses of the parameter with the local.
            // - Otherwise (compound assignments, ++/--, etc.), insert a local at the start initialized from the parameter
            //   ("var xLocal = x;") and replace uses with the local.

            // Look for a simple assignment statement that assigns to the parameter
            ExpressionStatementSyntax assignmentStatementToReplace = null;
            AssignmentExpressionSyntax simpleAssignment = null;

            foreach (var id in identifiers)
            {
                var parentAssign = id.Parent as AssignmentExpressionSyntax;
                if (parentAssign != null && parentAssign.Kind() == SyntaxKind.SimpleAssignmentExpression)
                {
                    // Ensure the identifier is the left side of the assignment and the assignment is a standalone statement
                    if (parentAssign.Left == id && parentAssign.Parent is ExpressionStatementSyntax exprStmt)
                    {
                        assignmentStatementToReplace = exprStmt;
                        simpleAssignment = parentAssign;
                        break;
                    }
                }
            }

            // Create the local declaration node to insert or use when replacing assignment
            LocalDeclarationStatementSyntax localDecl;

            if (assignmentStatementToReplace != null && simpleAssignment != null)
            {
                var rhs = simpleAssignment.Right;
                localDecl = SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(localName))
                        .WithInitializer(SyntaxFactory.EqualsValueClause(rhs)))))
                    .WithLeadingTrivia(assignmentStatementToReplace.GetLeadingTrivia())
                    .WithTrailingTrivia(assignmentStatementToReplace.GetTrailingTrivia());

                // Replace the assignment statement with the local declaration using a direct root replacement
                var newRoot = root.ReplaceNode(assignmentStatementToReplace, localDecl);

                // Remove any identifiers that were part of the replaced statement
                var remainingIdentifiers = identifiers.Where(id => !assignmentStatementToReplace.Span.Contains(id.Span)).ToList();

                // Replace remaining identifier usages with the local identifier
                var newId = SyntaxFactory.IdentifierName(localName);
                var rootAfterReplacements = newRoot.ReplaceNodes(remainingIdentifiers, (oldNode, _) => newId.WithTriviaFrom(oldNode));

                // Create a document from the changed root
                var docWithRoot = document.WithSyntaxRoot(rootAfterReplacements);
                var formattedDoc = await Formatter.FormatAsync(docWithRoot, cancellationToken: cancellationToken).ConfigureAwait(false);

                // Write fixed document text to disk for inspection during tests
                try
                {
                    var text = (await formattedDoc.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
                    var outPath = System.IO.Path.Combine(Environment.CurrentDirectory, "last_fixed.cs");
                    System.IO.File.WriteAllText(outPath, text);
                }
                catch { }

                return formattedDoc;
            }
            else
            {
                // No simple assignment — insert local initialized from parameter at start
                localDecl = SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(localName))
                        .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.IdentifierName(parameterSymbol.Name))))))
                    .WithLeadingTrivia(body.GetFirstToken().LeadingTrivia);

                // Replace identifier usages first
                var newId = SyntaxFactory.IdentifierName(localName);
                var rootAfterIdReplacements = root.ReplaceNodes(identifiers, (oldNode, _) => newId.WithTriviaFrom(oldNode));

                // Find the corresponding body node in the updated root
                var updatedMethod = rootAfterIdReplacements.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.SpanStart == method.SpanStart && m.Identifier.ValueText == method.Identifier.ValueText);

                if (updatedMethod == null)
                {
                    var docWithRoot = document.WithSyntaxRoot(rootAfterIdReplacements);
                    var formattedDoc = await Formatter.FormatAsync(docWithRoot, cancellationToken: cancellationToken).ConfigureAwait(false);
                    return formattedDoc;
                }

                var updatedBody = updatedMethod.Body;
                BlockSyntax newBody;
                if (updatedBody.Statements.Count > 0)
                {
                    newBody = updatedBody.WithStatements(updatedBody.Statements.Insert(0, localDecl));
                }
                else
                {
                    newBody = updatedBody.WithStatements(SyntaxFactory.List(new StatementSyntax[] { localDecl }));
                }

                var rootAfterBodyInsert = rootAfterIdReplacements.ReplaceNode(updatedBody, newBody);
                var docWithRoot2 = document.WithSyntaxRoot(rootAfterBodyInsert);
                var formattedDoc2 = await Formatter.FormatAsync(docWithRoot2, cancellationToken: cancellationToken).ConfigureAwait(false);

                try
                {
                    var text = (await formattedDoc2.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
                    var outPath = System.IO.Path.Combine(Environment.CurrentDirectory, "last_fixed.cs");
                    System.IO.File.WriteAllText(outPath, text);
                }
                catch { }

                return formattedDoc2;
            }
        }
    }
}
