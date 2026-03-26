using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Linq;
using ATAS.Indicators;
using ATAS.Indicators.Drawing;
using OFT.Attributes;
using OFT.Rendering.Context;
using OFT.Rendering.Settings;
using CrossColor = System.Windows.Media.Color;

namespace CustomIndicators
{
    [DisplayName("TR_Template")]
    [Description("TradingView TR template port: EMAs, EMA cloud, PVSRA candles, ADR/ATR, previous day high/low, daily open, sessions and weekly psy.")]
    public class TR_Template : Indicator
    {
        private const string GroupEma = "01. EMAs";
        private const string GroupPvsra = "02. PVSRA Candles";
        private const string GroupYDay = "03. Yesterday High/Low";
        private const string GroupAdr = "04. ADR";
        private const string GroupAtr = "05. ATR";
        private const string GroupDailyOpen = "06. Daily Open";
        private const string GroupSessions = "07. Market Sessions";
        private const string GroupPsy = "08. Weekly Psy";
        private const string GroupVolume = "09. Volume & Profile";

        private const int Ema5Period = 5;
        private const int Ema13Period = 13;
        private const int Ema50Period = 50;
        private const int Ema200Period = 200;
        private const int Ema800Period = 800;
        private const int EmaCloudStdevPeriod = 100;
        private const int MaxStoredRanges = 500;
        private static readonly TimeZoneInfo UsEasternTimeZone = ResolveTimeZone("Eastern Standard Time", "America/New_York");
        private static readonly TimeZoneInfo UkTimeZone = ResolveTimeZone("GMT Standard Time", "Europe/London");

        public enum ProfileDirection
        {
            [Display(Name = "Left To Right")]
            LeftToRight,

            [Display(Name = "Right To Left")]
            RightToLeft
        }

        public enum ProfileRenderMode
        {
            [Display(Name = "Histogram")]
            Histogram,

            [Display(Name = "Line")]
            Line,

            [Display(Name = "Both")]
            Both
        }

        public enum ProfileLineStyle
        {
            [Display(Name = "Solid")]
            Solid,

            [Display(Name = "Dashed")]
            Dashed,

            [Display(Name = "Dotted")]
            Dotted
        }

        public enum ZoneBoundaryType
        {
            [Display(Name = "Body only")]
            BodyOnly,

            [Display(Name = "Body with wicks")]
            BodyWithWicks
        }

        private sealed class DayVolumeProfile
        {
            public DateTime Date { get; init; }
            public int StartBar { get; init; }
            public int EndBar { get; init; }
            public SortedDictionary<decimal, decimal> Levels { get; } = new();
            public decimal MaxVolume { get; set; }
            public decimal MinNonZeroVolume { get; set; }
            public decimal PriceStep { get; set; }
            public decimal PocPrice { get; set; }
            public decimal VahPrice { get; set; }
            public decimal ValPrice { get; set; }
            public bool IsValid { get; set; }
        }

        private sealed class VectorZone
        {
            public int StartBar { get; set; }
            public int EndBar { get; set; }
            public int CreatedBar { get; set; }
            public decimal Top { get; set; }
            public decimal Bottom { get; set; }
            public Color FillColor { get; set; }
            public Color BorderColor { get; set; }
        }

        private readonly ValueDataSeries _ema5 = new("EMA5", "EMA 5");
        private readonly ValueDataSeries _ema13 = new("EMA13", "EMA 13");
        private readonly ValueDataSeries _ema50 = new("EMA50", "EMA 50");
        private readonly ValueDataSeries _ema200 = new("EMA200", "EMA 200");
        private readonly ValueDataSeries _ema800 = new("EMA800", "EMA 800");
        private readonly ValueDataSeries _vwapSeries = new("VWAP", "VWAP");
        private readonly RangeDataSeries _emaCloud = new("EMACloud", "EMA 50 Cloud")
        {
            DrawAbovePrice = false
        };

        private readonly ValueDataSeries _prevDayHighSeries = new("PrevDayHigh", "YDay Hi");
        private readonly ValueDataSeries _prevDayLowSeries = new("PrevDayLow", "YDay Lo");
        private readonly ValueDataSeries _adrHighSeries = new("ADRHigh", "Hi-ADR");
        private readonly ValueDataSeries _adrLowSeries = new("ADRLow", "Lo-ADR");
        private readonly ValueDataSeries _atrHighSeries = new("ATRHigh", "ATR High");
        private readonly ValueDataSeries _atrLowSeries = new("ATRLow", "ATR Low");
        private readonly ValueDataSeries _dailyOpenSeries = new("DailyOpen", "Daily Open");
        private readonly ValueDataSeries _londonHighSeries = new("LondonHigh", "London Hi");
        private readonly ValueDataSeries _londonLowSeries = new("LondonLow", "London Lo");
        private readonly ValueDataSeries _newYorkHighSeries = new("NewYorkHigh", "NY Hi");
        private readonly ValueDataSeries _newYorkLowSeries = new("NewYorkLow", "NY Lo");
        private readonly ValueDataSeries _asiaHighSeries = new("AsiaHigh", "Asia Hi");
        private readonly ValueDataSeries _asiaLowSeries = new("AsiaLow", "Asia Lo");
        private readonly RangeDataSeries _londonSessionBox = new("LondonSessionBox", "London Session")
        {
            DrawAbovePrice = false
        };
        private readonly RangeDataSeries _newYorkSessionBox = new("NewYorkSessionBox", "NY Session")
        {
            DrawAbovePrice = false
        };
        private readonly RangeDataSeries _asiaSessionBox = new("AsiaSessionBox", "Asia Session")
        {
            DrawAbovePrice = false
        };
        private readonly RangeDataSeries _euBrinksSessionBox = new("EuBrinksSessionBox", "EU Brinks Session")
        {
            DrawAbovePrice = false
        };
        private readonly RangeDataSeries _usBrinksSessionBox = new("UsBrinksSessionBox", "US Brinks Session")
        {
            DrawAbovePrice = false
        };
        private readonly ValueDataSeries _psyHighSeries = new("PsyHigh", "Psy Hi");
        private readonly ValueDataSeries _psyLowSeries = new("PsyLow", "Psy Lo");

        private readonly PaintbarsDataSeries _pvsraBars = new("PVSRA", "PVSRA");

        private readonly ValueDataSeries _ema5Calc = new("EMA5Calc");
        private readonly ValueDataSeries _ema13Calc = new("EMA13Calc");
        private readonly ValueDataSeries _ema50Calc = new("EMA50Calc");
        private readonly ValueDataSeries _ema200Calc = new("EMA200Calc");
        private readonly ValueDataSeries _ema800Calc = new("EMA800Calc");

        private readonly List<decimal> _closedDailyRanges = new();
        private readonly List<decimal> _closedDailyTrueRanges = new();
        private readonly List<VectorZone> _vectorZonesAbove = new();
        private readonly List<VectorZone> _vectorZonesBelow = new();
        private readonly List<Color> _pvsraComputedColors = new();
        private int _lastProcessedBar = -1;
        private int _lastVectorAlertBar = -1;
        private int _lastVectorZoneCreationBar = -1;
        private bool _hasActiveSession;
        private DateTime _sessionDate = DateTime.MinValue;
        private decimal _sessionOpen;
        private decimal _sessionHigh;
        private decimal _sessionLow;
        private decimal _sessionClose;
        private bool _hasPreviousSession;
        private decimal _previousSessionHigh;
        private decimal _previousSessionLow;
        private bool _hasPreviousCloseForAtr;
        private decimal _previousSessionClose;
        private bool _adrHighTouched;
        private bool _adrLowTouched;
        private DateTime _londonSessionId = DateTime.MinValue;
        private int _londonSessionStartBar = -1;
        private decimal _londonSessionHigh;
        private decimal _londonSessionLow;
        private DateTime _newYorkSessionId = DateTime.MinValue;
        private int _newYorkSessionStartBar = -1;
        private decimal _newYorkSessionHigh;
        private decimal _newYorkSessionLow;
        private DateTime _asiaSessionId = DateTime.MinValue;
        private int _asiaSessionStartBar = -1;
        private decimal _asiaSessionHigh;
        private decimal _asiaSessionLow;
        private DateTime _euBrinksSessionId = DateTime.MinValue;
        private int _euBrinksSessionStartBar = -1;
        private decimal _euBrinksSessionHigh;
        private decimal _euBrinksSessionLow;
        private DateTime _usBrinksSessionId = DateTime.MinValue;
        private int _usBrinksSessionStartBar = -1;
        private decimal _usBrinksSessionHigh;
        private decimal _usBrinksSessionLow;
        private int _psyWeekKey = int.MinValue;
        private decimal _psyHigh;
        private decimal _psyLow;
        private bool _hasPsyValue;
        private DateTime _vwapSessionDate = DateTime.MinValue;
        private decimal _vwapCumulativePriceVolume;
        private decimal _vwapCumulativeVolume;
        private readonly List<DayVolumeProfile> _volumeProfiles = new();

        private bool _showEmas = true;
        private bool _showEma5 = true;
        private bool _showEma13 = true;
        private bool _showEma50 = true;
        private bool _showEma200 = true;
        private bool _showEma800 = true;
        private bool _showEmaCloud = true;

        private int _ema5Width = 1;
        private int _ema13Width = 1;
        private int _ema50Width = 1;
        private int _ema200Width = 1;
        private int _ema800Width = 2;

        private Color _ema5Color = Color.FromArgb(254, 234, 74);
        private Color _ema13Color = Color.FromArgb(253, 84, 87);
        private Color _ema50Color = Color.FromArgb(31, 188, 211);
        private Color _ema200Color = Color.FromArgb(255, 255, 255);
        private Color _ema800Color = Color.FromArgb(50, 34, 144);
        private Color _emaCloudColor = Color.FromArgb(102, 155, 47, 174);

        private bool _showPvsraCandles = true;
        private int _pvsraLookback = 10;
        private Color _vectorRedColor = Color.Red;
        private Color _vectorGreenColor = Color.Lime;
        private Color _vectorVioletColor = Color.Fuchsia;
        private Color _vectorBlueColor = Color.Blue;
        private Color _regularUpColor = Color.FromArgb(153, 153, 153);
        private Color _regularDownColor = Color.FromArgb(77, 77, 77);
        private bool _useVectorAlerts = true;
        private string _vectorAlertFile = "alert1";
        private bool _showVectorZones = true;
        private int _vectorZonesMax = 500;
        private ZoneBoundaryType _vectorZoneType = ZoneBoundaryType.BodyOnly;
        private ZoneBoundaryType _vectorZoneUpdateType = ZoneBoundaryType.BodyWithWicks;
        private int _vectorZoneBorderWidth;
        private bool _vectorZoneColorOverride = true;
        private Color _vectorZoneColor = Color.FromArgb(26, 255, 230, 75);
        private int _vectorZoneTransparency = 90;
        private bool _showPvsraVolumeHistogram;
        private int _pvsraVolumeHistogramHeightPercent = 15;
        private int _pvsraVolumeHistogramOpacityPercent = 70;

        private bool _showPreviousDayLevels = true;
        private bool _showYDayToday = true;
        private bool _showYDayHistorical = true;
        private int _yDayHistoryDays = 5;
        private Color _previousDayLevelsColor = Color.Aqua;
        private int _previousDayLevelsWidth = 2;

        private bool _showAdrLevels = true;
        private bool _showAdrToday = true;
        private bool _showAdrHistorical;
        private int _adrHistoryDays = 5;
        private bool _useAdrFromDailyOpen = true;
        private int _adrLength = 14;
        private Color _adrColor = Color.Orange;
        private int _adrWidth = 1;

        private bool _showAtrLevels;
        private bool _useAtrFromDailyOpen;
        private int _atrLength = 14;
        private Color _atrColor = Color.FromArgb(160, 255, 165, 0);
        private int _atrWidth = 1;

        private bool _showDailyOpen = true;
        private bool _showDailyOpenToday = true;
        private bool _showDailyOpenHistorical;
        private int _dailyOpenHistoryDays = 5;
        private Color _dailyOpenColor = Color.FromArgb(254, 234, 78);
        private int _dailyOpenWidth = 1;

        private bool _showLondonSession;
        private bool _showNewYorkSession = true;
        private bool _showAsiaSession = true;
        private bool _showEuBrinksSession = true;
        private bool _showUsBrinksSession = true;
        private bool _autoSessionDst = true;
        private bool _showSessionHistorical = true;
        private int _sessionHistoryDays = 5;
        private TimeSpan _londonSessionOpen = new(8, 0, 0);
        private TimeSpan _londonSessionClose = new(16, 30, 0);
        private TimeSpan _newYorkSessionOpen = new(14, 30, 0);
        private TimeSpan _newYorkSessionClose = new(21, 0, 0);
        private TimeSpan _asiaSessionOpen = new(0, 0, 0);
        private TimeSpan _asiaSessionClose = new(8, 0, 0);
        private TimeSpan _euBrinksSessionOpen = new(8, 0, 0);
        private TimeSpan _euBrinksSessionClose = new(9, 0, 0);
        private TimeSpan _usBrinksSessionOpen = new(14, 0, 0);
        private TimeSpan _usBrinksSessionClose = new(15, 0, 0);
        private Color _londonSessionColor = Color.FromArgb(75, 120, 123, 134);
        private Color _newYorkSessionColor = Color.FromArgb(75, 251, 86, 91);
        private Color _asiaSessionColor = Color.FromArgb(75, 37, 228, 123);
        private Color _euBrinksSessionColor = Color.FromArgb(65, 255, 255, 255);
        private Color _usBrinksSessionColor = Color.FromArgb(65, 255, 255, 255);
        private int _sessionWidth = 3;

        private bool _showWeeklyPsy = true;
        private bool _showWeeklyPsyToday = true;
        private bool _showWeeklyPsyHistorical;
        private int _weeklyPsyHistoryWeeks = 4;
        private Color _weeklyPsyHighColor = Color.Lime;
        private Color _weeklyPsyLowColor = Color.Lime;
        private int _weeklyPsyWidth = 2;

