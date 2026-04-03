using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace YASN;

public static class DialogService
{
    public static async Task ShowMessageAsync(Window? owner, string title, string message)
    {
        var dialog = BuildBaseDialog(title, message);
        var okButton = new Button
        {
            Content = "确定",
            Width = 88,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        okButton.Classes.Add("accent");

        okButton.Click += (_, _) => dialog.Close();
        AddButtons(dialog, okButton);
        await ShowDialogAsync(dialog, owner);
    }

    public static async Task<bool> ShowConfirmationAsync(Window? owner, string title, string message)
    {
        var dialog = BuildBaseDialog(title, message);
        var result = false;

        var confirmButton = new Button
        {
            Content = "确定",
            Width = 88
        };
        var cancelButton = new Button
        {
            Content = "取消",
            Width = 88
        };
        confirmButton.Classes.Add("accent");
        cancelButton.Classes.Add("subtle");

        confirmButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        AddButtons(dialog, cancelButton, confirmButton);
        await ShowDialogAsync(dialog, owner);
        return result;
    }

    public static async Task<string?> PromptTextAsync(Window? owner, string title, string message, string initialValue)
    {
        var dialog = BuildBaseDialog(title, message);
        var result = (string?)null;

        var input = new TextBox
        {
            Text = initialValue,
            Watermark = message,
            MinWidth = 320
        };

        InsertBody(dialog, input);

        var confirmButton = new Button
        {
            Content = "确定",
            Width = 88
        };
        var cancelButton = new Button
        {
            Content = "取消",
            Width = 88
        };
        confirmButton.Classes.Add("accent");
        cancelButton.Classes.Add("subtle");

        confirmButton.Click += (_, _) =>
        {
            result = input.Text;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        AddButtons(dialog, cancelButton, confirmButton);
        await ShowDialogAsync(dialog, owner);
        return result;
    }

    private static Window BuildBaseDialog(string title, string message)
    {
        var root = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(20)
        };

        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420
        });

        var dialog = new Window
        {
            Title = title,
            CanResize = false,
            MinWidth = 420,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root
        };

        return dialog;
    }

    private static void InsertBody(Window dialog, Control control)
    {
        if (dialog.Content is not StackPanel panel)
        {
            return;
        }

        panel.Children.Add(control);
    }

    private static void AddButtons(Window dialog, params Button[] buttons)
    {
        if (dialog.Content is not StackPanel panel)
        {
            return;
        }

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        foreach (var button in buttons)
        {
            footer.Children.Add(button);
        }

        panel.Children.Add(footer);
    }

    private static async Task ShowDialogAsync(Window dialog, Window? owner)
    {
        if (owner != null)
        {
            await dialog.ShowDialog(owner);
            return;
        }

        dialog.Show();
    }
}
