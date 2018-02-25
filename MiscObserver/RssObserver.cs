using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.EntityFrameworkCore;


namespace STKProject.MiscObserver
{

    

    public class RssObserver : STKWorker
    {
        class RssData : DbContext
        {
            [Key]
            public string GUID { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }

            public DateTime PubTime { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseSqlite($"Data Source={_dbpath}");
            }

            public RssData(string databasePath)
            {
                _dbpath = databasePath;
            }

            public string _dbpath;
        }
        public string URL { get; set; }
        public string CustomTimeFormat { get; set; }
        private RssData _context;

        protected override void Prepare()
        {
            _context = new RssData(Alias + ".db");
        }

        public static SyndicationFeed GetFeed(string uri, string timeFormat = "")
        {
            if (!string.IsNullOrEmpty(uri))
            {
                var ff = new Rss20FeedFormatter(); // for Atom you can use Atom10FeedFormatter()
                var xr = new MyXmlReader(uri, timeFormat);
                ff.ReadFrom(xr);
                return ff.Feed;
            }
            return null;
        }

        public SQLiteConnection CreateConnectionForSchemaCreation(string fileName)
        {
            var conn = new SQLiteConnection(
                new SQLitePlatformGeneric()
                , fileName);
            conn.CreateTable<RSSData>();
            return conn;
        }

        [Table("FeedData")]
        class FeedGUID
        {
            public string GUID { get; set; }
        }

        protected override void Run()
        {
            base.Run();
            if (IsFirstRun)
            {
                this.waitToken.WaitHandle.WaitOne(new Random().Next(0, 2000000));
            }
            List<string> historyFeeds = new List<string>();
            var result = _conn.Query<FeedGUID>("SELECT GUID FROM FeedData ORDER BY PubTime DESC LIMIT 50");
            foreach (var val in result)
            {
                historyFeeds.Add(val.GUID);
            }
            SyndicationFeed feed;
            try
            {
                feed = GetFeed(URL, CustomTimeFormat);
            }
            catch (Exception e)
            {
                //Network Error
                Console.WriteLine(e);
                Console.WriteLine("Unable to Get Feed:" + URL);
                return;
            }
            string title = feed.Title.Text;
            _conn.RunInTransaction(() =>
            {
                foreach (var synItem in feed.Items.Reverse())
                {
                    if (historyFeeds.IndexOf(synItem.Id) != -1) continue;
                    try
                    {
                        _conn.Insert(new RSSData
                        {
                            Description = synItem.Summary.Text,
                            GUID = synItem.Id,
                            Title = synItem.Title.Text,
                            PubTime = synItem.PublishDate.DateTime.ToUniversalTime()
                        });
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to Insert data:\n{synItem.Id}\n{synItem.Title.Text}\n{synItem.Summary.Text}\n{synItem.PublishDate}");
                        Console.WriteLine("Error info:" + e);
                        //continue;
                        break;
                        //此处违反约束的原因是重复的Id，所以不应该更新数据
                        //因为其实数据约束也就这一个嘛
                    }
                    DiffDetected?.Invoke(synItem.Id, title + " - " + synItem.Title.Text, synItem.Summary.Text, Alias + ".Updated");
                }
            });
            /*
            using (SQLiteTransaction trans = _conn.BeginTransaction())
            {
                foreach (var synItem in feed.Items.Reverse())
                {
                    if (historyFeeds.IndexOf(synItem.Id) != -1) continue;
                    try
                    {
                        _conn.ExecCommand(
                            "INSERT INTO FeedData(GUID,Title,Desc, PubTime) VALUES(@GUID,@Title,@Desc,@PubTime)",
                            new Dictionary<string, object>
                            {
                                {"@GUID", synItem.Id},
                                {"@Title", synItem.Title.Text},
                                {"@Desc", synItem.Summary.Text},
                                {"@PubTime", synItem.PublishDate.DateTime.ToUnixTime()}
                            });
                    }
                    catch (Exception e)
                    {
                        //Another change will modify even if some commands fail.
                        Console.WriteLine(e);
                    }
                    DiffDetected?.Invoke(synItem.Id,synItem.Title.Text,synItem.Summary.Text,Alias + ".Updated");
                }
                trans.Commit();
            }
            */
        }

        public override void LoadDefaultSetting()
        {
            base.LoadDefaultSetting();
            int randInt = new Random().Next(1, 100000);
            Alias = "RssObserver" + randInt.ToString();
            Interval = 3600000;
            CustomTimeFormat = "ddd, dd MMM yyyy HH:MM:SS GMT";
        }

        public Action<string, string, string, string> DiffDetected { get; set; }
    }
}

