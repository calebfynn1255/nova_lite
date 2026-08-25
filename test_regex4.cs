using System;
using System.Text.RegularExpressions;
using System.Linq;

class Program
{
    static void Main()
    {
        var text = @"Here's a general approach to fix this issue:

1. **Check if the key exists before accessing it**: Use the `isset` function or the `array_key_exists()` function to check if the key exists in the array before trying to access it.

2. **Provide a default value**: If the key does not exist, you can provide a default value to avoid undefined behavior.

Here's an example of how you can modify your code to check for the existence of the keys:

```php
<?php
// Example array
$exampleArray = [
    'user_ids' => [1, 2, 3],
    'roles' => ['admin', 'user']
];

// Check if 'user_ids' exists
$user_ids = isset($exampleArray['user_ids']) ? $exampleArray['user_ids'] : [];

// Check if 'roles' exists
$roles = isset($exampleArray['roles']) ? $exampleArray['roles'] : [];
?>
```

This approach ensures that you don't encounter undefined index errors when accessing array keys that might not be set.

In your specific case, you should add checks similar to the above in your `assessment_list.php` file to ensure that the keys `user_ids` and `roles` are set before trying to access them.";

        var m1 = Regex.Match(text, @"```(?:cmd|powershell|bash|sh)?\s*\r?\n(?<cmd>.*?)\r?\n```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (m1.Success) Console.WriteLine("Fence: " + ExtractFirstCommandLine(m1.Groups["cmd"].Value.Trim()));

        var m2 = Regex.Match(text, @"(?:command|cmd|powershell)\s*[:\-]\s*\r?\n(?<cmd>.+)$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (m2.Success) Console.WriteLine("Block: " + ExtractFirstCommandLine(m2.Groups["cmd"].Value.Trim()));

//        var m3 = Regex.Match(text, "`(?<cmd>[^`\r\n]+)`");
//        if (m3.Success) Console.WriteLine("Inline: " + m3.Groups["cmd"].Value.Trim());

        var m4 = Regex.Match(text, @"\b(?:run|execute)\s+(?<cmd>(?:[A-Za-z0-9_./\\:-]+\s*)+)", RegexOptions.IgnoreCase);
        if (m4.Success) Console.WriteLine("Direct: " + m4.Groups["cmd"].Value.Trim());

        var m5 = Regex.Match(text, "[(\\\"']\\s*(?<cmd>(?:sfc|chkdsk|netsh|ipconfig|tasklist|taskkill|robocopy|xcopy|reg|bcdedit|bootrec|systeminfo|wmic|ping|tracert|shutdown|gpupdate|powershell|cmd)\\b[^\\)\\\"']*)[\\)\\\"']", RegexOptions.IgnoreCase);
        if (m5.Success) Console.WriteLine("Parenthesis: " + m5.Groups["cmd"].Value.Trim());

        var m6 = Regex.Match(text, @"\b(?<cmd>(?:sfc|chkdsk|netsh|ipconfig|tasklist|taskkill|robocopy|xcopy|reg|bcdedit|bootrec|systeminfo|wmic|ping|tracert|shutdown|gpupdate|powershell|cmd)\b[^\r\n]*)", RegexOptions.IgnoreCase);
        if (m6.Success) Console.WriteLine("Anywhere: " + m6.Groups["cmd"].Value.Trim());
    }

    private static string ExtractFirstCommandLine(string candidate)
    {
        var lines = candidate.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0) return null;
        var first = lines[0];
        if (first.Length > 300) return null;
        return first;
    }
}
