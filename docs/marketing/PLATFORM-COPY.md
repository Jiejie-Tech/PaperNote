# PaperNote 平台宣传文案

发布时请把项目地址、下载地址、版本号和设备截图替换为实际内容。

## B站

### 标题

我做了一款支持电脑、手机和平板的开源手写笔记｜PaperNote

### 简介

PaperNote 是一款本地优先、免费开源的手写笔记与 PDF 批注应用，目前支持 Windows 10/11 和 Android 6.0 以上设备。

当前功能包括：

- 钢笔、荧光笔、橡皮擦、撤销重做和触控笔压力输入
- 空白、点阵、横线、方格等纸张模板
- 页面新增、复制、删除、命名和快速跳转
- PDF 导入、手写批注与重新导出
- 文本、图片和七类常用形状
- 搜索、回收站、自动保存、单笔记导入导出和整库备份恢复
- Windows 与 Android 共用 `.papernote` 和 PaperInk 墨迹格式

所有核心笔记数据默认保存在设备本地，不需要账号。Android 客户端不申请网络权限。PDF 文本搜索适用于自带文本层的文件；扫描版 PDF 暂不含离线 OCR。当前版本也不包含账号云同步或多人协作。

项目源码：GitHub / Gitee 搜索 PaperNote
下载位置：项目 Releases 中的 Windows 包、Android ZIP 与 Android APK

推荐标签：开源软件、手写笔记、Android、Windows、触控笔、平板、PDF批注、效率工具

## V2EX「分享创造」

### 标题

[分享创造] PaperNote：支持 Windows 和 Android 的本地优先手写笔记应用

### 正文

最近完成了 PaperNote 的 Android 客户端和跨平台数据层。

PaperNote 是一款本地优先的手写笔记与 PDF 批注应用。Windows 版适合触控电脑和数位板，Android 版适配手机和平板，并使用原生触控输入采集压力、倾角和触控笔橡皮端。两个平台可以通过 `.papernote` 文件和整库备份交换数据。

目前支持资料库、搜索、回收站、分页和模板、钢笔/荧光笔/橡皮擦、撤销重做、文本/图片/形状、PDF 文本搜索/目录/内部链接/批注列表、页面范围导出与提取，以及本地自动保存。Android 默认可直接手指书写，也可关闭手指书写后用单指移动；双指始终可缩放和平移。

技术栈为 .NET 10、WPF、.NET MAUI 和 Android 原生 MotionEvent。仓库包含不操控用户鼠标的 Windows 隐藏界面测试，以及 ADB 驱动的 Android 安装、书写、页面、返回键和持久化测试。

当前没有账号云同步、多人协作和内置扫描 OCR。自带文本层的 PDF 可离线搜索；大型 PDF 已支持进度、取消和续接。希望获得不同触控笔设备、低内存手机、500 页/200 MB 真实讲义和首次使用体验方面的反馈。

项目与下载地址：发布时粘贴 GitHub / Gitee 地址。

## 知乎 / 掘金 / CSDN

### 标题

从 WPF 到 Android：PaperNote 如何共享笔记数据与跨平台墨迹

### 内容提纲

1. 为什么选择本地优先和文件交换；
2. 从桌面单体到 `PaperNote.Core` 的拆分；
3. ISF 与 PaperInk 双格式兼容；
4. Android MotionEvent 的压力、倾角和橡皮端；
5. 手机和平板响应式页面导航；
6. 自动保存并发问题和返回键异常修复；
7. 后台自动化测试与签名 APK 发布；
8. 后续性能、可访问性和同步方向。

## 小红书 / 即刻 / 朋友圈

### 短版

PaperNote 现在支持 Windows 和 Android：触控笔书写、几何规整、混合选区 PNG、分页模板、对象与图层、PDF 文本搜索/目录/内部链接/批注列表、页面范围导出与提取、本地录音波形与笔迹同步回放、恢复中心和带校验的备份都能离线使用。电脑与手机/平板可以直接传 `.papernote` 文件，不需要注册账号。源码和安装包已放到 GitHub / Gitee。

### 更新版

PaperNote Android 1.0.0：

- 手机和平板响应式界面
- 原生触控笔压力书写
- 单指平移、双指缩放
- PDF 页码范围导入、进度、取消续接、文本搜索、目录、内部链接、批注列表和范围导出/提取
- 直线/45°/矩形/椭圆规整与混合选区 PNG 分享
- 与 Windows 交换笔记文件
- 本地录音波形、分段跳转、播放笔迹高亮、草稿恢复与损坏文件抢救
- 本地保存，无内置网络权限

