using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace STKProject.NianObserver
{
    public class NianContext :DbContext
    {

        public DbSet<Dream> Dreams { get; set; }
        public DbSet<Step> Steps { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<UserStatus> UserInfo { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbpath}");
        }

        public NianContext(string databasePath)
        {
            _dbpath = databasePath;
        }

        public string _dbpath;
    }

    public class MixJson
    {
        [NotMapped]
        public Dictionary<string, string> Status => _stat;

        private void UpdateStat(string value)
        {
            _stat.Clear();
            JObject obj = JObject.Parse(value);
            foreach (KeyValuePair<string, JToken> ele in obj)
            {
                _stat[ele.Key] = ele.Value.Value<string>();
            }
        }

        private string GetJson()
        {
            JObject obj = new JObject();
            foreach (var ele in Status)
            {
                obj.Add(ele.Key,ele.Value);
            }

            return obj.ToString(Formatting.None);
        }

        [NotMapped]
        public Dictionary<string, string> _stat = new Dictionary<string, string>();

        public string Stat
        {
            get => GetJson();
            set => UpdateStat(value);
        }
    }

    public class UserStatus : MixJson
    {
        [Key]
        public int UserId { get; set; }
    }

    public class Dream : MixJson
    {
        public int DreamId { get; set; }
        public List<Step> Steps { get; set; }
    }

    public class Step : MixJson
    {
        public Dream Dream { get; set; }
        public int StepId { get; set; }
        public List<Comment> Comments { get; set; }
        public bool IsRemoved { get; set; }

        private List<string> _images = new List<string>();
        [NotMapped]
        public List<string> Images => _images;

        public string Image
        {
            get { return string.Join('^', _images); }
            set
            {
                _images.Clear();
                _images.AddRange(value.Split('^'));
            }
        }
    }

    public class Comment : MixJson
    {
        public Step Step { get; set; }
        public int CommentId { get; set; }
        public bool IsRemoved { get; set; }
    }

    /*
       public class NianData
       {
       public ObjectId Id { get; set; }
       /// <summary>
       /// 用于存放始终存在的数据：粉丝，关注等
       /// </summary>
       public Dictionary<string,string> ListItems { get; set; }
       public List<DreamInfo> Dreams { get; set; }
       }

       public class DreamInfo
       {
       public ObjectId Id { get; set; }
       public Dictionary<string, string> Status { get; set; }
       //public bool isRemoved { get; set; }
       public List<StepInfo> Steps { get; set; }
       }

       public class StepInfo : IRemoveFlag
       {
       public ObjectId Id { get; set; }
       public List<string> Images { get; set; }
       public Dictionary<string,string> Status { get; set; }
       public List<CommentInfo> Comments { get; set; }
       /*
       * StepInfo中,因为会影响到增量计数,所以还是加上isRemoved标识了
       * /
       public bool IsRemoved { get; set; }
       }

       public class CommentInfo : IRemoveFlag
       {
       public ObjectId Id { get; set; }
       public Dictionary<string,string> Status { get; set; }
       public bool IsRemoved { get; set; }
       }
     */
}
