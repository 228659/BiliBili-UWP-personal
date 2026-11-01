using BiliBili_Lib.Models.BiliBili;
using BiliBili_Lib.Models.BiliBili.Video;
using BiliBili_Lib.Models.Others;
using BiliBili_Lib.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Streaming.Adaptive;
using Windows.Web.Http;

namespace BiliBili_Lib.Service
{
    public class VideoService
    {
        private static readonly HttpClient _dashHttpClient = CreateDashHttpClient();

        private static HttpClient CreateDashHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Referer = new Uri("https://www.bilibili.com");
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/69.0.3497.100 Safari/537.36");
            return client;
        }

        public VideoService()
        {

        }
        /// <summary>
        /// 加载默认的子分区视频内容
        /// </summary>
        /// <param name="rid">分区ID</param>
        /// <param name="ctime">偏移值</param>
        /// <returns></returns>
        public async Task<Tuple<long, List<RegionVideo>>> GetSubRegionDefaultAsync(int rid, long ctime = 0)
        {
            var param = new Dictionary<string, string>();
            param.Add("rid", rid.ToString());
            param.Add("pull", "0");
            string api = "";
            if (ctime == 0)
                api = Api.REGION_DYNAMIC_CHILD_INIT;
            else
            {
                api = Api.REGION_DYNAMIC_CHILD_REFRESH;
                param.Add("ctime", ctime.ToString());
            }
            string url = BiliTool.UrlContact(api, param, true);
            string content = await BiliTool.GetTextFromWebAsync(url);
            if (!string.IsNullOrEmpty(content))
            {
                var jobj = JObject.Parse(content);
                var list = new List<RegionVideo>();
                if (jobj.ContainsKey("recommend"))
                {
                    var rec = JsonConvert.DeserializeObject<List<RegionVideo>>(jobj["recommend"].ToString());
                    rec.ForEach(p => list.Add(p));
                }
                if (jobj.ContainsKey("new"))
                {
                    var news = JsonConvert.DeserializeObject<List<RegionVideo>>(jobj["new"].ToString());
                    news.ForEach(p => list.Add(p));
                }
                var result = new Tuple<long, List<RegionVideo>>(Convert.ToInt64(jobj["cbottom"]), list);
                return result;
            }
            return null;
        }
        /// <summary>
        /// 加载排序后的分区子内容
        /// </summary>
        /// <param name="rid">分区ID</param>
        /// <param name="order">排序方式</param>
        /// <param name="pn">页码</param>
        /// <returns></returns>
        public async Task<List<RegionVideo>> GetSubRegionSortVideoAsync(int rid, string order, int pn = 1)
        {
            var param = new Dictionary<string, string>();
            param.Add("rid", rid.ToString());
            param.Add("pn", pn.ToString());
            param.Add("ps", "30");
            param.Add("order", order);
            string url = BiliTool.UrlContact(Api.REGION_DYNAMIC_CHILD_SORT, param, true);
            var data = await BiliTool.ConvertEntityFromWebAsync<List<RegionVideo>>(url);
            return data;
        }
        /// <summary>
        /// 旧分区ID与新排行榜频道ID的映射，网页排行榜已改用新ID，旧ID返回的是冻结数据
        /// </summary>
        private static readonly Dictionary<int, int> WebRankRidMap = new Dictionary<int, int>()
        {
            { 0, 0 },{ 1, 1005 },{ 3, 1003 },{ 4, 1008 },{ 5, 1002 },
            { 36, 1010 },{ 119, 1007 },{ 129, 1004 },{ 155, 1014 },
            { 181, 1001 },{ 188, 1012 },{ 211, 1020 },{ 217, 1024 },
            { 223, 1013 },{ 234, 1018 }
        };
        /// <summary>
        /// 获取分区排行榜数据
        /// </summary>
        /// <param name="rid">分区ID</param>
        /// <returns></returns>
        public async Task<List<WebVideo>> GetRegionRankAsync(int rid)
        {
            int webRid;
            if (!WebRankRidMap.TryGetValue(rid, out webRid))
            {
                var oldParam = new Dictionary<string, string>();
                oldParam.Add("rid", rid.ToString());
                oldParam.Add("day", "3");
                string oldUrl = BiliTool.UrlContact(Api.REGION_PART_RANK, oldParam, true);
                return await BiliTool.ConvertEntityFromWebAsync<List<WebVideo>>(oldUrl, "data");
            }
            var param = new Dictionary<string, string>();
            param.Add("rid", webRid.ToString());
            param.Add("type", "all");
            string url = BiliTool.UrlContact(Api.REGION_RANK_V2, param, true);
            string json = await BiliTool.GetTextFromWebAsync(url, true);
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                var jobj = JObject.Parse(json);
                if (jobj.Value<int?>("code") != 0)
                {
                    BiliTool._logger.Warn($"排行榜请求失败：rid={webRid}，{jobj.Value<string>("message")}");
                    return null;
                }
                var list = jobj["data"]?["list"] as JArray;
                if (list == null)
                    return null;
                var result = new List<WebVideo>();
                foreach (var video in list)
                {
                    var stat = video["stat"];
                    var owner = video["owner"];
                    result.Add(new WebVideo()
                    {
                        aid = video.Value<string>("aid"),
                        bvid = video.Value<string>("bvid"),
                        typename = video.Value<string>("tname"),
                        title = video.Value<string>("title"),
                        pic = video.Value<string>("pic"),
                        description = video.Value<string>("desc"),
                        play = stat?.Value<long?>("view") ?? 0,
                        video_review = stat?.Value<int?>("danmaku") ?? 0,
                        review = stat?.Value<int?>("reply") ?? 0,
                        favorites = stat?.Value<int?>("favorite") ?? 0,
                        coins = stat?.Value<int?>("coin") ?? 0,
                        mid = owner?.Value<long?>("mid") ?? 0,
                        author = owner?.Value<string>("name"),
                        duration = FormatRankDuration(video.Value<int?>("duration") ?? 0),
                        pts = video.Value<int?>("score") ?? 0
                    });
                }
                return result;
            }
            catch (Exception ex)
            {
                BiliTool._logger.Error($"排行榜数据解析失败：rid={webRid}", ex);
                return null;
            }
        }
        private static string FormatRankDuration(int seconds)
        {
            if (seconds <= 0)
                return string.Empty;
            int h = seconds / 3600;
            int m = seconds % 3600 / 60;
            int s = seconds % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }
        /// <summary>
        /// 加载默认的分区内容
        /// </summary>
        /// <param name="rid">分区ID</param>
        /// <param name="ctime">偏移值</param>
        /// <returns></returns>
        public async Task<Tuple<List<RegionBanner>, long, List<RegionVideo>>> GetRegionSquareAsync(int rid, long ctime = 0)
        {
            var param = new Dictionary<string, string>();
            param.Add("rid", rid.ToString());
            param.Add("pull", "0");
            string api = "";
            if (ctime == 0)
                api = Api.REGION_DYNAMIC_INIT;
            else
            {
                api = Api.REGION_DYNAMIC_REFRESH;
                param.Add("ctime", ctime.ToString());
            }
            string url = BiliTool.UrlContact(api, param, true);
            string content = await BiliTool.GetTextFromWebAsync(url);
            if (!string.IsNullOrEmpty(content))
            {
                var jobj = JObject.Parse(content);
                var list = new List<RegionVideo>();
                List<RegionBanner> banner = new List<RegionBanner>();
                if (jobj.ContainsKey("banner"))
                {
                    var ban = JsonConvert.DeserializeObject<List<RegionBanner>>(jobj["banner"]["top"].ToString());
                    ban.ForEach(p => banner.Add(p));
                }
                if (jobj.ContainsKey("recommend"))
                {
                    var rec = JsonConvert.DeserializeObject<List<RegionVideo>>(jobj["recommend"].ToString());
                    rec.ForEach(p => list.Add(p));
                }
                if (jobj.ContainsKey("new"))
                {
                    var news = JsonConvert.DeserializeObject<List<RegionVideo>>(jobj["new"].ToString());
                    news.ForEach(p => list.Add(p));
                }
                var result = new Tuple<List<RegionBanner>, long, List<RegionVideo>>(banner, Convert.ToInt64(jobj["cbottom"]), list);
                return result;
            }
            return null;
        }

        /// <summary>
        /// 获取视频分P
        /// </summary>
        /// <param name="aid">视频avid</param>
        /// <returns></returns>
        public async Task<List<VideoPart>> GetVideoPartsAsync(long aid)
        {
            var param = new Dictionary<string, string>();
            param.Add("aid", aid.ToString());
            string url = BiliTool.UrlContact(Api.VIDEO_PART, param, true);
            var parts = await BiliTool.ConvertEntityFromWebAsync<List<VideoPart>>(url);
            return parts;
        }
        /// <summary>
        /// 获取视频详细信息（包括分P）
        /// </summary>
        /// <param name="aid">视频avid</param>
        /// <returns></returns>
        public async Task<VideoDetail> GetVideoDetailAsync(long aid,string fromSign="",string bvId="")
        {
            var param = new Dictionary<string, string>();
            if (aid > 0)
                param.Add("aid", aid.ToString());
            else
                param.Add("bvid", bvId.ToString());
            param.Add("from_spmid", fromSign);
            string url = BiliTool.UrlContact(Api.VIDEO_DETAIL_INFO, param, true);
            var data = await BiliTool.ConvertEntityFromWebAsync<VideoDetail>(url,needReferer:true);
            return data;
        }
        /// <summary>
        /// 获取视频简易信息
        /// </summary>
        /// <param name="aid">视频avid</param>
        /// <returns></returns>
        public async Task<VideoSlim> GetVideoSlimAsync(long aid)
        {
            var param = new Dictionary<string, string>();
            param.Add("aid", aid.ToString());
            string url = BiliTool.UrlContact(Api.VIDEO_SLIM_INFO, param, true);
            var data = await BiliTool.ConvertEntityFromWebAsync<VideoSlim>(url);
            return data;
        }
        /// <summary>
        /// 获取视频相关
        /// </summary>
        /// <param name="aid">视频avid</param>
        /// <returns></returns>
        public async Task<List<VideoDetail>> GetVideoRelatedAsync(long aid)
        {
            var param = new Dictionary<string, string>();
            param.Add("aid", aid.ToString());
            string url = BiliTool.UrlContact(Api.VIDEO_RELATED, param, true);
            var data = await BiliTool.ConvertEntityFromWebAsync<List<VideoDetail>>(url);
            return data;
        }

        /// <summary>
        /// 获取视频播放信息
        /// </summary>
        /// <param name="aid">视频avid</param>
        /// <param name="cid">视频分Pid</param>
        /// <param name="qn">分辨率ID</param>
        /// <returns></returns>
        public async Task<VideoPlayBase> GetVideoPlayAsync(long aid, long cid, int qn = 64)
        {
            var param = new Dictionary<string, string>();
            param.Add("avid", aid.ToString());
            param.Add("cid", cid.ToString());
            param.Add("qn", qn.ToString());
            param.Add("mid", BiliTool.mid);
            param.Add("fnver", "0");
            param.Add("fnval", "16");
            param.Add("fourk", "1");
            string url = BiliTool.UrlContact(Api.VIDEO_PLAY, param, true,true);
            // 网页播放接口按 Cookie 识别登录，不带头部 Cookie 只会返回 480P
            var data = await BiliTool.GetTextFromWebAsync(url, useCookie: true);
            if (!string.IsNullOrEmpty(data))
            {
                var jobj = JObject.Parse(data);
                if (jobj.ContainsKey("dash"))
                {
                    var play = JsonConvert.DeserializeObject<VideoPlayDash>(data);
                    if (play != null)
                    {
                        var streams = play.dash?.video == null ? string.Empty : string.Join(",", play.dash.video.Select(p => $"{p.id}/{p.codecid}"));
                        var accepted = play.accept_quality == null ? string.Empty : string.Join("/", play.accept_quality);
                        await BiliTool.WriteNetworkLogAsync($"播放地址：请求 qn={qn}，返回 quality={play.quality}，可接受 {accepted}，DASH 流 {streams}，令牌 {(string.IsNullOrEmpty(BiliTool._accessToken) ? "无" : "有")}，SESSDATA {(BiliTool.GetLoginCookieNames().Contains("SESSDATA") ? "有" : "无")}");
                    }
                    return play;
                }
                else if (jobj.ContainsKey("durl"))
                {
                    return JsonConvert.DeserializeObject<VideoPlayFlv>(data);
                }
            }
            return null;
        }

        /// <summary>
        /// 根据分片条目创建媒体来源
        /// </summary>
        /// <param name="video">视频</param>
        /// <param name="audio">音频</param>
        /// <returns></returns>
        public async Task<MediaSource> CreateMediaSourceAsync(VideoDashItem video, VideoDashItem audio)
        {
            try
            {
                var httpClient = _dashHttpClient;
                var mpdStr = $@"<MPD xmlns=""urn:mpeg:DASH:schema:MPD:2011""  profiles=""urn:mpeg:dash:profile:isoff-on-demand:2011"" type=""static"">
                                  <Period  start=""PT0S"">
                                    <AdaptationSet>
                                      <ContentComponent contentType=""video"" id=""1"" />
                                      <Representation bandwidth=""{video.bandwidth}"" codecs=""{video.codecs}"" height=""{video.height}"" id=""{video.id}"" mimeType=""{video.mimeType}"" width=""{video.width}"">
                                        <BaseURL></BaseURL>
                                        <SegmentBase indexRange=""{video.segment_base.index_range}"">
                                          <Initialization range=""{video.segment_base.initialization}"" />
                                        </SegmentBase>
                                      </Representation>
                                    </AdaptationSet>
                                    {{audio}}
                                  </Period>
                                </MPD>
                                ";
                if (audio == null)
                    mpdStr = mpdStr.Replace("{audio}", "");
                else
                    mpdStr = mpdStr.Replace("{audio}", $@"<AdaptationSet>
                                      <ContentComponent contentType=""audio"" id=""2"" />
                                      <Representation bandwidth=""{audio.bandwidth}"" codecs=""{audio.codecs}"" id=""{audio.id}"" mimeType=""{audio.mimeType}"" >
                                        <BaseURL></BaseURL>
                                        <SegmentBase indexRange=""{audio.segment_base.index_range}"">
                                          <Initialization range=""{audio.segment_base.initialization}"" />
                                        </SegmentBase>
                                      </Representation>
                                    </AdaptationSet>");
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(mpdStr)).AsInputStream();
                var soure = await AdaptiveMediaSource.CreateFromStreamAsync(stream, new Uri(video.baseUrl), "application/dash+xml", httpClient);
                var s = soure.Status;
                soure.MediaSource.DownloadRequested += (sender, args) =>
                {
                    if (args.ResourceContentType == "audio/mp4" && audio != null)
                    {
                        args.Result.ResourceUri = new Uri(audio.baseUrl);
                    }
                };
                return MediaSource.CreateFromAdaptiveMediaSource(soure.MediaSource);
            }
            catch (Exception)
            {
                return null;
            }

        }

        /// <summary>
        /// 检查视频状态（是否投币，点赞，收藏等）
        /// </summary>
        /// <param name="aid">视频ID</param>
        /// <param name="type">检查类型</param>
        /// <returns></returns>
        public async Task<bool> CheckVideoStatusAsync(long aid, string type = "like")
        {
            var param = new Dictionary<string, string>();
            param.Add("aid", aid.ToString());
            string url = "";
            switch (type)
            {
                case "like":
                    url = Api.VIDEO_IS_LIKE;
                    break;
                case "coin":
                    url = Api.VIDEO_IS_COIN;
                    break;
                case "favorite":
                    url = Api.VIDEO_IS_FAVORITE;
                    break;
            }
            url = BiliTool.UrlContact(url, param, true);
            var content = await BiliTool.GetTextFromWebAsync(url);
            if (type == "like")
                return content == "1";
            else
            {
                var jobj = JObject.Parse(content);
                if (type == "coin")
                    return jobj["multiply"].ToString() == "1";
                else if (type == "favorite")
                    return Convert.ToBoolean(jobj["favoured"]);
            }
            return false;
        }

        /// <summary>
        /// 视频点赞/取消点赞
        /// </summary>
        /// <param name="aid">视频ID</param>
        /// <param name="isLike">是否点赞</param>
        /// <returns></returns>
        public async Task<bool> LikeVideoAsync(long aid, bool isLike)
        {
            string is_like = isLike ? "0" : "1";
            var param = new Dictionary<string, string>();
            param.Add("aid", aid.ToString());
            param.Add("like", is_like);
            var result = await BiliTool.PostContentToWebAsync(Api.VIDEO_LIKE, BiliTool.UrlContact("", param, true));
            if (!string.IsNullOrEmpty(result))
            {
                var jobj = JObject.Parse(result);
                return jobj["code"].ToString() == "0";
            }
            return false;
        }

        /// <summary>
        /// 视频投币
        /// </summary>
        /// <param name="aid">视频ID</param>
        /// <param name="coin">投币数量</param>
        /// <param name="isLike">是否同时点赞</param>
        /// <returns></returns>
        public async Task<bool> GiveCoinToVideoAsync(long aid, int coin, bool isLike)
        {
            string is_like = isLike ? "1" : "0";
            var param = new Dictionary<string, string>();
            param.Add("aid", aid.ToString());
            param.Add("select_like", is_like);
            param.Add("multiply", coin.ToString());
            var result = await BiliTool.PostContentToWebAsync(Api.VIDEO_COIN, BiliTool.UrlContact("", param, true));
            if (!string.IsNullOrEmpty(result))
            {
                var jobj = JObject.Parse(result);
                return jobj["code"].ToString() == "0";
            }
            return false;
        }
        /// <summary>
        /// 获取收藏夹
        /// </summary>
        /// <param name="aid">视频ID</param>
        /// <param name="uid">用户ID</param>
        /// <returns></returns>
        public async Task<List<FavoriteItem>> GetFavoritesAsync(long aid, long uid)
        {
            var param = new Dictionary<string, string>();
            param.Add("up_mid", uid.ToString());
            param.Add("rid", aid.ToString());
            param.Add("ps", "100");
            param.Add("pn", "1");
            param.Add("type", "2");
            string url = BiliTool.UrlContact(Api.VIDEO_FAVORITE_LIST, param, true);
            return await BiliTool.ConvertEntityFromWebAsync<List<FavoriteItem>>(url, "data.list");
        }
        /// <summary>
        /// 操作收藏夹
        /// </summary>
        /// <param name="aid">视频ID</param>
        /// <param name="addIds">添加的收藏夹ID列表</param>
        /// <param name="delIds">移出的收藏夹ID列表</param>
        /// <returns></returns>
        public async Task<bool> AddVideoToFavoriteAsync(long aid, List<string> addIds, List<string> delIds)
        {
            if (addIds.Count == 0 && delIds.Count == 0)
                return false;
            var param = new Dictionary<string, string>();
            param.Add("rid", aid.ToString());
            param.Add("type", "2");
            if (addIds.Count > 0)
                param.Add("add_media_ids", string.Join(',', addIds));
            else if (delIds.Count > 0)
                param.Add("del_media_ids", string.Join(',', delIds));
            var result = await BiliTool.PostContentToWebAsync(Api.VIDEO_FAVORITE, BiliTool.UrlContact("", param, true));
            if (!string.IsNullOrEmpty(result))
            {
                var jobj = JObject.Parse(result);
                return jobj["code"].ToString() == "0";
            }
            return false;
        }
        /// <summary>
        /// 一键三连
        /// </summary>
        /// <param name="aid">视频ID</param>
        /// <returns></returns>
        public async Task<bool> TripleVideoAsync(long aid)
        {
            var param = new Dictionary<string, string>();
            param.Add("aid", aid.ToString());
            var result = await BiliTool.PostContentToWebAsync(Api.VIDEO_TRIPLE, BiliTool.UrlContact("", param, true));
            if (!string.IsNullOrEmpty(result))
            {
                var jobj = JObject.Parse(result);
                return jobj["code"].ToString() == "0";
            }
            return false;
        }
        /// <summary>
        /// 添加观看视频的历史记录
        /// </summary>
        /// <param name="aid">视频ID</param>
        /// <returns></returns>
        public async Task AddVideoHistoryAsync(long aid, long cid, int seconds = 0)
        {
            var param = new Dictionary<string, string>();
            param.Add("aid", aid.ToString());
            param.Add("cid", cid.ToString());
            param.Add("progress", seconds.ToString());
            param.Add("type", "3");
            var data = BiliTool.UrlContact("", param, true);
            await BiliTool.PostContentToWebAsync(Api.VIDEO_ADD_WATCH, data);
        }

        /// <summary>
        /// 获取互动视频的节点信息
        /// </summary>
        /// <param name="aid">视频ID</param>
        /// <param name="graphVersion">标识值</param>
        /// <param name="edgeId">选项ID</param>
        /// <returns></returns>
        public async Task<InteractionVideo> GetInteractionVideoAsync(long aid, int graphVersion, int edgeId = 0)
        {
            var param = new Dictionary<string, string>();
            param.Add("aid", aid.ToString());
            param.Add("graph_version", graphVersion.ToString());
            param.Add("edge_id", edgeId.ToString());
            string url = BiliTool.UrlContact(Api.VIDEO_INTERACTION_EDGE, param, true);
            var data = await BiliTool.ConvertEntityFromWebAsync<InteractionVideo>(url);
            return data;
        }

        /// <summary>
        /// 发送弹幕
        /// </summary>
        /// <param name="message">弹幕信息</param>
        /// <param name="aid">视频ID</param>
        /// <param name="cid">弹幕块ID</param>
        /// <param name="progress">进度</param>
        /// <param name="color">颜色（已处理）</param>
        /// <param name="fontSize">文本大小</param>
        /// <param name="mode">模式</param>
        /// <returns></returns>
        public async Task<bool> SendDanmakuAsync(string message,long aid, long cid, double progress,string color,string fontSize,string mode)
        {
            var param = new Dictionary<string, string>();
            param.Add("aid", aid.ToString());
            param.Add("msg", Uri.EscapeDataString(message));
            param.Add("oid", cid.ToString());
            param.Add("color", color);
            param.Add("fontSize", fontSize);
            param.Add("mode", mode);
            param.Add("pool", "0");
            param.Add("plat", "3");
            param.Add("type", "1");
            param.Add("progress", Math.Round(progress).ToString());
            var data = BiliTool.UrlContact("", param, true);
            var response = await BiliTool.PostContentToWebAsync(Api.VIDEO_SEND_DANMAKU, data);
            if (!string.IsNullOrEmpty(response))
            {
                var jobj = JObject.Parse(response);
                return jobj["code"].ToString() == "0";
            }
            return false;
        }

        /// <summary>
        /// 不喜欢某视频
        /// </summary>
        /// <param name="arg">参数</param>
        /// <param name="reason_id">原因ID</param>
        /// <param name="go">类型</param>
        /// <param name="isFeedback">标识是反馈还是不感兴趣</param>
        /// <returns></returns>
        public async Task<bool> DislikeRecommendVideoAsync(Args arg,int reason_id,string go,bool isFeedback=false)
        {
            var param = new Dictionary<string, string>();
            param.Add("id", arg.aid.ToString());
            param.Add("rid", arg.rid.ToString());
            if(isFeedback)
                param.Add("feedback_id", reason_id.ToString());
            else
                param.Add("reason_id", reason_id.ToString());
            param.Add("goto", go);
            param.Add("mid", arg.up_id.ToString());
            param.Add("tag_id", arg.tid.ToString());
            var url = BiliTool.UrlContact(Api.VIDEO_RECOMMEND_DISLIKE, param, true);
            var response = await BiliTool.GetTextFromWebAsync(url, true);
            if (!string.IsNullOrEmpty(response))
            {
                var jobj = JObject.Parse(response);
                return jobj["code"].ToString() == "0";
            }
            return false;
        }

        /// <summary>
        /// 获取字幕文件索引
        /// </summary>
        /// <param name="aid">视频ID</param>
        /// <param name="cid">分PID</param>
        /// <returns></returns>
        public async Task<List<SubtitleIndexItem>> GetVideoSubtitleIndexAsync(long aid,long cid)
        {
            var param = new Dictionary<string, string>();
            param.Add("id", $"cid:{cid}");
            param.Add("aid", aid.ToString());
            string url = BiliTool.UrlContact(Api.VIDEO_SUBTITLE, param, true);
            string result = await BiliTool.GetTextFromWebAsync(url, true,needReferer:true);
            if(!string.IsNullOrEmpty(result) && result.Contains("subtitle"))
            {
                var json = Regex.Match(result, @"<subtitle>(.*?)</subtitle>").Groups[1].Value;
                var index = JsonConvert.DeserializeObject<VideoSubtitleIndex>(json);
                return index.subtitles;
            }
            return null;
        }

        /// <summary>
        /// 获取字幕数据
        /// </summary>
        /// <param name="url">网址</param>
        /// <returns></returns>
        public async Task<VideoSubtitle> GetSubtitlesAsync(string url)
        {
            if (!url.StartsWith("http"))
                url = "https:" + url;
            var response = await BiliTool.ConvertEntityFromWebAsync<VideoSubtitle>(url, "");
            return response;
        }
        /// <summary>
        /// 转发视频
        /// </summary>
        /// <param name="content">转发内容</param>
        /// <param name="videoId">视频ID</param>
        /// <param name="atIds">At的人</param>
        /// <returns></returns>
        public async Task<bool> RepostVideoAsync(string content, long videoId, List<RepostLocation> atIds)
        {
            var param = new Dictionary<string, string>();
            param.Add("content", Uri.EscapeDataString(content));
            param.Add("at_uids", string.Join(',', atIds.Select(p=>p.data)));
            param.Add("ctrl", Uri.EscapeDataString(JsonConvert.SerializeObject(atIds)));
            param.Add("share_uid", BiliTool.mid.ToString());
            param.Add("rid", videoId.ToString());
            param.Add("type", "8");
            param.Add("repost_code", "20000");
            param.Add("sync_comment", "0");
            param.Add("sketch", "");
            param.Add("uid", "0");
            param.Add("share_info", "");
            param.Add("extension", Uri.EscapeDataString("{\"emoji_type\":1}"));
            param.Add("statistics", Uri.EscapeDataString("{\"appId\":1,\"version\":\"5.56.1\",\"abtest\":\"\",\"platform\":1}"));
            var req = BiliTool.UrlContact("", param, true);
            var response = await BiliTool.PostContentToWebAsync(Api.VIDEO_REPOST, req);
            if (!string.IsNullOrEmpty(response))
            {
                var jobj = JObject.Parse(response);
                return jobj["code"].ToString() == "0";
            }
            return false;
        }
    }
}