项目地址与 APK：发布时补充。

## 英文仓库简介

Local-first handwriting and PDF study app for Windows and Android, with offline text search, outlines, internal links, annotations and page extraction. Built with .NET 10, WPF and .NET MAUI.

## 推荐 Topics

`android` `windows` `dotnet` `dotnet-maui` `wpf` `handwriting` `stylus` `pdf-annotation` `offline-first` `note-taking`

## Implementation status — 2026-07-23

本节是当前离线版本能力边界的统一说明。

### 已实现并纳入仓库测试

- Windows 与 Android 共用 PaperInk 和页面对象模型；支持压力、倾角、平滑、整笔/局部擦除、透明度、图层及自由形状混合套索。
- 页面支持复制、删除、移动到开头/末尾、书签、PDF 背景旋转，以及按页码范围导出 PDF 或提取为新的 `.papernote`；提取时会生成新 ID 并重映射保留下来的内部链接。
- Windows 与 Android 的 PDF 导入支持 200 MB/500 页边界、逐页进度、取消、完整 SHA-256 内容指纹缓存、失败续接和旧缓存清理；原 PDF 始终不被修改。
- 对带文本层的 PDF，会在本机离线提取并保存页面文本、原 PDF 目录/书签和内部 GoTo 链接；Windows 与 Android 均可搜索文本、浏览目录并跳转到已导入的目标页。
- PDF 学习工作流包含统一批注列表、类型/颜色筛选和页面文字评论；评论、PDF 文本、目录和链接都保存在 `.papernote` 中，可随 Windows/Android 文件往返。
- 离线搜索覆盖笔记/页面标题、标签、文本对象、PDF 文本、文字评论、来源名称以及已保存的 OCR/手写识别结果。
- 保存采用“临时文件写入并验证后替换”；Windows 与 Android 均提供恢复中心、只读损坏检查和另存抢救副本。
- 整库备份格式 3 包含笔记、历史版本和音频附件，并校验长度与 SHA-256；页面级本地录音支持播放、暂停、重命名、删除、时间标记、本地波形、分段跳转和播放笔迹高亮。
- 当前 `.papernote` 数据格式版本为 17；加载器会修复常见无效 ID、坐标、图层引用、链接、目录、评论和波形数据。
- 后台回归覆盖 Core、真实 PDF 内容提取、格式 17 存储往返、几何规整、混合选区 PNG、录音波形、播放笔迹高亮、Windows 隐藏 UI、Android Release/AOT 与 APK 静态检查；测试过程不操控用户鼠标。

### 当前明确边界

- PDF 文本搜索依赖原文件自带文本层；扫描版 PDF 和图片尚未内置离线 OCR，因此只能作为页面图像批注。
- PDF 内部链接只有目标页也已导入到当前笔记时才能跳转；未导入目标会明确提示。
- 当前可搜索 PDF 文本，但尚未提供逐字选择、复制原文或直接修改原 PDF 的“原生文本高亮”；高亮仍以 PaperNote 独立批注保存。
- 尚未包含真正的离线 OCR、手写转文字、数学/LaTeX 识别、完整标尺和更高级的几何构造工具。
- 录音已包含本地波形、分段跳转和播放笔迹高亮；麦克风设备选择、质量设置及点击笔迹反向跳转仍待完善。
- PDF 可编辑表单、数字签名、测量、合并多个源 PDF、双页阅读和分屏仍未实现；200 MB/500 页边界仍需更多低内存 Android 真机长期压力验证。
- 完整屏幕阅读器语义、高对比度专项适配和本地插件机制仍待完善；笔记级本地密码保护已完成。
- 账号、云同步、联网 AI、遥测、广告和多人协作不在离线版本范围内。
- APK、ZIP、签名密钥、构建输出和私人笔记属于发布产物或本地数据，不提交到源码仓库。

### 验证入口

```powershell
.\scripts\test.ps1 -SkipAndroidRuntime
.\scripts\build-release.ps1 -SkipTests
.\scripts\build-android.ps1
.\scripts\test-android.ps1 -SkipBuild -SkipUi
```


## 可用于平台介绍的密码保护文案

**单本笔记本地密码保护**：Windows 与 Android 共用离线加密格式，可为重要笔记设置密码；页面、笔迹、PDF 内容和录音信息加密保存在本机，自动保存、历史版本与整库备份会保持保护状态。密码不会上传或保存，忘记后无法找回。
