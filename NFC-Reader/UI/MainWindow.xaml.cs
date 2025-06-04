using System;
using System.ComponentModel;
using System.Data;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NFC_Reader.Core;
using NFC_Reader.Services;

namespace NFC_Reader.UI
{
    /// <summary>
    /// Hauptfenster der NFC TextScanner Anwendung
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private Fields
        private readonly ILogger<MainWindow>? _logger;
        private readonly NFCScanner _nfcScanner;
        private readonly TextInjector _textInjector;
        private readonly SystemTrayManager _systemTrayManager;
        private readonly NotificationService _notificationService;
        private readonly ConfigurationService _configurationService;
        private readonly LoggingService _loggingService;

        private bool _isExiting = false;
        private DispatcherTimer? _statusAnimationTimer;
        private DispatcherTimer? _uiUpdateTimer;
        private int _animationStep = 0;
        private int _cardCount = 0;
        #endregion

        #region Constructor
        public MainWindow(
            ILogger<MainWindow>? logger,
            NFCScanner nfcScanner,
            TextInjector textInjector,
            NotificationService notificationService,
            ConfigurationService configurationService,
            LoggingService loggingService)
        {
            _logger = logger;
            _nfcScanner = nfcScanner ?? throw new ArgumentNullException(nameof(nfcScanner));
            _textInjector = textInjector ?? throw new ArgumentNullException(nameof(textInjector));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            InitializeComponent();

            _systemTrayManager = new SystemTrayManager(this, logger);

            InitializeEventHandlers();
            InitializeTimers();

            _logger?.LogInformation("MainWindow initialisiert");
        }
        #endregion

        #region Initialization
        private void InitializeEventHandlers()
        {
            // NFC Scanner Events
            _nfcScanner.CardDetected += OnCardDetected;
            _nfcScanner.CardRemoved += OnCardRemoved;
            _nfcScanner.StatusChanged += OnScannerStatusChanged;
            _nfcScanner.ErrorOccurred += OnScannerErrorOccurred;

            // System Tray Events
            _systemTrayManager.ShowWindowRequested += OnShowWindowRequested;
            _systemTrayManager.ExitApplicationRequested += OnExitApplicationRequested;
            _systemTrayManager.NotificationClicked += OnNotificationClicked;

            // Window Events
            Loaded += OnWindowLoaded;
            Closing += OnWindowClosing;
            StateChanged += OnWindowStateChanged;

            _logger?.LogDebug("Event Handler initialisiert");
        }

        private void InitializeTimers()
        {
            // Status Animation Timer
            _statusAnimationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _statusAnimationTimer.Tick += OnStatusAnimationTick;

            // UI Update Timer
            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _uiUpdateTimer.Tick += OnUIUpdateTick;

            _logger?.LogDebug("Timer initialisiert");
        }
        #endregion

        #region Window Events
        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger?.LogInformation("Fenster geladen, starte NFC-Scanner...");

                // UI initialisieren
                UpdateUI(NFCScannerStatus.Stopped);
                _systemTrayManager.Show();

                // Scanner starten
                var success = await _nfcScanner.StartScanningAsync();

                if (success)
                {
                    _statusAnimationTimer?.Start();
                    _uiUpdateTimer?.Start();

                    _systemTrayManager.UpdateStatus("Aktiv", NFCScannerStatus.Scanning);
                    _systemTrayManager.ShowSuccess("NFC-Scanner erfolgreich gestartet");

                    // Reader-Info aktualisieren
                    if (_nfcScanner.AvailableReaders.Length > 0)
                    {
                        ReaderInfo.Text = $"Reader: {_nfcScanner.CurrentReader}";
                        ConnectionStatus.Text = "Verbunden";
                        ConnectionIndicator.Fill = (System.Windows.Media.Brush)FindResource("DiscordGreen");
                    }

                    _logger?.LogInformation("NFC-Scanner erfolgreich gestartet");
                }
                else
                {
                    _systemTrayManager.UpdateStatus("Fehler", NFCScannerStatus.Error);
                    _systemTrayManager.ShowError("NFC-Scanner konnte nicht gestartet werden");

                    ReaderInfo.Text = "Reader: Nicht gefunden";
                    ConnectionStatus.Text = "Fehler";
                    ConnectionIndicator.Fill = (System.Windows.Media.Brush)FindResource("DiscordRed");

                    _logger?.LogError("NFC-Scanner konnte nicht gestartet werden");
                }

