using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace RefScrn.Services
{
    public class TranslationResult
    {
        public string OriginalText { get; set; }
        public string TranslatedText { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class TranslationService
    {
        private OcrEngine _ocrEngine;

        public TranslationService()
        {
            // Initialize OCR engine for English
            var lang = new Windows.Globalization.Language("en-US");
            if (OcrEngine.IsLanguageSupported(lang))
            {
                _ocrEngine = OcrEngine.TryCreateFromLanguage(lang);
            }
            else
            {
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
        }

        public async Task<TranslationResult> AnalyzeAndTranslateAsync(BitmapSource bitmapSource)
        {
            try
            {
                if (_ocrEngine == null)
                    return new TranslationResult { Success = false, ErrorMessage = "OCR引擎初始化失败" };

                // 1. Convert BitmapSource to SoftwareBitmap
                SoftwareBitmap softwareBitmap = await ConvertToSoftwareBitmap(bitmapSource);

                // 2. Perform OCR
                var ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);
                
                if (ocrResult == null || string.IsNullOrEmpty(ocrResult.Text))
                {
                    return new TranslationResult { Success = false, ErrorMessage = "未检测到文本" };
                }

                string detectedText = ocrResult.Text;

                // 3. Translate using a lightweight approach (e.g., Google Translate free API)
                string translatedText = await TranslateTextAsync(detectedText);

                return new TranslationResult
                {
                    Success = true,
                    OriginalText = detectedText,
                    TranslatedText = translatedText
                };
            }
            catch (Exception ex)
            {
                return new TranslationResult { Success = false, ErrorMessage = ex.GetBaseException().Message };
            }
        }

        private async Task<string> TranslateTextAsync(string text)
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    // Use a common free endpoint pattern for testing
                    // Note: In a production app, a proper API key is recommended.
                    string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=zh-CN&dt=t&q={Uri.EscapeDataString(text)}";
                    
                    var response = await client.GetStringAsync(url);
                    
                    // The result is a nested JSON array: [[["翻译结果", "原文", null, null, 1]], null, "en", ...]
                    // We can use a simple regex or string manipulation to avoid heavy JSON dependencies if needed, 
                    // but since dotnet has JsonSerializer, let's use it roughly.
                    
                    using (var doc = System.Text.Json.JsonDocument.Parse(response))
                    {
                        var firstArray = doc.RootElement[0];
                        string result = "";
                        foreach (var item in firstArray.EnumerateArray())
                        {
                            result += item[0].GetString();
                        }
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return $"翻译失败: {ex.Message}";
            }
        }

        private async Task<SoftwareBitmap> ConvertToSoftwareBitmap(BitmapSource bitmapSource)
        {
            using (var stream = new InMemoryRandomAccessStream())
            {
                var wpfEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                wpfEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                
                using (var ioStream = stream.AsStreamForWrite())
                {
                    wpfEncoder.Save(ioStream);
                    await ioStream.FlushAsync();
                }

                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                return await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            }
        }
    }
}
