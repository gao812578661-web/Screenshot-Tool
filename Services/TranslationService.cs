using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace RefScrn.Services
{
    public class TranslationResult
    {
        public bool Success { get; set; }
        public string OriginalText { get; set; }
        public string TranslatedText { get; set; }
        public string ErrorMessage { get; set; }
        public List<TranslatedLine> Lines { get; set; } = new List<TranslatedLine>();
    }

    public class TranslatedLine
    {
        public string Text { get; set; }
        public Windows.Foundation.Rect BoundingRect { get; set; }
        public string BackgroundColor { get; set; } = "#FFFFFF";
        public string TextColor { get; set; } = "#000000";
    }

    public class TranslationService
    {
        private List<OcrEngine> _ocrEngines = new List<OcrEngine>();

        public TranslationService()
        {
            var zhLang = new Windows.Globalization.Language("zh-Hans-CN");
            if (OcrEngine.IsLanguageSupported(zhLang))
            {
                var engine = OcrEngine.TryCreateFromLanguage(zhLang);
                if (engine != null) _ocrEngines.Add(engine);
            }

            var enLang = new Windows.Globalization.Language("en-US");
            if (OcrEngine.IsLanguageSupported(enLang))
            {
                var engine = OcrEngine.TryCreateFromLanguage(enLang);
                if (engine != null) _ocrEngines.Add(engine);
            }

            if (_ocrEngines.Count == 0)
            {
                var engine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (engine != null) _ocrEngines.Add(engine);
            }
        }

        public async Task<TranslationResult> AnalyzeAndTranslateAsync(BitmapSource bitmapSource)
        {
            try
            {
                // 获取像素数据用于颜色识别
                byte[] pixels = GetPixels(bitmapSource);

                using (SoftwareBitmap softwareBitmap = await ConvertToSoftwareBitmap(bitmapSource))
                {
                    OcrResult finalOcrResult = null;

                    foreach (var engine in _ocrEngines)
                    {
                        var ocrResult = await engine.RecognizeAsync(softwareBitmap);
                        if (ocrResult != null && ocrResult.Lines.Count > 0)
                        {
                            finalOcrResult = ocrResult;
                            break; 
                        }
                    }
                    
                    if (finalOcrResult == null)
                    {
                        return new TranslationResult { Success = false, ErrorMessage = "未检测到文本" };
                    }

                    var linesInfo = new List<TranslatedLine>();
                    var textBuilder = new System.Text.StringBuilder();

                    foreach (var line in finalOcrResult.Lines)
                    {
                        string lineText = "";
                        foreach (var word in line.Words)
                        {
                            lineText += word.Text + " ";
                        }
                        string cleanLine = lineText.Trim();
                        textBuilder.AppendLine(cleanLine);

                        double minX = line.Words.Min(w => w.BoundingRect.X);
                        double minY = line.Words.Min(w => w.BoundingRect.Y);
                        double maxX = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
                        double maxY = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);

                        var rect = new Windows.Foundation.Rect(
                                minX / 2.0, 
                                minY / 2.0, 
                                (maxX - minX) / 2.0, 
                                (maxY - minY) / 2.0);

                        var colors = SampleColors(pixels, (int)bitmapSource.PixelWidth, (int)bitmapSource.PixelHeight, rect);

                        linesInfo.Add(new TranslatedLine 
                        { 
                            Text = cleanLine, 
                            BoundingRect = rect,
                            BackgroundColor = colors.bg,
                            TextColor = colors.fg
                        });
                    }

                    string detectedText = textBuilder.ToString().Trim();
                    int chineseChars = detectedText.Count(c => c >= 0x4E00 && c <= 0x9FFF);
                    bool isChinese = chineseChars > 0 && (double)chineseChars / detectedText.Length > 0.1;

                    string translatedText = await TranslateTextAsync(detectedText, isChinese ? "zh-CN" : "en", isChinese ? "en" : "zh-CN");

                    var translatedLinesArray = translatedText.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    if (translatedLinesArray.Length == linesInfo.Count)
                    {
                        for (int i = 0; i < linesInfo.Count; i++)
                        {
                            linesInfo[i].Text = translatedLinesArray[i];
                        }
                    }
                    else
                    {
                        if (linesInfo.Count > 0) linesInfo[0].Text = translatedText;
                    }

                    return new TranslationResult
                    {
                        Success = true,
                        OriginalText = detectedText,
                        TranslatedText = translatedText,
                        Lines = linesInfo
                    };
                }
            }
            catch (Exception ex)
            {
                return new TranslationResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private byte[] GetPixels(BitmapSource source)
        {
            int stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
            byte[] pixels = new byte[source.PixelHeight * stride];
            source.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        private (string bg, string fg) SampleColors(byte[] pixels, int width, int height, Windows.Foundation.Rect rect)
        {
            int startX = (int)Math.Max(0, rect.X);
            int startY = (int)Math.Max(0, rect.Y);
            int endX = (int)Math.Min(width - 1, rect.X + rect.Width);
            int endY = (int)Math.Min(height - 1, rect.Y + rect.Height);

            Dictionary<uint, int> colorCounts = new Dictionary<uint, int>();
            
            for (int y = startY; y <= endY; y += 2)
            {
                for (int x = startX; x <= endX; x += 2)
                {
                    int index = (y * width + x) * 4;
                    if (index + 3 >= pixels.Length) continue;

                    byte b = pixels[index];
                    byte g = pixels[index + 1];
                    byte r = pixels[index + 2];
                    uint color = ((uint)r << 16) | ((uint)g << 8) | b;
                    
                    if (colorCounts.ContainsKey(color)) colorCounts[color]++;
                    else colorCounts[color] = 1;
                }
            }

            if (colorCounts.Count == 0) return ("#FFFFFF", "#000000");

            var sortedColors = colorCounts.OrderByDescending(kv => kv.Value).ToList();
            uint bgColorVal = sortedColors[0].Key;
            string bgHex = $"#{bgColorVal:X6}";

            uint fgColorVal = bgColorVal;
            foreach (var kv in sortedColors.Skip(1))
            {
                if (GetColorDistance(bgColorVal, kv.Key) > 100)
                {
                    fgColorVal = kv.Key;
                    break;
                }
            }

            if (fgColorVal == bgColorVal)
            {
                double luminance = (0.299 * ((bgColorVal >> 16) & 0xFF) + 0.587 * ((bgColorVal >> 8) & 0xFF) + 0.114 * (bgColorVal & 0xFF)) / 255.0;
                fgColorVal = luminance > 0.5 ? 0x000000u : 0xFFFFFFu;
            }

            return (bgHex, $"#{fgColorVal:X6}");
        }

        private double GetColorDistance(uint c1, uint c2)
        {
            int r1 = (int)((c1 >> 16) & 0xFF);
            int g1 = (int)((c1 >> 8) & 0xFF);
            int b1 = (int)(c1 & 0xFF);

            int r2 = (int)((c2 >> 16) & 0xFF);
            int g2 = (int)((c2 >> 8) & 0xFF);
            int b2 = (int)(c2 & 0xFF);

            return Math.Sqrt(Math.Pow(r1 - r2, 2) + Math.Pow(g1 - g2, 2) + Math.Pow(b1 - b2, 2));
        }

        private async Task<string> TranslateTextAsync(string text, string sl = "auto", string tl = "zh-CN")
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={Uri.EscapeDataString(text)}";
                    var response = await client.GetStringAsync(url);
                    
                    using (var doc = System.Text.Json.JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        if (root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0)
                        {
                            var firstArray = root[0];
                            if (firstArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                string result = "";
                                foreach (var item in firstArray.EnumerateArray())
                                {
                                    if (item.ValueKind == System.Text.Json.JsonValueKind.Array && item.GetArrayLength() > 0)
                                    {
                                        result += item[0].GetString();
                                    }
                                }
                                return result;
                            }
                        }
                        return "翻译失败";
                    }
                }
            }
            catch { return "翻译失败"; }
        }

        private async Task<SoftwareBitmap> ConvertToSoftwareBitmap(BitmapSource bitmapSource)
        {
            var stream = new InMemoryRandomAccessStream();
            
            try
            {
                var drawingVisual = new System.Windows.Media.DrawingVisual();
                using (var context = drawingVisual.RenderOpen())
                {
                    context.DrawImage(bitmapSource, new System.Windows.Rect(0, 0, bitmapSource.Width * 2, bitmapSource.Height * 2));
                }
                
                var rtb = new RenderTargetBitmap((int)(bitmapSource.Width * 2), (int)(bitmapSource.Height * 2), 96, 96, PixelFormats.Pbgra32);
                rtb.Render(drawingVisual);
                
                var wpfEncoder = new PngBitmapEncoder();
                wpfEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                
                // 先编码到 MemoryStream，然后写入 InMemoryRandomAccessStream
                byte[] imageBytes;
                using (var memStream = new MemoryStream())
                {
                    wpfEncoder.Save(memStream);
                    imageBytes = memStream.ToArray();
                }
                
                // 直接写入字节数组到 WinRT stream
                await stream.WriteAsync(imageBytes.AsBuffer());
                stream.Seek(0);
                
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
                
                return softwareBitmap;
            }
            finally
            {
                stream?.Dispose();
            }
        }
    }
}
