using System.Reflection;

string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

Console.WriteLine($"DDT agent {version}");
