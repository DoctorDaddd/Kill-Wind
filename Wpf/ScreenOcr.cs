using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace KillWind.Wpf
{
    internal sealed class ScreenOcrResult
    {
        public bool Success;
        public int Value;
        public string Text;
        public string Error;
    }

    internal static class ScreenOcr
    {
        private const int CaptureWidth = 360;
        private const int CaptureHeight = 120;
        private static readonly Regex NumberPattern = new Regex("[-+]?\\d[\\d,]*", RegexOptions.Compiled);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        public static Task<ScreenOcrResult> RecognizeAtPointAsync(ScreenPoint point)
        {
            return Task.Run(delegate { return RecognizeAtPoint(point); });
        }

        private static ScreenOcrResult RecognizeAtPoint(ScreenPoint point)
        {
            try
            {
                int left;
                int top;
                using (var image = Capture(point, out left, out top))
                using (var png = new MemoryStream())
                {
                    image.Save(png, ImageFormat.Png);
                    using (var input = new InMemoryRandomAccessStream())
                    using (var writer = new DataWriter(input))
                    {
                        writer.WriteBytes(png.ToArray());
                        WaitResult(writer.StoreAsync(), typeof(uint));
                        WaitResult(writer.FlushAsync(), typeof(bool));
                        input.Seek(0);
                        var decoder = (BitmapDecoder)WaitResult(BitmapDecoder.CreateAsync(input), typeof(BitmapDecoder));
                        var bitmap = (SoftwareBitmap)WaitResult(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied), typeof(SoftwareBitmap));
                        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
                        if (engine == null) return Failure("系统没有可用的 Windows OCR 语言包。", "");
                        var result = (OcrResult)WaitResult(engine.RecognizeAsync(bitmap), typeof(OcrResult));
                        int value;
                        if (TryFindNumber(result, point.x - left, point.y - top, out value))
                            return new ScreenOcrResult { Success = true, Value = value, Text = result.Text };
                        return Failure("没有在点击位置附近识别到整数。", result.Text);
                    }
                }
            }
            catch (Exception error)
            {
                return Failure(error.Message, "");
            }
        }

        private static Bitmap Capture(ScreenPoint point, out int left, out int top)
        {
            int virtualLeft = GetSystemMetrics(76);
            int virtualTop = GetSystemMetrics(77);
            int virtualRight = virtualLeft + GetSystemMetrics(78);
            int virtualBottom = virtualTop + GetSystemMetrics(79);
            left = point.x - CaptureWidth / 2;
            top = point.y - CaptureHeight / 2;
            left = Math.Max(virtualLeft, Math.Min(left, virtualRight - CaptureWidth));
            top = Math.Max(virtualTop, Math.Min(top, virtualBottom - CaptureHeight));
            var image = new Bitmap(CaptureWidth, CaptureHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(image))
                graphics.CopyFromScreen(left, top, 0, 0, new Size(CaptureWidth, CaptureHeight), CopyPixelOperation.SourceCopy);
            return image;
        }

        private static bool TryFindNumber(OcrResult result, int clickX, int clickY, out int value)
        {
            double bestDistance = Double.MaxValue;
            value = 0;
            foreach (var line in result.Lines)
            {
                foreach (var word in line.Words)
                {
                    int candidate;
                    if (!TryParseNumber(word.Text, out candidate)) continue;
                    double centerX = word.BoundingRect.X + word.BoundingRect.Width / 2.0;
                    double centerY = word.BoundingRect.Y + word.BoundingRect.Height / 2.0;
                    double distance = (centerX - clickX) * (centerX - clickX) + (centerY - clickY) * (centerY - clickY);
                    if (distance < bestDistance) { bestDistance = distance; value = candidate; }
                }
            }
            if (bestDistance != Double.MaxValue) return true;
            foreach (Match match in NumberPattern.Matches(result.Text ?? ""))
                if (TryParseNumber(match.Value, out value)) return true;
            return false;
        }

        private static bool TryParseNumber(string text, out int value)
        {
            string normalized = NormalizeDigits(text).Replace(",", "");
            return Int32.TryParse(normalized, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static string NormalizeDigits(string text)
        {
            if (String.IsNullOrEmpty(text)) return "";
            var chars = text.ToCharArray();
            for (int index = 0; index < chars.Length; index++)
                if (chars[index] >= '０' && chars[index] <= '９') chars[index] = (char)('0' + chars[index] - '０');
            return new string(chars);
        }

        private static ScreenOcrResult Failure(string error, string text)
        {
            return new ScreenOcrResult { Success = false, Error = error, Text = text };
        }

        private static object WaitResult(object operation, Type resultType)
        {
            foreach (var candidate in typeof(System.WindowsRuntimeSystemExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (candidate.Name != "AsTask" || candidate.GetParameters().Length != 1) continue;
                try
                {
                    var method = candidate.IsGenericMethodDefinition ? candidate.MakeGenericMethod(resultType) : candidate;
                    var task = method.Invoke(null, new[] { operation }) as Task;
                    if (task == null) continue;
                    task.GetAwaiter().GetResult();
                    var result = task.GetType().GetProperty("Result");
                    return result == null ? null : result.GetValue(task, null);
                }
                catch (TargetInvocationException) { }
                catch (ArgumentException) { }
            }
            throw new InvalidOperationException("无法适配 Windows OCR 异步操作。");
        }
    }
}
