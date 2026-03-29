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
        private const string GroupLabels = "00. Labels & Quick Settings";
        private const string GroupEma = "01. EMAs";
        private const string GroupPvsra = "02. PVSRA, Alerts & Recovery";
        private const string GroupYDay = "03. Levels: Yesterday Hi/Lo";
        private const string GroupAdr = "04. Levels: ADR";
        private const string GroupAtr = "05. Levels: ATR";
        private const string GroupDailyOpen = "06. Levels: Daily Open";
        private const string GroupSessions = "08. Market Sessions";
        private const string GroupPsy = "09. Weekly Psy";
        private const string GroupVolume = "10. Volume, VWAP & Profile";
        private const string GroupMidPivots = "07. Levels: Mid Pivots";
        private const string GroupInfoTable = "11. TR Metrics Table";

        private const int Ema5Period = 5;
        private const int Ema13Period = 13;
        private const int Ema50Period = 50;
        private const int Ema200Period = 200;
        private const int Ema800Period = 800;
        private const int EmaCloudStdevPeriod = 100;
        private const int MaxStoredRanges = 500;
        private static readonly TimeSpan TableTimerLength = TimeSpan.FromSeconds(1);
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

        public enum QuickPreset
        {
            [Display(Name = "Full")]
            Full,

            [Display(Name = "Clean")]
            Clean,

            [Display(Name = "Crypto")]
            Crypto,

            [Display(Name = "Forex")]
            Forex
        }

        public enum InfoTablePosition
        {
            [Display(Name = "Top Left")]
            TopLeft,

            [Display(Name = "Top Right")]
            TopRight,

            [Display(Name = "Bottom Left")]
            BottomLeft,

            [Display(Name = "Bottom Right")]
            BottomRight
        }

        private sealed class DayVolumeProfile
        {
            public DateTime Date { get; init; }
            public int StartBar { get; init; }
            public int EndBar { get; init; }
            public string MergeLabel { get; set; } = string.Empty;
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

        private sealed class DynamicProfileCacheEntry
        {
            public DayVolumeProfile Profile { get; init; } = new();
            public int EndBar { get; init; }
            public decimal EndVolume { get; init; }
            public decimal EndHigh { get; init; }
            public decimal EndLow { get; init; }
            public decimal EndClose { get; init; }
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
        private readonly ValueDataSeries _m0Series = new("M0", "M0");
        private readonly ValueDataSeries _m1Series = new("M1", "M1");
        private readonly ValueDataSeries _m2Series = new("M2", "M2");
        private readonly ValueDataSeries _m3Series = new("M3", "M3");
        private readonly ValueDataSeries _m4Series = new("M4", "M4");
        private readonly ValueDataSeries _m5Series = new("M5", "M5");
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
        private bool _hasSaturdayDataForPsy;
        private int _lastSaturdayScanBar = -1;
        private DateTime _vwapSessionDate = DateTime.MinValue;
        private decimal _vwapCumulativePriceVolume;
        private decimal _vwapCumulativeVolume;
        private readonly List<DayVolumeProfile> _volumeProfiles = new();
        private readonly Dictionary<string, DayVolumeProfile> _marketProfileCache = new();
        private readonly Dictionary<string, DynamicProfileCacheEntry> _marketProfileDynamicCache = new();
        private readonly List<(DateTime Date, int StartBar, int EndBar)> _marketProfileDayRanges = new();
        private int _marketProfileDayRangesLastBar = -1;
        private int _marketProfileCacheSettingsHash = int.MinValue;
        private int _lastMarketProfileComputedBar = -1;
        private DateTime _lastMarketProfileRealtimeUpdateUtc = DateTime.MinValue;

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
        private int _ema800Width = 1;

        private Color _ema5Color = Color.FromArgb(254, 234, 74);
        private Color _ema13Color = Color.FromArgb(253, 84, 87);
        private Color _ema50Color = Color.FromArgb(31, 188, 211);
        private Color _ema200Color = Color.FromArgb(255, 255, 255);
        private Color _ema800Color = Color.FromArgb(50, 34, 144);
        private Color _emaCloudColor = Color.FromArgb(102, 155, 47, 174);

        private bool _showPvsraCandles = true;
        private const int PvsraLookbackBars = 10;
        private Color _vectorRedColor = Color.Red;
        private Color _vectorGreenColor = Color.Lime;
        private Color _vectorVioletColor = Color.Fuchsia;
        private Color _vectorBlueColor = Color.Blue;
        private Color _regularUpColor = Color.FromArgb(153, 153, 153);
        private Color _regularDownColor = Color.FromArgb(77, 77, 77);
        private bool _useVectorAlerts = true;
        private string _vectorAlertFile = "alert1";
        private bool _useCompositeAlerts = true;
        private bool _compositeAlertUseAdrTouch = true;
        private bool _compositeAlertUseProfileTouch = true;
        private string _compositeAlertFile = "alert2";
        private int _compositeAlertCooldownSeconds = 30;
        private int _compositeAlertProximityTicks = 0;
        private int _lastCompositeAlertBar = -1;
        private DateTime _lastCompositeAlertUtc = DateTime.MinValue;
        private bool _showVectorZones = true;
        private int _vectorZonesMax = 500;
        private ZoneBoundaryType _vectorZoneType = ZoneBoundaryType.BodyOnly;
        private ZoneBoundaryType _vectorZoneUpdateType = ZoneBoundaryType.BodyWithWicks;
        private int _vectorZoneBorderWidth;
        private bool _vectorRecoveryMatchCandleColor = true;
        private Color _vectorZoneColor = Color.FromArgb(26, 255, 230, 75);
        private int _vectorZoneTransparency = 80;
        private bool _showPvsraVolumeHistogram = true;
        private int _pvsraVolumeHistogramHeightPercent = 15;
        private int _pvsraVolumeHistogramOpacityPercent = 40;

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
        private Color _adrColor = Color.Red;
        private int _adrWidth = 2;

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

        private bool _showMidPivotLevels = true;
        private bool _showMidPivotLabels = true;
        private Color _midPivotColor = Color.FromArgb(128, 255, 255, 255);
        private Color _midPivotLabelColor = Color.FromArgb(128, 255, 255, 255);
        private int _midPivotWidth = 1;
        private ProfileLineStyle _midPivotLineStyle = ProfileLineStyle.Dashed;

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
        private int _sessionWidth = 2;
        private bool _showNyLondonCloseMarker = true;
        private Color _nyLondonCloseMarkerColor = Color.FromArgb(120, 0, 0, 0);
        private int _nyLondonCloseMarkerWidth = 1;

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
        private int _marketProfileHistoryDays = 6;
        private int _marketProfileHistoryDaysPending = 6;
        private bool _marketProfileIncludeCurrentDay = true;
        private bool _marketProfileIgnoreSunday;
        private string _marketProfileMergeGroups = "1-7";
        private string _marketProfileMergeGroupsPending = "1-7";
        private string _marketProfileExtraIndividualDays = "1,2";
        private string _marketProfileExtraIndividualDaysPending = "1,2";
        private bool _applyProfileChanges;
        private int _marketProfileRealtimeRefreshMs = 250;
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
        private int _marketProfileInsideVaOpacityBoost = 20;
        private int _marketProfileMaxBinsPerProfile = 180;
        private int _marketProfileMinBinSizeTicks = 1;
        private int _marketProfilePriceStepTicks = 1;
        private int _marketProfileValueAreaPercent = 70;
        private bool _marketProfileExtendLevelsToCurrentDay = true;
        private int _marketProfileVahOffsetTicks;
        private int _marketProfileValOffsetTicks;
        private Color _marketProfileHistogramColor = Color.FromArgb(95, 108, 144, 196);
        private Color _marketProfileInsideVaColor = Color.FromArgb(165, 30, 119, 212);
        private Color _marketProfileContourColor = Color.FromArgb(170, 120, 171, 229);
        private int _marketProfileContourWidth = 1;
        private ProfileLineStyle _marketProfileContourLineStyle = ProfileLineStyle.Solid;
        private ProfileLineStyle _marketProfilePocLineStyle = ProfileLineStyle.Solid;
        private ProfileLineStyle _marketProfileVaLineStyle = ProfileLineStyle.Dotted;
        private bool _showProfilePoc = true;
        private bool _showProfileValueArea = true;
        private Color _marketProfilePocColor = Color.FromArgb(255, 246, 181, 0);
        private Color _marketProfileVahColor = Color.FromArgb(30, 144, 255);
        private Color _marketProfileValColor = Color.FromArgb(30, 144, 255);
        private int _marketProfileLineWidth = 2;


        private bool _showInfoTable = true;
        private InfoTablePosition _infoTablePosition = InfoTablePosition.TopLeft;
        private int _infoTableOpacityPercent = 10;
        private bool _infoTableShow3xAdr = true;
        private bool _infoTableShowDistances = true;
        private bool _infoTableShowHeader = true;
        private bool _infoTableShowBorder = true;
        private int _infoTableMarginPx = 10;
        private int _infoTablePaddingPx = 6;
        private int _infoTableColumnGapPx = 12;
        private Color _infoTableBackgroundColor = Color.FromArgb(15, 18, 24);
        private Color _infoTableBorderColor = Color.FromArgb(90, 120, 120, 120);
        private Color _infoTableHeaderColor = Color.FromArgb(254, 234, 78);
        private Color _infoTableTextColor = Color.White;
        private QuickPreset _quickPreset = QuickPreset.Full;
        private bool _applyingQuickPreset;
        private bool _cleanChart;
        private int _dayTimeShiftHours;
        private bool _showLabels = true;
        private bool _showProfileLabels = true;
        private bool _showHistoricalProfileLabels = true;
        private bool _showYDayLabels = true;
        private bool _showAdrLabels = true;
        private bool _showPsyLabels = true;


        [Display(Name = "Quick Preset", GroupName = GroupLabels, Order = 0)]
        public QuickPreset Preset
        {
            get => _quickPreset;
            set
            {
                if (_quickPreset == value)
                    return;

                _quickPreset = value;
                ApplyQuickPreset(value);
                RecalculateValues();
            }
        }

        [Display(Name = "Clean Chart", GroupName = GroupLabels, Order = 1)]
        public bool CleanChart
        {
            get => _cleanChart;
            set => SetAndRecalculate(ref _cleanChart, value);
        }

        [Display(Name = "Day Time Shift (hours)", GroupName = GroupLabels, Order = 2)]
        [Range(-23, 23)]
        public int DayTimeShiftHours
        {
            get => _dayTimeShiftHours;
            set => SetAndRecalculate(ref _dayTimeShiftHours, Math.Clamp(value, -23, 23));
        }

        [Display(Name = "Show Labels", GroupName = GroupLabels, Order = 10)]
        public bool ShowLabels
        {
            get => _showLabels;
            set => SetAndRecalculate(ref _showLabels, value);
        }

        [Display(Name = "Show Profile Labels", GroupName = GroupLabels, Order = 20)]
        public bool ShowProfileLabels
        {
            get => _showProfileLabels;
            set => SetAndRecalculate(ref _showProfileLabels, value);
        }

        [Display(Name = "Show Historical Profile Labels", GroupName = GroupLabels, Order = 25)]
        public bool ShowHistoricalProfileLabels
        {
            get => _showHistoricalProfileLabels;
            set => SetAndRecalculate(ref _showHistoricalProfileLabels, value);
        }

        [Display(Name = "Show YDay Labels", GroupName = GroupLabels, Order = 30)]
        public bool ShowYDayLabels
        {
            get => _showYDayLabels;
            set => SetAndRecalculate(ref _showYDayLabels, value);
        }

        [Display(Name = "Show ADR Labels", GroupName = GroupLabels, Order = 40)]
        public bool ShowAdrLabels
        {
            get => _showAdrLabels;
            set => SetAndRecalculate(ref _showAdrLabels, value);
        }

        [Display(Name = "Show Psy Labels", GroupName = GroupLabels, Order = 50)]
        public bool ShowPsyLabels
        {
            get => _showPsyLabels;
            set => SetAndRecalculate(ref _showPsyLabels, value);
        }
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

        [Display(Name = "Use Composite Alerts", GroupName = GroupPvsra, Order = 101)]
        public bool UseCompositeAlerts
        {
            get => _useCompositeAlerts;
            set => SetAndRecalculate(ref _useCompositeAlerts, value);
        }

        [Display(Name = "Composite Alert File", GroupName = GroupPvsra, Order = 102)]
        public string CompositeAlertFile
        {
            get => _compositeAlertFile;
            set => SetAndRecalculate(ref _compositeAlertFile, string.IsNullOrWhiteSpace(value) ? "alert2" : value.Trim());
        }

        [Display(Name = "Composite Cooldown (sec)", GroupName = GroupPvsra, Order = 103)]
        [Range(0, 3600)]
        public int CompositeAlertCooldownSeconds
        {
            get => _compositeAlertCooldownSeconds;
            set => SetAndRecalculate(ref _compositeAlertCooldownSeconds, Math.Clamp(value, 0, 3600));
        }

        [Display(Name = "Composite: ADR Touch", GroupName = GroupPvsra, Order = 104)]
        public bool CompositeAlertUseAdrTouch
        {
            get => _compositeAlertUseAdrTouch;
            set => SetAndRecalculate(ref _compositeAlertUseAdrTouch, value);
        }

        [Display(Name = "Composite: Profile Touch", GroupName = GroupPvsra, Order = 105)]
        public bool CompositeAlertUseProfileTouch
        {
            get => _compositeAlertUseProfileTouch;
            set => SetAndRecalculate(ref _compositeAlertUseProfileTouch, value);
        }

        [Display(Name = "Composite Proximity (ticks)", GroupName = GroupPvsra, Order = 106)]
        [Range(0, 50)]
        public int CompositeAlertProximityTicks
        {
            get => _compositeAlertProximityTicks;
            set => SetAndRecalculate(ref _compositeAlertProximityTicks, Math.Clamp(value, 0, 50));
        }

        [Display(Name = "Enable Vector Candle Recovery", GroupName = GroupPvsra, Order = 110)]
        public bool ShowVectorZones
        {
            get => _showVectorZones;
            set => SetAndRecalculate(ref _showVectorZones, value);
        }

        [Display(Name = "Max Recovery Zones", GroupName = GroupPvsra, Order = 120)]
        [Range(1, 2000)]
        public int VectorZonesMax
        {
            get => _vectorZonesMax;
            set => SetAndRecalculate(ref _vectorZonesMax, Math.Max(1, value));
        }

        [Display(Name = "Recovery Zone Source (Body/Wick)", GroupName = GroupPvsra, Order = 130)]
        public ZoneBoundaryType VectorZoneType
        {
            get => _vectorZoneType;
            set => SetAndRecalculate(ref _vectorZoneType, value);
        }

        [Display(Name = "Recovery Fill Source (Body/Wick)", GroupName = GroupPvsra, Order = 140)]
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

        [Display(Name = "Match Recovery Zone To Candle Color", GroupName = GroupPvsra, Order = 160)]
        public bool MatchRecoveryZoneToCandleColor
        {
            get => _vectorRecoveryMatchCandleColor;
            set => SetAndRecalculate(ref _vectorRecoveryMatchCandleColor, value);
        }

        [Display(Name = "Recovery Zone Color", GroupName = GroupPvsra, Order = 170)]
        public CrossColor VectorZoneColor
        {
            get => _vectorZoneColor.Convert();
            set => SetAndRecalculate(ref _vectorZoneColor, value.Convert());
        }

        [Display(Name = "Recovery Zone Transparency", GroupName = GroupPvsra, Order = 180)]
        [Range(0, 100)]
        public int VectorZoneTransparency
        {
            get => _vectorZoneTransparency;
            set => SetAndRecalculate(ref _vectorZoneTransparency, Math.Clamp(value, 0, 100));
        }


        [Display(Name = "Show PVSRA Volume Histogram", GroupName = GroupPvsra, Order = 200)]
        public bool ShowPvsraVolumeHistogram
        {
            get => _showPvsraVolumeHistogram;
            set => SetAndRecalculate(ref _showPvsraVolumeHistogram, value);
        }

        [Display(Name = "PVSRA Histogram Height %", GroupName = GroupPvsra, Order = 210)]
        [Range(5, 50)]
        public int PvsraVolumeHistogramHeightPercent
        {
            get => _pvsraVolumeHistogramHeightPercent;
            set => SetAndRecalculate(ref _pvsraVolumeHistogramHeightPercent, Math.Clamp(value, 5, 50));
        }

        [Display(Name = "PVSRA Histogram Opacity %", GroupName = GroupPvsra, Order = 220)]
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

        [Display(Name = "Show M Levels", GroupName = GroupMidPivots, Order = 10)]
        public bool ShowMidPivotLevels
        {
            get => _showMidPivotLevels;
            set => SetAndRecalculate(ref _showMidPivotLevels, value);
        }

        [Display(Name = "Show Labels", GroupName = GroupMidPivots, Order = 20)]
        public bool ShowMidPivotLabels
        {
            get => _showMidPivotLabels;
            set => SetAndRecalculate(ref _showMidPivotLabels, value);
        }

        [Display(Name = "M Levels Color", GroupName = GroupMidPivots, Order = 30)]
        public CrossColor MidPivotColor
        {
            get => _midPivotColor.Convert();
            set => SetAndRecalculate(ref _midPivotColor, value.Convert());
        }

        [Display(Name = "M Labels Color", GroupName = GroupMidPivots, Order = 40)]
        public CrossColor MidPivotLabelColor
        {
            get => _midPivotLabelColor.Convert();
            set => SetAndRecalculate(ref _midPivotLabelColor, value.Convert());
        }

        [Display(Name = "M Levels Line Style", GroupName = GroupMidPivots, Order = 50)]
        public ProfileLineStyle MidPivotLineStyle
        {
            get => _midPivotLineStyle;
            set => SetAndRecalculate(ref _midPivotLineStyle, value);
        }

        [Display(Name = "M Levels Width", GroupName = GroupMidPivots, Order = 60)]
        [Range(1, 10)]
        public int MidPivotWidth
        {
            get => _midPivotWidth;
            set => SetAndRecalculate(ref _midPivotWidth, Math.Max(1, value));
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

        [Display(Name = "Show London Close In NY", GroupName = GroupSessions, Order = 260)]
        public bool ShowNyLondonCloseMarker
        {
            get => _showNyLondonCloseMarker;
            set => SetAndRecalculate(ref _showNyLondonCloseMarker, value);
        }

        [Display(Name = "NY Marker Color", GroupName = GroupSessions, Order = 270)]
        public CrossColor NyLondonCloseMarkerColor
        {
            get => _nyLondonCloseMarkerColor.Convert();
            set => SetAndRecalculate(ref _nyLondonCloseMarkerColor, value.Convert());
        }

        [Display(Name = "NY Marker Width", GroupName = GroupSessions, Order = 280)]
        [Range(1, 5)]
        public int NyLondonCloseMarkerWidth
        {
            get => _nyLondonCloseMarkerWidth;
            set => SetAndRecalculate(ref _nyLondonCloseMarkerWidth, Math.Max(1, value));
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
            get => _marketProfileHistoryDaysPending;
            set
            {
                var normalized = Math.Clamp(value, 1, 50);
                if (_marketProfileHistoryDaysPending == normalized)
                    return;

                _marketProfileHistoryDaysPending = normalized;

                // During initial load keep active value synchronized.
                if (CurrentBar <= 0)
                    _marketProfileHistoryDays = normalized;
            }
        }

        [Display(Name = "Include Current Day", GroupName = GroupVolume, Order = 55)]
        public bool MarketProfileIncludeCurrentDay
        {
            get => _marketProfileIncludeCurrentDay;
            set => SetAndRecalculate(ref _marketProfileIncludeCurrentDay, value);
        }

        [Display(Name = "Merge Groups (e.g. 2-4;5-8)", GroupName = GroupVolume, Order = 56)]
        public string MarketProfileMergeGroups
        {
            get => _marketProfileMergeGroupsPending;
            set
            {
                var normalized = value?.Trim() ?? string.Empty;
                if (_marketProfileMergeGroupsPending == normalized)
                    return;

                _marketProfileMergeGroupsPending = normalized;

                // During initial load keep active value synchronized.
                if (CurrentBar <= 0)
                    _marketProfileMergeGroups = normalized;
            }
        }

        [Display(Name = "Extra Individual Days (e.g. 1-3,5)", GroupName = GroupVolume, Order = 57)]
        public string MarketProfileExtraIndividualDays
        {
            get => _marketProfileExtraIndividualDaysPending;
            set
            {
                var normalized = value?.Trim() ?? string.Empty;
                if (_marketProfileExtraIndividualDaysPending == normalized)
                    return;

                _marketProfileExtraIndividualDaysPending = normalized;

                // During initial load keep active value synchronized.
                if (CurrentBar <= 0)
                    _marketProfileExtraIndividualDays = normalized;
            }
        }

        [Display(Name = ">>> APPLY PROFILE CHANGES <<<", GroupName = GroupVolume, Order = 58)]
        public bool ApplyProfileChanges
        {
            get => _applyProfileChanges;
            set
            {
                if (_applyProfileChanges == value)
                    return;

                _applyProfileChanges = value;
                if (!_applyProfileChanges)
                    return;

                _applyProfileChanges = false;
                if (ApplyPendingMarketProfileChanges())
                    RecalculateValues();
            }
        }

        [Display(Name = "Realtime Refresh (ms)", GroupName = GroupVolume, Order = 59)]
        [Range(0, 5000)]
        public int MarketProfileRealtimeRefreshMs
        {
            get => _marketProfileRealtimeRefreshMs;
            set => SetAndRecalculate(ref _marketProfileRealtimeRefreshMs, Math.Clamp(value, 0, 5000));
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

        [Display(Name = "Inside VA Extra Opacity %", GroupName = GroupVolume, Order = 101)]
        [Range(0, 100)]
        public int MarketProfileInsideVaOpacityBoost
        {
            get => _marketProfileInsideVaOpacityBoost;
            set => SetAndRecalculate(ref _marketProfileInsideVaOpacityBoost, Math.Clamp(value, 0, 100));
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


        [Display(Name = "Show Info Table", GroupName = GroupInfoTable, Order = 10)]
        public bool ShowInfoTable
        {
            get => _showInfoTable;
            set => SetAndRecalculate(ref _showInfoTable, value);
        }

        [Display(Name = "Position", GroupName = GroupInfoTable, Order = 20)]
        public InfoTablePosition TablePosition
        {
            get => _infoTablePosition;
            set => SetAndRecalculate(ref _infoTablePosition, value);
        }

        [Display(Name = "Opacity %", GroupName = GroupInfoTable, Order = 30)]
        [Range(0, 100)]
        public int InfoTableOpacityPercent
        {
            get => _infoTableOpacityPercent;
            set => SetAndRecalculate(ref _infoTableOpacityPercent, Math.Clamp(value, 0, 100));
        }

        [Display(Name = "Show 3x ADR", GroupName = GroupInfoTable, Order = 40)]
        public bool InfoTableShow3xAdr
        {
            get => _infoTableShow3xAdr;
            set => SetAndRecalculate(ref _infoTableShow3xAdr, value);
        }

        [Display(Name = "Show Distances", GroupName = GroupInfoTable, Order = 50)]
        public bool InfoTableShowDistances
        {
            get => _infoTableShowDistances;
            set => SetAndRecalculate(ref _infoTableShowDistances, value);
        }

        [Display(Name = "Show Header", GroupName = GroupInfoTable, Order = 60)]
        public bool InfoTableShowHeader
        {
            get => _infoTableShowHeader;
            set => SetAndRecalculate(ref _infoTableShowHeader, value);
        }

        [Display(Name = "Show Border", GroupName = GroupInfoTable, Order = 70)]
        public bool InfoTableShowBorder
        {
            get => _infoTableShowBorder;
            set => SetAndRecalculate(ref _infoTableShowBorder, value);
        }

        [Display(Name = "Margin Px", GroupName = GroupInfoTable, Order = 80)]
        [Range(0, 100)]
        public int InfoTableMarginPx
        {
            get => _infoTableMarginPx;
            set => SetAndRecalculate(ref _infoTableMarginPx, Math.Clamp(value, 0, 100));
        }

        [Display(Name = "Padding Px", GroupName = GroupInfoTable, Order = 90)]
        [Range(2, 20)]
        public int InfoTablePaddingPx
        {
            get => _infoTablePaddingPx;
            set => SetAndRecalculate(ref _infoTablePaddingPx, Math.Clamp(value, 2, 20));
        }

        [Display(Name = "Column Gap Px", GroupName = GroupInfoTable, Order = 100)]
        [Range(4, 40)]
        public int InfoTableColumnGapPx
        {
            get => _infoTableColumnGapPx;
            set => SetAndRecalculate(ref _infoTableColumnGapPx, Math.Clamp(value, 4, 40));
        }

        [Display(Name = "Background Color", GroupName = GroupInfoTable, Order = 110)]
        public CrossColor InfoTableBackgroundColor
        {
            get => _infoTableBackgroundColor.Convert();
            set => SetAndRecalculate(ref _infoTableBackgroundColor, value.Convert());
        }

        [Display(Name = "Border Color", GroupName = GroupInfoTable, Order = 120)]
        public CrossColor InfoTableBorderColor
        {
            get => _infoTableBorderColor.Convert();
            set => SetAndRecalculate(ref _infoTableBorderColor, value.Convert());
        }

        [Display(Name = "Header Color", GroupName = GroupInfoTable, Order = 130)]
        public CrossColor InfoTableHeaderColor
        {
            get => _infoTableHeaderColor.Convert();
            set => SetAndRecalculate(ref _infoTableHeaderColor, value.Convert());
        }

        [Display(Name = "Text Color", GroupName = GroupInfoTable, Order = 140)]
        public CrossColor InfoTableTextColor
        {
            get => _infoTableTextColor.Convert();
            set => SetAndRecalculate(ref _infoTableTextColor, value.Convert());
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
            _m0Series.VisualType = VisualMode.Line;
            _m1Series.VisualType = VisualMode.Line;
            _m2Series.VisualType = VisualMode.Line;
            _m3Series.VisualType = VisualMode.Line;
            _m4Series.VisualType = VisualMode.Line;
            _m5Series.VisualType = VisualMode.Line;
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
            DataSeries.Add(_m0Series);
            DataSeries.Add(_m1Series);
            DataSeries.Add(_m2Series);
            DataSeries.Add(_m3Series);
            DataSeries.Add(_m4Series);
            DataSeries.Add(_m5Series);
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
            ApplyQuickPreset(_quickPreset);
            ApplySeriesStyles();
        }

        protected override void OnInitialize()
        {
            SubscribeToTimer(TableTimerLength, OnTableTimer);
        }

        protected override void OnDispose()
        {
            UnsubscribeFromTimer(TableTimerLength, OnTableTimer);
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
            DrawMidPivotLevels(bar);
            DrawSessionLevels(bar, candle);
            DrawWeeklyPsy(bar, candle);
        }

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (ChartInfo is null)
                return;

            DrawVectorZones(context);
            DrawPvsraVolumeHistogram(context);
            DrawNyLondonCloseMarker(context);
            DrawAuxiliaryLineLabels(context);

            if (InstrumentInfo is null || !_showMarketProfile || _volumeProfiles.Count == 0)
            {
                DrawInfoTable(context);
                return;
            }

            var leftToRight = _marketProfileDirection == ProfileDirection.LeftToRight;
            var barWidth = Math.Max(1, (int)Math.Round(ChartInfo.PriceChartContainer.BarsWidth));
            var tick = InstrumentInfo.TickSize > 0m ? InstrumentInfo.TickSize : 0.00000001m;
            var lastBarX = ChartInfo.GetXByBar(Math.Max(0, CurrentBar - 1), false) + barWidth;
            var drawHistogram = _marketProfileRenderMode == ProfileRenderMode.Histogram || _marketProfileRenderMode == ProfileRenderMode.Both;
            var drawContour = _marketProfileRenderMode == ProfileRenderMode.Line || _marketProfileRenderMode == ProfileRenderMode.Both;
            var outsideOpacity = Math.Clamp(_marketProfileOpacity, 0, 100);
            var insideOpacity = Math.Clamp(_marketProfileOpacity + _marketProfileInsideVaOpacityBoost, 0, 100);
            var outsideVaColor = ApplyOpacity(_marketProfileHistogramColor, outsideOpacity);
            var insideVaColor = ApplyOpacity(_marketProfileInsideVaColor, insideOpacity);
            var widthFraction = Math.Clamp(_marketProfileWidthPercent / 100.0, 0.01, 0.95);
            var minFrac = Math.Clamp(_marketProfileMinBinWidthPercent / 100.0, 0.0, 0.20);
            var currentChartDate = GetCurrentChartDate();

            foreach (var profile in _volumeProfiles)
            {
                if (!profile.IsValid || profile.MaxVolume <= 0m || profile.PriceStep <= 0m)
                    continue;

                var profileBars = Math.Max(1, profile.EndBar - profile.StartBar + 1);
                var widthByPercent = Math.Max(1, (int)Math.Round(profileBars * barWidth * widthFraction));
                var profileWidth = Math.Max(20, Math.Min(widthByPercent, _marketProfileMaxWidthPx));
                var anchorBar = leftToRight ? profile.StartBar : profile.EndBar;
                var anchorX = ChartInfo.GetXByBar(anchorBar, false) + (leftToRight ? 0 : barWidth) + _marketProfileOffsetPx;
                var lineX1 = ChartInfo.GetXByBar(profile.StartBar, false);
                var lineX2 = ChartInfo.GetXByBar(profile.EndBar, false) + barWidth;
                var isMergedRange = !string.IsNullOrWhiteSpace(profile.MergeLabel);
                if (_marketProfileExtendLevelsToCurrentDay && (profile.EndBar >= CurrentBar - 1 || isMergedRange))
                    lineX2 = Math.Max(lineX2, lastBarX);
                var isCurrentDayProfile = currentChartDate != DateTime.MinValue &&
                    GetTradingDate(GetCandle(Math.Clamp(profile.EndBar, 0, Math.Max(0, CurrentBar - 1))).Time) == currentChartDate;

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

                var mergeSuffix = string.IsNullOrWhiteSpace(profile.MergeLabel) ? string.Empty : $" {profile.MergeLabel}";
                var shouldDrawProfileLabels = _showProfileLabels && (isCurrentDayProfile || _showHistoricalProfileLabels);

                if (_showProfilePoc)
                {
                    var pocY = ChartInfo.GetYByPrice(profile.PocPrice, false);
                    DrawStyledHorizontalBand(context, lineX1, lineX2, pocY, _marketProfilePocColor, _marketProfileLineWidth + 1, _marketProfilePocLineStyle);
                    if (shouldDrawProfileLabels)
                        DrawProfileLevelLabel(context, $"POC{mergeSuffix}", _marketProfilePocColor, lineX2, pocY);
                }

                if (_showProfileValueArea)
                {
                    var vahPrice = profile.VahPrice + _marketProfileVahOffsetTicks * tick;
                    var valPrice = profile.ValPrice + _marketProfileValOffsetTicks * tick;

                    var vahY = ChartInfo.GetYByPrice(vahPrice, false);
                    var valY = ChartInfo.GetYByPrice(valPrice, false);

                    DrawStyledHorizontalBand(context, lineX1, lineX2, vahY, _marketProfileVahColor, _marketProfileLineWidth + 1, _marketProfileVaLineStyle);
                    DrawStyledHorizontalBand(context, lineX1, lineX2, valY, _marketProfileValColor, _marketProfileLineWidth, _marketProfileVaLineStyle);
                    if (shouldDrawProfileLabels)
                        DrawProfileLevelLabel(context, $"VAH{mergeSuffix}", _marketProfileVahColor, lineX2, vahY);
                    if (shouldDrawProfileLabels)
                        DrawProfileLevelLabel(context, $"VAL{mergeSuffix}", _marketProfileValColor, lineX2, valY);
                }
            }

            DrawInfoTable(context);
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

        private bool CanDrawAnyLabels()
        {
            return _showLabels;
        }

        private DateTime GetShiftedDateTime(DateTime time)
        {
            return _dayTimeShiftHours == 0
                ? time
                : time.AddHours(_dayTimeShiftHours);
        }

        private DateTime GetTradingDate(DateTime time)
        {
            return GetShiftedDateTime(time).Date;
        }

        private TimeSpan GetTradingTimeOfDay(DateTime time)
        {
            return GetShiftedDateTime(time).TimeOfDay;
        }

        private DayOfWeek GetTradingDayOfWeek(DateTime time)
        {
            return GetShiftedDateTime(time).DayOfWeek;
        }

        private DateTime GetCurrentChartDate()
        {
            var lastBar = CurrentBar - 1;
            if (lastBar < 0)
                return DateTime.MinValue;

            return GetTradingDate(GetCandle(lastBar).Time);
        }

        private void DrawProfileLevelLabel(RenderContext context, string text, Color color, int lineX2, int y)
        {
            if (!CanDrawAnyLabels() || ChartInfo is null || Container is null || string.IsNullOrWhiteSpace(text))
                return;

            var font = ChartInfo.PriceAxisFont;
            if (font is null)
                return;

            var size = context.MeasureString(text, font);
            var x = Math.Min(Container.Region.Right - size.Width - 2, Math.Max(Container.Region.Left + 2, lineX2 + 6));
            var yTop = Math.Max(Container.Region.Top, Math.Min(Container.Region.Bottom - size.Height, y - size.Height / 2));
            context.DrawString(text, font, color, x, yTop);
        }


        private void DrawAuxiliaryLineLabels(RenderContext context)
        {
            if (!CanDrawAnyLabels() || ChartInfo is null || Container is null)
                return;

            if (_showPreviousDayLevels && _showYDayLabels)
            {
                DrawSeriesRunLabels(context, _prevDayHighSeries, "YDay Hi", _previousDayLevelsColor);
                DrawSeriesRunLabels(context, _prevDayLowSeries, "YDay Lo", _previousDayLevelsColor);
            }

            if (_showAdrLevels && _showAdrLabels)
            {
                DrawSeriesRunLabels(context, _adrHighSeries, "Hi-ADR", _adrColor);
                DrawSeriesRunLabels(context, _adrLowSeries, "Lo-ADR", _adrColor);
            }

            if (_showMidPivotLevels && _showMidPivotLabels)
            {
                DrawSeriesRunLabels(context, _m0Series, "M0", _midPivotLabelColor);
                DrawSeriesRunLabels(context, _m1Series, "M1", _midPivotLabelColor);
                DrawSeriesRunLabels(context, _m2Series, "M2", _midPivotLabelColor);
                DrawSeriesRunLabels(context, _m3Series, "M3", _midPivotLabelColor);
                DrawSeriesRunLabels(context, _m4Series, "M4", _midPivotLabelColor);
                DrawSeriesRunLabels(context, _m5Series, "M5", _midPivotLabelColor);
            }

            if (_showWeeklyPsy && _showPsyLabels)
            {
                DrawSeriesRunLabels(context, _psyHighSeries, "Psy Hi", _weeklyPsyHighColor);
                DrawSeriesRunLabels(context, _psyLowSeries, "Psy Lo", _weeklyPsyLowColor);
            }
        }


        private void DrawInfoTable(RenderContext context)
        {
            if (_cleanChart || !_showInfoTable || ChartInfo is null || Container is null || !_hasActiveSession || CurrentBar <= 0)
                return;

            var font = ChartInfo.PriceAxisFont;
            if (font is null)
                return;

            var lastBar = Math.Max(0, CurrentBar - 1);
            var lastCandle = GetCandle(lastBar);
            var lastPrice = lastCandle.Close;

            var hod = _sessionHigh;
            var lod = _sessionLow;
            var dayRange = Math.Max(0m, hod - lod);
            var adr = AverageRange(_adrLength);
            var adrStdDev = RangeStandardDeviation(_adrLength);
            var threeAdr = adr * 3m;
            var threeAdrStdDev = adrStdDev * 3m;
            var distToHod = hod - lastPrice;
            var distToLod = lastPrice - lod;
            var adrUsedPct = adr > 0m ? dayRange / adr * 100m : 0m;
            var tickSize = InstrumentInfo?.TickSize ?? 0m;

            var rows = new List<(string Label, string Value)>
            {
                ($"ADR ({_adrLength})", $"{FormatTableNumber(adr)} +/- {FormatTableNumber(adrStdDev)}")
            };

            if (_infoTableShow3xAdr)
                rows.Add(("3x ADR", $"{FormatTableNumber(threeAdr)} +/- {FormatTableNumber(threeAdrStdDev)}"));

            rows.Add(("ADR Used %", adr > 0m ? $"{adrUsedPct:0.0}%" : "n/a"));

            if (_infoTableShowDistances)
            {
                rows.Add(("Dist to HOD", FormatTableDistance(distToHod, tickSize)));
                rows.Add(("Dist to LOD", FormatTableDistance(distToLod, tickSize)));
            }

            rows.Add(("Candle Time", GetCurrentCandleTimeRemaining(lastBar)));

            if (rows.Count == 0)
                return;

            var header = "TR Metrics";
            var rowHeight = Math.Max(12, context.MeasureString("Ag", font).Height + 2);
            var padding = Math.Clamp(_infoTablePaddingPx, 2, 20);
            var gutter = Math.Clamp(_infoTableColumnGapPx, 4, 40);

            var labelWidth = 0;
            var valueWidth = 0;
            foreach (var row in rows)
            {
                labelWidth = Math.Max(labelWidth, context.MeasureString(row.Label, font).Width);
                valueWidth = Math.Max(valueWidth, context.MeasureString(row.Value, font).Width);
            }

            var headerWidth = _infoTableShowHeader ? context.MeasureString(header, font).Width : 0;
            var tableWidth = Math.Max(headerWidth + padding * 2, padding * 2 + labelWidth + gutter + valueWidth);
            var headerRows = _infoTableShowHeader ? 1 : 0;
            var separatorHeight = _infoTableShowHeader ? 2 : 0;
            var tableHeight = padding * 2 + rowHeight * (rows.Count + headerRows) + separatorHeight;

            var margin = Math.Clamp(_infoTableMarginPx, 0, 100);
            var region = Container.Region;
            var x = _infoTablePosition switch
            {
                InfoTablePosition.TopLeft => region.Left + margin,
                InfoTablePosition.BottomLeft => region.Left + margin,
                InfoTablePosition.BottomRight => region.Right - tableWidth - margin,
                _ => region.Right - tableWidth - margin
            };

            var y = _infoTablePosition switch
            {
                InfoTablePosition.BottomLeft => region.Bottom - tableHeight - margin,
                InfoTablePosition.BottomRight => region.Bottom - tableHeight - margin,
                _ => region.Top + margin
            };

            x = Math.Max(region.Left + 1, Math.Min(x, region.Right - tableWidth - 1));
            y = Math.Max(region.Top + 1, Math.Min(y, region.Bottom - tableHeight - 1));

            var rect = new Rectangle(x, y, tableWidth, tableHeight);
            var bg = ApplyOpacity(_infoTableBackgroundColor, _infoTableOpacityPercent);
            var border = ApplyOpacity(_infoTableBorderColor, Math.Clamp(_infoTableOpacityPercent + 20, 0, 100));

            context.FillRectangle(bg, rect);
            if (_infoTableShowBorder)
                DrawRectangleBorder(context, rect, border, 1);

            var rowY = y + padding;
            if (_infoTableShowHeader)
            {
                context.DrawString(header, font, _infoTableHeaderColor, x + padding, rowY);
                var separatorY = rowY + rowHeight;
                context.FillRectangle(border, new Rectangle(x + padding, separatorY, Math.Max(1, tableWidth - padding * 2), 1));
                rowY = separatorY + 2;
            }

            foreach (var row in rows)
            {
                context.DrawString(row.Label, font, _infoTableTextColor, x + padding, rowY);
                var valueSize = context.MeasureString(row.Value, font);
                var valueX = x + tableWidth - padding - valueSize.Width;
                context.DrawString(row.Value, font, _infoTableTextColor, valueX, rowY);
                rowY += rowHeight;
            }
        }

        private void OnTableTimer()
        {
            if (_showInfoTable && !_cleanChart)
                RedrawChart();
        }
        private string GetCurrentCandleTimeRemaining(int bar)
        {
            if (bar < 0 || bar >= CurrentBar)
                return "n/a";

            var openTime = GetCandle(bar).Time;
            var duration = EstimateCandleDuration(bar);
            if (duration <= TimeSpan.Zero)
                return "n/a";

            var closeTime = openTime + duration;
            var remaining = closeTime - MarketTime;
            if (remaining <= TimeSpan.Zero)
                remaining += duration;

            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            return FormatRemainingTime(remaining);
        }

        private TimeSpan EstimateCandleDuration(int bar)
        {
            if (bar < CurrentBar - 1)
            {
                var diff = GetCandle(bar + 1).Time - GetCandle(bar).Time;
                if (diff > TimeSpan.Zero && diff <= TimeSpan.FromDays(2))
                    return diff;
            }

            for (var i = bar; i > 0; i--)
            {
                var diff = GetCandle(i).Time - GetCandle(i - 1).Time;
                if (diff > TimeSpan.Zero && diff <= TimeSpan.FromDays(2))
                    return diff;
            }

            return TimeSpan.FromMinutes(1);
        }


        private static string FormatRemainingTime(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            var totalHours = (int)remaining.TotalHours;
            if (totalHours > 0)
                return $"{totalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";

            return $"{remaining.Minutes:00}:{remaining.Seconds:00}";
        }
        private static string FormatTableNumber(decimal value)
        {
            var abs = Math.Abs(value);
            if (abs >= 10000m)
                return value.ToString("0");

            if (abs >= 1000m)
                return value.ToString("0.##");

            return value.ToString("0.#####");
        }

        private static string FormatTableDistance(decimal distance, decimal tickSize)
        {
            var price = FormatTableNumber(distance);
            if (tickSize <= 0m)
                return price;

            var ticks = distance / tickSize;
            return $"{price} ({ticks:0.#}t)";
        }
        private void DrawSeriesRunLabels(RenderContext context, ValueDataSeries series, string label, Color color)
        {
            if (ChartInfo?.PriceChartContainer is null || Container is null)
                return;

            var currentChartDate = GetCurrentChartDate();
            if (currentChartDate == DateTime.MinValue)
                return;
            var firstBar = Math.Max(0, FirstVisibleBarNumber);
            var lastBar = Math.Min(LastVisibleBarNumber, CurrentBar - 1);
            if (lastBar < firstBar)
                return;

            var barWidth = Math.Max(1, (int)Math.Round(ChartInfo.PriceChartContainer.BarsWidth));

            for (var i = firstBar; i <= lastBar; i++)
            {
                var value = series[i];
                if (value == 0m)
                    continue;

                var isRunEnd = i == lastBar;
                if (!isRunEnd)
                {
                    var nextValue = series[i + 1];
                    var thisDate = GetTradingDate(GetCandle(i).Time);
                    var nextDate = GetTradingDate(GetCandle(i + 1).Time);
                    isRunEnd = nextValue == 0m || nextDate != thisDate;
                }

                if (!isRunEnd)
                    continue;

                if (GetTradingDate(GetCandle(i).Time) != currentChartDate)
                    continue;

                var x = ChartInfo.GetXByBar(i, false) + barWidth;
                var y = ChartInfo.GetYByPrice(value, false);
                DrawProfileLevelLabel(context, label, color, x, y);
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

                var x = chart.GetXByBar(i);
                var color = ApplyOpacity(baseColor, _pvsraVolumeHistogramOpacityPercent);
                var y = Container.Region.Bottom - barHeight;
                context.FillRectangle(color, new Rectangle(x, y, barsWidth, barHeight));
            }
        }

        private void DrawNyLondonCloseMarker(RenderContext context)
        {
            if (!_showNyLondonCloseMarker)
                return;

            var chart = ChartInfo;
            if (chart?.PriceChartContainer is null || Container is null)
                return;

            var firstBar = Math.Max(0, FirstVisibleBarNumber);
            var lastBar = Math.Min(LastVisibleBarNumber, CurrentBar - 1);
            if (lastBar < firstBar)
                return;

            for (var i = firstBar; i <= lastBar; i++)
            {
                var candle = GetCandle(i);
                var dayOfWeek = GetTradingDayOfWeek(candle.Time);
                if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
                    continue;

                var nyRange = _newYorkSessionBox[i];
                if (nyRange.Upper == 0m || nyRange.Lower == 0m)
                    continue;

                var ukDst = _autoSessionDst && IsUkDst(candle.Time);
                var londonClose = ApplyDstShift(_londonSessionClose, ukDst);
                if (!BarContainsTime(i, londonClose))
                    continue;

                var x = chart.GetXByBar(i);
                var topPrice = Math.Max(nyRange.Upper, nyRange.Lower);
                var bottomPrice = Math.Min(nyRange.Upper, nyRange.Lower);
                var yTop = chart.GetYByPrice(topPrice, false);
                var yBottom = chart.GetYByPrice(bottomPrice, false);

                DrawStyledSegment(
                    context,
                    x,
                    yTop,
                    x,
                    yBottom,
                    _nyLondonCloseMarkerColor,
                    _nyLondonCloseMarkerWidth,
                    ProfileLineStyle.Dashed);
            }
        }

        private bool BarContainsTime(int bar, TimeSpan targetTime)
        {
            if (bar < 0 || bar >= CurrentBar)
                return false;

            var start = GetCandle(bar).Time.TimeOfDay;
            TimeSpan end;

            if (bar < CurrentBar - 1)
            {
                end = GetCandle(bar + 1).Time.TimeOfDay;
            }
            else if (bar > 0)
            {
                var prev = GetCandle(bar - 1).Time.TimeOfDay;
                var step = start - prev;
                if (step <= TimeSpan.Zero)
                    step += TimeSpan.FromDays(1);

                end = NormalizeSessionTime(start + step);
            }
            else
            {
                end = NormalizeSessionTime(start + TimeSpan.FromMinutes(1));
            }

            if (start == end)
                return start == targetTime;

            return IsTimeInSession(targetTime, start, end);
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
            _lastCompositeAlertBar = -1;
            _lastCompositeAlertUtc = DateTime.MinValue;
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
            _hasSaturdayDataForPsy = false;
            _lastSaturdayScanBar = -1;
            _vwapSessionDate = DateTime.MinValue;
            _vwapCumulativePriceVolume = 0m;
            _vwapCumulativeVolume = 0m;            _volumeProfiles.Clear();
            _marketProfileCache.Clear();
            _marketProfileDynamicCache.Clear();
            _marketProfileDayRanges.Clear();
            _marketProfileDayRangesLastBar = -1;
            _marketProfileCacheSettingsHash = int.MinValue;
            _lastMarketProfileComputedBar = -1;
            _lastMarketProfileRealtimeUpdateUtc = DateTime.MinValue;
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
            _sessionDate = GetTradingDate(candle.Time);
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

            return GetTradingDate(candle.Time) != _sessionDate;
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
            var tradingDate = GetTradingDate(candle.Time);

            if (bar == 0 || tradingDate != _vwapSessionDate)
            {
                _vwapSessionDate = tradingDate;
                _vwapCumulativePriceVolume = 0m;
                _vwapCumulativeVolume = 0m;
            }

            var typicalPrice = (candle.High + candle.Low + candle.Close) / 3m;
            _vwapCumulativePriceVolume += typicalPrice * candle.Volume;
            _vwapCumulativeVolume += candle.Volume;

            if (!_cleanChart && _showVwap && _vwapCumulativeVolume > 0m)
                _vwapSeries[bar] = _vwapCumulativePriceVolume / _vwapCumulativeVolume;
            else
                _vwapSeries[bar] = 0m;
        }

        private void UpdateMarketProfiles(int bar)
        {
            if (!_showMarketProfile || _cleanChart)
            {
                _volumeProfiles.Clear();
                _marketProfileDynamicCache.Clear();
                _lastMarketProfileComputedBar = -1;
                _lastMarketProfileRealtimeUpdateUtc = DateTime.MinValue;
                return;
            }

            UpdateMarketProfileDayRanges(bar);

            if (bar != CurrentBar - 1)
                return;

            var settingsHash = BuildMarketProfileSettingsHash();
            if (settingsHash != _marketProfileCacheSettingsHash || bar < _lastMarketProfileComputedBar)
            {
                _marketProfileCache.Clear();
                _marketProfileDynamicCache.Clear();
                _marketProfileCacheSettingsHash = settingsHash;
                _lastMarketProfileComputedBar = -1;
                _lastMarketProfileRealtimeUpdateUtc = DateTime.MinValue;
            }

            if (!_marketProfileIncludeCurrentDay && _lastMarketProfileComputedBar == bar)
                return;

            if (bar == _lastMarketProfileComputedBar && _marketProfileRealtimeRefreshMs > 0)
            {
                var elapsedMs = (DateTime.UtcNow - _lastMarketProfileRealtimeUpdateUtc).TotalMilliseconds;
                if (elapsedMs >= 0 && elapsedMs < _marketProfileRealtimeRefreshMs)
                    return;
            }

            _volumeProfiles.Clear();

            var latestDate = GetTradingDate(GetCandle(bar).Time);
            var profileDays = Math.Max(1, _marketProfileHistoryDays);
            var dayRanges = GetRecentProfileDayRanges(profileDays, latestDate);

            var activeDynamicKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var range in BuildMergedProfileRanges(dayRanges))
            {
                var isDynamicRange = _marketProfileIncludeCurrentDay && range.Date == latestDate;
                var cacheKey = BuildMarketProfileCacheKey(range);

                if (isDynamicRange)
                    activeDynamicKeys.Add(cacheKey);

                if (!isDynamicRange && _marketProfileCache.TryGetValue(cacheKey, out var cachedProfile))
                {
                    _volumeProfiles.Add(cachedProfile);
                    continue;
                }

                if (isDynamicRange && TryGetDynamicProfileFromCache(range, cacheKey, out var liveProfile))
                {
                    _volumeProfiles.Add(liveProfile);
                    continue;
                }

                var profile = BuildDayVolumeProfile(range.Date, range.StartBar, range.EndBar);
                if (!profile.IsValid)
                    continue;

                profile.MergeLabel = range.MergeLabel;
                _volumeProfiles.Add(profile);

                if (isDynamicRange)
                {
                    RememberDynamicProfile(cacheKey, range.EndBar, profile);
                    continue;
                }

                _marketProfileCache[cacheKey] = profile;
                if (_marketProfileCache.Count > 512)
                    _marketProfileCache.Clear();
            }

            CleanupDynamicProfileCache(activeDynamicKeys);
            _lastMarketProfileComputedBar = bar;
            _lastMarketProfileRealtimeUpdateUtc = DateTime.UtcNow;
        }

        private void UpdateMarketProfileDayRanges(int bar)
        {
            if (bar < 0)
                return;

            if (bar < _marketProfileDayRangesLastBar)
            {
                _marketProfileDayRanges.Clear();
                _marketProfileDayRangesLastBar = -1;
            }

            var start = Math.Max(0, _marketProfileDayRangesLastBar + 1);
            if (start > bar)
                return;

            for (var i = start; i <= bar; i++)
            {
                var date = GetTradingDate(GetCandle(i).Time);

                if (_marketProfileDayRanges.Count == 0)
                {
                    _marketProfileDayRanges.Add((date, i, i));
                    continue;
                }

                var last = _marketProfileDayRanges[^1];
                if (last.Date == date)
                {
                    last.EndBar = i;
                    _marketProfileDayRanges[^1] = last;
                }
                else
                {
                    _marketProfileDayRanges.Add((date, i, i));
                    if (_marketProfileDayRanges.Count > MaxStoredRanges * 2)
                        _marketProfileDayRanges.RemoveAt(0);
                }
            }

            _marketProfileDayRangesLastBar = bar;
        }

        private List<(DateTime Date, int StartBar, int EndBar)> GetRecentProfileDayRanges(int maxDays, DateTime latestDate)
        {
            var result = new List<(DateTime Date, int StartBar, int EndBar)>();
            if (_marketProfileDayRanges.Count == 0 || maxDays <= 0)
                return result;

            for (var i = _marketProfileDayRanges.Count - 1; i >= 0 && result.Count < maxDays; i--)
            {
                var range = _marketProfileDayRanges[i];
                if (!_marketProfileIncludeCurrentDay && range.Date == latestDate)
                {
                    continue;
                }

                result.Add(range);
            }

            return result;
        }

        private bool TryGetDynamicProfileFromCache(
            (DateTime Date, int StartBar, int EndBar, string MergeLabel) range,
            string cacheKey,
            out DayVolumeProfile profile)
        {
            profile = default!;

            if (!_marketProfileDynamicCache.TryGetValue(cacheKey, out var entry))
                return false;

            if (entry.EndBar != range.EndBar)
                return false;

            var candle = GetCandle(range.EndBar);
            if (entry.EndVolume != candle.Volume ||
                entry.EndHigh != candle.High ||
                entry.EndLow != candle.Low ||
                entry.EndClose != candle.Close)
            {
                return false;
            }

            profile = entry.Profile;
            return profile.IsValid;
        }

        private void RememberDynamicProfile(string cacheKey, int endBar, DayVolumeProfile profile)
        {
            var candle = GetCandle(endBar);
            _marketProfileDynamicCache[cacheKey] = new DynamicProfileCacheEntry
            {
                Profile = profile,
                EndBar = endBar,
                EndVolume = candle.Volume,
                EndHigh = candle.High,
                EndLow = candle.Low,
                EndClose = candle.Close
            };
        }

        private void CleanupDynamicProfileCache(HashSet<string> activeDynamicKeys)
        {
            if (_marketProfileDynamicCache.Count == 0)
                return;

            var staleKeys = _marketProfileDynamicCache.Keys
                .Where(key => !activeDynamicKeys.Contains(key))
                .ToList();

            foreach (var staleKey in staleKeys)
                _marketProfileDynamicCache.Remove(staleKey);
        }

        private List<(DateTime Date, int StartBar, int EndBar, string MergeLabel)> BuildMergedProfileRanges(List<(DateTime Date, int StartBar, int EndBar)> dayRanges)
        {
            var result = new List<(DateTime Date, int StartBar, int EndBar, string MergeLabel)>();
            var count = dayRanges.Count;
            if (count == 0)
                return result;

            var groups = ParseMergeGroups(_marketProfileMergeGroups, count);
            var assigned = new bool[count];
            var shownIndividualDays = new HashSet<int>();

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
                var label = from == to ? from.ToString() : $"{from}-{to}";
                result.Add((newest.Date, oldest.StartBar, newest.EndBar, label));

                for (var i = from; i <= to; i++)
                    assigned[i - 1] = true;

                if (from == to)
                    shownIndividualDays.Add(from);
            }

            for (var i = 0; i < count; i++)
            {
                if (!assigned[i])
                {
                    var dayIndex = i + 1;
                    result.Add((dayRanges[i].Date, dayRanges[i].StartBar, dayRanges[i].EndBar, string.Empty));
                    shownIndividualDays.Add(dayIndex);
                }
            }

            var extraIndividualDays = ParseDayIndexSelection(_marketProfileExtraIndividualDays, count);
            foreach (var dayIndex in extraIndividualDays)
            {
                if (shownIndividualDays.Contains(dayIndex))
                    continue;

                var range = dayRanges[dayIndex - 1];
                result.Add((range.Date, range.StartBar, range.EndBar, dayIndex.ToString()));
                shownIndividualDays.Add(dayIndex);
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

        private static List<int> ParseDayIndexSelection(string text, int maxIndex)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(text) || maxIndex <= 0)
                return result;

            var seen = new HashSet<int>();
            var groups = ParseMergeGroups(text, maxIndex);
            foreach (var group in groups)
            {
                var from = Math.Clamp(group.From, 1, maxIndex);
                var to = Math.Clamp(group.To, 1, maxIndex);
                if (from > to)
                    (from, to) = (to, from);

                for (var day = from; day <= to; day++)
                {
                    if (seen.Add(day))
                        result.Add(day);
                }
            }

            return result;
        }
        private int BuildMarketProfileSettingsHash()
        {
            return HashCode.Combine(
                HashCode.Combine(
                    _marketProfileMaxBinsPerProfile,
                    _marketProfileMinBinSizeTicks,
                    _marketProfilePriceStepTicks,
                    _marketProfileValueAreaPercent,
                    _marketProfileUseSessionFilter,
                    _marketProfileSessionOpen,
                    _marketProfileSessionClose,
                    _marketProfileMergeGroups ?? string.Empty),
                HashCode.Combine(
                    _marketProfileHistoryDays,
                    _marketProfileIncludeCurrentDay,
                    _marketProfileIgnoreSunday,
                    _dayTimeShiftHours),
                _marketProfileExtraIndividualDays ?? string.Empty);
        }
        private static string BuildMarketProfileCacheKey((DateTime Date, int StartBar, int EndBar, string MergeLabel) range)
        {
            return $"{range.Date:yyyyMMdd}:{range.StartBar}:{range.EndBar}:{range.MergeLabel}";
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

            decimal maxVolume = 0m;
            decimal minNonZeroVolume = decimal.MaxValue;
            decimal pocVolume = decimal.MinValue;
            decimal pocPrice = 0m;

            foreach (var level in profile.Levels)
            {
                var volume = level.Value;
                if (volume <= 0m)
                    continue;

                if (volume > maxVolume)
                    maxVolume = volume;

                if (volume < minNonZeroVolume)
                    minNonZeroVolume = volume;

                if (volume > pocVolume || (volume == pocVolume && level.Key < pocPrice))
                {
                    pocVolume = volume;
                    pocPrice = level.Key;
                }
            }

            if (maxVolume <= 0m)
                return profile;

            profile.MaxVolume = maxVolume;
            profile.MinNonZeroVolume = minNonZeroVolume == decimal.MaxValue ? maxVolume : minNonZeroVolume;
            profile.PocPrice = pocPrice;

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

            return IsTimeInSession(GetTradingTimeOfDay(candle.Time), _marketProfileSessionOpen, _marketProfileSessionClose);
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
            var avgVolume = AverageVolume(bar, PvsraLookbackBars);
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
                var highestSpreadVolume = HighestSpreadVolume(bar, PvsraLookbackBars);

                isVector = candle.Volume >= avgVolume * 2m || spreadVolume >= highestSpreadVolume;
                isBlueViolet = candle.Volume >= avgVolume * 1.5m;

                if (isVector)
                    color = isBull ? _vectorGreenColor : _vectorRedColor;
                else if (isBlueViolet)
                    color = isBull ? _vectorBlueColor : _vectorVioletColor;
                else
                    color = isBull ? _regularUpColor : _regularDownColor;
            }

            var isPvsraVector = hasAverage && IsPvsraVectorColor(color);

            _pvsraBars[bar] = _showPvsraCandles ? color.Convert() : null;
            EnsurePvsraColorBuffer(bar);
            _pvsraComputedColors[bar] = color;
            UpdateVectorZones(bar, candle, isPvsraVector, isBull, color);

            if (isVector)
                TryAlertVectorCandle(bar, isBull);

            if (isPvsraVector)
                TryAlertCompositePvsraCandle(bar, candle, isBull);
        }

        private void UpdateVectorZones(int bar, IndicatorCandle candle, bool isPvsraZoneCandidate, bool isBull, Color pvsraColor)
        {
            UpdateExistingVectorZones(_vectorZonesAbove, bar, candle, false);
            UpdateExistingVectorZones(_vectorZonesBelow, bar, candle, true);

            if (!_showVectorZones || !isPvsraZoneCandidate || bar == _lastVectorZoneCreationBar)
                return;

            if (!TryGetZoneRange(candle, _vectorZoneType, isBull, out var top, out var bottom))
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

        private void UpdateExistingVectorZones(List<VectorZone> zones, int bar, IndicatorCandle candle, bool isBelowZone)
        {
            if (zones.Count == 0 || !TryGetZoneRange(candle, _vectorZoneUpdateType, isBelowZone, out var testTop, out var testBottom))
                return;

            for (var i = zones.Count - 1; i >= 0; i--)
            {
                var zone = zones[i];
                zone.EndBar = bar;

                if (bar <= zone.CreatedBar)
                    continue;

                var intersects = testTop >= zone.Bottom && testBottom <= zone.Top;
                if (!intersects)
                    continue;

                if (isBelowZone)
                {
                    var newTop = Math.Min(zone.Top, testBottom);
                    if (newTop <= zone.Bottom)
                    {
                        zones.RemoveAt(i);
                        continue;
                    }

                    zone.Top = newTop;
                    continue;
                }

                var newBottom = Math.Max(zone.Bottom, testTop);
                if (newBottom >= zone.Top)
                {
                    zones.RemoveAt(i);
                    continue;
                }

                zone.Bottom = newBottom;
            }
        }

        private static bool TryGetZoneRange(IndicatorCandle candle, ZoneBoundaryType boundaryType, bool isBelowZone, out decimal top, out decimal bottom)
        {
            var bodyTop = Math.Max(candle.Open, candle.Close);
            var bodyBottom = Math.Min(candle.Open, candle.Close);

            if (boundaryType == ZoneBoundaryType.BodyWithWicks)
            {
                if (isBelowZone)
                {
                    // Bullish recovery zone below price: body plus lower wick.
                    top = bodyTop;
                    bottom = candle.Low;
                }
                else
                {
                    // Bearish recovery zone above price: body plus upper wick.
                    top = candle.High;
                    bottom = bodyBottom;
                }
            }
            else
            {
                top = bodyTop;
                bottom = bodyBottom;
            }

            return top > bottom;
        }

        private bool IsPvsraVectorColor(Color color)
        {
            var argb = color.ToArgb();
            return argb == _vectorRedColor.ToArgb() ||
                argb == _vectorGreenColor.ToArgb() ||
                argb == _vectorVioletColor.ToArgb() ||
                argb == _vectorBlueColor.ToArgb();
        }
        private Color ResolveZoneFillColor(Color pvsraColor)
        {
            if (_vectorRecoveryMatchCandleColor)
                return ApplyTradingViewTransparency(pvsraColor, _vectorZoneTransparency);

            return ApplyTradingViewTransparency(_vectorZoneColor, _vectorZoneTransparency);
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

        private void TryAlertCompositePvsraCandle(int bar, IndicatorCandle candle, bool isBull)
        {
            if (!_useCompositeAlerts || bar != CurrentBar - 1 || bar == _lastCompositeAlertBar)
                return;

            if (_compositeAlertCooldownSeconds > 0 && _lastCompositeAlertUtc != DateTime.MinValue)
            {
                var sinceLastAlert = DateTime.UtcNow - _lastCompositeAlertUtc;
                if (sinceLastAlert.TotalSeconds < _compositeAlertCooldownSeconds)
                    return;
            }

            var tickSize = InstrumentInfo?.TickSize ?? 0m;
            var proximity = tickSize > 0m ? Math.Max(0, _compositeAlertProximityTicks) * tickSize : 0m;
            var reasons = new List<string>();

            if (_compositeAlertUseAdrTouch && _showAdrLevels)
            {
                var adrHigh = _adrHighSeries[bar];
                var adrLow = _adrLowSeries[bar];

                if (adrHigh != 0m && IsPriceNearLevel(candle, adrHigh, proximity))
                    reasons.Add("Hi-ADR");

                if (adrLow != 0m && IsPriceNearLevel(candle, adrLow, proximity))
                    reasons.Add("Lo-ADR");
            }

            if (_compositeAlertUseProfileTouch && _showMarketProfile && _volumeProfiles.Count > 0)
            {
                var currentDate = GetTradingDate(candle.Time);
                var profile = _volumeProfiles.FirstOrDefault(x =>
                    x.IsValid && x.EndBar >= 0 &&
                    GetTradingDate(GetCandle(Math.Clamp(x.EndBar, 0, Math.Max(0, CurrentBar - 1))).Time) == currentDate);

                if (profile is not null && profile.IsValid)
                {
                    if (_showProfilePoc && IsPriceNearLevel(candle, profile.PocPrice, proximity))
                        reasons.Add("POC");

                    if (_showProfileValueArea)
                    {
                        var tick = tickSize > 0m ? tickSize : 0m;
                        var vah = profile.VahPrice + _marketProfileVahOffsetTicks * tick;
                        var val = profile.ValPrice + _marketProfileValOffsetTicks * tick;

                        if (IsPriceNearLevel(candle, vah, proximity))
                            reasons.Add("VAH");

                        if (IsPriceNearLevel(candle, val, proximity))
                            reasons.Add("VAL");
                    }
                }
            }

            if (reasons.Count == 0)
                return;

            var file = string.IsNullOrWhiteSpace(_compositeAlertFile) ? "alert2" : _compositeAlertFile;
            var side = isBull ? "bull" : "bear";
            var details = string.Join(" + ", reasons.Distinct());
            AddAlert(file, $"Composite {side} vector: {details}");

            _lastCompositeAlertBar = bar;
            _lastCompositeAlertUtc = DateTime.UtcNow;
        }

        private static bool IsPriceNearLevel(IndicatorCandle candle, decimal level, decimal tolerance)
        {
            if (level == 0m)
                return false;

            var high = candle.High + Math.Max(0m, tolerance);
            var low = candle.Low - Math.Max(0m, tolerance);
            return level >= low && level <= high;
        }

        private void DrawDailyLevels(int bar)
        {
            if (_cleanChart)
            {
                _prevDayHighSeries[bar] = 0m;
                _prevDayLowSeries[bar] = 0m;
                _adrHighSeries[bar] = 0m;
                _adrLowSeries[bar] = 0m;
                _atrHighSeries[bar] = 0m;
                _atrLowSeries[bar] = 0m;
                _adrHighSeries.Colors[bar] = _adrColor;
                _adrLowSeries.Colors[bar] = _adrColor;
                return;
            }

            var dayAge = GetDayAge(bar);
            var showYDayBar = ShouldRenderByAge(dayAge, _showYDayToday, _showYDayHistorical, _yDayHistoryDays);
            var showAdrBar = ShouldRenderByAge(dayAge, _showAdrToday, _showAdrHistorical, _adrHistoryDays);

            if (_showPreviousDayLevels && _hasPreviousSession && showYDayBar)
            {
                _prevDayHighSeries[bar] = _previousSessionHigh;
                _prevDayLowSeries[bar] = _previousSessionLow;
                BackfillCurrentDayLine(_prevDayHighSeries, bar, _prevDayHighSeries[bar]);
                BackfillCurrentDayLine(_prevDayLowSeries, bar, _prevDayLowSeries[bar]);
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

                BackfillCurrentDayLine(_adrHighSeries, bar, _adrHighSeries[bar]);
                BackfillCurrentDayLine(_adrLowSeries, bar, _adrLowSeries[bar]);

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

                BackfillCurrentDayLine(_atrHighSeries, bar, _atrHighSeries[bar]);
                BackfillCurrentDayLine(_atrLowSeries, bar, _atrLowSeries[bar]);
            }
            else
            {
                _atrHighSeries[bar] = 0m;
                _atrLowSeries[bar] = 0m;
            }
        }

        private void DrawDailyOpen(int bar)
        {
            if (_cleanChart)
            {
                _dailyOpenSeries[bar] = 0m;
                return;
            }

            var dayAge = GetDayAge(bar);
            var showDoBar = ShouldRenderByAge(dayAge, _showDailyOpenToday, _showDailyOpenHistorical, _dailyOpenHistoryDays);

            if (_showDailyOpen && _hasActiveSession && showDoBar)
            {
                _dailyOpenSeries[bar] = _sessionOpen;
                BackfillCurrentDayLine(_dailyOpenSeries, bar, _dailyOpenSeries[bar]);
            }
            else
            {
                _dailyOpenSeries[bar] = 0m;
            }
        }
        private void DrawMidPivotLevels(int bar)
        {
            if (_cleanChart || !_showMidPivotLevels || !_hasPreviousSession || !_hasPreviousCloseForAtr)
            {
                _m0Series[bar] = 0m;
                _m1Series[bar] = 0m;
                _m2Series[bar] = 0m;
                _m3Series[bar] = 0m;
                _m4Series[bar] = 0m;
                _m5Series[bar] = 0m;
                return;
            }

            var pivotPoint = (_previousSessionHigh + _previousSessionLow + _previousSessionClose) / 3m;
            var pivR1 = 2m * pivotPoint - _previousSessionLow;
            var pivS1 = 2m * pivotPoint - _previousSessionHigh;
            var pivR2 = pivotPoint - pivS1 + pivR1;
            var pivS2 = pivotPoint - pivR1 + pivS1;
            var pivR3 = 2m * pivotPoint + _previousSessionHigh - 2m * _previousSessionLow;
            var pivS3 = 2m * pivotPoint - (2m * _previousSessionHigh - _previousSessionLow);

            _m0Series[bar] = (pivS2 + pivS3) / 2m;
            _m1Series[bar] = (pivS1 + pivS2) / 2m;
            _m2Series[bar] = (pivotPoint + pivS1) / 2m;
            _m3Series[bar] = (pivotPoint + pivR1) / 2m;
            _m4Series[bar] = (pivR1 + pivR2) / 2m;
            _m5Series[bar] = (pivR2 + pivR3) / 2m;

            BackfillCurrentDayLine(_m0Series, bar, _m0Series[bar]);
            BackfillCurrentDayLine(_m1Series, bar, _m1Series[bar]);
            BackfillCurrentDayLine(_m2Series, bar, _m2Series[bar]);
            BackfillCurrentDayLine(_m3Series, bar, _m3Series[bar]);
            BackfillCurrentDayLine(_m4Series, bar, _m4Series[bar]);
            BackfillCurrentDayLine(_m5Series, bar, _m5Series[bar]);
        }
        private void BackfillCurrentDayLine(ValueDataSeries series, int bar, decimal value)
        {
            if (bar <= 0 || value == 0m)
                return;

            var date = GetTradingDate(GetCandle(bar).Time);
            if (GetTradingDate(GetCandle(bar - 1).Time) != date || series[bar - 1] != 0m)
                return;

            var startBar = bar - 1;
            while (startBar > 0 && GetTradingDate(GetCandle(startBar - 1).Time) == date)
                startBar--;

            for (var i = startBar; i < bar; i++)
                series[i] = value;
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
            var dayOfWeek = GetTradingDayOfWeek(candle.Time);
            var isWeekend = dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday;
            var showSessionBar = !_cleanChart && !isWeekend && (dayAge == 0 || (_showSessionHistorical && dayAge <= _sessionHistoryDays));
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
            if (_cleanChart || !_showWeeklyPsy)
            {
                _psyHighSeries[bar] = 0m;
                _psyLowSeries[bar] = 0m;
                return;
            }

            var shiftedTime = GetShiftedDateTime(candle.Time);
            var shiftedDate = shiftedTime.Date;
            var psyWeekStartDay = GetPsyWeekStartDay(bar);
            var weekKey = GetWeekKey(shiftedDate, psyWeekStartDay);
            if (weekKey != _psyWeekKey)
            {
                _psyWeekKey = weekKey;
                _psyHigh = 0m;
                _psyLow = 0m;
                _hasPsyValue = false;
            }

            var weekStart = StartOfWeek(shiftedDate, psyWeekStartDay);
            var psyStart = weekStart;
            var psyEnd = weekStart.AddHours(8);

            if (shiftedTime >= psyStart && shiftedTime < psyEnd)
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

            var weekAge = GetWeekAge(bar, psyWeekStartDay);
            var showPsyBar = ShouldRenderByAge(weekAge, _showWeeklyPsyToday, _showWeeklyPsyHistorical, _weeklyPsyHistoryWeeks);

            if (_hasPsyValue && showPsyBar && shiftedTime >= psyStart)
            {
                _psyHighSeries[bar] = _psyHigh;
                _psyLowSeries[bar] = _psyLow;
                BackfillCurrentDayLine(_psyHighSeries, bar, _psyHighSeries[bar]);
                BackfillCurrentDayLine(_psyLowSeries, bar, _psyLowSeries[bar]);
            }
            else
            {
                _psyHighSeries[bar] = 0m;
                _psyLowSeries[bar] = 0m;
            }
        }

        private DayOfWeek GetPsyWeekStartDay(int bar)
        {
            UpdatePsySaturdayAvailability(bar);
            return _hasSaturdayDataForPsy ? DayOfWeek.Saturday : DayOfWeek.Monday;
        }

        private void UpdatePsySaturdayAvailability(int bar)
        {
            if (_hasSaturdayDataForPsy || bar <= _lastSaturdayScanBar)
                return;

            var start = Math.Max(0, _lastSaturdayScanBar + 1);
            for (var i = start; i <= bar; i++)
            {
                if (GetTradingDayOfWeek(GetCandle(i).Time) == DayOfWeek.Saturday)
                {
                    _hasSaturdayDataForPsy = true;
                    break;
                }
            }

            _lastSaturdayScanBar = bar;
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

        private decimal RangeStandardDeviation(int length)
        {
            if (_closedDailyRanges.Count == 0 || length <= 0)
                return 0m;

            var count = Math.Min(length, _closedDailyRanges.Count);
            if (count <= 1)
                return 0m;

            var ranges = _closedDailyRanges.Skip(_closedDailyRanges.Count - count).ToList();
            if (ranges.Count <= 1)
                return 0m;

            var mean = ranges.Average();
            double sumSquares = 0d;
            foreach (var range in ranges)
            {
                var diff = (double)(range - mean);
                sumSquares += diff * diff;
            }

            var variance = sumSquares / ranges.Count;
            return (decimal)Math.Sqrt(Math.Max(0d, variance));
        }

        private bool IsFirstBarOfDay(int bar)
        {
            if (bar <= 0)
                return true;

            return GetTradingDate(GetCandle(bar).Time) != GetTradingDate(GetCandle(bar - 1).Time);
        }

        private bool IsFirstBarOfWeek(int bar)
        {
            if (bar <= 0)
                return true;

            var currentWeek = StartOfWeek(GetTradingDate(GetCandle(bar).Time), DayOfWeek.Monday);
            var prevWeek = StartOfWeek(GetTradingDate(GetCandle(bar - 1).Time), DayOfWeek.Monday);
            return currentWeek != prevWeek;
        }

        private int GetDayAge(int bar)
        {
            if (bar < 0)
                return 0;

            var sourceCount = (SourceDataSeries as IDataSeries)?.Count ?? 0;
            var lastBarIndex = sourceCount > 0 ? sourceCount - 1 : Math.Max(0, CurrentBar - 1);

            var latestDate = GetTradingDate(GetCandle(lastBarIndex).Time);
            var barDate = GetTradingDate(GetCandle(bar).Time);
            var age = (latestDate - barDate).Days;
            return age < 0 ? 0 : age;
        }

        private int GetWeekAge(int bar)
        {
            return GetWeekAge(bar, DayOfWeek.Monday);
        }

        private int GetWeekAge(int bar, DayOfWeek weekStartDay)
        {
            if (bar < 0)
                return 0;

            var sourceCount = (SourceDataSeries as IDataSeries)?.Count ?? 0;
            var lastBarIndex = sourceCount > 0 ? sourceCount - 1 : Math.Max(0, CurrentBar - 1);

            var latestWeekStart = StartOfWeek(GetTradingDate(GetCandle(lastBarIndex).Time), weekStartDay);
            var barWeekStart = StartOfWeek(GetTradingDate(GetCandle(bar).Time), weekStartDay);
            var age = (int)((latestWeekStart - barWeekStart).TotalDays / 7d);
            return age < 0 ? 0 : age;
        }

        private static int GetWeekKey(DateTime date)
        {
            return GetWeekKey(date, DayOfWeek.Monday);
        }

        private static int GetWeekKey(DateTime date, DayOfWeek weekStartDay)
        {
            var weekStart = StartOfWeek(date, weekStartDay);
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
            var cleanChart = _cleanChart;

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
            _vwapSeries.VisualType = !cleanChart && _showVwap ? VisualMode.Line : VisualMode.Hide;

            _emaCloud.RangeColor = _emaCloudColor.Convert();
            _emaCloud.IsHidden = !(_showEmas && _showEmaCloud);

            _prevDayHighSeries.Color = _previousDayLevelsColor.Convert();
            _prevDayHighSeries.Width = _previousDayLevelsWidth;
            _prevDayHighSeries.LineDashStyle = (LineDashStyle)2;
            _prevDayHighSeries.ShowCurrentValue = false;
            _prevDayHighSeries.VisualType = !cleanChart && _showPreviousDayLevels ? VisualMode.Line : VisualMode.Hide;

            _prevDayLowSeries.Color = _previousDayLevelsColor.Convert();
            _prevDayLowSeries.Width = _previousDayLevelsWidth;
            _prevDayLowSeries.LineDashStyle = (LineDashStyle)2;
            _prevDayLowSeries.ShowCurrentValue = false;
            _prevDayLowSeries.VisualType = !cleanChart && _showPreviousDayLevels ? VisualMode.Line : VisualMode.Hide;

            _adrHighSeries.Color = _adrColor.Convert();
            _adrHighSeries.Width = _adrWidth;
            _adrHighSeries.LineDashStyle = LineDashStyle.Solid;
            _adrHighSeries.ShowCurrentValue = false;
            _adrHighSeries.VisualType = !cleanChart && _showAdrLevels ? VisualMode.Line : VisualMode.Hide;

            _adrLowSeries.Color = _adrColor.Convert();
            _adrLowSeries.Width = _adrWidth;
            _adrLowSeries.LineDashStyle = LineDashStyle.Solid;
            _adrLowSeries.ShowCurrentValue = false;
            _adrLowSeries.VisualType = !cleanChart && _showAdrLevels ? VisualMode.Line : VisualMode.Hide;

            _atrHighSeries.Color = _atrColor.Convert();
            _atrHighSeries.Width = _atrWidth;
            _atrHighSeries.ShowCurrentValue = false;
            _atrHighSeries.VisualType = !cleanChart && _showAtrLevels ? VisualMode.Line : VisualMode.Hide;

            _atrLowSeries.Color = _atrColor.Convert();
            _atrLowSeries.Width = _atrWidth;
            _atrLowSeries.ShowCurrentValue = false;
            _atrLowSeries.VisualType = !cleanChart && _showAtrLevels ? VisualMode.Line : VisualMode.Hide;

            _dailyOpenSeries.Color = _dailyOpenColor.Convert();
            _dailyOpenSeries.Width = _dailyOpenWidth;
            _dailyOpenSeries.LineDashStyle = LineDashStyle.Solid;
            _dailyOpenSeries.ShowCurrentValue = false;
            _dailyOpenSeries.VisualType = !cleanChart && _showDailyOpen ? VisualMode.Line : VisualMode.Hide;

            var midPivotDashStyle = ToLineDashStyle(_midPivotLineStyle);

            _m0Series.Color = _midPivotColor.Convert();
            _m0Series.Width = _midPivotWidth;
            _m0Series.LineDashStyle = midPivotDashStyle;
            _m0Series.ShowCurrentValue = false;
            _m0Series.VisualType = !cleanChart && _showMidPivotLevels ? VisualMode.Line : VisualMode.Hide;

            _m1Series.Color = _midPivotColor.Convert();
            _m1Series.Width = _midPivotWidth;
            _m1Series.LineDashStyle = midPivotDashStyle;
            _m1Series.ShowCurrentValue = false;
            _m1Series.VisualType = !cleanChart && _showMidPivotLevels ? VisualMode.Line : VisualMode.Hide;

            _m2Series.Color = _midPivotColor.Convert();
            _m2Series.Width = _midPivotWidth;
            _m2Series.LineDashStyle = midPivotDashStyle;
            _m2Series.ShowCurrentValue = false;
            _m2Series.VisualType = !cleanChart && _showMidPivotLevels ? VisualMode.Line : VisualMode.Hide;

            _m3Series.Color = _midPivotColor.Convert();
            _m3Series.Width = _midPivotWidth;
            _m3Series.LineDashStyle = midPivotDashStyle;
            _m3Series.ShowCurrentValue = false;
            _m3Series.VisualType = !cleanChart && _showMidPivotLevels ? VisualMode.Line : VisualMode.Hide;

            _m4Series.Color = _midPivotColor.Convert();
            _m4Series.Width = _midPivotWidth;
            _m4Series.LineDashStyle = midPivotDashStyle;
            _m4Series.ShowCurrentValue = false;
            _m4Series.VisualType = !cleanChart && _showMidPivotLevels ? VisualMode.Line : VisualMode.Hide;

            _m5Series.Color = _midPivotColor.Convert();
            _m5Series.Width = _midPivotWidth;
            _m5Series.LineDashStyle = midPivotDashStyle;
            _m5Series.ShowCurrentValue = false;
            _m5Series.VisualType = !cleanChart && _showMidPivotLevels ? VisualMode.Line : VisualMode.Hide;

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
            _londonSessionBox.IsHidden = cleanChart || !_showLondonSession;

            _newYorkSessionBox.RangeColor = WithOpacity(_newYorkSessionColor, _sessionWidth).Convert();
            _newYorkSessionBox.IsHidden = cleanChart || !_showNewYorkSession;

            _asiaSessionBox.RangeColor = WithOpacity(_asiaSessionColor, _sessionWidth).Convert();
            _asiaSessionBox.IsHidden = cleanChart || !_showAsiaSession;

            _euBrinksSessionBox.RangeColor = WithOpacity(_euBrinksSessionColor, _sessionWidth).Convert();
            _euBrinksSessionBox.IsHidden = cleanChart || !_showEuBrinksSession;

            _usBrinksSessionBox.RangeColor = WithOpacity(_usBrinksSessionColor, _sessionWidth).Convert();
            _usBrinksSessionBox.IsHidden = cleanChart || !_showUsBrinksSession;

            _psyHighSeries.Color = _weeklyPsyHighColor.Convert();
            _psyHighSeries.Width = _weeklyPsyWidth;
            _psyHighSeries.LineDashStyle = LineDashStyle.Solid;
            _psyHighSeries.ShowCurrentValue = false;
            _psyHighSeries.VisualType = !cleanChart && _showWeeklyPsy ? VisualMode.Line : VisualMode.Hide;

            _psyLowSeries.Color = _weeklyPsyLowColor.Convert();
            _psyLowSeries.Width = _weeklyPsyWidth;
            _psyLowSeries.LineDashStyle = LineDashStyle.Solid;
            _psyLowSeries.ShowCurrentValue = false;
            _psyLowSeries.VisualType = !cleanChart && _showWeeklyPsy ? VisualMode.Line : VisualMode.Hide;
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

        private static LineDashStyle ToLineDashStyle(ProfileLineStyle style)
        {
            return style switch
            {
                ProfileLineStyle.Dotted => (LineDashStyle)1,
                ProfileLineStyle.Dashed => (LineDashStyle)2,
                _ => LineDashStyle.Solid
            };
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

        private void ApplyQuickPreset(QuickPreset preset)
        {
            if (_applyingQuickPreset)
                return;

            _applyingQuickPreset = true;
            try
            {
                _showLondonSession = false;
                _marketProfileIgnoreSunday = false;

                switch (preset)
                {
                    case QuickPreset.Clean:
                        _cleanChart = true;
                        _showEmas = true;
                        _showPvsraCandles = true;
                        _showPvsraVolumeHistogram = true;
                        break;

                    case QuickPreset.Crypto:
                        _cleanChart = false;
                        _showEmas = true;
                        _showPvsraCandles = true;
                        _showPvsraVolumeHistogram = true;
                        _showPreviousDayLevels = true;
                        _showAdrLevels = true;
                        _showDailyOpen = true;
                        _showMidPivotLevels = true;
                        _showWeeklyPsy = true;
                        _showVwap = true;
                        _showMarketProfile = true;
                        _showLondonSession = false;
                        _showNewYorkSession = true;
                        _showAsiaSession = true;
                        _showEuBrinksSession = true;
                        _showUsBrinksSession = true;
                        _marketProfileIgnoreSunday = false;
                        break;

                    case QuickPreset.Forex:
                        _cleanChart = false;
                        _showEmas = true;
                        _showPvsraCandles = true;
                        _showPvsraVolumeHistogram = true;
                        _showPreviousDayLevels = true;
                        _showAdrLevels = true;
                        _showDailyOpen = true;
                        _showMidPivotLevels = true;
                        _showWeeklyPsy = true;
                        _showVwap = true;
                        _showMarketProfile = true;
                        _showLondonSession = false;
                        _showNewYorkSession = true;
                        _showAsiaSession = true;
                        _showEuBrinksSession = true;
                        _showUsBrinksSession = true;
                        _marketProfileIgnoreSunday = false;
                        break;

                    default:
                        _cleanChart = false;
                        _showEmas = true;
                        _showPvsraCandles = true;
                        _showPvsraVolumeHistogram = true;
                        _showPreviousDayLevels = true;
                        _showAdrLevels = true;
                        _showDailyOpen = true;
                        _showMidPivotLevels = true;
                        _showWeeklyPsy = true;
                        _showVwap = true;
                        _showMarketProfile = true;
                        _showLondonSession = false;
                        _showNewYorkSession = true;
                        _showAsiaSession = true;
                        _showEuBrinksSession = true;
                        _showUsBrinksSession = true;
                        break;
                }

                ApplySeriesStyles();
            }
            finally
            {
                _applyingQuickPreset = false;
            }
        }
        private bool ApplyPendingMarketProfileChanges()
        {
            var normalizedMerge = _marketProfileMergeGroupsPending?.Trim() ?? string.Empty;
            var normalizedExtraIndividual = _marketProfileExtraIndividualDaysPending?.Trim() ?? string.Empty;
            var normalizedDays = Math.Clamp(_marketProfileHistoryDaysPending, 1, 50);
            var changed = false;

            if (_marketProfileMergeGroups != normalizedMerge)
            {
                _marketProfileMergeGroups = normalizedMerge;
                changed = true;
            }

            if (_marketProfileExtraIndividualDays != normalizedExtraIndividual)
            {
                _marketProfileExtraIndividualDays = normalizedExtraIndividual;
                changed = true;
            }

            if (_marketProfileHistoryDays != normalizedDays)
            {
                _marketProfileHistoryDays = normalizedDays;
                changed = true;
            }

            if (!changed)
                return false;

            _marketProfileCache.Clear();
            _marketProfileDynamicCache.Clear();
            _marketProfileCacheSettingsHash = int.MinValue;
            _lastMarketProfileComputedBar = -1;
            _lastMarketProfileRealtimeUpdateUtc = DateTime.MinValue;
            return true;
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

namespace CustomIndicators
{
    [DisplayName("PVA Volume")]
    [Description("Volume histogram with PVSRA candle colors and MA overlay.")]
    public class PVA_Volume : Indicator
    {

        private const string GroupColors = "01. PVSRA Colors";
        private const string GroupMa = "02. MA";

        private readonly ValueDataSeries _volumeSeries = new("PVAVolume", "PVA Volume")
        {
            VisualType = VisualMode.Histogram,
            ShowZeroValue = false,
            UseMinimizedModeIfEnabled = true,
            ResetAlertsOnNewBar = true
        };

        private readonly ValueDataSeries _maSeries = new("PVAVolumeMA", "MA 50")
        {
            VisualType = VisualMode.Line,
            Width = 2,
            UseMinimizedModeIfEnabled = true,
            IgnoredByAlerts = true
        };

        private const int PvsraLookbackBars = 10;
        private Color _vectorRedColor = Color.Red;
        private Color _vectorGreenColor = Color.Lime;
        private Color _vectorVioletColor = Color.Fuchsia;
        private Color _vectorBlueColor = Color.Blue;
        private Color _regularUpColor = Color.FromArgb(153, 153, 153);
        private Color _regularDownColor = Color.FromArgb(77, 77, 77);

        private bool _showMa = true;
        private int _maPeriod = 50;
        private int _maWidth = 2;
        private Color _maColor = Color.Gold;

        [Display(Name = "Regular Up Color", GroupName = GroupColors, Order = 10)]
        public CrossColor RegularUpColor
        {
            get => _regularUpColor.Convert();
            set => SetAndRecalculate(ref _regularUpColor, value.Convert());
        }

        [Display(Name = "Regular Down Color", GroupName = GroupColors, Order = 20)]
        public CrossColor RegularDownColor
        {
            get => _regularDownColor.Convert();
            set => SetAndRecalculate(ref _regularDownColor, value.Convert());
        }

        [Display(Name = "Vector Green Color", GroupName = GroupColors, Order = 30)]
        public CrossColor VectorGreenColor
        {
            get => _vectorGreenColor.Convert();
            set => SetAndRecalculate(ref _vectorGreenColor, value.Convert());
        }

        [Display(Name = "Vector Red Color", GroupName = GroupColors, Order = 40)]
        public CrossColor VectorRedColor
        {
            get => _vectorRedColor.Convert();
            set => SetAndRecalculate(ref _vectorRedColor, value.Convert());
        }

        [Display(Name = "Vector Blue Color", GroupName = GroupColors, Order = 50)]
        public CrossColor VectorBlueColor
        {
            get => _vectorBlueColor.Convert();
            set => SetAndRecalculate(ref _vectorBlueColor, value.Convert());
        }

        [Display(Name = "Vector Violet Color", GroupName = GroupColors, Order = 60)]
        public CrossColor VectorVioletColor
        {
            get => _vectorVioletColor.Convert();
            set => SetAndRecalculate(ref _vectorVioletColor, value.Convert());
        }

        [Display(Name = "Show MA", GroupName = GroupMa, Order = 10)]
        public bool ShowMa
        {
            get => _showMa;
            set => SetAndRecalculate(ref _showMa, value);
        }

        [Display(Name = "MA Period", GroupName = GroupMa, Order = 20)]
        [Range(1, 500)]
        public int MaPeriod
        {
            get => _maPeriod;
            set => SetAndRecalculate(ref _maPeriod, Math.Max(1, value));
        }

        [Display(Name = "MA Color", GroupName = GroupMa, Order = 30)]
        public CrossColor MaColor
        {
            get => _maColor.Convert();
            set => SetAndRecalculate(ref _maColor, value.Convert());
        }

        [Display(Name = "MA Width", GroupName = GroupMa, Order = 40)]
        [Range(1, 10)]
        public int MaWidth
        {
            get => _maWidth;
            set => SetAndRecalculate(ref _maWidth, Math.Max(1, value));
        }

        public PVA_Volume()
            : base(true)
        {
            Panel = IndicatorDataProvider.NewPanel;

            DataSeries[0] = _volumeSeries;
            DataSeries.Add(_maSeries);

            ApplySeriesStyles();
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            var volume = candle.Volume;

            if (bar == 0)
                ApplySeriesStyles();

            _volumeSeries[bar] = volume;

            var avgVolume = AverageVolume(bar, PvsraLookbackBars);
            var isBull = candle.Close >= candle.Open;
            var hasAverage = avgVolume > 0m;
            Color color;

            if (!hasAverage)
            {
                color = isBull ? _regularUpColor : _regularDownColor;
            }
            else
            {
                var spread = Math.Max(0m, candle.High - candle.Low);
                var spreadVolume = spread * candle.Volume;
                var highestSpreadVolume = HighestSpreadVolume(bar, PvsraLookbackBars);

                var isVector = candle.Volume >= avgVolume * 2m || spreadVolume >= highestSpreadVolume;
                var isBlueViolet = candle.Volume >= avgVolume * 1.5m;

                if (isVector)
                    color = isBull ? _vectorGreenColor : _vectorRedColor;
                else if (isBlueViolet)
                    color = isBull ? _vectorBlueColor : _vectorVioletColor;
                else
                    color = isBull ? _regularUpColor : _regularDownColor;
            }

            _volumeSeries.Colors[bar] = color;

            if (_showMa)
                _maSeries[bar] = CalculateVolumeSma(bar, _maPeriod);
            else
                _maSeries[bar] = 0m;
        }

        private decimal CalculateVolumeSma(int bar, int period)
        {
            var start = Math.Max(0, bar - period + 1);
            decimal sum = 0m;
            var count = 0;

            for (var i = start; i <= bar; i++)
            {
                sum += _volumeSeries[i];
                count++;
            }

            return count > 0 ? sum / count : 0m;
        }

        private decimal AverageVolume(int bar, int length)
        {
            var start = Math.Max(0, bar - length + 1);
            decimal sum = 0m;
            var count = 0;

            for (var i = start; i <= bar; i++)
            {
                sum += GetCandle(i).Volume;
                count++;
            }

            return count > 0 ? sum / count : 0m;
        }

        private decimal HighestSpreadVolume(int bar, int length)
        {
            var start = Math.Max(0, bar - length + 1);
            decimal highest = 0m;

            for (var i = start; i <= bar; i++)
            {
                var c = GetCandle(i);
                var spreadVolume = Math.Max(0m, c.High - c.Low) * c.Volume;
                if (spreadVolume > highest)
                    highest = spreadVolume;
            }

            return highest;
        }

        private void ApplySeriesStyles()
        {
            _maSeries.Color = _maColor.Convert();
            _maSeries.Width = Math.Max(1, _maWidth);
            _maSeries.VisualType = _showMa ? VisualMode.Line : VisualMode.Hide;
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































































