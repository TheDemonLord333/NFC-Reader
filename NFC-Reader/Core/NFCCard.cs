using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace NFC_Reader.Core
{
    /// <summary>
    /// Datenmodell für eine NFC-Karte mit allen relevanten Informationen
    /// </summary>
    public class NFCCard : INotifyPropertyChanged
    {
        #region Private Fields
        private string _text = string.Empty;
        private string _readerName = string.Empty;
        private DateTime _detectedAt;
        private string _atr = string.Empty;
        private NFCCardType _cardType;
        private string _uid = string.Empty;
        private int _memorySize;
        private bool _isEncrypted;
        #endregion

        #region Properties
        /// <summary>
        /// Der auf der Karte gespeicherte Text
        /// </summary>
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        /// <summary>
        /// Name des NFC-Readers, der die Karte erkannt hat
        /// </summary>
        public string ReaderName
        {
            get => _readerName;
            set => SetProperty(ref _readerName, value);
        }

        /// <summary>
        /// Zeitpunkt der Kartenerkennung
        /// </summary>
        public DateTime DetectedAt
        {
            get => _detectedAt;
            set => SetProperty(ref _detectedAt, value);
        }

        /// <summary>
        /// Answer to Reset (ATR) - eindeutige Kartenkennung
        /// </summary>
        public string ATR
        {
            get => _atr;
            set => SetProperty(ref _atr, value);
        }

        /// <summary>
        /// Typ der NFC-Karte (MIFARE, NTAG, etc.)
        /// </summary>
        public NFCCardType CardType
        {
            get => _cardType;
            set => SetProperty(ref _cardType, value);
        }

        /// <summary>
        /// Eindeutige ID der Karte (UID)
        /// </summary>
        public string UID
        {
            get => _uid;
            set => SetProperty(ref _uid, value);
        }

        /// <summary>
        /// Speichergröße der Karte in Bytes
        /// </summary>
        public int MemorySize
        {
            get => _memorySize;
            set => SetProperty(ref _memorySize, value);
        }

        /// <summary>
        /// Gibt an, ob die Karte verschlüsselt ist
        /// </summary>
        public bool IsEncrypted
        {
            get => _isEncrypted;
            set => SetProperty(ref _isEncrypted, value);
        }

        /// <summary>
        /// Benutzerfreundlicher Name für die Karte
        /// </summary>
        [JsonIgnore]
        public string DisplayName => $"{CardType} - {Text?.Substring(0, Math.Min(Text?.Length ?? 0, 20))}...";

        /// <summary>
        /// Formatierte Anzeige der Erkennungszeit
        /// </summary>
        [JsonIgnore]
        public string DetectedAtFormatted => DetectedAt.ToString("dd.MM.yyyy HH:mm:ss");

        /// <summary>
        /// Gibt an, ob die Karte gültige Textdaten enthält
        /// </summary>
        [JsonIgnore]
        public bool HasValidText => !string.IsNullOrWhiteSpace(Text) && Text.Length > 0;

        /// <summary>
        /// Kurze ATR-Anzeige für UI
        /// </summary>
        [JsonIgnore]
        public string ShortATR => ATR?.Length > 16 ? $"{ATR.Substring(0, 16)}..." : ATR;
        #endregion

        #region Constructors
        /// <summary>
        /// Standardkonstruktor
        /// </summary>
        public NFCCard()
        {
            DetectedAt = DateTime.Now;
            CardType = NFCCardType.Unknown;
        }

        /// <summary>
        /// Konstruktor mit Text
        /// </summary>
        /// <param name="text">Text auf der Karte</param>
        public NFCCard(string text) : this()
        {
            Text = text;
        }

        /// <summary>
        /// Vollständiger Konstruktor
        /// </summary>
        public NFCCard(string text, string readerName, string atr, NFCCardType cardType = NFCCardType.Unknown) : this()
        {
            Text = text;
            ReaderName = readerName;
            ATR = atr;
            CardType = cardType;
        }
        #endregion

        #region Methods
        /// <summary>
        /// Erstellt eine Kopie der aktuellen Karte
        /// </summary>
        public NFCCard Clone()
        {
            return new NFCCard
            {
                Text = this.Text,
                ReaderName = this.ReaderName,
                DetectedAt = this.DetectedAt,
                ATR = this.ATR,
                CardType = this.CardType,
                UID = this.UID,
                MemorySize = this.MemorySize,
                IsEncrypted = this.IsEncrypted
            };
        }

        /// <summary>
        /// Validiert die Kartendaten
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(ATR) &&
                   !string.IsNullOrEmpty(ReaderName) &&
                   DetectedAt != default(DateTime);
        }

        /// <summary>
        /// Serialisiert die Karte zu JSON
        /// </summary>
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        /// <summary>
        /// Deserialisiert eine Karte aus JSON
        /// </summary>
        public static NFCCard? FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<NFCCard>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Gibt eine benutzerfreundliche String-Darstellung zurück
        /// </summary>
        public override string ToString()
        {
            return $"NFC Card [{CardType}]: {Text} (Reader: {ReaderName}, {DetectedAtFormatted})";
        }

        /// <summary>
        /// Vergleicht zwei Karten basierend auf ATR und UID
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is NFCCard other)
            {
                return ATR == other.ATR && UID == other.UID;
            }
            return false;
        }

        /// <summary>
        /// Generiert Hash-Code basierend auf ATR und UID
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(ATR, UID);
        }
        #endregion

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion
    }

    /// <summary>
    /// Unterstützte NFC-Kartentypen
    /// </summary>
    public enum NFCCardType
    {
        Unknown,
        MifareClassic1K,
        MifareClassic4K,
        MifareUltralight,
        NTAG213,
        NTAG215,
        NTAG216,
        ISO14443TypeA,
        ISO14443TypeB,
        FeliCa,
        ISO15693,
        Topaz,
        Custom
    }

    /// <summary>
    /// Hilfsklasse für Kartentyp-Erkennung und -Information
    /// </summary>
    public static class NFCCardTypeHelper
    {
        /// <summary>
        /// Bestimmt den Kartentyp basierend auf ATR
        /// </summary>
        public static NFCCardType DetermineCardType(string atr)
        {
            if (string.IsNullOrEmpty(atr))
                return NFCCardType.Unknown;

            // ATR-basierte Erkennung
            if (atr.Contains("3B8F8001804F0CA000000306030001000000006A"))
                return NFCCardType.MifareClassic1K;

            if (atr.Contains("3B8F8001804F0CA000000306030004000000006D"))
                return NFCCardType.MifareClassic4K;

            if (atr.StartsWith("3B8F") && atr.Contains("804F"))
                return NFCCardType.MifareUltralight;

            if (atr.Contains("NTAG213"))
                return NFCCardType.NTAG213;
            if (atr.Contains("NTAG215"))
                return NFCCardType.NTAG215;
            if (atr.Contains("NTAG216"))
                return NFCCardType.NTAG216;

            return NFCCardType.Unknown;
        }

        /// <summary>
        /// Gibt die typische Speichergröße für einen Kartentyp zurück
        /// </summary>
        public static int GetTypicalMemorySize(NFCCardType cardType)
        {
            return cardType switch
            {
                NFCCardType.MifareClassic1K => 1024,
                NFCCardType.MifareClassic4K => 4096,
                NFCCardType.MifareUltralight => 512,
                NFCCardType.NTAG213 => 180,
                NFCCardType.NTAG215 => 540,
                NFCCardType.NTAG216 => 924,
                _ => 0
            };
        }

        /// <summary>
        /// Gibt eine benutzerfreundliche Beschreibung des Kartentyps zurück
        /// </summary>
        public static string GetDescription(NFCCardType cardType)
        {
            return cardType switch
            {
                NFCCardType.MifareClassic1K => "MIFARE Classic 1K (1024 Bytes)",
                NFCCardType.MifareClassic4K => "MIFARE Classic 4K (4096 Bytes)",
                NFCCardType.MifareUltralight => "MIFARE Ultralight (512 Bytes)",
                NFCCardType.NTAG213 => "NTAG213 (180 Bytes)",
                NFCCardType.NTAG215 => "NTAG215 (540 Bytes)",
                NFCCardType.NTAG216 => "NTAG216 (924 Bytes)",
                NFCCardType.ISO14443TypeA => "ISO14443 Type A",
                NFCCardType.ISO14443TypeB => "ISO14443 Type B",
                NFCCardType.FeliCa => "FeliCa",
                NFCCardType.ISO15693 => "ISO15693",
                NFCCardType.Topaz => "Topaz",
                NFCCardType.Custom => "Benutzerdefiniert",
                _ => "Unbekannter Kartentyp"
            };
        }

        /// <summary>
        /// Prüft, ob ein Kartentyp beschreibbar ist
        /// </summary>
        public static bool IsWritable(NFCCardType cardType)
        {
            return cardType switch
            {
                NFCCardType.MifareClassic1K => true,
                NFCCardType.MifareClassic4K => true,
                NFCCardType.MifareUltralight => true,
                NFCCardType.NTAG213 => true,
                NFCCardType.NTAG215 => true,
                NFCCardType.NTAG216 => true,
                _ => false
            };
        }
    }
}