using NovaLite.Core.AI;
using NovaLite.Core.Services;
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

    [Fact]
    public async Task PlayStationAbbreviation_IsNotInterceptedAsPowerShell()
    {
        var service = new FileCommandService();

        var result = await service.TryHandleCommandAsync("How can I check the features of my PS?");

        Assert.False(result.Handled);
    }

    [Fact]
    public async Task InformationalPowerShellQuestion_IsNotTreatedAsPcControl()
    {
        var service = new FileCommandService();

        var result = await service.TryHandleCommandAsync("What is PowerShell used for?");

        Assert.False(result.Handled);
    }

    [Fact]
    public async Task StartFromTheBeginning_IsNotTreatedAsAnOpenFileRequest()
    {
        var service = new FileCommandService();

        var result = await service.TryHandleCommandAsync("No, let's start from the beginning.");

        Assert.False(result.Handled);
    }

    [Theory]
    [InlineData("I need prices in Ghana cedis")]
    [InlineData("Can you recommend an everyday laptop for me?")]
    [InlineData("I need something with an RTX 5080")]
    public void TimeSensitiveShoppingQuestions_RequireVerifiedData(string prompt)
    {
        var handled = ResponseReliabilityGuard.TryCreateResponse(prompt, out var response);

        Assert.True(handled);
        Assert.Contains("don't have", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartFromTheBeginning_IsNotTreatedAsAFileCommand()
    {
        var service = new FileCommandService();

        var result = await service.TryHandleCommandAsync("No, let's start from the beginning");

        Assert.False(result.Handled);
    }
}
