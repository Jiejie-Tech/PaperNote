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

所有核心笔记数据默认保存在设备本地，不需要账号。Android 客户端不申请网络权限。当前版本暂不包含账号云同步、多人协作和 OCR。

项目源码：GitHub / Gitee 搜索 PaperNote
下载位置：项目 Releases 中的 Windows 包与 Android APK

推荐标签：开源软件、手写笔记、Android、Windows、触控笔、平板、PDF批注、效率工具

## V2EX「分享创造」

### 标题

[分享创造] PaperNote：支持 Windows 和 Android 的本地优先手写笔记应用

### 正文

最近完成了 PaperNote 的 Android 客户端和跨平台数据层。

PaperNote 是一款本地优先的手写笔记与 PDF 批注应用。Windows 版适合触控电脑和数位板，Android 版适配手机和平板，并使用原生触控输入采集压力、倾角和触控笔橡皮端。两个平台可以通过 `.papernote` 文件和整库备份交换数据。

目前支持资料库、搜索、回收站、分页和模板、钢笔/荧光笔/橡皮擦、撤销重做、文本/图片/形状、PDF 导入批注与导出，以及本地自动保存。Android 默认单指移动、双指缩放，也可在设置中开启手指书写。

技术栈为 .NET 10、WPF、.NET MAUI 和 Android 原生 MotionEvent。仓库包含不操控用户鼠标的 Windows 隐藏界面测试，以及 ADB 驱动的 Android 安装、书写、页面、返回键和持久化测试。

当前没有账号云同步、多人协作和 OCR。希望获得不同触控笔设备、长笔记性能、大型 PDF 和首次使用体验方面的反馈。

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

PaperNote 现在支持 Windows 和 Android 了：触控笔书写、分页模板、PDF 批注、文本图片形状、搜索和备份都能离线使用。电脑与手机/平板可以直接传 `.papernote` 文件，不需要注册账号。源码和安装包已放到 GitHub / Gitee。

### 更新版

PaperNote Android 1.0.0：

- 手机和平板响应式界面
- 原生触控笔压力书写
- 单指平移、双指缩放
- PDF 导入批注和导出
- 与 Windows 交换笔记文件
- 本地保存，无内置网络权限

项目地址与 APK：发布时补充。

## 英文仓库简介

Local-first handwriting and PDF annotation app for Windows and Android, built with .NET 10, WPF and .NET MAUI.

## 推荐 Topics

`android` `windows` `dotnet` `dotnet-maui` `wpf` `handwriting` `stylus` `pdf-annotation` `offline-first` `note-taking`
