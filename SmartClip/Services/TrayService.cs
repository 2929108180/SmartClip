using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SmartClip.Services
{
    public class TrayService : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private readonly Action _showWindowAction;
        private readonly Action _exitAction;

        public TrayService(Action showWindowAction, Action exitAction)
        {
            _showWindowAction = showWindowAction;
            _exitAction = exitAction;
        }

        public void Initialize()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "SmartClip - 按 Win+V 打开",
                Visible = true,
                Icon = LoadIcon()
            };

            // Create context menu
            var contextMenu = new ContextMenuStrip();

            var showItem = new ToolStripMenuItem("打开 SmartClip");
            showItem.Click += (s, e) => _showWindowAction();
            showItem.Font = new Font(showItem.Font, FontStyle.Bold);
            contextMenu.Items.Add(showItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var startupItem = new ToolStripMenuItem("开机自启动");
            startupItem.Checked = IsStartupEnabled();
            startupItem.Click += (s, e) =>
            {
                ToggleStartup();
                startupItem.Checked = IsStartupEnabled();
            };
            contextMenu.Items.Add(startupItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += (s, e) => _exitAction();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double click to show window
            _notifyIcon.DoubleClick += (s, e) => _showWindowAction();
        }

        private Icon LoadIcon()
        {
            try
            {
                // Try to load from exe resources
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null) return icon;
                }
            }
            catch { }

            // Fallback: create a simple icon programmatically
            return CreateDefaultIcon();
        }

        private Icon CreateDefaultIcon()
        {
            // Create a simple clipboard icon programmatically
            var bitmap = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // Draw clipboard shape
                using var brush = new SolidBrush(Color.FromArgb(64, 158, 255));

                // Clipboard body
                FillRoundedRectangle(g, brush, 4, 6, 24, 22, 3);

                // Clipboard clip
                g.FillRectangle(brush, 10, 2, 12, 6);

                // Lines on clipboard
                using var whitePen = new Pen(Color.White, 2);
                g.DrawLine(whitePen, 8, 14, 24, 14);
                g.DrawLine(whitePen, 8, 19, 20, 19);
                g.DrawLine(whitePen, 8, 24, 16, 24);
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }

        private static void FillRoundedRectangle(Graphics g, Brush brush, int x, int y, int width, int height, int radius)
        {
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
            path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
            path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }

        private bool IsStartupEnabled()
        {
            var startupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "SmartClip.lnk");
            return File.Exists(startupPath);
        }

        private void ToggleStartup()
        {
            var startupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "SmartClip.lnk");

            if (File.Exists(startupPath))
            {
                try { File.Delete(startupPath); } catch { }
            }
            else
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    CreateShortcut(startupPath, exePath, "SmartClip Clipboard Manager");
                }
            }
        }

        /// <summary>
        /// Create shortcut using Shell32
        /// </summary>
        public static void CreateShortcut(string shortcutPath, string targetPath, string description)
        {
            var link = (IShellLink)new ShellLink();

            link.SetPath(targetPath);
            link.SetDescription(description);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");

            var file = (IPersistFile)link;
            file.Save(shortcutPath, false);
        }

        public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon?.ShowBalloonTip(3000, title, text, icon);
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }

        #region COM Interop for Shell Shortcut

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile);
            void IsDirty();
            void Load([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [In, MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        }

        #endregion
    }
}
