# STKProject
## 介绍
这是一个用于对若干个SNS平台所获取的信息，进行总合，汇集，以特定的方式（如每日邮件，RSS，总合网页）进行展现，以提高信息获取效率的项目。
~~设置成他人后可以变相进行STK，不过这样做大概会孤独一生的！~~

计划采用Vue.js架设前端，nginx+C#(.Net Core)架设后端，对于部分页面的抓取通过casperjs进行。

此项目的前身是 [StalkerProject](https://github.com/hxdnshx/StalkerProject),为了迁移到.Net Core+docker的环境以及功能上的需求而进行了重构，目前正逐步将过去的功能进行迁移。

## 进度
1. 抓取目标
 - [ ] 网易云（完成）（[netease.js](https://github.com/hxdnshx/StalkerProject/blob/master/netease.js)）
 - [ ] 念（除图片拉取全部完成）（登录相关参考[nian-Robot](https://github.com/ConnorNowhere/nian-robot)）（[API-nian.so](https://github.com/hxdnshx/StalkerProject/blob/master/API-nian.so)）(直接使用网页抓取,可能会增加浏览计数)
 - [ ] RSS源
 - [ ] 微博(可登录)
 - [ ] QQ（待定）
 - [ ] 微信朋友圈（待定）
2. 数据分析	
 - [ ] 网易云（进行增量Diff，输出新的数据）
 - [ ] 念(进行增量Diff,输出新的数据)
 - [ ] 微博
 - [ ] QQ（待定）
 - [ ] 微信朋友圈（待定）
 - [ ] 腾讯语义分析对接
3. 数据输出
 - [ ] 邮件(完成!)
 - [ ] RSS(完成!)
 - [ ] 网页报表(支持念记录的网络显示)
 - [ ] 微信推送(通过[ServerChan](http://sc.ftqq.com)实现)
4. 工程部署
 - [x] docker
5. 系统管理
 - [ ] 网页界面
 - [x] 服务稳定性监视(可以查看单个服务的运行状态，服务器的运行可以借助uptimerobot之类的工具)

