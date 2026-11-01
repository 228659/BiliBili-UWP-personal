using BiliBili_UWP.Components.Widgets;
using BiliBili_UWP.Models.UI;
using BiliBili_UWP.Models.UI.Interface;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using ZXing;
using ZXing.QrCode;
using ZXing.Rendering;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace BiliBili_UWP.Components.Account
{
    public sealed partial class LoginPopup : UserControl, IAppPopup
    {
        public Popup _popup { get; set; }
        public Guid _popupId { get; set; }
        private readonly DispatcherTimer _pollTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(2) };
        private string _authCode;
        private bool _isPolling;
        private bool _isActive;

        public LoginPopup()
        {
            this.InitializeComponent();
            UIHelper.PopupInit(this);
            _pollTimer.Tick += PollTimer_Tick;
        }

        public async void ShowPopup()
        {
            _isActive = true;
            StatusBlock.Text = "正在获取二维码...";
            RefreshButton.Visibility = Visibility.Collapsed;
            QrImage.Source = null;
            QrLoadingRing.IsActive = true;
            UIHelper.PopupShow(this);
            PopupIn.Begin();
            await LoadQrAsync();
        }
        public void HidePopup()
        {
            _isActive = false;
            _pollTimer.Stop();
            PopupOut.Begin();
            PopupOut.Completed -= PopupOut_Completed;
            PopupOut.Completed += PopupOut_Completed;
        }

        private void PopupOut_Completed(object sender, object e)
        {
            App.AppViewModel.WindowsSizeChangedNotify.RemoveAll(p => p.Item1 == _popupId);
            _popup.IsOpen = false;
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HidePopup();
        }

        private async Task LoadQrAsync()
        {
            _pollTimer.Stop();
            _authCode = null;
            QrImage.Source = null;
            QrLoadingRing.IsActive = true;
            RefreshButton.Visibility = Visibility.Collapsed;
            StatusBlock.Text = "正在获取二维码...";
            var qr = await App.BiliViewModel._client.Account.GetTvLoginQrAsync();
            if (!_isActive)
                return;
            if (qr == null)
            {
                QrLoadingRing.IsActive = false;
                RefreshButton.Visibility = Visibility.Visible;
                StatusBlock.Text = "二维码获取失败，请检查网络后刷新重试";
                return;
            }
            _authCode = qr.AuthCode;
            QrImage.Source = RenderQr(qr.Url);
            QrLoadingRing.IsActive = false;
            StatusBlock.Text = "请使用哔哩哔哩手机客户端扫码登录";
            _pollTimer.Start();
        }

        private WriteableBitmap RenderQr(string content)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Width = 440,
                    Height = 440,
                    Margin = 2,
                    CharacterSet = "UTF-8"
                }
            };
            var pixels = writer.Write(content);
            var bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
            if (pixels.Pixels.Length == pixels.Width * pixels.Height * 4)
            {
                for (int i = 3; i < pixels.Pixels.Length; i += 4)
                    pixels.Pixels[i] = 255;
            }
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixels.Pixels, 0, pixels.Pixels.Length);
                stream.Flush();
            }
            bitmap.Invalidate();
            return bitmap;
        }

        private async void PollTimer_Tick(object sender, object e)
        {
            if (_isPolling || string.IsNullOrEmpty(_authCode) || !_isActive)
                return;
            _isPolling = true;
            try
            {
                var result = await App.BiliViewModel._client.Account.PollTvLoginAsync(_authCode);
                if (!_isActive)
                    return;
                if (result == null)
                {
                    StatusBlock.Text = "网络异常，正在重试...";
                    return;
                }
                switch (result.Code)
                {
                    case 0:
                        _pollTimer.Stop();
                        QrLoadingRing.IsActive = true;
                        StatusBlock.Text = "登录成功，正在获取账号信息...";
                        await App.BiliViewModel.GetMeAsync();
                        if (!App.BiliViewModel.IsLogin)
                        {
                            await Task.Delay(1200);
                            await App.BiliViewModel.GetMeAsync();
                        }
                        QrLoadingRing.IsActive = false;
                        if (App.BiliViewModel.IsLogin)
                        {
                            StatusBlock.Text = "登录成功";
                            HidePopup();
                        }
                        else
                        {
                            RefreshButton.Visibility = Visibility.Visible;
                            StatusBlock.Text = "登录凭证已保存，但账号信息获取失败，请点击刷新重试";
                        }
                        break;
                    case 86038:
                        _pollTimer.Stop();
                        RefreshButton.Visibility = Visibility.Visible;
                        StatusBlock.Text = "二维码已失效，请点击刷新";
                        break;
                    case 86090:
                        StatusBlock.Text = "已扫码，请在手机上确认登录";
                        break;
                    case 86039:
                    case 86101:
                        StatusBlock.Text = "请使用哔哩哔哩手机客户端扫码登录";
                        break;
                    default:
                        _pollTimer.Stop();
                        RefreshButton.Visibility = Visibility.Visible;
                        StatusBlock.Text = string.IsNullOrEmpty(result.Message) ? "登录失败，请刷新重试" : result.Message;
                        break;
                }
            }
            finally
            {
                _isPolling = false;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadQrAsync();
        }
    }
}
