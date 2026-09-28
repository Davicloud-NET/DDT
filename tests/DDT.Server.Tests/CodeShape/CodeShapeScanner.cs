// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDT.Server.Tests.CodeShape;

// Finds code that breaks the limits of docs/code-style.md no analyzer checks. Those are class size, constructor
// dependencies, parameters and comment blocks. MA0048 and MA0051 already check one type per file and method length.
public static class CodeShapeScanner
{
    public const int MaxClassLines = 400;
    public const int MaxTestClassLines = 700;
    public const int MaxDependencies = 6;
    public const int MaxParameters = 5;
    public const int MaxCommentLines = 4;

    private static readonly string[] s_sourceFolders = ["src", "tests"];

    // Build output, and the migrations EF Core generates.
    private static readonly HashSet<string> s_skippedFolders = new(StringComparer.Ordinal) { "bin", "obj", "node_modules", "Migrations" };

    // A native import mirrors the function it declares, and a log method's parameters are the message's fields.
    private static readonly string[] s_fixedSignatures = ["LibraryImport", "DllImport", "LoggerMessage"];

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
        int maxLines = path.StartsWith("tests/", StringComparison.Ordinal) ? MaxTestClassLines : MaxClassLines;

        foreach (TypeDeclarationSyntax type in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
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

            // Records are data and have as many members as their data.
            if (type is ClassDeclarationSyntax && Dependencies(type) > MaxDependencies)
            {
                findings.Add($"{path}: {name}: over {MaxDependencies} constructor dependencies");
            }

            foreach ((string method, int parameters) in Methods(type))
            {
                if (parameters > MaxParameters)
                {
                    findings.Add($"{path}: {name}.{method}: over {MaxParameters} parameters");
                }
            }
        }
    }

    // Methods and their local functions. A constructor's parameters count as dependencies instead.
    private static IEnumerable<(string Name, int Parameters)> Methods(TypeDeclarationSyntax type)
    {
        foreach (MethodDeclarationSyntax method in type.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!HasFixedSignature(method))
            {
                yield return (method.Identifier.Text, method.ParameterList.Parameters.Count);
            }
        }

        foreach (MemberDeclarationSyntax member in type.Members.Where(member => member is not BaseTypeDeclarationSyntax))
        {
            foreach (LocalFunctionStatementSyntax local in member.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                yield return (local.Identifier.Text, local.ParameterList.Parameters.Count);
            }
        }
    }

    private static bool HasFixedSignature(MethodDeclarationSyntax method) =>
        method.AttributeLists
            .SelectMany(list => list.Attributes)
            .Select(attribute => attribute.Name.ToString())
            .Any(name => s_fixedSignatures.Any(fixedName =>
                name.EndsWith(fixedName, StringComparison.Ordinal) || name.EndsWith(fixedName + "Attribute", StringComparison.Ordinal)));

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
}
