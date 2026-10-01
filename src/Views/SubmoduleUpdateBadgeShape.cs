using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Views
{
    public class SubmoduleUpdateBadgeShape : Control
    {
        public static readonly StyledProperty<uint> AccentColorProperty =
            AvaloniaProperty.Register<SubmoduleUpdateBadgeShape, uint>(nameof(AccentColor));

        public uint AccentColor
        {
            get => GetValue(AccentColorProperty);
            set => SetValue(AccentColorProperty, value);
        }

        static SubmoduleUpdateBadgeShape()
        {
            AffectsRender<SubmoduleUpdateBadgeShape>(AccentColorProperty);
        }

        public override void Render(DrawingContext context)
        {
            var width = Bounds.Width;
            var height = Bounds.Height;
            if (width <= 1 || height <= 1)
                return;

            EnsureDrawingResources(width, height);
            context.DrawGeometry(_fill, _borderPen, _geometry);
        }

        private void EnsureDrawingResources(double width, double height)
        {
            if (_geometry != null &&
                _accentColor == AccentColor &&
                System.Math.Abs(_renderWidth - width) < 0.01 &&
                System.Math.Abs(_renderHeight - height) < 0.01)
                return;

            _geometry = CreateArrowGeometry(new Rect(0.5, 0.5, width - 1, height - 1));

            var color = Color.FromUInt32(AccentColor);
            _fill = Brushes.Transparent;
            _borderPen = new Pen(new SolidColorBrush(color), 1, new DashStyle([3, 2], 0));
            _accentColor = AccentColor;
            _renderWidth = width;
            _renderHeight = height;
        }

        public static StreamGeometry CreateArrowGeometry(Rect bounds)
        {
            var head = System.Math.Min(10.0, bounds.Width * 0.25);
            var notch = System.Math.Min(4.0, bounds.Width * 0.1);
            var geometry = new StreamGeometry();
            using (var shape = geometry.Open())
            {
                shape.BeginFigure(bounds.TopLeft, true);
                shape.LineTo(new Point(bounds.Right - head, bounds.Top));
                shape.LineTo(new Point(bounds.Right, bounds.Center.Y));
                shape.LineTo(new Point(bounds.Right - head, bounds.Bottom));
                shape.LineTo(bounds.BottomLeft);
                shape.LineTo(new Point(bounds.Left + notch, bounds.Center.Y));
                shape.EndFigure(true);
            }
            return geometry;
        }

        private StreamGeometry _geometry = null;
        private IBrush _fill = null;
        private Pen _borderPen = null;
        private uint _accentColor = 0;
        private double _renderWidth = double.NaN;
        private double _renderHeight = double.NaN;
    }
}
