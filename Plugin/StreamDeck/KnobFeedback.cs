using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;

namespace PilotsDeck.StreamDeck
{
    public class KnobFeedback
    {
        protected Size CanvasSize { get; }
        protected RectangleF CanvasRect { get; }
        protected Color Background { get; }
        protected string SourceImage64 { get; set; }
        protected string RenderedImage64 { get; set; }

        public KnobFeedback()
        {
            using JsonDocument layout = JsonDocument.Parse(File.ReadAllText("Plugin/layout-pilotsdeck.json"));
            var root = layout.RootElement;
            CanvasSize = new Size(176, 112);
            Size logicalSize = root.TryGetProperty("size", out var size) ? new Size(size[0].GetInt32(), size[1].GetInt32()) : new Size(200, 100);
            Background = root.TryGetProperty("background", out var background) ? ColorTranslator.FromHtml(background.GetString()) : Color.Black;
            var items = root.GetProperty("items");
            if (items.GetArrayLength() != 1 || items[0].GetProperty("key").GetString() != AppConfiguration.SdTargetImage
                || items[0].GetProperty("type").GetString() != "pixmap")
                throw new InvalidDataException("The Knob feedback layout must contain a single canvas pixmap.");

            var rect = items[0].GetProperty("rect");
            if (logicalSize.Width <= 0 || logicalSize.Height <= 0 || rect[2].GetSingle() <= 0 || rect[3].GetSingle() <= 0)
                throw new InvalidDataException("Invalid Knob feedback canvas dimensions.");
            CanvasRect = new RectangleF(rect[0].GetSingle() * CanvasSize.Width / logicalSize.Width,
                rect[1].GetSingle() * CanvasSize.Height / logicalSize.Height,
                rect[2].GetSingle() * CanvasSize.Width / logicalSize.Width,
                rect[3].GetSingle() * CanvasSize.Height / logicalSize.Height);
        }

        public string RenderCanvasImage(string image64)
        {
            if (SourceImage64 == image64 && RenderedImage64 != null)
                return RenderedImage64;

            using MemoryStream source = new(Convert.FromBase64String(image64));
            using Image image = Image.FromStream(source);
            using Bitmap output = new(CanvasSize.Width, CanvasSize.Height);
            using (Graphics graphics = Graphics.FromImage(output))
            {
                graphics.Clear(Background);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SetClip(CanvasRect);
                float scale = Math.Min(CanvasRect.Width / image.Width, CanvasRect.Height / image.Height);
                float width = image.Width * scale;
                float height = image.Height * scale;
                graphics.DrawImage(image, new RectangleF(CanvasRect.X + (CanvasRect.Width - width) / 2,
                    CanvasRect.Y + (CanvasRect.Height - height) / 2, width, height),
                    new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
            }

            using MemoryStream result = new();
            output.Save(result, ImageFormat.Png);
            RenderedImage64 = Convert.ToBase64String(result.ToArray());
            SourceImage64 = image64;
            return RenderedImage64;
        }
    }
}
