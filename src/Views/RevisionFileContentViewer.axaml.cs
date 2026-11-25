using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;

namespace SourceGit.Views
{
    public class RevisionTextFileView : TextEditor
    {
        public static readonly StyledProperty<int> TabWidthProperty =
            AvaloniaProperty.Register<RevisionTextFileView, int>(nameof(TabWidth), 4);

        public int TabWidth
        {
            get => GetValue(TabWidthProperty);
            set => SetValue(TabWidthProperty, value);
        }

        protected override Type StyleKeyOverride => typeof(TextEditor);

        public RevisionTextFileView() : base(new TextArea(), new TextDocument())
        {
            IsReadOnly = true;
            ShowLineNumbers = true;
            WordWrap = false;

            Options.IndentationSize = TabWidth;
            Options.EnableHyperlinks = false;
            Options.EnableEmailHyperlinks = false;

            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            TextArea.LeftMargins[0].Margin = new Thickness(8, 0);
            TextArea.TextView.Margin = new Thickness(4, 0);
            TextArea.TextView.BackgroundRenderers.Add(new LineFlashRenderer(this, () => _flashLine));
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            TextArea.TextView.ContextRequested += OnTextViewContextRequested;

            _textMate ??= Models.TextMateHelper.CreateForEditor(this);
            if (DataContext is Models.RevisionTextFile file)
                Models.TextMateHelper.SetGrammarByFileName(_textMate, file.FileName);
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            TextArea.TextView.ContextRequested -= OnTextViewContextRequested;

            if (_textMate != null)
            {
                _textMate.Dispose();
                _textMate = null;
            }

            GC.Collect();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is Models.RevisionTextFile source)
            {
                Text = source.Content;
                Models.TextMateHelper.SetGrammarByFileName(_textMate, source.FileName);
                ScrollToHome();
            }
            else
            {
                Text = string.Empty;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == TabWidthProperty)
                Options.IndentationSize = TabWidth;
            else if (change.Property.Name == nameof(ActualThemeVariant) && change.NewValue != null)
                Models.TextMateHelper.SetThemeByApp(_textMate);
        }

        public void FlashLine(int line)
        {
            _flashLine = line;
            _flashUntil = Environment.TickCount64 + 800;
            TextArea.TextView.InvalidateLayer(KnownLayer.Background);

            DispatcherTimer.RunOnce(() =>
            {
                if (Environment.TickCount64 >= _flashUntil)
                {
                    _flashLine = -1;
                    TextArea.TextView.InvalidateLayer(KnownLayer.Background);
                }
            }, TimeSpan.FromMilliseconds(800));
        }

        private void OnTextViewContextRequested(object sender, ContextRequestedEventArgs e)
        {
            var selected = SelectedText;
            if (string.IsNullOrEmpty(selected))
                return;

            var copy = new MenuItem() { Header = App.Text("Copy") };
            copy.Click += async (_, ev) =>
            {
                await this.CopyTextAsync(selected);
                ev.Handled = true;
            };

            if (this.FindResource("Icons.Copy") is Geometry geo)
            {
                copy.Icon = new Avalonia.Controls.Shapes.Path()
                {
                    Width = 10,
                    Height = 10,
                    Stretch = Stretch.Uniform,
                    Data = geo,
                };
            }

            var menu = new ContextMenu();
            menu.Items.Add(copy);
            menu.Open(TextArea.TextView);

            e.Handled = true;
        }

        private TextMate.Installation _textMate = null;
        private int _flashLine = -1;
        private long _flashUntil;
    }

    public partial class RevisionFileContentViewer : UserControl
    {
        public RevisionFileContentViewer()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            FindGotoLineBar()?.Close();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ContentProperty)
                FindGotoLineBar()?.Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (e.Key == Key.G && e.KeyModifiers == modifier && ShowGotoLine())
                e.Handled = true;
        }

        private async void OnOpenBinaryFileViewer(object sender, RoutedEventArgs e)
        {
            if (sender is not Control { DataContext: Models.RevisionBinaryFile vm })
                return;

            await this.ShowDialogAsync(new ViewModels.BinaryFileViewer(vm.Repository, vm.File, vm.Revision));
            e.Handled = true;
        }

        private bool ShowGotoLine()
        {
            var bar = FindGotoLineBar();
            var editor = FindEditor();
            if (bar == null || editor?.Document == null)
                return false;

            if (bar.IsVisible)
            {
                bar.Close();
                return true;
            }

            bar.Open(editor.Document.LineCount);
            return true;
        }

        private void OnGotoLinePreview(object sender, int line)
        {
            var editor = FindEditor();
            if (editor == null)
                return;

            editor.TextArea.Caret.Line = line;
            editor.ScrollToLine(line);
            editor.FlashLine(line);
        }

        private void OnGotoLineClosed(object sender, EventArgs e)
        {
            FindGotoLineBar()?.Close();
            FindEditor()?.TextArea.Focus();
        }

        private GotoLineBar FindGotoLineBar() => this.FindDescendantOfType<GotoLineBar>();

        private RevisionTextFileView FindEditor() => this.FindDescendantOfType<RevisionTextFileView>();
    }
}
