using BiliBili_Lib.Enums;
using BiliBili_Lib.Models.BiliBili;
using BiliBili_Lib.Models.Others;
using BiliBili_Lib.Service;
using BiliBili_Lib.Tools;
using BiliBili_UWP.Components.Account;
using BiliBili_UWP.Components.Widgets;
using BiliBili_UWP.Models.UI.Others;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace BiliBili_UWP.Models.Core
{
    public partial class BiliViewModel
    {
        private bool _isLogin;
        public bool IsLogin
        {
            get => _isLogin;
            set
            {
                if (_isLogin != value)
                {
                    Set(ref _isLogin, value);
                    IsLoginChanged?.Invoke(this, value);
                }
            }
        }

        public bool IsLogining = false;

        public event EventHandler<bool> IsLoginChanged;
        public event EventHandler MyInfoChanged;
        public DispatcherTimer _messageTimer = new DispatcherTimer() { Interval = TimeSpan.FromMinutes(10) };
        public LoginPopup LoginPopup;
        public void ShowLoginPopup()
        {
            if (LoginPopup == null)
                LoginPopup = new LoginPopup();
            LoginPopup.ShowPopup();
        }

        private async void MessageTimer_Tick(object sender, object e)
        {
            await CheckUnreadMessage();
            await _client.Account.SSO();
        }

        public async Task CheckUnreadMessage()
        {
            if (IsLogin)
            {
                var unread = await _client.Account.GetMyUnreadMessageAsync();
                if (unread != null)
                {
                    int total = unread.like + unread.reply + unread.at;
                    if (!App._isTabletMode)
                        App.AppViewModel.CurrentSidePanel.SetMenuItemUnread(Enums.AppMenuItemType.MyMessage, total);
                    else
                        App.AppViewModel.CurrentTopPanel.SetUnreadCount(total);
                }
            }
        }

        /// <summary>
        /// 获取我的个人信息
        /// </summary>
        /// <returns></returns>
        public async Task GetMeAsync()
        {
            var data = await _client.Account.GetMeAsync();
            if (data != null && data.mid > 0)
            {
                if (!IsLogin)
                {
                    IsLogin = true;
                }
            }
        }

        public async void ChangeMyInfo()
        {
            await GetMeAsync();
            MyInfoChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearAccountInformation()
        {
            IsLogin = false;
            AppTool.WriteLocalSetting(Settings.AccessToken, "");
            AppTool.WriteLocalSetting(Settings.RefreshToken, "");
            AppTool.WriteLocalSetting(Settings.TokenExpiry, "0");
            AppTool.WriteLocalSetting(Settings.UserId, "");
            AppTool.WriteLocalSetting(Settings.LastSeemDynamicId, "");
            BiliTool.ClearCookies();
            _client.Account = new AccountService(new TokenPackage());
            _client.Account.TokenChanged += TokenChanged;
            BiliTool.ClearCookies();
        }

        /// <summary>
        /// 自动登录
        /// </summary>
        /// <returns></returns>
        public async Task AutoLoginAsync()
        {
            if (!string.IsNullOrEmpty(_client.Account._accessToken))
            {
                bool isValid = false;
                isValid = await _client.Account.CheckTokenStatusAsync();
                if (isValid)
                {
                    await GetMeAsync();
                    // 令牌有效时也要换取一次 Web Cookie，动态时间线等网页接口依赖 SESSDATA
                    await _client.Account.SSO();
                }
                else
                {
                    isValid = await _client.Account.RefreshTokenAsync();
                    if (!isValid)
                        ClearAccountInformation();
                    else
                        await GetMeAsync();
                }
            }
            else
            {
                await _client.Account.SSO();
                IsLogin = false;
            }
            await CheckUnreadMessage();
            if (IsLogin)
                _ = PrepareWebSessionAsync();
        }
        /// <summary>
        /// 预热动态页依赖的 Web 登录态、buvid 与 WBI 密钥
        /// </summary>
        /// <returns></returns>
        public async Task PrepareWebSessionAsync()
        {
            if (!IsLogin)
                return;
            var watch = Stopwatch.StartNew();
            try
            {
                var tasks = new List<Task>();
                if (!BiliTool.GetLoginCookieNames().Contains("SESSDATA"))
                    tasks.Add(_client.Account.SSO());
                tasks.Add(BiliTool.EnsureBuvidCookieAsync());
                tasks.Add(BiliTool.PrepareWbiKeyAsync());
                await Task.WhenAll(tasks);
                await BiliTool.WriteNetworkLogAsync($"Web 会话预热完成：{watch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                BiliTool._logger.Warn($"Web 会话预热失败：{ex.Message}");
            }
        }
        /// <summary>
        /// 检查用户是否登录
        /// </summary>
        /// <returns></returns>
        public bool CheckAccoutStatus()
        {
            var me = _client.Account.Me;
            if (me == null)
            {
                new TipPopup("你需要登录才能执行此操作").ShowError();
                return false;
            }
            return true;
        }
    }
}
