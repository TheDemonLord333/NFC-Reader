using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NFC_Reader.Core;

namespace NFC_Reader.Services
{
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
        public void LogCardDetection(NFCCard card)
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