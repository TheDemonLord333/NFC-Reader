using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace NFC_Reader.Services
{
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
}