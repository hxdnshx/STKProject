using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stalker = StalkerProject.NianObserver;
using STK = STKProject.NianObserver;
using LiteDB;


namespace Nian_immigration
{
    public static class DictonaryHelper
    {
        public static void AddRange<T, U>(this Dictionary<T, U> dst, Dictionary<T, U> src)
        {
            foreach (var elements in src)
            {
                dst[elements.Key] = elements.Value;
            }
        }
    }
    class Program
    {



        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            if (args.Length != 2)
                return;
            if (!File.Exists(args[0]))
                return;
            LiteDatabase db = new LiteDatabase(args[0]);
            var oldData = db.GetCollection<Stalker.NianData>().FindOne(Query.All());
            var newData = new STK.NianContext(args[1]);
            newData.Database.EnsureCreated();
            {
                //UserInfo
                var status = new STK.UserStatus();
                status.Status.AddRange(oldData.ListItems);
                status.UserId =Convert.ToInt32(oldData.ListItems["uid"]);
                newData.UserInfo.Add(status);
            }

            {
                //Dreams Steps Comments
                foreach (var oldDataDream in oldData.Dreams)
                {
                    var newDataDream = new STK.Dream();
                    newDataDream.DreamId = Convert.ToInt32(oldDataDream.Status["id"]);
                    newDataDream.Status.AddRange(oldDataDream.Status);
                    if(newDataDream.Steps == null)
                        newDataDream.Steps = new List<STK.Step>();
                    newDataDream.Steps.AddRange(oldDataDream.Steps.Select(steps =>
                    {
                        var newStep = new STK.Step();
                        newStep.StepId = Convert.ToInt32(steps.Status["sid"]);
                        newStep.Images.AddRange(steps.Images);
                        newStep.IsRemoved = steps.IsRemoved;
                        newStep.Status.AddRange(steps.Status);
                        newStep.Comments = new List<STK.Comment>(steps.Comments.Select(comment =>
                        {
                            STK.Comment newComment = new STK.Comment();
                            newComment.CommentId = Convert.ToInt32(comment.Status["id"]);
                            newComment.Status.AddRange(comment.Status);
                            return newComment;
                        }));
                        return newStep;
                    }));
                    newData.Dreams.Add(newDataDream);
                }
            }

            newData.SaveChanges();
            foreach (var steps in newData.Steps)
            {
                Console.WriteLine(steps.StepId);
            }
        }
    }
}
