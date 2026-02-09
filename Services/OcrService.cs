using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace RefScrn.Services
{
    public class ScannedTextResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<ScannedTextLine> Lines { get; set; } = new List<ScannedTextLine>();
    }

    public class ScannedTextLine
    {
        public string Text { get; set; }
        public System.Windows.Rect BoundingRect { get; set; }
    }

    public class OcrService
    {
        private OcrEngine _ocrEngine;

        public async Task<ScannedTextResult> RecognizeTextAsync(BitmapSource bitmapSource)
        {
            var result = new ScannedTextResult();

            try
            {
                if (_ocrEngine == null)
                {
                    // Try to initialize OCR engine with preferred languages
                    // 1. Chinese (Simplified)
                    // 2. English
                    // 3. Any available
                    var preferLang = OcrEngine.AvailableRecognizerLanguages
                        .FirstOrDefault(l => l.LanguageTag.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase)) 
                        ?? OcrEngine.AvailableRecognizerLanguages
                        .FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        ?? OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();

                    if (preferLang == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = "未安装 OCR 语言包 (请在 Windows 设置中添加)";
                        return result;
                    }

                    _ocrEngine = OcrEngine.TryCreateFromLanguage(preferLang);
                }

                if (_ocrEngine == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "无法初始化 OCR 引擎";
                    return result;
                }

                using (var softwareBitmap = await ConvertToSoftwareBitmap(bitmapSource))
                {
                    var ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);
                    
                    if (ocrResult == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = "OCR 识别无结果";
                        return result;
                    }

                    result.Lines = ocrResult.Lines.Select(line =>
                    {
                        // Calculate bounding rect from words
                        double minX = double.MaxValue, minY = double.MaxValue;
                        double maxX = double.MinValue, maxY = double.MinValue;
                        
                        foreach (var word in line.Words)
                        {
                            if (word.BoundingRect.X < minX) minX = word.BoundingRect.X;
                            if (word.BoundingRect.Y < minY) minY = word.BoundingRect.Y;
                            if (word.BoundingRect.Right > maxX) maxX = word.BoundingRect.Right;
                            if (word.BoundingRect.Bottom > maxY) maxY = word.BoundingRect.Bottom;
                        }

                        // If line has no words (shouldn't happen), use default
                        if (minX == double.MaxValue) return new ScannedTextLine { Text = line.Text, BoundingRect = new System.Windows.Rect() };

                        return new ScannedTextLine 
                        { 
                            Text = line.Text,
                            BoundingRect = new System.Windows.Rect(minX, minY, maxX - minX, maxY - minY)
                        };

                    }).ToList();
                    
                    result.Success = true;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"OCR 异常: {ex.Message}";
            }

            return result;
        }

        private async Task<SoftwareBitmap> ConvertToSoftwareBitmap(BitmapSource bitmapSource)
        {
            using (var memoryStream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                encoder.Save(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);

                var randomAccessStream = memoryStream.AsRandomAccessStream();
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccessStream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                
                return softwareBitmap;
            }
        }
    }
}
