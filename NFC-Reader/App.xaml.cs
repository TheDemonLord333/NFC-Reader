using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NFC_Reader.Core;
using NFC_Reader.Services;
using NFC_Reader.UI;

namespace NFC_Reader
{
    /// <summary>
    /// Hauptanwendungsklasse mit Dependency Injection Container
    /// </summary>
    public partial class App : System.Windows.Application
    {
        #region Private Fields
        private ServiceProvider? _serviceProvider;
        private ILogger<App>? _logger;
        #endregion

        #region Application Lifecycle
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                // Prüfe auf bereits laufende Instanz
                if (IsAnotherInstanceRunning())
                {
                    System.Windows.MessageBox.Show(
                        "NFC TextScanner läuft bereits!\n\nPrüfen Sie das System Tray (^) in der Taskleiste.",
                        "Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    Shutdown();
                    return;
                }

                // Services konfigurieren
                ConfigureServices();

                _logger = _serviceProvider?.GetRequiredService<ILogger<App>>();
                _logger?.LogInformation("=== NFC TextScanner Pro gestartet ===");

                // Global Exception Handler
                SetupExceptionHandling();

                // Hauptfenster starten über DI Container
                var mainWindow = _serviceProvider?.GetRequiredService<MainWindow>();
                if (mainWindow != null)
                {
                    MainWindow = mainWindow;

                    _logger?.LogInformation("Hauptfenster wird angezeigt");
                    mainWindow.Show();
                }
                else
                {
                    throw new InvalidOperationException("MainWindow konnte nicht erstellt werden");
                }

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                HandleStartupException(ex);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _logger?.LogInformation("=== NFC TextScanner Pro wird beendet ===");

                // Services aufräumen
                _serviceProvider?.Dispose();

                base.OnExit(e);
            }
            catch (Exception ex)
            {
                // Logging könnte bereits disposed sein
                System.Diagnostics.Debug.WriteLine($"Fehler beim Beenden: {ex}");
            }
        }
        #endregion

        #region Service Configuration
        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            // Logging konfigurieren
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                // File Logging (einfache Implementierung)
                builder.AddProvider(new FileLoggerProvider());
            });

            // Configuration Services
            services.AddSingleton<ConfigurationService>();
            services.AddSingleton<NotificationService>();
            services.AddSingleton<LoggingService>();

            // Core Services
            services.AddSingleton<NFCScanner>();
            services.AddSingleton<TextInjector>();

            // UI Services
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            // Konfiguration initialisieren
            var configService = _serviceProvider.GetRequiredService<ConfigurationService>();

            // TextInjector konfigurieren
            var textInjector = _serviceProvider.GetRequiredService<TextInjector>();
            textInjector.Method = Enum.Parse<TextInjectionMethod>(
                configService.GetValue<string>("TextInjection:Method", "Smart"));
            textInjector.DelayBetweenChars = configService.GetValue<int>("TextInjection:DelayBetweenChars", 10);
            textInjector.DelayBeforeInjection = configService.GetValue<int>("TextInjection:DelayBeforeInjection", 100);
            textInjector.PreserveClipboard = configService.GetValue<bool>("TextInjection:PreserveClipboard", true);
        }
        #endregion

        #region Exception Handling
        private void SetupExceptionHandling()
        {
            // Unhandled Exceptions in UI Thread
            DispatcherUnhandledException += (sender, e) =>
            {
                _logger?.LogError(e.Exception, "Unbehandelte Exception im UI Thread");

                var result = System.Windows.MessageBox.Show(
                    $"Ein unerwarteter Fehler ist aufgetreten:\n\n{e.Exception.Message}\n\nMöchten Sie die Anwendung dennoch weiterlaufen lassen?",
                    "Unerwarteter Fehler",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                e.Handled = (result == MessageBoxResult.Yes);

                if (!e.Handled)
                {
                    _logger?.LogCritical("Anwendung wird aufgrund unbehandelter Exception beendet");
                }
            };

            // Unhandled Exceptions in Background Threads
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var exception = e.ExceptionObject as Exception;
                _logger?.LogCritical(exception, "Unbehandelte Exception in Background Thread");

                if (e.IsTerminating)
                {
                    _logger?.LogCritical("Anwendung wird beendet (IsTerminating=true)");

                    System.Windows.MessageBox.Show(
                        $"Ein kritischer Fehler ist aufgetreten:\n\n{exception?.Message}\n\nDie Anwendung wird beendet.",
                        "Kritischer Fehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Stop);
                }
            };

            // Task Scheduler Exceptions
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                _logger?.LogError(e.Exception, "Unbeobachtete Task Exception");
                e.SetObserved(); // Verhindert App-Crash
            };
        }

        private void HandleStartupException(Exception ex)
        {
            var errorMessage = $"Fehler beim Starten der Anwendung:\n\n{ex.Message}";

            if (ex.InnerException != null)
            {
                errorMessage += $"\n\nDetails: {ex.InnerException.Message}";
            }

            // Detaillierte Fehlermeldung für häufige Probleme
            if (ex.Message.Contains("PCSC") || ex.Message.Contains("Smart Card"))
            {
                errorMessage += "\n\n💡 Lösungsvorschläge:\n" +
                               "• Stellen Sie sicher, dass ein NFC-Reader angeschlossen ist\n" +
                               "• Starten Sie den Smart Card Service:\n" +
                               "  sc start SCardSvr\n" +
                               "• Führen Sie die Anwendung als Administrator aus";
            }

            System.Windows.MessageBox.Show(
                errorMessage,
                "Startfehler - NFC TextScanner",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Environment.Exit(1);
        }
        #endregion

        #region Instance Management
        private bool IsAnotherInstanceRunning()
        {
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var processes = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);

                // Prüfe ob mehr als eine Instanz läuft
                if (processes.Length > 1)
                {
                    // Versuche das andere Fenster in den Vordergrund zu bringen
                    foreach (var process in processes)
                    {
                        if (process.Id != currentProcess.Id && process.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(process.MainWindowHandle, 9); // SW_RESTORE
                            SetForegroundWindow(process.MainWindowHandle);
                            break;
                        }
                    }
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                // Im Zweifel erlauben wir den Start
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        #endregion
    }

    /// <summary>
    /// Einfacher File Logger Provider
    /// </summary>
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _logPath;

        public FileLoggerProvider()
        {
            var logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            System.IO.Directory.CreateDirectory(logDir);
            _logPath = System.IO.Path.Combine(logDir, $"nfc_scanner_{DateTime.Now:yyyy-MM-dd}.log");
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(categoryName, _logPath);
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Einfacher File Logger
    /// </summary>
    public class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _logPath;
        private readonly object _lock = new object();

        public FileLogger(string categoryName, string logPath)
        {
            _categoryName = categoryName;
            _logPath = logPath;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            try
            {
                lock (_lock)
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var level = logLevel.ToString().ToUpper().PadLeft(11);
                    var category = _categoryName.Length > 30 ? _categoryName.Substring(_categoryName.Length - 30) : _categoryName;
                    var message = formatter(state, exception);

                    var logLine = $"[{timestamp}] {level} {category.PadRight(30)} | {message}";

                    if (exception != null)
                    {
                        logLine += $"\n    Exception: {exception}";
                    }

                    System.IO.File.AppendAllText(_logPath, logLine + Environment.NewLine);
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }
}