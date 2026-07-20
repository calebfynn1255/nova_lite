using NovaLite.Core.AI;
using Xunit;

namespace NovaLite.Tests;

public class LocalInferenceProviderTests
{
    [Fact]
    public void PreparePromptText_TruncatesLongContent()
    {
        var longText = new string('x', 6000);

        var result = LocalInferenceProvider.PreparePromptText(longText, 2000);

        Assert.True(result.Length < longText.Length);
        Assert.Contains("truncated", result);
    }

    [Fact]
    public void TrimPromptToBudget_LeavesTailWhenPromptIsTooLong()
    {
        var prompt = new string('a', 20000);

        var result = LocalInferenceProvider.PreparePromptText(prompt, 2000);

        Assert.Contains("truncated", result);
    }
}
