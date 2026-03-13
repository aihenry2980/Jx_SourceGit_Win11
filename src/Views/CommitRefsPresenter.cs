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
            public FormattedText Label { get; set; } = null;
            public FormattedText FoldLabel { get; set; } = null;
            public IBrush Brush { get; set; } = null;
            public IBrush BorderBrush { get; set; } = null;
            public IBrush IconBrush { get; set; } = null;
            public IBrush FoldButtonBackground { get; set; } = null;
            public IBrush FoldButtonForeground { get; set; } = null;
            public bool IsHead { get; set; } = false;
            public bool IsCurrentCommitHead { get; set; } = false;
            public bool UseSolidBackground { get; set; } = false;
            public bool CanFold { get; set; } = false;
            public bool IsFolded { get; set; } = false;
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

        static CommitRefsPresenter()
        {
            AffectsMeasure<CommitRefsPresenter>(
                FontFamilyProperty,
                FontSizeProperty,
                ForegroundProperty,
                UseGraphColorProperty,
                BackgroundProperty,
                ShowTagsProperty);
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
                    y += 20.0;
                }

                var entireRect = new RoundedRect(new Rect(x, y, item.Width, 16), new CornerRadius(4));

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

                    context.DrawText(item.Label, new Point(x + 16, y + 8.0 - item.Label.Height * 0.5));
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

                        var labelRect = new RoundedRect(new Rect(x + 16, y, item.Label.Width + 8, 16), new CornerRadius(0, 4, 4, 0));
                        using (context.PushOpacity(.2))
                            context.DrawRectangle(item.Brush, null, labelRect);

                        context.DrawLine(new Pen(item.Brush), new Point(x + 16, y), new Point(x + 16, y + 16));
                    }

                    context.DrawText(item.Label, new Point(x + 20, y + 8.0 - item.Label.Height * 0.5));
                }

                var borderBrush = item.BorderBrush ?? item.Brush;
                context.DrawRectangle(null, new Pen(borderBrush), entireRect);

                using (context.PushTransform(Matrix.CreateTranslation(x + 3, y + 3)))
                    context.DrawGeometry(item.IconBrush ?? fg, null, item.Icon);

                if (item.CanFold)
                {
                    var foldButtonX = x + item.Width - 17;
                    var foldButtonRect = new RoundedRect(new Rect(foldButtonX, y + 1, 15, 14), new CornerRadius(3));

                    context.DrawRectangle(
                        item.FoldButtonBackground ?? Brushes.LightGray,
                        new Pen(borderBrush, 1.2),
                        foldButtonRect);

                    context.DrawLine(
                        new Pen(borderBrush, 1.2),
                        new Point(foldButtonX - 2, y + 2),
                        new Point(foldButtonX - 2, y + 14));

                    if (item.FoldLabel != null)
                        context.DrawText(item.FoldLabel, new Point(foldButtonX + (15 - item.FoldLabel.Width) * 0.5, y + 8.0 - item.FoldLabel.Height * 0.5));
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
                var x = 0.0;
                var allowWrap = AllowWrap;
                var showTags = ShowTags;

                foreach (var decorator in refs)
                {
                    if (!showTags && decorator.Type == Models.DecoratorType.Tag)
                        continue;

                    var isHead = decorator.Type is Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.CurrentCommitHead;
                    var isCurrentCommitHead = decorator.Type == Models.DecoratorType.CurrentCommitHead;
                    var isSuperProjectPointer = decorator.Type == Models.DecoratorType.SuperProjectPointer;
                    var isParentRepository = decorator.Type == Models.DecoratorType.ParentRepository;
                    var labelBrush = isCurrentCommitHead
                        ? s_headTagForegroundBrush
                        : isSuperProjectPointer
                            ? s_superProjectPointerForegroundBrush
                            : isParentRepository
                                ? s_parentRepositoryForegroundBrush
                            : fg;

                    var labelTypeface = isHead || isSuperProjectPointer || isParentRepository ? typefaceBold : typeface;
                    var labelSizeForItem = isHead ? labelSize + 1 : labelSize;
                    var label = new FormattedText(
                        decorator.Name,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        labelTypeface,
                        labelSizeForItem,
                        labelBrush);

                    var item = new RenderItem()
                    {
                        Label = label,
                        Brush = normalBG,
                        BorderBrush = normalBG,
                        IconBrush = isCurrentCommitHead
                            ? s_headTagForegroundBrush
                            : isSuperProjectPointer
                                ? s_superProjectPointerForegroundBrush
                                : isParentRepository
                                    ? s_parentRepositoryForegroundBrush
                                : fg,
                        UseSolidBackground = isSuperProjectPointer || isParentRepository,
                        IsHead = isHead,
                        IsCurrentCommitHead = isCurrentCommitHead,
                        Decorator = decorator,
                    };

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
                        item.BorderBrush = item.Brush;
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
                            geo = this.FindResource("Icons.Branch") as StreamGeometry;
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

                    item.Width = 16 + (isHead ? 0 : 4) + label.Width + 4;
                    if (item.CanFold)
                        item.Width += 18;
                    _items.Add(item);

                    x += item.Width + 4;
                    if (allowWrap)
                    {
                        if (x > availableSize.Width)
                        {
                            requiredHeight += 20.0;
                            x = item.Width;
                        }
                    }
                }

                var requiredWidth = allowWrap && requiredHeight > 16.0
                    ? availableSize.Width
                    : x + 2;
                InvalidateVisual();
                return new Size(requiredWidth, requiredHeight);
            }

            InvalidateVisual();
            return new Size(0, 0);
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
                    y += 20.0;
                }

                var itemRect = new Rect(x, y, item.Width, 16);
                if (itemRect.Contains(point))
                {
                    found = item;
                if (item.CanFold)
                        foldRect = new Rect(x + item.Width - 18, y, 18, 16);

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
    }
}
