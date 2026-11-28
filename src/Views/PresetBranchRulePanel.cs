using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;

namespace SourceGit.Views
{
    // Keeps rule chips and the trailing input in the same wrapping flow.
    public class PresetBranchRulePanel : Panel
    {
        public static readonly StyledProperty<IEnumerable<string>> RulesProperty =
            AvaloniaProperty.Register<PresetBranchRulePanel, IEnumerable<string>>(nameof(Rules));

        public static readonly StyledProperty<IDataTemplate> RuleTemplateProperty =
            AvaloniaProperty.Register<PresetBranchRulePanel, IDataTemplate>(nameof(RuleTemplate));

        public IEnumerable<string> Rules
        {
            get => GetValue(RulesProperty);
            set => SetValue(RulesProperty, value);
        }

        public IDataTemplate RuleTemplate
        {
            get => GetValue(RuleTemplateProperty);
            set => SetValue(RuleTemplateProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property != RulesProperty && change.Property != RuleTemplateProperty)
                return;

            foreach (var chip in _chips)
                Children.Remove(chip);
            _chips.Clear();

            if (Rules == null || RuleTemplate == null)
                return;

            foreach (var rule in Rules)
            {
                var chip = new ContentPresenter { Content = rule, ContentTemplate = RuleTemplate };
                Children.Insert(_chips.Count, chip);
                _chips.Add(chip);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children)
            {
                // Reserve a usable input on the last row, without letting its
                // watermark or unfinished text push otherwise fitting chips down.
                var width = child is TextBox ? Math.Min(80, availableSize.Width) : availableSize.Width;
                child.Measure(new Size(width, double.PositiveInfinity));
            }

            return Layout(availableSize.Width, false);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Layout(finalSize.Width, true);
            return finalSize;
        }

        private Size Layout(double width, bool arrange)
        {
            var rowHeight = 0.0;
            foreach (var child in Children)
            {
                if (child.IsVisible)
                    rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            var x = 0.0;
            var y = 0.0;
            var maxWidth = 0.0;
            foreach (var child in Children)
            {
                if (!child.IsVisible)
                    continue;

                var childWidth = Math.Min(width, child.DesiredSize.Width);
                if (x > 0 && x + childWidth > width)
                {
                    x = 0;
                    y += rowHeight + 2;
                }

                if (arrange)
                {
                    var arrangedWidth = child is TextBox ? Math.Max(0, width - x) : childWidth;
                    child.Arrange(new Rect(x, y, arrangedWidth, rowHeight));
                }

                x += childWidth;
                maxWidth = Math.Max(maxWidth, x);
            }

            return new Size(maxWidth, y + rowHeight);
        }

        private readonly List<ContentPresenter> _chips = [];
    }
}
