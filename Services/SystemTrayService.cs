using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace RefScrn.Services
{
    public class SystemTrayService : IDisposable
    {
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        public SystemTrayService(Action onExit, Action onSettings)
        {
            _contextMenu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Font = new Font("Microsoft YaHei", 10.5f),
                Renderer = new WeChatMenuRenderer(),
                Padding = new Padding(0), // NO padding for the container
                AutoSize = true,
                BackColor = Color.White,
                Margin = new Padding(0)
            };

            int menuWidth = 110;
            int itemHeight = 36;

            var settingsItem = new ToolStripMenuItem("设置") 
            { 
                AutoSize = false,
                Size = new Size(menuWidth, itemHeight),
                Margin = new Padding(0)
            };
            settingsItem.Click += (s, e) => onSettings();
            
            var exitItem = new ToolStripMenuItem("退出程序") 
            { 
                AutoSize = false,
                Size = new Size(menuWidth, itemHeight),
                Margin = new Padding(0)
            };
            exitItem.Click += (s, e) => onExit();

            _contextMenu.Items.Add(settingsItem);
            _contextMenu.Items.Add(new ToolStripSeparator { 
                AutoSize = false, 
                Size = new Size(menuWidth, 1), 
                Margin = new Padding(0, 1, 0, 1) 
            });
            _contextMenu.Items.Add(exitItem);

            _contextMenu.HandleCreated += (s, e) => {
                var handle = _contextMenu.Handle;
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            };

            try
            {
                // Load from Embedded Resource (Pack URI)
                var uri = new Uri("pack://application:,,,/assets/app_icon.ico", UriKind.Absolute);
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                
                if (streamInfo != null)
                {
                    using (streamInfo.Stream)
                    {
                        var icon = new System.Drawing.Icon(streamInfo.Stream);
                        _notifyIcon = new NotifyIcon
                        {
                            Icon = icon,
                            Visible = true,
                            Text = "RefScrn",
                            ContextMenuStrip = _contextMenu
                        };
                    }
                }
                else
                {
                    // Fallback should not happen if build is correct
                    throw new Exception("Resource stream is null");
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Failed to load icon: {ex.Message}");
                // Fallback to system icon
                _notifyIcon = new NotifyIcon
                {
                    Icon = SystemIcons.Application,
                    Visible = true,
                    Text = "RefScrn",
                    ContextMenuStrip = _contextMenu
                };
            }
        }

        public void ShowMessage(string title, string message)
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            if (_contextMenu != null)
            {
                _contextMenu.Dispose();
            }
        }
    }

    public class WeChatMenuRenderer : ToolStripRenderer
    {
        private static readonly Color WeChatGreen = Color.FromArgb(7, 193, 96);

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                int margin = 5;
                int radius = 6;
                // Use e.Item.Width to be safe
                Rectangle rect = new Rectangle(margin, 2, e.Item.Width - margin * 2, e.Item.Height - 4);
                
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = GetRoundedRectPath(rect, radius))
                using (var brush = new SolidBrush(WeChatGreen))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }
        }

        private GraphicsPath GetRoundedRectPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2f;
            if (diameter > rect.Width) diameter = rect.Width;
            if (diameter > rect.Height) diameter = rect.Height;

            RectangleF arc = new RectangleF(rect.Location, new SizeF(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Rectangle rect = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
            Color textColor = e.Item.Selected ? Color.White : Color.Black;
            
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | 
                                   TextFormatFlags.VerticalCenter | 
                                   TextFormatFlags.SingleLine | 
                                   TextFormatFlags.NoPadding;
            
            TextRenderer.DrawText(e.Graphics, e.Text, e.Item.Font, rect, textColor, flags);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Color.FromArgb(245, 245, 245)))
            {
                e.Graphics.DrawLine(pen, 5, e.Item.Height / 2, e.Item.Width - 5, e.Item.Height / 2);
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Color.White);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Subtle border
            using (var pen = new Pen(Color.FromArgb(235, 235, 235)))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }
    }
}