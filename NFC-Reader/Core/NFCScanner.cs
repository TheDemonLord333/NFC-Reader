using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PCSC;
using PCSC.Exceptions;
using PCSC.Monitoring;
using Microsoft.Extensions.Logging;

namespace NFC_Reader.Core
{
    public class NFCScanner : IDisposable
    {
        #region Events
        public event EventHandler<NFCCardDetectedEventArgs>? CardDetected;
        public event EventHandler<NFCCardRemovedEventArgs>? CardRemoved;
        public event EventHandler<NFCScannerStatusEventArgs>? StatusChanged;
        public event EventHandler<NFCErrorEventArgs>? ErrorOccurred;
        #endregion

        #region Private Fields
        private readonly ILogger<NFCScanner>? _logger;
        private ISCardContext? _context;
        private ISCardMonitor? _monitor;
        private bool _isScanning = false;
        private bool _disposed = false;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _lockObject = new object();
        #endregion

        #region Properties
        public bool IsScanning => _isScanning;
        public string[] AvailableReaders { get; private set; } = Array.Empty<string>();
        public string? CurrentReader { get; private set; }
        public NFCScannerStatus Status { get; private set; } = NFCScannerStatus.Stopped;
        #endregion

        #region Constructor
        public NFCScanner(ILogger<NFCScanner>? logger = null)
        {
            _logger = logger;
            InitializeContext();
        }
        #endregion

        #region Public Methods
        public async Task<bool> StartScanningAsync()
        {
            if (_isScanning)
            {
                _logger?.LogWarning("Scanner läuft bereits!");
                return true;
            }

            try
            {
                lock (_lockObject)
                {
                    if (_disposed)
                        throw new ObjectDisposedException(nameof(NFCScanner));

                    _cancellationTokenSource = new CancellationTokenSource();
                }

                await RefreshReadersAsync();

                if (!AvailableReaders.Any())
                {
                    var errorMsg = "Keine NFC-Reader gefunden! Bitte prüfen Sie die Hardware-Verbindung.";
                    _logger?.LogError(errorMsg);
                    OnErrorOccurred(new NFCException(errorMsg));
                    return false;
                }

                CurrentReader = AvailableReaders.First();
                _logger?.LogInformation($"Verwende NFC-Reader: {CurrentReader}");

                SetupMonitoring();

                _isScanning = true;
                UpdateStatus(NFCScannerStatus.Scanning);

                _logger?.LogInformation("NFC-Scanner erfolgreich gestartet");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Starten des NFC-Scanners");
                OnErrorOccurred(ex);
                return false;
            }
        }

        public void StopScanning()
        {
            if (!_isScanning)
                return;

            try
            {
                _cancellationTokenSource?.Cancel();
                _monitor?.Cancel();
                _isScanning = false;
                UpdateStatus(NFCScannerStatus.Stopped);

                _logger?.LogInformation("NFC-Scanner gestoppt");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Stoppen des NFC-Scanners");
                OnErrorOccurred(ex);
            }
        }

        public async Task<List<string>> GetAvailableReadersAsync()
        {
            await RefreshReadersAsync();
            return AvailableReaders.ToList();
        }

