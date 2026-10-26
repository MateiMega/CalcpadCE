using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Calcpad.OpenXml
{
    internal abstract class ImageProcessor(string imageSrc)
    {
        protected readonly string src = imageSrc;
        public abstract ImagePart GetImagePart(MainDocumentPart mainPart);
        public abstract (int, int) ImageSize { get; }

        protected static string GetImageDataType(string data)
        {
            var start = data.IndexOf('/') + 1;
            var end = data.IndexOf(';');
            if (start <= 0 || end <= 0)
                throw new Exception(Messages.Invalid_image_data);
            return data[start..end];
        }

        protected static byte[] GetImageData(string data)
        {
            var start = data.IndexOf(',') + 1;
            if (start <= 0)
                throw new Exception(Messages.Invalid_image_data);

            var base64 = data[start..];
            return Convert.FromBase64String(base64);
        }

        protected static PartTypeInfo GetImagePartType(string ext)
        {
            return ext.ToLowerInvariant() switch
            {
                "png" => ImagePartType.Png,
                "gif" => ImagePartType.Gif,
                "jpg" => ImagePartType.Jpeg,
                "jpeg" => ImagePartType.Jpeg,
                "bmp" => ImagePartType.Bmp,
                "tif" => ImagePartType.Tiff,
                "tiff" => ImagePartType.Tiff,
                "wmf" => ImagePartType.Wmf,
                "emf" => ImagePartType.Emf,
                "ico" => ImagePartType.Icon,
                "pcx" => ImagePartType.Pcx,
                "svg" => ImagePartType.Svg,
                "svg+xml" => ImagePartType.Svg,
                _ => throw new Exception(string.Format(Messages.Unsupported_image_type_0, ext))
            };
        }

        protected static (int Width, int Height) GetSvgImageSize(string svg)
        {
            var width = GetSvgNumber(svg, "width");
            var height = GetSvgNumber(svg, "height");
            if (width > 0 && height > 0)
                return ((int)Math.Round(width), (int)Math.Round(height));

            var viewBox = Regex.Match(
                svg,
                @"\bviewBox\s*=\s*['""]\s*(-?[0-9]+(?:\.[0-9]+)?)\s+(-?[0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s+([0-9]+(?:\.[0-9]+)?)\s*['""]",
                RegexOptions.IgnoreCase);
            if (!viewBox.Success)
                return default;

            if (!double.TryParse(viewBox.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var viewWidth) ||
                !double.TryParse(viewBox.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var viewHeight) ||
                viewWidth <= 0 || viewHeight <= 0)
                return default;

            if (width > 0 && height <= 0)
                height = width * viewHeight / viewWidth;
            else if (height > 0 && width <= 0)
                width = height * viewWidth / viewHeight;
            else
            {
                width = viewWidth;
                height = viewHeight;
            }

            return ((int)Math.Round(width), (int)Math.Round(height));
        }

        private static double GetSvgNumber(string svg, string attribute)
        {
            var match = Regex.Match(
                svg,
                $@"\b{attribute}\s*=\s*['""]\s*([0-9]+(?:\.[0-9]+)?)([a-z%]*)",
                RegexOptions.IgnoreCase);
            if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return 0;

            var unit = match.Groups[2].Value;
            return unit is "" or "%" ? value : CssParser.ParseSize(value + unit);
        }
    }

    internal class Base64ImageProcessor(string imageSrc) : ImageProcessor(imageSrc)
    {
        private readonly byte[] _imageData = GetImageData(imageSrc);

        public override ImagePart GetImagePart(MainDocumentPart mainPart)
        {
            var ext = GetImageDataType(src);
            var imageType = GetImagePartType(ext);
            var imagePart = mainPart.AddImagePart(imageType);
            using var stream = new MemoryStream(_imageData);
            imagePart.FeedData(stream);
            return imagePart;
        }

        public override (int, int) ImageSize
        {
            get
            {
                using var stream = new MemoryStream(_imageData);
                using var img = SKBitmap.Decode(stream);
                return (img.Width, img.Height);
            }
        }
    }

    internal class UrlImageProcessor(string imageSrc) : ImageProcessor(imageSrc)
    {
        public override ImagePart GetImagePart(MainDocumentPart mainPart)
        {
            var ext = GetUrlExtension(src);
            var imageType = GetImagePartType(ext);
            var imagePart = mainPart.AddImagePart(imageType);
            var uri = new Uri(src);
            if (uri.IsFile)
            {
                using var stream = new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read);
                imagePart.FeedData(stream);
            }
            else
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(2);
                using var stream = client.GetStreamAsync(src).Result;
                imagePart.FeedData(stream);
            }
            return imagePart;
        }

        private static string GetUrlExtension(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return Path.GetExtension(uri.AbsolutePath).TrimStart('.');

            return Path.GetExtension(value).TrimStart('.');
        }

        public override (int, int) ImageSize
        {
            get
            {
                var isBitmap = string.Equals(Path.GetExtension(src), ".bmp", StringComparison.OrdinalIgnoreCase);
                var uri = new Uri(src);
                if (uri.IsFile)
                {
                    if (isBitmap)
                    {
                        using var bmp = SKBitmap.Decode(src);
                        return (bmp.Width, bmp.Height);
                    }
                    using var data = SKData.Create(src);
                    using var img = SKImage.FromEncodedData(data);
                    return (img.Width, img.Height);
                }
                else
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(2);
                    using var stream = client.GetStreamAsync(src).Result;
                    if (isBitmap)
                    {
                        using var bmp = SKBitmap.Decode(stream);
                        return new(bmp.Width, bmp.Height);
                    }
                    using var data = SKData.Create(stream);
                    using var img = SKImage.FromEncodedData(data);
                    return new(img.Width, img.Height);
                }
            }
        }
    }

    internal class FileImageProcessor : ImageProcessor
    {
        private readonly FileInfo _fileInfo;
        public FileImageProcessor(string imageSrc) : base(imageSrc)
        {
            _fileInfo = new FileInfo(imageSrc);
            if (!_fileInfo.Exists)
                throw new FileNotFoundException(string.Format(Messages.Invalid_image_file_0, imageSrc));
        }
        public override ImagePart GetImagePart(MainDocumentPart mainPart)
        {
            var ext = _fileInfo.Extension[1..];
            var imageType = GetImagePartType(ext);
            var imagePart = mainPart.AddImagePart(imageType);
            using var stream = new FileStream(src, FileMode.Open, FileAccess.Read);
            imagePart.FeedData(stream);
            return imagePart;
        }

        public override (int, int) ImageSize
        {
            get
            {
                if (string.Equals(Path.GetExtension(src), ".bmp", StringComparison.OrdinalIgnoreCase))
                {
                    using var bmp = SKBitmap.Decode(src);
                    return (bmp.Width, bmp.Height);
                }
                if (string.Equals(Path.GetExtension(src), ".svg", StringComparison.OrdinalIgnoreCase))
                    return GetSVGImageSize(src);

                using var data = SKData.Create(src);
                using var img = SKImage.FromEncodedData(data);
                return new(img.Width, img.Height);
            }
        }

        private static (int, int) GetSVGImageSize(string src)
        {
            string svg = File.ReadAllText(src);
            return GetSvgImageSize(svg);
        }
    }

    internal class SvgImageProcessor(string imageSrc) : ImageProcessor(imageSrc)
    {
        public override ImagePart GetImagePart(MainDocumentPart mainPart)
        {
            var imagePart = mainPart.AddImagePart(ImagePartType.Svg);
            var bytes = System.Text.Encoding.UTF8.GetBytes(src);
            using var stream = new MemoryStream(bytes);
            imagePart.FeedData(stream);
            return imagePart;
        }

        public override (int, int) ImageSize => GetSvgImageSize(src);
    }
}
