using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NovaLite.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NovaLite.UI.Controls;

public partial class ChatBubble : UserControl
{
    private ChatMessageViewModel? _currentVm;

    public ChatBubble()
    {
        InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.PropertyChanged -= OnMessagePropertyChanged;
            _currentVm = null;
        }

        if (DataContext is ChatMessageViewModel vm)
        {
            _currentVm = vm;
            _currentVm.PropertyChanged += OnMessagePropertyChanged;

            if (_currentVm.IsAiMessage)
            {
                RenderAiMessageWithClickableFiles(_currentVm.Content);
            }
        }
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ChatMessageViewModel vm && e.PropertyName == nameof(ChatMessageViewModel.Content) && vm.IsAiMessage)
        {
            RenderAiMessageWithClickableFiles(vm.Content);
        }
    }

    private void RenderAiMessageWithClickableFiles(string content)
    {
        if (this.FindControl<StackPanel>("AiMessageContent") is not StackPanel container)
            return;

        container.Children.Clear();
        content = StripLeakedControlTokens(content);

        var linkBrush = this.FindResource("AccentBrush") as IBrush ?? Brushes.Blue;
        var defaultBrush = this.FindResource("AiBubbleTextBrush") as IBrush ?? Brushes.Black;

        int currentIndex = 0;
        while (currentIndex < content.Length)
        {
            int thinkStart = content.IndexOf("<think>", currentIndex, StringComparison.OrdinalIgnoreCase);
            if (thinkStart == -1)
            {
                RenderMarkdown(container, content.Substring(currentIndex), defaultBrush, linkBrush);
                break;
            }

            if (thinkStart > currentIndex)
            {
                RenderMarkdown(container, content.Substring(currentIndex, thinkStart - currentIndex), defaultBrush, linkBrush);
            }

            int thinkContentStart = thinkStart + "<think>".Length;
            int thinkEnd = content.IndexOf("</think>", thinkContentStart, StringComparison.OrdinalIgnoreCase);

            string thinkText;
            if (thinkEnd == -1)
            {
                thinkText = content.Substring(thinkContentStart);
                currentIndex = content.Length;
            }
            else
            {
                thinkText = content.Substring(thinkContentStart, thinkEnd - thinkContentStart);
                currentIndex = thinkEnd + "</think>".Length;
            }

            RenderThinkBlock(container, thinkText, defaultBrush, linkBrush);
        }
    }

    // Older saved conversations may already contain a DeepSeek delimiter. Hide it
    // while rendering those records as well as stopping it in new generations.
    private static string StripLeakedControlTokens(string content) =>
        Regex.Replace(
            content,
            @"<[^>\r\n]*(?:end(?:\s|_|-)of(?:\s|_|-)sentence|\|im_(?:end|start)\||\|eot_id\|)[^>\r\n]*>",
            string.Empty,
            RegexOptions.IgnoreCase);

    private void RenderMarkdown(Panel container, string content, IBrush defaultBrush, IBrush linkBrush)
    {
        var blocks = content.Split(new[] { "```" }, StringSplitOptions.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            if (i % 2 == 0) // Normal text
            {
                RenderTextWithLinks(container, blocks[i], defaultBrush, linkBrush);
            }
            else // Code block
            {
                var codeText = blocks[i];
                var lang = "Code";
                
                var firstNewline = codeText.IndexOf('\n');
                if (firstNewline != -1)
                {
                    var firstLine = codeText.Substring(0, firstNewline).Trim();
                    if (!firstLine.Contains(" ") && firstLine.Length < 20)
                    {
                        lang = string.IsNullOrEmpty(firstLine) ? "Code" : firstLine;
                        codeText = codeText.Substring(firstNewline + 1);
                    }
                }
                
                container.Children.Add(CreateCodeBlockControl(lang, codeText.TrimEnd('\r', '\n')));
            }
        }
    }

    private void RenderThinkBlock(Panel container, string thinkText, IBrush defaultBrush, IBrush linkBrush)
    {
        // Deliberation tags are model-internal work, not user-facing content. In
        // particular, they must not imply that Nova is reading files or taking a
        // PC action when the user only asked a normal question.
    }

    private Control CreateCodeBlockControl(string lang, string code)
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#30000000")),
            CornerRadius = new Avalonia.CornerRadius(8, 8, 0, 0),
            Padding = new Avalonia.Thickness(12, 6)
        };
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var langText = new TextBlock
        {
            Text = lang.ToUpperInvariant(),
            FontSize = 11,
            Foreground = Brushes.Gray,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var copyBtn = new Button
        {
            Content = "Copy",
            Padding = new Avalonia.Thickness(8, 4),
            FontSize = 11,
            Foreground = Brushes.Gray,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        copyBtn.Classes.Add("ghost");
        copyBtn.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(code);
                copyBtn.Content = "Copied!";
                await System.Threading.Tasks.Task.Delay(2000);
                copyBtn.Content = "Copy";
            }
        };
        headerGrid.Children.Add(langText);
        Grid.SetColumn(copyBtn, 1);
        headerGrid.Children.Add(copyBtn);
        headerBorder.Child = headerGrid;

        var codeBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#20000000")),
            CornerRadius = new Avalonia.CornerRadius(0, 0, 8, 8),
            Padding = new Avalonia.Thickness(12)
        };
        var codeText = new SelectableTextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#E0E0E0")),
            TextWrapping = TextWrapping.Wrap
        };
        codeBorder.Child = codeText;

        grid.Children.Add(headerBorder);
        Grid.SetRow(codeBorder, 1);
        grid.Children.Add(codeBorder);

        var containerBorder = new Border
        {
            Margin = new Avalonia.Thickness(0, 8),
            CornerRadius = new Avalonia.CornerRadius(8),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#1A000000"))
        };
        containerBorder.Child = grid;
        return containerBorder;
    }

    private void RenderTextWithLinks(Panel container, string content, IBrush defaultBrush, IBrush linkBrush)
    {
        var pattern = new Regex(@"\[CLICK:(?<path>[^\|]+)\|(?<label>[^\]]+)\]");
        var lastIndex = 0;

        foreach (Match match in pattern.Matches(content))
        {
            if (match.Index > lastIndex)
            {
                var text = content[lastIndex..match.Index];
                AddMarkdownLines(container, text, defaultBrush);
            }

            var pathValue = Uri.UnescapeDataString(match.Groups["path"].Value);
            var label = match.Groups["label"].Value;

            var button = new Button
            {
                Content = label,
                Background = Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0),
                Foreground = linkBrush,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Padding = new Avalonia.Thickness(0)
            };
            button.Classes.Add("ghost");
            button.Click += (_, _) => OpenFileAtPath(pathValue);
            container.Children.Add(button);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < content.Length)
        {
            var text = content[lastIndex..];
            AddMarkdownLines(container, text, defaultBrush);
        }
    }

    private static void AddMarkdownLines(Panel container, string text, IBrush brush)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                container.Children.Add(new TextBlock { Text = string.Empty, Height = 8 });
                continue;
            }

            var headingMatch = Regex.Match(line, @"^\s*(?<hashes>#{1,6})\s+(?<text>.+?)\s*$");
            var bulletMatch = Regex.Match(line, @"^\s*[-*+]\s+(?<text>.+?)\s*$");
            var numberedMatch = Regex.Match(line, @"^\s*(?<number>\d+)[.)]\s+(?<text>.+?)\s*$");

            var textBlock = new TextBlock
            {
                Foreground = brush,
                FontSize = 14,
                LineHeight = 22,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 550
            };

            if (headingMatch.Success)
            {
                textBlock.FontSize = headingMatch.Groups["hashes"].Length == 1 ? 18 : 16;
                textBlock.FontWeight = FontWeight.Bold;
                textBlock.Margin = new Avalonia.Thickness(0, 6, 0, 2);
                AddInlineMarkdown(textBlock, headingMatch.Groups["text"].Value);
            }
            else if (bulletMatch.Success)
            {
                textBlock.Margin = new Avalonia.Thickness(10, 1, 0, 1);
                textBlock.Inlines!.Add(new Run("• "));
                AddInlineMarkdown(textBlock, bulletMatch.Groups["text"].Value);
            }
            else if (numberedMatch.Success)
            {
                textBlock.Margin = new Avalonia.Thickness(10, 1, 0, 1);
                textBlock.Inlines!.Add(new Run($"{numberedMatch.Groups["number"].Value}. "));
                AddInlineMarkdown(textBlock, numberedMatch.Groups["text"].Value);
            }
            else
            {
                AddInlineMarkdown(textBlock, line);
            }

            container.Children.Add(textBlock);
        }
    }

    private static void AddInlineMarkdown(TextBlock textBlock, string text)
    {
        var pattern = new Regex(@"(?<bold>\*\*(?<boldText>.+?)\*\*|__(?<boldText2>.+?)__)|(?<code>`(?<codeText>[^`]+)`)|(?<italic>(?<!\*)\*(?<italicText>[^*]+)\*(?!\*))");
        var lastIndex = 0;

        foreach (Match match in pattern.Matches(text))
        {
            if (match.Index > lastIndex)
                textBlock.Inlines!.Add(new Run(text[lastIndex..match.Index]));

            if (match.Groups["bold"].Success)
            {
                var value = match.Groups["boldText"].Success
                    ? match.Groups["boldText"].Value
                    : match.Groups["boldText2"].Value;
                textBlock.Inlines!.Add(new Run(value) { FontWeight = FontWeight.Bold });
            }
            else if (match.Groups["code"].Success)
            {
                textBlock.Inlines!.Add(new Run(match.Groups["codeText"].Value)
                {
                    FontFamily = new FontFamily("Consolas, Courier New, monospace")
                });
            }
            else
            {
                textBlock.Inlines!.Add(new Run(match.Groups["italicText"].Value)
                {
                    FontStyle = FontStyle.Italic
                });
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            textBlock.Inlines!.Add(new Run(text[lastIndex..]));
    }

    private static void OpenFileAtPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore failures from click actions.
        }
    }
}
