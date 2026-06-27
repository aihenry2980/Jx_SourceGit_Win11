using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class PopupRunningStatus : UserControl
    {
        public static readonly DirectProperty<PopupRunningStatus, string> DescriptionProperty =
            AvaloniaProperty.RegisterDirect<PopupRunningStatus, string>(
                nameof(Description),
                static o => o.Description,
                static (o, v) => o.Description = v);
        public static readonly StyledProperty<string> ActionButtonTextProperty =
            AvaloniaProperty.Register<PopupRunningStatus, string>(nameof(ActionButtonText), string.Empty);
        public static readonly StyledProperty<bool> IsActionButtonVisibleProperty =
            AvaloniaProperty.Register<PopupRunningStatus, bool>(nameof(IsActionButtonVisible), false);
        public static readonly StyledProperty<string> SecondaryActionButtonTextProperty =
            AvaloniaProperty.Register<PopupRunningStatus, string>(nameof(SecondaryActionButtonText), string.Empty);
        public static readonly StyledProperty<bool> IsSecondaryActionButtonVisibleProperty =
            AvaloniaProperty.Register<PopupRunningStatus, bool>(nameof(IsSecondaryActionButtonVisible), false);

        public event EventHandler<RoutedEventArgs> ActionButtonClick;
        public event EventHandler<RoutedEventArgs> SecondaryActionButtonClick;

        public string Description
        {
            get => _description;
            set => SetAndRaise(DescriptionProperty, ref _description, value);
        }

        public string ActionButtonText
        {
            get => GetValue(ActionButtonTextProperty);
            set => SetValue(ActionButtonTextProperty, value);
        }

        public bool IsActionButtonVisible
        {
            get => GetValue(IsActionButtonVisibleProperty);
            set => SetValue(IsActionButtonVisibleProperty, value);
        }

        public string SecondaryActionButtonText
        {
            get => GetValue(SecondaryActionButtonTextProperty);
            set => SetValue(SecondaryActionButtonTextProperty, value);
        }

        public bool IsSecondaryActionButtonVisible
        {
            get => GetValue(IsSecondaryActionButtonVisibleProperty);
            set => SetValue(IsSecondaryActionButtonVisibleProperty, value);
        }

        public PopupRunningStatus()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            _isUnloading = false;
            if (IsVisible)
                StartAnim();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            _isUnloading = true;
            base.OnUnloaded(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsVisibleProperty)
            {
                if (IsVisible && !_isUnloading)
                    StartAnim();
                else
                    StopAnim();
            }
        }

        private void StartAnim()
        {
            Icon.Content = new Path() { Classes = { "waiting" } };
            ProgressBar.IsIndeterminate = true;
        }

        private void StopAnim()
        {
            if (Icon.Content is Path path)
                path.Classes.Clear();
            Icon.Content = null;
            ProgressBar.IsIndeterminate = false;
        }

        private void OnActionButtonClick(object sender, RoutedEventArgs e)
        {
            ActionButtonClick?.Invoke(this, e);
            e.Handled = true;
        }

        private void OnSecondaryActionButtonClick(object sender, RoutedEventArgs e)
        {
            SecondaryActionButtonClick?.Invoke(this, e);
            e.Handled = true;
        }

        private string _description = string.Empty;
        private bool _isUnloading = false;
    }
}
