using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class HistorySubmoduleFilter : UserControl
    {
        public event Action<IReadOnlyList<string>> ApplyRequested;
        public event Action ClearFilterRequested;

        public HistorySubmoduleFilter()
        {
            InitializeComponent();
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.HistorySubmoduleFilter vm)
                vm.SelectAll();

            e.Handled = true;
        }

        private void OnClearSelection(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.HistorySubmoduleFilter vm)
                vm.ClearSelection();

            e.Handled = true;
        }

        private void OnClearFilter(object sender, RoutedEventArgs e)
        {
            ClearFilterRequested?.Invoke();
            e.Handled = true;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.HistorySubmoduleFilter vm)
                ApplyRequested?.Invoke(vm.GetSelectedPaths());

            e.Handled = true;
        }
    }
}