        private bool _showVwap = true;
        private Color _vwapColor = Color.DeepPink;
        private int _vwapWidth = 2;
        private bool _showMarketProfile = true;
        private int _marketProfileHistoryDays = 1;
        private bool _marketProfileIncludeCurrentDay = true;
        private bool _marketProfileIgnoreSunday;
        private string _marketProfileMergeGroups = string.Empty;
        private bool _marketProfileUseSessionFilter;
        private TimeSpan _marketProfileSessionOpen = new(9, 30, 0);
        private TimeSpan _marketProfileSessionClose = new(16, 0, 0);
        private ProfileDirection _marketProfileDirection = ProfileDirection.LeftToRight;
        private ProfileRenderMode _marketProfileRenderMode = ProfileRenderMode.Histogram;
        private int _marketProfileMaxWidthPx = 180;
        private int _marketProfileWidthPercent = 28;
        private int _marketProfileMinBinWidthPercent = 1;
        private int _marketProfileOffsetPx = 0;
        private int _marketProfileOpacity = 55;
        private int _marketProfileMaxBinsPerProfile = 50;
        private int _marketProfileMinBinSizeTicks = 20;
        private int _marketProfilePriceStepTicks = 1;
        private int _marketProfileValueAreaPercent = 70;
        private bool _marketProfileExtendLevelsToCurrentDay = true;
        private int _marketProfileVahOffsetTicks;
        private int _marketProfileValOffsetTicks;
        private Color _marketProfileHistogramColor = Color.FromArgb(130, 255, 165, 0);
        private Color _marketProfileInsideVaColor = Color.FromArgb(130, 173, 216, 230);
        private Color _marketProfileContourColor = Color.Gold;
        private int _marketProfileContourWidth = 1;
        private ProfileLineStyle _marketProfileContourLineStyle = ProfileLineStyle.Solid;
        private ProfileLineStyle _marketProfilePocLineStyle = ProfileLineStyle.Solid;
        private ProfileLineStyle _marketProfileVaLineStyle = ProfileLineStyle.Dashed;
        private bool _showProfilePoc = true;
        private bool _showProfileValueArea = true;
        private Color _marketProfilePocColor = Color.Gold;
        private Color _marketProfileVahColor = Color.Lime;
        private Color _marketProfileValColor = Color.Lime;
        private int _marketProfileLineWidth = 2;

        [Display(Name = "Show EMAs", GroupName = GroupEma, Order = 10)]
        public bool ShowEmas
        {
            get => _showEmas;
            set => SetAndRecalculate(ref _showEmas, value);
        }

        [Display(Name = "Show EMA 5", GroupName = GroupEma, Order = 20)]
        public bool ShowEma5
        {
            get => _showEma5;
            set => SetAndRecalculate(ref _showEma5, value);
        }

        [Display(Name = "Show EMA 13", GroupName = GroupEma, Order = 30)]
        public bool ShowEma13
        {
            get => _showEma13;
            set => SetAndRecalculate(ref _showEma13, value);
        }

        [Display(Name = "Show EMA 50", GroupName = GroupEma, Order = 40)]
        public bool ShowEma50
        {
            get => _showEma50;
            set => SetAndRecalculate(ref _showEma50, value);
        }

        [Display(Name = "Show EMA 200", GroupName = GroupEma, Order = 50)]
        public bool ShowEma200
        {
            get => _showEma200;
            set => SetAndRecalculate(ref _showEma200, value);
        }

        [Display(Name = "Show EMA 800", GroupName = GroupEma, Order = 60)]
        public bool ShowEma800
        {
            get => _showEma800;
            set => SetAndRecalculate(ref _showEma800, value);
        }

        [Display(Name = "Show EMA 50 Cloud", GroupName = GroupEma, Order = 70)]
        public bool ShowEmaCloud
        {
            get => _showEmaCloud;
            set => SetAndRecalculate(ref _showEmaCloud, value);
        }

        [Display(Name = "EMA 5 Color", GroupName = GroupEma, Order = 80)]
        public CrossColor Ema5Color
        {
            get => _ema5Color.Convert();
            set => SetAndRecalculate(ref _ema5Color, value.Convert());
        }

        [Display(Name = "EMA 13 Color", GroupName = GroupEma, Order = 90)]
        public CrossColor Ema13Color
        {
            get => _ema13Color.Convert();
            set => SetAndRecalculate(ref _ema13Color, value.Convert());
        }

        [Display(Name = "EMA 50 Color", GroupName = GroupEma, Order = 100)]
        public CrossColor Ema50Color
        {
            get => _ema50Color.Convert();
            set => SetAndRecalculate(ref _ema50Color, value.Convert());
        }

        [Display(Name = "EMA 200 Color", GroupName = GroupEma, Order = 110)]
        public CrossColor Ema200Color
        {
            get => _ema200Color.Convert();
            set => SetAndRecalculate(ref _ema200Color, value.Convert());
        }

        [Display(Name = "EMA 800 Color", GroupName = GroupEma, Order = 120)]
        public CrossColor Ema800Color
        {
            get => _ema800Color.Convert();
            set => SetAndRecalculate(ref _ema800Color, value.Convert());
        }

        [Display(Name = "EMA Cloud Color", GroupName = GroupEma, Order = 130)]
        public CrossColor EmaCloudColor
        {
            get => _emaCloudColor.Convert();
            set => SetAndRecalculate(ref _emaCloudColor, value.Convert());
        }

        [Display(Name = "EMA 5 Width", GroupName = GroupEma, Order = 140)]
        [Range(1, 10)]
        public int Ema5Width
        {
            get => _ema5Width;
            set => SetAndRecalculate(ref _ema5Width, Math.Max(1, value));
        }

        [Display(Name = "EMA 13 Width", GroupName = GroupEma, Order = 150)]
        [Range(1, 10)]
        public int Ema13Width
        {
            get => _ema13Width;
            set => SetAndRecalculate(ref _ema13Width, Math.Max(1, value));
        }

        [Display(Name = "EMA 50 Width", GroupName = GroupEma, Order = 160)]
        [Range(1, 10)]
        public int Ema50Width
        {
            get => _ema50Width;
            set => SetAndRecalculate(ref _ema50Width, Math.Max(1, value));
        }

        [Display(Name = "EMA 200 Width", GroupName = GroupEma, Order = 170)]
        [Range(1, 10)]
        public int Ema200Width
        {
            get => _ema200Width;
            set => SetAndRecalculate(ref _ema200Width, Math.Max(1, value));
        }

        [Display(Name = "EMA 800 Width", GroupName = GroupEma, Order = 180)]
        [Range(1, 10)]
        public int Ema800Width
        {
            get => _ema800Width;
            set => SetAndRecalculate(ref _ema800Width, Math.Max(1, value));
        }

        [Display(Name = "Show PVSRA Candles", GroupName = GroupPvsra, Order = 10)]
        public bool ShowPvsraCandles
        {
            get => _showPvsraCandles;
            set => SetAndRecalculate(ref _showPvsraCandles, value);
        }

        [Display(Name = "Lookback", GroupName = GroupPvsra, Order = 20)]
        [Range(1, 100)]
        public int PvsraLookback
        {
            get => _pvsraLookback;
            set => SetAndRecalculate(ref _pvsraLookback, Math.Max(1, value));
        }

        [Display(Name = "Regular Up Color", GroupName = GroupPvsra, Order = 30)]
        public CrossColor RegularUpColor
        {
            get => _regularUpColor.Convert();
            set => SetAndRecalculate(ref _regularUpColor, value.Convert());
        }

        [Display(Name = "Regular Down Color", GroupName = GroupPvsra, Order = 40)]
        public CrossColor RegularDownColor
        {
            get => _regularDownColor.Convert();
            set => SetAndRecalculate(ref _regularDownColor, value.Convert());
        }

        [Display(Name = "Vector Green Color", GroupName = GroupPvsra, Order = 50)]
        public CrossColor VectorGreenColor
        {
            get => _vectorGreenColor.Convert();
            set => SetAndRecalculate(ref _vectorGreenColor, value.Convert());
        }

        [Display(Name = "Vector Red Color", GroupName = GroupPvsra, Order = 60)]
        public CrossColor VectorRedColor
        {
            get => _vectorRedColor.Convert();
            set => SetAndRecalculate(ref _vectorRedColor, value.Convert());
        }

        [Display(Name = "Vector Blue Color", GroupName = GroupPvsra, Order = 70)]
        public CrossColor VectorBlueColor
        {
            get => _vectorBlueColor.Convert();
            set => SetAndRecalculate(ref _vectorBlueColor, value.Convert());
        }

        [Display(Name = "Vector Violet Color", GroupName = GroupPvsra, Order = 80)]
        public CrossColor VectorVioletColor
        {
            get => _vectorVioletColor.Convert();
            set => SetAndRecalculate(ref _vectorVioletColor, value.Convert());
        }

        [Display(Name = "Use Vector Alerts", GroupName = GroupPvsra, Order = 90)]
        public bool UseVectorAlerts
        {
            get => _useVectorAlerts;
            set => SetAndRecalculate(ref _useVectorAlerts, value);
        }

        [Display(Name = "Vector Alert File", GroupName = GroupPvsra, Order = 100)]
        public string VectorAlertFile
        {
            get => _vectorAlertFile;
            set => SetAndRecalculate(ref _vectorAlertFile, string.IsNullOrWhiteSpace(value) ? "alert1" : value.Trim());
        }

        [Display(Name = "Show Vector Zones", GroupName = GroupPvsra, Order = 110)]
        public bool ShowVectorZones
        {
            get => _showVectorZones;
            set => SetAndRecalculate(ref _showVectorZones, value);
        }

        [Display(Name = "Vector Zones Max", GroupName = GroupPvsra, Order = 120)]
        [Range(1, 2000)]
        public int VectorZonesMax
        {
            get => _vectorZonesMax;
            set => SetAndRecalculate(ref _vectorZonesMax, Math.Max(1, value));
        }

        [Display(Name = "Zone Type", GroupName = GroupPvsra, Order = 130)]
        public ZoneBoundaryType VectorZoneType
        {
            get => _vectorZoneType;
            set => SetAndRecalculate(ref _vectorZoneType, value);
        }

        [Display(Name = "Zone Update Type", GroupName = GroupPvsra, Order = 140)]
        public ZoneBoundaryType VectorZoneUpdateType
        {
            get => _vectorZoneUpdateType;
            set => SetAndRecalculate(ref _vectorZoneUpdateType, value);
        }

        [Display(Name = "Zone Border Width", GroupName = GroupPvsra, Order = 150)]
        [Range(0, 10)]
        public int VectorZoneBorderWidth
        {
            get => _vectorZoneBorderWidth;
            set => SetAndRecalculate(ref _vectorZoneBorderWidth, Math.Max(0, value));
        }

        [Display(Name = "Zone Color Override", GroupName = GroupPvsra, Order = 160)]
        public bool VectorZoneColorOverride
        {
            get => _vectorZoneColorOverride;
            set => SetAndRecalculate(ref _vectorZoneColorOverride, value);
        }

        [Display(Name = "Zone Color", GroupName = GroupPvsra, Order = 170)]
        public CrossColor VectorZoneColor
        {
            get => _vectorZoneColor.Convert();
            set => SetAndRecalculate(ref _vectorZoneColor, value.Convert());
        }

        [Display(Name = "Zone Transparency", GroupName = GroupPvsra, Order = 180)]
        [Range(0, 100)]
        public int VectorZoneTransparency
        {
            get => _vectorZoneTransparency;
            set => SetAndRecalculate(ref _vectorZoneTransparency, Math.Clamp(value, 0, 100));
        }


        [Display(Name = "Show PVSRA Volume Histogram", GroupName = GroupPvsra, Order = 190)]
        public bool ShowPvsraVolumeHistogram
        {
            get => _showPvsraVolumeHistogram;
            set => SetAndRecalculate(ref _showPvsraVolumeHistogram, value);
        }

        [Display(Name = "PVSRA Histogram Height %", GroupName = GroupPvsra, Order = 200)]
        [Range(5, 50)]
        public int PvsraVolumeHistogramHeightPercent
        {
            get => _pvsraVolumeHistogramHeightPercent;
            set => SetAndRecalculate(ref _pvsraVolumeHistogramHeightPercent, Math.Clamp(value, 5, 50));
        }

        [Display(Name = "PVSRA Histogram Opacity %", GroupName = GroupPvsra, Order = 210)]
        [Range(0, 100)]
        public int PvsraVolumeHistogramOpacityPercent
        {
            get => _pvsraVolumeHistogramOpacityPercent;
            set => SetAndRecalculate(ref _pvsraVolumeHistogramOpacityPercent, Math.Clamp(value, 0, 100));
        }
        [Display(Name = "Show YDay Hi/Lo", GroupName = GroupYDay, Order = 10)]
        public bool ShowPreviousDayLevels
        {
            get => _showPreviousDayLevels;
            set => SetAndRecalculate(ref _showPreviousDayLevels, value);
        }

        [Display(Name = "Show Today", GroupName = GroupYDay, Order = 20)]
        public bool ShowYDayToday
        {
            get => _showYDayToday;
            set => SetAndRecalculate(ref _showYDayToday, value);
        }

        [Display(Name = "Show Historical", GroupName = GroupYDay, Order = 30)]
        public bool ShowYDayHistorical
        {
            get => _showYDayHistorical;
            set => SetAndRecalculate(ref _showYDayHistorical, value);
        }

        [Display(Name = "History Days", GroupName = GroupYDay, Order = 40)]
        [Range(1, 200)]
        public int YDayHistoryDays
        {
            get => _yDayHistoryDays;
            set => SetAndRecalculate(ref _yDayHistoryDays, Math.Max(1, value));
        }

        [Display(Name = "YDay Color", GroupName = GroupYDay, Order = 50)]
        public CrossColor PreviousDayLevelsColor
        {
            get => _previousDayLevelsColor.Convert();
            set => SetAndRecalculate(ref _previousDayLevelsColor, value.Convert());
        }

        [Display(Name = "YDay Width", GroupName = GroupYDay, Order = 60)]
        [Range(1, 10)]
        public int PreviousDayLevelsWidth
        {
            get => _previousDayLevelsWidth;
            set => SetAndRecalculate(ref _previousDayLevelsWidth, Math.Max(1, value));
        }