                // Auto-Minimieren wenn konfiguriert
                if (_configurationService.GetValue<bool>("NFCScanner:AutoMinimize", true))
                {
                    await Task.Delay(2000); // 2 Sekunden anzeigen
                    MinimizeToTray();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Laden des Fensters");
                _systemTrayManager.ShowError($"Startfehler: {ex.Message}");
            }
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                MinimizeToTray();
                _logger?.LogDebug("Fenster in System Tray minimiert");
            }
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                MinimizeToTray();
            }
        }
        #endregion

        #region NFC Scanner Events
        private async void OnCardDetected(object sender, NFCCardDetectedEventArgs e)
        {
            try
            {
                _logger?.LogInformation($"NFC-Karte erkannt: {e.Card.Text}");

                // UI Update
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "🔄 Karte erkannt...";
                    DetailText.Text = $"Text: {e.Card.Text?.Substring(0, Math.Min(e.Card.Text.Length, 30))}...";
                    _cardCount++;
                    CardCount.Text = $"Karten: {_cardCount}";
                });

                // Logging
                _loggingService.LogCardDetection(e.Card);

                // Text einfügen
                var success = await _textInjector.InjectTextAsync(e.Card.Text);

                if (success)
                {
                    _systemTrayManager.ShowCardDetected(e.Card);
                    _notificationService.PlayNotificationSound();
                    _loggingService.LogTextInjection(e.Card.Text, "Active Application", true);

                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = "✅ Text eingefügt";
                        DetailText.Text = "Erfolgreich eingefügt!";
                    });

                    _logger?.LogInformation("Text erfolgreich eingefügt");
                }
                else
                {
                    _systemTrayManager.ShowError("Text-Einfügung fehlgeschlagen");
                    _notificationService.PlayErrorSound();
                    _loggingService.LogTextInjection(e.Card.Text, "Active Application", false);

                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = "❌ Einfügung fehlgeschlagen";
                        DetailText.Text = "Fehler beim Einfügen des Texts";
                    });

                    _logger?.LogWarning("Text-Einfügung fehlgeschlagen");
                }

                // Status nach Delay zurücksetzen
                await Task.Delay(3000);
                Dispatcher.Invoke(() => UpdateUI(NFCScannerStatus.Scanning));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler bei der Kartenverarbeitung");
                _systemTrayManager.ShowError($"Verarbeitungsfehler: {ex.Message}");
            }
        }

        private void OnCardRemoved(object sender, NFCCardRemovedEventArgs e)
        {
            _logger?.LogDebug($"Karte entfernt von Reader: {e.ReaderName}");

            Dispatcher.Invoke(() =>
            {
                if (StatusText.Text.Contains("Karte erkannt") || StatusText.Text.Contains("eingefügt"))
                {
                    UpdateUI(NFCScannerStatus.Scanning);
                }
            });
        }

        private void OnScannerStatusChanged(object sender, NFCScannerStatusEventArgs e)
        {
            _logger?.LogDebug($"Scanner Status geändert: {e.Status}");

            Dispatcher.Invoke(() =>
            {
                UpdateUI(e.Status);
                _systemTrayManager.UpdateStatus(GetStatusText(e.Status), e.Status);
            });
        }

        private void OnScannerErrorOccurred(object sender, NFCErrorEventArgs e)
        {
            _logger?.LogError(e.Exception, "NFC-Scanner Fehler aufgetreten");

            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "❌ Scanner-Fehler";
                DetailText.Text = e.Exception.Message;
                UpdateUI(NFCScannerStatus.Error);

                ConnectionStatus.Text = "Fehler";
                ConnectionIndicator.Fill = (System.Windows.Media.Brush)FindResource("DiscordRed");
            });

            _systemTrayManager.ShowError($"Scanner-Fehler: {e.Exception.Message}");
            _notificationService.PlayErrorSound();
        }
        #endregion

        #region System Tray Events
        private void OnShowWindowRequested(object sender, EventArgs e)
        {
            ShowWindow();
        }

        private void OnExitApplicationRequested(object sender, EventArgs e)
        {
            ExitApplication();
        }

        private void OnNotificationClicked(object sender, string e)
        {
            ShowWindow();
        }
        #endregion

        #region Timer Events
        private void OnStatusAnimationTick(object sender, EventArgs e)
        {
            if (_nfcScanner.Status == NFCScannerStatus.Scanning)
            {
                _animationStep = (_animationStep + 1) % 4;
                var dots = new string('.', _animationStep);
                DetailText.Text = $"Bereit für NFC-Karten{dots}";
            }
        }

        private void OnUIUpdateTick(object sender, EventArgs e)
        {
            // Regelmäßige UI-Updates (z.B. Zeitanzeigen, Status-Validierung)
            if (_nfcScanner.Status == NFCScannerStatus.Scanning)
            {
                // Prüfe ob Scanner noch aktiv ist
                if (!_nfcScanner.IsScanning)
                {
                    UpdateUI(NFCScannerStatus.Error);
                    _systemTrayManager.ShowWarning("Scanner-Verbindung unterbrochen");
                }
            }
        }
        #endregion

        #region Button Event Handlers
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            MinimizeToTray();
        }

        private void ExitBtn_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }
        #endregion

        #region Private Methods
        private void UpdateUI(NFCScannerStatus status)
        {
            switch (status)
            {
                case NFCScannerStatus.Scanning:
                    StatusText.Text = "📡 Textscanner aktiv";
                    DetailText.Text = "Bereit für NFC-Karten...";
                    MinimizeBtn.IsEnabled = true;
                    StatusIndicator.Fill = (System.Windows.Media.Brush)FindResource("DiscordGreen");
                    break;

                case NFCScannerStatus.CardDetected:
                    StatusText.Text = "🔄 Karte erkannt...";
                    DetailText.Text = "Verarbeite Kartendaten...";
                    StatusIndicator.Fill = (System.Windows.Media.Brush)FindResource("DiscordBlue");
                    break;

                case NFCScannerStatus.Error:
                    StatusText.Text = "❌ Scanner-Fehler";
                    DetailText.Text = "Überprüfen Sie die Hardware";
                    StatusIndicator.Fill = (System.Windows.Media.Brush)FindResource("DiscordRed");
                    break;

                case NFCScannerStatus.Stopped:
                default:
                    StatusText.Text = "⏸️ Scanner gestoppt";
                    DetailText.Text = "Scanner ist nicht aktiv";
                    MinimizeBtn.IsEnabled = false;
                    StatusIndicator.Fill = (System.Windows.Media.Brush)FindResource("DiscordTextDark");
                    break;
            }
        }

        private string GetStatusText(NFCScannerStatus status)
        {
            return status switch
            {
                NFCScannerStatus.Scanning => "Aktiv",
                NFCScannerStatus.CardDetected => "Karte erkannt",
                NFCScannerStatus.Error => "Fehler",
                NFCScannerStatus.Stopped => "Gestoppt",
                _ => "Unbekannt"
            };
        }

        private void ShowWindow()
        {
            try
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();

                _logger?.LogDebug("Fenster angezeigt");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Anzeigen des Fensters");
            }
        }

        private void MinimizeToTray()
        {
            try
            {
                Hide();

                if (_configurationService.GetValue<bool>("NFCScanner:ShowMinimizeNotification", true))
                {
                    _systemTrayManager.ShowBalloonTip(
                        "NFC TextScanner",
                        "Anwendung läuft im Hintergrund weiter",
                        System.Windows.Forms.ToolTipIcon.Info,
                        2000);
                }

                _logger?.LogDebug("Fenster in System Tray minimiert");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Minimieren in System Tray");
            }
        }

        private async void ExitApplication()
        {
            try
            {
                _isExiting = true;

                _logger?.LogInformation("Anwendung wird beendet...");

                // Timer stoppen
                _statusAnimationTimer?.Stop();
                _uiUpdateTimer?.Stop();

                // Scanner stoppen
                _nfcScanner?.StopScanning();

                // System Tray aufräumen
                _systemTrayManager?.Hide();
                _systemTrayManager?.Dispose();

                // Alte Logs aufräumen
                _loggingService?.CleanupOldLogs();

                // Services aufräumen
                _nfcScanner?.Dispose();

                await Task.Delay(500); // Kurz warten für cleanup

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Beenden der Anwendung");
                Application.Current.Shutdown();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Verstecke Taskleisten-Button wenn minimiert (Discord-Verhalten)
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                HideFromTaskbar(helper.Handle);
            }
        }

        private void HideFromTaskbar(IntPtr handle)
        {
            try
            {
                const int GWL_EXSTYLE = -20;
                const int WS_EX_TOOLWINDOW = 0x00000080;

                var style = GetWindowLong(handle, GWL_EXSTYLE);
                SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Konnte Taskleisten-Button nicht verstecken");
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        #endregion
    }
}