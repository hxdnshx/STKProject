using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace STKProject
{
    public class VariableRouterCollection : RouteCollection
    {
        /*
         * RouteCollection内私有的成员
         */
        private List<IRouter> m_routes;
        private List<IRouter> m_unnamedRoutes;

        public VariableRouterCollection() : base()
        {

        }

        public void RemoveRouting(object obj)
        {
            if (obj == null)
                return;
            if (m_routes == null)
            {
                try
                {
                    var routeInfo = typeof(RouteCollection).GetField("_routes",
                        BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
                    var unamerouteInfo = typeof(RouteCollection).GetField("_unnamedRoutes",
                        BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance);
                    m_routes = (List<IRouter>)routeInfo.GetValue(this);
                    m_unnamedRoutes = (List<IRouter>)unamerouteInfo.GetValue(this as RouteCollection);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }   
    }
}
