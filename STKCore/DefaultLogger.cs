using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace STKProject
{
    class DefaultLogger : ILogger,ISTKService
    {
        public void Log(string strr)
        {
            Debug.WriteLine(strr);
        }

        public string Alias { get; set; }
        public void Start()
        {
            
        }

        public void Stop()
        {
            
        }

        public void LoadDefaultSetting()
        {
            Alias = "Logger";
        }
    }
}
