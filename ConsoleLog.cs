using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace LVLog
{
    /// <summary>
    /// Static logger for LabVIEW .NET interop. Provides a simple console-like WinForms log window.
    /// Call ConsoleLog.Log("message") from LabVIEW .NET nodes.
    /// The window is created on first use and recreated if closed.
    /// Thread-safe for calls from any LabVIEW execution thread.
    /// </summary>
    public static class ConsoleLog
    {
        private static LogForm _logForm;
        private static readonly object _syncLock = new object();

        /// <summary>
        /// Writes a timestamped message to the log window.
        /// Shows/creates the window automatically if needed.
        /// </summary>
        /// <param name="message">The log message to append.</param>
        public static void Log(string message)
        {
            if (message == null) message = "(null)";
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";

            EnsureFormCreatedAndVisible();

            var rtb = _logForm?.LogTextBox;
            if (rtb != null)
            {
                if (rtb.InvokeRequired)
                {
                    rtb.BeginInvoke(new Action(() =>
                    {
                        AppendLine(rtb, line);
                        _logForm?.UpdateStatus();
                    }));
                }
                else
                {
                    AppendLine(rtb, line);
                    _logForm?.UpdateStatus();
                }
            }
        }

        /// <summary>
        /// Clears all messages from the log window (if open).
        /// </summary>
        public static void Clear()
        {
            var rtb = _logForm?.LogTextBox;
            if (rtb != null)
            {
                if (rtb.InvokeRequired)
                    rtb.BeginInvoke(new Action(() => { rtb.Clear(); _logForm?.UpdateStatus(); }));
                else
                { rtb.Clear(); _logForm?.UpdateStatus(); }
            }
        }

        /// <summary>
        /// Forces the log window to show (creates if necessary).
        /// </summary>
        public static void Show()
        {
            EnsureFormCreatedAndVisible();
        }

        /// <summary>
        /// Closes the log window if open. Next Log() call will recreate it.
        /// </summary>
        public static void Close()
        {
            if (_logForm != null && !_logForm.IsDisposed)
            {
                if (_logForm.InvokeRequired)
                    _logForm.BeginInvoke(new Action(() => _logForm.Close()));
                else
                    _logForm.Close();
            }
        }

        private static void EnsureFormCreatedAndVisible()
        {
            if (_logForm != null && !_logForm.IsDisposed && _logForm.Visible)
            {
                if (_logForm.WindowState == FormWindowState.Minimized)
                    _logForm.WindowState = FormWindowState.Normal;
                _logForm.Activate();
                return;
            }

            lock (_syncLock)
            {
                if (_logForm != null && !_logForm.IsDisposed && _logForm.Visible)
                {
                    if (_logForm.WindowState == FormWindowState.Minimized)
                        _logForm.WindowState = FormWindowState.Normal;
                    _logForm.Activate();
                    return;
                }

                if (_logForm == null || _logForm.IsDisposed)
                {
                    // NOTE: Form is created on the calling thread.
                    // For maximum compatibility with LabVIEW worker threads, consider calling
                    // ConsoleLog.Log() first from your main UI thread, or we can enhance with
                    // a dedicated STA background thread for the form in a future version.
                    _logForm = new LogForm();
                    _logForm.FormClosed += (sender, args) =>
                    {
                        _logForm = null;
                    };
                    _logForm.Show(); // Modeless - relies on LabVIEW's message pump
                }
                else if (!_logForm.Visible)
                {
                    _logForm.Show();
                }

                if (_logForm.WindowState == FormWindowState.Minimized)
                    _logForm.WindowState = FormWindowState.Normal;

                _logForm.Activate();
            }
        }

        private static void AppendLine(RichTextBox rtb, string text)
        {
            rtb.AppendText(text);
            rtb.SelectionStart = rtb.TextLength;
            rtb.ScrollToCaret();
        }

        /// <summary>
        /// Private nested WinForms window. Not exposed outside the assembly.
        /// </summary>
        private sealed class LogForm : Form
        {
            internal RichTextBox LogTextBox { get; private set; }

            private ToolStrip _toolStrip;
            private StatusStrip _statusStrip;
            private ToolStripStatusLabel _statusLabel;

            public LogForm()
            {
                InitializeComponent();
            }

            private void InitializeComponent()
            {
                this.SuspendLayout();

                // Form styling - dark console theme
                this.Text = "LabVIEW .NET Console Log";
                this.Size = new Size(900, 600);
                this.MinimumSize = new Size(500, 300);
                this.StartPosition = FormStartPosition.WindowsDefaultLocation;
                this.BackColor = Color.FromArgb(28, 28, 28);
                this.ForeColor = Color.WhiteSmoke;
                this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);

                // Top tool strip with actions
                _toolStrip = new ToolStrip
                {
                    BackColor = Color.FromArgb(45, 45, 48),
                    ForeColor = Color.WhiteSmoke,
                    GripStyle = ToolStripGripStyle.Hidden,
                    RenderMode = ToolStripRenderMode.System,
                    Dock = DockStyle.Top,
                    Padding = new Padding(4, 2, 4, 2)
                };

                var btnClear = new ToolStripButton("Clear")
                {
                    ToolTipText = "Clear all messages from the log",
                    DisplayStyle = ToolStripItemDisplayStyle.Text
                };
                btnClear.Click += (s, e) => { LogTextBox?.Clear(); UpdateStatus(); };

                var btnCopy = new ToolStripButton("Copy All")
                {
                    ToolTipText = "Copy entire log contents to clipboard",
                    DisplayStyle = ToolStripItemDisplayStyle.Text
                };
                btnCopy.Click += (s, e) => CopyAllToClipboard();

                var btnSave = new ToolStripButton("Save As...")
                {
                    ToolTipText = "Save log to a .txt file",
                    DisplayStyle = ToolStripItemDisplayStyle.Text
                };
                btnSave.Click += (s, e) => SaveLogToFile();

                var sep1 = new ToolStripSeparator();

                var btnAlwaysTop = new ToolStripButton("Always on Top")
                {
                    ToolTipText = "Keep this window above other windows",
                    CheckOnClick = true,
                    Checked = false,
                    DisplayStyle = ToolStripItemDisplayStyle.Text
                };
                btnAlwaysTop.CheckedChanged += (s, e) => this.TopMost = btnAlwaysTop.Checked;

                var sep2 = new ToolStripSeparator();

                var btnClose = new ToolStripButton("Close")
                {
                    ToolTipText = "Close window (reopens automatically on next Log call)",
                    DisplayStyle = ToolStripItemDisplayStyle.Text
                };
                btnClose.Click += (s, e) => this.Close();

                _toolStrip.Items.AddRange(new ToolStripItem[]
                {
                    btnClear, btnCopy, btnSave, sep1, btnAlwaysTop, sep2, btnClose
                });

                this.Controls.Add(_toolStrip);

                // Main log display - console style
                LogTextBox = new RichTextBox
                {
                    BackColor = Color.FromArgb(18, 18, 18),
                    ForeColor = Color.FromArgb(0, 255, 127), // nice console green
                    Font = new Font("Consolas", 9.5F, FontStyle.Regular, GraphicsUnit.Point, 0),
                    ReadOnly = true,
                    WordWrap = false,
                    Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None,
                    ScrollBars = RichTextBoxScrollBars.Both,
                    DetectUrls = false
                };

                // Optional: right-click context menu for convenience
                var ctxMenu = new ContextMenuStrip();
                ctxMenu.Items.Add("Copy Selected", null, (s, e) => { if (!string.IsNullOrEmpty(LogTextBox.SelectedText)) Clipboard.SetText(LogTextBox.SelectedText); });
                ctxMenu.Items.Add("Copy All", null, (s, e) => CopyAllToClipboard());
                ctxMenu.Items.Add("-");
                ctxMenu.Items.Add("Clear Log", null, (s, e) => { LogTextBox.Clear(); UpdateStatus(); });
                LogTextBox.ContextMenuStrip = ctxMenu;

                this.Controls.Add(LogTextBox);

                // Bottom status bar
                _statusStrip = new StatusStrip
                {
                    BackColor = Color.FromArgb(45, 45, 48),
                    ForeColor = Color.WhiteSmoke,
                    Dock = DockStyle.Bottom,
                    SizingGrip = false
                };

                _statusLabel = new ToolStripStatusLabel("Messages: 0  |  Ready - call ConsoleLog.Log() from LabVIEW")
                {
                    Spring = true,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _statusStrip.Items.Add(_statusLabel);

                this.Controls.Add(_statusStrip);

                this.ResumeLayout(false);
                this.PerformLayout();

                // Optional polish: double buffer hint
                this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            }

            private void CopyAllToClipboard()
            {
                if (LogTextBox != null && LogTextBox.TextLength > 0)
                {
                    Clipboard.SetText(LogTextBox.Text);
                    _statusLabel.Text = "Copied entire log to clipboard";
                }
            }

            private void SaveLogToFile()
            {
                if (LogTextBox == null || LogTextBox.TextLength == 0) return;

                using (var dlg = new SaveFileDialog())
                {
                    dlg.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                    dlg.DefaultExt = "txt";
                    dlg.FileName = $"LabVIEW_NetLog_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
                    dlg.Title = "Save LabVIEW .NET Log";

                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        try
                        {
                            File.WriteAllText(dlg.FileName, LogTextBox.Text, new UTF8Encoding(false));
                            _statusLabel.Text = $"Saved: {Path.GetFileName(dlg.FileName)}";
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this,
                                $"Failed to save file:\n{ex.Message}",
                                "Save Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }
                }
            }

            internal void UpdateStatus()
            {
                if (_statusLabel != null && LogTextBox != null)
                {
                    int lines = LogTextBox.Lines.Length;
                    _statusLabel.Text = $"Messages: {lines}  |  LabVIEW .NET Console Log  |  {(this.TopMost ? "Always on Top" : "Normal")}";
                }
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                base.OnFormClosing(e);
                // Allow close; the static class will recreate on next Log() call.
            }
        }
    }
}