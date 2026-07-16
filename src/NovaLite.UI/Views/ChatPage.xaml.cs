using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
            vm.Messages.CollectionChanged += (_, _) => ScrollToBottom();
            // Attach Enter key binding to the input box so Enter reliably sends
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
        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // Shift+Enter: let the TextBox handle it (insert newline)
                return;
            }
            
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
}
