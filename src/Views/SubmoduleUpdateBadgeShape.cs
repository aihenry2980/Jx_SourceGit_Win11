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

            var notch = System.Math.Min(8.0, height * 0.42);
            var geometry = new StreamGeometry();
            using (var shape = geometry.Open())
            {
                shape.BeginFigure(new Point(notch, 0.5), true);
                shape.LineTo(new Point(width - 0.5, 0.5));
                shape.LineTo(new Point(width - 0.5, height - 0.5));
                shape.LineTo(new Point(notch, height - 0.5));
                shape.LineTo(new Point(0.5, height * 0.5));
                shape.EndFigure(true);
            }

            var color = Color.FromUInt32(AccentColor);
            var fill = new SolidColorBrush(Color.FromArgb(
                color.A,
                ToPastelChannel(color.R),
                ToPastelChannel(color.G),
                ToPastelChannel(color.B)));
            var border = new SolidColorBrush(color);
            context.DrawGeometry(fill, new Pen(border), geometry);
        }

        private static byte ToPastelChannel(byte channel)
        {
            return (byte)(channel + (255 - channel) * 0.78);
        }
    }
}
