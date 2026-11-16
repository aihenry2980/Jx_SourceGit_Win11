using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace SourceGit.Views
{
    public static class ControlExtensions
    {
        public static Control CreateFromViewModels(object data)
        {
            var dataTypeName = data.GetType().FullName;
            if (string.IsNullOrEmpty(dataTypeName) || !dataTypeName.Contains(".ViewModels.", StringComparison.Ordinal))
                return null;

            var viewTypeName = dataTypeName.Replace(".ViewModels.", ".Views.");
            var viewType = Type.GetType(viewTypeName);
            if (viewType != null)
                return Activator.CreateInstance(viewType) as Control;

            return null;
        }

        public static async Task CopyTextAsync(this Control control, string text)
        {
            var topLevel = TopLevel.GetTopLevel(control) as Window;
            if (topLevel == null)
                return;

            try
            {
                if (topLevel.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(text);
                    App.ShowCopyToast(text);
                }
            }
            catch (Exception e)
            {
                await new Alert().ShowAsync(topLevel, "Failed to copy text to clipboard: " + e.Message, true);
            }
        }

        public static Path CreateMenuIcon(this Control control, string iconKey)
        {
            if (control?.FindResource(iconKey) is StreamGeometry geo)
            {
                return new Path()
                {
                    Data = geo,
                    Width = 12,
                    Height = 12,
                    Stretch = Stretch.Uniform
                };
            }

            return null;
        }

        public static MenuItem CreateCopyBranchNameMenuItem(this Control control, Models.Branch branch)
        {
            var name = branch.Name;
            var copy = new MenuItem
            {
                Icon = control.CreateMenuIcon("Icons.Copy"),
                Classes = { "copy_branch_name" },
            };
            var label = new TextBlock { Text = App.Text("BranchCM.CopyName") };
            label.Bind(TextBlock.ForegroundProperty, new Avalonia.Data.Binding(nameof(MenuItem.Foreground)) { Source = copy });
            var preview = new TextBlock
            {
                Text = name,
                Classes = { "branch_name_preview" },
                MaxWidth = 360,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontFamily = control.FindResource("Fonts.Monospace") as FontFamily ?? FontFamily.Default,
                Foreground = control.FindResource("Brush.FG1") as IBrush,
            };
            copy.Header = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    label,
                    new Border
                    {
                        Padding = new Thickness(6, 1),
                        CornerRadius = new CornerRadius(3),
                        BorderThickness = new Thickness(1),
                        BorderBrush = control.FindResource("Brush.Border2") as IBrush,
                        Background = control.FindResource("Brush.ToolBar") as IBrush,
                        Child = preview,
                    },
                },
            };
            ToolTip.SetTip(copy, name);
            copy.Click += async (_, e) =>
            {
                await control.CopyTextAsync(name);
                e.Handled = true;
            };
            return copy;
        }

        public static Control CreateDeleteBranchHeader(Models.Branch branch, Control branchLabel = null)
        {
            var header = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            var scope = new Border
            {
                Classes = { "branch_delete_scope", branch.IsLocal ? "local" : "remote" },
                Padding = new Thickness(6, 1),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = branch.IsLocal ? "LOCAL" : $"REMOTE: {branch.Remote}",
                    Classes = { "branch_delete_scope_label" },
                    FontWeight = FontWeight.Bold,
                    MaxWidth = 200,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            ToolTip.SetTip(scope, branch.FullName);
            var parts = App.Text("BranchCM.Delete", string.Empty).Split('$');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1)
                {
                    header.Children.Add(scope);
                    header.Children.Add(branchLabel ?? new TextBlock
                    {
                        Text = branch.FriendlyName,
                        MaxWidth = 360,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    });
                }
                else if (!string.IsNullOrEmpty(parts[i]))
                {
                    var text = parts[i].Trim();
                    if (text.Trim('.', '\u2026').Length > 0)
                        header.Children.Add(CreateBranchDeleteActionBadge(text));
                    else if (text.Length > 0)
                        header.Children.Add(new TextBlock { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                }
            }
            return header;
        }

        public static Border CreateBranchDeleteActionBadge(string text)
        {
            return new Border
            {
                Classes = { "branch_delete_action" },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Padding = new Thickness(6, 1),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = text,
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
            };
        }

        public static void ShowWindow(this Control control, object data)
        {
            if (data == null)
                return;

            if (data is not ChromelessWindow window)
            {
                window = CreateFromViewModels(data) as ChromelessWindow;
                if (window == null)
                    return;

                window.DataContext = data;
            }

            do
            {
                var owner = TopLevel.GetTopLevel(control) as Window;
                if (owner != null)
                {
                    // Get the screen where current window locates.
                    var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
                    if (screen == null)
                        break;

                    // Calculate the startup position (Center Screen Mode) of target window
                    var rect = new PixelRect(PixelSize.FromSize(window.ClientSize, owner.DesktopScaling));
                    var centeredRect = screen.WorkingArea.CenterRect(rect);
                    if (owner.Screens.ScreenFromPoint(centeredRect.Position) == null)
                        break;

                    // Use the startup position
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Position = centeredRect.Position;
                }
            } while (false);

            window.Show();
        }

        public static Task ShowDialogAsync(this Control control, object data)
        {
            var owner = TopLevel.GetTopLevel(control) as Window;
            if (owner == null)
                return null;

            if (data is ChromelessWindow window)
                return window.ShowDialog(owner);

            window = CreateFromViewModels(data) as ChromelessWindow;
            if (window != null)
            {
                window.DataContext = data;
                return window.ShowDialog(owner);
            }

            return null;
        }
    }
}
