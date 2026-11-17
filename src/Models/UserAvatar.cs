using System;
using System.IO;
using System.Text.Json.Serialization;

using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    public class UserAvatar : ObservableObject
    {
        public string Email
        {
            get => _email;
            set
            {
                if (SetProperty(ref _email, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasEmail));
            }
        }

        public string ImageData
        {
            get => _imageData;
            set
            {
                var data = value?.Length <= 128 * 1024 ? value : string.Empty;
                data ??= string.Empty;
                if (_imageData == data)
                    return;
                var previous = _preview;
                _preview = null;
                _decoded = false;
                SetProperty(ref _imageData, data);
                OnPropertyChanged(nameof(Preview));
                previous?.Dispose();
            }
        }

        [JsonIgnore]
        public bool HasEmail => !string.IsNullOrWhiteSpace(_email);

        [JsonIgnore]
        public Bitmap Preview
        {
            get
            {
                if (!_decoded)
                {
                    _decoded = true;
                    try
                    {
                        if (!string.IsNullOrEmpty(_imageData))
                        {
                            using var stream = new MemoryStream(Convert.FromBase64String(_imageData));
                            _preview = new Bitmap(stream);
                        }
                    }
                    catch { _preview = null; }
                }
                return _preview;
            }
        }

        public void SetFromFile(string file)
        {
            using var original = new Bitmap(file);
            using var thumbnail = new RenderTargetBitmap(new PixelSize(64, 64));
            var size = original.Size;
            var edge = Math.Min(size.Width, size.Height);
            using (var context = thumbnail.CreateDrawingContext())
                context.DrawImage(original, new Rect((size.Width - edge) / 2, (size.Height - edge) / 2, edge, edge), new Rect(0, 0, 64, 64));
            using var output = new MemoryStream();
            thumbnail.Save(output);
            ImageData = Convert.ToBase64String(output.ToArray());
        }

        private string _email = string.Empty;
        private string _imageData = string.Empty;
        private Bitmap _preview;
        private bool _decoded;
    }
}
