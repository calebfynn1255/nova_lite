using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using NovaLite.UI.ViewModels;
using System;
using System.Collections.Specialized;

namespace NovaLite.UI.Views;

public partial class ChatPage : UserControl
{
    public ChatPage()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ChatPageViewModel vm)
        {
            // Auto-scroll only when user is near the bottom; otherwise show a quick-jump button.
            vm.Messages.CollectionChanged += (_, _) => OnMessagesChanged();
            vm.ChatUpdated += () => ForceScrollToBottom();

            try
            {
                var input = this.FindControl<TextBox>("InputBox");
                if (input != null)
                {
                    input.KeyBindings.Clear();
                    input.KeyBindings.Add(new KeyBinding
                    {
                        Gesture = new KeyGesture(Key.Enter),
                        Command = vm.SendCommand
                    });
                }
            }
            catch { }
        }
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // Enter without Shift: Send message
            e.Handled = true; // Prevent newline
            
            if (DataContext is ChatPageViewModel vm && vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
            }
        }
    }

    private void ScrollToBottom()
    {
        var scroll = this.FindControl<ScrollViewer>("MessagesScroll");
        scroll?.ScrollToEnd();
    }

    private void ForceScrollToBottom()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ScrollToBottom());
    }

    private void OnMessagesChanged()
    {
        var scroll = this.FindControl<ScrollViewer>("MessagesScroll");
        var btn = this.FindControl<Button>("ScrollToBottomButton");
        if (scroll == null) return;

        // Determine how far from the bottom we are
        try
        {
            var offset = scroll.Offset.Y;
            var viewport = scroll.Viewport.Height;
            var extent = scroll.Extent.Height;
            var distanceFromBottom = extent - (offset + viewport);

            // If near bottom (within 48px), auto-scroll; otherwise show the jump button
            if (distanceFromBottom <= 48)
            {
                ScrollToBottom();
                if (btn != null) btn.IsVisible = false;
            }
            else
            {
                if (btn != null) btn.IsVisible = true;
            }
        }
        catch { /* best-effort UI handling */ }
    }

    private void ScrollToBottomButton_Click(object? sender, RoutedEventArgs e)
    {
        var scroll = this.FindControl<ScrollViewer>("MessagesScroll");
        var btn = this.FindControl<Button>("ScrollToBottomButton");
        scroll?.ScrollToEnd();
        if (btn != null) btn.IsVisible = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Enter sends, Shift+Enter adds newline
        if (e.Key == Key.Return && e.KeyModifiers == KeyModifiers.None)
        {
            if (DataContext is ChatPageViewModel vm && vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    public async void AttachFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ChatPageViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select a file to upload",
                AllowMultiple = false
            });

        if (files.Count > 0)
        {
            var file = files[0];
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                await vm.AttachFileAsync(path, file.Name);
            }
        }
    }

    public async void LinkWorkspace_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ChatPageViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select a project context folder to link",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            var folder = folders[0];
            var path = folder.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                vm.SetWorkspace(path);
            }
        }
    }
}
