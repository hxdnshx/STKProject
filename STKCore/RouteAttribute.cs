using System;
using System.Collections.Generic;
using System.Text;

namespace STKProject
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    class RouteAttribute : Attribute,IRouteInfo
    {
        public string Template { get; set; }

        public string Verb { get; set; }

        public RouteAttribute(string template, string verb = "Get")
        {
            Template = template;
            Verb = verb;
        }
    }
}
