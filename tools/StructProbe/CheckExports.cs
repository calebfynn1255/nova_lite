using System;
using System.IO;
using System.Runtime.InteropServices;

class ExportChecker
{
    public static void Run()
    {
        var publishDir = @"C:\Users\Fynn\nova_lite\src\NovaLite.UI\bin\Release\net9.0\win-x64\publish";
        IntPtr hModule = System.Runtime.InteropServices.NativeLibrary.Load(System.IO.Path.Combine(publishDir, "llama.dll"));
        
        string[] candidates = { 
            "llama_tokenize", 
            "llama_model_tokenize", 
            "llama_vocab_tokenize"
        };

        foreach (var c in candidates)
        {
            bool found = System.Runtime.InteropServices.NativeLibrary.TryGetExport(hModule, c, out IntPtr p);
            Console.WriteLine($"{c}: {(found ? "FOUND" : "NOT FOUND")}");
        }
    }
}
