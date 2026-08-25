using System;
using System.Text.RegularExpressions;
using System.Linq;

class Program
{
    static void Main()
    {
        var text = @"
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

In your specific case, you should add checks similar to the above in your `assessment_list.php` file to ensure that the keys `user_ids` and `roles` are set before trying to access them.
";
        
        var matches = Regex.Matches(text, @"^\s*(\d+)[\.)]\s*(.+)$", RegexOptions.Multiline);
        foreach (Match match in matches)
        {
            if (!match.Success || match.Groups.Count < 3)
                continue;

            var optionText = match.Groups[2].Value.Trim();
            Console.WriteLine("Matched Option: " + optionText);
        }
    }
}
