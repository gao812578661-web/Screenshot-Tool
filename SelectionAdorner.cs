using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RefScrn
{
    public class SelectionAdorner
    {
        private Canvas _canvas;
        private System.Windows.Shapes.Rectangle _selectionRect;
        private System.Windows.Shapes.Rectangle[] _handles;
        private const double HandleSize = 16; // Hit test size is virtual, visual size is 8

        public SelectionAdorner(Canvas canvas, System.Windows.Shapes.Rectangle selectionRect)
        {
            _canvas = canvas;
            _selectionRect = selectionRect;
            _handles = new System.Windows.Shapes.Rectangle[8];
            
            for (int i = 0; i < 8; i++)
            {
                _handles[i] = new System.Windows.Shapes.Rectangle
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x07, 0xC1, 0x60)), // WeChat Green
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1,
                    Visibility = Visibility.Collapsed
                };
                _canvas.Children.Add(_handles[i]);
            }
        }

        public void Update(Rect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var h in _handles) h.Visibility = Visibility.Visible;

            // 0 1 2
            // 7   3
            // 6 5 4
            
            double x = rect.X;
            double y = rect.Y;
            double w = rect.Width;
            double height = rect.Height;
            double offset = 4; // Half of 8px width

            SetPos(0, x - offset, y - offset);
            SetPos(1, x + w / 2 - offset, y - offset);
            SetPos(2, x + w - offset, y - offset);
            SetPos(3, x + w - offset, y + height / 2 - offset);
            SetPos(4, x + w - offset, y + height - offset);
            SetPos(5, x + w / 2 - offset, y + height - offset);
            SetPos(6, x - offset, y + height - offset);
            SetPos(7, x - offset, y + height / 2 - offset);
        }

        private void SetPos(int index, double x, double y)
        {
            Canvas.SetLeft(_handles[index], x);
            Canvas.SetTop(_handles[index], y);
        }

        public int GetHandleUnderMouse(System.Windows.Point p)
        {
            for (int i = 0; i < 8; i++)
            {
                if (_handles[i].Visibility == Visibility.Visible)
                {
                    double left = Canvas.GetLeft(_handles[i]);
                    double top = Canvas.GetTop(_handles[i]);
                    if (p.X >= left && p.X <= left + HandleSize && p.Y >= top && p.Y <= top + HandleSize)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        public System.Windows.Input.Cursor GetCursorForHandle(int handleIndex)
        {
            switch (handleIndex)
            {
                case 0: return System.Windows.Input.Cursors.SizeNWSE; // Top-Left
                case 1: return System.Windows.Input.Cursors.SizeNS;   // Top-Center
                case 2: return System.Windows.Input.Cursors.SizeNESW; // Top-Right
                case 3: return System.Windows.Input.Cursors.SizeWE;   // Right-Center
                case 4: return System.Windows.Input.Cursors.SizeNWSE; // Bottom-Right
                case 5: return System.Windows.Input.Cursors.SizeNS;   // Bottom-Center
                case 6: return System.Windows.Input.Cursors.SizeNESW; // Bottom-Left
                case 7: return System.Windows.Input.Cursors.SizeWE;   // Left-Center
                default: return System.Windows.Input.Cursors.Arrow;
            }
        }

        public void Hide()
        {
            foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
        }
    }
}
