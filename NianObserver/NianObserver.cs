using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;   
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace STKProject.NianObserver
{
    public class NianObserver : STKWorker
    {
        public string TargetUID { get; set; }
        public string UserName { get; set; }
        public string PassWord { get; set; }
        public string Session { get; set; }
        private string database => Alias + ".db";
        private NianApi api;
        private string uName;
        private DateTime currentTime;

        protected NianContext context
        {
            get
            {
                if (_context == null)
                {
                    lock (_contextlock)
                    {
                        if (_context == null)
                        {
                            _context = new NianContext(database);
                        }
                    }
                }

                return _context;
            }
        }

        private NianContext _context;
        private object _contextlock = new object();
        /// <summary>
        /// 每天只会获取一次private记本的内容
        /// </summary>
        private int privatePeroid => (1000 * 60 * 60 * 24) / Math.Max(Interval, 60000);
        private int currentPeroid;
        /// <summary>
        /// 延迟登录,获取其他Worker的登录token用
        /// </summary>
        private bool _isDeferedLogin = false;
        /// <summary>
        /// 是否是第一次运行该模块,用于屏蔽第一次启动的数据获取
        /// </summary>
        private bool _isFirstRun = false;
        /// <summary>
        /// 对应数据项的增量抓取函数表
        /// </summary>

        private void GetDreamExtendInfo(Dream dream, NianContext context, bool Incremantal = true)
        {
            int lastupdated = 0;
            {
                string tmp;
                dream.Status.TryGetValue("lastupdated", out tmp);
                lastupdated = int.Parse(tmp??"0");
            }
            context.Entry(dream).Collection(d => d.Steps).Load();
            int maxPage = 1;
            //Origin Target
            int unresolvedDiff = 0;
            bool isAllResolved = false;
            int pos = dream.Steps.FindLastIndex(info => info.IsRemoved == false);

            bool isFoundHead = false;//是否找到最近出现的元素
            List<Step> pendingInsert = new List<Step>();
            int page;
            for (page = 1; page <= maxPage; page++)
            {
                if (dream.Status.ContainsKey("private"))
                {
                    if (dream.Status["private"] == "2") continue; //被删除的记本
                    if (dream.Status["private"] == "1" && currentPeroid % privatePeroid != 0) return;
                    //私密记本一天只能拉取1次
                }
                var result = api.GetDreamUpdate(dream.Status["id"], page);
                //Get Extend Dream Info

                var id = dream.Status["id"]; var title = dream.Status["title"];
                if (page == 1)
                {
                    var dreamInfo = (JObject)result["dream"];
                    foreach (var values in dreamInfo)
                    {
                        string valItem = "";
                        if (values.Value is JArray) continue;
                        string targetValue = values.Value.Value<string>();
                        if (dream.Status.ContainsKey(values.Key))
                            valItem = dream.Status[values.Key];
                        if (values.Key == "step")
                        {
                            unresolvedDiff =
                                int.Parse(targetValue) - int.Parse("0" + valItem);
                        }
                        if (valItem != targetValue)
                        {
                            DiffDetected?.Invoke(
                                "http://nian.so/#!/dream/" + id,
                                uName + "修改了梦想" + title + "的信息",
                                "属性" + values.Key + "从" + valItem + "变为" + targetValue,
                                "Nian." + TargetUID + ".Dream." + id + ".Info." + values.Key);
                            dream.Status[values.Key] = targetValue;
                        }
                    }
                    maxPage = (int)Math.Ceiling(int.Parse(dream.Status["step"]) / 20.0f);
                }
                if (unresolvedDiff == 0 && Incremantal)//需要翻到下一页才能完全保证,因为有同时删除和增加的情况
                {
                    if (isAllResolved)
                        break;
                    else
                        isAllResolved = true;
                }
                else
                    isAllResolved = false;
                var stepsArray = (JArray)result["steps"];
                foreach (var stepInfo in stepsArray)
                {
                    Step si;
                    int unresolvedComment = 0;
                    var currentId = stepInfo["sid"].Value<int>();
                    int index = dream.Steps.FindIndex(item => item.StepId == currentId);
                    Step refsi = dream.Steps.ElementAtOrDefault(index);
                    if (index == -1)
                    {
                        if (isFoundHead)
                            throw new Exception("莫名的顺序,怀疑不对");
                        si = new Step
                        {
                            Comments = new List<Comment>()
                        };
                        pendingInsert.Add(si);
                        foreach (var val in (JObject)stepInfo)
                        {
                            if (val.Key == "images")
                            {
                                foreach (var imgPath in (JArray)val.Value)
                                {
                                    si.Images.Add(imgPath["path"].Value<string>());
                                }
                            }
                            else
                                si.Status[val.Key] = val.Value.Value<string>();
                        }
                        DiffDetected?.Invoke(
                            "http://nian.so/m/step/" + currentId,
                            uName + "在记本" + title + "发布了一条足迹",
                            "足迹内容:" + si.Status["content"],
                            "Nian." + TargetUID + ".Dream." + id + ".Step." + currentId);
                        unresolvedComment = ((JObject)stepInfo)["comments"].Value<int>();
                        unresolvedDiff--;
                        si.Status["lastupdated"] = DateTime.Now.ToUnixTime().ToString();
                        lastupdated = (int)DateTime.Now.ToUnixTime();
                    }
                    else
                    {
                        isFoundHead = true;
                        if (refsi.IsRemoved == true)
                        {
                            refsi.IsRemoved = false; //修复之前因为转为private被误标记为已删除的内容
                            pos = index;
                        }
                        if (pos - index < 0)
                            throw new Exception("???WTF???");
                        for (int j = index + 1; j <= pos; j++)
                        {
                            if (!dream.Steps[j].IsRemoved)
                            {
                                dream.Steps[j].IsRemoved = true;
                                unresolvedDiff++;
                                DiffDetected?.Invoke(
                                    "http://nian.so/m/dream/" + dream.DreamId,
                                    uName + "删除了一条在" + title + "的足迹",
                                    "足迹内容:" + dream.Steps[j].Status["content"],
                                    "Nian." + TargetUID + ".Dream." + id + ".Step." + dream.Steps[j].StepId);
                                dream.Steps[j].Status["lastupdated"] = DateTime.Now.ToUnixTime().ToString();
                                lastupdated = (int)DateTime.Now.ToUnixTime();
                            }
                        }
                        pos = index - 1;
                        unresolvedComment =
                                int.Parse(refsi.Status["comments"]) -
                                ((JObject)stepInfo)["comments"].Value<int>();
                        refsi.Status["comments"] = ((JObject)stepInfo)["comments"].Value<string>();
                        si = refsi;
                    }

                    //需要检测的只有comment
                    if (unresolvedComment != 0)
                    {
                        ResolveComment(si, context, unresolvedComment);
                        si.Status["lastupdated"] = DateTime.Now.ToUnixTime().ToString();
                        lastupdated = (int)DateTime.Now.ToUnixTime();
                    }
                }
                dream.Status["lastupdated"] = lastupdated.ToString();
            }
            page -= 1;//for跳出的那一轮需要减去
            if ((page == maxPage || maxPage == 0) && pos >= 0)//最后一页时如果pos不等于0，说明有step被删除了
            {
                for (int j = pos; j >= 0; j--)
                {
                    if (!dream.Steps[j].IsRemoved)
                    {
                        dream.Steps[j].IsRemoved = true;
                        unresolvedDiff++;
                        DiffDetected?.Invoke(
                            "http://nian.so/m/dream/" + dream.Status["id"],
                            uName + "删除了一条在" + dream.Status["title"] + "的足迹",
                            "足迹内容:" + dream.Steps[j].Status["content"],
                            "Nian." + TargetUID + ".Dream." + dream.Status["id"] + ".Step." + dream.Steps[j].Status["sid"]);
                    }
                }
            }
            if (unresolvedDiff != 0)
                Console.WriteLine("Still Have UnresolvedDiff!!!!" + " Offset:" + unresolvedDiff);
            for (int j = pendingInsert.Count - 1; j >= 0; j--)
            {
                dream.Steps.Add(pendingInsert[j]);
            }
        }

        private void ResolveComment(Step step,NianContext context, int unresolvedComments)
        {
            //不想写增量了!
            //不过不用增量的方法怎么检测删除比较好呢?
            //还是写增量逻辑吧Q Q
            context.Entry(step).Collection(b => b.Comments);
            bool isFoundHead = false;
            List<Comment> pendingInsert = new List<Comment>();
            bool isAllResolved = false;
            int pos = step.Comments.FindLastIndex(info => info.IsRemoved == false);
            int maxPage = (int)Math.Ceiling(int.Parse(step.Status["comments"]) / 15.0f);
            string sid = step.StepId.ToString();
            for (int page = 1; page <= maxPage; page++)
            {
                var result = api.GetComments(step.StepId.ToString(), page);
                var commentArray = (JArray)result["comments"];
                foreach (var comment in commentArray)
                {
                    var cid = comment["id"].Value<int>();
                    int index = step.Comments.FindIndex(item => item.CommentId == cid);
                    if (index == -1)
                    {
                        if (isFoundHead)
                            throw new Exception("这里顺序不对吧");
                        //CreateNew
                        Comment cmt = new Comment
                        {
                        };
                        pendingInsert.Add(cmt);
                        foreach (var val in (JObject)comment)
                        {
                            cmt.Status[val.Key] = val.Value.Value<string>();
                        }
                        DiffDetected?.Invoke(
                                    "http://nian.so/m/step/" + step.Status["sid"],
                                    uName + "的足迹下出现了一条新评论!",
                                    "足迹:" + step.Status["content"] + " 评论:" + cmt.Status["content"] + " - By " + cmt.Status["user"],
                                    "Nian." + TargetUID + ".Dream." + step.Status["dream"] + ".Step." + sid + ".Comments");
                        unresolvedComments--;
                    }
                    else
                    {
                        isFoundHead = true;
                        if (pos - index < 0)
                            throw new Exception("??????WTF");
                        for (int j = index + 1; j <= pos; j++)
                        {
                            if (!step.Comments[j].IsRemoved)
                            {
                                step.Comments[j].IsRemoved = true;
                                unresolvedComments++;
                                DiffDetected?.Invoke(
                                    "http://nian.so/m/step/" + step.Status["sid"],
                                    uName + "的足迹下有一条评论被删除了!",
                                    "足迹:" + step.Status["content"] + " 评论:" + step.Comments[j].Status["content"] + " - By " + step.Comments[j].Status["user"],
                                    "Nian." + TargetUID + ".Dream." + step.Status["dream"] + ".Step." + sid + ".Comments");
                            }
                        }
                        pos = index - 1;
                    }
                }
                if (unresolvedComments == 0)
                {
                    if (isAllResolved)
                        break;
                    else
                        isAllResolved = true;
                }
                else
                    isAllResolved = false;
            }
            for (int j = pendingInsert.Count - 1; j >= 0; j--)
            {
                step.Comments.Add(pendingInsert[j]);
            }
        }

        private void GetDreamList(int targetUser, NianContext data)
        {
            int page = 1;
            var userInfo = data.UserInfo.Single(v => v.UserId == targetUser);
            for (page = 1;
                page <= Math.Ceiling(
                    int.Parse(userInfo.Status["dream"]) / 20.0f);
                page++)
            {
                var result = api.GetUserDreams(targetUser.ToString(), page);
                foreach (var dreamStatus in (JArray)result["dreams"])
                {
                    var dreamInfo = (JObject)dreamStatus;
                    var id = dreamInfo["id"].Value<int>();
                    var title = dreamInfo["title"].Value<string>();
                    var di = data.Dreams.SingleOrDefault(
                        info => info.DreamId == id);
                    if (di == null)
                    {
                        di = new Dream();
                        userInfo.Dreams.Add(di);
                        DiffDetected?.Invoke(
                            "http://nian.so/#!/dream/" + id,
                            uName + "新增了梦想" + title,
                            uName + "新增了梦想" + title,
                            "Nian." + targetUser + ".Dream." + id + ".Info");
                        di.Steps = new List<Step>();
                    }
                    foreach (var val in dreamInfo)
                    {
                        di.Status[val.Key] = val.Value.Value<string>();
                    }

                }
            }
            foreach (var dataDream in userInfo.Dreams)
            {
                GetDreamExtendInfo(dataDream, data, currentPeroid % privatePeroid != 0);
            }
        }



        protected override void Prepare()
        {
            base.Prepare();

            api = new NianApi();
            if (string.IsNullOrWhiteSpace(Session))
            {
                Session = Alias + ".session";
                Login();
            }
            else
            {
                if (Session.IndexOf(".session", StringComparison.OrdinalIgnoreCase) == -1)
                    Session += ".session";
                _isDeferedLogin = true;
            }
            context.Database.EnsureCreated();
            context.Database.Migrate();
        }

        void Login()
        {
            bool loginFlag = false;
            if (File.Exists(Session))
            {
                string[] data = File.ReadAllLines(Session);
                if (api.RestoreLogin(data[0], data[1]))
                    loginFlag = true;
            }
            if (loginFlag == false && api.Login(UserName, PassWord))//Short Circuit
            {
                string uid = "";
                string shell = "";
                api.GetLoginToken(out uid, out shell);
                File.WriteAllText(Session, uid + "\r\n" + shell);
            }
            else if (loginFlag == false)
            {
                throw new ArgumentException(Alias + ":无法登录!");
            }
        }

        protected override void Run()
        {

            if (IsFirstRun && _isDeferedLogin)
            {
                waitToken.WaitHandle.WaitOne(1000);
                Login();
            }
            if (IsFirstRun && !IsTest)
            {
                waitToken.WaitHandle.WaitOne(new Random().Next(0, 100000));
            }
            
            var value = DiffDetected;
            if (_isFirstRun)
                DiffDetected = null;
            using (var transaction = context.Database.BeginTransaction())
            {
                foreach (var uid in TargetUID.Split(',').Select(str => int.Parse(str)))
                {

                    try
                    {
                        var data = context.UserInfo.SingleOrDefault(r => r.UserId == uid);
                        if (data == null)
                        {
                            data = new User();
                            data.UserId = uid;
                            context.UserInfo.Add(data);
                        }

                        currentTime = DateTime.Now;
                        //Status Data Compare
                        var userData = api.GetUserData(TargetUID.ToString());
                        var result = userData["user"] as JObject;
                        uName = result["name"].Value<string>();
                        foreach (var obj in result)
                        {
                            var val = obj.Value as JValue;
                            if (val != null)
                            {
                                var targetValue = obj.Value.Value<string>();
                                var sourceValue = "";
                                if (data.Status.ContainsKey(obj.Key))
                                    sourceValue = data.Status[obj.Key];
                                if (sourceValue != targetValue)
                                {
                                    data.Status[obj.Key] = targetValue;
                                    DiffDetected?.Invoke(
                                        "http://nian.so/#!/user/" + TargetUID,
                                        uName + "修改了" + obj.Key,
                                        uName + "修改了" + obj.Key + ",从" + sourceValue + "变为" + targetValue,
                                        "Nian." + TargetUID + ".UserInfo." + obj.Key);
                                }
                            }
                        }

                        Console.WriteLine("wwww" + data);
                        GetDreamList(uid, context);

                        currentPeroid++;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        Console.WriteLine("Module" + Alias + " Throw an Exception");
                    }
                }
            }

            context.SaveChanges();

            if (_isFirstRun)
            {
                _isFirstRun = false;
                DiffDetected = value;
            }
            Console.WriteLine(Alias + ":Data Fetched");

        }

        public override void LoadDefaultSetting()
        {
            base.LoadDefaultSetting();
            int randNum = new Random().Next(1, 100000);
            Alias = "NianStalker" + randNum;
            Interval = 600000;
        }

        //[STKDescription("当有数据更新之后")]
        public Action<string, string, string, string> DiffDetected { get; set; }

        /*
        private void FailReturn(HttpListenerContext context, string reason)
        {
            JObject obj = new JObject();
            obj.Add("status", 401);
            obj.Add("error", 0);
            JObject data = new JObject();
            obj.Add("data", data);
            data.Add("description", reason);
            context.ResponseString(obj.ToString());
        }
        /// <summary>
        /// 
        /// /[DreamID]
        /// /[DreamID]/[StepID]
        /// /[DreamID]/[StepID]/[CommentID]
        /// </summary>
        /// <param name="context"></param>
        /// <param name="subUrl"></param>
        [STKDescription("输出Step信息")]
        public void DisplayStepInfo(HttpListenerContext context, string subUrl)
        {
            string inUrl = context.Request.RawUrl.Replace(subUrl, "");
            string[] splited = inUrl.Split('?')[0].Split('/');
            if (splited.Length == 0 || splited.Length == 1)
            {
                ResponseUserInfo(context);
                return;
            }
            DreamInfo dream = data.Dreams.FirstOrDefault(d => d.Status["id"] == splited[1]);
            if (dream == null)
            {
                FailReturn(context, "Dream" + splited[1] + "Not Found");
                return;
            }
            else
            {
                if (splited.Length == 2)
                {
                    ResponseDreamInfo(context, dream);
                    return;
                }
            }
            StepInfo step = dream.Steps.FirstOrDefault(d => d.Status["sid"] == splited[2]);
            if (step == null)
            {
                FailReturn(context, "Step" + splited[2] + "Not Found");
                return;
            }
            else
            {
                if (splited.Length == 3)
                {
                    ResponseStepInfo(context, step);
                    return;
                }
            }
            CommentInfo cmt = step.Comments.FirstOrDefault(d => d.Status["id"] == splited[3]);
            if (cmt == null)
            {
                FailReturn(context, "Comment" + splited[3] + "Not Found");
                return;
            }
            else
            {
                if (splited.Length == 4)
                {
                    ResponseCommentInfo(context, cmt);
                    return;
                }
            }
            FailReturn(context, "???");
        }

        private static void ResponseCommentInfo(HttpListenerContext context, CommentInfo cmt)
        {
            var obj = new JObject { { "status", 200 }, { "error", 0 } };
            var inner = new JObject();
            obj.Add("data", inner);
            foreach (var stat in cmt.Status)
            {
                inner.Add(stat.Key, stat.Value);
            }
            obj.Add("isremoved", cmt.IsRemoved);
            context.ResponseString(obj.ToString());
        }

        private static void ResponseStepInfo(HttpListenerContext context, StepInfo step)
        {
            var obj = new JObject { { "status", 200 }, { "error", 0 } };
            var inner = new JObject();
            obj.Add("data", inner);
            foreach (var stat in step.Status)
            {
                inner.Add(stat.Key, stat.Value);
            }
            JArray comments = new JArray();
            inner.Add("comment", comments);
            inner.Add("isremoved", step.IsRemoved);
            foreach (var stepComment in step.Comments)
            {
                JObject stepContent = new JObject
                {
                    {"id", stepComment.Status["id"]},
                    {"content", stepComment.Status["content"]},
                    {"isremoved", stepComment.IsRemoved},
                    {"user", stepComment.Status["user"]}
                };
                comments.Add(stepContent);
            }
            JArray images = new JArray();
            inner.Add("images", images);
            foreach (var stepImage in step.Images)
            {
                images.Add(stepImage);
            }
            context.ResponseString(obj.ToString());
        }

        private static void ResponseDreamInfo(HttpListenerContext context, DreamInfo dream)
        {
            var obj = new JObject { { "status", 200 }, { "error", 0 } };
            var inner = new JObject();
            obj.Add("data", inner);
            foreach (var stat in dream.Status)
            {
                inner.Add(stat.Key, stat.Value);
            }
            JArray steps = new JArray();
            inner.Add("steps", steps);
            foreach (var dreamStep in dream.Steps)
            {
                JObject stepContent = new JObject
                {
                    {"id", dreamStep.Status["sid"]},
                    {"content", dreamStep.Status["content"]},
                    {"isremoved", dreamStep.IsRemoved},
                    {"lastupdated",dreamStep.Status.ContainsKey("lastupdated") ? dreamStep.Status["lastupdated"] : "0" }
                };
                steps.Add(stepContent);
            }
            context.ResponseString(obj.ToString());
        }

        private void ResponseUserInfo(HttpListenerContext context)
        {
            var obj = new JObject { { "status", 200 }, { "error", 0 } };
            var inner = new JObject();
            obj.Add("data", inner);
            foreach (var dataListItem in data.ListItems)
            {
                inner.Add(dataListItem.Key, dataListItem.Value);
            }
            JArray dreams = new JArray();
            inner.Add("dreams", dreams);
            foreach (var dataDream in data.Dreams)
            {
                JObject dreamContent = new JObject
                {
                    {"id", dataDream.Status["id"]},
                    {"title", dataDream.Status["title"]},
                    {"private", dataDream.Status["private"]},
                    {"lastupdated", dataDream.Status.ContainsKey("lastupdated")? dataDream.Status["lastupdated"] : "0" }
                };
                dreams.Add(dreamContent);
            }
            context.ResponseString(obj.ToString());
        }
        */
        

        #region WebInterface

        [Route("User/{id}")]
        [Route("{id}")]
        public JObject GetUserInfo(int id)
        {
            var result = context.UserInfo.SingleOrDefault(status => status.UserId == id);
            if (result == null)
                return null;
            JObject obj = new JObject();
            obj.AddStatus(result.Status);
            context.Entry(result).Collection(w => w.Dreams);
            JArray dreams = new JArray();
            obj.Add("dreams", dreams);
            foreach (var dataDream in result.Dreams)
            {
                JObject dreamContent = new JObject
                {
                    {"id", dataDream.DreamId},
                    {"title", dataDream.Status["title"]},
                    {"private", dataDream.Status["private"]},
                    {"lastupdated", dataDream.Status.ContainsKey("lastupdated")? dataDream.Status["lastupdated"] : "0" }
                };
                dreams.Add(dreamContent);
            }
            return obj;
        }

        [Route("Dream/{id}")]
        public JObject GetDreamInfo(int id)
        {
            var result = context.Dreams.SingleOrDefault(status => status.DreamId == id);
            if (result == null)
                return null;
            JObject obj = new JObject();
            obj.AddStatus(result.Status);
            context.Entry(result).Collection(w => w.Steps);
            JArray steps = new JArray();
            obj.Add("steps", steps);
            foreach (var dreamStep in result.Steps)
            {
                JObject stepContent = new JObject
                {
                    {"id", dreamStep.StepId},
                    {"content", dreamStep.Status["content"]},
                    {"isremoved", dreamStep.IsRemoved},
                    {"lastupdated",dreamStep.Status.ContainsKey("lastupdated") ? dreamStep.Status["lastupdated"] : "0" }
                };
                steps.Add(stepContent);
            }

            return obj;
        }

        [Route("Step/{id}")]
        public JObject GetStepInfo(int id)
        {
            var result = context.Steps.SingleOrDefault(status => status.StepId == id);
            if (result == null)
                return null;
            JObject obj = new JObject();
            obj.AddStatus(result.Status);
            context.Entry(result).Collection(w => w.Comments);
            JArray comments = new JArray();
            obj.Add("comment", comments);
            obj.Add("isremoved", result.IsRemoved);
            foreach (var stepComment in result.Comments)
            {
                JObject stepContent = new JObject
                {
                    {"id", stepComment.Status["id"]},
                    {"content", stepComment.Status["content"]},
                    {"isremoved", stepComment.IsRemoved},
                    {"user", stepComment.Status["user"]}
                };
                comments.Add(stepContent);
            }
            JArray images = new JArray();
            obj.Add("images", images);
            foreach (var stepImage in result.Images)
            {
                images.Add(stepImage);
            }

            return obj;
        }

        [Route("Comment/{id}")]
        public JObject GetCommentInfo(int id)
        {
            var result = context.Comments.SingleOrDefault(w => w.CommentId == id);
            if (result == null)
                return null;
            JObject obj = new JObject();
            obj.AddStatus(result.Status);
            obj.Add("isremoved",result.IsRemoved);
            return obj;
        }
        #endregion
    }

    internal static class JObjectHelper
    {
        public static void AddStatus(this JObject obj, Dictionary<string, string> status)
        {
            foreach (var element in status)
            {
                obj.Add(element.Key, element.Value);
            }
        }
    }
}
