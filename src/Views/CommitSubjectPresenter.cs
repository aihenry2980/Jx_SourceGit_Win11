using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SourceGit.Views
{
    public partial class CommitSubjectPresenter : Control
    {
        public static readonly StyledProperty<FontFamily> FontFamilyProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, FontFamily>(nameof(FontFamily));

        public FontFamily FontFamily
        {
            get => GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly StyledProperty<FontFamily> CodeFontFamilyProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, FontFamily>(nameof(CodeFontFamily));

        public FontFamily CodeFontFamily
        {
            get => GetValue(CodeFontFamilyProperty);
            set => SetValue(CodeFontFamilyProperty, value);
        }

        public static readonly StyledProperty<double> FontSizeProperty =
           TextBlock.FontSizeProperty.AddOwner<CommitSubjectPresenter>();

        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public static readonly StyledProperty<FontWeight> FontWeightProperty =
           TextBlock.FontWeightProperty.AddOwner<CommitSubjectPresenter>();

        public FontWeight FontWeight
        {
            get => GetValue(FontWeightProperty);
            set => SetValue(FontWeightProperty, value);
        }

        public static readonly StyledProperty<IBrush> InlineCodeBackgroundProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, IBrush>(nameof(InlineCodeBackground), Brushes.Transparent);

        public IBrush InlineCodeBackground
        {
            get => GetValue(InlineCodeBackgroundProperty);
            set => SetValue(InlineCodeBackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> InlineCodeForegroundProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, IBrush>(nameof(InlineCodeForeground), Brushes.White);

        public IBrush InlineCodeForeground
        {
            get => GetValue(InlineCodeForegroundProperty);
            set => SetValue(InlineCodeForegroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> ForegroundProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, IBrush>(nameof(Foreground), Brushes.White);

        public IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> LinkForegroundProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, IBrush>(nameof(LinkForeground), Brushes.White);

        public IBrush LinkForeground
        {
            get => GetValue(LinkForegroundProperty);
            set => SetValue(LinkForegroundProperty, value);
        }

        public static readonly StyledProperty<bool> ShowStrikethroughProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, bool>(nameof(ShowStrikethrough), false);

        public bool ShowStrikethrough
        {
            get => GetValue(ShowStrikethroughProperty);
            set => SetValue(ShowStrikethroughProperty, value);
        }

        public static readonly StyledProperty<string> SubjectProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, string>(nameof(Subject));

        public string Subject
        {
            get => GetValue(SubjectProperty);
            set => SetValue(SubjectProperty, value);
        }

        public static readonly StyledProperty<string> HighlightTextProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, string>(nameof(HighlightText), string.Empty);

        public string HighlightText
        {
            get => GetValue(HighlightTextProperty);
            set => SetValue(HighlightTextProperty, value);
        }

        public static readonly StyledProperty<AvaloniaList<Models.IssueTracker>> IssueTrackersProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, AvaloniaList<Models.IssueTracker>>(nameof(IssueTrackers));

        public AvaloniaList<Models.IssueTracker> IssueTrackers
        {
            get => GetValue(IssueTrackersProperty);
            set => SetValue(IssueTrackersProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            if (_needRebuildInlines)
            {
                _needRebuildInlines = false;
                GenerateFormattedTextElements();
            }

            if (_inlines.Count == 0)
                return;

            var ro = new RenderOptions()
            {
                TextRenderingMode = TextRenderingMode.SubpixelAntialias,
                EdgeMode = EdgeMode.Antialias
            };

            using (context.PushRenderOptions(ro))
            {
                var height = Bounds.Height;
                var width = Bounds.Width;
                var maxX = 0.0;
                foreach (var inline in _inlines)
                {
                    if (inline.X > width)
                        break;

                    if (inline.Element is { Type: Models.InlineElementType.Code })
                    {
                        var rect = new Rect(inline.X, (height - inline.Text.Height - 2) * 0.5, inline.Text.WidthIncludingTrailingWhitespace + 8, inline.Text.Height + 2);
                        var roundedRect = new RoundedRect(rect, new CornerRadius(4));
                        context.DrawRectangle(InlineCodeBackground, null, roundedRect);
                        DrawInlineHighlights(context, inline, inline.X + 4, (height - inline.Text.Height) * 0.5);
                        context.DrawText(inline.Text, new Point(inline.X + 4, (height - inline.Text.Height) * 0.5));
                        maxX = Math.Min(width, inline.X + inline.Text.WidthIncludingTrailingWhitespace + 8);
                    }
                    else
                    {
                        DrawInlineHighlights(context, inline, inline.X, (height - inline.Text.Height) * 0.5);
                        context.DrawText(inline.Text, new Point(inline.X, (height - inline.Text.Height) * 0.5));
                        maxX = Math.Min(width, inline.X + inline.Text.WidthIncludingTrailingWhitespace);
                    }
                }

                if (ShowStrikethrough)
                    context.DrawLine(new Pen(Foreground), new Point(0, height * 0.5), new Point(maxX, height * 0.5));
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == SubjectProperty)
            {
                _needRebuildInlines = true;
                GenerateInlineElements();
                InvalidateVisual();
            }
            else if (change.Property == IssueTrackersProperty)
            {
                if (change.OldValue is AvaloniaList<Models.IssueTracker> oldValue)
                    oldValue.CollectionChanged -= OnIssueTrackersChanged;
                if (change.NewValue is AvaloniaList<Models.IssueTracker> newValue)
                    newValue.CollectionChanged += OnIssueTrackersChanged;

                OnIssueTrackersChanged(null, null);
            }
            else if (change.Property == FontFamilyProperty ||
                change.Property == CodeFontFamilyProperty ||
                change.Property == FontSizeProperty ||
                change.Property == FontWeightProperty ||
                change.Property == ForegroundProperty ||
                change.Property == LinkForegroundProperty ||
                change.Property == InlineCodeForegroundProperty)
            {
                _needRebuildInlines = true;
                InvalidateVisual();
            }
            else if (change.Property == HighlightTextProperty)
            {
                InvalidateVisual();
            }
            else if (change.Property == InlineCodeBackgroundProperty ||
                change.Property == ShowStrikethroughProperty)
            {
                InvalidateVisual();
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            var point = e.GetPosition(this);
            foreach (var inline in _inlines)
            {
                if (inline.Element is not { Type: Models.InlineElementType.Link } link)
                    continue;

                if (inline.X > point.X || inline.X + inline.Text.WidthIncludingTrailingWhitespace < point.X)
                    continue;

                _lastHover = link;
                SetCurrentValue(CursorProperty, Cursor.Parse("Hand"));
                ToolTip.SetTip(this, link.Link);
                e.Handled = true;
                return;
            }

            ClearHoveredIssueLink();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (_lastHover != null)
                Native.OS.OpenBrowser(_lastHover.Link);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            ClearHoveredIssueLink();
        }

        private void OnIssueTrackersChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _needRebuildInlines = true;
            GenerateInlineElements();
            InvalidateVisual();
        }

        private void GenerateInlineElements()
        {
            _elements.Clear();
            ClearHoveredIssueLink();

            var subject = Subject;
            if (string.IsNullOrEmpty(subject))
            {
                _needRebuildInlines = true;
                InvalidateVisual();
                return;
            }

            var countPrefixMatch = REG_COUNT_PREFIX_FORMAT().Match(subject);
            if (countPrefixMatch.Success)
                _elements.Add(new Models.InlineElement(Models.InlineElementType.CountPrefix, 0, countPrefixMatch.Length, string.Empty));

            var rules = IssueTrackers ?? [];
            foreach (var rule in rules)
                rule.Matches(_elements, subject);

            if (subject.StartsWith("fixup! ", StringComparison.Ordinal) || subject.StartsWith("amend! ", StringComparison.Ordinal))
            {
                _elements.Add(new Models.InlineElement(Models.InlineElementType.Keyword, 0, 6, string.Empty));
            }
            else if (subject.StartsWith("squash! ", StringComparison.Ordinal))
            {
                _elements.Add(new Models.InlineElement(Models.InlineElementType.Keyword, 0, 7, string.Empty));
            }
            else if (subject.StartsWith('['))
            {
                var bracketIdx = subject.IndexOf(']');
                if (bracketIdx > 1 && bracketIdx < 50 && _elements.Intersect(0, bracketIdx + 1) == null)
                    _elements.Add(new Models.InlineElement(Models.InlineElementType.Keyword, 0, bracketIdx + 1, string.Empty));
            }
            else
            {
                var colonIdx = subject.IndexOf(": ", StringComparison.Ordinal);
                if (colonIdx > 0 && colonIdx < 32 && colonIdx < subject.Length - 3 && subject.IndexOf('"', 0, colonIdx) == -1 && _elements.Intersect(0, colonIdx) == null)
                {
                    _elements.Add(new Models.InlineElement(Models.InlineElementType.Keyword, 0, colonIdx + 1, string.Empty));
                }
                else
                {
                    var hyphenIdx = subject.IndexOf(" - ", StringComparison.Ordinal);
                    if (hyphenIdx > 0 && hyphenIdx < 32 && hyphenIdx < subject.Length - 4 && subject.IndexOf('"', 0, hyphenIdx) == -1 && _elements.Intersect(0, hyphenIdx) == null)
                        _elements.Add(new Models.InlineElement(Models.InlineElementType.Keyword, 0, hyphenIdx, string.Empty));
                }
            }

            var codeMatches = REG_INLINECODE_FORMAT().Matches(subject);
            foreach (Match match in codeMatches)
            {
                var start = match.Index;
                var len = match.Length;
                if (_elements.Intersect(start, len) != null)
                    continue;

                _elements.Add(new Models.InlineElement(Models.InlineElementType.Code, start, len, string.Empty));
            }

            _elements.Sort();
        }

        private void GenerateFormattedTextElements()
        {
            _inlines.Clear();

            var subject = Subject;
            if (string.IsNullOrEmpty(subject))
                return;

            var fontFamily = FontFamily;
            var codeFontFamily = CodeFontFamily;
            var fontSize = FontSize;
            var foreground = Foreground;
            var linkForeground = LinkForeground;
            var inlineCodeForeground = InlineCodeForeground;
            var typeface = new Typeface(fontFamily, FontStyle.Normal, FontWeight);
            var codeTypeface = new Typeface(codeFontFamily, FontStyle.Normal, FontWeight);
            var pos = 0;
            var x = 0.0;
            for (var i = 0; i < _elements.Count; i++)
            {
                var elem = _elements[i];
                if (elem.Start > pos)
                {
                    var normal = new FormattedText(
                        subject.Substring(pos, elem.Start - pos),
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        foreground);

                    _inlines.Add(new Inline(x, normal, null));
                    _inlines[^1].RawText = subject.Substring(pos, elem.Start - pos);
                    _inlines[^1].Typeface = typeface;
                    _inlines[^1].FontSize = fontSize;
                    _inlines[^1].Brush = foreground;
                    x += normal.WidthIncludingTrailingWhitespace;
                }

                if (elem.Type == Models.InlineElementType.Keyword)
                {
                    var raw = subject.Substring(elem.Start, elem.Length);
                    var keyword = new FormattedText(
                        raw,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(fontFamily, FontStyle.Normal, FontWeight.Bold),
                        fontSize,
                        foreground);
                    _inlines.Add(new Inline(x, keyword, elem)
                    {
                        RawText = raw,
                        Typeface = new Typeface(fontFamily, FontStyle.Normal, FontWeight.Bold),
                        FontSize = fontSize,
                        Brush = foreground,
                    });
                    x += keyword.WidthIncludingTrailingWhitespace;
                }
                else if (elem.Type == Models.InlineElementType.Link)
                {
                    var raw = subject.Substring(elem.Start, elem.Length);
                    var link = new FormattedText(
                        raw,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        linkForeground);
                    _inlines.Add(new Inline(x, link, elem)
                    {
                        RawText = raw,
                        Typeface = typeface,
                        FontSize = fontSize,
                        Brush = linkForeground,
                    });
                    x += link.WidthIncludingTrailingWhitespace;
                }
                else if (elem.Type == Models.InlineElementType.Code)
                {
                    var raw = subject.Substring(elem.Start + 1, elem.Length - 2);
                    var link = new FormattedText(
                        raw,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        codeTypeface,
                        fontSize - 0.5,
                        inlineCodeForeground);
                    _inlines.Add(new Inline(x, link, elem)
                    {
                        RawText = raw,
                        Typeface = codeTypeface,
                        FontSize = fontSize - 0.5,
                        Brush = inlineCodeForeground,
                    });
                    x += link.WidthIncludingTrailingWhitespace + 8;
                }
                else if (elem.Type == Models.InlineElementType.CountPrefix)
                {
                    var raw = subject.Substring(elem.Start, elem.Length);
                    var prefix = new FormattedText(
                        raw,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(fontFamily, FontStyle.Normal, FontWeight.Bold),
                        fontSize,
                        linkForeground);
                    _inlines.Add(new Inline(x, prefix, elem)
                    {
                        RawText = raw,
                        Typeface = new Typeface(fontFamily, FontStyle.Normal, FontWeight.Bold),
                        FontSize = fontSize,
                        Brush = linkForeground,
                    });
                    x += prefix.WidthIncludingTrailingWhitespace;
                }

                pos = elem.Start + elem.Length;
            }

            if (pos < subject.Length)
            {
                var normal = new FormattedText(
                        subject.Substring(pos),
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        foreground);

                _inlines.Add(new Inline(x, normal, null)
                {
                    RawText = subject.Substring(pos),
                    Typeface = typeface,
                    FontSize = fontSize,
                    Brush = foreground,
                });
            }
        }

        private void DrawInlineHighlights(DrawingContext context, Inline inline, double x, double y)
        {
            var query = HighlightText;
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(inline.RawText))
                return;

            var start = 0;
            while (true)
            {
                var found = inline.RawText.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    break;

                var prefixWidth = 0.0;
                if (found > 0)
                {
                    var prefix = new FormattedText(
                        inline.RawText.Substring(0, found),
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        inline.Typeface,
                        inline.FontSize,
                        inline.Brush);
                    prefixWidth = prefix.WidthIncludingTrailingWhitespace;
                }

                var matchLen = Math.Min(query.Length, inline.RawText.Length - found);
                var match = new FormattedText(
                    inline.RawText.Substring(found, matchLen),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    inline.Typeface,
                    inline.FontSize,
                    inline.Brush);

                var rect = new RoundedRect(
                    new Rect(x + prefixWidth - 1, y, match.WidthIncludingTrailingWhitespace + 2, inline.Text.Height),
                    new CornerRadius(3));
                context.DrawRectangle(s_highlightBackground, null, rect);

                var highlightText = new FormattedText(
                    inline.RawText.Substring(found, matchLen),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    inline.Typeface,
                    inline.FontSize,
                    s_highlightForeground);
                context.DrawText(highlightText, new Point(x + prefixWidth, y));
                start = found + matchLen;
            }
        }

        private void ClearHoveredIssueLink()
        {
            if (_lastHover != null)
            {
                ToolTip.SetTip(this, null);
                SetCurrentValue(CursorProperty, Cursor.Parse("Arrow"));
                _lastHover = null;
            }
        }

        [GeneratedRegex(@"`.*?`")]
        private static partial Regex REG_INLINECODE_FORMAT();

        [GeneratedRegex(@"^\(\d+\)\s+")]
        private static partial Regex REG_COUNT_PREFIX_FORMAT();

        [GeneratedRegex(@"^\[[^]]{1,48}?\]")]
        private static partial Regex REG_KEYWORD_FORMAT();

        private class Inline
        {
            public double X { get; set; } = 0;
            public FormattedText Text { get; set; } = null;
            public Models.InlineElement Element { get; set; } = null;
            public string RawText { get; set; } = string.Empty;
            public Typeface Typeface { get; set; } = new Typeface(FontFamily.Default);
            public double FontSize { get; set; } = 0;
            public IBrush Brush { get; set; } = null;

            public Inline(double x, FormattedText text, Models.InlineElement elem)
            {
                X = x;
                Text = text;
                Element = elem;
            }
        }

        private Models.InlineElementCollector _elements = new();
        private List<Inline> _inlines = [];
        private Models.InlineElement _lastHover = null;
        private bool _needRebuildInlines = false;
        private static readonly IBrush s_highlightBackground = new SolidColorBrush(Color.Parse("#E6F2C200"));
        private static readonly IBrush s_highlightForeground = Brushes.Black;
    }
}
