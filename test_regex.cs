using System;
using System.Text.RegularExpressions;
using System.Linq;

class Program
{
    static void Main()
    {
        var text = @"
This approach ensures that you don't encounter undefined index errors when accessing array keys that might not be set.

In your specific case, you should add checks similar to the above in your `assessment_list.php` file to ensure that the keys `user_ids` and `roles` are set before trying to access them.
";
        
        var fenceMatch = Regex.Match(text, @"```(?:cmd|powershell|bash|sh)?\s*\r?\n(?<cmd>.*?)\r?\n```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (fenceMatch.Success && fenceMatch.Groups["cmd"].Success)
        {
            Console.WriteLine("Fence: " + ExtractFirstCommandLine(fenceMatch.Groups["cmd"].Value.Trim()));
            return;
        }

        var commandBlockMatch = Regex.Match(text, @"(?:command|cmd|powershell)\s*[:\-]\s*\r?\n(?<cmd>.+)$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (commandBlockMatch.Success && commandBlockMatch.Groups["cmd"].Success)
        {
            Console.WriteLine("Block: " + ExtractFirstCommandLine(commandBlockMatch.Groups["cmd"].Value.Trim()));
            return;
        }

        var inlineMatch = Regex.Match(text, "`(?<cmd>[^`\r\n]+)`");
        if (inlineMatch.Success && inlineMatch.Groups["cmd"].Success)
        {
            Console.WriteLine("Inline: " + inlineMatch.Groups["cmd"].Value.Trim());
            return;
        }

        var directMatch = Regex.Match(text, @"\b(?:run|execute)\s+(?<cmd>(?:[A-Za-z0-9_./\\:-]+\s*)+)", RegexOptions.IgnoreCase);
        if (directMatch.Success && directMatch.Groups["cmd"].Success)
        {
            Console.WriteLine("Direct: " + ExtractFirstCommandLine(directMatch.Groups["cmd"].Value.Trim()));
            return;
        }

        var anywhereMatch = Regex.Match(text, @"\b(?<cmd>(?:sfc|chkdsk|netsh|ipconfig|tasklist|taskkill|robocopy|xcopy|reg|bcdedit|bootrec|systeminfo|wmic|ping|tracert|shutdown|gpupdate|powershell|cmd)\b[^\r\n]*)", RegexOptions.IgnoreCase);
        if (anywhereMatch.Success && anywhereMatch.Groups["cmd"].Success)
        {
            Console.WriteLine("Anywhere: " + anywhereMatch.Groups["cmd"].Value.Trim());
            return;
        }
        
        Console.WriteLine("None");
    }

    private static string ExtractFirstCommandLine(string candidate)
    {
        var lines = candidate.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0) return null;
        var first = lines[0];
        if (first.Length > 300) return null;
        return first;
    }
}
