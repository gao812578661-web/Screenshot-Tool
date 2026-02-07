using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RefScrn
{
    public partial class OverlayWindow : Window
    {
        private System.Windows.Point _startPoint;
        private System.Windows.Rect _startRect;
        private bool _isSelecting = false;
        private int _resizeHandleIndex = -1; // -1: none, -2: move, 0-7: handles, 8: new
        private System.Windows.Media.RectangleGeometry _selectionGeometry;
        private System.Windows.Media.CombinedGeometry _maskGeometry;

        // Annotation State
        private enum AnnotationTool { None, Rectangle, Ellipse, Arrow, Brush, Text }
        private AnnotationTool _currentTool = AnnotationTool.None;
        private System.Windows.Shapes.Shape _tempShape;
        private System.Windows.Controls.TextBox _activeTextBox;
        private System.Windows.Point _drawStartPoint;
        private bool _isDrawing = false;
        private System.Windows.Media.Brush _drawColor;
        private double _drawThickness = 3.0;

        public OverlayWindow(BitmapSource screenshot, double x, double y)
        {
            InitializeComponent();
            BackgroundImage.Source = screenshot;

            // Calculate DPI Scale for the target monitor
            GetDpiScale(x, y, out double scaleX, out double scaleY);

            // Convert Pixels (from Screenshot/Screen.Bounds) to WPF DIUs
            this.Width = screenshot.PixelWidth / scaleX;
            this.Height = screenshot.PixelHeight / scaleY;
            this.Left = x / scaleX;
            this.Top = y / scaleY;

            _selectionGeometry = new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(0, 0, 0, 0));
            _maskGeometry = new System.Windows.Media.CombinedGeometry(
                System.Windows.Media.GeometryCombineMode.Exclude,
                new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(0, 0, 10000, 10000)),
                _selectionGeometry);

            MaskPath.Data = _maskGeometry;
            
            // Set initial color (Red #FF3B30)
            _drawColor = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 59, 48)); 
        }

        protected override void OnPreviewMouseDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsTouchToolbar(e.OriginalSource)) return;

            var pos = e.GetPosition(this);

            if (_currentTool != AnnotationTool.None && _selectionGeometry.Rect.Contains(pos))
            {
                HandleDrawingMouseDown(pos);
                e.Handled = true;
                return;
            }

            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                _startPoint = pos;
                _resizeHandleIndex = GetHandleIndex(pos);

                _isSelecting = true;
                if (_resizeHandleIndex != -1)
                {
                    _startRect = _selectionGeometry.Rect;
                }
                else
                {
                    _resizeHandleIndex = 8;
                    _startRect = new System.Windows.Rect(pos, pos);
                    UpdateSelection(_startRect);
                }
                this.CaptureMouse();
            }
            else if (e.RightButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                if (_activeTextBox != null)
                {
                    FinalizeText();
                }
                else
                {
                    this.Close();
                }
            }
        }

        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            var currentPoint = e.GetPosition(this);

            if (_currentTool != AnnotationTool.None && _isDrawing)
            {
                HandleDrawingMouseMove(currentPoint);
                return;
            }

            if (_isSelecting && this.IsMouseCaptured)
            {
                if (_resizeHandleIndex == -2)
                {
                    double offsetX = currentPoint.X - _startPoint.X;
                    double offsetY = currentPoint.Y - _startPoint.Y;
                    UpdateSelection(new System.Windows.Rect(_startRect.X + offsetX, _startRect.Y + offsetY, _startRect.Width, _startRect.Height));
                }
                else if (_resizeHandleIndex == 8)
                {
                    UpdateSelection(new System.Windows.Rect(_startPoint, currentPoint));
                }
                else
                {
                    HandleResize(_resizeHandleIndex, currentPoint);
                }
            }
            else
            {
                this.Cursor = (_currentTool != AnnotationTool.None && _selectionGeometry.Rect.Contains(currentPoint)) 
                    ? System.Windows.Input.Cursors.Pen 
                    : GetCursorForHandle(GetHandleIndex(currentPoint));
            }
        }

        protected override void OnMouseUp(System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDrawing)
            {
                HandleDrawingMouseUp();
                return;
            }

            if (_isSelecting)
            {
                _isSelecting = false;
                _resizeHandleIndex = -1;
                this.ReleaseMouseCapture();
                UpdateToolbarPosition(_selectionGeometry.Rect);
            }
        }

        private bool IsTouchToolbar(object source)
        {
            if (source is DependencyObject dep)
            {
                while (dep != null)
                {
                    if (dep == ToolbarArea) return true; // Check ToolbarArea covers both ToolbarPanel and ColorPanel
                    if (dep == this) return false;
                    dep = VisualTreeHelper.GetParent(dep);
                }
            }
            return false;
        }

        private void HandleDrawingMouseDown(System.Windows.Point pos)
        {
            if (_activeTextBox != null && !_activeTextBox.IsMouseOver)
            {
                FinalizeText();
                return;
            }

            _isDrawing = true;
            _drawStartPoint = pos;

            switch (_currentTool)
            {
                case AnnotationTool.Rectangle:
                    _tempShape = new System.Windows.Shapes.Rectangle { Stroke = _drawColor, StrokeThickness = _drawThickness };
                    System.Windows.Controls.Canvas.SetLeft(_tempShape, pos.X);
                    System.Windows.Controls.Canvas.SetTop(_tempShape, pos.Y);
                    AnnotationCanvas.Children.Add(_tempShape);
                    break;
                case AnnotationTool.Ellipse:
                    _tempShape = new System.Windows.Shapes.Ellipse { Stroke = _drawColor, StrokeThickness = _drawThickness };
                    System.Windows.Controls.Canvas.SetLeft(_tempShape, pos.X);
                    System.Windows.Controls.Canvas.SetTop(_tempShape, pos.Y);
                    AnnotationCanvas.Children.Add(_tempShape);
                    break;
                case AnnotationTool.Arrow:
                    _tempShape = new System.Windows.Shapes.Path { Stroke = _drawColor, StrokeThickness = _drawThickness, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round };
                    AnnotationCanvas.Children.Add(_tempShape);
                    break;
                case AnnotationTool.Brush:
                    var poly = new System.Windows.Shapes.Polyline { Stroke = _drawColor, StrokeThickness = _drawThickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    poly.Points.Add(pos);
                    _tempShape = poly;
                    AnnotationCanvas.Children.Add(_tempShape);
                    break;
                case AnnotationTool.Text:
                    CreateTextBox(pos);
                    _isDrawing = false;
                    break;
            }
        }

        private void HandleDrawingMouseMove(System.Windows.Point pos)
        {
            if (!_isDrawing || _tempShape == null) return;

            if (_currentTool == AnnotationTool.Rectangle || _currentTool == AnnotationTool.Ellipse)
            {
                double x = Math.Min(pos.X, _drawStartPoint.X);
                double y = Math.Min(pos.Y, _drawStartPoint.Y);
                double w = Math.Abs(pos.X - _drawStartPoint.X);
                double h = Math.Abs(pos.Y - _drawStartPoint.Y);
                System.Windows.Controls.Canvas.SetLeft(_tempShape, x);
                System.Windows.Controls.Canvas.SetTop(_tempShape, y);
                _tempShape.Width = Math.Max(1, w);
                _tempShape.Height = Math.Max(1, h);
            }
            else if (_currentTool == AnnotationTool.Arrow)
            {
                UpdateArrow(_drawStartPoint, pos);
            }
            else if (_currentTool == AnnotationTool.Brush)
            {
                if (_tempShape is System.Windows.Shapes.Polyline poly)
                {
                    poly.Points.Add(pos);
                }
            }
        }

        private void HandleDrawingMouseUp() { _isDrawing = false; _tempShape = null; }

        private void UpdateArrow(System.Windows.Point start, System.Windows.Point end)
        {
            if (_tempShape is System.Windows.Shapes.Path path)
            {
                double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
                double arrowLength = 15;
                double arrowAngle = Math.PI / 6; // 30 degrees

                var geometry = new System.Windows.Media.StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    // Figure 1: Shaft (Start -> End)
                    ctx.BeginFigure(start, false, false);
                    ctx.LineTo(end, true, false);

                    // Figure 2: Arrow Head (Wing1 -> Tip -> Wing2)
                    System.Windows.Point p1 = new System.Windows.Point(
                        end.X - arrowLength * Math.Cos(angle - arrowAngle),
                        end.Y - arrowLength * Math.Sin(angle - arrowAngle));
                    System.Windows.Point p2 = new System.Windows.Point(
                        end.X - arrowLength * Math.Cos(angle + arrowAngle),
                        end.Y - arrowLength * Math.Sin(angle + arrowAngle));

                    ctx.BeginFigure(p1, false, false);
                    ctx.LineTo(end, true, false);
                    ctx.LineTo(p2, true, false);
                }
                path.Data = geometry;
            }
        }

        private void CreateTextBox(System.Windows.Point pos)
        {
            if (_activeTextBox != null) FinalizeText();

            var tb = new System.Windows.Controls.TextBox
            {
                FontSize = 24,
                Foreground = _drawColor,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new System.Windows.Thickness(1),
                BorderBrush = System.Windows.Media.Brushes.Gray,
                MinWidth = 50,
                AcceptsReturn = true
            };

            System.Windows.Controls.Canvas.SetLeft(tb, pos.X);
            System.Windows.Controls.Canvas.SetTop(tb, pos.Y);
            AnnotationCanvas.Children.Add(tb);
            tb.Loaded += (s, ev) => tb.Focus();
            tb.LostFocus += (s, ev) => FinalizeText();
            _activeTextBox = tb;
        }

        private void FinalizeText()
        {
            if (_activeTextBox == null) return;
            
            var text = _activeTextBox.Text;
            var left = System.Windows.Controls.Canvas.GetLeft(_activeTextBox);
            var top = System.Windows.Controls.Canvas.GetTop(_activeTextBox);
            
            AnnotationCanvas.Children.Remove(_activeTextBox);
            
            if (!string.IsNullOrWhiteSpace(text))
            {
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = text,
                    FontSize = 24,
                    Foreground = _drawColor,
                    FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei")
                };
                System.Windows.Controls.Canvas.SetLeft(textBlock, left);
                System.Windows.Controls.Canvas.SetTop(textBlock, top + 2);
                AnnotationCanvas.Children.Add(textBlock);
            }
            _activeTextBox = null;
        }

        private void OnToolClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string toolStr)
            {
                if (Enum.TryParse(toolStr, out AnnotationTool tool))
                {
                    if (_currentTool == tool)
                    {
                        // Toggle Color Panel if clicking the same tool
                        ColorPanel.Visibility = (ColorPanel.Visibility == Visibility.Visible) 
                            ? Visibility.Collapsed 
                            : Visibility.Visible;
                    }
                    else
                    {
                        // Switching tool: select tool and show panel
                        _currentTool = tool;
                        _isDrawing = false;
                        this.Cursor = System.Windows.Input.Cursors.Pen;
                        ColorPanel.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private void OnColorClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Background is System.Windows.Media.Brush brush)
            {
                _drawColor = brush;
            }
        }

        private void HandleResize(int index, System.Windows.Point pos)
        {
            double left = _startRect.Left;
            double top = _startRect.Top;
            double right = _startRect.Right;
            double bottom = _startRect.Bottom;

            switch (index)
            {
                case 0: left = pos.X; top = pos.Y; break;
                case 1: top = pos.Y; break;
                case 2: right = pos.X; top = pos.Y; break;
                case 3: right = pos.X; break;
                case 4: right = pos.X; bottom = pos.Y; break;
                case 5: bottom = pos.Y; break;
                case 6: left = pos.X; bottom = pos.Y; break;
                case 7: left = pos.X; break;
            }

            var rect = new System.Windows.Rect(
                new System.Windows.Point(Math.Min(left, right), Math.Min(top, bottom)),
                new System.Windows.Point(Math.Max(left, right), Math.Max(top, bottom)));
            UpdateSelection(rect);
        }

        private int GetHandleIndex(System.Windows.Point pos)
        {
            System.Windows.Rect r = _selectionGeometry.Rect;
            if (r.Width <= 0) return -1;
            double t = 10;
            if (new System.Windows.Rect(r.Left - t, r.Top - t, 2 * t, 2 * t).Contains(pos)) return 0;
            if (new System.Windows.Rect(r.Right - t, r.Top - t, 2 * t, 2 * t).Contains(pos)) return 2;
            if (new System.Windows.Rect(r.Right - t, r.Bottom - t, 2 * t, 2 * t).Contains(pos)) return 4;
            if (new System.Windows.Rect(r.Left - t, r.Bottom - t, 2 * t, 2 * t).Contains(pos)) return 6;
            if (r.Contains(pos)) return -2;
            return -1;
        }

        private System.Windows.Input.Cursor GetCursorForHandle(int index)
        {
            switch (index)
            {
                case 0: case 4: return System.Windows.Input.Cursors.SizeNWSE;
                case 2: case 6: return System.Windows.Input.Cursors.SizeNESW;
                case -2: return System.Windows.Input.Cursors.SizeAll;
                default: return System.Windows.Input.Cursors.Arrow;
            }
        }

        private void UpdateSelection(System.Windows.Rect rect)
        {
            System.Windows.Controls.Canvas.SetLeft(SelectionRect, rect.X);
            System.Windows.Controls.Canvas.SetTop(SelectionRect, rect.Y);
            SelectionRect.Width = rect.Width;
            SelectionRect.Height = rect.Height;
            SelectionRect.Visibility = Visibility.Visible;
            _selectionGeometry.Rect = rect;
            
            SizeText.Text = $"{(int)rect.Width} x {(int)rect.Height}";
            SizeLabel.Visibility = Visibility.Visible;
            System.Windows.Controls.Canvas.SetLeft(SizeLabel, rect.X);
            System.Windows.Controls.Canvas.SetTop(SizeLabel, rect.Y - 25);
            UpdateToolbarPosition(rect);
        }

        private void UpdateToolbarPosition(System.Windows.Rect rect)
        {
            if (rect.Width > 0 && rect.Height > 0)
            {
                ToolbarArea.Visibility = Visibility.Visible;
                ToolbarArea.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double width = ToolbarArea.DesiredSize.Width;
                double height = ToolbarArea.DesiredSize.Height;
                double left = rect.Right - width;
                if (left < 0) left = 0;
                double top = rect.Bottom + 5;
                if (top + height > this.ActualHeight) top = rect.Top - height - 5;
                System.Windows.Controls.Canvas.SetLeft(ToolbarArea, left);
                System.Windows.Controls.Canvas.SetTop(ToolbarArea, top);
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e) 
        { 
            try
            {
                var rect = _selectionGeometry.Rect;
                if (rect.Width <= 0 || rect.Height <= 0) return;

                // Hide UI elements before capture
                SelectionRect.Visibility = Visibility.Collapsed;
                SizeLabel.Visibility = Visibility.Collapsed;
                ToolbarArea.Visibility = Visibility.Collapsed;
                if (MaskPath != null) MaskPath.Visibility = Visibility.Collapsed; // Hide mask too just in case
                this.UpdateLayout();

                // 1. Capture the image
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)this.ActualWidth, (int)this.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(this);
                var crop = new System.Windows.Media.Imaging.CroppedBitmap(rtb, new System.Windows.Int32Rect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));

                // Restore UI elements
                SelectionRect.Visibility = Visibility.Visible;
                SizeLabel.Visibility = Visibility.Visible;
                ToolbarArea.Visibility = Visibility.Visible;
                if (MaskPath != null) MaskPath.Visibility = Visibility.Visible;

                // 2. Open Save File Dialog
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}",
                    DefaultExt = ".png",
                    Filter = "PNG Image (.png)|*.png|JPEG Image (.jpg)|*.jpg|Bitmap Image (.bmp)|*.bmp"
                };

                if (dlg.ShowDialog() == true)
                {
                    // 3. Save to file
                    BitmapEncoder encoder = new PngBitmapEncoder(); // Default
                    if (dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) encoder = new JpegBitmapEncoder();
                    else if (dlg.FileName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) encoder = new BmpBitmapEncoder();

                    encoder.Frames.Add(BitmapFrame.Create(crop));
                    using (var stream = new System.IO.FileStream(dlg.FileName, System.IO.FileMode.Create))
                    {
                        encoder.Save(stream);
                    }
                    
                    // 4. Close window after successful save
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                // Ensure UI is restored even on error
                SelectionRect.Visibility = Visibility.Visible;
                SizeLabel.Visibility = Visibility.Visible;
                ToolbarArea.Visibility = Visibility.Visible;
                if (MaskPath != null) MaskPath.Visibility = Visibility.Visible;

                System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) { this.Close(); }
        private void OnConfirmClick(object sender, RoutedEventArgs e) { SaveToClipboard(); this.Close(); }

        private void SaveToClipboard()
        {
            var rect = _selectionGeometry.Rect;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // Hide UI elements before capture
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            ToolbarArea.Visibility = Visibility.Collapsed;
            if (MaskPath != null) MaskPath.Visibility = Visibility.Collapsed;
            this.UpdateLayout();

            // Use ActualWidth/Height to capture full window including annotations
            System.Windows.Media.Imaging.RenderTargetBitmap rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)this.ActualWidth, (int)this.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(this);

            var crop = new System.Windows.Media.Imaging.CroppedBitmap(rtb, new System.Windows.Int32Rect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
            System.Windows.Clipboard.SetImage(crop);

             // Restore (though window closes immediately after, good practice)
            SelectionRect.Visibility = Visibility.Visible;
            SizeLabel.Visibility = Visibility.Visible;
            ToolbarArea.Visibility = Visibility.Visible;
            if (MaskPath != null) MaskPath.Visibility = Visibility.Visible;
        }
        private void GetDpiScale(double x, double y, out double scaleX, out double scaleY)
        {
            scaleX = 1.0;
            scaleY = 1.0;

            try
            {
                var point = new System.Drawing.Point((int)Math.Round(x), (int)Math.Round(y));
                IntPtr hMonitor = Services.NativeMethods.MonitorFromPoint(point, Services.NativeMethods.MONITOR_DEFAULTTONEAREST);

                if (hMonitor != IntPtr.Zero)
                {
                    uint dpiX, dpiY;
                    if (Services.NativeMethods.GetDpiForMonitor(hMonitor, Services.NativeMethods.MonitorDpiType.Effective, out dpiX, out dpiY) == 0)
                    {
                        scaleX = dpiX / 96.0;
                        scaleY = dpiY / 96.0;
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback to system DPI or 1.0 if fails
                // Console.WriteLine($"DPI check failed: {ex.Message}");
            }
        }
    }
}