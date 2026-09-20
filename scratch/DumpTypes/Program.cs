using System;
using System.Linq;
using System.Reflection;

class Program
{
    static void Main(string[] args)
    {
        var asm = Assembly.LoadFrom(args[0]);
        foreach (var type in asm.GetTypes().Where(t => t.Name.Contains("SecuritySchemeType") || t.Name.Contains("ParameterLocation") || t.Name.Contains("ReferenceType") || t.Name.Contains("OpenApiReference")))
        {
            Console.WriteLine(type.FullName);
        }
    }
}
