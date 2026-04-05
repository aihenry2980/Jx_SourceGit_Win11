using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Views
{
    public class CommitRefsPresenter : Control
    {
        public class RenderItem
        {
            public Geometry Icon { get; set; } = null;
            public Geometry SecondaryIcon { get; set; } = null;
            public FormattedText PrefixLabel { get; set; } = null;
            public FormattedText Label { get; set; } = null;
            public FormattedText FoldLabel { get; set; } = null;
            public string RawLabel { get; set; } = string.Empty;
            public Typeface LabelTypeface { get; set; } = new Typeface(FontFamily.Default);
            public double LabelFontSize { get; set; } = 0;
            public IBrush Brush { get; set; } = null;
            public IBrush BorderBrush { get; set; } = null;
            public IBrush IconBrush { get; set; } = null;
            public IBrush SecondaryIconBrush { get; set; } = null;
            public IBrush PrimaryIconBackground { get; set; } = null;
            public IBrush SecondaryIconBackground { get; set; } = null;
            public IBrush LabelBrush { get; set; } = null;
            public IBrush FoldButtonBackground { get; set; } = null;
            public IBrush FoldButtonForeground { get; set; } = null;
            public bool IsHead { get; set; } = false;
            public bool IsCurrentCommitHead { get; set; } = false;
            public bool UseSolidBackground { get; set; } = false;
            public bool CanFold { get; set; } = false;
            public bool IsFolded { get; set; } = false;
            public double LeadingWidth { get; set; } = 16.0;
            public double Height { get; set; } = 16.0;
            public double Width { get; set; } = 0.0;
            public Models.Decorator Decorator { get; set; } = null;
        }

        public static readonly StyledProperty<FontFamily> FontFamilyProperty =
            TextBlock.FontFamilyProperty.AddOwner<CommitRefsPresenter>();

        public FontFamily FontFamily
        {
            get => GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly StyledProperty<double> FontSizeProperty =
           TextBlock.FontSizeProperty.AddOwner<CommitRefsPresenter>();

        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public static readonly StyledProperty<IBrush> BackgroundProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, IBrush>(nameof(Background), Brushes.Transparent);

        public IBrush Background
        {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> ForegroundProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, IBrush>(nameof(Foreground), Brushes.White);

        public IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static readonly StyledProperty<bool> UseGraphColorProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(UseGraphColor));

        public bool UseGraphColor
        {
            get => GetValue(UseGraphColorProperty);
            set => SetValue(UseGraphColorProperty, value);
        }

        public static readonly StyledProperty<bool> AllowWrapProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(AllowWrap));

        public bool AllowWrap
        {
            get => GetValue(AllowWrapProperty);
            set => SetValue(AllowWrapProperty, value);
        }

        public static readonly StyledProperty<bool> ShowTagsProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(ShowTags), true);

        public bool ShowTags
        {
            get => GetValue(ShowTagsProperty);
            set => SetValue(ShowTagsProperty, value);
        }

        public static readonly StyledProperty<string> HighlightTextProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, string>(nameof(HighlightText), string.Empty);

        public string HighlightText
        {
            get => GetValue(HighlightTextProperty);
            set => SetValue(HighlightTextProperty, value);
        }

        public static readonly StyledProperty<bool> CompactTrackingBranchesProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(CompactTrackingBranches));

        public bool CompactTrackingBranches
        {
            get => GetValue(CompactTrackingBranchesProperty);
            set => SetValue(CompactTrackingBranchesProperty, value);
        }

        static CommitRefsPresenter()
        {
            AffectsMeasure<CommitRefsPresenter>(
                FontFamilyProperty,
                FontSizeProperty,
                ForegroundProperty,
                UseGraphColorProperty,
                BackgroundProperty,
                ShowTagsProperty,
                HighlightTextProperty,
                CompactTrackingBranchesProperty);
        }

        public Models.Decorator DecoratorAt(Point point)
        {
            return TryGetItemAtPoint(point, out var item, out _) ? item.Decorator : null;
        }

        public bool TryGetFoldableDecoratorAt(Point point, out Models.Decorator decorator)
        {
            decorator = null;
            if (!TryGetItemAtPoint(point, out var item, out var foldRect))
                return false;

            if (!item.CanFold || !foldRect.Contains(point))
                return false;

            decorator = item.Decorator;
            return decorator != null;
        }

        public override void Render(DrawingContext context)
        {
            if (_items.Count == 0)
                return;

            var useGraphColor = UseGraphColor;
            var fg = Foreground;
            var bg = Background;
            var allowWrap = AllowWrap;
            var x = 1.5;
            var y = 0.5;

            foreach (var item in _items)
            {
                if (allowWrap && x > 1.5 && x + item.Width > Bounds.Width)
                {
                    x = 1.5;
                    y += item.Height + 4.0;
                }

                var entireRect = new RoundedRect(new Rect(x, y, item.Width, item.Height), new CornerRadius(4));
                var centerY = y + item.Height * 0.5;
                var iconBackgroundY = y + (item.Height - 12.0) * 0.5;
                var iconY = y + (item.Height - 10.0) * 0.5;

                if (item.IsHead)
                {
                    if (item.IsCurrentCommitHead)
                    {
                        context.DrawRectangle(s_headTagBackgroundBrush, null, entireRect);
                    }
                    else if (useGraphColor)
                    {
                        if (bg != null)
                            context.DrawRectangle(bg, null, entireRect);

                        using (context.PushOpacity(.6))
                            context.DrawRectangle(item.Brush, null, entireRect);
                    }

                    var labelX = x + item.LeadingWidth;
                    DrawLabelHighlights(context, item, labelX, centerY - item.Label.Height * 0.5);
                    if (item.PrefixLabel != null)
                    {
                        context.DrawText(item.PrefixLabel, new Point(labelX, centerY - item.PrefixLabel.Height * 0.5));
                        labelX += item.PrefixLabel.WidthIncludingTrailingWhitespace;
                    }

                    context.DrawText(item.Label, new Point(labelX, centerY - item.Label.Height * 0.5));
                }
                else
                {
                    if (item.UseSolidBackground)
                    {
                        context.DrawRectangle(item.Brush, null, entireRect);
                    }
                    else
                    {
                        if (bg != null)
                            context.DrawRectangle(bg, null, entireRect);

                        var fullLabelWidth = (item.PrefixLabel?.WidthIncludingTrailingWhitespace ?? 0.0) + item.Label.Width;
                        var labelRect = new RoundedRect(new Rect(x + item.LeadingWidth, y, fullLabelWidth + 8, item.Height), new CornerRadius(0, 4, 4, 0));
                        using (context.PushOpacity(.2))
                            context.DrawRectangle(item.Brush, null, labelRect);

                        context.DrawLine(new Pen(item.Brush), new Point(x + item.LeadingWidth, y), new Point(x + item.LeadingWidth, y + item.Height));
                    }

                    var labelX = x + item.LeadingWidth + 4;
                    DrawLabelHighlights(context, item, labelX, centerY - item.Label.Height * 0.5);
                    if (item.PrefixLabel != null)
                    {
                        context.DrawText(item.PrefixLabel, new Point(labelX, centerY - item.PrefixLabel.Height * 0.5));
                        labelX += item.PrefixLabel.WidthIncludingTrailingWhitespace;
                    }

                    context.DrawText(item.Label, new Point(labelX, centerY - item.Label.Height * 0.5));
                }

                var borderBrush = item.BorderBrush ?? item.Brush;
                context.DrawRectangle(null, new Pen(borderBrush), entireRect);

                if (item.PrimaryIconBackground != null)
                {
                    context.DrawRectangle(
                        item.PrimaryIconBackground,
                        null,
                        new RoundedRect(new Rect(x + 2, iconBackgroundY, 10, 12), new CornerRadius(3)));
                }

                using (context.PushTransform(Matrix.CreateTranslation(x + 3, iconY)))
                    context.DrawGeometry(item.IconBrush ?? fg, null, item.Icon);

                if (item.SecondaryIcon != null)
                {
                    if (item.SecondaryIconBackground != null)
                    {
                        context.DrawRectangle(
                            item.SecondaryIconBackground,
                            null,
                            new RoundedRect(new Rect(x + 14, iconBackgroundY, 10, 12), new CornerRadius(3)));
                    }

                    context.DrawLine(
                        new Pen(item.BorderBrush ?? item.Brush, 1),
                        new Point(x + 13, iconY),
                        new Point(x + 13, iconY + 10));

                    using (context.PushTransform(Matrix.CreateTranslation(x + 15, iconY)))
                        context.DrawGeometry(item.SecondaryIconBrush ?? fg, null, item.SecondaryIcon);
                }

                if (item.CanFold)
                {
                    var foldButtonWidth = 15.0;
                    var foldButtonHeight = Math.Max(14.0, item.Height - 2.0);
                    var foldButtonX = x + item.Width - 17;
                    var foldButtonY = y + (item.Height - foldButtonHeight) * 0.5;
                    var foldButtonRect = new RoundedRect(new Rect(foldButtonX, foldButtonY, foldButtonWidth, foldButtonHeight), new CornerRadius(3));

                    context.DrawRectangle(
                        item.FoldButtonBackground ?? Brushes.LightGray,
                        new Pen(borderBrush, 1.2),
                        foldButtonRect);

                    context.DrawLine(
                        new Pen(borderBrush, 1.2),
                        new Point(foldButtonX - 2, y + 2),
                        new Point(foldButtonX - 2, y + item.Height - 2));

                    if (item.FoldLabel != null)
                        context.DrawText(item.FoldLabel, new Point(foldButtonX + (foldButtonWidth - item.FoldLabel.Width) * 0.5, centerY - item.FoldLabel.Height * 0.5));
                }

                x += item.Width + 4;
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _items.Clear();

            if (DataContext is not Models.Commit commit)
                return new Size(0, 0);

            var refs = commit.Decorators;
            if (refs is { Count: > 0 })
            {
                var typeface = new Typeface(FontFamily);
                var typefaceBold = new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold);
                var fg = Foreground;
                var normalBG = UseGraphColor ? Models.CommitGraph.Pens[commit.Color].Brush : Brushes.Gray;
                var labelSize = FontSize;
                var requiredHeight = 16.0;
                var currentLineHeight = 16.0;
                var x = 0.0;
                var allowWrap = AllowWrap;
                var showTags = ShowTags;
                var compactTrackingBranches = CompactTrackingBranches;
                var consumedRemoteDecoratorIndexes = compactTrackingBranches ? new HashSet<int>() : null;

                for (var i = 0; i < refs.Count; i++)
                {
                    if (compactTrackingBranches && consumedRemoteDecoratorIndexes.Contains(i))
                        continue;

                    var decorator = refs[i];
                    if (!showTags && decorator.Type == Models.DecoratorType.Tag)
                        continue;

                    Models.Decorator secondaryDecorator = null;
                    if (compactTrackingBranches &&
                        decorator.Type is Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.LocalBranchHead &&
                        !string.IsNullOrWhiteSpace(decorator.Name))
                    {
                        var remoteDecoratorIndex = FindCompactRemoteMatch(refs, decorator.Name);
                        if (remoteDecoratorIndex >= 0 && remoteDecoratorIndex != i)
                        {
                            consumedRemoteDecoratorIndexes.Add(remoteDecoratorIndex);
                            secondaryDecorator = refs[remoteDecoratorIndex];
                        }
                    }

                    var isHead = decorator.Type is Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.CurrentCommitHead;
                    var isCurrentCommitHead = decorator.Type == Models.DecoratorType.CurrentCommitHead;
                    var isSuperProjectPointer = decorator.Type == Models.DecoratorType.SuperProjectPointer;
                    var isParentRepository = decorator.Type == Models.DecoratorType.ParentRepository;
                    var isMutedIncidentalBranch = IsMutedIncidentalBranch(decorator);
                    var labelBrush = isCurrentCommitHead
                        ? s_headTagForegroundBrush
                        : isSuperProjectPointer
                            ? s_superProjectPointerForegroundBrush
                            : isParentRepository
                                ? s_parentRepositoryForegroundBrush
                            : isMutedIncidentalBranch
                                ? s_incidentalBranchForegroundBrush
                                : fg;

                    var labelTypeface = isHead || isSuperProjectPointer || isParentRepository ? typefaceBold : typeface;
                    var labelSizeForItem = isHead ? labelSize + 1 : labelSize;
                    FormattedText prefixLabel = null;
                    var labelText = decorator.Name;
                    if (decorator.Type == Models.DecoratorType.RemoteBranchHead)
                    {
                        var slashIdx = decorator.Name.IndexOf('/');
                        if (slashIdx > 0 && slashIdx + 1 < decorator.Name.Length)
                        {
                            prefixLabel = new FormattedText(
                                decorator.Name.Substring(0, slashIdx + 1),
                                CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                typefaceBold,
                                labelSizeForItem,
                                isMutedIncidentalBranch ? s_incidentalBranchForegroundBrush : s_remotePrefixAccentBrush);
                            labelText = decorator.Name.Substring(slashIdx + 1);
                        }
                    }

                    var label = new FormattedText(
                        labelText,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        labelTypeface,
                        labelSizeForItem,
                        labelBrush);

                    var item = new RenderItem()
                    {
                        PrefixLabel = prefixLabel,
                        Label = label,
                        RawLabel = decorator.Name,
                        LabelTypeface = labelTypeface,
                        LabelFontSize = labelSizeForItem,
                        Brush = normalBG,
                        BorderBrush = normalBG,
                        LabelBrush = labelBrush,
                        IconBrush = isCurrentCommitHead
                            ? s_headTagForegroundBrush
                            : isSuperProjectPointer
                                ? s_superProjectPointerForegroundBrush
                                : isParentRepository
                                    ? s_parentRepositoryForegroundBrush
                                : isMutedIncidentalBranch
                                    ? s_incidentalBranchForegroundBrush
                                    : fg,
                        UseSolidBackground = isSuperProjectPointer || isParentRepository,
                        IsHead = isHead,
                        IsCurrentCommitHead = isCurrentCommitHead,
                        Decorator = decorator,
                    };

                    if (secondaryDecorator != null)
                    {
                        item.SecondaryIcon = CreateIcon(this.FindResource("Icons.Remote") as StreamGeometry, 10.0);
                        item.IconBrush = isCurrentCommitHead
                            ? s_headTagForegroundBrush
                            : isSuperProjectPointer
                                ? s_superProjectPointerForegroundBrush
                                : isParentRepository
                                    ? s_parentRepositoryForegroundBrush
                                : s_compactLocalIconBrush;
                        item.SecondaryIconBrush = s_compactRemoteIconBrush;
                        item.PrimaryIconBackground = s_compactLocalIconBackgroundBrush;
                        item.SecondaryIconBackground = s_compactRemoteIconBackgroundBrush;
                        item.LeadingWidth = 29.0;
                    }

                    item.CanFold = decorator.IsBranchFoldable &&
                        decorator.Type is Models.DecoratorType.CurrentBranchHead or
                            Models.DecoratorType.LocalBranchHead or
                            Models.DecoratorType.RemoteBranchHead;
                    item.IsFolded = item.CanFold && decorator.IsBranchFolded;

                    if (decorator.Color != 0 &&
                        decorator.Type is Models.DecoratorType.CurrentBranchHead or
                                         Models.DecoratorType.LocalBranchHead or
                                         Models.DecoratorType.RemoteBranchHead)
                    {
                        item.Brush = new SolidColorBrush(Color.FromUInt32(decorator.Color));
                        item.BorderBrush = isMutedIncidentalBranch ? s_incidentalBranchBorderBrush : item.Brush;
                    }

                    if (isCurrentCommitHead)
                    {
                        item.Brush = s_headTagBackgroundBrush;
                        item.BorderBrush = s_headTagBorderBrush;
                    }

                    StreamGeometry geo;
                    switch (decorator.Type)
                    {
                        case Models.DecoratorType.CurrentBranchHead:
                            geo = secondaryDecorator != null
                                ? this.FindResource("Icons.Laptop") as StreamGeometry
                                : this.FindResource("Icons.Head") as StreamGeometry;
                            break;
                        case Models.DecoratorType.CurrentCommitHead:
                            geo = this.FindResource("Icons.Head") as StreamGeometry;
                            break;
                        case Models.DecoratorType.ParentRepository:
                            item.Brush = s_parentRepositoryBackgroundBrush;
                            item.BorderBrush = s_parentRepositoryBorderBrush;
                            geo = this.FindResource("Icons.Submodule") as StreamGeometry;
                            break;
                        case Models.DecoratorType.RemoteBranchHead:
                            geo = this.FindResource("Icons.Remote") as StreamGeometry;
                            break;
                        case Models.DecoratorType.SuperProjectPointer:
                            item.Brush = s_superProjectPointerBackgroundBrush;
                            item.BorderBrush = s_superProjectPointerBorderBrush;
                            geo = this.FindResource("Icons.Submodule") as StreamGeometry;
                            break;
                        case Models.DecoratorType.Tag:
                            item.Brush = Brushes.Gray;
                            geo = this.FindResource("Icons.Tag") as StreamGeometry;
                            break;
                        default:
                            geo = secondaryDecorator != null
                                ? this.FindResource("Icons.Laptop") as StreamGeometry
                                : this.FindResource("Icons.Branch") as StreamGeometry;
                            break;
                    }

                    item.Icon = CreateIcon(geo, 10.0);
                    if (item.CanFold)
                    {
                        item.FoldButtonBackground = item.IsFolded
                            ? new SolidColorBrush(Color.Parse("#FFD54F"))
                            : new SolidColorBrush(Color.Parse("#ECEFF1"));
                        item.FoldButtonForeground = Brushes.Black;
                        item.FoldLabel = new FormattedText(
                            item.IsFolded ? "+" : "-",
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typefaceBold,
                            labelSize + 2,
                            item.FoldButtonForeground);
                    }

                    var contentHeight = Math.Max(
                        item.Label.Height,
                        item.PrefixLabel?.Height ?? 0.0);
                    item.Height = Math.Max(16.0, Math.Ceiling(contentHeight + 4.0));

                    var prefixWidth = prefixLabel?.WidthIncludingTrailingWhitespace ?? 0.0;
                    item.Width = item.LeadingWidth + (isHead ? 0 : 4) + prefixWidth + label.Width + 4;
                    if (item.CanFold)
                        item.Width += 18;
                    _items.Add(item);
                    currentLineHeight = Math.Max(currentLineHeight, item.Height);
                    requiredHeight = Math.Max(requiredHeight, currentLineHeight);

                    x += item.Width + 4;
                    if (allowWrap)
                    {
                        if (x > availableSize.Width)
                        {
                            requiredHeight += currentLineHeight + 4.0;
                            currentLineHeight = item.Height;
                            x = item.Width;
                        }
                    }
                }

                var requiredWidth = allowWrap && requiredHeight > 16.0
                    ? availableSize.Width
                    : x + 2;
                return new Size(requiredWidth, requiredHeight);
            }

            return new Size(0, 0);
        }

        private void DrawLabelHighlights(DrawingContext context, RenderItem item, double x, double y)
        {
            var query = HighlightText;
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(item.RawLabel))
                return;

            var start = 0;
            while (true)
            {
                var found = item.RawLabel.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    break;

                var prefixWidth = 0.0;
                if (found > 0)
                {
                    var prefix = new FormattedText(
                        item.RawLabel.Substring(0, found),
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        item.LabelTypeface,
                        item.LabelFontSize,
                        item.LabelBrush);
                    prefixWidth = prefix.WidthIncludingTrailingWhitespace;
                }

                var matchLen = Math.Min(query.Length, item.RawLabel.Length - found);
                var match = new FormattedText(
                    item.RawLabel.Substring(found, matchLen),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    item.LabelTypeface,
                    item.LabelFontSize,
                    item.LabelBrush);

                var rect = new RoundedRect(
                    new Rect(x + prefixWidth - 1, y, match.WidthIncludingTrailingWhitespace + 2, item.Label.Height),
                    new CornerRadius(3));
                context.DrawRectangle(s_highlightBackground, null, rect);

                var highlightText = new FormattedText(
                    item.RawLabel.Substring(found, matchLen),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    item.LabelTypeface,
                    item.LabelFontSize,
                    s_highlightForeground);
                context.DrawText(highlightText, new Point(x + prefixWidth, y));
                start = found + matchLen;
            }
        }

        private bool TryGetItemAtPoint(Point point, out RenderItem found, out Rect foldRect)
        {
            found = null;
            foldRect = default;

            var allowWrap = AllowWrap;
            var x = 1.5;
            var y = 0.5;
            foreach (var item in _items)
            {
                if (allowWrap && x > 1.5 && x + item.Width > Bounds.Width)
                {
                    x = 1.5;
                    y += item.Height + 4.0;
                }

                var itemRect = new Rect(x, y, item.Width, item.Height);
                if (itemRect.Contains(point))
                {
                    found = item;
                    if (item.CanFold)
                        foldRect = new Rect(x + item.Width - 18, y, 18, item.Height);

                    return true;
                }

                x += item.Width + 4;
            }

            return false;
        }

        private static Geometry CreateIcon(StreamGeometry source, double size)
        {
            if (source == null)
                return null;

            var drawGeo = source.Clone();
            var iconBounds = drawGeo.Bounds;
            if (iconBounds.Width <= 0 || iconBounds.Height <= 0)
                return drawGeo;

            var translation = Matrix.CreateTranslation(-(Vector)iconBounds.Position);
            var scale = Math.Min(size / iconBounds.Width, size / iconBounds.Height);
            var transform = translation * Matrix.CreateScale(scale, scale);
            if (drawGeo.Transform == null || drawGeo.Transform.Value == Matrix.Identity)
                drawGeo.Transform = new MatrixTransform(transform);
            else
                drawGeo.Transform = new MatrixTransform(drawGeo.Transform.Value * transform);
            return drawGeo;
        }

        private static int FindCompactRemoteMatch(List<Models.Decorator> refs, string localName)
        {
            if (refs == null || string.IsNullOrWhiteSpace(localName))
                return -1;

            var exactOriginName = $"origin/{localName}";
            var firstMatch = -1;
            var matchCount = 0;

            for (var i = 0; i < refs.Count; i++)
            {
                var decorator = refs[i];
                if (decorator.Type != Models.DecoratorType.RemoteBranchHead)
                    continue;

                if (!GetRemoteLeafName(decorator.Name).Equals(localName, StringComparison.Ordinal))
                    continue;

                if (decorator.Name.Equals(exactOriginName, StringComparison.Ordinal))
                    return i;

                if (firstMatch < 0)
                    firstMatch = i;

                matchCount++;
            }

            return matchCount == 1 ? firstMatch : -1;
        }

        private static string GetRemoteLeafName(string remoteName)
        {
            if (string.IsNullOrWhiteSpace(remoteName))
                return string.Empty;

            var slashIdx = remoteName.IndexOf('/');
            return slashIdx >= 0 && slashIdx + 1 < remoteName.Length
                ? remoteName.Substring(slashIdx + 1)
                : remoteName;
        }

        private List<RenderItem> _items = new List<RenderItem>();
        private static readonly IBrush s_headTagBackgroundBrush = new SolidColorBrush(Color.Parse("#C62828"));
        private static readonly IBrush s_headTagBorderBrush = new SolidColorBrush(Color.Parse("#7F0000"));
        private static readonly IBrush s_headTagForegroundBrush = new SolidColorBrush(Color.Parse("#FFEB3B"));
        private static readonly IBrush s_superProjectPointerBackgroundBrush = new SolidColorBrush(Color.Parse("#005BBB"));
        private static readonly IBrush s_superProjectPointerBorderBrush = new SolidColorBrush(Color.Parse("#003A75"));
        private static readonly IBrush s_superProjectPointerForegroundBrush = new SolidColorBrush(Color.Parse("#FFEB3B"));
        private static readonly IBrush s_parentRepositoryBackgroundBrush = new SolidColorBrush(Color.Parse("#1B5E20"));
        private static readonly IBrush s_parentRepositoryBorderBrush = new SolidColorBrush(Color.Parse("#0F3A12"));
        private static readonly IBrush s_parentRepositoryForegroundBrush = new SolidColorBrush(Color.Parse("#FFEB3B"));
        private static readonly IBrush s_highlightBackground = new SolidColorBrush(Color.Parse("#E6F2C200"));
        private static readonly IBrush s_highlightForeground = Brushes.Black;
        private static readonly IBrush s_remotePrefixAccentBrush = new SolidColorBrush(Color.Parse("#1565C0"));
        private static readonly IBrush s_incidentalBranchForegroundBrush = new SolidColorBrush(Color.Parse("#FF9AA0A6"));
        private static readonly IBrush s_incidentalBranchBorderBrush = new SolidColorBrush(Color.Parse("#CC202124"));
        private static readonly IBrush s_compactLocalIconBrush = Brushes.White;
        private static readonly IBrush s_compactRemoteIconBrush = Brushes.White;
        private static readonly IBrush s_compactLocalIconBackgroundBrush = new SolidColorBrush(Color.Parse("#FF1E8E3E"));
        private static readonly IBrush s_compactRemoteIconBackgroundBrush = new SolidColorBrush(Color.Parse("#FF1565C0"));

        private static bool IsMutedIncidentalBranch(Models.Decorator decorator)
        {
            if (decorator == null ||
                decorator.Color == 0 ||
                decorator.Type is not Models.DecoratorType.CurrentBranchHead and
                    not Models.DecoratorType.LocalBranchHead and
                    not Models.DecoratorType.RemoteBranchHead)
            {
                return false;
            }

            var color = Color.FromUInt32(decorator.Color);
            return color.A <= 0x40;
        }
    }
}
