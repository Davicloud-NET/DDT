// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDT.Server.Tests.CodeShape;

// Names every C# limit of docs/code-style.md a file breaks. A finding leaves out line numbers and counts, so it stays
// the same while the file changes for other reasons.
public static class CodeShapeScanner
{
    public const int MaxClassLines = 400;
    public const int MaxTestClassLines = 700;
    public const int MaxMethodLines = 60;
    public const int MaxDependencies = 6;
    public const int MaxParameters = 5;
    public const int MaxCommentLines = 4;

    private static readonly string[] s_sourceFolders = ["src", "tests"];

    // Build output, and the migrations EF Core generates.
    private static readonly HashSet<string> s_skippedFolders = new(StringComparer.Ordinal) { "bin", "obj", "node_modules", "Migrations" };

    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    public static SortedSet<string> Scan(string root)
    {
        SortedSet<string> findings = new(StringComparer.Ordinal);

        foreach (string folder in s_sourceFolders)
        {
            foreach (string file in SourceFiles(new DirectoryInfo(Path.Combine(root, folder))))
            {
                string path = Path.GetRelativePath(root, file).Replace('\\', '/');
                SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), s_parseOptions);
                CheckTypes(path, tree, findings);
                CheckComments(path, tree, findings);
            }
        }

        return findings;
    }

    private static IEnumerable<string> SourceFiles(DirectoryInfo directory)
    {
        foreach (FileInfo file in directory.EnumerateFiles("*.cs"))
        {
            yield return file.FullName;
        }

        foreach (DirectoryInfo child in directory.EnumerateDirectories())
        {
            if (!s_skippedFolders.Contains(child.Name))
            {
                foreach (string file in SourceFiles(child))
                {
                    yield return file;
                }
            }
        }
    }

    private static void CheckTypes(string path, SyntaxTree tree, SortedSet<string> findings)
    {
        SyntaxNode root = tree.GetRoot();
        List<MemberDeclarationSyntax> topLevel =
        [
            .. root.DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
                .OfType<MemberDeclarationSyntax>()
                .Where(member => member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax),
        ];

        if (topLevel.Count > 1)
        {
            findings.Add($"{path}: more than one type");
        }

        if (topLevel.Count > 0 && !topLevel.Any(type => TypeName(type) == FileStem(path)))
        {
            findings.Add($"{path}: no type named after the file");
        }

        int maxLines = path.StartsWith("tests/", StringComparison.Ordinal) ? MaxTestClassLines : MaxClassLines;

        foreach (TypeDeclarationSyntax type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (type is InterfaceDeclarationSyntax)
            {
                continue;
            }

            string name = type.Identifier.Text;

            if (Lines(tree, type) > maxLines)
            {
                findings.Add($"{path}: {name}: over {maxLines} lines");
            }

            if (type is ClassDeclarationSyntax && Dependencies(type) > MaxDependencies)
            {
                findings.Add($"{path}: {name}: over {MaxDependencies} constructor dependencies");
            }

            CheckMembers(path, tree, type, findings);
        }
    }

    private static void CheckMembers(string path, SyntaxTree tree, TypeDeclarationSyntax type, SortedSet<string> findings)
    {
        string name = type.Identifier.Text;

        foreach (MemberDeclarationSyntax member in type.Members)
        {
            if (member is BaseTypeDeclarationSyntax)
            {
                continue;
            }

            foreach ((string memberName, SyntaxNode body, int parameters) in Callables(member))
            {
                if (Lines(tree, body) > MaxMethodLines)
                {
                    findings.Add($"{path}: {name}.{memberName}: over {MaxMethodLines} lines");
                }

                if (parameters > MaxParameters)
                {
                    findings.Add($"{path}: {name}.{memberName}: over {MaxParameters} parameters");
                }
            }
        }
    }

    // Everything MA0051 measures: methods, local functions, constructors, operators and accessors. Parameters count
    // for methods and local functions only; a constructor's are dependencies.
    private static IEnumerable<(string Name, SyntaxNode Body, int Parameters)> Callables(MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case MethodDeclarationSyntax method:
                yield return (method.Identifier.Text, method, method.ParameterList.Parameters.Count);
                break;
            case ConstructorDeclarationSyntax constructor:
                yield return (".ctor", constructor, 0);
                break;
            case DestructorDeclarationSyntax destructor:
                yield return ("~", destructor, 0);
                break;
            case OperatorDeclarationSyntax op:
                yield return ($"operator {op.OperatorToken.Text}", op, 0);
                break;
            case ConversionOperatorDeclarationSyntax conversion:
                yield return ($"operator {conversion.Type}", conversion, 0);
                break;
            case BasePropertyDeclarationSyntax property:
                string propertyName = property switch
                {
                    PropertyDeclarationSyntax p => p.Identifier.Text,
                    EventDeclarationSyntax e => e.Identifier.Text,
                    _ => "this[]",
                };
                yield return (propertyName, property, 0);
                break;
        }

        foreach (LocalFunctionStatementSyntax local in member.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            yield return (local.Identifier.Text, local, local.ParameterList.Parameters.Count);
        }
    }

    private static int Dependencies(TypeDeclarationSyntax type)
    {
        int primary = type.ParameterList?.Parameters.Count ?? 0;
        int explicitMost = type.Members.OfType<ConstructorDeclarationSyntax>()
            .Select(constructor => constructor.ParameterList.Parameters.Count)
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(primary, explicitMost);
    }

    // Runs of lines that hold only a // comment, after the licence header.
    private static void CheckComments(string path, SyntaxTree tree, SortedSet<string> findings)
    {
        int run = 0;

        foreach (string line in tree.GetText().Lines.Skip(3).Select(line => line.ToString().TrimStart()))
        {
            run = line.StartsWith("//", StringComparison.Ordinal) ? run + 1 : 0;

            if (run > MaxCommentLines)
            {
                findings.Add($"{path}: comment block over {MaxCommentLines} lines");
                return;
            }
        }
    }

    private static int Lines(SyntaxTree tree, SyntaxNode node)
    {
        FileLinePositionSpan span = tree.GetLineSpan(node.Span);
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private static string TypeName(MemberDeclarationSyntax type) => type switch
    {
        BaseTypeDeclarationSyntax declaration => declaration.Identifier.Text,
        DelegateDeclarationSyntax declaration => declaration.Identifier.Text,
        _ => string.Empty,
    };

    // SequenceRunner.cs, ServerMessages.Machines.cs and Result{T}.cs all name their type before the first dot or brace.
    private static string FileStem(string path)
    {
        string name = Path.GetFileName(path);
        int end = name.IndexOfAny(['.', '{']);
        return end < 0 ? name : name[..end];
    }
}
