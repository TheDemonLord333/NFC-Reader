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
}