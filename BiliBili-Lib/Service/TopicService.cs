using BiliBili_Lib.Models.BiliBili;
using BiliBili_Lib.Models.Others;
using BiliBili_Lib.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BiliBili_Lib.Service
{
    public class TopicService
    {
        /// <summary>
        /// 获取话题动态
        /// </summary>
        /// <param name="topicId">话题ID</param>
        /// <param name="topicName">话题名</param>
        /// <param name="offset">偏移值，初次不需要，每次请求会返回下一次请求的偏移值</param>
        /// <returns>Item1:下次偏移值;Item2:视频列表</returns>
        public async Task<Tuple<string, List<Topic>>> GetTopicAsync(int topicId, string topicName, string offset = "")
        {
            var param = new Dictionary<string, string>();
            param.Add("topic_id", topicId.ToString());
            param.Add("topic_name", Uri.EscapeDataString(topicName));
            //param.Add("video_meta", "qn:32,fnval:16,fnver:0,fourk:1");
            if (!string.IsNullOrEmpty(offset))
                param.Add("offset", offset);
            string url = BiliTool.UrlContact(Api.TOPIC_COMPLEX, param, true);
            var data = await BiliTool.GetTextFromWebAsync(url);
            if (!string.IsNullOrEmpty(data))
            {
                var jobj = JObject.Parse(data);
                string nextOffset = jobj["offset"].ToString();
                var topics = JsonConvert.DeserializeObject<List<Topic>>(jobj["cards"].ToString());
                topics.RemoveAll(p => p == null || p.card == null || p.card.Length < 10 || p.desc.status != 1);
                return new Tuple<string, List<Topic>>(nextOffset, topics);
            }
            return null;
        }
        /// <summary>
        /// 获取新动态
        /// </summary>
        /// <returns>动态响应</returns>
        public async Task<NewDynamicResponse> GetNewDynamicAsync(string lastSeemId="0")
        {
            var param = new Dictionary<string, string>();
            param.Add("cold_start", "1");
            param.Add("qn", "32");
            param.Add("rsp_type", "2");
            param.Add("type_list", "268435455");
            param.Add("uid", BiliTool.mid);
            if (!string.IsNullOrEmpty(lastSeemId)&& lastSeemId!="0")
            {
                param.Add("update_num_dy_id", lastSeemId);
            }
            string url = BiliTool.UrlContact(Api.DYNAMIC_NEW, param, true);
            var data = await BiliTool.ConvertEntityFromWebAsync<NewDynamicResponse>(url);
            if (data == null || data.cards == null)
                return null;
            int total = data.cards.Count;
            string typeSummary = string.Join(",", data.cards.Where(p => p?.desc != null)
                .GroupBy(p => p.desc.type).Select(g => g.Key + "x" + g.Count()));
            data.cards.RemoveAll(p => p == null || string.IsNullOrEmpty(p.card) || p.desc == null);
            BiliTool._logger.Info($"动态列表：原始 {total} 条（{typeSummary}），可用 {data.cards.Count} 条");
            if (!string.IsNullOrEmpty(data.max_dynamic_id))
                AppTool.WriteLocalSetting(Enums.Settings.LastSeemDynamicId, data.max_dynamic_id);
            return data;
        }
        /// <summary>
        /// 获取历史动态
        /// </summary>
        /// <param name="offset">偏移值，初次不需要，每次请求会返回下一次请求的偏移值</param>
        /// <returns>Item1:下次偏移值;Item2:动态列表</returns>
        public async Task<Tuple<string, List<Topic>>> GetHistoryDynamicAsync(string offset)
        {
            var param = new Dictionary<string, string>();
            param.Add("qn", "32");
            param.Add("uid", BiliTool.mid);
            param.Add("offset_dynamic_id", offset);
            param.Add("rsp_type", "2");
            param.Add("type_list", "268435455");
            string url = BiliTool.UrlContact(Api.DYNAMIC_HISTORY, param, true);
            var data = await BiliTool.GetTextFromWebAsync(url);
            if (!string.IsNullOrEmpty(data))
            {
                try
                {
                    var jobj = JObject.Parse(data);
                    int code = jobj.Value<int?>("code") ?? 0;
                    if (code != 0)
                    {
                        BiliTool._logger.Warn($"历史动态请求失败：{jobj.Value<string>("message")}");
                        return null;
                    }
                    var body = jobj["data"] as JObject ?? jobj;
                    string nextOffset = (body["next_offset"] ?? jobj["next_offset"])?.ToString();
                    var cardsToken = body["cards"] as JArray ?? jobj["cards"] as JArray;
                    if (cardsToken == null)
                    {
                        BiliTool._logger.Warn("历史动态响应缺少 cards 字段");
                        return null;
                    }
                    var topics = cardsToken.ToObject<List<Topic>>();
                    if (topics == null)
                        return null;
                    int total = topics.Count;
                    topics.RemoveAll(p => p == null || string.IsNullOrEmpty(p.card) || p.desc == null);
                    BiliTool._logger.Info($"历史动态：原始 {total} 条，可用 {topics.Count} 条，next_offset={nextOffset}");
                    return new Tuple<string, List<Topic>>(nextOffset, topics);
                }
                catch (Exception ex)
                {
                    BiliTool._logger.Error("历史动态解析失败", ex);
                }
            }
            return null;
        }
        /// <summary>
        /// 获取新版关注动态流（httpOnly=false 时需要 Web 登录 Cookie）
        /// </summary>
        /// <param name="offset">首页传空，后续传上一次返回的 offset</param>
        /// <returns>Item1: 下一页 offset; Item2: 动态列表</returns>
        public async Task<Tuple<string, List<Topic>>> GetWebDynamicFeedAsync(string offset)
        {
            try
            {
                var cookieNames = BiliTool.GetLoginCookieNames();
                if (!cookieNames.Contains("SESSDATA"))
                {
                    BiliTool._logger.Warn($"新版动态：Cookie 缺少 SESSDATA（现有 {string.Join(",", cookieNames)}）");
                    return null;
                }
                await BiliTool.EnsureBuvidCookieAsync();
                var param = new Dictionary<string, string>
                {
                    { "offset", offset ?? "" },
                    { "timezone_offset", "-480" },
                    { "type", "all" },
                    { "features", "itemOpusStyle,opusBigCover,onlyfansVote,decorationCard,onlyfansAssetsV2,ugcDelete,onlyfansQaCard" }
                };
                string query = await BiliTool.CreateWbiQueryAsync(param);
                if (string.IsNullOrEmpty(query))
                    return null;
                string url = $"{Api.WEB_DYNAMIC_FEED}?{query}";
                string content = await BiliTool.GetTextFromWebAsync(url, true, useCookie: true);
                if (string.IsNullOrEmpty(content))
                    return null;
                var jobj = JObject.Parse(content);
                int code = jobj.Value<int?>("code") ?? -1;
                if (code != 0)
                {
                    BiliTool._logger.Warn($"新版动态请求失败：code={code}，message={jobj.Value<string>("message")}");
                    return null;
                }
                var body = jobj["data"] as JObject;
                var items = body?["items"] as JArray;
                if (items == null)
                {
                    BiliTool._logger.Warn("新版动态响应缺少 items");
                    return null;
                }
                var topics = new List<Topic>();
                foreach (var token in items)
                {
                    var topic = ConvertWebDynamicItem(token as JObject);
                    if (topic != null)
                        topics.Add(topic);
                }
                bool hasMore = body.Value<bool?>("has_more") ?? false;
                string nextOffset = hasMore ? (body.Value<string>("offset") ?? "") : "";
                BiliTool._logger.Info($"新版动态：本页 {items.Count} 条，转换 {topics.Count} 条，has_more={hasMore}");
                return new Tuple<string, List<Topic>>(nextOffset, topics);
            }
            catch (Exception ex)
            {
                BiliTool._logger.Error("新版动态解析失败", ex);
                return null;
            }
        }
        private static long ToLong(JToken token)
        {
            if (token == null)
                return 0;
            long result;
            return long.TryParse(token.ToString(), out result) ? result : 0;
        }
        private static int ToInt(JToken token)
        {
            long result = ToLong(token);
            if (result > int.MaxValue)
                return int.MaxValue;
            if (result < int.MinValue)
                return int.MinValue;
            return (int)result;
        }
        private static int ParseDurationText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            int total = 0;
            foreach (var part in text.Split(':'))
            {
                int value;
                if (!int.TryParse(part, out value))
                    return 0;
                total = total * 60 + value;
            }
            return total;
        }
        private static string[] GetStringArray(JToken token)
        {
            var array = token as JArray;
            if (array == null)
                return new string[0];
            return array.Select(p => p.ToString()).Where(p => !string.IsNullOrEmpty(p)).ToArray();
        }
        private Topic ConvertWebDynamicItem(JObject item, int depth = 0)
        {
            if (item == null || depth > 2)
                return null;
            string idStr = item.Value<string>("id_str") ?? item.SelectToken("basic.dyn_id_str")?.ToString();
            if (string.IsNullOrEmpty(idStr))
                return null;
            string itemType = item.Value<string>("type") ?? "";
            var modules = item["modules"] as JObject;
            var author = modules?["module_author"] as JObject;
            var dynamicModule = modules?["module_dynamic"] as JObject;
            var major = dynamicModule?["major"] as JObject;
            var opus = major?["opus"] as JObject;
            string text = dynamicModule?.SelectToken("desc.text")?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                text = opus?.Value<string>("title");
            if (string.IsNullOrWhiteSpace(text))
                text = opus?.SelectToken("summary.text")?.ToString();
            text = text?.Trim() ?? "";
            long uid = author?.Value<long?>("mid") ?? 0;
            string uname = author?.Value<string>("name") ?? "";
            string face = author?.Value<string>("face") ?? "";
            long pubTs = author?.Value<long?>("pub_ts") ?? 0;
            int like = modules?.SelectToken("module_stat.like.count")?.Value<int?>() ?? 0;
            int comment = modules?.SelectToken("module_stat.comment.count")?.Value<int?>() ?? 0;
            int forward = modules?.SelectToken("module_stat.forward.count")?.Value<int?>() ?? 0;
            Topic topic = null;
            if (itemType == "DYNAMIC_TYPE_FORWARD" || item["orig"] is JObject)
            {
                var origin = ConvertWebDynamicItem(item["orig"] as JObject, depth + 1);
                string originCard = origin?.card ?? string.Empty;
                int originType = origin?.desc?.type ?? 4;
                var originInfo = origin?.desc?.user_profile?.info;
                if (origin == null)
                {
                    origin = BuildTopic(idStr + "_origin", 4, author, 0, 0, 0,
                        JsonConvert.SerializeObject(new { item = new { uid = uid, content = text, timestamp = pubTs, reply = 0 } }), text);
                    originCard = origin.card;
                    originType = 4;
                }
                var card = new
                {
                    user = new { name = uname, face = face, mid = uid },
                    item = new
                    {
                        rp_id = ToLong(idStr),
                        uid = uid,
                        content = text,
                        ctrl = "",
                        orig_dy_id = ToLong(origin?.desc?.dynamic_id_str),
                        pre_dy_id = 0,
                        timestamp = pubTs,
                        reply = comment,
                        orig_type = originType
                    },
                    origin = originCard,
                    origin_user = new
                    {
                        info = new
                        {
                            uid = originInfo?.uid ?? origin?.desc?.uid ?? 0,
                            uname = originInfo?.uname ?? "",
                            face = originInfo?.face ?? ""
                        }
                    }
                };
                topic = BuildTopic(idStr, 1, author, like, comment, forward, JsonConvert.SerializeObject(card), text);
            }
            else
            {
                var archive = major?["archive"] as JObject;
                var draw = major?["draw"] as JObject;
                var article = major?["article"] as JObject;
                var live = major?["live"] as JObject;
                var music = major?["music"] as JObject;
                var course = major?["courses"] as JObject;
                var pgc = major?["pgc"] as JObject;
                var common = major?["common"] as JObject;
                if (archive != null)
                {
                    var card = new
                    {
                        aid = ToLong(archive["aid"]),
                        attribute = 0,
                        cid = 0,
                        copyright = 0,
                        ctime = pubTs,
                        desc = archive.Value<string>("desc") ?? "",
                        duration = ParseDurationText(archive.Value<string>("duration_text")),
                        dynamic = text,
                        jump_url = $"https://www.bilibili.com/video/{archive.Value<string>("bvid")}",
                        owner = new { mid = uid, name = uname, face = face },
                        pic = archive.Value<string>("cover") ?? "",
                        pubdate = pubTs,
                        stat = new
                        {
                            view = ToLong(archive.SelectToken("stat.play")),
                            danmaku = ToLong(archive.SelectToken("stat.danmaku")),
                            reply = comment,
                            favorite = ToLong(archive.SelectToken("stat.favorite")),
                            coin = ToLong(archive.SelectToken("stat.coin")),
                            share = forward,
                            like = like
                        },
                        title = archive.Value<string>("title") ?? "",
                        videos = 1,
                        redirect_url = ""
                    };
                    topic = BuildTopic(idStr, 8, author, like, comment, forward, JsonConvert.SerializeObject(card), text);
                }
                else if (draw != null || opus != null)
                {
                    var pictureArray = draw?["items"] as JArray ?? opus?["pics"] as JArray;
                    var pictures = new List<object>();
                    if (pictureArray != null)
                    {
                        foreach (var picture in pictureArray)
                        {
                            string src = picture.Value<string>("src") ?? picture.Value<string>("url");
                            if (string.IsNullOrEmpty(src))
                                continue;
                            pictures.Add(new
                            {
                                img_src = src,
                                img_width = picture.Value<double?>("width") ?? 0,
                                img_height = picture.Value<double?>("height") ?? 0,
                                img_size = picture.Value<double?>("size") ?? 0
                            });
                        }
                    }
                    if (pictures.Count == 0)
                    {
                        var textCard = new { item = new { uid = uid, content = text, timestamp = pubTs, reply = comment } };
                        topic = BuildTopic(idStr, 4, author, like, comment, forward, JsonConvert.SerializeObject(textCard), text);
                    }
                    else
                    {
                        var card = new
                        {
                            item = new
                            {
                                id = 0,
                                title = "",
                                description = text,
                                category = "日常",
                                role = new object[0],
                                source = new object[0],
                                pictures = pictures,
                                pictures_count = pictures.Count,
                                upload_time = pubTs,
                                at_control = "",
                                reply = comment,
                                settings = new { copy_forbidden = 0 },
                                is_fav = 0
                            }
                        };
                        topic = BuildTopic(idStr, 2, author, like, comment, forward, JsonConvert.SerializeObject(card), text);
                    }
                }
                else if (article != null)
                {
                    var covers = GetStringArray(article["covers"]);
                    var card = new
                    {
                        id = ToInt(article["id"]),
                        title = article.Value<string>("title") ?? "",
                        summary = article.Value<string>("desc") ?? "",
                        banner_url = covers.FirstOrDefault() ?? "",
                        author = new { mid = uid, name = uname, face = face },
                        image_urls = covers,
                        publish_time = pubTs,
                        ctime = pubTs,
                        stats = new
                        {
                            view = ToLong(article.SelectToken("stat.view")),
                            favorite = ToLong(article.SelectToken("stat.favorite")),
                            like = like,
                            reply = comment,
                            share = forward
                        },
                        dynamic = text,
                        origin_image_urls = covers,
                        is_like = false
                    };
                    topic = BuildTopic(idStr, 64, author, like, comment, forward, JsonConvert.SerializeObject(card), text);
                }
                else if (live != null)
                {
                    var card = new
                    {
                        roomid = ToInt(live["id"]),
                        uid = uid,
                        uname = uname,
                        face = face,
                        cover = live.Value<string>("cover") ?? "",
                        title = live.Value<string>("title") ?? "",
                        live_status = live.Value<int?>("live_state") ?? 0,
                        online = 0,
                        attentions = 0,
                        area_v2_name = ""
                    };
                    topic = BuildTopic(idStr, 4200, author, like, comment, forward, JsonConvert.SerializeObject(card), text);
                }
                else if (music != null)
                {
                    var card = new
                    {
                        id = ToInt(music["id"]),
                        title = music.Value<string>("title") ?? "",
                        cover = music.Value<string>("cover") ?? "",
                        intro = text,
                        replyCnt = comment
                    };
                    topic = BuildTopic(idStr, 256, author, like, comment, forward, JsonConvert.SerializeObject(card), text);
                }
                else if (course != null)
                {
                    var card = new
                    {
                        title = course.Value<string>("title") ?? "",
                        cover = course.Value<string>("cover") ?? "",
                        url = "",
                        new_ep = new { title = text }
                    };
                    topic = BuildTopic(idStr, 4303, author, like, comment, forward, JsonConvert.SerializeObject(card), text);
                }
                else if (pgc != null)
                {
                    string epid = pgc.Value<string>("epid") ?? "";
                    var card = new
                    {
                        rid = ToLong(pgc["epid"]),
                        user = new { uid = uid, uname = uname, face = face },
                        vest = new { uid = uid, content = text, ctrl = "" },
                        sketch = new
                        {
                            title = pgc.Value<string>("title") ?? "",
                            desc_text = text,
                            cover_url = pgc.Value<string>("cover") ?? "",
                            target_url = string.IsNullOrEmpty(epid) ? "https://www.bilibili.com/anime/" : $"https://www.bilibili.com/bangumi/play/ep{epid}",
                            sketch_id = ToLong(pgc["epid"]),
                            biz_type = 0
                        }
                    };
                    topic = BuildTopic(idStr, 2048, author, like, comment, forward, JsonConvert.SerializeObject(card), text);
                }
                else if (common != null)
                {
                    var card = new
                    {
                        rid = ToLong(common["id"]),
                        user = new { uid = uid, uname = uname, face = face },
                        vest = new { uid = uid, content = text, ctrl = "" },
                        sketch = new
                        {
                            title = common.Value<string>("title") ?? "",
                            desc_text = common.Value<string>("desc") ?? text,
                            cover_url = common.Value<string>("cover") ?? "",
                            target_url = common.Value<string>("jump_url") ?? "",
                            sketch_id = ToLong(common["id"]),
                            biz_type = 0
                        }
                    };
                    topic = BuildTopic(idStr, 2048, author, like, comment, forward, JsonConvert.SerializeObject(card), text);
                }
                else
                {
                    var card = new { item = new { uid = uid, content = text, timestamp = pubTs, reply = comment } };
                    topic = BuildTopic(idStr, 4, author, like, comment, forward, JsonConvert.SerializeObject(card), text);
                }
            }
            return topic;
        }
        private Topic BuildTopic(string idStr, int type, JObject author, int like, int comment, int forward, string card, string text)
        {
            long uid = author?.Value<long?>("mid") ?? 0;
            long pubTs = author?.Value<long?>("pub_ts") ?? 0;
            int timestamp = pubTs > int.MaxValue ? int.MaxValue : (int)pubTs;
            return new Topic
            {
                desc = new TopicDescription
                {
                    uid = uid,
                    type = type,
                    rid = idStr,
                    repost = forward,
                    like = like,
                    comment = comment,
                    dynamic_id = idStr,
                    dynamic_id_str = idStr,
                    timestamp = timestamp,
                    status = 1,
                    user_profile = new UserProfile
                    {
                        info = new SlimUserInfo
                        {
                            uid = uid,
                            uname = author?.Value<string>("name") ?? "",
                            face = author?.Value<string>("face") ?? ""
                        }
                    }
                },
                card = card
            };
        }
        /// <summary>
        /// 设置动态点赞状态
        /// </summary>
        /// <param name="isLike">是否点赞</param>
        /// <param name="dynamicId">动态ID</param>
        /// <param name="rid">动态里的参数</param>
        /// <param name="uid">用户ID</param>
        /// <returns></returns>
        public async Task<bool> SetDynamicLikeStatus(bool isLike, string dynamicId, string rid, string uid)
        {
            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(dynamicId) || string.IsNullOrEmpty(rid))
                return false;
            var param = new Dictionary<string, string>();
            param.Add("dynamic_id", dynamicId);
            param.Add("rid", rid);
            param.Add("uid", uid);
            param.Add("up", isLike ? "1" : "2");
            var data = BiliTool.UrlContact("", param, true);
            var response = await BiliTool.PostContentToWebAsync(Api.DYNAMIC_LIKE, data);
            if (!string.IsNullOrEmpty(response))
            {
                var jobj = JObject.Parse(response);
                if (jobj["code"].ToString() == "0")
                    return true;
            }
            return false;
        }
        /// <summary>
        /// 获取用户空间历史动态
        /// </summary>
        /// <param name="uid">要查看的用户ID</param>
        /// <param name="page">页码</param>
        /// <param name="offset_id">偏移值，初次不需要，每次请求会返回下一次请求的偏移值</param>
        /// <returns>Item1:下次偏移值;Item2:动态列表</returns>
        public async Task<Tuple<string, List<Topic>>> GetUserSpaceDynamicAsync(long uid, int page = 1, string offset_id = "0")
        {
            var param = new Dictionary<string, string>();
            param.Add("host_uid", uid.ToString());
            param.Add("qn", "32");
            if (!string.IsNullOrEmpty(BiliTool.mid))
                param.Add("visitor_uid", BiliTool.mid);
            param.Add("offset_dynamic_id", offset_id);
            param.Add("page", page.ToString());
            string url = BiliTool.UrlContact(Api.DYNAMIC_USER_HISTORY, param, true);
            var data = await BiliTool.GetTextFromWebAsync(url);
            if (!string.IsNullOrEmpty(data))
            {
                try
                {
                    var jobj = JObject.Parse(data);
                    string nextOffset = jobj["next_offset"].ToString();
                    var topics = JsonConvert.DeserializeObject<List<Topic>>(jobj["cards"].ToString());
                    if (topics == null)
                        return null;
                    topics.RemoveAll(p => p == null || p.card == null || p.card.Length < 10 || p.desc == null || p.desc.status != 1);
                    return new Tuple<string, List<Topic>>(nextOffset, topics);
                }
                catch (Exception)
                {
                }
            }
            return null;
        }
        /// <summary>
        /// 转发动态
        /// </summary>
        /// <param name="content">附加内容</param>
        /// <param name="dynamicId">被转发动态ID</param>
        /// <param name="rid">被转发动态评论ID</param>
        /// <param name="type">被转发动态类型</param>
        /// <param name="atIds">at的人</param>
        /// <returns></returns>
        public async Task<bool> RepostDynamicAsync(string content, string dynamicId,string rid,int type,List<RepostLocation> atIds)
        {
            if (atIds == null)
                atIds = new List<RepostLocation>();
            var param = new Dictionary<string, string>();
            param.Add("content", Uri.EscapeDataString(content));
            param.Add("at_uids", string.Join(',', atIds.Select(p=>p.data)));
            param.Add("ctrl", Uri.EscapeDataString(JsonConvert.SerializeObject(atIds)));
            param.Add("dynamic_id", dynamicId);
            param.Add("rid", rid);
            param.Add("repost_code", "10000");
            param.Add("sync_comment", "0");
            param.Add("spec_type", "0");
            param.Add("type", type.ToString());
            param.Add("uid", BiliTool.mid.ToString());
            param.Add("extension", Uri.EscapeDataString("{\"emoji_type\":1}"));
            param.Add("statistics", Uri.EscapeDataString("{\"appId\":1,\"version\":\"5.56.1\",\"abtest\":\"\",\"platform\":1}"));
            var req = BiliTool.UrlContact("", param, true);
            var response = await BiliTool.PostContentToWebAsync(Api.DYNAMIC_REPOST, req);
            if (!string.IsNullOrEmpty(response))
            {
                var jobj = JObject.Parse(response);
                return jobj["code"].ToString() == "0";
            }
            return false;
        }
        
    }
}
