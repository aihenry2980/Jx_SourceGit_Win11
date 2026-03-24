using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;

namespace SourceGit.Views
{
    public class GitCommandScriptEditor : TextEditor
    {
        private class GitCommandLineTransformer : DocumentColorizingTransformer
        {
            protected override void ColorizeLine(DocumentLine line)
            {
                var content = CurrentContext.Document.GetText(line);
                if (string.IsNullOrWhiteSpace(content))
                    return;

                var trimmed = content.TrimStart();
                var leadingWhitespace = content.Length - trimmed.Length;
                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    ColorizeComment(line.Offset + leadingWhitespace, line.EndOffset);
                    return;
                }

                var tokenIndex = 0;
                foreach (var token in EnumerateTokens(content))
                {
                    if (token.Text.StartsWith("#", StringComparison.Ordinal))
                    {
                        ColorizeComment(line.Offset + token.Start, line.EndOffset);
                        break;
                    }

                    var start = line.Offset + token.Start;
                    var end = start + token.Length;

                    if (tokenIndex == 0 && token.Text.Equals("git", StringComparison.OrdinalIgnoreCase))
                    {
                        Colorize(start, end, s_gitBrush, FontWeight.Bold);
                    }
                    else if (tokenIndex == 1 && !token.Text.StartsWith("-", StringComparison.Ordinal))
                    {
                        Colorize(start, end, s_subCommandBrush, FontWeight.Bold);
                    }
                    else if (token.Text.StartsWith("--", StringComparison.Ordinal) || token.Text.StartsWith("-", StringComparison.Ordinal))
                    {
                        Colorize(start, end, s_optionBrush);
                    }
                    else if ((token.Text.StartsWith("\"", StringComparison.Ordinal) && token.Text.EndsWith("\"", StringComparison.Ordinal)) ||
                             (token.Text.StartsWith("'", StringComparison.Ordinal) && token.Text.EndsWith("'", StringComparison.Ordinal)))
                    {
                        Colorize(start, end, s_argumentBrush);
                    }
                    else if (token.Text.StartsWith("<", StringComparison.Ordinal) && token.Text.EndsWith(">", StringComparison.Ordinal))
                    {
                        Colorize(start, end, s_placeholderBrush, null, FontStyle.Italic);
                    }

                    tokenIndex++;
                }
            }

            private void ColorizeComment(int start, int end)
            {
                Colorize(start, end, s_commentBrush, null, FontStyle.Italic);
            }

            private void Colorize(int start, int end, IBrush brush, FontWeight? weight = null, FontStyle? style = null)
            {
                ChangeLinePart(start, end, visualLine =>
                {
                    if (brush != null)
                        visualLine.TextRunProperties.SetForegroundBrush(brush);

                    if (weight != null || style != null)
                    {
                        var old = visualLine.TextRunProperties.Typeface;
                        visualLine.TextRunProperties.SetTypeface(new Typeface(
                            old.FontFamily,
                            style ?? old.Style,
                            weight ?? old.Weight));
                    }
                });
            }

            private static IEnumerable<(int Start, int Length, string Text)> EnumerateTokens(string content)
            {
                var idx = 0;
                while (idx < content.Length)
                {
                    while (idx < content.Length && char.IsWhiteSpace(content[idx]))
                        idx++;

                    if (idx >= content.Length)
                        yield break;

                    var start = idx;
                    while (idx < content.Length && !char.IsWhiteSpace(content[idx]))
                    {
                        if (content[idx] == '"' || content[idx] == '\'')
                        {
                            var quote = content[idx++];
                            while (idx < content.Length)
                            {
                                if (content[idx] == '\\' && idx + 1 < content.Length)
                                {
                                    idx += 2;
                                    continue;
                                }

                                if (content[idx] == quote)
                                {
                                    idx++;
                                    break;
                                }

                                idx++;
                            }
                        }
                        else
                        {
                            idx++;
                        }
                    }

                    yield return (start, idx - start, content.Substring(start, idx - start));
                }
            }

            private static readonly IBrush s_gitBrush = new SolidColorBrush(0xFF1F6FEB);
            private static readonly IBrush s_subCommandBrush = new SolidColorBrush(0xFF1A7F37);
            private static readonly IBrush s_optionBrush = new SolidColorBrush(0xFFD97706);
            private static readonly IBrush s_argumentBrush = new SolidColorBrush(0xFF0F766E);
            private static readonly IBrush s_placeholderBrush = new SolidColorBrush(0xFF7C3AED);
            private static readonly IBrush s_commentBrush = Brushes.Gray;
        }

        public static readonly StyledProperty<string> CommandTextProperty =
            AvaloniaProperty.Register<GitCommandScriptEditor, string>(
                nameof(CommandText),
                string.Empty,
                defaultBindingMode: BindingMode.TwoWay);

        public string CommandText
        {
            get => GetValue(CommandTextProperty);
            set => SetValue(CommandTextProperty, value);
        }

        protected override Type StyleKeyOverride => typeof(TextEditor);

        public GitCommandScriptEditor() : base(new TextArea(), new TextDocument())
        {
            ShowLineNumbers = false;
            WordWrap = false;
            Background = Brushes.Transparent;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            TextArea.TextView.Margin = new Thickness(4, 0);
            TextArea.TextView.Options.EnableHyperlinks = false;
            TextArea.TextView.Options.EnableEmailHyperlinks = false;
            TextArea.TextView.Options.AllowScrollBelowDocument = false;
            TextArea.TextView.LineTransformers.Add(new GitCommandLineTransformer());
            TextChanged += OnTextChanged;
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            _textMate ??= Models.TextMateHelper.CreateForEditor(this);
            Models.TextMateHelper.SetGrammarByFileName(_textMate, "toolbar-command.sh");
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            if (_textMate != null)
            {
                _textMate.Dispose();
                _textMate = null;
            }

            GC.Collect();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == CommandTextProperty && !_isUpdating)
            {
                var next = change.GetNewValue<string>() ?? string.Empty;
                if (!string.Equals(Text, next, StringComparison.Ordinal))
                {
                    _isUpdating = true;
                    Text = next;
                    _isUpdating = false;
                }
            }
            else if (change.Property.Name == nameof(ActualThemeVariant) && change.NewValue != null)
            {
                Models.TextMateHelper.SetThemeByApp(_textMate);
            }
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            if (_isUpdating)
                return;

            _isUpdating = true;
            SetCurrentValue(CommandTextProperty, Text);
            _isUpdating = false;
        }

        private bool _isUpdating = false;
        private TextMate.Installation _textMate = null;
    }

    public partial class ToolbarGitCommandEditorWindow : ChromelessWindow
    {
        public ToolbarGitCommandEditorWindow()
        {
            InitializeComponent();
            CloseOnESC = true;
        }

        private async void OnRun(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.ToolbarGitCommandEditor vm)
                return;

            if (await vm.StartAsync())
                Close();

            e.Handled = true;
        }

        private void OnRunByHotKey(object sender, RoutedEventArgs e)
        {
            OnRun(sender, e);
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
            e.Handled = true;
        }
    }
}
