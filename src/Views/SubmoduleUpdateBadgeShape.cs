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

            var notch = System.Math.Min(8.0, height * 0.42);
            _geometry = new StreamGeometry();
            using (var shape = _geometry.Open())
            {
                shape.BeginFigure(new Point(notch, 0.5), true);
                shape.LineTo(new Point(width - 0.5, 0.5));
                shape.LineTo(new Point(width - 0.5, height - 0.5));
                shape.LineTo(new Point(notch, height - 0.5));
                shape.LineTo(new Point(0.5, height * 0.5));
                shape.EndFigure(true);
            }

            var color = Color.FromUInt32(AccentColor);
            _fill = new SolidColorBrush(Color.FromArgb(
                color.A,
                ToPastelChannel(color.R),
                ToPastelChannel(color.G),
                ToPastelChannel(color.B)));
            _borderPen = new Pen(new SolidColorBrush(color));
            _accentColor = AccentColor;
            _renderWidth = width;
            _renderHeight = height;
        }

        private static byte ToPastelChannel(byte channel)
        {
            return (byte)(channel + (255 - channel) * 0.78);
        }

        private StreamGeometry _geometry = null;
        private IBrush _fill = null;
        private Pen _borderPen = null;
        private uint _accentColor = 0;
        private double _renderWidth = double.NaN;
        private double _renderHeight = double.NaN;
    }
}
