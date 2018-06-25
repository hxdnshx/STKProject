using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Web;
using Newtonsoft.Json.Linq;

namespace STKProject.MiscObserver
{
    public class ServerChan : STKWorker
    {
        private readonly HttpClient client;
        public string SCKEY { get; set; }

        struct sendData
        {
            public string text;
            public string desp;
        }

        public const string RequestUrl = "https://sc.ftqq.com/{0}.send";
        private readonly ConcurrentQueue<sendData> _sendQueue;
        private AutoResetEvent _isQueue;
        private bool _stat;


        public ServerChan() : base()
        {
            client = new HttpClient();
            _sendQueue = new ConcurrentQueue<sendData>();
            _isQueue = new AutoResetEvent(false);
            _stat = true;
        }

        public string HttpGet(string addr)
        {
            var result = client.GetAsync(addr).Result;
            return result.Content.ReadAsStringAsync().Result;
        }

        public string HttpPost(string addr, HttpContent content)
        {

            var result = client.PostAsync(addr, content).Result;
            return result.Content.ReadAsStringAsync().Result;
        }

        protected override void Run()
        {
            base.Run();
            sendData result;
            if (_sendQueue.TryDequeue(out result))
            {
                for (;;)
                {
                    bool success = false;
                    try
                    {
                        var ret = HttpPost(string.Format(RequestUrl, SCKEY),
                            new FormUrlEncodedContent(
                                new Dictionary<string, string>
                                {
                                    {"text", result.text},
                                    {"desp", result.desp}
                                })); //_helper.HttpGet(result);
                        var jsonDoc = JObject.Parse(ret);
                        var err = jsonDoc["errno"].Value<int>();
                        if (err != 0)
                        {
                            var errtxt = jsonDoc["errmsg"].Value<string>();
                            Console.WriteLine("Serverchan send Error:" + errtxt);
                            if (errtxt == "bad pushtoken")
                            {
                                Console.WriteLine("Invalid Pushtoken,Exit ServerChan Module");
                                _stat = false;
                                return;
                            }
                        }

                        success = true;
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Send Failed.Try Resend after 5 seconds.");
                    }

                    if (success)
                        break;
                    Thread.Sleep(5000);
                }
            }
            else
            {
                //列表空，等待
                //_isQueue.WaitOne();
                WaitHandle.WaitAny(new WaitHandle[] { _isQueue, this.waitToken.WaitHandle },10000);
            }
        }

        public override void LoadDefaultSetting()
        {
            int randInt = new Random().Next(1, 100000);
            Alias = "ServerChan" + randInt.ToString();
            Interval = 1000;
        }

        //[STKDescription("录入新的数据")]
        public void InputData(string relatedAddress, string summary, string content, string relatedVar)
        {
            if (!_stat) return;
            _sendQueue.Enqueue(new sendData{desp = content,text = summary});
            _isQueue.Set();
        }
    }
}
