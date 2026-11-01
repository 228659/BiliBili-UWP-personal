using BiliBili_Lib.Enums;
using BiliBili_Lib.Models.BiliBili;
using BiliBili_Lib.Models.BiliBili.Anime;
using BiliBili_Lib.Models.BiliBili.Video;
using BiliBili_Lib.Service;
using BiliBili_Lib.Tools;
using BiliBili_UWP.Components.Widgets;
using BiliBili_UWP.Models.Enums;
using BiliBili_UWP.Models.UI;
using BiliBili_UWP.Models.UI.Others;
using Microsoft.Graphics.Canvas;
using Microsoft.Toolkit.Uwp.Helpers;
using NSDanmaku.Helper;
using NSDanmaku.Model;
using Richasy.Font.UWP;
using SYEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace BiliBili_UWP.Components.Controls
{
    public sealed partial class VideoPlayer : UserControl
    {
        #region 集合
        private ObservableCollection<Tuple<int, string>> QualityCollection = new ObservableCollection<Tuple<int, string>>();
        private ObservableCollection<Tuple<double, string>> PlayRateCollection;
        private ObservableCollection<Choice> ChoiceCollection = new ObservableCollection<Choice>();
        private ObservableCollection<SystemFont> FontCollection = new ObservableCollection<SystemFont>();
        private ObservableCollection<SubtitleIndexItem> SubtitleIndexCollection = new ObservableCollection<SubtitleIndexItem>();
        private List<DanmakuColor> DanmakuColors = DanmakuColor.GetColorList();
        private List<SubtitleItem> Subtitles = new List<SubtitleItem>();
        private List<DanmakuModel> DanmakuList = new List<DanmakuModel>();
        private List<string> ShieldTextList = new List<string>(); //屏蔽关键字列表
        private List<string> SendDanmakuList = new List<string>();
        #endregion

        #region 变量
        private VideoPlayBase _playData = null;
        private int _currentQn = 0;
        private MediaPlayer _player = new MediaPlayer();
        private MediaSource _tempSource = null;
        private int _pointerHoldCount = 0; // 光标保持不动的持续时间
        private int _heartBeatCount = 0;
        private Point _lastPointerStayPoint = new Point(0, 0);
        private bool _isCatchPointer = false;
        private bool _isMergeSameDanmaku = false; //合并相同弹幕
        private int _maxDanmakuNumber = 0;
        public bool IsFocus = false;
        private DisplayRequest dispRequest = null;
        private InteractionVideo _interaction = null;
        private bool _isMediaElementLoaded = false;

        private long _videoId = 0;
        private long _partId = 0;
        private Episode _bangumiPart = null;
        private VideoDetail _videoDetail = null;
        private BangumiDetail _bangumiDetail = null;

        public bool isBangumi = false;
        private bool _isChoiceHandling = false;
        private bool _isMTCShow = true;

        public int _skipStep = 0;
        private bool _isDanmakuOptionsInit = false;

        private double _playRate = 1;
        private int _tipShowSeconds = 0;
        private int _historyShowSeconds = 0;
        private bool _needShowHistory = false;

        private bool _isDanmakuBarFocus = false;
        private bool _isPlayerLocked = false;

        public int CurrentProgress
        {
            get => _player == null ? 0 : Convert.ToInt32(_player.PlaybackSession.Position.TotalSeconds);
        }

        private Point _manipulationStartPoint = new Point(0, 0);
        private double _manipulationDeltaX = 0d;
        private double _manipulationDeltaY = 0d;
        private double _manipulationProgress = 0d;
        private double _manipulationVolume = 0d;
        private double _manipulationUnitLength = 0d;
        private bool _manipulationBeforeIsPlay = false;
        private PlayerManipulationType _manipulationType = PlayerManipulationType.None;
        #endregion

        #region 服务及控件
        private VideoService _videoService = App.BiliViewModel._client.Video;
        private AnimeService _animeService = App.BiliViewModel._client.Anime;

        private DanmakuParse _danmakuParse = new DanmakuParse();
        private NSDanmaku.Controls.Danmaku DanmakuControls = null;

        private DispatcherTimer _defaultTimer = new DispatcherTimer();
        private DispatcherTimer _subtitleTimer = new DispatcherTimer();

        private int _sourceGeneration = 0;
        private int _danmakuGeneration = 0;
        private int _subtitleGeneration = 0;
        private int _danmakuCursor = 0;
        private double _danmakuEmitPosition = -1;
        private readonly Dictionary<long, List<DanmakuModel>> _danmakuCache = new Dictionary<long, List<DanmakuModel>>();

        public VideoTransportControls MTC;
        #endregion

        #region 事件
        public event EventHandler<bool> FullWindowChanged;
        public event EventHandler<bool> CinemaChanged;
        public event EventHandler<bool> CompactOverlayChanged;
        public event RoutedEventHandler SeparateButtonClick;
        public event EventHandler MTCLoaded;
        public event EventHandler<long> MediaEnded;
        public event EventHandler<int> PartSwitched;
        public event EventHandler PreviousVideoRequest;
        public event EventHandler NextVideoRequest;
        #endregion

        public VideoPlayer()
        {
            this.InitializeComponent();
            _defaultTimer.Interval = TimeSpan.FromSeconds(1);
            _defaultTimer.Tick += DefaultTimer_Tick;
            _subtitleTimer.Interval = TimeSpan.FromSeconds(0.25);
            _subtitleTimer.Tick += SubtitleTimer_Tick;
            DanmakuControls = VideoMTC.DanmakuControls;
            PlayRateCollection = new ObservableCollection<Tuple<double, string>>
            {
                new Tuple<double, string>(0.5,"0.5倍速"),
                new Tuple<double, string>(0.75,"0.75倍速"),
                new Tuple<double, string>(1,"正常播放"),
                new Tuple<double, string>(1.25,"1.25倍速"),
                new Tuple<double, string>(1.5,"1.5倍速"),
                new Tuple<double, string>(2,"2倍速"),
            };
            MTC = VideoMTC;
        }

        #region 加载
        public async Task Init(VideoDetail detail, long cid = 0)
        {
            _videoDetail = detail;
            _videoId = detail.aid;
            isBangumi = false;
            _needShowHistory = true;
            if (cid == 0)
            {
                if (detail != null && detail.pages != null)
                {
                    cid = detail.pages.First().cid;
                }
            }
            mediaElement.PosterSource = new BitmapImage { DecodePixelWidth = 960, UriSource = new Uri(detail.pic) };
            HideNextPart();
            SetPlayerMode();
            ResetPlayRate();
            if (detail.interaction != null)
            {
                int nodeId = 0;
                if (detail.interaction.history_node != null)
                {
                    cid = detail.interaction.history_node.cid;
                    nodeId = detail.interaction.history_node.node_id;
                }
                await InitInteraction(cid, nodeId);
            }
            else
            {
                Reset();
                int progress = 0;
                await RefreshVideoSource(cid, progress);
            }
            UpdateMediaProperties(detail.title, "", detail.pic);
        }
        public async Task Init(BangumiDetail detail, Episode part)
        {
            isBangumi = true;
            _bangumiDetail = detail;
            _bangumiPart = part;
            _needShowHistory = true;
            Reset();
            ResetPlayRate();
            HideNextPart();
            mediaElement.PosterSource = new BitmapImage { DecodePixelWidth = 960, UriSource = new Uri(detail.cover) };
            SetPlayerMode();
            await RefreshVideoSource(part);
        }
        private void SetPlayerMode()
        {
            string mode = AppTool.GetLocalSetting(Settings.PlayerMode, "Default");
            if (mode == "Cinema")
                MTC.IsCinema = true;
            else if (mode == "Full")
                MTC.IsFullWindow = true;
        }
        private void Reset()
        {
            ErrorContainer.Visibility = Visibility.Collapsed;
            ErrorBlock.Text = "嗨呀，加载失败啦！";
            InteractionHomeButton.Visibility = Visibility.Collapsed;
            InteractionEndContainer.Visibility = Visibility.Collapsed;
            HistoryContainer.Visibility = Visibility.Collapsed;
            ChoiceItemsControl.Visibility = Visibility.Collapsed;

            bool isShowDanmaku = AppTool.GetBoolSetting(Settings.IsDanmakuOpen);
            DanmakuVisibilityButton.Content = isShowDanmaku ? "" : "";
            DanmakuColorItemsControl.ItemsSource = DanmakuColors;
            ColorTextBox.Text = "#FFFFFF";
            DefaultFontSizeRadio.IsChecked = true;
            ModeComboBox.SelectedIndex = 0;
            ColorViewBorder.Background = new SolidColorBrush(Windows.UI.Colors.White);
            DanmakuBox.Text = "";
            _skipStep = Convert.ToInt32(AppTool.GetLocalSetting(Settings.PlayerSkipStep, "30"));

            _playData = null;
            _currentQn = 0;
            _sourceGeneration++;
            _danmakuGeneration++;
            _subtitleGeneration++;
            _subtitleTimer.Stop();
            QualityCollection.Clear();
            DanmakuList = new List<DanmakuModel>();
            _danmakuCursor = 0;
            _danmakuEmitPosition = -1;
            SendDanmakuList.Clear();
            Subtitles.Clear();
            ChoiceCollection.Clear();
            SubtitleIndexCollection.Clear();
            _isMTCShow = true;
            ShowMTC();
            HideSubtitle();
            if (mediaElement.MediaPlayer != null)
            {
                mediaElement.MediaPlayer.Pause();
                mediaElement.MediaPlayer.TimelineControllerPositionOffset = TimeSpan.FromSeconds(0);
            }
            mediaElement.SetMediaPlayer(null);
            ReleasePlayer();
            if (_tempSource != null)
            {
                _tempSource.Dispose();
                _tempSource = null;
            }
            _player = new MediaPlayer();
            double volume = Convert.ToDouble(AppTool.GetLocalSetting(Settings.PlayerLastVolume, "1"));
            _player.Volume = volume;
            bool isAutoPlay = AppTool.GetBoolSetting(Settings.IsAutoPlay);
            _player.AutoPlay = isAutoPlay;
            _player.MediaEnded += Media_Ended;
            _player.MediaFailed += Media_Failed;
            _player.MediaOpened += Media_Opened;
            _player.PlaybackSession.PositionChanged += Media_PositionChanged;
            _player.PlaybackSession.PlaybackStateChanged += Media_StatusChanged;
            _player.VolumeChanged += Volume_Changed;
            mediaElement.SetMediaPlayer(_player);
            if (DanmakuControls != null)
                DanmakuControls.ClearAll();
            _defaultTimer.Start();
        }
        #endregion

        #region 媒体播放事件
        private async void Media_PositionChanged(MediaPlaybackSession sender, object args)
        {
            if (_videoDetail == null || _videoDetail.interaction == null)
                return;
            if (_player == null || sender != _player.PlaybackSession)
                return;
            await DispatcherHelper.ExecuteOnUIThreadAsync(() =>
            {
                if (_player == null || sender != _player.PlaybackSession)
                    return;
                if (_videoDetail != null && _videoDetail.interaction != null)
                {
                    InteractionEndContainer.Visibility = Visibility.Collapsed;
                    if (sender.Position < sender.NaturalDuration)
                    {
                        if (ChoiceItemsControl.Visibility == Visibility.Visible)
                            ChoiceItemsControl.Visibility = Visibility.Collapsed;
                        if ((sender.NaturalDuration - sender.Position).TotalSeconds > 2)
                            HideNextPart();
                    }
                }
            });
        }

        private async void Media_StatusChanged(MediaPlaybackSession sender, object args)
        {
            if (_player == null || sender != _player.PlaybackSession)
                return;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (_player == null || sender != _player.PlaybackSession || _playData == null)
                    return;
                VideoMTC.IsInit = false;
                if (sender.PlaybackState == MediaPlaybackState.Paused && VideoMTC.IsPlaying)
                    VideoMTC.IsPlaying = false;
                else if (sender.PlaybackState == MediaPlaybackState.Playing && !VideoMTC.IsPlaying)
                {
                    VideoMTC.IsPlaying = true;
                    HideMTC();
                }
                VideoMTC.IsInit = true;
            });
        }

        private async void Media_Opened(MediaPlayer sender, object args)
        {
            if (sender != _player)
                return;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
             {
                 if (sender != _player)
                     return;
                 bool isAutoPlay = AppTool.GetBoolSetting(Settings.IsAutoPlay);
                 HideMTC();
                 if (isAutoPlay)
                 {
                     MTC.IsPlaying = true;
                     sender.Play();
                 }
                 if (_needShowHistory)
                 {
                     if (!isBangumi)
                     {
                         if (_videoDetail != null && _videoDetail.history != null)
                         {
                             var historyPage = _videoDetail.pages.Where(p => p.cid == _videoDetail.history.cid).FirstOrDefault();
                             if (historyPage != null)
                             {
                                 if (_partId != historyPage.cid)
                                 {
                                     ShowHistory($"{historyPage.part}-{AppTool.GetReadDuration(_videoDetail.history.progress)}");
                                 }
                                 else
                                 {
                                     ShowHistory($"{AppTool.GetReadDuration(_videoDetail.history.progress)}");
                                 }
                             }
                         }
                     }
                     else
                     {
                         if (_bangumiDetail != null && _bangumiDetail.user_status.progress != null)
                         {
                             var progress = _bangumiDetail.user_status.progress;
                             var historyEp = _bangumiDetail.episodes.Where(p => p.id == progress.last_ep_id).FirstOrDefault();
                             if (historyEp != null)
                             {
                                 if (_bangumiPart.id != historyEp.id)
                                 {
                                     ShowHistory($"{historyEp.title}-{AppTool.GetReadDuration(progress.last_time)}");
                                 }
                                 else
                                 {
                                     ShowHistory($"{AppTool.GetReadDuration(progress.last_time)}");
                                 }
                             }
                         }
                     }
                 }
                 ErrorContainer.Visibility = Visibility.Collapsed;
             });
        }

        private async void Media_Failed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            if (sender != _player)
                return;
            await DispatcherHelper.ExecuteOnUIThreadAsync(() =>
            {
                if (sender != _player)
                    return;
                string msg = "";
                switch (args.Error)
                {
                    case MediaPlayerError.Unknown:
                        msg = "奇怪的未知错误发生了";
                        break;
                    case MediaPlayerError.Aborted:
                        msg = "操作中止";
                        break;
                    case MediaPlayerError.NetworkError:
                        msg = "无法从网络获取数据";
                        break;
                    case MediaPlayerError.DecodingError:
                        msg = "解码失败或正在切换片源";
                        break;
                    case MediaPlayerError.SourceNotSupported:
                        msg = "不支持该视频源";
                        break;
                    default:
                        break;
                }
                ErrorBlock.Text = msg;
                ErrorContainer.Visibility = Visibility.Visible;
                if (isBangumi)
                    App._logger.Error($"播放动漫:{_bangumiDetail.season_id} & {_bangumiPart.cid}: {msg}");
                else
                    App._logger.Error($"播放视频:{_videoDetail.aid} & {_partId}: {msg}");
            });
        }

        private void Volume_Changed(MediaPlayer sender, object args)
        {
            if (sender != _player)
                return;
            AppTool.WriteLocalSetting(Settings.PlayerLastVolume, sender.Volume.ToString());
        }

        private async void Media_Ended(MediaPlayer sender, object args)
        {
            if (sender != _player)
                return;
            await DispatcherHelper.ExecuteOnUIThreadAsync(async () =>
            {
                if (sender != _player)
                    return;
                SendDanmakuList.Clear();
                if (!sender.IsLoopingEnabled)
                {
                    VideoMTC.IsPlaying = false;
                    if (!isBangumi && _videoDetail.interaction != null)
                    {
                        //互动视频
                        if (ChoiceCollection.Count > 0)
                        {
                            if (ChoiceCollection.Count == 1 && string.IsNullOrEmpty(ChoiceCollection.First().option))
                                await InitInteraction(ChoiceCollection.First().cid, ChoiceCollection.First().id);
                            else
                                ChoiceItemsControl.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            InteractionEndContainer.Visibility = Visibility.Visible;
                            MediaEnded?.Invoke(this, _videoId);
                        }
                    }
                    else
                    {
                        if (IsAutoReturnWhenEnd)
                        {
                            if (VideoMTC.IsFullWindow)
                                VideoMTC.IsFullWindow = false;
                            else if (VideoMTC.IsCinema)
                                VideoMTC.IsCinema = false;
                            else if (VideoMTC.IsCompactOverlay)
                                VideoMTC.IsCompactOverlay = false;
                        }
                        long id = 0;
                        if (isBangumi)
                            id = _bangumiPart.id;
                        else
                            id = _videoId;
                        bool result = await ShowNextPart();
                        if (!result)
                            MediaEnded?.Invoke(this, id);
                    }
                }
            });
        }
        #endregion

        #region 播放及刷新
        public async Task RefreshVideoSource(long partId, int progress = 0, bool needRefresh = false, bool forceRefresh = false)
        {
            LoadingBar.Visibility = Visibility.Visible;
            if (needRefresh)
                Reset();
            int generation = ++_sourceGeneration;
            if (_playData == null || _partId != partId || forceRefresh)
            {
                _partId = partId;
                _playData = null;
                _danmakuGeneration++;
                _danmakuCursor = 0;
                _danmakuEmitPosition = -1;
                DanmakuList = new List<DanmakuModel>();
                if (DanmakuControls != null)
                    DanmakuControls.ClearAll();
                _subtitleGeneration++;
                _subtitleTimer.Stop();
                Subtitles.Clear();
                SubtitleIndexCollection.Clear();
                HideSubtitle();
                if (_currentQn == 0)
                    _currentQn = GetDefaultQuality();
                var data = await _videoService.GetVideoPlayAsync(_videoId, partId, _currentQn);
                if (generation != _sourceGeneration)
                    return;
                if (data != null)
                {
                    _playData = data;
                    ApplyQualitySelection();
                }
            }
            if (generation != _sourceGeneration || _player == null)
                return;
            if (_playData != null)
            {
                MediaSource mediaSource = null;
                if (_playData is VideoPlayDash)
                    mediaSource = await HandleDashSource();
                else
                    mediaSource = await HandleFlvSource(_videoId);
                if (generation != _sourceGeneration || _player == null)
                {
                    mediaSource?.Dispose();
                    return;
                }
                if (mediaSource != null)
                {
                    TimeSpan offset = TimeSpan.FromSeconds(progress);
                    if (progress == 0)
                        offset = TimeSpan.FromSeconds(_player.PlaybackSession.Position.TotalSeconds);
                    var other = _tempSource;
                    _tempSource = mediaSource;
                    _player.Source = new MediaPlaybackItem(mediaSource);
                    _player.PlaybackSession.PlaybackRate = _playRate;
                    if (offset.TotalSeconds > 0)
                        _player.PlaybackSession.Position = offset;
                    other?.Dispose();
                    VideoMTC.IsInit = false;
                    VideoMTC.IsPlaying = _player.AutoPlay;
                    VideoMTC.IsInit = true;
                    if (_player.AutoPlay)
                        Resume();
                    mediaElement.PosterSource = null;
                    _ = LoadDanmakuAsync();
                    _ = LoadSubtitleAsync();
                }
                else
                    ErrorContainer.Visibility = Visibility.Visible;
                _player.IsLoopingEnabled = AutoLoop;
            }
            else
            {
                App._logger.Error($"播放源（视频）加载失败: {_videoDetail.aid} & {partId}");
                ErrorContainer.Visibility = Visibility.Visible;
            }
            SetFocus();
            LoadingBar.Visibility = Visibility.Collapsed;
        }
        public async Task RefreshVideoSource(Episode part, bool isRefresh = false, int progress = 0, bool forceRefresh = false)
        {
            if (part == null)
            {
                ErrorContainer.Visibility = Visibility.Visible;
                return;
            }
            if (isRefresh)
                Reset();
            LoadingBar.Visibility = Visibility.Visible;
            int generation = ++_sourceGeneration;
            if (_playData == null || _bangumiPart.id != part.id || forceRefresh)
            {
                _bangumiPart = part;
                _playData = null;
                _danmakuGeneration++;
                _danmakuCursor = 0;
                _danmakuEmitPosition = -1;
                DanmakuList = new List<DanmakuModel>();
                if (DanmakuControls != null)
                    DanmakuControls.ClearAll();
                if (_currentQn == 0)
                    _currentQn = GetDefaultQuality();
                var data = await _animeService.GetBangumiPlayAsync(_bangumiDetail.type, part.cid, _currentQn);
                if (generation != _sourceGeneration)
                    return;
                if (data != null)
                {
                    _playData = data;
                    ApplyQualitySelection();
                }
            }
            if (generation != _sourceGeneration || _player == null)
                return;
            if (_playData != null)
            {
                MediaSource mediaSource = null;
                if (_playData is VideoPlayDash)
                    mediaSource = await HandleDashSource();
                else
                    mediaSource = await HandleFlvSource(_videoId, true);
                if (generation != _sourceGeneration || _player == null)
                {
                    mediaSource?.Dispose();
                    return;
                }
                if (mediaSource == null)
                {
                    ErrorContainer.Visibility = Visibility.Visible;
                    LoadingBar.Visibility = Visibility.Collapsed;
                    return;
                }
                var other = _tempSource;
                _tempSource = mediaSource;
                TimeSpan offset = TimeSpan.FromSeconds(progress);
                if (progress == 0)
                    offset = TimeSpan.FromSeconds(_player.PlaybackSession.Position.TotalSeconds);
                _player.Source = new MediaPlaybackItem(mediaSource);
                if (offset.TotalSeconds > 0)
                    _player.PlaybackSession.Position = offset;
                other?.Dispose();
                _player.PlaybackSession.PlaybackRate = _playRate;
                VideoMTC.IsInit = false;
                VideoMTC.IsPlaying = _player.AutoPlay;
                VideoMTC.IsInit = true;
                if (_player.AutoPlay)
                    Resume();
                mediaElement.PosterSource = null;
                _ = LoadDanmakuAsync();
            }
            else
            {
                App._logger.Error($"播放源（动漫）加载失败: {_bangumiDetail.season_id} & {part.cid}");
                ErrorContainer.Visibility = Visibility.Visible;
            }
            UpdateMediaProperties(part.title, part.subtitle, _bangumiDetail.cover);
            SetFocus();
            LoadingBar.Visibility = Visibility.Collapsed;
        }
        private int GetDefaultQuality()
        {
            int savedQn = Convert.ToInt32(AppTool.GetLocalSetting(Settings.FirstQuality, "0"));
            if (savedQn > 0)
                return savedQn;
            return AppTool.GetBoolSetting(Settings.IsFirst4K, false) ? 120 : 80;
        }
        private int FindQualityIndex(int qn)
        {
            for (int i = 0; i < QualityCollection.Count; i++)
            {
                if (QualityCollection[i].Item1 == qn)
                    return i;
            }
            return -1;
        }
        private void ApplyQualitySelection()
        {
            if (_playData == null)
                return;
            int requestedQn = _currentQn;
            QualityCollection.Clear();
            int count = Math.Min(_playData.accept_quality?.Count ?? 0, _playData.accept_description?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                QualityCollection.Add(new Tuple<int, string>(_playData.accept_quality[i], _playData.accept_description[i]));
            }
            VideoMTC._qualityComboBox.SelectedIndex = -1;
            int index = FindQualityIndex(requestedQn);
            if (index == -1 && _playData.quality > 0)
                index = FindQualityIndex(_playData.quality);
            if (index == -1)
            {
                bool isFirst4K = AppTool.GetBoolSetting(Settings.IsFirst4K, false);
                if (isFirst4K && QualityCollection.Count > 0 && QualityCollection[0].Item1 == 120)
                    index = 0;
                else
                {
                    for (int i = 0; i < QualityCollection.Count; i++)
                    {
                        if (QualityCollection[i].Item1 < 120)
                        {
                            index = i;
                            break;
                        }
                    }
                }
                if (index == -1 && QualityCollection.Count > 0)
                    index = 0;
            }
            if (index >= 0)
            {
                _currentQn = QualityCollection[index].Item1;
                VideoMTC._qualityComboBox.SelectedIndex = index;
                App._logger.Info($"清晰度：请求 {requestedQn}，返回 {_playData.quality}，选中 {_currentQn}（{QualityCollection[index].Item2}），可选 {string.Join("/", QualityCollection.Select(p => p.Item1))}");
            }
            else
                App._logger.Warn($"清晰度列表为空：请求 {requestedQn}，返回 {_playData.quality}");
        }
        private async Task InitInteraction(long cid, int edgeId)
        {
            if (_isChoiceHandling)
                return;
            _isChoiceHandling = true;
            Reset();
            InteractionHomeButton.Visibility = Visibility.Visible;
            var data = await _videoService.GetInteractionVideoAsync(_videoId, _videoDetail.interaction.graph_version, edgeId);
            ChoiceItemsControl.Visibility = Visibility.Collapsed;
            await RefreshVideoSource(cid);
            if (data != null)
            {
                ChoiceCollection.Clear();
                _interaction = data;
                try
                {
                    if (_interaction.edges.questions != null)
                    {
                        var choices = _interaction.edges.questions.First().choices;
                        foreach (var choice in choices)
                        {
                            if (!string.IsNullOrEmpty(choice.condition))
                            {
                                var variables = _interaction.hidden_vars.Where(p => choice.condition.Contains(p.id_v2));
                                if (variables != null)
                                {
                                    string minString = Regex.Match(choice.condition, ">=([0-9]{1,}[.][0-9]*)").Value.Replace(">=", "");
                                    string maxString = Regex.Match(choice.condition, "<=([0-9]{1,}[.][0-9]*)").Value.Replace("<=", "");
                                    double min = string.IsNullOrEmpty(minString) ? 0 : Convert.ToDouble(minString);
                                    double max = string.IsNullOrEmpty(maxString) ? -1 : Convert.ToDouble(maxString);
                                    var variable = variables.Where(p => choice.condition.Contains(p.id_v2)).FirstOrDefault();
                                    if (variable != null)
                                    {
                                        if (variable.value >= min && (max == -1 || variable.value <= max))
                                        {
                                            ChoiceCollection.Add(choice);
                                        }
                                    }
                                }
                            }
                            else
                                ChoiceCollection.Add(choice);
                        }
                    }
                }
                catch (Exception)
                {

                }
            }
            _isChoiceHandling = false;
        }
        /// <summary>
        /// 更新SMTC的显示
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="subtitle">副标题</param>
        /// <param name="cover">封面</param>
        private void UpdateMediaProperties(string title, string subtitle, string cover)
        {
            SystemMediaTransportControlsDisplayUpdater updater = _player.SystemMediaTransportControls.DisplayUpdater;
            updater.Type = MediaPlaybackType.Video;
            updater.VideoProperties.Title = title;
            updater.VideoProperties.Subtitle = subtitle;
            updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(cover));
            updater.Update();
        }
        #endregion

        #region 弹幕
        private async Task LoadDanmakuAsync()
        {
            bool isShow = AppTool.GetBoolSetting(Settings.IsDanmakuOpen);
            if (DanmakuControls == null)
                return;
            if (!isShow)
            {
                DanmakuControls.Visibility = Visibility.Collapsed;
                DanmakuList = new List<DanmakuModel>();
                _danmakuCursor = 0;
                _danmakuEmitPosition = -1;
                return;
            }
            if (isBangumi ? _bangumiPart == null : _partId == 0)
                return;
            long cid = isBangumi ? Convert.ToInt64(_bangumiPart.cid) : Convert.ToInt64(_partId);
            DanmakuControls.Visibility = Visibility.Visible;
            if (_danmakuCache.TryGetValue(cid, out var cached))
            {
                ApplyDanmaku(cached);
                return;
            }
            int generation = ++_danmakuGeneration;
            try
            {
                var list = await _danmakuParse.ParseBiliBili(cid);
                if (generation != _danmakuGeneration || !IsCurrentDanmakuPart(cid))
                    return;
                if (list == null)
                    list = new List<DanmakuModel>();
                else if (list.Count > 1)
                    await Task.Run(() => list.Sort((a, b) => a.time.CompareTo(b.time)));
                if (generation != _danmakuGeneration || !IsCurrentDanmakuPart(cid))
                    return;
                if (_danmakuCache.Count >= 3)
                    _danmakuCache.Remove(_danmakuCache.Keys.First());
                _danmakuCache[cid] = list;
                ApplyDanmaku(list);
            }
            catch (Exception ex)
            {
                App._logger.Error($"弹幕加载失败: {cid}", ex);
            }
        }

        private bool IsCurrentDanmakuPart(long cid)
        {
            if (isBangumi)
                return _bangumiPart != null && cid == Convert.ToInt64(_bangumiPart.cid);
            return cid == Convert.ToInt64(_partId);
        }

        private void ApplyDanmaku(List<DanmakuModel> list)
        {
            DanmakuList = list;
            double position = _player == null ? 0 : _player.PlaybackSession.Position.TotalSeconds;
            RebindDanmakuCursor(position > 0 ? position : 0);
            DanmakuControls.ClearAll();
        }

        private void RebindDanmakuCursor(double position)
        {
            int low = 0, high = DanmakuList.Count;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (DanmakuList[mid].time <= position)
                    low = mid + 1;
                else
                    high = mid;
            }
            _danmakuCursor = low;
            _danmakuEmitPosition = position;
        }

        /// <summary>
        /// 弹幕是否该被屏蔽（不显示）
        /// </summary>
        /// <param name="text">弹幕文本</param>
        /// <param name="location">弹幕位置</param>
        /// <returns></returns>
        private bool IsTextShouldShield(string text, DanmakuLocation location = DanmakuLocation.Other)
        {
            if (ShieldTextList.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (_isMergeSameDanmaku && SendDanmakuList.Contains(text + location))
                return true;
            return false;
        }
        private async void DanmakuVisibilityButton_Click(object sender, RoutedEventArgs e)
        {
            await ChangeDanmakuStatus();
        }

        private async void SendDanmakuButton_Click(object sender, RoutedEventArgs e)
        {
            await SendDanmaku();
        }

        private async void DanmakuBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                await SendDanmaku();
            }
        }
        private void DanmakuBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _isDanmakuBarFocus = true;
        }

        private void DanmakuBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _isDanmakuBarFocus = false;
        }
        private async Task SendDanmaku()
        {
            if (!App.BiliViewModel.CheckAccoutStatus())
                return;
            if (_playData == null || _player == null || _player.PlaybackSession.PlaybackState == MediaPlaybackState.Opening)
            {
                new TipPopup("请等待视频加载完成").ShowMessage();
                return;
            }
            string text = DanmakuBox.Text;
            if (!string.IsNullOrEmpty(text))
            {
                SendDanmakuButton.IsEnabled = false;
                DanmakuBox.IsEnabled = false;
                string color = "";
                if (!string.IsNullOrEmpty(ColorTextBox.Text))
                    color = UIHelper.GetDanmakuColor(ColorHelper.ToColor(ColorTextBox.Text));
                else
                    color = UIHelper.GetDanmakuColor(Windows.UI.Colors.White);
                string fontSize = Convert.ToBoolean(DefaultFontSizeRadio.IsChecked) ? "25" : "20";
                string mode = (ModeComboBox.SelectedItem as ComboBoxItem).Tag.ToString();
                bool result = false;
                double progress = _player.PlaybackSession.Position.TotalMilliseconds;
                if (isBangumi)
                    result = await _videoService.SendDanmakuAsync(text, _bangumiPart.aid, _bangumiPart.cid, progress, color, fontSize, mode);
                else
                    result = await _videoService.SendDanmakuAsync(text, _videoId, _partId, progress, color, fontSize, mode);
                if (result)
                {
                    DanmakuBox.Text = string.Empty;
                    int fontSizeNum = Convert.ToInt32(fontSize);
                    if (mode == "1")
                        DanmakuControls.AddRollDanmu(new DanmakuModel { color = ColorHelper.ToColor(ColorTextBox.Text), size = fontSizeNum, text = text }, true);
                    else if (mode == "4")
                        DanmakuControls.AddBottomDanmu(new DanmakuModel { color = ColorHelper.ToColor(ColorTextBox.Text), size = fontSizeNum, text = text }, true);
                    else if (mode == "5")
                        DanmakuControls.AddTopDanmu(new DanmakuModel { color = ColorHelper.ToColor(ColorTextBox.Text), size = fontSizeNum, text = text }, true);
                    else
                        DanmakuControls.AddRollDanmu(new DanmakuModel { color = ColorHelper.ToColor(ColorTextBox.Text), size = fontSizeNum, text = text }, true);
                }
                else
                {
                    new TipPopup("发送失败").ShowError();
                }
                // send
                SendDanmakuButton.IsEnabled = true;
                DanmakuBox.IsEnabled = true;
            }
        }
        #endregion

        #region 弹幕设置
        private void DanmakuColor_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var data = (sender as FrameworkElement).DataContext as DanmakuColor;
            ColorTextBox.Text = data.Color.Color.ToString();
            ColorViewBorder.Background = data.Color;
        }

        private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                double v = OpacitySlider.Value;
                DanmakuControls.Opacity = v;
                AppTool.WriteLocalSetting(Settings.DanmakuOpacity, v.ToString());
            }
        }

        private void FontSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                double v = FontSizeSlider.Value;
                DanmakuControls.sizeZoom = v;
                SubtitleContentBlock.FontSize = v * 25;
                AppTool.WriteLocalSetting(Settings.DanmakuFontSize, v.ToString());
            }
        }

        private void SpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                double v = SpeedSlider.Value;
                DanmakuControls.speed = 25 - Convert.ToInt32(v * 12);
                AppTool.WriteLocalSetting(Settings.DanmakuSpeed, v.ToString());
            }
        }

        private void MaxinumSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                double v = MaxinumSlider.Value;
                _maxDanmakuNumber = Convert.ToInt32(v);
                AppTool.WriteLocalSetting(Settings.DanmakuMaxNumber, v.ToString("0"));
            }
        }
        private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                var item = FontComboBox.SelectedItem as SystemFont;
                string oldFont = AppTool.GetLocalSetting(Settings.DanmakuFontFamily, "微软雅黑");
                if (item.Name != oldFont)
                {
                    AppTool.WriteLocalSetting(Settings.DanmakuFontFamily, item.Name);
                    DanmakuControls.font = item.Name;
                    try
                    {
                        SubtitleContentBlock.FontFamily = item.FontFamily;
                    }
                    catch (Exception)
                    {
                        SubtitleContentBlock.FontFamily = new FontFamily(item.Name);
                    }
                }
            }
        }

        private void MergeDanmakuSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                bool isMerge = MergeDanmakuSwitch.IsOn;
                _isMergeSameDanmaku = isMerge;
                AppTool.WriteLocalSetting(Settings.DanmakuMerge, isMerge.ToString());
            }
        }

        private void ProtectSubtitleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                bool isProtect = ProtectSubtitleSwitch.IsOn;
                DanmakuControls.notHideSubtitle = isProtect;
                AppTool.WriteLocalSetting(Settings.DanmakuProtectSubtitle, isProtect.ToString());
            }
        }

        private void BorderStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isDanmakuOptionsInit)
            {
                int index = BorderStyleComboBox.SelectedIndex;
                if (index == -1)
                    return;
                DanmakuControls.borderStyle = (DanmakuBorderStyle)index;
                AppTool.WriteLocalSetting(Settings.DanmakuBorderStyle, index.ToString());
            }
        }
        #endregion

        #region 字幕
        private async Task LoadSubtitleAsync()
        {
            int generation = ++_subtitleGeneration;
            try
            {
                SubtitleIndexCollection.Clear();
                var index = await _videoService.GetVideoSubtitleIndexAsync(_videoId, _partId);
                if (generation != _subtitleGeneration)
                    return;
                if (index != null)
                {
                    index.ForEach(p => SubtitleIndexCollection.Add(p));
                }
                if (SubtitleIndexCollection.Count > 0)
                {
                    SubtitleIndexCollection.Insert(0, SubtitleIndexItem.UnSelected);
                    VideoMTC._subtitleListView.SelectedIndex = -1;
                    await SubtitleInitCore(SubtitleIndexCollection[1], generation);
                    if (generation != _subtitleGeneration)
                        return;
                    VideoMTC._subtitleListView.SelectedIndex = 1;
                    VideoMTC.SubtitleHolderVisibility = Visibility.Collapsed;
                }
                else
                    VideoMTC.SubtitleHolderVisibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App._logger.Error($"字幕索引加载失败: {_videoId} & {_partId}", ex);
            }
        }

        private async Task SubtitleInit(SubtitleIndexItem item)
        {
            await SubtitleInitCore(item, ++_subtitleGeneration);
        }

        private async Task SubtitleInitCore(SubtitleIndexItem item, int generation)
        {
            _subtitleTimer.Stop();
            Subtitles.Clear();
            HideSubtitle();
            if (item == null || string.IsNullOrEmpty(item.subtitle_url))
                return;
            try
            {
                var response = await _videoService.GetSubtitlesAsync(item.subtitle_url);
                if (generation != _subtitleGeneration)
                    return;
                var subtitles = response?.body;
                if (subtitles != null)
                {
                    Subtitles.Clear();
                    subtitles.ForEach(p => Subtitles.Add(p));
                    _subtitleTimer.Start();
                }
                else
                {
                    new TipPopup("字幕加载异常").ShowError();
                }
            }
            catch (Exception ex)
            {
                App._logger.Error("字幕内容加载失败", ex);
            }
        }


        private void ShowSubtitle(string content)
        {
            SubtitleContainer.Visibility = Visibility.Visible;
            SubtitleContentBlock.Text = content;
        }

        private void HideSubtitle()
        {
            SubtitleContainer.Visibility = Visibility.Collapsed;
            SubtitleContentBlock.Text = string.Empty;
        }
        #endregion

        #region 播放器状态控制
        public void Pause()
        {
            VideoMTC.IsPlaying = false;
        }

        public void Resume(bool isChangePlaying = true)
        {
            if (isChangePlaying)
                VideoMTC.IsPlaying = true;
            _defaultTimer.Start();
            if (dispRequest == null)
            {
                // 用户观看视频，需要保持屏幕的点亮状态
                dispRequest = new DisplayRequest();
            }
            dispRequest.RequestActive();
        }

        public void Close()
        {
            Pause();
            _sourceGeneration++;
            _danmakuGeneration++;
            _subtitleGeneration++;
            if (dispRequest != null)
            {
                dispRequest.RequestRelease();
            }
            dispRequest = null;
            _defaultTimer.Stop();
            _subtitleTimer.Stop();
            DanmakuList = new List<DanmakuModel>();
            _danmakuCache.Clear();
            _danmakuCursor = 0;
            _danmakuEmitPosition = -1;
            if (DanmakuControls != null)
                DanmakuControls.ClearAll();
            mediaElement.SetMediaPlayer(null);
            ReleasePlayer();
            if (_tempSource != null)
            {
                _tempSource.Dispose();
                _tempSource = null;
            }
            LoadingBar.Visibility = Visibility.Collapsed;
        }

        private void ReleasePlayer()
        {
            var player = _player;
            _player = null;
            if (player == null)
                return;
            player.MediaEnded -= Media_Ended;
            player.MediaFailed -= Media_Failed;
            player.MediaOpened -= Media_Opened;
            player.VolumeChanged -= Volume_Changed;
            var session = player.PlaybackSession;
            if (session != null)
            {
                session.PositionChanged -= Media_PositionChanged;
                session.PlaybackStateChanged -= Media_StatusChanged;
            }
            try
            {
                player.Pause();
                player.Source = null;
                player.Dispose();
            }
            catch (Exception)
            {
            }
        }
        public void SkipForward()
        {
            if (_player != null && _player.PlaybackSession.CanSeek)
            {
                var position = _player.PlaybackSession.Position.TotalSeconds;
                var total = _player.PlaybackSession.NaturalDuration.TotalSeconds;
                double target = 0d;
                if (position + _skipStep > total)
                    target = total;
                else
                    target = position + _skipStep;
                _player.PlaybackSession.Position = TimeSpan.FromSeconds(target);
            }
        }
        public void SkipRewind()
        {
            if (_player != null && _player.PlaybackSession.CanSeek)
            {
                var position = _player.PlaybackSession.Position.TotalSeconds;
                double target = 0d;
                if (position - _skipStep < 0)
                    target = 0;
                else
                    target = position - _skipStep;
                _player.PlaybackSession.Position = TimeSpan.FromSeconds(target);
            }
        }
        public void UpVolume()
        {
            if (_player != null && _player.Volume * 100.0 < 100)
            {
                double volume = _player.Volume * 100.0 + 5;
                if (volume > 100)
                    volume = 100;
                _player.Volume = volume / 100.0;
                ShowTip($"当前音量: {Math.Round(volume)}");
            }
        }
        public void DownVolume()
        {
            if (_player != null && _player.Volume > 0)
            {
                double volume = _player.Volume * 100.0 - 5;
                if (volume < 0)
                    volume = 0;
                _player.Volume = volume / 100.0;
                ShowTip($"当前音量: {Math.Round(volume)}");
            }
        }
        private void MediaPresenter_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            _manipulationVolume = 0;
            _manipulationProgress = 0;
            _manipulationDeltaX = 0;
            _manipulationDeltaY = 0;
            _manipulationStartPoint = new Point(0, 0);
            _manipulationType = PlayerManipulationType.None;
            if (_manipulationBeforeIsPlay)
                Resume();
            _manipulationBeforeIsPlay = false;
        }

        private void MediaPresenter_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (_playData == null || _player == null)
                return;
            _manipulationDeltaX += e.Delta.Translation.X;
            _manipulationDeltaY -= e.Delta.Translation.Y;
            if (Math.Abs(_manipulationDeltaX) > 15 || Math.Abs(_manipulationDeltaY) > 15)
            {
                if (_manipulationType == PlayerManipulationType.None)
                {
                    bool isVolume = Math.Abs(_manipulationDeltaY) > Math.Abs(_manipulationDeltaX);
                    _manipulationType = isVolume ? PlayerManipulationType.Volume : PlayerManipulationType.Progress;
                    if (!isVolume)
                        Pause();
                }
                if (_manipulationType == PlayerManipulationType.Volume)
                {
                    var volume = _manipulationVolume + _manipulationDeltaY / 2.0;
                    if (volume > 100)
                        volume = 100;
                    else if (volume < 0)
                        volume = 0;
                    ShowTip($"当前音量: {Math.Round(volume)}");
                    _player.Volume = volume / 100.0;
                    if (volume == 0)
                        ShowTip("静音了！");
                }
                else
                {
                    var progress = _manipulationProgress + (_manipulationDeltaX * _manipulationUnitLength);
                    if (progress > _player.PlaybackSession.NaturalDuration.TotalSeconds)
                        progress = _player.PlaybackSession.NaturalDuration.TotalSeconds;
                    else if (progress < 0)
                        progress = 0;
                    ShowTip($"当前进度: {AppTool.GetReadDuration(Convert.ToInt32(progress))}");
                    _player.PlaybackSession.Position = TimeSpan.FromSeconds(progress);
                }
            }
        }

        private void MediaPresenter_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            if (_player == null)
                return;
            _manipulationStartPoint = e.Position;
            _manipulationProgress = CurrentProgress;
            _manipulationVolume = _player.Volume * 100.0;
            _manipulationBeforeIsPlay = VideoMTC.IsPlaying;
            if (_player.PlaybackSession != null && _player.PlaybackSession.NaturalDuration.TotalSeconds > 0)
            {
                //获取单位像素对应的时长
                double unit = _player.PlaybackSession.NaturalDuration.TotalSeconds / mediaElement.ActualWidth;
                _manipulationUnitLength = unit / 1.5;
            }
        }
        #endregion

        #region 播放源处理
        private async Task<MediaSource> HandleDashSource()
        {
            bool isHevc = AppTool.GetBoolSetting(Settings.IsUseHevc, false);
            var data = _playData as VideoPlayDash;
            int codecId = isHevc ? 12 : 7;
            var video = data.dash.video.FirstOrDefault(p => p.id == _currentQn && p.codecid == codecId);
            if (video == null && codecId == 12)
                video = data.dash.video.FirstOrDefault(p => p.id == _currentQn);
            if (video == null)
                video = data.dash.video.OrderByDescending(p => p.id).FirstOrDefault(p => p.codecid == 7);
            App._logger.Info($"DASH：qn={_currentQn}，选中 {video?.id}/{video?.codecid} {video?.width}x{video?.height}，码率 {video?.bandwidth}，可用 {string.Join(",", data.dash.video.Select(p => p.id + "/" + p.codecid + "/" + p.width + "x" + p.height))}");
            await BiliTool.WriteNetworkLogAsync($"DASH 选流：qn={_currentQn}，实际 {video?.id}/{video?.codecid} {video?.width}x{video?.height}，码率 {video?.bandwidth}");
            if (video == null)
            {
                App._logger.Warn($"DASH 无可用视频流：qn={_currentQn}，codec={codecId}");
                return null;
            }
            var audio = data.dash.audio?.FirstOrDefault();
            // 遇到新的电影时，可能出现HEVC音频源，若系统未安装相应解码器，则会解码失败，没有声音
            if (!isHevc && data.dash.audio != null && data.dash.audio.Any(p => p.codecs.Contains("mp4a")))
                audio = data.dash.audio.Where(p => p.codecs.Contains("mp4a")).FirstOrDefault();
            MediaSource source = null;
            if (isBangumi)
                source = await _animeService.CreateMediaSourceAsync(video, audio);
            else
                source = await _videoService.CreateMediaSourceAsync(video, audio);
            return source;
        }
        private async Task<MediaSource> HandleFlvSource(long videoId, bool isBangumi = false)
        {
            var playList = new Playlist(PlaylistTypes.NetworkHttp);
            var data = _playData as VideoPlayFlv;
            App._logger.Info($"FLV：qn={_currentQn}，返回 quality={data.quality}，分片 {data.durl?.Count ?? 0}");
            await BiliTool.WriteNetworkLogAsync($"FLV 选流：qn={_currentQn}，返回 quality={data.quality}，分片 {data.durl?.Count ?? 0}");
            List<string> urls = new List<string>();
            data.durl.ForEach(p =>
            {
                urls.Add(p.url);
                playList.Append(p.url, 0, p.length / 1000);
            });
            string prefix = isBangumi ? "https://www.bilibili.com/bangumi/play/ep" : "https://www.bilibili.com/video/av";
            playList.NetworkConfigs = CreatePlaylistNetworkConfigs("https://www.bilibili.com/video/av" + videoId + "/");
            var mediaSouce = MediaSource.CreateFromUri(await playList.SaveAndGetFileUriAsync());
            return mediaSouce;
        }

        private PlaylistNetworkConfigs CreatePlaylistNetworkConfigs(string referer)
        {
            PlaylistNetworkConfigs config = new PlaylistNetworkConfigs();
            config.DownloadRetryOnFail = true;
            config.HttpCookie = string.Empty;
            config.UniqueId = string.Empty;
            config.HttpUserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/69.0.3497.100 Safari/537.36";
            config.HttpReferer = referer;
            return config;
        }
        #endregion

        #region 控件事件处理
        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DanmakuControls != null)
                DanmakuControls.Clip = new RectangleGeometry() { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        }

        private void UserControl_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isCatchPointer = true;
            _lastPointerStayPoint = e.GetCurrentPoint(this).Position;
        }
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            SetFocus();
            HideMTC();
        }
        private void UserControl_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isCatchPointer = false;
            _lastPointerStayPoint = new Point(0, 0);
            Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 0);
        }
        private void MediaPlayerElement_GotFocus(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("已获取焦点");
            IsFocus = true;
        }

        private void MediaPlayerElement_LostFocus(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("失去焦点");
            IsFocus = false;
        }
        private void UserControl_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            _pointerHoldCount = 0;
            var pos = e.GetCurrentPoint(this).Position;
            bool isManual = AppTool.GetBoolSetting(Settings.IsManualMediaTransportControls, false);

            if (!_isMTCShow && !isManual && pos != _lastPointerStayPoint)
            {
                ShowMTC();
            }
            if (pos != _lastPointerStayPoint)
            {
                _lastPointerStayPoint = pos;
                if (Window.Current.CoreWindow.PointerCursor == null)
                    Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 0);
            }
        }
        private void mediaElement_Tapped(object sender, TappedRoutedEventArgs e)
        {
            bool isManual = AppTool.GetBoolSetting(Settings.IsManualMediaTransportControls, false);
            if (isManual)
            {
                _isMTCShow = !_isMTCShow;
                if (_isMTCShow)
                    ShowMTC();
                else
                    HideMTC();
            }
            SetFocus();
        }

        private void mediaElement_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            e.Handled = true;
            VideoMTC.IsPlaying = !VideoMTC.IsPlaying;
        }
        private async void Choice_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var data = (sender as FrameworkElement).DataContext as Choice;
            await InitInteraction(data.cid, data.id);
        }
        private void ChoiceItemsControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ((sender as ItemsControl).ItemsPanelRoot as ItemsWrapGrid).ItemWidth = (e.NewSize.Width * 0.8) / 2;
        }
        private async void InteractionHomeButton_Click(object sender, RoutedEventArgs e)
        {
            ChoiceCollection.Clear();
            ChoiceItemsControl.Visibility = Visibility.Collapsed;
            await InitInteraction(_videoDetail.pages.First().cid, 0);
        }
        private void ExitScreenButton_Click(object sender, RoutedEventArgs e)
        {
            ExitCurrentStatus();
        }
        private async void ContinueHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isBangumi)
            {
                var history = _videoDetail.history;
                if (history != null)
                {
                    if (_player != null && _partId == history.cid)
                        _player.PlaybackSession.Position = TimeSpan.FromSeconds(history.progress);
                    else
                    {
                        await RefreshVideoSource(history.cid, history.progress, true);
                        var index = _videoDetail.pages.IndexOf(_videoDetail.pages.Where(p => p.cid == history.cid).FirstOrDefault());
                        if (index != -1)
                            PartSwitched?.Invoke(this, index);
                    }
                }
            }
            else
            {
                var progress = _bangumiDetail.user_status.progress;
                if (progress != null)
                {
                    if (_player != null && _bangumiPart.id == progress.last_ep_id)
                        _player.PlaybackSession.Position = TimeSpan.FromSeconds(progress.last_time);
                    else
                    {
                        var part = _bangumiDetail.episodes.Where(p => p.id == progress.last_ep_id).FirstOrDefault();
                        if (part != null)
                        {
                            await RefreshVideoSource(part, true, progress.last_time);
                            var index = _bangumiDetail.episodes.IndexOf(part);
                            PartSwitched?.Invoke(this, index);
                        }
                    }
                }
            }
            HideHistory();
        }

        private void HideHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            HideHistory();
        }
        private void HistoryContainer_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            _historyShowSeconds = 0;
        }
        private void HideNextPartButton_Click(object sender, RoutedEventArgs e)
        {
            HideNextPart();
        }

        private async void NextPartButton_Click(object sender, RoutedEventArgs e)
        {
            int index = GetNextPartIndex();
            await SwitchNextPart(index);
            HideNextPart();
        }
        private void LockScreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlayerLocked)
                UnlockPlayer();
            else
                LockPlayer();
        }
        private void MaskContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            LockScreenButton.Visibility = Visibility.Visible;
            ScreenShotButton.Visibility = Visibility.Visible;
        }
        private async void ScreenShotButton_Click(object sender, RoutedEventArgs e)
        {
            await ScreenShot();
        }
        #endregion

        #region MTC事件
        private void VideoMTC_DanmakuLoaded(object sender, NSDanmaku.Controls.Danmaku e)
        {
            DanmakuControls = e;
            VideoMTC.MediaPlayerElement = mediaElement;

            _isDanmakuOptionsInit = false;
            _maxDanmakuNumber = Convert.ToInt32(AppTool.GetLocalSetting(Settings.DanmakuMaxNumber, "200"));
            MaxinumSlider.Value = _maxDanmakuNumber;
            double danmakuOpacity = Convert.ToDouble(AppTool.GetLocalSetting(Settings.DanmakuOpacity, "1.0"));
            OpacitySlider.Value = danmakuOpacity;
            DanmakuControls.Opacity = danmakuOpacity;
            double danmakuSize = Convert.ToDouble(AppTool.GetLocalSetting(Settings.DanmakuFontSize, "1.0"));
            FontSizeSlider.Value = danmakuSize;
            DanmakuControls.sizeZoom = danmakuSize;
            SubtitleContentBlock.FontSize = danmakuSize * 25;
            double danmakuSpeed = Convert.ToDouble(AppTool.GetLocalSetting(Settings.DanmakuSpeed, "1.0"));
            SpeedSlider.Value = danmakuSpeed;
            DanmakuControls.speed = 25 - Convert.ToInt32(12 * danmakuSpeed);
            FontInit();
            bool isProtect = AppTool.GetBoolSetting(Settings.DanmakuProtectSubtitle, false);
            ProtectSubtitleSwitch.IsOn = isProtect;
            DanmakuControls.notHideSubtitle = isProtect;
            bool isMerge = AppTool.GetBoolSetting(Settings.DanmakuMerge, false);
            _isMergeSameDanmaku = isMerge;
            MergeDanmakuSwitch.IsOn = isMerge;
            int borderStyle = Convert.ToInt32(AppTool.GetLocalSetting(Settings.DanmakuBorderStyle, "0"));
            DanmakuControls.borderStyle = (DanmakuBorderStyle)borderStyle;
            BorderStyleComboBox.SelectedIndex = borderStyle;

            DanmakuControls.InitializeDanmaku(DanmakuMode.Video, danmakuSize, 25 - Convert.ToInt32(12 * danmakuSpeed), (DanmakuBorderStyle)borderStyle);

            _isDanmakuOptionsInit = true;
        }
        private void VideoMTC_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isMediaElementLoaded)
            {
                mediaElement.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
                mediaElement.ManipulationStarted += MediaPresenter_ManipulationStarted;
                mediaElement.ManipulationDelta += MediaPresenter_ManipulationDelta;
                mediaElement.ManipulationCompleted += MediaPresenter_ManipulationCompleted;
                _isMediaElementLoaded = true;
            }
            MTCLoaded?.Invoke(this, EventArgs.Empty);
        }
        private void VideoMTC_PlayButtonClick(object sender, bool e)
        {
            if (e)
            {
                Resume(false);
            }
            ShowMTC();
            SetFocus();
        }
        private void VideoMTC_FullWindowChanged(object sender, bool e)
        {
            bool isShowDanmakuBar = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInFullWindow, false);
            DanmakuBarVisibility = isShowDanmakuBar ? Visibility.Visible : Visibility.Collapsed;
            ExitScreenButton.Visibility = e ? Visibility.Visible : Visibility.Collapsed;
            ChangeDanmakuBarDisplayMode(!e, isShowDanmakuBar);
            FullWindowChanged?.Invoke(this, e);
        }
        private void VideoMTC_CinemaChanged(object sender, bool e)
        {
            bool isShowDanmakuBar = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInCinema, false);
            DanmakuBarVisibility = isShowDanmakuBar ? Visibility.Visible : Visibility.Collapsed;
            ExitScreenButton.Visibility = e ? Visibility.Visible : Visibility.Collapsed;
            ChangeDanmakuBarDisplayMode(!e, isShowDanmakuBar);
            CinemaChanged?.Invoke(this, e);
        }
        private async void VideoMTC_QualityChanged(object sender, int e)
        {
            if (_currentQn != e)
            {
                _currentQn = e;
                AppTool.WriteLocalSetting(Settings.FirstQuality, e.ToString());
                await BiliTool.WriteNetworkLogAsync($"清晰度切换：qn={e}，重新请求播放地址");
                // 清晰度变化必须按新 qn 重新请求播放地址，否则会一直播放旧清晰度的流
                if (isBangumi && _bangumiPart != null)
                    await RefreshVideoSource(_bangumiPart, forceRefresh: true);
                else if (!isBangumi)
                    await RefreshVideoSource(_partId, forceRefresh: true);
            }
        }
        private void VideoMTC_CompactOverlayButtonClick(object sender, bool e)
        {
            bool isShowDanmakuBar = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInCompactOverlay, false);
            DanmakuBarVisibility = isShowDanmakuBar ? Visibility.Visible : Visibility.Collapsed;
            ExitScreenButton.Visibility = e ? Visibility.Visible : Visibility.Collapsed;
            ChangeDanmakuBarDisplayMode(!e, isShowDanmakuBar);
            CompactOverlayChanged?.Invoke(this, e);
        }
        private void VideoMTC_SeparateButtonClick(object sender, RoutedEventArgs e)
        {
            if (VideoMTC.IsCompactOverlay)
                VideoMTC.IsCompactOverlay = false;
            if (VideoMTC.IsFullWindow)
                VideoMTC.IsFullWindow = false;
            if (VideoMTC.IsCinema)
                VideoMTC.IsCinema = false;
            ChangeDanmakuBarDisplayMode(true);
            SeparateButtonClick?.Invoke(this, e);
        }
        private async void VideoMTC_SubtitleChanged(object sender, SubtitleIndexItem e)
        {
            await SubtitleInit(e);
        }
        private void VideoMTC_PlayRateChanged(object sender, double e)
        {
            _playRate = e;
            if (_player != null)
                _player.PlaybackSession.PlaybackRate = _playRate;
            ShowTip($"播放倍率：{_playRate}");
        }
        private async void VideoMTC_PreviousButtonClick(object sender, RoutedEventArgs e)
        {
            var index = GetPreviousPartIndex();
            if (index == -1)
            {
                PreviousVideoRequest?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (isBangumi)
                await RefreshVideoSource(_bangumiDetail.episodes[index], true);
            else
                await RefreshVideoSource(_videoDetail.pages[index].cid, 0, true);
            PartSwitched?.Invoke(this, index);
        }

        private async void VideoMTC_NextButtonClick(object sender, RoutedEventArgs e)
        {
            var index = GetNextPartIndex();
            if (index == -1)
            {
                NextVideoRequest?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (isBangumi)
                await RefreshVideoSource(_bangumiDetail.episodes[index], true);
            else
                await RefreshVideoSource(_videoDetail.pages[index].cid, 0, true);
            PartSwitched?.Invoke(this, index);
        }
        #endregion

        #region MTC状态控制
        public void HideMTC()
        {
            _isMTCShow = false;
            VideoMTC.Hide();
            ExitScreenButton.Visibility = Visibility.Collapsed;
            LockScreenButton.Visibility = Visibility.Collapsed;
            ScreenShotButton.Visibility = Visibility.Collapsed;
            SubtitleContainer.Margin = new Thickness(20, 0, 20, 20);
            TipContainer.Margin = new Thickness(20, 0, 0, 20);
            if (MTC.IsFullWindow || MTC.IsCinema || MTC.IsCompactOverlay)
                DanmakuBarVisibility = Visibility.Collapsed;
        }

        public void ShowMTC()
        {
            if (_isPlayerLocked)
                return;
            bool isShowBarInFullWindow = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInFullWindow);
            bool isShowBarInCinema = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInCinema);
            bool isShowBarInCompact = AppTool.GetBoolSetting(Settings.IsShowDanmakuBarInCompactOverlay);
            _isMTCShow = true;
            if (VideoMTC.IsFullWindow || VideoMTC.IsCinema || VideoMTC.IsCompactOverlay)
                ExitScreenButton.Visibility = Visibility.Visible;
            LockScreenButton.Visibility = Visibility.Visible;
            ScreenShotButton.Visibility = Visibility.Visible;
            SubtitleContainer.Margin = new Thickness(20, 0, 20, 140);
            TipContainer.Margin = new Thickness(20, 0, 0, 140);
            VideoMTC.Show();
            if ((MTC.IsFullWindow && isShowBarInFullWindow) || (MTC.IsCinema && isShowBarInCinema) || (MTC.IsCompactOverlay && isShowBarInCompact))
                DanmakuBarVisibility = Visibility.Visible;
        }
        #endregion

        #region 依赖属性
        public Visibility CinemaButtonVisibility
        {
            get { return (Visibility)GetValue(CinemaButtonVisibilityProperty); }
            set { SetValue(CinemaButtonVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for CinemaButtonVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty CinemaButtonVisibilityProperty =
            DependencyProperty.Register("CinemaButtonVisibility", typeof(Visibility), typeof(VideoPlayer), new PropertyMetadata(Visibility.Visible));

        public Visibility FullWindowButtonVisibility
        {
            get { return (Visibility)GetValue(FullWindowButtonVisibilityProperty); }
            set { SetValue(FullWindowButtonVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for FullWindowButtonVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty FullWindowButtonVisibilityProperty =
            DependencyProperty.Register("FullWindowButtonVisibility", typeof(Visibility), typeof(VideoPlayer), new PropertyMetadata(Visibility.Visible));

        public Visibility CompactOverlayButtonVisibility
        {
            get { return (Visibility)GetValue(CompactOverlayButtonVisibilityProperty); }
            set { SetValue(CompactOverlayButtonVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for CompactOverlayButtonVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty CompactOverlayButtonVisibilityProperty =
            DependencyProperty.Register("CompactOverlayButtonVisibility", typeof(Visibility), typeof(VideoPlayer), new PropertyMetadata(Visibility.Visible));

        public Visibility SeparateButtonVisibility
        {
            get { return (Visibility)GetValue(SeparateButtonVisibilityProperty); }
            set { SetValue(SeparateButtonVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SeparateButtonVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SeparateButtonVisibilityProperty =
            DependencyProperty.Register("SeparateButtonVisibility", typeof(Visibility), typeof(VideoPlayer), new PropertyMetadata(Visibility.Visible));
        public Visibility DanmakuBarVisibility
        {
            get { return (Visibility)GetValue(DanmakuBarVisibilityProperty); }
            set { SetValue(DanmakuBarVisibilityProperty, value); }
        }

        // Using a DependencyProperty as the backing store for DanmakuBarVisibility.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty DanmakuBarVisibilityProperty =
            DependencyProperty.Register("DanmakuBarVisibility", typeof(Visibility), typeof(VideoPlayer), new PropertyMetadata(Visibility.Visible));

        /// <summary>
        /// 播放完成后是否自动解除全屏/影院/小窗模式
        /// </summary>
        public bool IsAutoReturnWhenEnd
        {
            get { return (bool)GetValue(IsAutoReturnWhenEndProperty); }
            set { SetValue(IsAutoReturnWhenEndProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsAutoReturnWhenEnd.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsAutoReturnWhenEndProperty =
            DependencyProperty.Register("IsAutoReturnWhenEnd", typeof(bool), typeof(VideoPlayer), new PropertyMetadata(true));

        public bool IsRegenerateFonts
        {
            get { return (bool)GetValue(IsRegenerateFontsProperty); }
            set { SetValue(IsRegenerateFontsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsRegenerateFonts.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsRegenerateFontsProperty =
            DependencyProperty.Register("IsRegenerateFonts", typeof(bool), typeof(VideoPlayer), new PropertyMetadata(false));

        public bool AutoLoop
        {
            get { return (bool)GetValue(AutoLoopProperty); }
            set { SetValue(AutoLoopProperty, value); }
        }

        // Using a DependencyProperty as the backing store for AutoLoop.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AutoLoopProperty =
            DependencyProperty.Register("AutoLoop", typeof(bool), typeof(VideoPlayer), new PropertyMetadata(false, new PropertyChangedCallback(AutoLoop_Changed)));

        private static void AutoLoop_Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var instance = d as VideoPlayer;
            if (instance._player != null)
                instance._player.IsLoopingEnabled = (bool)e.NewValue;
        }

        #endregion

        #region 计时器
        private void DefaultTimer_Tick(object sender, object e)
        {
            // 自动隐藏鼠标及媒体传输控件
            if (_pointerHoldCount >= 2)
            {
                bool isManual = AppTool.GetBoolSetting(Settings.IsManualMediaTransportControls, false);
                if (_isCatchPointer && Window.Current.CoreWindow.PointerCursor != null)
                    Window.Current.CoreWindow.PointerCursor = null;
                if (_isMTCShow && !isManual && !_isDanmakuBarFocus)
                {
                    HideMTC();
                }
            }
            if (_pointerHoldCount < 2)
                _pointerHoldCount++;
            // 自动隐藏提示文本
            if (_tipShowSeconds >= 2)
                HideTip();
            else if (_tipShowSeconds != -1)
                _tipShowSeconds++;
            if (_historyShowSeconds >= 5)
                HideHistory();
            else if (_historyShowSeconds != -1)
                _historyShowSeconds++;
            // 发送心跳包
            if (_heartBeatCount >= 10)
            {
                _heartBeatCount = 0;
                if (_player != null && _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                    HeartBeat();
            }
            else
                _heartBeatCount++;
            try
            {
                var player = mediaElement.MediaPlayer;
                if (DanmakuControls == null || player == null || DanmakuList.Count == 0)
                    return;
                if (DanmakuControls.Visibility != Visibility.Visible)
                    return;
                var session = player.PlaybackSession;
                if (session.PlaybackState != MediaPlaybackState.Playing)
                    return;
                double currentPosition = session.Position.TotalSeconds;
                if (currentPosition < _danmakuEmitPosition - 1.0 || currentPosition - _danmakuEmitPosition > 4.0)
                {
                    RebindDanmakuCursor(currentPosition);
                    DanmakuControls.ClearAll();
                    return;
                }
                int nowDanmaNum = 0;
                double windowEnd = currentPosition + 1.0;
                while (_danmakuCursor < DanmakuList.Count)
                {
                    var item = DanmakuList[_danmakuCursor];
                    if (item.time > windowEnd)
                        break;
                    _danmakuCursor++;
                    if (item.time <= currentPosition || item.time <= _danmakuEmitPosition)
                        continue;
                    if (_maxDanmakuNumber != 0 && nowDanmaNum >= _maxDanmakuNumber)
                        continue;
                    if (IsTextShouldShield(item.text))
                        continue;
                    switch (item.location)
                    {
                        case DanmakuLocation.Top:
                            DanmakuControls.AddTopDanmu(item, false);
                            break;
                        case DanmakuLocation.Bottom:
                            DanmakuControls.AddBottomDanmu(item, false);
                            break;
                        case DanmakuLocation.Position:
                            DanmakuControls.AddPositionDanmu(item);
                            break;
                        default:
                            DanmakuControls.AddRollDanmu(item, false);
                            break;
                    }
                    nowDanmaNum++;
                }
                _danmakuEmitPosition = currentPosition;
            }
            catch (Exception)
            {

            }
        }
        private void SubtitleTimer_Tick(object sender, object e)
        {
            if (_player == null || Subtitles.Count == 0)
                return;
            if (_player.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)
                return;
            var time = _player.PlaybackSession.Position.TotalSeconds;
            var subtitle = Subtitles.FirstOrDefault(p => p.from <= time && p.to >= time);
            if (subtitle != null && !string.IsNullOrEmpty(subtitle.content))
            {
                ShowSubtitle(subtitle.content);
            }
            else
            {
                HideSubtitle();
            }
        }
        #endregion

        #region 其它
        private async void HeartBeat()
        {
            if (isBangumi)
                await _animeService.AddVideoHistoryAsync(_bangumiPart.aid, _bangumiPart.cid, _bangumiPart.id, CurrentProgress, _bangumiDetail.season_id);
            else
                await _videoService.AddVideoHistoryAsync(_videoId, _partId, CurrentProgress);
        }
        private void FontInit()
        {
            FontComboBox.IsEnabled = false;
            if (FontCollection.Count == 0)
            {
                if (IsRegenerateFonts)
                {
                    var fonts = SystemFont.GetSystemFonts("zh-cn");
                    fonts.ForEach(p => FontCollection.Add(p));
                }
                else
                {
                    foreach (var item in App.AppViewModel.FontCollection)
                    {
                        FontCollection.Add(item);
                    }
                }
            }
            if (FontCollection != null && FontCollection.Count > 0)
            {
                string fontName = AppTool.GetLocalSetting(Settings.DanmakuFontFamily, "微软雅黑");
                var font = FontCollection.Where(p => p.Name == fontName).FirstOrDefault();
                if (font != null)
                {
                    FontComboBox.SelectedItem = font;
                    DanmakuControls.font = fontName;
                    try
                    {
                        SubtitleContentBlock.FontFamily = font.FontFamily;
                    }
                    catch (Exception)
                    {
                        SubtitleContentBlock.FontFamily = new FontFamily(font.Name);
                    }
                }
            }
            FontComboBox.IsEnabled = true;
        }
        public async void ResetDanmakuStatus()
        {
            bool isShow = AppTool.GetBoolSetting(Settings.IsDanmakuOpen);
            if (isShow && DanmakuControls != null)
            {
                DanmakuControls.Width = 100;
                await Task.Delay(100);
                DanmakuControls.Width = double.NaN;
            }
        }
        public bool ExitCurrentStatus()
        {
            bool isHandled = true;
            if (VideoMTC.IsFullWindow)
                VideoMTC.IsFullWindow = false;
            else if (VideoMTC.IsCinema)
                VideoMTC.IsCinema = false;
            else if (VideoMTC.IsCompactOverlay)
                VideoMTC.IsCompactOverlay = false;
            else
                isHandled = false;
            return isHandled;
        }
        public void ShowTip(string content)
        {
            if (TipContainer.Visibility == Visibility.Collapsed)
                TipContainer.Visibility = Visibility.Visible;
            TipContentBlock.Text = content;
            _tipShowSeconds = 0;
        }
        public void HideTip()
        {
            if (TipContainer.Visibility == Visibility.Visible)
                TipContainer.Visibility = Visibility.Collapsed;
            TipContentBlock.Text = "";
            _tipShowSeconds = -1;
        }
        public void ShowHistory(string progress)
        {
            HistoryTimeBlock.Text = progress;
            if (HistoryContainer.Visibility == Visibility.Collapsed)
                HistoryContainer.Visibility = Visibility.Visible;
            _historyShowSeconds = 0;
        }
        public void HideHistory()
        {
            if (HistoryContainer.Visibility == Visibility.Visible)
                HistoryContainer.Visibility = Visibility.Collapsed;
            _historyShowSeconds = -1;
            if (!isBangumi)
                _videoDetail.history = null;
        }
        public async Task<bool> ShowNextPart()
        {
            var index = GetNextPartIndex();
            if (index != -1 && _player != null && CurrentProgress >= _player.PlaybackSession.NaturalDuration.TotalSeconds - 1)
            {
                bool isAutoNext = AppTool.GetBoolSetting(Settings.IsAutoNextPart, false);
                if (!isAutoNext)
                {
                    if (NextPartContainer.Visibility == Visibility.Collapsed)
                        NextPartContainer.Visibility = Visibility.Visible;
                }
                else
                {
                    await SwitchNextPart(index);
                }
                return true;
            }
            return false;
        }
        public void HideNextPart()
        {
            if (NextPartContainer.Visibility == Visibility.Visible)
                NextPartContainer.Visibility = Visibility.Collapsed;
        }
        public void ChangeDanmakuBarDisplayMode(bool isSingleRow = false, bool isShow = true)
        {
            if (!isSingleRow)
            {
                bool needChange = Grid.GetRow(DanmakuBarContainer) == 1;
                if (needChange)
                {
                    Grid.SetRow(DanmakuBarContainer, 0);
                }
                if (isShow)
                    VisualStateManager.GoToState(mediaElement, "MarginState", false);
                else
                    VisualStateManager.GoToState(mediaElement, "DefaultState", false);
            }
            else
            {
                DanmakuBarVisibility = Visibility.Visible;
                bool needChange = Grid.GetRow(DanmakuBarContainer) == 0;
                if (needChange)
                {
                    Grid.SetRow(DanmakuBarContainer, 1);
                }
                VisualStateManager.GoToState(mediaElement, "DefaultState", false);
            }
        }
        public async Task SwitchNextPart(int index)
        {
            if (index != -1)
            {
                if (!isBangumi)
                {
                    var next = _videoDetail.pages[index];
                    await RefreshVideoSource(next.cid, 0, true);
                }
                else
                {
                    var next = _bangumiDetail.episodes[index];
                    await RefreshVideoSource(next, true);
                }
                PartSwitched?.Invoke(this, index);
            }
        }
        private int GetNextPartIndex()
        {
            if (!isBangumi)
            {
                var index = _videoDetail.pages.IndexOf(_videoDetail.pages.Where(p => p.cid == _partId).FirstOrDefault());
                if (index != -1 && index < _videoDetail.pages.Count - 1)
                {
                    return index + 1;
                }
            }
            else
            {
                var index = _bangumiDetail.episodes.IndexOf(_bangumiPart);
                if (index != -1 && index < _bangumiDetail.episodes.Count - 1)
                {
                    return index + 1;
                }
            }
            return -1;
        }
        private int GetPreviousPartIndex()
        {
            if (!isBangumi)
            {
                var index = _videoDetail.pages.IndexOf(_videoDetail.pages.Where(p => p.cid == _partId).FirstOrDefault());
                if (index != -1 && index > 0)
                {
                    return index - 1;
                }
            }
            else
            {
                var index = _bangumiDetail.episodes.IndexOf(_bangumiPart);
                if (index != -1 && index > 0)
                {
                    return index - 1;
                }
            }
            return -1;
        }
        public void SetFocus()
        {
            mediaElement.Focus(FocusState.Programmatic);
        }

        public void LockPlayer()
        {
            MaskContainer.Visibility = Visibility.Visible;
            LockScreenButton.Content = "";
            _isPlayerLocked = true;
        }
        public void UnlockPlayer()
        {
            MaskContainer.Visibility = Visibility.Collapsed;
            LockScreenButton.Content = "";
            _isPlayerLocked = false;
        }
        public async Task ChangeDanmakuStatus()
        {
            bool isShowDanmaku = AppTool.GetBoolSetting(Settings.IsDanmakuOpen);
            isShowDanmaku = !isShowDanmaku;
            AppTool.WriteLocalSetting(Settings.IsDanmakuOpen, isShowDanmaku.ToString());
            DanmakuVisibilityButton.Content = isShowDanmaku ? "" : "";
            if (isShowDanmaku)
                await LoadDanmakuAsync();
            else
            {
                _danmakuGeneration++;
                DanmakuList = new List<DanmakuModel>();
                _danmakuCursor = 0;
                _danmakuEmitPosition = -1;
                DanmakuControls.Visibility = Visibility.Collapsed;
                DanmakuControls.ClearAll();
            }
        }
        public async Task ScreenShot()
        {
            if (_playData != null && _player != null && _tempSource != null)
            {
                CanvasRenderTarget rendertarget = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), _player.PlaybackSession.NaturalVideoWidth, _player.PlaybackSession.NaturalVideoHeight, 96);
                _player.CopyFrameToVideoSurface(rendertarget);
                var folder = await KnownFolders.PicturesLibrary.CreateFolderAsync("Bili ScreenShot", CreationCollisionOption.OpenIfExists);
                var file = await folder.CreateFileAsync(Guid.NewGuid().ToString("N") + ".png", CreationCollisionOption.OpenIfExists);
                using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    await rendertarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }
                NotificationTool.SendScreenShotToast(file.Name, folder.Path);
            }
        }
        public void ResetPlayRate()
        {
            _playRate = 1;
            if (VideoMTC._playRateComboBox != null)
            {
                int index = PlayRateCollection.IndexOf(PlayRateCollection.Where(q => q.Item1 == _playRate).FirstOrDefault());
                VideoMTC._playRateComboBox.SelectedIndex = index;
            }
        }

        #endregion


    }
}
