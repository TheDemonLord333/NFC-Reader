using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using NFC_Reader.Core;
using Application = System.Windows.Application;

namespace NFC_Reader.UI
{
    /// <summary>
    /// Verwaltet das System Tray Icon und dessen Funktionalität
    /// </summary>
    public class SystemTrayManager : IDisposable
    {
        #region Private Fields
        private readonly ILogger<SystemTrayManager>? _logger;
        private NotifyIcon? _notifyIcon;
        private ContextMenuStrip? _contextMenu;
        private readonly Window _mainWindow;
        private bool _disposed = false;
        #endregion

        #region Events
        public event EventHandler? ShowWindowRequested;
        public event EventHandler? HideWindowRequested;
        public event EventHandler? ExitApplicationRequested;
        public event EventHandler<string>? NotificationClicked;
        #endregion

        #region Properties
        public bool IsVisible => _notifyIcon?.Visible ?? false;
        public string StatusText { get; private set; } = "NFC TextScanner";
        #endregion

        #region Constructor
        public SystemTrayManager(Window mainWindow, ILogger<SystemTrayManager>? logger = null)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _logger = logger;

            InitializeTrayIcon();
            SetupEventHandlers();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Zeigt das System Tray Icon an
        /// </summary>
        public void Show()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = true;
                _logger?.LogDebug("System Tray Icon angezeigt");
            }
        }

        /// <summary>
        /// Versteckt das System Tray Icon
        /// </summary>
        public void Hide()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _logger?.LogDebug("System Tray Icon versteckt");
            }
        }

        /// <summary>
        /// Aktualisiert den Status-Text des Tray Icons
        /// </summary>
        public void UpdateStatus(string status, NFCScannerStatus scannerStatus = NFCScannerStatus.Stopped)
        {
            if (_notifyIcon == null) return;

            StatusText = status;
            _notifyIcon.Text = $"NFC TextScanner - {status}";

            // Icon basierend auf Status ändern
            UpdateIconForStatus(scannerStatus);

            _logger?.LogDebug($"System Tray Status aktualisiert: {status}");
        }

        /// <summary>
        /// Zeigt eine Ballon-Benachrichtigung
        /// </summary>
        public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeout = 3000)
        {
            if (_notifyIcon?.Visible == true)
            {
                _notifyIcon.ShowBalloonTip(timeout, title, text, icon);
                _logger?.LogDebug($"Ballon-Tip angezeigt: {title} - {text}");
            }
        }

        /// <summary>
        /// Zeigt eine Erfolgsmeldung
        /// </summary>
        public void ShowSuccess(string message)
        {
            ShowBalloonTip("✅ Erfolg", message, ToolTipIcon.Info);
        }

        /// <summary>
        /// Zeigt eine Warnung
        /// </summary>
        public void ShowWarning(string message)
        {
            ShowBalloonTip("⚠️ Warnung", message, ToolTipIcon.Warning);
        }

        /// <summary>
        /// Zeigt einen Fehler
        /// </summary>
        public void ShowError(string message)
        {
            ShowBalloonTip("❌ Fehler", message, ToolTipIcon.Error);
        }

        /// <summary>
        /// Zeigt eine NFC-Karten-Benachrichtigung
        /// </summary>
        public void ShowCardDetected(NFCCard card)
        {
            var message = $"Text eingefügt: {card.Text?.Substring(0, Math.Min(card.Text.Length, 50))}...";
            ShowBalloonTip("📡 NFC-Karte erkannt", message, ToolTipIcon.Info);
        }

        /// <summary>
        /// Blinkt das Tray Icon für Aufmerksamkeit
        /// </summary>
        public async void BlinkIcon(int times = 3)
        {
            if (_notifyIcon == null) return;

            var originalIcon = _notifyIcon.Icon;
            var blinkIcon = CreateBlinkIcon();

            for (int i = 0; i < times; i++)
            {
                _notifyIcon.Icon = blinkIcon;
                await System.Threading.Tasks.Task.Delay(300);
                _notifyIcon.Icon = originalIcon;
                await System.Threading.Tasks.Task.Delay(300);
            }
        }
        #endregion

        #region Private Methods
        private void InitializeTrayIcon()
        {
            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Icon = CreateDefaultIcon(),
                    Text = StatusText,
                    Visible = false
                };

                CreateContextMenu();
                _notifyIcon.ContextMenuStrip = _contextMenu;

                _logger?.LogDebug("System Tray Icon initialisiert");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Initialisieren des System Tray Icons");
                throw;
            }
        }

        private void CreateContextMenu()
        {
            _contextMenu = new ContextMenuStrip();

            // Styling für Discord-ähnliches Aussehen
            _contextMenu.BackColor = Color.FromArgb(54, 57, 63);
            _contextMenu.ForeColor = Color.White;
            _contextMenu.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // Menü-Einträge
            var openItem = new ToolStripMenuItem("🔓 Öffnen")
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            openItem.Click += (s, e) => OnShowWindowRequested();

            var settingsItem = new ToolStripMenuItem("⚙️ Einstellungen");
            settingsItem.Click += (s, e) => ShowSettings();

            var aboutItem = new ToolStripMenuItem("ℹ️ Info");
            aboutItem.Click += (s, e) => ShowAbout();

            var separator = new ToolStripSeparator();

            var exitItem = new ToolStripMenuItem("❌ Beenden")
            {
                ForeColor = Color.FromArgb(237, 66, 69) // Discord Red
            };
            exitItem.Click += (s, e) => OnExitApplicationRequested();

            // Menü zusammenstellen
            _contextMenu.Items.AddRange(new ToolStripItem[]
            {
                openItem,
                separator,
                settingsItem,
                aboutItem,
                new ToolStripSeparator(),
                exitItem
            });
        }

        private void SetupEventHandlers()
        {
            if (_notifyIcon == null) return;

            // Doppelklick zum Öffnen
            _notifyIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    OnShowWindowRequested();
                }
            };

            // Ballon-Tip Klick
            _notifyIcon.BalloonTipClicked += (s, e) =>
            {
                NotificationClicked?.Invoke(this, StatusText);
            };

            // Mausklick für Feedback
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    // Kurzes visuelles Feedback
                    BlinkIcon(1);
                }
            };
        }

        private Icon CreateDefaultIcon()
        {
            try
            {
                // Versuche Icon aus Ressourcen zu laden
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Icons", "nfc_icon.ico");
                if (File.Exists(iconPath))
                {
                    return new Icon(iconPath);
                }

                // Fallback: Programmatisch erstelltes Icon
                return CreateProgrammaticIcon(Color.FromArgb(88, 101, 242), Color.FromArgb(87, 242, 135));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Fehler beim Laden des Standard-Icons, verwende Fallback");
                return CreateProgrammaticIcon(Color.Blue, Color.Green);
            }
        }

        private Icon CreateProgrammaticIcon(Color primaryColor, Color accentColor)
        {
            var bitmap = new Bitmap(16, 16);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);

                // Hintergrund-Kreis (Discord Blau)
                graphics.FillEllipse(new SolidBrush(primaryColor), 1, 1, 14, 14);

                // NFC-Symbol (vereinfacht)
                using (var pen = new Pen(accentColor, 2))
                {
                    // NFC-Wellen
                    graphics.DrawArc(pen, 4, 4, 8, 8, 0, 180);
                    graphics.DrawArc(pen, 6, 6, 4, 4, 0, 180);
                }

                // Zentraler Punkt
                graphics.FillEllipse(new SolidBrush(accentColor), 7, 9, 2, 2);
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }

        private Icon CreateBlinkIcon()
        {
            return CreateProgrammaticIcon(Color.FromArgb(255, 255, 0), Color.FromArgb(255, 0, 0));
        }

        private void UpdateIconForStatus(NFCScannerStatus status)
        {
            if (_notifyIcon == null) return;

            try
            {
                Color primaryColor, accentColor;

                switch (status)
                {
                    case NFCScannerStatus.Scanning:
                        primaryColor = Color.FromArgb(88, 101, 242); // Discord Blau
                        accentColor = Color.FromArgb(87, 242, 135);  // Discord Grün
                        break;
                    case NFCScannerStatus.CardDetected:
                        primaryColor = Color.FromArgb(87, 242, 135); // Discord Grün
                        accentColor = Color.FromArgb(255, 255, 255); // Weiß
                        break;
                    case NFCScannerStatus.Error:
                        primaryColor = Color.FromArgb(237, 66, 69);  // Discord Rot
                        accentColor = Color.FromArgb(255, 255, 255); // Weiß
                        break;
                    default:
                        primaryColor = Color.FromArgb(114, 118, 125); // Discord Grau
                        accentColor = Color.FromArgb(185, 187, 190);  // Hellgrau
                        break;
                }

                _notifyIcon.Icon = CreateProgrammaticIcon(primaryColor, accentColor);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Aktualisieren des Icons");
            }
        }

        private void ShowSettings()
        {
            try
            {
                var settingsWindow = new SettingsWindow();
                settingsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Öffnen der Einstellungen");
                ShowError("Einstellungen konnten nicht geöffnet werden");
            }
        }

        private void ShowAbout()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var about = $"NFC TextScanner Pro\n" +
                       $"Version: {version}\n" +
                       $"© 2025 NFC Solutions\n\n" +
                       $"Automatische Text-Einfügung mit NFC-Karten\n" +
                       $"Unterstützt MIFARE, NTAG und ISO14443";

            System.Windows.MessageBox.Show(about, "Über NFC TextScanner",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnShowWindowRequested()
        {
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnHideWindowRequested()
        {
            HideWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnExitApplicationRequested()
        {
            ExitApplicationRequested?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _notifyIcon?.Dispose();
                _contextMenu?.Dispose();
                _disposed = true;

                _logger?.LogDebug("SystemTrayManager disposed");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Dispose des SystemTrayManagers");
            }
        }
        #endregion
    }

    /// <summary>
    /// Einfaches Einstellungsfenster (Platzhalter)
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Title = "Einstellungen";
            Width = 400;
            Height = 300;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(47, 49, 54));

            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = "Einstellungen werden in einer zukünftigen Version implementiert.",
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new Thickness(20)
            };

            Content = textBlock;
        }
    }
}