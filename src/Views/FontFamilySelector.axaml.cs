using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class FontFamilySelector : ChromelessWindow
    {
        public FontFamilySelector()
            : this(Array.Empty<string>())
        {
        }

        public FontFamilySelector(IEnumerable<string> fonts, string selected = null, string title = null)
        {
            CloseOnESC = true;
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(title))
                Title = title;

            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _allFonts = [];
            if (fonts != null)
            {
                foreach (var raw in fonts)
                {
                    var name = raw?.Trim();
                    if (!string.IsNullOrEmpty(name) && dedupe.Add(name))
                        _allFonts.Add(name);
                }
            }

            _allFonts.Sort(StringComparer.OrdinalIgnoreCase);
            _preferred = selected?.Trim() ?? string.Empty;
            ApplyFilter();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            SearchBox.Focus();
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
            e.Handled = true;
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e is not { Key: Key.Enter, KeyModifiers: KeyModifiers.None })
                return;

            ConfirmAndClose();
            e.Handled = true;
        }

        private void OnListDoubleTapped(object sender, TappedEventArgs e)
        {
            ConfirmAndClose();
            e.Handled = true;
        }

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            ConfirmAndClose();
            e.Handled = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close(string.Empty);
            e.Handled = true;
        }

        private void ApplyFilter()
        {
            var query = SearchBox?.Text?.Trim() ?? string.Empty;
            List<string> visible;
            if (string.IsNullOrEmpty(query))
            {
                visible = _allFonts;
            }
            else
            {
                var starts = _allFonts
                    .Where(x => x.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
                var contains = _allFonts
                    .Where(x => !x.StartsWith(query, StringComparison.OrdinalIgnoreCase) && x.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
                visible = starts.Concat(contains).ToList();
            }

            FontList.ItemsSource = visible;
            if (visible.Count == 0)
                return;

            var current = FontList.SelectedItem as string;
            var target = PickPreferredSelection(visible, current);
            if (target == null)
                target = visible[0];

            FontList.SelectedItem = target;
            FontList.ScrollIntoView(target);
        }

        private string PickPreferredSelection(List<string> visible, string current)
        {
            if (!string.IsNullOrEmpty(current))
            {
                foreach (var one in visible)
                {
                    if (one.Equals(current, StringComparison.OrdinalIgnoreCase))
                        return one;
                }
            }

            if (!string.IsNullOrEmpty(_preferred))
            {
                foreach (var one in visible)
                {
                    if (one.Equals(_preferred, StringComparison.OrdinalIgnoreCase))
                        return one;
                }
            }

            return null;
        }

        private void ConfirmAndClose()
        {
            if (FontList.SelectedItem is string name && !string.IsNullOrWhiteSpace(name))
                Close(name.Trim());
            else
                Close(string.Empty);
        }

        private readonly List<string> _allFonts;
        private readonly string _preferred;
    }
}
