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
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            // Determine the parameter symbol reported
            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel == null)
                return;

            var symbol = semanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol as IParameterSymbol;
            if (symbol == null)
                return;

            // Offer a fix that introduces a local and redirects uses to the local
            context.RegisterCodeFix(
                CodeAction.Create("Introduce local and use it instead of modifying parameter",
                    c => ReplaceParameterWithLocalAsync(context.Document, node, symbol, c), equivalenceKey: "IntroduceLocalAndRedirect"),
                diagnostic);
        }

        private async Task<Document> ReplaceParameterWithLocalAsync(Document document, SyntaxNode node, IParameterSymbol parameterSymbol, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null)
                return document;

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

            // Create local declaration: var localName = paramName;
            var localDecl = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(localName))
                    .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.IdentifierName(parameterSymbol.Name))))))
                .WithLeadingTrivia(body.GetFirstToken().LeadingTrivia);

            // Insert local declaration at start of the body
            BlockSyntax newBody;
            if (body.Statements.Count > 0)
            {
                newBody = body.WithStatements(body.Statements.Insert(0, localDecl));
            }
            else
            {
                newBody = body.WithStatements(SyntaxFactory.List(new StatementSyntax[] { localDecl }));
            }

            editor.ReplaceNode(body, newBody);

            // Replace all identifier usages (collected earlier) with the local identifier
            var newId = SyntaxFactory.IdentifierName(localName);
            foreach (var id in identifiers)
            {
                // Skip the identifier if it is part of the parameter declaration (shouldn't be in body)
                editor.ReplaceNode(id, newId.WithTriviaFrom(id));
            }

            return editor.GetChangedDocument();
        }
    }
}
