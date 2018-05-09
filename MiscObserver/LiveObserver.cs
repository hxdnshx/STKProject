using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;

namespace STKProject.MiscObserver
{
    public class LiveObserver : STKWorker
    {
        public int TargetRoom { get; set; }
        private HttpClient client;
        private bool prevStatus = true;

        protected override void Prepare()
        {
            base.Prepare();
            client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(
                MediaTypeWithQualityHeaderValue.Parse("text/html"));
            client.DefaultRequestHeaders.Accept.Add(
                MediaTypeWithQualityHeaderValue.Parse("application/xhtml+xml"));
            client.DefaultRequestHeaders.Accept.Add(
                MediaTypeWithQualityHeaderValue.Parse("application/xml"));
            client.DefaultRequestHeaders.Accept.Add(
                MediaTypeWithQualityHeaderValue.Parse("*/*"));
            client.DefaultRequestHeaders.Host = "api.live.bilibili.com";
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 6.2; WOW64; rv:19.0) Gecko/20100101 Firefox/19.0");
        }

        public string HttpGet(string addr)
        {
            var result = client.GetAsync(addr).Result;
            return result.Content.ReadAsStringAsync().Result;
        }

        protected override void Run()
        {
            base.Run();
            string ret = "";
            try
            {
                ret = HttpGet($"https://api.live.bilibili.com/room/v1/Room/get_info?room_id={TargetRoom}&from=room");
                //ret = HttpGet("https://api.live.bilibili.com/live/getInfo?roomid=" + TargetRoom);
                JObject obj = JObject.Parse(ret);
                if (obj["code"].Value<int>() == -400)
                {
                    Console.WriteLine("无效的房间号ID:" + TargetRoom);
                    return;
                }
                bool status = obj["data"]["live_status"].Value<int>() == 1;
                string title = obj["data"]["title"].Value<string>();
                //string nickname = obj["data"]["ANCHOR_NICK_NAME"].Value<string>();
                //Console.WriteLine(String.Format("Bilibili LiveRoom{0},Status:{1}", title, status ? "ON" : "OFF"));
                if (status != prevStatus)
                {
                    if (status == true)
                    {
                        DiffDetected?.Invoke(
                            "http://live.bilibili.com/" + TargetRoom,
                             "开启了直播间" + title + "！",
                            "直播间标题是：" + title,
                            "Bilibili.Live." + TargetRoom);
                    }
                    else
                    {
                        DiffDetected?.Invoke(
                            "http://live.bilibili.com/" + TargetRoom,
                            "关闭了直播间" + title + "！",
                            "直播间标题是：" + title,
                            "Bilibili.Live." + TargetRoom);
                    }
                    prevStatus = status;
                }
            }
            catch (Exception)
            {
                /*
                File.AppendAllText("LiveError.log",
                    "\n" + e + "\n" + "收到包内容：\n" + ret);
                    */
            }
        }

        public override void LoadDefaultSetting()
        {
            base.LoadDefaultSetting();
            int randInt = new Random().Next(1, 100000);
            Alias = "LiveStalker" + randInt;
            Interval = 300000;
        }

        public Action<string, string, string, string> DiffDetected { get; set; }
    }
}