        public async Task<string> ReadCardTextAsync(string? readerName = null)
        {
            var reader = readerName ?? CurrentReader;
            if (string.IsNullOrEmpty(reader))
                throw new InvalidOperationException("Kein Reader verfügbar");

            try
            {
                using var cardReader = _context!.ConnectReader(reader, SCardShareMode.Shared, SCardProtocol.Any);

                var status = cardReader.GetStatus();
                var atr = status.GetAtr();

                _logger?.LogDebug($"Karte verbunden. ATR: {BitConverter.ToString(atr)}");

                // Je nach Kartentyp unterschiedliche Lesemethoden
                if (IsNTAGCard(atr))
                {
                    return await ReadNTAGCardAsync(cardReader);
                }
                else if (IsMifareCard(atr))
                {
                    return await ReadMifareCardAsync(cardReader);
                }
                else
                {
                    return await ReadGenericCardAsync(cardReader);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Fehler beim Lesen der Karte von Reader: {reader}");
                throw;
            }
        }
        #endregion

        #region Private Methods
        private void InitializeContext()
        {
            try
            {
                _context = ContextFactory.Instance.Establish(SCardScope.System);
                _logger?.LogDebug("PC/SC Kontext erfolgreich erstellt");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Erstellen des PC/SC Kontexts");
                throw new NFCException("PC/SC Service nicht verfügbar. Bitte starten Sie den Smart Card Service.", ex);
            }
        }

        private async Task RefreshReadersAsync()
        {
            try
            {
                var readers = _context!.GetReaders();
                AvailableReaders = readers?.ToArray() ?? Array.Empty<string>();

                _logger?.LogDebug($"Verfügbare Reader: {string.Join(", ", AvailableReaders)}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Abrufen verfügbarer Reader");
                AvailableReaders = Array.Empty<string>();
            }
            await Task.CompletedTask;
        }

        private void SetupMonitoring()
        {
            try
            {
                _monitor = MonitorFactory.Instance.Create(SCardScope.System);
                _monitor.CardInserted += OnCardInserted;
                _monitor.CardRemoved += OnCardRemoved;
                _monitor.MonitorException += OnMonitorException;

                _monitor.Start(AvailableReaders);
                _logger?.LogDebug("Karten-Monitoring gestartet");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Setup des Monitorings");
                throw;
            }
        }

        private async void OnCardInserted(object sender, CardStatusEventArgs e)
        {
            try
            {
                _logger?.LogInformation($"Karte eingelegt in Reader: {e.ReaderName}");
                UpdateStatus(NFCScannerStatus.CardDetected);

                await Task.Delay(100); // Kurz warten für stabile Verbindung

                var text = await ReadCardTextAsync(e.ReaderName);

                var cardInfo = new NFCCard
                {
                    ReaderName = e.ReaderName,
                    DetectedAt = DateTime.Now,
                    Text = text,
                    ATR = BitConverter.ToString(e.Atr)
                };

                OnCardDetected(cardInfo);
                UpdateStatus(NFCScannerStatus.Scanning);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Verarbeiten der eingelegten Karte");
                OnErrorOccurred(ex);
                UpdateStatus(NFCScannerStatus.Scanning);
            }
        }

        private void OnCardRemoved(object sender, CardStatusEventArgs e)
        {
            _logger?.LogInformation($"Karte entfernt aus Reader: {e.ReaderName}");
            OnCardRemoved(new NFCCardRemovedEventArgs(e.ReaderName, DateTime.Now));
            UpdateStatus(NFCScannerStatus.Scanning);
        }

        private void OnMonitorException(object sender, PCSCException ex)
        {
            _logger?.LogError(ex, "Monitor Exception aufgetreten");
            OnErrorOccurred(ex);
        }

        #region Card Reading Methods
        private async Task<string> ReadNTAGCardAsync(ICardReader reader)
        {
            try
            {
                // NTAG-Karten (NTAG213, NTAG215, NTAG216) lesen
                byte[] command = { 0xFF, 0xB0, 0x00, 0x04, 0x10 }; // READ BINARY, Start bei Seite 4, 16 Bytes

                byte[] response = new byte[18]; // 16 Bytes Daten + 2 Bytes Status
                int bytesReceived = reader.Transmit(command, response);

                if (bytesReceived >= 2)
                {
                    byte sw1 = response[bytesReceived - 2];
                    byte sw2 = response[bytesReceived - 1];

                    if (sw1 == 0x90 && sw2 == 0x00)
                    {
                        byte[] data = new byte[bytesReceived - 2];
                        Array.Copy(response, 0, data, 0, bytesReceived - 2);
                        return ParseNDEFText(data);
                    }
                }

                return "Fehler beim Lesen der NTAG-Karte";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Lesen der NTAG-Karte");
                return $"NTAG Lesefehler: {ex.Message}";
            }
        }

        private async Task<string> ReadMifareCardAsync(ICardReader reader)
        {
            try
            {
                // MIFARE Classic Karte lesen
                // Authentifizierung mit Standard-Schlüssel
                byte[] loadKeyCommand = { 0xFF, 0x82, 0x00, 0x00, 0x06, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
                byte[] authResponse = new byte[2];
                int authBytes = reader.Transmit(loadKeyCommand, authResponse);

                if (authBytes >= 2 && authResponse[0] == 0x90)
                {
                    // Authentifizierung für Block 4
                    byte[] authCommand = { 0xFF, 0x86, 0x00, 0x00, 0x05, 0x01, 0x00, 0x04, 0x60, 0x00 };
                    byte[] authResult = new byte[2];
                    int authResultBytes = reader.Transmit(authCommand, authResult);

                    if (authResultBytes >= 2 && authResult[0] == 0x90)
                    {
                        // Block 4 lesen
                        byte[] readCommand = { 0xFF, 0xB0, 0x00, 0x04, 0x10 };
                        byte[] readResponse = new byte[18];
                        int readBytes = reader.Transmit(readCommand, readResponse);

                        if (readBytes >= 2)
                        {
                            byte sw1 = readResponse[readBytes - 2];
                            byte sw2 = readResponse[readBytes - 1];

                            if (sw1 == 0x90 && sw2 == 0x00)
                            {
                                byte[] data = new byte[readBytes - 2];
                                Array.Copy(readResponse, 0, data, 0, readBytes - 2);
                                return Encoding.UTF8.GetString(data).TrimEnd('\0');
                            }
                        }
                    }
                }

                return "Fehler beim Lesen der MIFARE-Karte";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Lesen der MIFARE-Karte");
                return $"MIFARE Lesefehler: {ex.Message}";
            }
        }

        private async Task<string> ReadGenericCardAsync(ICardReader reader)
        {
            try
            {
                // Generisches Lesen für unbekannte Kartentypen
                byte[] command = { 0x00, 0xB0, 0x00, 0x00, 0x00 }; // READ BINARY
                byte[] response = new byte[258]; // Maximale Antwort
                int bytesReceived = reader.Transmit(command, response);

                if (bytesReceived > 2)
                {
                    byte sw1 = response[bytesReceived - 2];
                    byte sw2 = response[bytesReceived - 1];

                    if (sw1 == 0x90 && sw2 == 0x00)
                    {
                        byte[] data = new byte[bytesReceived - 2];
                        Array.Copy(response, 0, data, 0, bytesReceived - 2);
                        var text = Encoding.UTF8.GetString(data);
                        return text.TrimEnd('\0', ' ');
                    }
                }

                return "Keine lesbaren Daten auf der Karte gefunden";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim generischen Kartenlesen");
                return $"Generischer Lesefehler: {ex.Message}";
            }
        }

        private string ParseNDEFText(byte[] data)
        {
            try
            {
                // NDEF Text Record parsen
                if (data.Length < 3)
                    return "Unvollständige NDEF-Daten";

                // NDEF Header prüfen
                if (data[0] == 0x03) // NDEF Message
                {
                    var length = data[1];
                    if (data[2] == 0xD1 && data[3] == 0x01) // Text Record
                    {
                        var textLength = data[4];
                        var languageCodeLength = data[5] & 0x3F;
                        var textStart = 6 + languageCodeLength;

                        if (textStart < data.Length)
                        {
                            var textBytes = new byte[textLength - languageCodeLength - 1];
                            Array.Copy(data, textStart, textBytes, 0, Math.Min(textBytes.Length, data.Length - textStart));
                            return Encoding.UTF8.GetString(textBytes);
                        }
                    }
                }

                // Fallback: Raw-Text extrahieren
                return Encoding.UTF8.GetString(data).TrimEnd('\0', ' ');
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Fehler beim Parsen der NDEF-Daten");
                return Encoding.UTF8.GetString(data).TrimEnd('\0', ' ');
            }
        }

        private bool IsNTAGCard(byte[] atr)
        {
            // NTAG Karten erkennen basierend auf ATR
            return atr.Length >= 4 && atr[0] == 0x3B;
        }

        private bool IsMifareCard(byte[] atr)
        {
            // MIFARE Karten erkennen
            return atr.Length >= 6 && atr[13] == 0x00;
        }
        #endregion

        private void UpdateStatus(NFCScannerStatus newStatus)
        {
            if (Status != newStatus)
            {
                Status = newStatus;
                StatusChanged?.Invoke(this, new NFCScannerStatusEventArgs(newStatus));
            }
        }

        private void OnCardDetected(NFCCard card)
        {
            CardDetected?.Invoke(this, new NFCCardDetectedEventArgs(card));
        }

        private void OnCardRemoved(NFCCardRemovedEventArgs args)
        {
            CardRemoved?.Invoke(this, args);
        }

        private void OnErrorOccurred(Exception ex)
        {
            ErrorOccurred?.Invoke(this, new NFCErrorEventArgs(ex));
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            if (_disposed)
                return;

            StopScanning();

            _monitor?.Dispose();
            _context?.Dispose();
            _cancellationTokenSource?.Dispose();

            _disposed = true;
            _logger?.LogDebug("NFCScanner disposed");
        }
        #endregion
    }

    #region Enums and Event Args
    public enum NFCScannerStatus
    {
        Stopped,
        Scanning,
        CardDetected,
        Error
    }

    public class NFCCardDetectedEventArgs : EventArgs
    {
        public NFCCard Card { get; }

        public NFCCardDetectedEventArgs(NFCCard card)
        {
            Card = card;
        }
    }

    public class NFCCardRemovedEventArgs : EventArgs
    {
        public string ReaderName { get; }
        public DateTime RemovedAt { get; }

        public NFCCardRemovedEventArgs(string readerName, DateTime removedAt)
        {
            ReaderName = readerName;
            RemovedAt = removedAt;
        }
    }

    public class NFCScannerStatusEventArgs : EventArgs
    {
        public NFCScannerStatus Status { get; }

        public NFCScannerStatusEventArgs(NFCScannerStatus status)
        {
            Status = status;
        }
    }

    public class NFCErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }

        public NFCErrorEventArgs(Exception exception)
        {
            Exception = exception;
        }
    }

    public class NFCException : Exception
    {
        public NFCException(string message) : base(message) { }
        public NFCException(string message, Exception innerException) : base(message, innerException) { }
    }
    #endregion
}