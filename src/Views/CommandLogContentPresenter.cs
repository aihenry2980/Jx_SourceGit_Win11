using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;

namespace SourceGit.Views
{
    public class CommandLogContentPresenter : TextEditor, Models.ICommandLogReceiver
    {
        public class LineStyleTransformer : DocumentColorizingTransformer
        {
            protected override void ColorizeLine(DocumentLine line)
            {
                var content = CurrentContext.Document.GetText(line);
                if (content.StartsWith("$ git ", StringComparison.Ordinal))
                {
                    ChangeLinePart(line.Offset, line.Offset + 1, v =>
                    {
                        v.TextRunProperties.SetForegroundBrush(Brushes.Orange);
                    });

                    ChangeLinePart(line.Offset + 2, line.EndOffset, v =>
                    {
                        var old = v.TextRunProperties.Typeface;
                        v.TextRunProperties.SetTypeface(new Typeface(old.FontFamily, old.Style, FontWeight.Bold));
                    });
                }
                else if (content.StartsWith("remote: ", StringComparison.Ordinal))
                {
                    ChangeLinePart(line.Offset, line.Offset + 7, v =>
                    {
                        v.TextRunProperties.SetForegroundBrush(Brushes.SeaGreen);
                    });
                }
                else
                {
                    foreach (var err in _errors)
                    {
                        var idx = content.IndexOf(err, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            ChangeLinePart(line.Offset + idx, line.Offset + idx + err.Length, v =>
                            {
                                var old = v.TextRunProperties.Typeface;
                                v.TextRunProperties.SetForegroundBrush(Brushes.Red);
                                v.TextRunProperties.SetTypeface(new Typeface(old.FontFamily, old.Style, FontWeight.Bold));
                            });
                        }
                    }
                }
            }

            private readonly List<string> _errors = ["! [rejected]", "! [remote rejected]"];
        }

        public static readonly DirectProperty<CommandLogContentPresenter, object> LogProperty =
            AvaloniaProperty.RegisterDirect<CommandLogContentPresenter, object>(
                nameof(Log),
                static o => o.Log,
                static (o, v) => o.Log = v);

        public object Log
        {
            get => _log;
            set => SetAndRaise(LogProperty, ref _log, value);
        }

        public static readonly DirectProperty<CommandLogContentPresenter, bool> IsFollowingOutputProperty =
            AvaloniaProperty.RegisterDirect<CommandLogContentPresenter, bool>(
                nameof(IsFollowingOutput), static o => o.IsFollowingOutput);

        public bool IsFollowingOutput
        {
            get => _isFollowingOutput;
            private set => SetAndRaise(IsFollowingOutputProperty, ref _isFollowingOutput, value);
        }

        protected override Type StyleKeyOverride => typeof(TextEditor);

        public CommandLogContentPresenter() : base(new TextArea(), new TextDocument())
        {
            IsReadOnly = true;
            ShowLineNumbers = false;
            WordWrap = false;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            FontSize = 13;

            if (Application.Current?.Resources.TryGetValue("Fonts.Monospace", out var font) == true &&
                font is FontFamily monospace)
                FontFamily = monospace;

            TextArea.TextView.Margin = new Thickness(4, 0);
            TextArea.TextView.Options.EnableHyperlinks = false;
            TextArea.TextView.Options.EnableEmailHyperlinks = false;
            TextArea.TextView.Options.AllowScrollBelowDocument = false;
            TextArea.TextView.LineTransformers.Add(new LineStyleTransformer());
        }

        public void OnReceiveCommandLog(string line)
        {
            _updatingContent = true;
            try { AppendText(line + Environment.NewLine); }
            finally { _updatingContent = false; }
            if (IsFollowingOutput)
                FollowOutput();
        }

        public void OnResetCommandLog(string content)
        {
            content ??= string.Empty;
            var offset = VerticalOffset;
            _updatingContent = true;
            try
            {
                // Keep document anchors and the reading position when bounded logs discard their prefix.
                var prefixLength = content.IndexOf('\n') + 1;
                var retained = content.AsSpan(prefixLength);
                var oldText = Text;
                var oldIndex = retained.Length >= 256 ? oldText.IndexOf(retained[..256].ToString(), StringComparison.Ordinal) : -1;
                if (!IsFollowingOutput && oldIndex > 0 && retained.StartsWith(oldText.AsSpan(oldIndex), StringComparison.Ordinal))
                {
                    var removedLines = Document.GetLineByOffset(oldIndex).LineNumber - 1;
                    var oldLength = oldText.Length;
                    Document.Replace(0, oldIndex, content[..prefixLength]);
                    AppendText(content[(prefixLength + oldLength - oldIndex)..]);
                    offset = Math.Max(0, offset - (removedLines - 1) * TextArea.TextView.DefaultLineHeight);
                }
                else
                {
                    Text = content;
                }

                if (!IsFollowingOutput && _scrollViewer != null)
                    _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, offset);
            }
            finally { _updatingContent = false; }
            if (IsFollowingOutput)
                FollowOutput();
        }

        public void FollowOutput()
        {
            IsFollowingOutput = true;
            if (_scrollViewer != null)
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, Math.Max(0, ExtentHeight - ViewportHeight));
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            if (_scrollViewer != null)
                _scrollViewer.ScrollChanged -= OnScrollChanged;
            base.OnApplyTemplate(e);
            _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
            if (_scrollViewer != null)
                _scrollViewer.ScrollChanged += OnScrollChanged;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_updatingContent)
                return;

            if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
            {
                if (IsFollowingOutput)
                    FollowOutput();
            }
            else if (e.OffsetDelta.Y != 0)
            {
                IsFollowingOutput = VerticalOffset >= Math.Max(0, ExtentHeight - ViewportHeight) - 2;
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (_log is ViewModels.CommandLog log)
            {
                Text = log.Content;
                log.Unsubscribe(this);
                log.Subscribe(this);
            }

            if (_textMate == null)
            {
                _textMate = Models.TextMateHelper.CreateForEditor(this);
                Models.TextMateHelper.SetGrammarByFileName(_textMate, "Log.log");
            }
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            if (_log is ViewModels.CommandLog log)
                log.Unsubscribe(this);

            if (_textMate != null)
            {
                _textMate.Dispose();
                _textMate = null;
            }

        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == LogProperty)
            {
                IsFollowingOutput = change.NewValue == null || change.NewValue is ViewModels.CommandLog;
                if (change.OldValue is ViewModels.CommandLog oldLog)
                    oldLog.Unsubscribe(this);

                if (change.NewValue is ViewModels.CommandLog newLog)
                {
                    Text = newLog.Content;
                    newLog.Subscribe(this);
                }
                else if (change.NewValue is string text)
                {
                    Text = text;
                }
                else
                {
                    Text = string.Empty;
                }
                if (IsFollowingOutput)
                    FollowOutput();
            }
            else if (change.Property.Name == nameof(ActualThemeVariant) && change.NewValue != null)
            {
                Models.TextMateHelper.SetThemeByApp(_textMate);
            }
        }

        private object _log = null;
        private bool _isFollowingOutput = true;
        private bool _updatingContent = false;
        private ScrollViewer _scrollViewer;
        private TextMate.Installation _textMate = null;
    }
}
