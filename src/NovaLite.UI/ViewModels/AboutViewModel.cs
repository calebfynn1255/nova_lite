using CommunityToolkit.Mvvm.ComponentModel;

namespace NovaLite.UI.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string AppVersion =>
        typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public string Description =>
        "NovaLite is a lightweight, cross-platform desktop AI chat client " +
        "powered by llama.cpp. Run GGUF, ONNX, and Safetensors models " +
        "entirely on your hardware — no cloud required.";

    public IReadOnlyList<LinkItem> Links { get; } =
    [
        new("GitHub",          "https://github.com/calebfynn1255/novalite"),
        new("Report a Bug",    "https://github.com/calebfynn1255/novalite/issues"),
        new("llama.cpp",       "https://github.com/ggerganov/llama.cpp"),
        new("Avalonia UI",     "https://avaloniaui.net"),
    ];
}

public record LinkItem(string Label, string Url);
