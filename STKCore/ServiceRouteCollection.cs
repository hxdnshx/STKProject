using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Routing;

namespace STKProject
{
    class ServiceRouteCollection : RouteCollection
    {
        public ISTKService Service { get; set; }
    }
}
