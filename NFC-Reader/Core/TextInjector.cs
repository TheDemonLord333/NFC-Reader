using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace NFC_Reader.Core
{
    /// <summary>
    /// Service für die automatische Text-Einfügung in aktive Anwendungen
    /// </summary>
    public class TextInjector
    {
        #region Windows API Imports
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        // Konstanten
        private const int KEYEVENTF_EXTENDEDKEY = 0x1;
        private const int KEYEVENTF_KEYUP = 0x2;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_LCONTROL = 0xA2;
        private const byte VK_RCONTROL = 0xA3;
        private const uint WM_CHAR = 0x0102;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        #endregion

        #region Private Fields
        private readonly ILogger<TextInjector>? _logger;
        private readonly object _lockObject = new object();
        #endregion

        #region Properties
        public TextInjectionMethod Method { get; set; } = TextInjectionMethod.Clipboard;
        public int DelayBetweenChars { get; set; } = 10;
        public int DelayBeforeInjection { get; set; } = 100;
        public bool PreserveClipboard { get; set; } = true;
        #endregion

        #region Constructor
        public TextInjector(ILogger<TextInjector>? logger = null)
        {
            _logger = logger;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Fügt Text in die aktive Anwendung ein
        /// </summary>
        public async Task<bool> InjectTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                _logger?.LogWarning("Leerer Text kann nicht eingefügt werden");
                return false;
            }

            return await InjectTextAsync(text, CancellationToken.None);
        }

        /// <summary>
        /// Fügt Text in die aktive Anwendung mit Cancellation Token ein
        /// </summary>
        public async Task<bool> InjectTextAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            lock (_lockObject)
            {
                try
                {
                    var activeWindow = GetActiveWindowInfo();
                    if (activeWindow == null)
                    {
                        _logger?.LogWarning("Kein aktives Fenster gefunden");
                        return false;
                    }

                    _logger?.LogInformation($"Text wird eingefügt in: {activeWindow.ProcessName} - {activeWindow.WindowTitle}");

                    return Method switch
                    {
                        TextInjectionMethod.Clipboard => InjectViaClipboard(text, activeWindow),
                        TextInjectionMethod.SendKeys => InjectViaSendKeys(text, activeWindow, cancellationToken),
                        TextInjectionMethod.PostMessage => InjectViaPostMessage(text, activeWindow),
                        TextInjectionMethod.Smart => InjectViaSmart(text, activeWindow, cancellationToken),
                        _ => false
                    };
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Fehler beim Text-Einfügen");
                    return false;
                }
            }
        }

        /// <summary>
        /// Fügt Text in eine spezifische Anwendung ein
        /// </summary>
        public async Task<bool> InjectTextToApplicationAsync(string text, string processName)
        {
            try
            {
                var process = Process.GetProcessesByName(processName);
                if (process.Length == 0)
                {
                    _logger?.LogWarning($"Prozess '{processName}' nicht gefunden");
                    return false;
                }

                var hwnd = process[0].MainWindowHandle;
                if (hwnd == IntPtr.Zero)
                {
                    _logger?.LogWarning($"Hauptfenster von '{processName}' nicht gefunden");
                    return false;
                }

                // Fenster in den Vordergrund bringen
                SetForegroundWindow(hwnd);
                await Task.Delay(DelayBeforeInjection);

                return await InjectTextAsync(text);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Fehler beim Text-Einfügen in Anwendung '{processName}'");
                return false;
            }
        }

        /// <summary>
        /// Simuliert Tastatureingabe für spezielle Zeichen
        /// </summary>
        public async Task<bool> SendSpecialKeysAsync(SpecialKey key)
        {
            try
            {
                byte keyCode = key switch
                {
                    SpecialKey.Enter => 0x0D,
                    SpecialKey.Tab => 0x09,
                    SpecialKey.Escape => 0x1B,
                    SpecialKey.Backspace => 0x08,
                    SpecialKey.Delete => 0x2E,
                    SpecialKey.Home => 0x24,
                    SpecialKey.End => 0x23,
                    SpecialKey.ArrowUp => 0x26,
                    SpecialKey.ArrowDown => 0x28,
                    SpecialKey.ArrowLeft => 0x25,
                    SpecialKey.ArrowRight => 0x27,
                    _ => 0
                };

                if (keyCode == 0)
                    return false;

                await Task.Delay(DelayBeforeInjection);

                keybd_event(keyCode, 0, 0, UIntPtr.Zero);
                await Task.Delay(50);
                keybd_event(keyCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Senden spezieller Tasten");
                return false;
            }
        }
        #endregion

        #region Private Methods
        private ActiveWindowInfo? GetActiveWindowInfo()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return null;

            try
            {
                var length = GetWindowTextLength(hwnd);
                var sb = new StringBuilder(length + 1);
                GetWindowText(hwnd, sb, sb.Capacity);

                GetWindowThreadProcessId(hwnd, out uint processId);
                var process = Process.GetProcessById((int)processId);

                return new ActiveWindowInfo
                {
                    Handle = hwnd,
                    WindowTitle = sb.ToString(),
                    ProcessName = process.ProcessName,
                    ProcessId = processId
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Abrufen der Fensterinformationen");
                return null;
            }
        }

        private bool InjectViaClipboard(string text, ActiveWindowInfo windowInfo)
        {
            try
            {
                string? originalClipboard = null;

                // Ursprünglichen Clipboard-Inhalt sichern
                if (PreserveClipboard)
                {
                    try
                    {
                        originalClipboard = Clipboard.GetText();
                    }
                    catch
                    {
                        // Clipboard ist möglicherweise von anderer Anwendung gesperrt
                    }
                }

                // Text in Clipboard kopieren
                Clipboard.SetText(text);
                Thread.Sleep(DelayBeforeInjection);

                // Ctrl+V simulieren
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(0x56, 0, 0, UIntPtr.Zero); // V
                Thread.Sleep(50);
                keybd_event(0x56, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                // Ursprünglichen Clipboard-Inhalt wiederherstellen
                if (PreserveClipboard && originalClipboard != null)
                {
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        try
                        {
                            Clipboard.SetText(originalClipboard);
                        }
                        catch
                        {
                            // Fehler beim Wiederherstellen ignorieren
                        }
                    });
                }

                _logger?.LogDebug("Text erfolgreich über Clipboard eingefügt");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Clipboard-basierten Text-Einfügen");
                return false;
            }
        }

        private bool InjectViaSendKeys(string text, ActiveWindowInfo windowInfo, CancellationToken cancellationToken)
        {
            try
            {
                Thread.Sleep(DelayBeforeInjection);

                foreach (char c in text)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return false;

                    SendChar(c);

                    if (DelayBetweenChars > 0)
                        Thread.Sleep(DelayBetweenChars);
                }

                _logger?.LogDebug("Text erfolgreich über SendKeys eingefügt");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim SendKeys-basierten Text-Einfügen");
                return false;
            }
        }

        private bool InjectViaPostMessage(string text, ActiveWindowInfo windowInfo)
        {
            try
            {
                foreach (char c in text)
                {
                    PostMessage(windowInfo.Handle, WM_CHAR, (IntPtr)c, IntPtr.Zero);
                    Thread.Sleep(DelayBetweenChars);
                }

                _logger?.LogDebug("Text erfolgreich über PostMessage eingefügt");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim PostMessage-basierten Text-Einfügen");
                return false;
            }
        }

        private bool InjectViaSmart(string text, ActiveWindowInfo windowInfo, CancellationToken cancellationToken)
        {
            // Intelligente Auswahl der besten Methode basierend auf der Anwendung
            var processName = windowInfo.ProcessName.ToLower();

            if (processName.Contains("notepad") || processName.Contains("wordpad") ||
                processName.Contains("code") || processName.Contains("devenv"))
            {
                // Für Editoren: SendKeys für bessere Formatierung
                return InjectViaSendKeys(text, windowInfo, cancellationToken);
            }
            else if (processName.Contains("chrome") || processName.Contains("firefox") ||
                     processName.Contains("edge") || processName.Contains("opera"))
            {
                // Für Browser: Clipboard für Zuverlässigkeit
                return InjectViaClipboard(text, windowInfo);
            }
            else if (processName.Contains("cmd") || processName.Contains("powershell") ||
                     processName.Contains("terminal"))
            {
                // Für Konsolen: PostMessage
                return InjectViaPostMessage(text, windowInfo);
            }
            else
            {
                // Standard: Clipboard
                return InjectViaClipboard(text, windowInfo);
            }
        }

        private void SendChar(char c)
        {
            short vkKey = VkKeyScan(c);
            byte key = (byte)(vkKey & 0xFF);
            byte shift = (byte)((vkKey >> 8) & 0xFF);

            // Shift-Taste drücken falls nötig
            if ((shift & 1) != 0)
                keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);

            // Zeichen senden
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            // Shift-Taste loslassen
            if ((shift & 1) != 0)
                keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        #endregion
    }

    #region Helper Classes and Enums
    /// <summary>
    /// Informationen über das aktive Fenster
    /// </summary>
    public class ActiveWindowInfo
    {
        public IntPtr Handle { get; set; }
        public string WindowTitle { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public uint ProcessId { get; set; }

        public override string ToString()
        {
            return $"{ProcessName} - {WindowTitle}";
        }
    }

    /// <summary>
    /// Methoden für Text-Einfügung
    /// </summary>
    public enum TextInjectionMethod
    {
        /// <summary>
        /// Über Zwischenablage (Ctrl+V) - am zuverlässigsten
        /// </summary>
        Clipboard,

        /// <summary>
        /// Über simulierte Tastatureingaben - behält Formatierung
        /// </summary>
        SendKeys,

        /// <summary>
        /// Über Windows-Nachrichten - schnell aber nicht alle Apps
        /// </summary>
        PostMessage,

        /// <summary>
        /// Intelligente Auswahl basierend auf Zielanwendung
        /// </summary>
        Smart
    }

    /// <summary>
    /// Spezielle Tasten für die Simulation
    /// </summary>
    public enum SpecialKey
    {
        Enter,
        Tab,
        Escape,
        Backspace,
        Delete,
        Home,
        End,
        ArrowUp,
        ArrowDown,
        ArrowLeft,
        ArrowRight
    }
    #endregion
}