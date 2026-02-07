using System;
using System.Windows;
using System.Windows.Input;
using RefScrn.Services;
using Microsoft.Win32;

namespace RefScrn
{
    public partial class SettingsWindow : Window
    {
        private SettingsService _settingsService;
        private string _currentHotkey;
        private bool _isRecording = false;

        public SettingsWindow(SettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;

            // Localization
            this.Title = "设置";
            if (AutoStartCheckBox != null) AutoStartCheckBox.Content = "开机自动启动";
            if (SetHotkeyButton != null) SetHotkeyButton.Content = "设置";
            if (BrowsePathButton != null) BrowsePathButton.Content = "...";
            
            // Named buttons from XAML
            if (SaveButton != null) SaveButton.Content = "保存";
            if (CancelButton != null) CancelButton.Content = "取消";
            
            // Named TextBlocks
            if (GeneralHeader != null) GeneralHeader.Text = "常规";
            if (HotkeysHeader != null) HotkeysHeader.Text = "快捷键";
            if (SavePathHeader != null) SavePathHeader.Text = "保存位置";

            this.PreviewKeyDown += OnPreviewKeyDown;
            LoadUI();
        }

        private void LoadUI()
        {
            if (_settingsService?.CurrentSettings == null) return;

            AutoStartCheckBox.IsChecked = _settingsService.CurrentSettings.AutoStart;
            HotkeyTextBox.Text = _settingsService.CurrentSettings.Hotkey;
            _currentHotkey = _settingsService.CurrentSettings.Hotkey;
            SavePathTextBox.Text = _settingsService.CurrentSettings.DefaultSavePath;
        }

        private void OnBrowsePathClick(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.SelectedPath = _settingsService.CurrentSettings.DefaultSavePath;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SavePathTextBox.Text = dialog.SelectedPath;
            }
        }

        private void OnSetHotkeyClick(object sender, RoutedEventArgs e)
        {
            _isRecording = true;
            SetHotkeyButton.Content = "请输入...";
            HotkeyTextBox.Focus();
        }

        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isRecording) return;

            e.Handled = true;

            // Get modifiers
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            System.Windows.Input.Key key = (e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key);

            // Ignore standalone modifier presses
            if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl || 
                key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt || 
                key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
                key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin)
            {
                return;
            }

            // Build string
            string hotkeyStr = "";
            if ((modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control) hotkeyStr += "Ctrl+";
            if ((modifiers & System.Windows.Input.ModifierKeys.Alt) == System.Windows.Input.ModifierKeys.Alt) hotkeyStr += "Alt+";
            if ((modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift) hotkeyStr += "Shift+";
            if ((modifiers & System.Windows.Input.ModifierKeys.Windows) == System.Windows.Input.ModifierKeys.Windows) hotkeyStr += "Win+";

            hotkeyStr += key.ToString();

            HotkeyTextBox.Text = hotkeyStr;
            _currentHotkey = hotkeyStr;
            
            _isRecording = false;
            SetHotkeyButton.Content = "设置";
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentHotkey)) _currentHotkey = "Alt+A"; // Default fallback

            _settingsService.CurrentSettings.AutoStart = AutoStartCheckBox.IsChecked ?? false;
            _settingsService.CurrentSettings.DefaultSavePath = SavePathTextBox.Text;
            _settingsService.CurrentSettings.Hotkey = _currentHotkey;

            _settingsService.Save();
            
            this.Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