        [Display(Name = "Show ADR High/Low", GroupName = GroupAdr, Order = 10)]
        public bool ShowAdrLevels
        {
            get => _showAdrLevels;
            set => SetAndRecalculate(ref _showAdrLevels, value);
        }

        [Display(Name = "Show Today", GroupName = GroupAdr, Order = 20)]
        public bool ShowAdrToday
        {
            get => _showAdrToday;
            set => SetAndRecalculate(ref _showAdrToday, value);
        }

        [Display(Name = "Show Historical", GroupName = GroupAdr, Order = 30)]
        public bool ShowAdrHistorical
        {
            get => _showAdrHistorical;
            set => SetAndRecalculate(ref _showAdrHistorical, value);
        }

        [Display(Name = "History Days", GroupName = GroupAdr, Order = 40)]
        [Range(1, 200)]
        public int AdrHistoryDays
        {
            get => _adrHistoryDays;
            set => SetAndRecalculate(ref _adrHistoryDays, Math.Max(1, value));
        }

        [Display(Name = "ADR from Daily Open", GroupName = GroupAdr, Order = 50)]
        public bool UseAdrFromDailyOpen
        {
            get => _useAdrFromDailyOpen;
            set => SetAndRecalculate(ref _useAdrFromDailyOpen, value);
        }

        [Display(Name = "ADR Length", GroupName = GroupAdr, Order = 60)]
        [Range(1, 100)]
        public int AdrLength
        {
            get => _adrLength;
            set => SetAndRecalculate(ref _adrLength, Math.Max(1, value));
        }

        [Display(Name = "ADR Color", GroupName = GroupAdr, Order = 70)]
        public CrossColor AdrColor
        {
            get => _adrColor.Convert();
            set => SetAndRecalculate(ref _adrColor, value.Convert());
        }

        [Display(Name = "ADR Width", GroupName = GroupAdr, Order = 80)]
        [Range(1, 10)]
        public int AdrWidth
        {
            get => _adrWidth;
            set => SetAndRecalculate(ref _adrWidth, Math.Max(1, value));
        }

        [Display(Name = "Show ATR High/Low", GroupName = GroupAtr, Order = 10)]
        public bool ShowAtrLevels
        {
            get => _showAtrLevels;
            set => SetAndRecalculate(ref _showAtrLevels, value);
        }

        [Display(Name = "ATR from Daily Open", GroupName = GroupAtr, Order = 20)]
        public bool UseAtrFromDailyOpen
        {
            get => _useAtrFromDailyOpen;
            set => SetAndRecalculate(ref _useAtrFromDailyOpen, value);
        }

        [Display(Name = "ATR Length", GroupName = GroupAtr, Order = 30)]
        [Range(1, 100)]
        public int AtrLength
        {
            get => _atrLength;
            set => SetAndRecalculate(ref _atrLength, Math.Max(1, value));
        }

        [Display(Name = "ATR Color", GroupName = GroupAtr, Order = 40)]
        public CrossColor AtrColor
        {
            get => _atrColor.Convert();
            set => SetAndRecalculate(ref _atrColor, value.Convert());
        }

        [Display(Name = "ATR Width", GroupName = GroupAtr, Order = 50)]
        [Range(1, 10)]
        public int AtrWidth
        {
            get => _atrWidth;
            set => SetAndRecalculate(ref _atrWidth, Math.Max(1, value));
        }

        [Display(Name = "Show Daily Open", GroupName = GroupDailyOpen, Order = 10)]
        public bool ShowDailyOpen
        {
            get => _showDailyOpen;
            set => SetAndRecalculate(ref _showDailyOpen, value);
        }

        [Display(Name = "Show Today", GroupName = GroupDailyOpen, Order = 20)]
        public bool ShowDailyOpenToday
        {
            get => _showDailyOpenToday;
            set => SetAndRecalculate(ref _showDailyOpenToday, value);
        }

        [Display(Name = "Show Historical", GroupName = GroupDailyOpen, Order = 30)]
        public bool ShowDailyOpenHistorical
        {
            get => _showDailyOpenHistorical;
            set => SetAndRecalculate(ref _showDailyOpenHistorical, value);
        }

        [Display(Name = "History Days", GroupName = GroupDailyOpen, Order = 40)]
        [Range(1, 200)]
        public int DailyOpenHistoryDays
        {
            get => _dailyOpenHistoryDays;
            set => SetAndRecalculate(ref _dailyOpenHistoryDays, Math.Max(1, value));
        }

        [Display(Name = "Daily Open Color", GroupName = GroupDailyOpen, Order = 50)]
        public CrossColor DailyOpenColor
        {
            get => _dailyOpenColor.Convert();
            set => SetAndRecalculate(ref _dailyOpenColor, value.Convert());
        }

        [Display(Name = "Daily Open Width", GroupName = GroupDailyOpen, Order = 60)]
        [Range(1, 10)]
        public int DailyOpenWidth
        {
            get => _dailyOpenWidth;
            set => SetAndRecalculate(ref _dailyOpenWidth, Math.Max(1, value));
        }

        [Display(Name = "Show London Box", GroupName = GroupSessions, Order = 10)]
        public bool ShowLondonSession
        {
            get => _showLondonSession;
            set => SetAndRecalculate(ref _showLondonSession, value);
        }

        [Display(Name = "Show NY Box", GroupName = GroupSessions, Order = 20)]
        public bool ShowNewYorkSession
        {
            get => _showNewYorkSession;
            set => SetAndRecalculate(ref _showNewYorkSession, value);
        }

        [Display(Name = "Show Asia Box", GroupName = GroupSessions, Order = 30)]
        public bool ShowAsiaSession
        {
            get => _showAsiaSession;
            set => SetAndRecalculate(ref _showAsiaSession, value);
        }

        [Display(Name = "Show EU Brinks Box", GroupName = GroupSessions, Order = 40)]
        public bool ShowEuBrinksSession
        {
            get => _showEuBrinksSession;
            set => SetAndRecalculate(ref _showEuBrinksSession, value);
        }

        [Display(Name = "Show US Brinks Box", GroupName = GroupSessions, Order = 50)]
        public bool ShowUsBrinksSession
        {
            get => _showUsBrinksSession;
            set => SetAndRecalculate(ref _showUsBrinksSession, value);
        }

        [Display(Name = "Auto DST Adjust", GroupName = GroupSessions, Order = 60)]
        public bool AutoSessionDst
        {
            get => _autoSessionDst;
            set => SetAndRecalculate(ref _autoSessionDst, value);
        }

        [Display(Name = "Show Historical", GroupName = GroupSessions, Order = 70)]
        public bool ShowSessionHistorical
        {
            get => _showSessionHistorical;
            set => SetAndRecalculate(ref _showSessionHistorical, value);
        }

        [Display(Name = "History Days", GroupName = GroupSessions, Order = 80)]
        [Range(1, 200)]
        public int SessionHistoryDays
        {
            get => _sessionHistoryDays;
            set => SetAndRecalculate(ref _sessionHistoryDays, Math.Max(1, value));
        }

        [Display(Name = "London Open", GroupName = GroupSessions, Order = 90)]
        public TimeSpan LondonSessionOpen
        {
            get => _londonSessionOpen;
            set => SetAndRecalculate(ref _londonSessionOpen, NormalizeSessionTime(value));
        }

        [Display(Name = "London Close", GroupName = GroupSessions, Order = 100)]
        public TimeSpan LondonSessionClose
        {
            get => _londonSessionClose;
            set => SetAndRecalculate(ref _londonSessionClose, NormalizeSessionTime(value));
        }

        [Display(Name = "NY Open", GroupName = GroupSessions, Order = 110)]
        public TimeSpan NewYorkSessionOpen
        {
            get => _newYorkSessionOpen;
            set => SetAndRecalculate(ref _newYorkSessionOpen, NormalizeSessionTime(value));
        }

        [Display(Name = "NY Close", GroupName = GroupSessions, Order = 120)]
        public TimeSpan NewYorkSessionClose
        {
            get => _newYorkSessionClose;
            set => SetAndRecalculate(ref _newYorkSessionClose, NormalizeSessionTime(value));
        }

        [Display(Name = "Asia Open", GroupName = GroupSessions, Order = 130)]
        public TimeSpan AsiaSessionOpen
        {
            get => _asiaSessionOpen;
            set => SetAndRecalculate(ref _asiaSessionOpen, NormalizeSessionTime(value));
        }

        [Display(Name = "Asia Close", GroupName = GroupSessions, Order = 140)]
        public TimeSpan AsiaSessionClose
        {
            get => _asiaSessionClose;
            set => SetAndRecalculate(ref _asiaSessionClose, NormalizeSessionTime(value));
        }

        [Display(Name = "EU Brinks Open", GroupName = GroupSessions, Order = 150)]
        public TimeSpan EuBrinksSessionOpen
        {
            get => _euBrinksSessionOpen;
            set => SetAndRecalculate(ref _euBrinksSessionOpen, NormalizeSessionTime(value));
        }

        [Display(Name = "EU Brinks Close", GroupName = GroupSessions, Order = 160)]
        public TimeSpan EuBrinksSessionClose
        {
            get => _euBrinksSessionClose;
            set => SetAndRecalculate(ref _euBrinksSessionClose, NormalizeSessionTime(value));
        }

        [Display(Name = "US Brinks Open", GroupName = GroupSessions, Order = 170)]
        public TimeSpan UsBrinksSessionOpen
        {
            get => _usBrinksSessionOpen;
            set => SetAndRecalculate(ref _usBrinksSessionOpen, NormalizeSessionTime(value));
        }

        [Display(Name = "US Brinks Close", GroupName = GroupSessions, Order = 180)]
        public TimeSpan UsBrinksSessionClose
        {
            get => _usBrinksSessionClose;
            set => SetAndRecalculate(ref _usBrinksSessionClose, NormalizeSessionTime(value));
        }

        [Display(Name = "London Color", GroupName = GroupSessions, Order = 200)]
        public CrossColor LondonSessionColor
        {
            get => _londonSessionColor.Convert();
            set => SetAndRecalculate(ref _londonSessionColor, value.Convert());
        }

        [Display(Name = "NY Color", GroupName = GroupSessions, Order = 210)]
        public CrossColor NewYorkSessionColor
        {
            get => _newYorkSessionColor.Convert();
            set => SetAndRecalculate(ref _newYorkSessionColor, value.Convert());
        }

        [Display(Name = "Asia Color", GroupName = GroupSessions, Order = 220)]
        public CrossColor AsiaSessionColor
        {
            get => _asiaSessionColor.Convert();
            set => SetAndRecalculate(ref _asiaSessionColor, value.Convert());
        }

        [Display(Name = "EU Brinks Color", GroupName = GroupSessions, Order = 230)]
        public CrossColor EuBrinksSessionColor
        {
            get => _euBrinksSessionColor.Convert();
            set => SetAndRecalculate(ref _euBrinksSessionColor, value.Convert());
        }

        [Display(Name = "US Brinks Color", GroupName = GroupSessions, Order = 240)]
        public CrossColor UsBrinksSessionColor
        {
            get => _usBrinksSessionColor.Convert();
            set => SetAndRecalculate(ref _usBrinksSessionColor, value.Convert());
        }

        [Display(Name = "Session Opacity", GroupName = GroupSessions, Order = 250)]
        [Range(1, 10)]
        public int SessionWidth
        {
            get => _sessionWidth;
            set => SetAndRecalculate(ref _sessionWidth, Math.Max(1, value));
        }

        [Display(Name = "Show Weekly Psy", GroupName = GroupPsy, Order = 10)]
        public bool ShowWeeklyPsy
        {
            get => _showWeeklyPsy;
            set => SetAndRecalculate(ref _showWeeklyPsy, value);
        }

        [Display(Name = "Show Current Week", GroupName = GroupPsy, Order = 20)]
        public bool ShowWeeklyPsyToday
        {
            get => _showWeeklyPsyToday;
            set => SetAndRecalculate(ref _showWeeklyPsyToday, value);
        }

        [Display(Name = "Show Historical", GroupName = GroupPsy, Order = 30)]
        public bool ShowWeeklyPsyHistorical
        {
            get => _showWeeklyPsyHistorical;
            set => SetAndRecalculate(ref _showWeeklyPsyHistorical, value);
        }

        [Display(Name = "History Weeks", GroupName = GroupPsy, Order = 40)]
        [Range(1, 52)]
        public int WeeklyPsyHistoryWeeks
        {
            get => _weeklyPsyHistoryWeeks;
            set => SetAndRecalculate(ref _weeklyPsyHistoryWeeks, Math.Max(1, value));
        }

        [Display(Name = "Psy High Color", GroupName = GroupPsy, Order = 50)]
        public CrossColor WeeklyPsyHighColor
        {
            get => _weeklyPsyHighColor.Convert();
            set => SetAndRecalculate(ref _weeklyPsyHighColor, value.Convert());
        }

        [Display(Name = "Psy Low Color", GroupName = GroupPsy, Order = 60)]
        public CrossColor WeeklyPsyLowColor
        {
            get => _weeklyPsyLowColor.Convert();
            set => SetAndRecalculate(ref _weeklyPsyLowColor, value.Convert());
        }

        [Display(Name = "Psy Width", GroupName = GroupPsy, Order = 70)]
        [Range(1, 10)]
        public int WeeklyPsyWidth
        {
            get => _weeklyPsyWidth;
            set => SetAndRecalculate(ref _weeklyPsyWidth, Math.Max(1, value));
        }

        [Display(Name = "Show VWAP", GroupName = GroupVolume, Order = 10)]
        public bool ShowVwap
        {
            get => _showVwap;
            set => SetAndRecalculate(ref _showVwap, value);
        }

        [Display(Name = "VWAP Color", GroupName = GroupVolume, Order = 20)]
        public CrossColor VwapColor
        {
            get => _vwapColor.Convert();
            set => SetAndRecalculate(ref _vwapColor, value.Convert());
        }

