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
        private enum AnnotationTool { None, Rectangle, Ellipse, Arrow, Brush, Text, Mosaic }
        private AnnotationTool _currentTool = AnnotationTool.None;
        private System.Windows.Shapes.Shape _tempShape;
        private System.Windows.Controls.TextBox _activeTextBox;
        private System.Windows.Point _drawStartPoint;
        private bool _isDrawing = false;
        private System.Windows.Media.Brush _drawColor;
        private double _drawThickness = 3.0;
        private Services.TranslationService _translationService;

        private Services.AppSettings _settings;
        private BitmapSource _originalScreenshot;
        private BitmapSource _mosaicBitmap;
        private double _scaleX = 1.0;
        private double _scaleY = 1.0;

        private void InitMosaicBitmap()
        {
            if (_originalScreenshot == null) return;

            // 1. Downscale to create pixelation effect (e.g., block size 15)
            // Use TransformedBitmap directly. No need to manual copy pixels.
            double blockSize = 15.0;
            var transform = new ScaleTransform(1.0 / blockSize, 1.0 / blockSize);
            var smallBmp = new TransformedBitmap(_originalScreenshot, transform);
            
            _mosaicBitmap = smallBmp;
            // The ImageBrush will stretch this small bitmap to the full Viewport.
            // NearestNeighbor on the Shape will make it pixelated.
        }

        public OverlayWindow(BitmapSource screenshot, double x, double y, Services.AppSettings settings = null)
        {
            InitializeComponent();
            _originalScreenshot = screenshot;
            BackgroundImage.Source = screenshot;
            _settings = settings;

            // Calculate DPI Scale for the target monitor
            GetDpiScale(x, y, out _scaleX, out _scaleY);

            // Convert Pixels (from Screenshot/Screen.Bounds) to WPF DIUs
            this.Width = screenshot.PixelWidth / _scaleX;
            this.Height = screenshot.PixelHeight / _scaleY;
            this.Left = x / _scaleX;
            this.Top = y / _scaleY;

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
                // 检查是否点击了文字块，如果是则不拦截，让事件传递到文字块处理拖动
                var hitElement = e.OriginalSource as FrameworkElement;
                if (hitElement is System.Windows.Controls.Border || hitElement is System.Windows.Controls.TextBlock)
                {
                    // 检查这个元素是否在 AnnotationCanvas 中（是文字标注）
                    var parent = System.Windows.Media.VisualTreeHelper.GetParent(hitElement);
                    while (parent != null)
                    {
                        if (parent == AnnotationCanvas)
                        {
                            // 是文字标注，不拦截事件
                            return;
                        }
                        parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                    }
                }
                
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
                    _tempShape = new System.Windows.Shapes.Path 
                    { 
                        Stroke = _drawColor, 
                        StrokeThickness = 4, // WeChat arrow is thicker and more visible
                        Fill = _drawColor,   // Fill the arrowhead
                        StrokeEndLineCap = PenLineCap.Round, 
                        StrokeStartLineCap = PenLineCap.Round 
                    };
                    AnnotationCanvas.Children.Add(_tempShape);
                    break;
                case AnnotationTool.Brush:
                    var poly = new System.Windows.Shapes.Polyline { Stroke = _drawColor, StrokeThickness = _drawThickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    poly.Points.Add(pos);
                    _tempShape = poly;
                    AnnotationCanvas.Children.Add(_tempShape);
                    break;
                case AnnotationTool.Mosaic:
                    if (_mosaicBitmap == null) InitMosaicBitmap();
                    var mosaicPoly = new System.Windows.Shapes.Polyline
                    {
                        Stroke = new ImageBrush(_mosaicBitmap)
                        {
                            ViewportUnits = BrushMappingMode.Absolute,
                            Viewport = new Rect(0, 0, this.ActualWidth, this.ActualHeight),
                            Stretch = Stretch.Fill
                        },
                        StrokeThickness = 20, // Thicker stroke for mosaic
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    RenderOptions.SetBitmapScalingMode(mosaicPoly, BitmapScalingMode.NearestNeighbor);
                    mosaicPoly.Points.Add(pos);
                    _tempShape = mosaicPoly;
                    AnnotationCanvas.Children.Add(_tempShape);
                    break;
                case AnnotationTool.Text:
                    // 检查是否点击了已有的文字块（现在是 Border 容器），如果是则不创建新文字框
                    var hitElement = AnnotationCanvas.InputHitTest(pos) as FrameworkElement;
                    if (hitElement is System.Windows.Controls.Border || hitElement is System.Windows.Controls.TextBlock)
                    {
                        _isDrawing = false;
                        return; // 点击的是已有文字，不创建新文字框
                    }
                    CreateTextBox(pos);
                    _isDrawing = false;
                    break;
            }
            UpdateUndoState();
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
            else if (_currentTool == AnnotationTool.Brush || _currentTool == AnnotationTool.Mosaic)
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
                // WeChat-style arrow: Thin shaft, sharp filled head
                
                System.Windows.Vector vec = end - start;
                double length = vec.Length;
                if (length < 1) return;

                System.Windows.Vector dir = vec;
                dir.Normalize();

                // Arrow head configuration (WeChat style is robust)
                double headLength = 18; // Increased from 15
                double headWidth = 12;  // Increased from 10
                
                // Calculate arrow head points
                // We want the tip to be at 'end'
                // The base of the head is at 'end - dir * headLength'
                // The wings are displaced perpendicularly
                
                System.Windows.Vector perpendicular = new System.Windows.Vector(-dir.Y, dir.X);
                
                System.Windows.Point tip = end;
                System.Windows.Point baseCenter = end - dir * headLength;
                System.Windows.Point wing1 = baseCenter + perpendicular * (headWidth / 2);
                System.Windows.Point wing2 = baseCenter - perpendicular * (headWidth / 2);

                var geometry = new System.Windows.Media.StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    // Draw Shaft (Start to BaseCenter)
                    ctx.BeginFigure(start, false, false);
                    ctx.LineTo(baseCenter, true, false);
                    
                    // Arrow Head (Closed figure to allow fill)
                    ctx.BeginFigure(tip, true, true);
                    ctx.LineTo(wing1, true, false);
                    ctx.LineTo(wing2, true, false);
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
                BorderBrush = System.Windows.Media.Brushes.White, // Revert to White/Light for standard editing look
                CaretBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#07C160")), // Keep WeChat Green Caret
                Padding = new System.Windows.Thickness(5),
                MinWidth = 50,
                AcceptsReturn = true
            };

            System.Windows.Controls.Canvas.SetLeft(tb, pos.X);
            System.Windows.Controls.Canvas.SetTop(tb, pos.Y);
            AnnotationCanvas.Children.Add(tb);
            tb.Loaded += (s, ev) => tb.Focus();
            tb.LostFocus += (s, ev) => FinalizeText();
            _activeTextBox = tb;
            UpdateUndoState();
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
                // 使用 Border 包装 TextBlock 以获得更好的鼠标事件支持
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = text,
                    FontSize = 24,
                    Foreground = _drawColor,
                    FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei")
                };
                
                var border = new System.Windows.Controls.Border
                {
                    Child = textBlock,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Padding = new System.Windows.Thickness(2),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                
                System.Windows.Controls.Canvas.SetLeft(border, left);
                System.Windows.Controls.Canvas.SetTop(border, top);
                
                // 添加拖动功能到 Border
                System.Windows.Point? dragStart = null;
                System.Windows.Point? elementStart = null;
                bool isDragging = false;
                
                border.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    dragStart = e.GetPosition(AnnotationCanvas);
                    elementStart = new System.Windows.Point(
                        System.Windows.Controls.Canvas.GetLeft(border),
                        System.Windows.Controls.Canvas.GetTop(border)
                    );
                    isDragging = true;
                    border.CaptureMouse();
                    e.Handled = true;
                };
                
                border.PreviewMouseMove += (s, e) =>
                {
                    if (isDragging && dragStart.HasValue && elementStart.HasValue && border.IsMouseCaptured)
                    {
                        var currentPos = e.GetPosition(AnnotationCanvas);
                        var offset = currentPos - dragStart.Value;
                        System.Windows.Controls.Canvas.SetLeft(border, elementStart.Value.X + offset.X);
                        System.Windows.Controls.Canvas.SetTop(border, elementStart.Value.Y + offset.Y);
                        e.Handled = true;
                    }
                };
                
                border.PreviewMouseLeftButtonUp += (s, e) =>
                {
                    if (isDragging)
                    {
                        isDragging = false;
                        border.ReleaseMouseCapture();
                        dragStart = null;
                        elementStart = null;
                        e.Handled = true;
                    }
                };
                
                AnnotationCanvas.Children.Add(border);
            }
            _activeTextBox = null;
            UpdateUndoState();
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

        private async void OnTranslateClick(object sender, RoutedEventArgs e)
        {
            if (_translationService == null) _translationService = new Services.TranslationService();

            // Toggle visibility if already visible
            if (TranslationResultOverlay.Visibility == Visibility.Visible)
            {
                TranslationResultOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            var rect = _selectionGeometry.Rect;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            try
            {
                // 1. Position and show loading state
                Canvas.SetLeft(TranslationResultOverlay, rect.X);
                Canvas.SetTop(TranslationResultOverlay, rect.Y);
                TranslationResultOverlay.Width = rect.Width;
                TranslationResultOverlay.Height = rect.Height;
                TranslationResultOverlay.Children.Clear();
                TranslationResultOverlay.Children.Add(new TextBlock 
                { 
                    Text = "正在识别并翻译...", 
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)),
                    Padding = new System.Windows.Thickness(5),
                    FontSize = 14
                });
                TranslationResultOverlay.Visibility = Visibility.Visible;

                // 2. Hide specific UI for clean capture
                ToolbarArea.Visibility = Visibility.Collapsed;
                if (MaskPath != null) MaskPath.Visibility = Visibility.Collapsed;
                this.UpdateLayout();

                // 3. Capture high-res crop
                var crop = GetCroppedCapture(rect, hideSelectionUI: false);
                if (crop == null)
                {
                    ToolbarArea.Visibility = Visibility.Visible;
                    if (MaskPath != null) MaskPath.Visibility = Visibility.Visible;
                    return;
                }

                // 4. Restore mask immediately
                if (MaskPath != null) MaskPath.Visibility = Visibility.Visible;

                // 5. Call service
                var result = await _translationService.AnalyzeAndTranslateAsync(crop);
                
                if (result.Success)
                {
                    TranslationResultOverlay.Children.Clear();
                    TranslationResultOverlay.Visibility = Visibility.Visible;

                    foreach (var line in result.Lines)
                    {
                        if (string.IsNullOrEmpty(line.Text)) continue;

                        // Calculate font size based on line height (heuristic for WeChat style)
                        double fontSize = Math.Max(10, line.BoundingRect.Height * 0.75);

                        var lineBorder = new Border
                        {
                            Background = HexToBrush(line.BackgroundColor),
                            Width = line.BoundingRect.Width + 4,
                            Height = line.BoundingRect.Height,
                            Child = new TextBlock
                            {
                                Text = line.Text,
                                Foreground = HexToBrush(line.TextColor),
                                FontSize = fontSize,
                                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei"),
                                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                                TextWrapping = System.Windows.TextWrapping.NoWrap
                            }
                        };

                        Canvas.SetLeft(lineBorder, line.BoundingRect.X);
                        Canvas.SetTop(lineBorder, line.BoundingRect.Y);
                    TranslationResultOverlay.Children.Add(lineBorder);
                    }
                }
                else
                {
                    // Error fallback
                    TranslationResultOverlay.Children.Clear();
                    TranslationResultOverlay.Children.Add(new TextBlock 
                    { 
                        Text = $"错误: {result.ErrorMessage}", 
                        Foreground = System.Windows.Media.Brushes.Red,
                        FontSize = 14,
                        Background = System.Windows.Media.Brushes.White
                    });
                    TranslationResultOverlay.Visibility = Visibility.Visible;
                    ToolbarArea.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                ToolbarArea.Visibility = Visibility.Visible;
                if (MaskPath != null) MaskPath.Visibility = Visibility.Visible;
                // Simple error display
                TranslationResultOverlay.Children.Clear();
                TranslationResultOverlay.Children.Add(new TextBlock { Text = $"异常: {ex.Message}", Foreground = System.Windows.Media.Brushes.Red });
                TranslationResultOverlay.Visibility = Visibility.Visible;
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

        private BitmapSource GetCroppedCapture(System.Windows.Rect rect, bool hideSelectionUI = true)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return null;

            try
            {
                // If there are annotations, we MUST use RenderTargetBitmap to merge them
                if (AnnotationCanvas.Children.Count > 0 || !hideSelectionUI)
                {
                    // Hide UI helpers based on requested flags
                    if (hideSelectionUI)
                    {
                        SelectionRect.Visibility = Visibility.Collapsed;
                        SizeLabel.Visibility = Visibility.Collapsed;
                    }
                    
                    // Always hide translation and toolbar for clean capture
                    var oldTranslationVisibility = TranslationResultOverlay.Visibility;
                    TranslationResultOverlay.Visibility = Visibility.Collapsed;
                    ToolbarArea.Visibility = Visibility.Collapsed;
                    if (MaskPath != null) MaskPath.Visibility = Visibility.Collapsed;
                    this.UpdateLayout();

                    // Render at high resolution (scaled up by DPI)
                    int pixelWidth = (int)Math.Round(this.ActualWidth * _scaleX);
                    int pixelHeight = (int)Math.Round(this.ActualHeight * _scaleY);
                    
                    var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * _scaleX, 96 * _scaleY, PixelFormats.Pbgra32);
                    rtb.Render(this);

                    // Restore UI helpers
                    if (hideSelectionUI)
                    {
                        SelectionRect.Visibility = Visibility.Visible;
                        SizeLabel.Visibility = Visibility.Visible;
                    }
                    TranslationResultOverlay.Visibility = oldTranslationVisibility;
                    ToolbarArea.Visibility = Visibility.Visible;
                    if (MaskPath != null) MaskPath.Visibility = Visibility.Visible;

                    // Crop using the high-res pixels
                    var pixelRect = new Int32Rect(
                        (int)Math.Round(rect.X * _scaleX),
                        (int)Math.Round(rect.Y * _scaleY),
                        (int)Math.Round(rect.Width * _scaleX),
                        (int)Math.Round(rect.Height * _scaleY));

                    return new CroppedBitmap(rtb, pixelRect);
                }
                else
                {
                    // Clean capture: direct crop from the original raw screenshot (maximum quality)
                    var pixelRect = new Int32Rect(
                        (int)Math.Round(rect.X * _scaleX),
                        (int)Math.Round(rect.Y * _scaleY),
                        (int)Math.Round(rect.Width * _scaleX),
                        (int)Math.Round(rect.Height * _scaleY));

                    // Ensure coordinates are within source bounds
                    pixelRect.X = Math.Max(0, Math.Min(pixelRect.X, _originalScreenshot.PixelWidth - 1));
                    pixelRect.Y = Math.Max(0, Math.Min(pixelRect.Y, _originalScreenshot.PixelHeight - 1));
                    pixelRect.Width = Math.Max(1, Math.Min(pixelRect.Width, _originalScreenshot.PixelWidth - pixelRect.X));
                    pixelRect.Height = Math.Max(1, Math.Min(pixelRect.Height, _originalScreenshot.PixelHeight - pixelRect.Y));

                    return new CroppedBitmap(_originalScreenshot, pixelRect);
                }
            }
            catch
            {
                // Fallback UI restore
                SelectionRect.Visibility = Visibility.Visible;
                SizeLabel.Visibility = Visibility.Visible;
                ToolbarArea.Visibility = Visibility.Visible;
                if (MaskPath != null) MaskPath.Visibility = Visibility.Visible;
                throw;
            }
        }

        private int GetHandleIndex(System.Windows.Point pos)
        {
            System.Windows.Rect r = _selectionGeometry.Rect;
            if (r.Width <= 0) return -1;
            double t = 10; // Tolerance

            // Corners (Priority)
            if (new System.Windows.Rect(r.Left - t, r.Top - t, 2 * t, 2 * t).Contains(pos)) return 0; // Top-Left
            if (new System.Windows.Rect(r.Right - t, r.Top - t, 2 * t, 2 * t).Contains(pos)) return 2; // Top-Right
            if (new System.Windows.Rect(r.Right - t, r.Bottom - t, 2 * t, 2 * t).Contains(pos)) return 4; // Bottom-Right
            if (new System.Windows.Rect(r.Left - t, r.Bottom - t, 2 * t, 2 * t).Contains(pos)) return 6; // Bottom-Left

            // Edges
            if (new System.Windows.Rect(r.Left + t, r.Top - t, Math.Max(0, r.Width - 2 * t), 2 * t).Contains(pos)) return 1; // Top
            if (new System.Windows.Rect(r.Right - t, r.Top + t, 2 * t, Math.Max(0, r.Height - 2 * t)).Contains(pos)) return 3; // Right
            if (new System.Windows.Rect(r.Left + t, r.Bottom - t, Math.Max(0, r.Width - 2 * t), 2 * t).Contains(pos)) return 5; // Bottom
            if (new System.Windows.Rect(r.Left - t, r.Top + t, 2 * t, Math.Max(0, r.Height - 2 * t)).Contains(pos)) return 7; // Left

            if (r.Contains(pos)) return -2; // Inside (Move)
            return -1;
        }

        private System.Windows.Input.Cursor GetCursorForHandle(int index)
        {
            switch (index)
            {
                case 0: case 4: return System.Windows.Input.Cursors.SizeNWSE;
                case 1: case 5: return System.Windows.Input.Cursors.SizeNS;
                case 2: case 6: return System.Windows.Input.Cursors.SizeNESW;
                case 3: case 7: return System.Windows.Input.Cursors.SizeWE;
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
            
            // Update the existing geometry's Rect, which updates the MaskPath binding
            _selectionGeometry.Rect = rect;

            // Update AnnotationCanvas clip to match selection
            // We create a new geometry for the clip to avoid sharing the same instance if that causes issues
            AnnotationCanvas.Clip = new RectangleGeometry(rect);

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

                // 1. Capture high-res crop
                var crop = GetCroppedCapture(rect);
                if (crop == null) return;

                // 2. Open Save File Dialog
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}",
                    DefaultExt = ".png",
                    Filter = "PNG Image (.png)|*.png|JPEG Image (.jpg)|*.jpg|Bitmap Image (.bmp)|*.bmp",
                    InitialDirectory = _settings?.DefaultSavePath
                };

                if (dlg.ShowDialog() == true)
                {
                    // Update default save path if user chose a different one
                    if (_settings != null)
                    {
                        var newPath = System.IO.Path.GetDirectoryName(dlg.FileName);
                        if (!string.IsNullOrEmpty(newPath))
                        {
                            _settings.DefaultSavePath = newPath;
                        }
                    }
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
                System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) { this.Close(); }
        private void OnConfirmClick(object sender, RoutedEventArgs e) { SaveToClipboard(); this.Close(); }

        private Services.OcrService _ocrService;

        private async void OnOcrClick(object sender, RoutedEventArgs e)
        {
            if (_ocrService == null) _ocrService = new Services.OcrService();

            // Toggle visibility if already visible
            if (TranslationResultOverlay.Visibility == Visibility.Visible)
            {
                TranslationResultOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            var rect = _selectionGeometry.Rect;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            try
            {
                // 1. Position and show loading state
                Canvas.SetLeft(TranslationResultOverlay, rect.X);
                Canvas.SetTop(TranslationResultOverlay, rect.Y);
                TranslationResultOverlay.Width = rect.Width;
                TranslationResultOverlay.Height = rect.Height;
                TranslationResultOverlay.Children.Clear();
                TranslationResultOverlay.Children.Add(new TextBlock 
                { 
                    Text = "正在识别文字...", 
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)),
                    Padding = new System.Windows.Thickness(5),
                    FontSize = 14
                });
                TranslationResultOverlay.Visibility = Visibility.Visible;

                // 2. Hide specific UI for clean capture
                ToolbarArea.Visibility = Visibility.Collapsed;
                if (MaskPath != null) MaskPath.Visibility = Visibility.Collapsed;
                this.UpdateLayout();

                // 3. Capture high-res crop
                var crop = GetCroppedCapture(rect, hideSelectionUI: false);
                if (crop == null)
                {
                    ToolbarArea.Visibility = Visibility.Visible;
                    if (MaskPath != null) MaskPath.Visibility = Visibility.Visible;
                    return;
                }

                // 4. Restore mask immediately
                if (MaskPath != null) MaskPath.Visibility = Visibility.Visible;

                // 5. Call service
                var result = await _ocrService.RecognizeTextAsync(crop);
                
                if (result.Success)
                {
                    // Construct full text
                    var fullText = string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
                    
                    // Open Result Window
                    var resultWin = new TextRecognitionWindow(crop, fullText);
                    resultWin.Show();
                    
                    // Close this overlay window as the action is complete
                    this.Close();
                }
                else
                {
                    // Error fallback
                    TranslationResultOverlay.Children.Clear();
                    TranslationResultOverlay.Children.Add(new TextBlock 
                    { 
                        Text = $"错误: {result.ErrorMessage}", 
                        Foreground = System.Windows.Media.Brushes.Red,
                        FontSize = 14,
                        Background = System.Windows.Media.Brushes.White
                    });
                    TranslationResultOverlay.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                // Simple error display
                TranslationResultOverlay.Children.Clear();
                TranslationResultOverlay.Children.Add(new TextBlock { Text = $"异常: {ex.Message}", Foreground = System.Windows.Media.Brushes.Red });
                TranslationResultOverlay.Visibility = Visibility.Visible;
            }
            finally
            {
                ToolbarArea.Visibility = Visibility.Visible; // Always restore toolbar
            }
        }

        private void OnLongScreenshotClick(object sender, RoutedEventArgs e)
        {
            // TODO: 实现长截图功能
            System.Windows.MessageBox.Show("长截图功能开发中...", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private void OnUndoClick(object sender, RoutedEventArgs e)
        {
            if (AnnotationCanvas.Children.Count > 0)
            {
                var lastElement = AnnotationCanvas.Children[AnnotationCanvas.Children.Count - 1];
                
                // If we are undoing the currently active text box, clear the reference
                if (lastElement == _activeTextBox)
                {
                    _activeTextBox = null;
                    _isDrawing = false;
                }

                AnnotationCanvas.Children.RemoveAt(AnnotationCanvas.Children.Count - 1);
                UpdateUndoState();
            }
        }

        private void UpdateUndoState()
        {
            if (UndoBtn != null)
            {
                UndoBtn.IsEnabled = AnnotationCanvas.Children.Count > 0;
            }
        }

        private void SaveToClipboard()
        {
            var rect = _selectionGeometry.Rect;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            var crop = GetCroppedCapture(rect);
            if (crop != null)
            {
                System.Windows.Clipboard.SetImage(crop);
            }
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

        private System.Windows.Media.Brush HexToBrush(string hex)
        {
            try
            {
                return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
            }
            catch
            {
                return System.Windows.Media.Brushes.White;
            }
        }
    }
}
