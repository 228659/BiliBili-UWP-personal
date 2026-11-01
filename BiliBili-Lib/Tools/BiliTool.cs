using BiliBili_Lib.Models.BiliBili;
using BiliBili_Lib.Models.Others;
using MetroLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Security.Cryptography.Certificates;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace BiliBili_Lib.Tools
{
    public class BiliTool
    {
        public static ApiKeyInfo AndroidKey = new ApiKeyInfo("4409e2ce8ffd12b8", "59b43e04ad6965f34319062b478f83dd");
        public static ApiKeyInfo AndroidVideoKey = new ApiKeyInfo("iVGUTjsxvpLeuDCf", "aHRmhWMLkdeMuILqORnYZocwMBpMEOdt");
        public static ApiKeyInfo WebVideoKey = new ApiKeyInfo("84956560bc028eb7", "94aba54af9065f71de72f5508f1cd42e");
        public static ApiKeyInfo VideoKey = new ApiKeyInfo("", "1c15888dc316e05a15fdd0a02ed6584f");
        public static ApiKeyInfo IosKey = new ApiKeyInfo("4ebafd7c4951b366", "8cb98205e9b2ad3669aad0fce12a4c13");
        public const string BuildNumber = "5520400";
        public static string _accessToken = "";
        public static string sid = "";
        public static string mid = "";
        public static ILogger _logger = LogManagerFactory.CreateLogManager().GetLogger("网络请求");
        private const string AcceptHeader = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9";
        private static readonly HttpBaseProtocolFilter CookieFilter = CreateCookieFilter();
        private static readonly HttpClient CookieClient = CreateCookieClient(CookieFilter);
        private static string _wbiMixinKey = null;
        private static DateTime _wbiKeyExpire = DateTime.MinValue;
        private static readonly int[] MixinKeyEncTab = new int[]
        {
            46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
            27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13,
            37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4,
            22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52
        };
        private static HttpBaseProtocolFilter CreateCookieFilter()
        {
            var filter = new HttpBaseProtocolFilter();
            filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
            // SSO 通过 302 跳转下发 SESSDATA，自动跳转才能把整条链路上的 Cookie 收进 CookieManager
            filter.AllowAutoRedirect = true;
            return filter;
        }
        private static HttpClient CreateCookieClient(HttpBaseProtocolFilter filter)
        {
            var client = new HttpClient(filter);
            client.DefaultRequestHeaders.TryAppendWithoutValidation("accept", AcceptHeader);
            return client;
        }
        public static async Task WriteNetworkLogAsync(string message)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("network.log", CreationCollisionOption.OpenIfExists);
                await FileIO.AppendTextAsync(file, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
        /// <summary>
        /// 从网络获取文本
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static async Task<string> GetTextFromWebAsync(string url, bool total = false, string path = "data", bool needReferer = false, bool useCookie = false)
        {
            HttpBaseProtocolFilter filter = null;
            HttpClient client;
            if (useCookie)
                client = CookieClient;
            else
            {
                filter = new HttpBaseProtocolFilter();
                filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
                filter.AllowAutoRedirect = false;
                client = new HttpClient(filter);
            }
            try
            {
                if (!useCookie)
                    client.DefaultRequestHeaders.TryAppendWithoutValidation("accept", AcceptHeader);
                var uri = new Uri(url);
                if (needReferer)
                    client.DefaultRequestHeaders.Referer = new Uri(uri.Scheme + "://" + uri.Host);
                var response = await client.GetAsync(new Uri(url));
                if (response.IsSuccessStatusCode)
                {
                    string res = await response.Content.ReadAsStringAsync();
                    if (total)
                        return res;
                    var jobj = JObject.Parse(res);
                    string content = string.Empty;
                    if (jobj.SelectToken(path) != null)
                        content = jobj.SelectToken(path).ToString();
                    else
                        content = res;
                    return content;
                }
                else if (response.StatusCode == Windows.Web.Http.HttpStatusCode.MovedPermanently
                    || response.StatusCode == Windows.Web.Http.HttpStatusCode.Found
                    || response.StatusCode == Windows.Web.Http.HttpStatusCode.SeeOther
                    || response.StatusCode == Windows.Web.Http.HttpStatusCode.TemporaryRedirect)
                {
                    string tempUrl = response.Headers.Location.AbsoluteUri;
                    return await GetTextFromWebAsync(tempUrl, total, path, needReferer, useCookie);
                }
                else
                {
                    string body = await response.Content.ReadAsStringAsync();
                    await WriteNetworkLogAsync($"状态码 {(int)response.StatusCode}：URL: {url}; Body: {body}");
                    _logger.Warn($"请求数据异常(Text)：URL: {url}; Message: {body}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                await WriteNetworkLogAsync($"请求异常：URL: {url}; {ex}");
                _logger.Error($"请求出现异常:{url}", ex);
                return null;
            }
            finally
            {
                if (filter != null)
                {
                    client.Dispose();
                    filter.Dispose();
                }
            }
        }
        /// <summary>
        /// 获取网络数据并转化为对应的类型
        /// </summary>
        /// <typeparam name="T">类型</typeparam>
        /// <param name="url">地址</param>
        /// <returns></returns>
        public static async Task<T> ConvertEntityFromWebAsync<T>(string url, string path = "data", bool needReferer = false) where T : class
        {
            string response = await GetTextFromWebAsync(url, path: path, needReferer: needReferer);
            if (!string.IsNullOrEmpty(response))
            {
                try
                {
                    return JsonConvert.DeserializeObject<T>(response);
                }
                catch (Exception ex)
                {
                    _logger.Error($"数据转化失败: {nameof(T)}", ex);
                }
            }
            return null;
        }
        /// <summary>
        /// 从网络获取流
        /// </summary>
        /// <param name="url">地址</param>
        /// <returns></returns>
        public static async Task<Stream> GetStreamFromWebAsync(string url)
        {
            HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter();
            var client = new HttpClient(filter);
            using (client)
            {
                client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 BiliDroid/4.34.0 (bbcallen@gmail.com)");
                client.DefaultRequestHeaders.Referer = new Uri("http://www.bilibili.com/");
                var response = await client.GetAsync(new Uri(url));
                if (response.IsSuccessStatusCode)
                {
                    var buffer = await response.Content.ReadAsBufferAsync();
                    return buffer.AsStream();
                }
                else
                {
                    _logger.Warn($"请求数据(Stream)异常：URL: {url}; Message: {await response.Content.ReadAsStringAsync()}");
                    return null;
                }
            }
        }
        /// <summary>
        /// 向网络上传字符串
        /// </summary>
        /// <param name="url">地址</param>
        /// <param name="content">数据</param>
        /// <returns></returns>
        public static async Task<string> PostContentToWebAsync(string url, string content)
        {
            HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter();
            filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
            var client = new HttpClient(filter);
            if (url.Contains("oauth2/login") && !string.IsNullOrEmpty(sid))
            {
                filter.CookieManager.SetCookie(new HttpCookie("sid", "bilibili.com", "/") { Value = sid });
            }
            using (client)
            {
                client.DefaultRequestHeaders.Referer = new Uri("http://www.bilibili.com/");
                var response = await client.PostAsync(new Uri(url), new HttpStringContent(content, Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/x-www-form-urlencoded"));
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else
                {
                    _logger.Warn($"上传数据异常：URL: {url}; Message: {await response.Content.ReadAsStringAsync()}");
                }
                return "";
            }
        }
        /// <summary>
        /// 获取B站网页的Cookie
        /// </summary>
        /// <returns></returns>
        public static string GetCookies()
        {
            HttpBaseProtocolFilter hb = new HttpBaseProtocolFilter();
            HttpCookieCollection cookieCollection = hb.CookieManager.GetCookies(new Uri("http://bilibili.com/"));
            string cookie = "";
            foreach (HttpCookie item in cookieCollection)
            {
                cookie += item.Name + "=" + item.Value + ";";
            }
            return cookie;
        }
        /// <summary>
        /// 清除Cookie
        /// </summary>
        public static void ClearCookies()
        {
            List<HttpCookie> listCookies = new List<HttpCookie>();
            listCookies.Add(new HttpCookie("sid", ".bilibili.com", "/"));
            listCookies.Add(new HttpCookie("DedeUserID", ".bilibili.com", "/"));
            listCookies.Add(new HttpCookie("DedeUserID__ckMd5", ".bilibili.com", "/"));
            listCookies.Add(new HttpCookie("SESSDATA", ".bilibili.com", "/"));
            listCookies.Add(new HttpCookie("LIVE_LOGIN_DATA", ".bilibili.com", "/"));
            listCookies.Add(new HttpCookie("LIVE_LOGIN_DATA__ckMd5", ".bilibili.com", "/"));
            HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter();
            foreach (HttpCookie cookie in listCookies)
            {
                filter.CookieManager.DeleteCookie(cookie);
            }
        }
        /// <summary>
        /// 读取带登录态 Cookie 的名称（不输出值）
        /// </summary>
        public static List<string> GetLoginCookieNames()
        {
            var names = new List<string>();
            try
            {
                foreach (var cookie in CookieFilter.CookieManager.GetCookies(new Uri("https://api.bilibili.com/")))
                    names.Add(cookie.Name);
            }
            catch
            {
            }
            return names;
        }
        /// <summary>
        /// 确保有 buvid 设备标识，否则 Web 接口容易触发风控
        /// </summary>
        public static async Task EnsureBuvidCookieAsync()
        {
            try
            {
                var names = GetLoginCookieNames();
                if (names.Contains("buvid3"))
                    return;
                string content = await GetTextFromWebAsync("https://api.bilibili.com/x/frontend/finger/spi", true);
                if (string.IsNullOrEmpty(content))
                    return;
                var jobj = JObject.Parse(content);
                string b3 = jobj.SelectToken("data.b_3")?.ToString();
                string b4 = jobj.SelectToken("data.b_4")?.ToString();
                if (!string.IsNullOrEmpty(b3))
                    CookieFilter.CookieManager.SetCookie(new HttpCookie("buvid3", ".bilibili.com", "/") { Value = b3 });
                if (!string.IsNullOrEmpty(b4))
                    CookieFilter.CookieManager.SetCookie(new HttpCookie("buvid4", ".bilibili.com", "/") { Value = b4 });
            }
            catch (Exception ex)
            {
                _logger.Warn($"buvid Cookie 获取失败：{ex.Message}");
            }
        }
        /// <summary>
        /// 生成 WBI 签名查询串
        /// </summary>
        public static async Task<string> CreateWbiQueryAsync(Dictionary<string, string> parameters)
        {
            string mixinKey = await GetWbiMixinKeyAsync();
            if (string.IsNullOrEmpty(mixinKey))
                return null;
            var sorted = parameters.Where(p => p.Key != "wts").OrderBy(p => p.Key, StringComparer.Ordinal);
            string query = string.Join("&", sorted.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value ?? "")}"));
            string wts = AppTool.GetNowSeconds().ToString();
            string wRid = MD5Tool.GetMd5String(query + "&wts=" + wts + mixinKey).ToLower();
            return query + $"&wts={wts}&w_rid={wRid}";
        }
        private static async Task<string> GetWbiMixinKeyAsync()
        {
            if (!string.IsNullOrEmpty(_wbiMixinKey) && DateTime.Now < _wbiKeyExpire)
                return _wbiMixinKey;
            try
            {
                string content = await GetTextFromWebAsync(Api.NAV, true, useCookie: true);
                if (string.IsNullOrEmpty(content))
                    return null;
                var jobj = JObject.Parse(content);
                string imgUrl = jobj.SelectToken("data.wbi_img.img_url")?.ToString();
                string subUrl = jobj.SelectToken("data.wbi_img.sub_url")?.ToString();
                if (string.IsNullOrEmpty(imgUrl) || string.IsNullOrEmpty(subUrl))
                {
                    _logger.Warn("WBI 密钥获取失败：nav 未返回 wbi_img");
                    return null;
                }
                string raw = Path.GetFileNameWithoutExtension(new Uri(imgUrl).AbsolutePath) + Path.GetFileNameWithoutExtension(new Uri(subUrl).AbsolutePath);
                var builder = new StringBuilder();
                foreach (var index in MixinKeyEncTab)
                {
                    if (index < raw.Length)
                        builder.Append(raw[index]);
                }
                string mixinKey = builder.ToString();
                if (mixinKey.Length < 32)
                {
                    _logger.Warn("WBI 密钥长度不足");
                    return null;
                }
                _wbiMixinKey = mixinKey.Substring(0, 32);
                _wbiKeyExpire = DateTime.Now.AddHours(1);
                return _wbiMixinKey;
            }
            catch (Exception ex)
            {
                _logger.Error("WBI 密钥获取异常", ex);
                return null;
            }
        }

        /// <summary>
        /// 获取签名
        /// </summary>
        /// <param name="url">网址</param>
        /// <param name="apiKeyInfo">取用的Api密钥</param>
        /// <returns></returns>
        public static string GetSign(string url, ApiKeyInfo apiKeyInfo = null)
        {
            if (apiKeyInfo == null)
            {
                apiKeyInfo = AndroidKey;
            }
            string result;
            if (url.StartsWith("http"))
                url.Substring(url.IndexOf("?", 4) + 1);
            List<string> list = url.Split('&').ToList();
            list.Sort();
            StringBuilder stringBuilder = new StringBuilder();
            foreach (string str1 in list)
            {
                stringBuilder.Append((stringBuilder.Length > 0 ? "&" : string.Empty));
                stringBuilder.Append(str1);
            }
            stringBuilder.Append(apiKeyInfo.Secret);
            result = MD5Tool.GetMd5String(stringBuilder.ToString()).ToLower();
            return result;
        }
        /// <summary>
        /// 请求地址拼接
        /// </summary>
        /// <param name="_baseUrl">请求url</param>
        /// <param name="parameters">查询参数</param>
        /// <param name="hasAccessKey">是否包含令牌</param>
        /// <returns></returns>
        public static string UrlContact(string _baseUrl, Dictionary<string, string> parameters = null, bool hasAccessKey = false, bool useWeb = false,
            bool useiPhone = false)
        {
            if (parameters == null)
                parameters = new Dictionary<string, string>();

            parameters.Add("build", BuildNumber);
            if (useiPhone)
            {
                parameters.Add("appkey", IosKey.Appkey);
                parameters.Add("mobi_app", "iphone");
                parameters.Add("platform", "ios");
                parameters.Add("ts", AppTool.GetNowSeconds().ToString());
            }
            else if (!useWeb)
            {
                parameters.Add("appkey", AndroidKey.Appkey);
                parameters.Add("mobi_app", "android");
                parameters.Add("platform", "android");
                parameters.Add("ts", AppTool.GetNowSeconds().ToString());
            }
            else
            {
                parameters.Add("appkey", WebVideoKey.Appkey);
                parameters.Add("ts", AppTool.GetNowMilliSeconds().ToString());
            }
            if (hasAccessKey && !string.IsNullOrEmpty(_accessToken))
                parameters.Add("access_key", _accessToken);
            string param = string.Empty;
            foreach (var item in parameters)
            {
                param += $"{item.Key}={item.Value}&";
            }
            param = param.TrimEnd('&');
            string sign = useWeb ? GetSign(param, WebVideoKey) : GetSign(param);
            param += $"&sign={sign}";
            return !string.IsNullOrEmpty(_baseUrl) ? _baseUrl + $"?{param}" : param;
        }

        /// <summary>
        /// 分析URL指向的结果
        /// </summary>
        /// <param name="url">地址</param>
        /// <returns></returns>
        public static UriResult GetResultFromUri(string url)
        {
            var uri = new Uri(url);
            string path = uri.AbsolutePath;
            if (path.Contains("/ep", StringComparison.OrdinalIgnoreCase))
            {
                var regex_ep = new Regex(@"ep(\d+)");
                if (regex_ep.IsMatch(url))
                {
                    string epId = regex_ep.Match(path).Value.Replace("ep", "");
                    return new UriResult(Enums.UriType.Bangumi, epId);
                }
            }
            else if (path.Contains("/cv", StringComparison.OrdinalIgnoreCase))
            {
                var regex_cv = new Regex(@"cv(\d+)");
                if (regex_cv.IsMatch(url))
                {
                    string cId = regex_cv.Match(path).Value.Replace("cv", "");
                    return new UriResult(Enums.UriType.Document, cId);
                }
            }
            else if (path.Contains("video/", StringComparison.OrdinalIgnoreCase))
            {
                var regex_av = new Regex(@"av(\d+)");
                var regex_bv = new Regex(@"BV(.*)");

                if (regex_av.IsMatch(path))
                {
                    string aid = regex_av.Match(path).Value.Replace("av", "");
                    return new UriResult(Enums.UriType.VideoA, aid);
                }
                else if (regex_bv.IsMatch(path))
                {
                    string bid = regex_bv.Match(path).Value;
                    return new UriResult(Enums.UriType.VideoB, bid);
                }
            }
            return new UriResult(Enums.UriType.Web, url);
        }
    }
}