        [Display(Name = "VWAP Width", GroupName = GroupVolume, Order = 30)]
        [Range(1, 10)]
        public int VwapWidth
        {
            get => _vwapWidth;
            set => SetAndRecalculate(ref _vwapWidth, Math.Max(1, value));
        }

        [Display(Name = "Show Market Profile", GroupName = GroupVolume, Order = 40)]
        public bool ShowMarketProfile
        {
            get => _showMarketProfile;
            set => SetAndRecalculate(ref _showMarketProfile, value);
        }

        [Display(Name = "Profile History Days", GroupName = GroupVolume, Order = 50)]
        [Range(1, 50)]
        public int MarketProfileHistoryDays
        {
            get => _marketProfileHistoryDays;
            set => SetAndRecalculate(ref _marketProfileHistoryDays, Math.Max(1, value));
        }

        [Display(Name = "Include Current Day", GroupName = GroupVolume, Order = 55)]
        public bool MarketProfileIncludeCurrentDay
        {
            get => _marketProfileIncludeCurrentDay;
            set => SetAndRecalculate(ref _marketProfileIncludeCurrentDay, value);
        }

        [Display(Name = "Ignore Sunday", GroupName = GroupVolume, Order = 56)]
        public bool MarketProfileIgnoreSunday
        {
            get => _marketProfileIgnoreSunday;
            set => SetAndRecalculate(ref _marketProfileIgnoreSunday, value);
        }


        [Display(Name = "Merge Groups (e.g. 2-4;5-8)", GroupName = GroupVolume, Order = 57)]
        public string MarketProfileMergeGroups
        {
            get => _marketProfileMergeGroups;
            set => SetAndRecalculate(ref _marketProfileMergeGroups, value?.Trim() ?? string.Empty);
        }
        [Display(Name = "Profile Direction", GroupName = GroupVolume, Order = 60)]
        public ProfileDirection MarketProfileDirectionMode
        {
            get => _marketProfileDirection;
            set => SetAndRecalculate(ref _marketProfileDirection, value);
        }

        [Display(Name = "Profile Render Mode", GroupName = GroupVolume, Order = 65)]
        public ProfileRenderMode MarketProfileRenderModeSetting
        {
            get => _marketProfileRenderMode;
            set => SetAndRecalculate(ref _marketProfileRenderMode, value);
        }

        [Display(Name = "Profile Width Px", GroupName = GroupVolume, Order = 70)]
        [Range(20, 1200)]
        public int MarketProfileMaxWidthPx
        {
            get => _marketProfileMaxWidthPx;
            set => SetAndRecalculate(ref _marketProfileMaxWidthPx, Math.Max(20, value));
        }

        [Display(Name = "Profile Width %", GroupName = GroupVolume, Order = 75)]
        [Range(1, 95)]
        public int MarketProfileWidthPercent
        {
            get => _marketProfileWidthPercent;
            set => SetAndRecalculate(ref _marketProfileWidthPercent, Math.Clamp(value, 1, 95));
        }

        [Display(Name = "Min Bin Width %", GroupName = GroupVolume, Order = 76)]
        [Range(0, 20)]
        public int MarketProfileMinBinWidthPercent
        {
            get => _marketProfileMinBinWidthPercent;
            set => SetAndRecalculate(ref _marketProfileMinBinWidthPercent, Math.Clamp(value, 0, 20));
        }

        [Display(Name = "Profile X Offset", GroupName = GroupVolume, Order = 80)]
        [Range(-2000, 2000)]
        public int MarketProfileOffsetPx
        {
            get => _marketProfileOffsetPx;
            set => SetAndRecalculate(ref _marketProfileOffsetPx, value);
        }

        [Display(Name = "Outside VA Color", GroupName = GroupVolume, Order = 90)]
        public CrossColor MarketProfileHistogramColor
        {
            get => _marketProfileHistogramColor.Convert();
            set => SetAndRecalculate(ref _marketProfileHistogramColor, value.Convert());
        }

        [Display(Name = "Inside VA Color", GroupName = GroupVolume, Order = 95)]
        public CrossColor MarketProfileInsideVaColor
        {
            get => _marketProfileInsideVaColor.Convert();
            set => SetAndRecalculate(ref _marketProfileInsideVaColor, value.Convert());
        }

        [Display(Name = "Histogram Opacity %", GroupName = GroupVolume, Order = 100)]
        [Range(0, 100)]
        public int MarketProfileOpacity
        {
            get => _marketProfileOpacity;
            set => SetAndRecalculate(ref _marketProfileOpacity, Math.Clamp(value, 0, 100));
        }

        [Display(Name = "Max Bins Per Profile", GroupName = GroupVolume, Order = 105)]
        [Range(10, 400)]
        public int MarketProfileMaxBinsPerProfile
        {
            get => _marketProfileMaxBinsPerProfile;
            set => SetAndRecalculate(ref _marketProfileMaxBinsPerProfile, Math.Clamp(value, 10, 400));
        }

        [Display(Name = "Min Bin Size Ticks", GroupName = GroupVolume, Order = 106)]
        [Range(1, 10000)]
        public int MarketProfileMinBinSizeTicks
        {
            get => _marketProfileMinBinSizeTicks;
            set => SetAndRecalculate(ref _marketProfileMinBinSizeTicks, Math.Max(1, value));
        }

        [Display(Name = "Price Step Ticks", GroupName = GroupVolume, Order = 110)]
        [Range(1, 20)]
        public int MarketProfilePriceStepTicks
        {
            get => _marketProfilePriceStepTicks;
            set => SetAndRecalculate(ref _marketProfilePriceStepTicks, Math.Max(1, value));
        }

        [Display(Name = "Value Area %", GroupName = GroupVolume, Order = 120)]
        [Range(50, 99)]
        public int MarketProfileValueAreaPercent
        {
            get => _marketProfileValueAreaPercent;
            set => SetAndRecalculate(ref _marketProfileValueAreaPercent, Math.Clamp(value, 50, 99));
        }

        [Display(Name = "Extend Levels To Today", GroupName = GroupVolume, Order = 125)]
        public bool MarketProfileExtendLevelsToCurrentDay
        {
            get => _marketProfileExtendLevelsToCurrentDay;
            set => SetAndRecalculate(ref _marketProfileExtendLevelsToCurrentDay, value);
        }

        [Display(Name = "VAH Offset Ticks", GroupName = GroupVolume, Order = 130)]
        [Range(-200, 200)]
        public int MarketProfileVahOffsetTicks
        {
            get => _marketProfileVahOffsetTicks;
            set => SetAndRecalculate(ref _marketProfileVahOffsetTicks, value);
        }

        [Display(Name = "VAL Offset Ticks", GroupName = GroupVolume, Order = 140)]
        [Range(-200, 200)]
        public int MarketProfileValOffsetTicks
        {
            get => _marketProfileValOffsetTicks;
            set => SetAndRecalculate(ref _marketProfileValOffsetTicks, value);
        }

        [Display(Name = "Use Session Filter", GroupName = GroupVolume, Order = 145)]
        public bool MarketProfileUseSessionFilter
        {
            get => _marketProfileUseSessionFilter;
            set => SetAndRecalculate(ref _marketProfileUseSessionFilter, value);
        }

        [Display(Name = "Session Open", GroupName = GroupVolume, Order = 146)]
        public TimeSpan MarketProfileSessionOpen
        {
            get => _marketProfileSessionOpen;
            set => SetAndRecalculate(ref _marketProfileSessionOpen, NormalizeSessionTime(value));
        }

        [Display(Name = "Session Close", GroupName = GroupVolume, Order = 147)]
        public TimeSpan MarketProfileSessionClose
        {
            get => _marketProfileSessionClose;
            set => SetAndRecalculate(ref _marketProfileSessionClose, NormalizeSessionTime(value));
        }

        [Display(Name = "Show POC", GroupName = GroupVolume, Order = 150)]
        public bool ShowProfilePoc
        {
            get => _showProfilePoc;
            set => SetAndRecalculate(ref _showProfilePoc, value);
        }

        [Display(Name = "Show Value Area", GroupName = GroupVolume, Order = 160)]
        public bool ShowProfileValueArea
        {
            get => _showProfileValueArea;
            set => SetAndRecalculate(ref _showProfileValueArea, value);
        }

        [Display(Name = "POC Color", GroupName = GroupVolume, Order = 170)]
        public CrossColor MarketProfilePocColor
        {
            get => _marketProfilePocColor.Convert();
            set => SetAndRecalculate(ref _marketProfilePocColor, value.Convert());
        }

        [Display(Name = "POC Style", GroupName = GroupVolume, Order = 175)]
        public ProfileLineStyle MarketProfilePocStyle
        {
            get => _marketProfilePocLineStyle;
            set => SetAndRecalculate(ref _marketProfilePocLineStyle, value);
        }

        [Display(Name = "VAH Color", GroupName = GroupVolume, Order = 180)]
        public CrossColor MarketProfileVahColor
        {
            get => _marketProfileVahColor.Convert();
            set => SetAndRecalculate(ref _marketProfileVahColor, value.Convert());
        }

        [Display(Name = "VAL Color", GroupName = GroupVolume, Order = 190)]
        public CrossColor MarketProfileValColor
        {
            get => _marketProfileValColor.Convert();
            set => SetAndRecalculate(ref _marketProfileValColor, value.Convert());
        }

        [Display(Name = "VA Style", GroupName = GroupVolume, Order = 195)]
        public ProfileLineStyle MarketProfileVaStyle
        {
            get => _marketProfileVaLineStyle;
            set => SetAndRecalculate(ref _marketProfileVaLineStyle, value);
        }

        [Display(Name = "Profile Line Width", GroupName = GroupVolume, Order = 200)]
        [Range(1, 10)]
        public int MarketProfileLineWidth
        {
            get => _marketProfileLineWidth;
            set => SetAndRecalculate(ref _marketProfileLineWidth, Math.Max(1, value));
        }

        [Display(Name = "Contour Color", GroupName = GroupVolume, Order = 205)]
        public CrossColor MarketProfileContourColor
        {
            get => _marketProfileContourColor.Convert();
            set => SetAndRecalculate(ref _marketProfileContourColor, value.Convert());
        }

        [Display(Name = "Contour Width", GroupName = GroupVolume, Order = 206)]
        [Range(1, 10)]
        public int MarketProfileContourWidth
        {
            get => _marketProfileContourWidth;
            set => SetAndRecalculate(ref _marketProfileContourWidth, Math.Max(1, value));
        }

        [Display(Name = "Contour Style", GroupName = GroupVolume, Order = 207)]
        public ProfileLineStyle MarketProfileContourStyle
        {
            get => _marketProfileContourLineStyle;
            set => SetAndRecalculate(ref _marketProfileContourLineStyle, value);
        }

        public TR_Template()
            : base(true)
        {
            _ema5.VisualType = VisualMode.Line;
            _ema13.VisualType = VisualMode.Line;
            _ema50.VisualType = VisualMode.Line;
            _ema200.VisualType = VisualMode.Line;
            _ema800.VisualType = VisualMode.Line;
            _vwapSeries.VisualType = VisualMode.Line;
            _prevDayHighSeries.VisualType = VisualMode.Line;
            _prevDayLowSeries.VisualType = VisualMode.Line;
            _adrHighSeries.VisualType = VisualMode.Line;
            _adrLowSeries.VisualType = VisualMode.Line;
            _atrHighSeries.VisualType = VisualMode.Line;
            _atrLowSeries.VisualType = VisualMode.Line;
            _dailyOpenSeries.VisualType = VisualMode.Line;
            _londonHighSeries.VisualType = VisualMode.Line;
            _londonLowSeries.VisualType = VisualMode.Line;
            _newYorkHighSeries.VisualType = VisualMode.Line;
            _newYorkLowSeries.VisualType = VisualMode.Line;
            _asiaHighSeries.VisualType = VisualMode.Line;
            _asiaLowSeries.VisualType = VisualMode.Line;
            _psyHighSeries.VisualType = VisualMode.Line;
            _psyLowSeries.VisualType = VisualMode.Line;
            _pvsraBars.IsHidden = true;

            DataSeries[0] = _ema5;
            DataSeries.Add(_ema13);
            DataSeries.Add(_ema50);
            DataSeries.Add(_ema200);
            DataSeries.Add(_ema800);
            DataSeries.Add(_vwapSeries);
            DataSeries.Add(_emaCloud);
            DataSeries.Add(_prevDayHighSeries);
            DataSeries.Add(_prevDayLowSeries);
            DataSeries.Add(_adrHighSeries);
            DataSeries.Add(_adrLowSeries);
            DataSeries.Add(_atrHighSeries);
            DataSeries.Add(_atrLowSeries);
            DataSeries.Add(_dailyOpenSeries);
            DataSeries.Add(_londonHighSeries);
            DataSeries.Add(_londonLowSeries);
            DataSeries.Add(_newYorkHighSeries);
            DataSeries.Add(_newYorkLowSeries);
            DataSeries.Add(_asiaHighSeries);
            DataSeries.Add(_asiaLowSeries);
            DataSeries.Add(_londonSessionBox);
            DataSeries.Add(_newYorkSessionBox);
            DataSeries.Add(_asiaSessionBox);
            DataSeries.Add(_euBrinksSessionBox);
            DataSeries.Add(_usBrinksSessionBox);
            DataSeries.Add(_psyHighSeries);
            DataSeries.Add(_psyLowSeries);
            DataSeries.Add(_pvsraBars);

            DenyToChangePanel = true;
            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final);
            ApplySeriesStyles();
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar == 0)
                ResetState();

            var candle = GetCandle(bar);

