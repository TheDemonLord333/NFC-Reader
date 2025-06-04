using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NFC_Reader.Services
{
    /// <summary>
    /// Service für Anwendungskonfiguration
    /// </summary>
    public class ConfigurationService
    {
        #region Private Fields
        private readonly ILogger<ConfigurationService>? _logger;
        private readonly IConfiguration _configuration;
        private readonly string _configFilePath;
        private JObject _configObject;
        #endregion

        #region Constructor
        public ConfigurationService(ILogger<ConfigurationService>? logger = null)
        {
            _logger = logger;
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

            LoadConfiguration();
            InitializeDefaultSettings();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Lädt einen Konfigurationswert
        /// </summary>
        public T GetValue<T>(string key, T defaultValue = default(T)!)
        {
            try
            {
                var value = _configuration[key];
                if (value != null)
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"Fehler beim Laden des Konfigurationswerts '{key}', verwende Standardwert");
                return defaultValue;
            }
        }

        /// <summary>
        /// Setzt einen Konfigurationswert
        /// </summary>
        public bool SetValue<T>(string key, T value)
        {
            try
            {
                var keyParts = key.Split(':');
                var current = _configObject;

                for (int i = 0; i < keyParts.Length - 1; i++)
                {
                    if (current[keyParts[i]] == null)
                    {
                        current[keyParts[i]] = new JObject();
                    }
                    current = (JObject)current[keyParts[i]]!;
                }

                current[keyParts[keyParts.Length - 1]] = JToken.FromObject(value!);

                SaveConfiguration();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Fehler beim Setzen des Konfigurationswerts '{key}'");
                return false;
            }
        }

        /// <summary>
        /// Speichert die Konfiguration
        /// </summary>
        public bool SaveConfiguration()
        {
            try
            {
                var json = _configObject.ToString(Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                _logger?.LogDebug("Konfiguration gespeichert");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Speichern der Konfiguration");
                return false;
            }
        }

        /// <summary>
        /// Lädt die Konfiguration neu
        /// </summary>
        public void ReloadConfiguration()
        {
            LoadConfiguration();
        }
        #endregion

        #region Private Methods
        private void LoadConfiguration()
        {
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

                _configuration = builder.Build();

                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    _configObject = JObject.Parse(json);
                }
                else
                {
                    _configObject = new JObject();
                }

                _logger?.LogDebug("Konfiguration geladen");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Laden der Konfiguration");
                _configObject = new JObject();
            }
        }

        private void InitializeDefaultSettings()
        {
            var defaults = new
            {
                NFCScanner = new
                {
                    ScanInterval = 500,
                    AutoMinimize = true,
                    ShowMinimizeNotification = true,
                    PlaySounds = true,
                    LogLevel = "Information"
                },
                TextInjection = new
                {
                    Method = "Clipboard",
                    DelayBetweenChars = 10,
                    DelayBeforeInjection = 100,
                    PreserveClipboard = true
                },
                UI = new
                {
                    Theme = "Dark",
                    ShowAnimations = true,
                    AutoStart = false
                }
            };

            foreach (var prop in defaults.GetType().GetProperties())
            {
                var sectionName = prop.Name;
                if (_configObject[sectionName] == null)
                {
                    _configObject[sectionName] = JToken.FromObject(prop.GetValue(defaults)!);
                }
            }

            SaveConfiguration();
        }
        #endregion
    }

    /// <summary>
    /// Service für Benachrichtigungen und Sound
    /// </summary>
    public class NotificationService
    {
        #region Private Fields
        private readonly ILogger<NotificationService>? _logger;
        private readonly ConfigurationService _configurationService;
        #endregion

        #region Constructor
        public NotificationService(ConfigurationService configurationService, ILogger<NotificationService>? logger = null)
        {
            _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            _logger = logger;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Spielt einen Benachrichtigungssound ab
        /// </summary>
        public void PlayNotificationSound()
        {
            if (!_configurationService.GetValue<bool>("NFCScanner:PlaySounds", true))
                return;

            try
            {
                // Versuche benutzerdefinierten Sound zu laden
                var soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Sounds", "notification.wav");

                if (File.Exists(soundPath))
                {
                    PlayWaveFile(soundPath);
                }
                else
                {
                    // Fallback: Windows System-Sound
                    System.Media.SystemSounds.Beep.Play();
                }

                _logger?.LogDebug("Benachrichtigungssound abgespielt");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Fehler beim Abspielen des Benachrichtigungssounds");
            }
        }

        /// <summary>
        /// Spielt einen Erfolgs-Sound ab
        /// </summary>
        public void PlaySuccessSound()
        {
            if (!_configurationService.GetValue<bool>("NFCScanner:PlaySounds", true))
                return;

            try
            {
                var soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Sounds", "success.wav");

                if (File.Exists(soundPath))
                {
                    PlayWaveFile(soundPath);
                }
                else
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Fehler beim Abspielen des Erfolgs-Sounds");
            }
        }

        /// <summary>
        /// Spielt einen Fehler-Sound ab
        /// </summary>
        public void PlayErrorSound()
        {
            if (!_configurationService.GetValue<bool>("NFCScanner:PlaySounds", true))
                return;

            try
            {
                var soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Sounds", "error.wav");

                if (File.Exists(soundPath))
                {
                    PlayWaveFile(soundPath);
                }
                else
                {
                    System.Media.SystemSounds.Hand.Play();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Fehler beim Abspielen des Fehler-Sounds");
            }
        }

        /// <summary>
        /// Zeigt eine Windows-Benachrichtigung an
        /// </summary>
        public void ShowWindowsNotification(string title, string message, NotificationType type = NotificationType.Info)
        {
            try
            {
                // Hier könnte Windows 10/11 Toast Notification implementiert werden
                // Für Einfachheit verwenden wir erstmal System Tray Notifications

                _logger?.LogInformation($"Benachrichtigung: {title} - {message}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Anzeigen der Windows-Benachrichtigung");
            }
        }
        #endregion

        #region Private Methods
        private void PlayWaveFile(string filePath)
        {
            try
            {
                using (var player = new System.Media.SoundPlayer(filePath))
                {
                    player.Play();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"Fehler beim Abspielen der Wave-Datei: {filePath}");
                // Fallback zu System-Sound
                System.Media.SystemSounds.Beep.Play();
            }
        }
        #endregion
    }

    /// <summary>
    /// Typen von Benachrichtigungen
    /// </summary>
    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Logging Service für strukturierte Logs
    /// </summary>
    public class LoggingService
    {
        #region Private Fields
        private readonly ILogger<LoggingService>? _logger;
        private readonly string _logDirectory;
        #endregion

        #region Constructor
        public LoggingService(ILogger<LoggingService>? logger = null)
        {
            _logger = logger;
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

            EnsureLogDirectoryExists();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Loggt eine NFC-Karten-Erkennung
        /// </summary>
        public void LogCardDetection(NFC_Reader.Core.NFCCard card)
        {
            try
            {
                var logEntry = new
                {
                    Timestamp = DateTime.Now,
                    EventType = "CardDetected",
                    ReaderName = card.ReaderName,
                    CardType = card.CardType.ToString(),
                    TextLength = card.Text?.Length ?? 0,
                    ATR = card.ATR
                };

                var logFile = Path.Combine(_logDirectory, $"nfc_activity_{DateTime.Now:yyyy-MM-dd}.json");
                var logLine = JsonConvert.SerializeObject(logEntry) + Environment.NewLine;

                File.AppendAllText(logFile, logLine);

                _logger?.LogInformation("NFC-Karten-Erkennung geloggt: {CardType} von {ReaderName}",
                    card.CardType, card.ReaderName);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Loggen der Karten-Erkennung");
            }
        }

        /// <summary>
        /// Loggt eine Text-Einfügung
        /// </summary>
        public void LogTextInjection(string text, string targetApplication, bool success)
        {
            try
            {
                var logEntry = new
                {
                    Timestamp = DateTime.Now,
                    EventType = "TextInjection",
                    TargetApplication = targetApplication,
                    TextLength = text?.Length ?? 0,
                    Success = success
                };

                var logFile = Path.Combine(_logDirectory, $"text_injection_{DateTime.Now:yyyy-MM-dd}.json");
                var logLine = JsonConvert.SerializeObject(logEntry) + Environment.NewLine;

                File.AppendAllText(logFile, logLine);

                _logger?.LogInformation("Text-Einfügung geloggt: {Success} in {TargetApplication}",
                    success, targetApplication);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Loggen der Text-Einfügung");
            }
        }

        /// <summary>
        /// Bereinigt alte Log-Dateien
        /// </summary>
        public void CleanupOldLogs(int daysToKeep = 30)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var logFiles = Directory.GetFiles(_logDirectory, "*.json");

                foreach (var file in logFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        File.Delete(file);
                        _logger?.LogDebug("Alte Log-Datei gelöscht: {FileName}", fileInfo.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Bereinigen alter Log-Dateien");
            }
        }
        #endregion

        #region Private Methods
        private void EnsureLogDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                    _logger?.LogDebug("Log-Verzeichnis erstellt: {LogDirectory}", _logDirectory);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Erstellen des Log-Verzeichnisses");
            }
        }
        #endregion
    }
}