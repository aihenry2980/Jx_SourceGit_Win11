using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace SourceGit.Views
{
    public partial class GotoLineBar : UserControl
    {
        public GotoLineBar()
        {
            InitializeComponent();
        }

        // Raised on each valid input while typing (live preview jump).
        public event EventHandler<int> Preview;
        // Raised when Enter/Escape closes the bar.
        public event EventHandler Closed;

        public void Open(int maxLine)
        {
            _maxLine = maxLine;
            if (maxLine <= 0)
            {
                Close();
                return;
            }

            TxtLine.Watermark = App.Text("GotoLine.Range", 1, maxLine);
            TxtLine.Text = string.Empty;
            IsVisible = true;
            Dispatcher.UIThread.Post(() => TxtLine.Focus(), DispatcherPriority.Input);
        }

        public void Close()
        {
            IsVisible = false;
        }

        private void OnInputTextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsVisible && _maxLine > 0 && int.TryParse(TxtLine.Text, out var line) && line >= 1)
                Preview?.Invoke(this, Math.Min(line, _maxLine));
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnInputLostFocus(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private int _maxLine = 0;
    }

    public class LineFlashRenderer : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public LineFlashRenderer(TextEditor owner, Func<int> getFlashLine)
        {
            _owner = owner;
            _getFlashLine = getFlashLine;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var flash = _getFlashLine();
            if (flash <= 0 || !textView.VisualLinesValid)
                return;

            var width = textView.Bounds.Width;
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
                return;

            var color = (Color)_owner.FindResource("SystemAccentColor")!;
            var brush = new SolidColorBrush(color, 0.35);

            foreach (var line in textView.VisualLines)
            {
                if (line.IsDisposed || line.FirstDocumentLine == null || line.FirstDocumentLine.IsDeleted)
                    continue;

                if (line.FirstDocumentLine.LineNumber != flash)
                    continue;

                var startY = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineTop) - textView.VerticalOffset;
                var endY = line.GetTextLineVisualYPosition(line.TextLines[^1], VisualYPosition.LineBottom) - textView.VerticalOffset;
                drawingContext.FillRectangle(brush, new Rect(0, startY, width, endY - startY));
            }
        }

        private readonly TextEditor _owner;
        private readonly Func<int> _getFlashLine;
    }
}
