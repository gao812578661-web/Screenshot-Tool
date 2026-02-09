using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RefScrn
{
    public partial class TextRecognitionWindow : Window
    {
        public TextRecognitionWindow(BitmapSource image, string recognizedText)
        {
            InitializeComponent();
            this.Title = "文字识别结果";
            
            OriginalTab.Header = "原文";
            TranslationTab.Header = "译文";
            TranslationPlaceholder.Text = "正在翻译..."; // Set initial state for background translation
            TranslationPlaceholder.Visibility = Visibility.Visible;
            
            PreviewImage.Source = image;
            ResultTextBox.Text = recognizedText;
            
            // Auto-trigger translation in background
            TranslateAsync();
        }

        private async void TranslateAsync()
        {
            var text = ResultTextBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            
            // Do NOT switch tab automatically
            // TranslationTab.IsSelected = true; 
            
            try 
            {
                // Simple Google Translate API call (Unofficial)
                using (var client = new System.Net.Http.HttpClient())
                {
                    var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=zh-CN&dt=t&q={System.Net.WebUtility.UrlEncode(text)}";
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync();
                    
                    try 
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var resultText = new System.Text.StringBuilder();
                        
                        if (root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0)
                        {
                            var sentences = root[0];
                            foreach (var sentence in sentences.EnumerateArray())
                            {
                                if (sentence.GetArrayLength() > 0)
                                {
                                    resultText.Append(sentence[0].GetString());
                                }
                            }
                        }
                        
                        TranslationTextBox.Text = resultText.ToString();
                        TranslationPlaceholder.Visibility = Visibility.Collapsed;
                    }
                    catch
                    {
                        TranslationTextBox.Text = "翻译解析失败";
                    }
                }
            }
            catch (Exception ex)
            {
                TranslationTextBox.Text = $"翻译失败: {ex.Message}";
            }
        }


    }
}
