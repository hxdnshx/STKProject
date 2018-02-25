using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;


namespace STKProject.MiscObserver
{
    class ContentMirror : ISTKService
    {
        public string MirrorDir { get; set; }
        public string LocalDir { get; set; }
        private HttpClient client;

        public string Alias { get; set; }

        public void Start()
        {
            
        }

        public void Stop()
        {
            
        }

        public void LoadDefaultSetting()
        {
            Alias = "ContentMirror" + new Random().Next(1, 10000);
            MirrorDir = "http://www.baidu.com/";
        }

        public ContentMirror()
        {
            client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 11_0 like Mac OS X) AppleWebKit/604.1.38 (KHTML, like Gecko) Version/11.0 Mobile/15A356 Safari/604.1");
            //client.DefaultRequestHeaders.Add("Content-Type", "application /x-www-form-urlencoded");
            client.DefaultRequestHeaders.Add("Referer", "http://nian.so/m/step/");
            //client.DefaultRequestHeaders.Add("Origin", "http://music.163.com");
            client.DefaultRequestHeaders.Add("Host", "img.nian.so");
            client.DefaultRequestHeaders.Add("Accept", "image/webp,image/*,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            client.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.8");
        }

        public static string CombineDir(string a, string b)
        {
            bool checka = a.EndsWith("/");
            bool checkb = b.StartsWith("/");
            if (checkb && checka)
                return a + b.Substring(1);
            else if (checkb || checka)
                return a + b;
            else
                return a + "/" + b;
        }

        [Route("{*dir}")]
        public void OnHttpRequest(string dir, HttpContext context)
        {
            //因为由Kestrel负责了多线程，所以这里弄成同步阻塞也是可以的
            string reldir = CombineDir(LocalDir, dir);
            if (!File.Exists(reldir))
            {
                var result = client.GetAsync(dir).Result;
                if (!result.IsSuccessStatusCode)
                {
                    context.Response.StatusCode = 404;
                }

                string saveDir = reldir;
                try
                {
                    FileHelper.ResolvePath(saveDir);
                    FileStream stream = new FileStream(saveDir, FileMode.Create);
                    var src = result.Content.ReadAsStreamAsync().Result;
                    src.CopyTo(stream);
                    src.Close();
                    stream.Close();
                    context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            {
                FileStream stream = new FileStream(reldir, FileMode.Open);
                stream.CopyTo(context.Response.Body);
                stream.Close();
            }
        }
        

        public static byte[] Decompress_GZip(byte[] gzip)
        {
            using (GZipStream stream = new GZipStream(new MemoryStream(gzip),
                                       CompressionMode.Decompress))
            {
                byte[] buffer = new byte[1024];
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(buffer, 0, 1024);
                        if (count > 0)
                        {
                            memory.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return memory.ToArray();
                }
            }
        }
    }
}