            UpdateSessionState(bar, candle);
            CalculateEmas(bar, candle.Close);
            DrawEmaAndCloud(bar);
            DrawVwap(bar, candle);
            UpdateMarketProfiles(bar);
            ColorPvsra(bar, candle);
            DrawDailyLevels(bar);
            DrawDailyOpen(bar);
            DrawSessionLevels(bar, candle);
            DrawWeeklyPsy(bar, candle);
        }

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (ChartInfo is null)
                return;

            DrawVectorZones(context);
            DrawPvsraVolumeHistogram(context);

            if (InstrumentInfo is null || !_showMarketProfile || _volumeProfiles.Count == 0)
                return;

            var leftToRight = _marketProfileDirection == ProfileDirection.LeftToRight;
            var barWidth = Math.Max(1, (int)Math.Round(ChartInfo.PriceChartContainer.BarsWidth));
            var tick = InstrumentInfo.TickSize > 0m ? InstrumentInfo.TickSize : 0.00000001m;
            var lastBarX = ChartInfo.GetXByBar(Math.Max(0, CurrentBar - 1), false) + barWidth;
            var drawHistogram = _marketProfileRenderMode == ProfileRenderMode.Histogram || _marketProfileRenderMode == ProfileRenderMode.Both;
            var drawContour = _marketProfileRenderMode == ProfileRenderMode.Line || _marketProfileRenderMode == ProfileRenderMode.Both;
            var outsideVaColor = ApplyOpacity(_marketProfileHistogramColor, _marketProfileOpacity);
            var insideVaColor = ApplyOpacity(_marketProfileInsideVaColor, _marketProfileOpacity);
            var widthFraction = Math.Clamp(_marketProfileWidthPercent / 100.0, 0.01, 0.95);
            var minFrac = Math.Clamp(_marketProfileMinBinWidthPercent / 100.0, 0.0, 0.20);

            foreach (var profile in _volumeProfiles)
            {
                if (!profile.IsValid || profile.MaxVolume <= 0m || profile.PriceStep <= 0m)
                    continue;

                var profileBars = Math.Max(1, profile.EndBar - profile.StartBar + 1);
                var widthByPercent = Math.Max(1, (int)Math.Round(profileBars * barWidth * widthFraction));
                var profileWidth = Math.Max(20, Math.Min(widthByPercent, _marketProfileMaxWidthPx));
                var anchorBar = leftToRight ? profile.StartBar : profile.EndBar;
                var anchorX = ChartInfo.GetXByBar(anchorBar, false) + (leftToRight ? 0 : barWidth) + _marketProfileOffsetPx;
                var lineX1 = leftToRight ? anchorX : anchorX - profileWidth;
                var lineX2 = leftToRight ? anchorX + profileWidth : anchorX;
                if (_marketProfileExtendLevelsToCurrentDay)
                    lineX2 = Math.Max(lineX2, lastBarX);

                var hasPrevTip = false;
                var prevTipX = 0;
                var prevTipY = 0;

                foreach (var level in profile.Levels)
                {
                    if (level.Value <= 0m)
                        continue;

                    double volumeRatio;
                    if (profile.MaxVolume > profile.MinNonZeroVolume && profile.MinNonZeroVolume > 0m)
                    {
                        volumeRatio = (double)((level.Value - profile.MinNonZeroVolume) / (profile.MaxVolume - profile.MinNonZeroVolume));
                    }
                    else
                    {
                        volumeRatio = (double)(level.Value / profile.MaxVolume);
                    }

                    volumeRatio = Math.Clamp(volumeRatio, 0.0, 1.0);
                    if (minFrac > 0)
                        volumeRatio = minFrac + volumeRatio * (1.0 - minFrac);

                    var width = Math.Max(1, (int)Math.Round(volumeRatio * profileWidth));
                    var y1 = ChartInfo.GetYByPrice(level.Key, false);
                    var y2 = ChartInfo.GetYByPrice(level.Key - profile.PriceStep, false);
                    var top = Math.Min(y1, y2);
                    var height = Math.Max(1, Math.Abs(y2 - y1));
                    var x = leftToRight ? anchorX : anchorX - width;
                    var tipX = leftToRight ? x + width : x;
                    var tipY = top + height / 2;
                    var inValueArea = level.Key >= profile.ValPrice && level.Key <= profile.VahPrice;

                    if (drawHistogram)
                    {
                        var barColor = inValueArea ? insideVaColor : outsideVaColor;
                        context.FillRectangle(barColor, new Rectangle(x, top, width, height));
                    }

                    if (drawContour)
                    {
                        if (hasPrevTip)
                            DrawStyledSegment(context, prevTipX, prevTipY, tipX, tipY, _marketProfileContourColor, _marketProfileContourWidth, _marketProfileContourLineStyle);

                        hasPrevTip = true;
                        prevTipX = tipX;
                        prevTipY = tipY;
                    }
                }

                if (_showProfilePoc)
                {
                    var pocY = ChartInfo.GetYByPrice(profile.PocPrice, false);
                    DrawStyledHorizontalBand(context, lineX1, lineX2, pocY, _marketProfilePocColor, _marketProfileLineWidth, _marketProfilePocLineStyle);
                }

                if (_showProfileValueArea)
                {
                    var vahPrice = profile.VahPrice + _marketProfileVahOffsetTicks * tick;
                    var valPrice = profile.ValPrice + _marketProfileValOffsetTicks * tick;

                    var vahY = ChartInfo.GetYByPrice(vahPrice, false);
                    var valY = ChartInfo.GetYByPrice(valPrice, false);

                    DrawStyledHorizontalBand(context, lineX1, lineX2, vahY, _marketProfileVahColor, _marketProfileLineWidth, _marketProfileVaLineStyle);
                    DrawStyledHorizontalBand(context, lineX1, lineX2, valY, _marketProfileValColor, _marketProfileLineWidth, _marketProfileVaLineStyle);
                }
            }
        }

        private static void DrawHorizontalBand(RenderContext context, int x1, int x2, int y, Color color, int width)
        {
            var left = Math.Min(x1, x2);
            var right = Math.Max(x1, x2);
            var lineWidth = Math.Max(1, width);
            var top = y - lineWidth / 2;
            var rectWidth = Math.Max(1, right - left);
            context.FillRectangle(color, new Rectangle(left, top, rectWidth, lineWidth));
        }

        private static void DrawStyledHorizontalBand(RenderContext context, int x1, int x2, int y, Color color, int width, ProfileLineStyle style)
        {
            if (style == ProfileLineStyle.Solid)
            {
                DrawHorizontalBand(context, x1, x2, y, color, width);
                return;
            }

            var left = Math.Min(x1, x2);
            var right = Math.Max(x1, x2);
            var lineWidth = Math.Max(1, width);
            var top = y - lineWidth / 2;
            var on = style == ProfileLineStyle.Dashed ? 10 : 2;
            var off = style == ProfileLineStyle.Dashed ? 6 : 4;

            for (var x = left; x < right; x += on + off)
            {
                var segment = Math.Min(on, right - x);
                if (segment <= 0)
                    continue;

                context.FillRectangle(color, new Rectangle(x, top, segment, lineWidth));
            }
        }

        private static void DrawStyledSegment(RenderContext context, int x1, int y1, int x2, int y2, Color color, int width, ProfileLineStyle style)
        {
            var lineWidth = Math.Max(1, width);
            var dx = x2 - x1;
            var dy = y2 - y1;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance < 1.0)
            {
                context.FillRectangle(color, new Rectangle(x1 - lineWidth / 2, y1 - lineWidth / 2, lineWidth, lineWidth));
                return;
            }

            var on = style == ProfileLineStyle.Dashed ? 10.0 : style == ProfileLineStyle.Dotted ? 2.0 : distance;
            var off = style == ProfileLineStyle.Dashed ? 6.0 : style == ProfileLineStyle.Dotted ? 4.0 : 0.0;
            var cycle = on + off;

            for (double d = 0; d <= distance; d += 1.0)
            {
                if (style != ProfileLineStyle.Solid && cycle > 0)
                {
                    var phase = d % cycle;
                    if (phase >= on)
                        continue;
                }

                var t = d / distance;
                var px = (int)Math.Round(x1 + dx * t);
                var py = (int)Math.Round(y1 + dy * t);
                context.FillRectangle(color, new Rectangle(px - lineWidth / 2, py - lineWidth / 2, lineWidth, lineWidth));
            }
        }

        private void DrawVectorZones(RenderContext context)
        {
            if (!_showVectorZones)
                return;

            var chart = ChartInfo;
            if (chart?.PriceChartContainer is null)
                return;

            var barWidth = Math.Max(1, (int)Math.Round(chart.PriceChartContainer.BarsWidth));
            DrawVectorZoneList(context, _vectorZonesAbove, barWidth);
            DrawVectorZoneList(context, _vectorZonesBelow, barWidth);
        }


        private void DrawPvsraVolumeHistogram(RenderContext context)
        {
            if (!_showPvsraVolumeHistogram)
                return;

            var chart = ChartInfo;
            if (chart?.PriceChartContainer is null || Container is null)
                return;

            var firstBar = Math.Max(0, FirstVisibleBarNumber);
            var lastBar = Math.Min(LastVisibleBarNumber, CurrentBar - 1);
            if (lastBar < firstBar)
                return;

            decimal maxVolume = 0m;
            for (var i = firstBar; i <= lastBar; i++)
            {
                var volume = GetCandle(i).Volume;
                if (volume > maxVolume)
                    maxVolume = volume;
            }

            if (maxVolume <= 0m)
                return;

            var barsWidth = Math.Max(1, (int)Math.Round(chart.PriceChartContainer.BarsWidth));
            var maxHeight = Container.Region.Height * Math.Clamp(_pvsraVolumeHistogramHeightPercent, 5, 50) / 100;

            for (var i = firstBar; i <= lastBar; i++)
            {
                var candle = GetCandle(i);
                var volume = candle.Volume;
                var barHeight = (int)Math.Round((double)(maxHeight * volume / maxVolume));
                if (barHeight <= 0)
                    continue;

                var baseColor = i < _pvsraComputedColors.Count
                    ? _pvsraComputedColors[i]
                    : (candle.Close >= candle.Open ? _regularUpColor : _regularDownColor);

                var color = ApplyOpacity(baseColor, _pvsraVolumeHistogramOpacityPercent);
                var x = chart.GetXByBar(i, false);
                var y = Container.Region.Bottom - barHeight;
                context.FillRectangle(color, new Rectangle(x, y, barsWidth, barHeight));
            }
        }

        private void EnsurePvsraColorBuffer(int bar)
        {
            while (_pvsraComputedColors.Count <= bar)
                _pvsraComputedColors.Add(_regularDownColor);
        }
        private void DrawVectorZoneList(RenderContext context, List<VectorZone> zones, int barWidth)
        {
            if (zones.Count == 0)
                return;

            var lastBar = Math.Max(0, CurrentBar - 1);

            foreach (var zone in zones)
            {
                var startBar = Math.Clamp(zone.StartBar, 0, lastBar);
                var endBar = Math.Clamp(Math.Max(zone.StartBar, zone.EndBar), 0, lastBar);

                var x1 = ChartInfo!.GetXByBar(startBar, false);
                var x2 = ChartInfo!.GetXByBar(endBar, false) + barWidth;
                var yTop = ChartInfo!.GetYByPrice(zone.Top, false);
                var yBottom = ChartInfo!.GetYByPrice(zone.Bottom, false);

                var left = Math.Min(x1, x2);
                var top = Math.Min(yTop, yBottom);
                var width = Math.Max(1, Math.Abs(x2 - x1));
                var height = Math.Max(1, Math.Abs(yBottom - yTop));
                var rect = new Rectangle(left, top, width, height);

                context.FillRectangle(zone.FillColor, rect);

                if (_vectorZoneBorderWidth > 0)
                    DrawRectangleBorder(context, rect, zone.BorderColor, _vectorZoneBorderWidth);
            }
        }

        private static void DrawRectangleBorder(RenderContext context, Rectangle rect, Color color, int borderWidth)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            var bw = Math.Max(1, borderWidth);
            bw = Math.Min(bw, Math.Max(1, Math.Min(rect.Width, rect.Height)));

            context.FillRectangle(color, new Rectangle(rect.Left, rect.Top, rect.Width, bw));
            context.FillRectangle(color, new Rectangle(rect.Left, rect.Bottom - bw, rect.Width, bw));
            context.FillRectangle(color, new Rectangle(rect.Left, rect.Top, bw, rect.Height));
            context.FillRectangle(color, new Rectangle(rect.Right - bw, rect.Top, bw, rect.Height));
        }

        private void ResetState()
        {
            for (var i = 0; i < DataSeries.Count; i++)
                DataSeries[i].Clear();

            _ema5Calc.Clear();
            _ema13Calc.Clear();
            _ema50Calc.Clear();
            _ema200Calc.Clear();
            _ema800Calc.Clear();

            _closedDailyRanges.Clear();
            _closedDailyTrueRanges.Clear();
            _lastProcessedBar = -1;
            _lastVectorAlertBar = -1;
            _lastVectorZoneCreationBar = -1;
            _hasActiveSession = false;
            _hasPreviousSession = false;
            _hasPreviousCloseForAtr = false;
            _adrHighTouched = false;
            _adrLowTouched = false;
            _sessionDate = DateTime.MinValue;
            _sessionOpen = 0m;
            _sessionHigh = 0m;
            _sessionLow = 0m;
            _sessionClose = 0m;
            _previousSessionHigh = 0m;
            _previousSessionLow = 0m;
            _previousSessionClose = 0m;
            _londonSessionId = DateTime.MinValue;
            _londonSessionStartBar = -1;
            _londonSessionHigh = 0m;
            _londonSessionLow = 0m;
            _newYorkSessionId = DateTime.MinValue;
            _newYorkSessionStartBar = -1;
            _newYorkSessionHigh = 0m;
            _newYorkSessionLow = 0m;
            _asiaSessionId = DateTime.MinValue;
            _asiaSessionStartBar = -1;
            _asiaSessionHigh = 0m;
            _asiaSessionLow = 0m;
            _euBrinksSessionId = DateTime.MinValue;
            _euBrinksSessionStartBar = -1;
            _euBrinksSessionHigh = 0m;
            _euBrinksSessionLow = 0m;
            _usBrinksSessionId = DateTime.MinValue;
            _usBrinksSessionStartBar = -1;
            _usBrinksSessionHigh = 0m;
            _usBrinksSessionLow = 0m;
            _psyWeekKey = int.MinValue;
            _psyHigh = 0m;
            _psyLow = 0m;
            _hasPsyValue = false;
            _vwapSessionDate = DateTime.MinValue;
            _vwapCumulativePriceVolume = 0m;
            _vwapCumulativeVolume = 0m;
            _volumeProfiles.Clear();
            _vectorZonesAbove.Clear();
            _vectorZonesBelow.Clear();
            _pvsraComputedColors.Clear();

            ApplySeriesStyles();
        }

        private void UpdateSessionState(int bar, IndicatorCandle candle)
        {
            var isNewBar = bar != _lastProcessedBar;

            if (!_hasActiveSession)
            {
                StartNewSession(candle);
                _lastProcessedBar = bar;
                return;
            }

            if (!isNewBar)
            {
                UpdateSessionExtremes(candle);
                return;
            }

            if (IsNewDailyBar(candle))
            {
                ClosePreviousSession();
                StartNewSession(candle);
            }
            else
            {
                UpdateSessionExtremes(candle);
            }

            _lastProcessedBar = bar;
        }

        private void StartNewSession(IndicatorCandle candle)
        {
            _sessionDate = candle.Time.Date;
            _sessionOpen = candle.Open;
            _sessionHigh = candle.High;
            _sessionLow = candle.Low;
            _sessionClose = candle.Close;
            _adrHighTouched = false;
            _adrLowTouched = false;
            _hasActiveSession = true;
        }

        private void UpdateSessionExtremes(IndicatorCandle candle)
        {
            if (candle.High > _sessionHigh)
                _sessionHigh = candle.High;

            if (candle.Low < _sessionLow)
                _sessionLow = candle.Low;

            _sessionClose = candle.Close;
        }

        private bool IsNewDailyBar(IndicatorCandle candle)
        {
            if (_sessionDate == DateTime.MinValue)
                return false;

            return candle.Time.Date != _sessionDate;
        }

        private void ClosePreviousSession()
        {
            _previousSessionHigh = _sessionHigh;
            _previousSessionLow = _sessionLow;
            _hasPreviousSession = true;

            var range = _sessionHigh - _sessionLow;
            if (range > 0m)
            {
                _closedDailyRanges.Add(range);
                if (_closedDailyRanges.Count > MaxStoredRanges)
                    _closedDailyRanges.RemoveAt(0);
            }

            var atrRange = range;
            if (_hasPreviousCloseForAtr)
            {
                var highToPrevClose = Math.Abs(_sessionHigh - _previousSessionClose);
                var lowToPrevClose = Math.Abs(_sessionLow - _previousSessionClose);
                atrRange = Math.Max(atrRange, Math.Max(highToPrevClose, lowToPrevClose));
            }

            if (atrRange > 0m)
            {
                _closedDailyTrueRanges.Add(atrRange);
                if (_closedDailyTrueRanges.Count > MaxStoredRanges)
                    _closedDailyTrueRanges.RemoveAt(0);
            }

            _previousSessionClose = _sessionClose;
            _hasPreviousCloseForAtr = true;
        }

        private void CalculateEmas(int bar, decimal close)
        {
            CalculateSingleEma(_ema5Calc, bar, close, Ema5Period);
            CalculateSingleEma(_ema13Calc, bar, close, Ema13Period);
            CalculateSingleEma(_ema50Calc, bar, close, Ema50Period);
            CalculateSingleEma(_ema200Calc, bar, close, Ema200Period);
            CalculateSingleEma(_ema800Calc, bar, close, Ema800Period);
        }

        private static void CalculateSingleEma(ValueDataSeries storage, int bar, decimal close, int period)
        {
            if (bar == 0)
            {
                storage[bar] = close;
                return;
            }

            var k = 2m / (period + 1m);
            storage[bar] = close * k + storage[bar - 1] * (1m - k);
        }

        private void DrawEmaAndCloud(int bar)
        {
            ApplySeriesStyles();

            _ema5[bar] = _ema5Calc[bar];
            _ema13[bar] = _ema13Calc[bar];
            _ema50[bar] = _ema50Calc[bar];
            _ema200[bar] = _ema200Calc[bar];
            _ema800[bar] = _ema800Calc[bar];

            if (_showEmas && _showEmaCloud)
            {
                var stdev = CalculateStdDevClose(bar, EmaCloudStdevPeriod);
                var cloudSize = stdev / 4m;
                var ema50 = _ema50Calc[bar];

                _emaCloud[bar].Upper = ema50 + cloudSize;
                _emaCloud[bar].Lower = ema50 - cloudSize;
            }
            else
            {
                _emaCloud[bar].Upper = 0m;
                _emaCloud[bar].Lower = 0m;
            }
        }

        private void DrawVwap(int bar, IndicatorCandle candle)
        {
            if (bar == 0 || candle.Time.Date != _vwapSessionDate)
            {
                _vwapSessionDate = candle.Time.Date;
                _vwapCumulativePriceVolume = 0m;
                _vwapCumulativeVolume = 0m;
            }

            var typicalPrice = (candle.High + candle.Low + candle.Close) / 3m;
            _vwapCumulativePriceVolume += typicalPrice * candle.Volume;
            _vwapCumulativeVolume += candle.Volume;

            if (_showVwap && _vwapCumulativeVolume > 0m)
                _vwapSeries[bar] = _vwapCumulativePriceVolume / _vwapCumulativeVolume;
            else
                _vwapSeries[bar] = 0m;
        }

        private void UpdateMarketProfiles(int bar)
        {
            if (!_showMarketProfile)
            {
                _volumeProfiles.Clear();
                return;
            }

            if (bar != CurrentBar - 1)
                return;

            _volumeProfiles.Clear();

            var profileDays = Math.Max(1, _marketProfileHistoryDays);
            var endBar = bar;
            var created = 0;
            var latestDate = GetCandle(bar).Time.Date;
            var dayRanges = new List<(DateTime Date, int StartBar, int EndBar)>();

            while (endBar >= 0 && created < profileDays)
            {
                var date = GetCandle(endBar).Time.Date;
                var startBar = endBar;

                while (startBar > 0 && GetCandle(startBar - 1).Time.Date == date)
                    startBar--;

                if ((!_marketProfileIncludeCurrentDay && date == latestDate) ||
                    (_marketProfileIgnoreSunday && date.DayOfWeek == DayOfWeek.Sunday))
                {
                    endBar = startBar - 1;
                    continue;
                }

                dayRanges.Add((date, startBar, endBar));
                created++;
                endBar = startBar - 1;
            }

            foreach (var range in BuildMergedProfileRanges(dayRanges))
            {
                var profile = BuildDayVolumeProfile(range.Date, range.StartBar, range.EndBar);
                if (profile.IsValid)
                    _volumeProfiles.Add(profile);
            }
        }

        private List<(DateTime Date, int StartBar, int EndBar)> BuildMergedProfileRanges(List<(DateTime Date, int StartBar, int EndBar)> dayRanges)
        {
            var result = new List<(DateTime Date, int StartBar, int EndBar)>();
            var count = dayRanges.Count;
            if (count == 0)
                return result;

            var groups = ParseMergeGroups(_marketProfileMergeGroups, count);
            var assigned = new bool[count];

            foreach (var group in groups)
            {
                var from = Math.Clamp(group.From, 1, count);
                var to = Math.Clamp(group.To, 1, count);
                if (from > to)
                    (from, to) = (to, from);

                var overlap = false;
                for (var i = from; i <= to; i++)
                {
                    if (assigned[i - 1])
                    {
                        overlap = true;
                        break;
                    }
                }

                if (overlap)
                    continue;

                var newest = dayRanges[from - 1];
                var oldest = dayRanges[to - 1];
                result.Add((newest.Date, oldest.StartBar, newest.EndBar));

                for (var i = from; i <= to; i++)
                    assigned[i - 1] = true;
            }

            for (var i = 0; i < count; i++)
            {
                if (!assigned[i])
                    result.Add(dayRanges[i]);
            }

            result.Sort((a, b) => b.EndBar.CompareTo(a.EndBar));
            return result;
        }

        private static List<(int From, int To)> ParseMergeGroups(string text, int maxIndex)
        {
            var result = new List<(int From, int To)>();
            if (string.IsNullOrWhiteSpace(text) || maxIndex <= 0)
                return result;

            var tokens = text.Replace(" ", string.Empty)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (token.Contains('-'))
                {
                    var parts = token.Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2)
                        continue;

                    if (!int.TryParse(parts[0], out var from) || !int.TryParse(parts[1], out var to))
                        continue;

                    result.Add((from, to));
                }
                else
                {
                    if (!int.TryParse(token, out var dayIndex))
                        continue;

                    result.Add((dayIndex, dayIndex));
                }
            }

            return result;
        }

        private DayVolumeProfile BuildDayVolumeProfile(DateTime date, int startBar, int endBar)
        {
            var profile = new DayVolumeProfile
            {
                Date = date,
                StartBar = startBar,
                EndBar = endBar
            };

            var tick = InstrumentInfo?.TickSize ?? 0m;
            if (tick <= 0m)
                return profile;

            var found = false;
            var profileLow = decimal.MaxValue;
            var profileHigh = decimal.MinValue;

            for (var i = startBar; i <= endBar; i++)
            {
                var candle = GetCandle(i);
                if (!IsMarketProfileCandleIncluded(candle))
                    continue;

                found = true;
                if (candle.Low < profileLow)
                    profileLow = candle.Low;

                if (candle.High > profileHigh)
                    profileHigh = candle.High;
            }

            if (!found || profileHigh <= profileLow)
                return profile;

            var step = GetAdaptiveProfilePriceStep(profileLow, profileHigh, tick);
            if (step <= 0m)
                return profile;

            profile.PriceStep = step;

            for (var i = startBar; i <= endBar; i++)
            {
                var candle = GetCandle(i);
                if (!IsMarketProfileCandleIncluded(candle))
                    continue;

                foreach (var level in candle.GetAllPriceLevels())
                {
                    if (level is null || level.Volume <= 0m)
                        continue;

                    var price = NormalizePriceToStep(level.Price, step);
                    if (profile.Levels.TryGetValue(price, out var volume))
                        profile.Levels[price] = volume + level.Volume;
                    else
                        profile.Levels[price] = level.Volume;
                }
            }

            if (profile.Levels.Count == 0)
                return profile;

            profile.MaxVolume = profile.Levels.Values.Max();
            profile.MinNonZeroVolume = profile.Levels.Values.Where(v => v > 0m).DefaultIfEmpty(profile.MaxVolume).Min();
            if (profile.MaxVolume <= 0m)
                return profile;

            var poc = profile.Levels.OrderByDescending(x => x.Value).ThenBy(x => x.Key).First();
            profile.PocPrice = poc.Key;

            CalculateValueArea(profile.Levels, profile.PocPrice, _marketProfileValueAreaPercent, out var vah, out var val);
            profile.VahPrice = vah;
            profile.ValPrice = val;
            profile.IsValid = true;

            return profile;
        }

        private static void CalculateValueArea(
            SortedDictionary<decimal, decimal> levels,
            decimal pocPrice,
            int valueAreaPercent,
            out decimal vah,
            out decimal val)
        {
            vah = 0m;
            val = 0m;

            if (levels.Count == 0)
                return;

            var ordered = levels.ToList();
            var totalVolume = ordered.Sum(x => x.Value);
            if (totalVolume <= 0m)
            {
                val = ordered[0].Key;
                vah = ordered[^1].Key;
                return;
            }

            var targetVolume = totalVolume * Math.Clamp(valueAreaPercent, 1, 100) / 100m;
            var pocIndex = ordered.FindIndex(x => x.Key == pocPrice);
            if (pocIndex < 0)
                pocIndex = 0;

            var low = pocIndex;
            var high = pocIndex;
            var accumulated = ordered[pocIndex].Value;

            while (accumulated < targetVolume && (low > 0 || high < ordered.Count - 1))
            {
                var downVolume = low > 0 ? ordered[low - 1].Value : -1m;
                var upVolume = high < ordered.Count - 1 ? ordered[high + 1].Value : -1m;

                if (upVolume >= downVolume && high < ordered.Count - 1)
                {
                    high++;
                    accumulated += ordered[high].Value;
                }
                else if (low > 0)
                {
                    low--;
                    accumulated += ordered[low].Value;
                }
                else if (high < ordered.Count - 1)
                {
                    high++;
                    accumulated += ordered[high].Value;
                }
                else
                {
                    break;
                }
            }

            val = ordered[low].Key;
            vah = ordered[high].Key;
        }

        private decimal GetAdaptiveProfilePriceStep(decimal profileLow, decimal profileHigh, decimal tick)
        {
            if (tick <= 0m)
                return 0m;

            var minStep = tick * Math.Max(1, _marketProfileMinBinSizeTicks);
            var range = Math.Max(0m, profileHigh - profileLow);
            if (range <= 0m)
                return minStep;

            var targetBins = Math.Max(10, _marketProfileMaxBinsPerProfile);
            var adaptive = range / targetBins;
            var step = Math.Max(minStep, adaptive);
            step = decimal.Ceiling(step / tick) * tick;

            var stepByTicks = tick * Math.Max(1, _marketProfilePriceStepTicks);
            return Math.Max(step, stepByTicks);
        }

        private static decimal NormalizePriceToStep(decimal price, decimal step)
        {
            if (step <= 0m)
                return price;

            var ticks = Math.Round(price / step, MidpointRounding.AwayFromZero);
            return ticks * step;
        }

        private bool IsMarketProfileCandleIncluded(IndicatorCandle candle)
        {
            if (!_marketProfileUseSessionFilter)
                return true;

            return IsTimeInSession(candle.Time.TimeOfDay, _marketProfileSessionOpen, _marketProfileSessionClose);
        }

        private static bool IsTimeInSession(TimeSpan current, TimeSpan start, TimeSpan end)
        {
            if (start == end)
                return true;

            if (start < end)
                return current >= start && current < end;

            return current >= start || current < end;
        }

        private void ColorPvsra(int bar, IndicatorCandle candle)
        {
            var avgVolume = AverageVolume(bar, _pvsraLookback);
            var isBull = candle.Close >= candle.Open;
            var hasAverage = avgVolume > 0m;
            var isVector = false;
            var isBlueViolet = false;
            Color color;

            if (!hasAverage)
            {
                color = isBull ? _regularUpColor : _regularDownColor;
            }
            else
            {
                var spread = Math.Max(0m, candle.High - candle.Low);
                var spreadVolume = spread * candle.Volume;
                var highestSpreadVolume = HighestSpreadVolume(bar, _pvsraLookback);

                isVector = candle.Volume >= avgVolume * 2m || spreadVolume >= highestSpreadVolume;
                isBlueViolet = candle.Volume >= avgVolume * 1.5m;

                if (isVector)
                    color = isBull ? _vectorGreenColor : _vectorRedColor;
                else if (isBlueViolet)
                    color = isBull ? _vectorBlueColor : _vectorVioletColor;
                else
                    color = isBull ? _regularUpColor : _regularDownColor;
            }

            _pvsraBars[bar] = _showPvsraCandles ? color.Convert() : null;
            EnsurePvsraColorBuffer(bar);
            _pvsraComputedColors[bar] = color;
            UpdateVectorZones(bar, candle, hasAverage && (isVector || isBlueViolet), isBull, color);

            if (isVector)
                TryAlertVectorCandle(bar, isBull);
        }

        private void UpdateVectorZones(int bar, IndicatorCandle candle, bool isPvsraZoneCandidate, bool isBull, Color pvsraColor)
        {
            UpdateExistingVectorZones(_vectorZonesAbove, bar, candle);
            UpdateExistingVectorZones(_vectorZonesBelow, bar, candle);

            if (!_showVectorZones || !isPvsraZoneCandidate || bar == _lastVectorZoneCreationBar)
                return;

            if (!TryGetZoneRange(candle, _vectorZoneType, out var top, out var bottom))
                return;

            var fillColor = ResolveZoneFillColor(pvsraColor);
            var borderColor = ResolveZoneBorderColor(fillColor);

            var zone = new VectorZone
            {
                StartBar = bar,
                EndBar = bar,
                CreatedBar = bar,
                Top = top,
                Bottom = bottom,
                FillColor = fillColor,
                BorderColor = borderColor
            };

            var targetZones = isBull ? _vectorZonesBelow : _vectorZonesAbove;
            targetZones.Add(zone);
            if (targetZones.Count > _vectorZonesMax)
                targetZones.RemoveAt(0);

            _lastVectorZoneCreationBar = bar;
        }

        private void UpdateExistingVectorZones(List<VectorZone> zones, int bar, IndicatorCandle candle)
        {
            if (zones.Count == 0 || !TryGetZoneRange(candle, _vectorZoneUpdateType, out var testTop, out var testBottom))
                return;

            for (var i = zones.Count - 1; i >= 0; i--)
            {
                var zone = zones[i];
                zone.EndBar = bar;

                if (bar <= zone.CreatedBar)
                    continue;

                var intersects = testTop >= zone.Bottom && testBottom <= zone.Top;
                if (intersects)
                    zones.RemoveAt(i);
            }
        }

        private static bool TryGetZoneRange(IndicatorCandle candle, ZoneBoundaryType boundaryType, out decimal top, out decimal bottom)
        {
            if (boundaryType == ZoneBoundaryType.BodyWithWicks)
            {
                top = candle.High;
                bottom = candle.Low;
            }
            else
            {
                top = Math.Max(candle.Open, candle.Close);
                bottom = Math.Min(candle.Open, candle.Close);
            }

            return top > bottom;
        }

        private Color ResolveZoneFillColor(Color pvsraColor)
        {
            if (_vectorZoneColorOverride)
                return _vectorZoneColor;

            return ApplyTradingViewTransparency(pvsraColor, _vectorZoneTransparency);
        }

        private static Color ResolveZoneBorderColor(Color fillColor)
        {
            return Color.FromArgb(Math.Max((int)fillColor.A, 170), fillColor.R, fillColor.G, fillColor.B);
        }

        private void TryAlertVectorCandle(int bar, bool isBull)
        {
            if (!_useVectorAlerts || bar != CurrentBar - 1 || bar == _lastVectorAlertBar)
                return;

            var direction = isBull ? "bullish" : "bearish";
            var file = string.IsNullOrWhiteSpace(_vectorAlertFile) ? "alert1" : _vectorAlertFile;
            AddAlert(file, $"PVSRA vector candle ({direction})");
            _lastVectorAlertBar = bar;
        }

        private void DrawDailyLevels(int bar)
        {
            var dayAge = GetDayAge(bar);
            var showYDayBar = ShouldRenderByAge(dayAge, _showYDayToday, _showYDayHistorical, _yDayHistoryDays);
            var showAdrBar = ShouldRenderByAge(dayAge, _showAdrToday, _showAdrHistorical, _adrHistoryDays);

            if (_showPreviousDayLevels && _hasPreviousSession && showYDayBar)
            {
                _prevDayHighSeries[bar] = _previousSessionHigh;
                _prevDayLowSeries[bar] = _previousSessionLow;
            }
            else
            {
                _prevDayHighSeries[bar] = 0m;
                _prevDayLowSeries[bar] = 0m;
            }

            var adr = AverageRange(_adrLength);
            if (_showAdrLevels && _hasActiveSession && adr > 0m && showAdrBar)
            {
                if (_useAdrFromDailyOpen)
                {
                    _adrHighSeries[bar] = _sessionOpen + adr;
                    _adrLowSeries[bar] = _sessionOpen - adr;
                }
                else
                {
                    _adrHighSeries[bar] = _sessionLow + adr;
                    _adrLowSeries[bar] = _sessionHigh - adr;
                }

                var candle = GetCandle(bar);
                if (!_adrHighTouched && candle.High >= _adrHighSeries[bar])
                    _adrHighTouched = true;

                if (!_adrLowTouched && candle.Low <= _adrLowSeries[bar])
                    _adrLowTouched = true;

                _adrHighSeries.Colors[bar] = _adrHighTouched ? Color.Red : _adrColor;
                _adrLowSeries.Colors[bar] = _adrLowTouched ? Color.Red : _adrColor;
            }
            else
            {
                _adrHighSeries[bar] = 0m;
                _adrLowSeries[bar] = 0m;
                _adrHighSeries.Colors[bar] = _adrColor;
                _adrLowSeries.Colors[bar] = _adrColor;
            }

            var atr = AverageTrueRange(_atrLength);
            if (_showAtrLevels && _hasActiveSession && atr > 0m)
            {
                if (_useAtrFromDailyOpen)
                {
                    _atrHighSeries[bar] = _sessionOpen + atr;
                    _atrLowSeries[bar] = _sessionOpen - atr;
                }
                else
                {
                    _atrHighSeries[bar] = _sessionLow + atr;
                    _atrLowSeries[bar] = _sessionHigh - atr;
                }
            }
            else
            {
                _atrHighSeries[bar] = 0m;
                _atrLowSeries[bar] = 0m;
            }
        }

        private void DrawDailyOpen(int bar)
        {
            var dayAge = GetDayAge(bar);
            var showDoBar = ShouldRenderByAge(dayAge, _showDailyOpenToday, _showDailyOpenHistorical, _dailyOpenHistoryDays);

            if (_showDailyOpen && _hasActiveSession && showDoBar)
                _dailyOpenSeries[bar] = _sessionOpen;
            else
                _dailyOpenSeries[bar] = 0m;
        }

        private void DrawSessionLevels(int bar, IndicatorCandle candle)
        {
            // Keep legacy hi/lo line series empty; sessions are rendered as boxes.
            _londonHighSeries[bar] = 0m;
            _londonLowSeries[bar] = 0m;
            _newYorkHighSeries[bar] = 0m;
            _newYorkLowSeries[bar] = 0m;
            _asiaHighSeries[bar] = 0m;
            _asiaLowSeries[bar] = 0m;

            var dayAge = GetDayAge(bar);
            var showSessionBar = dayAge == 0 || (_showSessionHistorical && dayAge <= _sessionHistoryDays);
            var ukDst = _autoSessionDst && IsUkDst(candle.Time);
            var usDst = _autoSessionDst && IsUsDst(candle.Time);

            var londonOpen = ApplyDstShift(_londonSessionOpen, ukDst);
            var londonClose = ApplyDstShift(_londonSessionClose, ukDst);
            var newYorkOpen = ApplyDstShift(_newYorkSessionOpen, usDst);
            var newYorkClose = ApplyDstShift(_newYorkSessionClose, usDst);
            var asiaOpen = _asiaSessionOpen;
            var asiaClose = _asiaSessionClose;
            var euBrinksOpen = ApplyDstShift(_euBrinksSessionOpen, ukDst);
            var euBrinksClose = ApplyDstShift(_euBrinksSessionClose, ukDst);
            var usBrinksOpen = ApplyDstShift(_usBrinksSessionOpen, usDst);
            var usBrinksClose = ApplyDstShift(_usBrinksSessionClose, usDst);

            DrawSingleSessionBox(
                bar,
                candle,
                _showLondonSession && showSessionBar,
                londonOpen,
                londonClose,
                ref _londonSessionId,
                ref _londonSessionStartBar,
                ref _londonSessionHigh,
                ref _londonSessionLow,
                _londonSessionBox);

            DrawSingleSessionBox(
                bar,
                candle,
                _showNewYorkSession && showSessionBar,
                newYorkOpen,
                newYorkClose,
                ref _newYorkSessionId,
                ref _newYorkSessionStartBar,
                ref _newYorkSessionHigh,
                ref _newYorkSessionLow,
                _newYorkSessionBox);

            DrawSingleSessionBox(
                bar,
                candle,
                _showAsiaSession && showSessionBar,
                asiaOpen,
                asiaClose,
                ref _asiaSessionId,
                ref _asiaSessionStartBar,
                ref _asiaSessionHigh,
                ref _asiaSessionLow,
                _asiaSessionBox);

            DrawSingleSessionBox(
                bar,
                candle,
                _showEuBrinksSession && showSessionBar,
                euBrinksOpen,
                euBrinksClose,
                ref _euBrinksSessionId,
                ref _euBrinksSessionStartBar,
                ref _euBrinksSessionHigh,
                ref _euBrinksSessionLow,
                _euBrinksSessionBox);

            DrawSingleSessionBox(
                bar,
                candle,
                _showUsBrinksSession && showSessionBar,
                usBrinksOpen,
                usBrinksClose,
                ref _usBrinksSessionId,
                ref _usBrinksSessionStartBar,
                ref _usBrinksSessionHigh,
                ref _usBrinksSessionLow,
                _usBrinksSessionBox);
        }

        private static void DrawSingleSessionBox(
            int bar,
            IndicatorCandle candle,
            bool enabled,
            TimeSpan startTime,
            TimeSpan endTime,
            ref DateTime sessionId,
            ref int sessionStartBar,
            ref decimal sessionHigh,
            ref decimal sessionLow,
            RangeDataSeries sessionBox)
        {
            if (!enabled)
            {
                sessionBox[bar].Upper = 0m;
                sessionBox[bar].Lower = 0m;
                return;
            }

            if (!TryGetSessionStart(candle.Time, startTime, endTime, out var currentSessionId))
            {
                sessionBox[bar].Upper = 0m;
                sessionBox[bar].Lower = 0m;
                return;
            }

            if (currentSessionId != sessionId)
            {
                sessionId = currentSessionId;
                sessionStartBar = bar;
                sessionHigh = candle.High;
                sessionLow = candle.Low;

                sessionBox[bar].Upper = sessionHigh;
                sessionBox[bar].Lower = sessionLow;
                return;
            }

            var highUpdated = false;
            var lowUpdated = false;

            if (candle.High > sessionHigh)
            {
                sessionHigh = candle.High;
                highUpdated = true;
            }

            if (candle.Low < sessionLow)
            {
                sessionLow = candle.Low;
                lowUpdated = true;
            }

            if (highUpdated || lowUpdated)
            {
                var startBar = Math.Max(0, sessionStartBar);
                for (var i = startBar; i <= bar; i++)
                {
                    sessionBox[i].Upper = sessionHigh;
                    sessionBox[i].Lower = sessionLow;
                }

                return;
            }

            sessionBox[bar].Upper = sessionHigh;
            sessionBox[bar].Lower = sessionLow;
        }

        private static bool TryGetSessionStart(
            DateTime barTime,
            TimeSpan startTime,
            TimeSpan endTime,
            out DateTime sessionStart)
        {
            sessionStart = DateTime.MinValue;

            var date = barTime.Date;
            var startToday = date.Add(startTime);
            var endToday = date.Add(endTime);
            var crossesMidnight = endToday <= startToday;

            if (!crossesMidnight)
            {
                if (barTime >= startToday && barTime < endToday)
                {
                    sessionStart = startToday;
                    return true;
                }

                return false;
            }

            var endNextDay = endToday.AddDays(1);
            if (barTime >= startToday && barTime < endNextDay)
            {
                sessionStart = startToday;
                return true;
            }

            var startPrevDay = startToday.AddDays(-1);
            var endPrevDay = endToday;
            if (barTime >= startPrevDay && barTime < endPrevDay)
            {
                sessionStart = startPrevDay;
                return true;
            }

            return false;
        }

        private void DrawWeeklyPsy(int bar, IndicatorCandle candle)
        {
            if (!_showWeeklyPsy)
            {
                _psyHighSeries[bar] = 0m;
                _psyLowSeries[bar] = 0m;
                return;
            }

            var weekKey = GetWeekKey(candle.Time.Date);
            if (weekKey != _psyWeekKey)
            {
                _psyWeekKey = weekKey;
                _psyHigh = 0m;
                _psyLow = 0m;
                _hasPsyValue = false;
            }

            var weekStart = StartOfWeek(candle.Time.Date, DayOfWeek.Monday);
            var psyStart = weekStart;
            var psyEnd = weekStart.AddHours(8);

            if (candle.Time >= psyStart && candle.Time < psyEnd)
            {
                if (!_hasPsyValue)
                {
                    _psyHigh = candle.High;
                    _psyLow = candle.Low;
                    _hasPsyValue = true;
                }
                else
                {
                    if (candle.High > _psyHigh)
                        _psyHigh = candle.High;

                    if (candle.Low < _psyLow)
                        _psyLow = candle.Low;
                }
            }

            var weekAge = GetWeekAge(bar);
            var showPsyBar = ShouldRenderByAge(weekAge, _showWeeklyPsyToday, _showWeeklyPsyHistorical, _weeklyPsyHistoryWeeks);

            if (_hasPsyValue && showPsyBar && candle.Time >= psyStart)
            {
                _psyHighSeries[bar] = _psyHigh;
                _psyLowSeries[bar] = _psyLow;
            }
            else
            {
                _psyHighSeries[bar] = 0m;
                _psyLowSeries[bar] = 0m;
            }
        }

        private decimal AverageRange(int length)
        {
            if (_closedDailyRanges.Count == 0 || length <= 0)
                return 0m;

            var count = Math.Min(length, _closedDailyRanges.Count);
            return _closedDailyRanges.Skip(_closedDailyRanges.Count - count).Average();
        }

        private decimal AverageTrueRange(int length)
        {
            if (_closedDailyTrueRanges.Count == 0 || length <= 0)
                return 0m;

            var count = Math.Min(length, _closedDailyTrueRanges.Count);
            return _closedDailyTrueRanges.Skip(_closedDailyTrueRanges.Count - count).Average();
        }

        private bool IsFirstBarOfDay(int bar)
        {
            if (bar <= 0)
                return true;

            return GetCandle(bar).Time.Date != GetCandle(bar - 1).Time.Date;
        }

        private bool IsFirstBarOfWeek(int bar)
        {
            if (bar <= 0)
                return true;

            var currentWeek = StartOfWeek(GetCandle(bar).Time.Date, DayOfWeek.Monday);
            var prevWeek = StartOfWeek(GetCandle(bar - 1).Time.Date, DayOfWeek.Monday);
            return currentWeek != prevWeek;
        }

        private int GetDayAge(int bar)
        {
            if (bar < 0)
                return 0;

            var sourceCount = (SourceDataSeries as IDataSeries)?.Count ?? 0;
            var lastBarIndex = sourceCount > 0 ? sourceCount - 1 : Math.Max(0, CurrentBar - 1);

            var latestDate = GetCandle(lastBarIndex).Time.Date;
            var barDate = GetCandle(bar).Time.Date;
            var age = (latestDate - barDate).Days;
            return age < 0 ? 0 : age;
        }

        private int GetWeekAge(int bar)
        {
            if (bar < 0)
                return 0;

            var sourceCount = (SourceDataSeries as IDataSeries)?.Count ?? 0;
            var lastBarIndex = sourceCount > 0 ? sourceCount - 1 : Math.Max(0, CurrentBar - 1);

            var latestWeekStart = StartOfWeek(GetCandle(lastBarIndex).Time.Date, DayOfWeek.Monday);
            var barWeekStart = StartOfWeek(GetCandle(bar).Time.Date, DayOfWeek.Monday);
            var age = (int)((latestWeekStart - barWeekStart).TotalDays / 7d);
            return age < 0 ? 0 : age;
        }

        private static int GetWeekKey(DateTime date)
        {
            var weekStart = StartOfWeek(date, DayOfWeek.Monday);
            return weekStart.Year * 10000 + weekStart.Month * 100 + weekStart.Day;
        }

        private static DateTime StartOfWeek(DateTime date, DayOfWeek startOfWeek)
        {
            var offset = (7 + (date.DayOfWeek - startOfWeek)) % 7;
            return date.Date.AddDays(-offset);
        }

        private static bool ShouldRenderByAge(int dayAge, bool showToday, bool showHistorical, int historyDays)
        {
            if (dayAge == 0)
                return showToday;

            if (!showHistorical)
                return false;

            return dayAge <= Math.Max(1, historyDays);
        }

        private decimal AverageVolume(int bar, int lookback)
        {
            if (bar <= 0 || lookback <= 0)
                return 0m;

            var start = Math.Max(0, bar - lookback);
            decimal sum = 0m;
            var count = 0;

            for (var i = start; i < bar; i++)
            {
                sum += GetCandle(i).Volume;
                count++;
            }

            return count == 0 ? 0m : sum / count;
        }

        private decimal HighestSpreadVolume(int bar, int lookback)
        {
            if (bar <= 0 || lookback <= 0)
                return 0m;

            var start = Math.Max(0, bar - lookback);
            decimal max = 0m;

            for (var i = start; i < bar; i++)
            {
                var c = GetCandle(i);
                var value = Math.Max(0m, c.High - c.Low) * c.Volume;
                if (value > max)
                    max = value;
            }

            return max;
        }

        private decimal CalculateStdDevClose(int bar, int period)
        {
            if (period <= 1)
                return 0m;

            var start = Math.Max(0, bar - period + 1);
            var count = bar - start + 1;
            if (count <= 1)
                return 0m;

            double sum = 0;
            double sumSquares = 0;

            for (var i = start; i <= bar; i++)
            {
                var close = (double)GetCandle(i).Close;
                sum += close;
                sumSquares += close * close;
            }

            var mean = sum / count;
            var variance = sumSquares / count - mean * mean;
            if (variance < 0)
                variance = 0;

            return (decimal)Math.Sqrt(variance);
        }

        private void ApplySeriesStyles()
        {
            _ema5.Color = _ema5Color.Convert();
            _ema5.Width = _ema5Width;
            _ema5.ShowCurrentValue = false;
            _ema5.VisualType = _showEmas && _showEma5 ? VisualMode.Line : VisualMode.Hide;

            _ema13.Color = _ema13Color.Convert();
            _ema13.Width = _ema13Width;
            _ema13.ShowCurrentValue = false;
            _ema13.VisualType = _showEmas && _showEma13 ? VisualMode.Line : VisualMode.Hide;

            _ema50.Color = _ema50Color.Convert();
            _ema50.Width = _ema50Width;
            _ema50.ShowCurrentValue = false;
            _ema50.VisualType = _showEmas && _showEma50 ? VisualMode.Line : VisualMode.Hide;

            _ema200.Color = _ema200Color.Convert();
            _ema200.Width = _ema200Width;
            _ema200.ShowCurrentValue = false;
            _ema200.VisualType = _showEmas && _showEma200 ? VisualMode.Line : VisualMode.Hide;

            _ema800.Color = _ema800Color.Convert();
            _ema800.Width = _ema800Width;
            _ema800.ShowCurrentValue = false;
            _ema800.VisualType = _showEmas && _showEma800 ? VisualMode.Line : VisualMode.Hide;

            _vwapSeries.Color = _vwapColor.Convert();
            _vwapSeries.Width = _vwapWidth;
            _vwapSeries.ShowCurrentValue = false;
            _vwapSeries.VisualType = _showVwap ? VisualMode.Line : VisualMode.Hide;

            _emaCloud.RangeColor = _emaCloudColor.Convert();
            _emaCloud.IsHidden = !(_showEmas && _showEmaCloud);

            _prevDayHighSeries.Color = _previousDayLevelsColor.Convert();
            _prevDayHighSeries.Width = _previousDayLevelsWidth;
            _prevDayHighSeries.LineDashStyle = (LineDashStyle)2;
            _prevDayHighSeries.ShowCurrentValue = false;
            _prevDayHighSeries.VisualType = _showPreviousDayLevels ? VisualMode.Line : VisualMode.Hide;

            _prevDayLowSeries.Color = _previousDayLevelsColor.Convert();
            _prevDayLowSeries.Width = _previousDayLevelsWidth;
            _prevDayLowSeries.LineDashStyle = (LineDashStyle)2;
            _prevDayLowSeries.ShowCurrentValue = false;
            _prevDayLowSeries.VisualType = _showPreviousDayLevels ? VisualMode.Line : VisualMode.Hide;

            _adrHighSeries.Color = _adrColor.Convert();
            _adrHighSeries.Width = _adrWidth;
            _adrHighSeries.LineDashStyle = LineDashStyle.Dot;
            _adrHighSeries.ShowCurrentValue = false;
            _adrHighSeries.VisualType = _showAdrLevels ? VisualMode.Line : VisualMode.Hide;

            _adrLowSeries.Color = _adrColor.Convert();
            _adrLowSeries.Width = _adrWidth;
            _adrLowSeries.LineDashStyle = LineDashStyle.Dot;
            _adrLowSeries.ShowCurrentValue = false;
            _adrLowSeries.VisualType = _showAdrLevels ? VisualMode.Line : VisualMode.Hide;

            _atrHighSeries.Color = _atrColor.Convert();
            _atrHighSeries.Width = _atrWidth;
            _atrHighSeries.ShowCurrentValue = false;
            _atrHighSeries.VisualType = _showAtrLevels ? VisualMode.Line : VisualMode.Hide;

            _atrLowSeries.Color = _atrColor.Convert();
            _atrLowSeries.Width = _atrWidth;
            _atrLowSeries.ShowCurrentValue = false;
            _atrLowSeries.VisualType = _showAtrLevels ? VisualMode.Line : VisualMode.Hide;

            _dailyOpenSeries.Color = _dailyOpenColor.Convert();
            _dailyOpenSeries.Width = _dailyOpenWidth;
            _dailyOpenSeries.LineDashStyle = LineDashStyle.Solid;
            _dailyOpenSeries.ShowCurrentValue = false;
            _dailyOpenSeries.VisualType = _showDailyOpen ? VisualMode.Line : VisualMode.Hide;

            _londonHighSeries.Color = _londonSessionColor.Convert();
            _londonHighSeries.ShowCurrentValue = false;
            _londonHighSeries.VisualType = VisualMode.Hide;

            _londonLowSeries.Color = _londonSessionColor.Convert();
            _londonLowSeries.ShowCurrentValue = false;
            _londonLowSeries.VisualType = VisualMode.Hide;

            _newYorkHighSeries.Color = _newYorkSessionColor.Convert();
            _newYorkHighSeries.ShowCurrentValue = false;
            _newYorkHighSeries.VisualType = VisualMode.Hide;

            _newYorkLowSeries.Color = _newYorkSessionColor.Convert();
            _newYorkLowSeries.ShowCurrentValue = false;
            _newYorkLowSeries.VisualType = VisualMode.Hide;

            _asiaHighSeries.Color = _asiaSessionColor.Convert();
            _asiaHighSeries.ShowCurrentValue = false;
            _asiaHighSeries.VisualType = VisualMode.Hide;

            _asiaLowSeries.Color = _asiaSessionColor.Convert();
            _asiaLowSeries.ShowCurrentValue = false;
            _asiaLowSeries.VisualType = VisualMode.Hide;

            _londonSessionBox.RangeColor = WithOpacity(_londonSessionColor, _sessionWidth).Convert();
            _londonSessionBox.IsHidden = !_showLondonSession;

            _newYorkSessionBox.RangeColor = WithOpacity(_newYorkSessionColor, _sessionWidth).Convert();
            _newYorkSessionBox.IsHidden = !_showNewYorkSession;

            _asiaSessionBox.RangeColor = WithOpacity(_asiaSessionColor, _sessionWidth).Convert();
            _asiaSessionBox.IsHidden = !_showAsiaSession;

            _euBrinksSessionBox.RangeColor = WithOpacity(_euBrinksSessionColor, _sessionWidth).Convert();
            _euBrinksSessionBox.IsHidden = !_showEuBrinksSession;

            _usBrinksSessionBox.RangeColor = WithOpacity(_usBrinksSessionColor, _sessionWidth).Convert();
            _usBrinksSessionBox.IsHidden = !_showUsBrinksSession;

            _psyHighSeries.Color = _weeklyPsyHighColor.Convert();
            _psyHighSeries.Width = _weeklyPsyWidth;
            _psyHighSeries.LineDashStyle = LineDashStyle.Solid;
            _psyHighSeries.ShowCurrentValue = false;
            _psyHighSeries.VisualType = _showWeeklyPsy ? VisualMode.Line : VisualMode.Hide;

            _psyLowSeries.Color = _weeklyPsyLowColor.Convert();
            _psyLowSeries.Width = _weeklyPsyWidth;
            _psyLowSeries.LineDashStyle = LineDashStyle.Solid;
            _psyLowSeries.ShowCurrentValue = false;
            _psyLowSeries.VisualType = _showWeeklyPsy ? VisualMode.Line : VisualMode.Hide;
        }

        private static Color WithOpacity(Color baseColor, int level)
        {
            var alpha = Math.Clamp(level, 1, 10) * 25;
            return Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
        }

        private static Color ApplyOpacity(Color baseColor, int opacityPercent)
        {
            var alpha = Math.Clamp(opacityPercent, 0, 100) * 255 / 100;
            return Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
        }

        private static Color ApplyTradingViewTransparency(Color baseColor, int transparencyPercent)
        {
            var alpha = (100 - Math.Clamp(transparencyPercent, 0, 100)) * 255 / 100;
            return Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
        }

        private static TimeSpan NormalizeSessionTime(TimeSpan value)
        {
            var minutes = (int)Math.Round(value.TotalMinutes);
            minutes %= 24 * 60;
            if (minutes < 0)
                minutes += 24 * 60;

            return TimeSpan.FromMinutes(minutes);
        }

        private static TimeSpan ApplyDstShift(TimeSpan baseTime, bool shiftOneHourEarlier)
        {
            return shiftOneHourEarlier
                ? NormalizeSessionTime(baseTime - TimeSpan.FromHours(1))
                : NormalizeSessionTime(baseTime);
        }

        private static bool IsUsDst(DateTime barTime)
        {
            return IsDst(barTime, UsEasternTimeZone);
        }

        private static bool IsUkDst(DateTime barTime)
        {
            return IsDst(barTime, UkTimeZone);
        }

        private static bool IsDst(DateTime barTime, TimeZoneInfo timeZone)
        {
            var utcTime = barTime.Kind == DateTimeKind.Utc
                ? barTime
                : DateTime.SpecifyKind(barTime, DateTimeKind.Utc);

            var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, timeZone);
            return timeZone.IsDaylightSavingTime(localTime);
        }

        private static TimeZoneInfo ResolveTimeZone(string windowsId, string ianaId)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
                }
                catch (TimeZoneNotFoundException)
                {
                    return TimeZoneInfo.Utc;
                }
            }
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        private void SetAndRecalculate<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            RecalculateValues();
        }
    }
}
