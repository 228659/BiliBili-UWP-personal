using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BiliBili_Lib.Enums
{
    public enum Settings
    {
        LocalVersion,
        Theme,
        AccessToken,
        RefreshToken,
        UserId,
        TokenExpiry,
        SearchHistory,
        FontFamily,
        IsDanmakuOpen,
        IsUseHevc,
        IsAutoPlay,
        DanmakuMaxNumber,
        IsInitEmoji,
        IsManualMediaTransportControls,
        PlayerSkipStep,
        PlayerLastVolume,
        DanmakuOpacity,
        DanmakuFontSize,
        DanmakuSpeed,
        DanmakuFontFamily,
        DanmakuMerge,
        DanmakuProtectSubtitle,
        DanmakuBorderStyle,
        IsStopInBackground,
        IsDynamicOnlyVideo,
        IsShowDanmakuBarInFullWindow,
        IsShowDanmakuBarInCinema,
        IsShowDanmakuBarInCompactOverlay,
        IsShowDanmakuBarInSeparate,
        IsShowDanmakuInCompactOverlay,
        FirstQuality,
        PagePanelDisplayBreakpoint,
        IsThemeWithSystem,
        IsLastSidePanelOpen,
        LastSeemDynamicId,
        IsOpenNewDynamicNotification,
        EnableAnimation,
        IsAutoNextPart,
        PlayerMode,
        BasicFontSize,
        DisplayMode,
        DisableXboxScale,
        IsFirst4K,
        RankRegionIds,
        RankColumnCount, // 键名沿用旧值以保留已保存设置，现为所有视频列表的每列数量
        AcrylicTransparency
    }
}
