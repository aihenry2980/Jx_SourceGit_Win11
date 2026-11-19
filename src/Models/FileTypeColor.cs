using System;
using System.IO;
using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    public class FileTypeColor : ObservableObject
    {
        public const uint DEFAULT_COLOR = 0xFF656D78;

        public string Extension
        {
            get => _extension;
            set
            {
                if (SetProperty(ref _extension, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(ExtensionKey));
                    OnPropertyChanged(nameof(PreviewLabel));
                }
            }
        }

        public uint Color
        {
            get => _color;
            set
            {
                if (SetProperty(ref _color, value | 0xFF000000))
                {
                    OnPropertyChanged(nameof(BackgroundBrush));
                    OnPropertyChanged(nameof(ForegroundBrush));
                }
            }
        }

        [JsonIgnore]
        public string ExtensionKey
        {
            get
            {
                var extension = _extension.Trim();
                if (extension.StartsWith("*.", StringComparison.Ordinal))
                    extension = extension.Substring(2);
                return extension.TrimStart('.').ToUpperInvariant();
            }
        }

        [JsonIgnore]
        public string PreviewLabel => ExtensionKey.Length > 0 ? ExtensionKey : "EXT";

        [JsonIgnore]
        public IBrush BackgroundBrush => new SolidColorBrush(Avalonia.Media.Color.FromUInt32(_color));

        [JsonIgnore]
        public IBrush ForegroundBrush => GetForegroundBrush(_color);

        public static string GetExtensionKey(string path)
        {
            var name = Path.GetFileName(path ?? string.Empty);
            var extension = Path.GetExtension(name);
            return extension.Length < 2 || (name.StartsWith('.') && name.IndexOf('.', 1) < 0)
                ? string.Empty : extension.Substring(1).ToUpperInvariant();
        }

        public static IBrush GetForegroundBrush(uint color)
        {
            var background = Avalonia.Media.Color.FromUInt32(color);
            static double Linear(byte channel) => channel <= 10 ? channel / 3294.6 : Math.Pow((channel / 255.0 + 0.055) / 1.055, 2.4);
            var luminance = 0.2126 * Linear(background.R) + 0.7152 * Linear(background.G) + 0.0722 * Linear(background.B);
            return luminance > 0.179 ? Brushes.Black : Brushes.White;
        }

        private string _extension = string.Empty;
        private uint _color = DEFAULT_COLOR;
    }
}
