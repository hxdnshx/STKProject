using System;
using System.Collections.Generic;
using System.Text;

namespace STKProject
{
    public interface ISTKService
    {
        /// <summary>
        /// 用于标识这个服务的别名
        /// </summary>
        string Alias { get; set; }
        void Start();
        void Stop();
        /// <summary>
        /// 调用后加载对这个服务而言的默认配置
        /// </summary>
        void LoadDefaultSetting();
    }
}
