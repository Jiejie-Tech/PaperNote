<div align="center">

# PaperNote

### 本地优先的手写笔记与文档批注应用

面向 Windows 笔记本电脑、触控设备和 Android 手机/平板，支持手写、分页笔记、PDF 批注、跨设备文件传递与离线备份。

[功能说明](docs/FEATURES.md) · [用户指南](docs/USER-GUIDE.md) · [Android 指南](docs/ANDROID.md) · [发布指南](docs/RELEASE.md) · [开发路线](ROADMAP.md)

</div>

## 项目特点

- **本地优先**：无需账号，笔记默认保存在设备本地。
- **自然书写**：钢笔、荧光笔、压力输入、橡皮擦、撤销与重做。
- **页面自由组织**：新增、复制、删除、重命名、跳转和模板切换。
- **丰富内容**：文本、图片、常用形状、PDF 导入批注与导出。
- **跨平台文件**：Windows 与 Android 共用 `.papernote`、PaperInk 和备份格式。
- **适配大小屏**：Android 手机使用底部页面入口，平板使用侧栏布局。
- **离线可用**：应用本身不要求联网，不包含账号、广告或遥测服务。

## 支持平台

| 平台 | 状态 | 主要用途 |
| --- | --- | --- |
| Windows 10/11 x64 | 可用 | 完整桌面资料库、手写、PDF、页面和对象编辑 |
| Android 6.0+（API 23+） | 可用 | 手机/平板手写、浏览、批注、导入导出和备份 |

## 快速开始

### Windows

1. 从 Releases 下载 Windows 压缩包并解压。
2. 运行 `PaperNote.Desktop.exe`。
3. 在资料库中新建笔记本，进入编辑器开始书写。

### Android

1. 从 Releases 下载 `PaperNote-Android-1.0.0.apk`。
2. 在系统提示时，仅为当前文件来源授权“安装未知应用”。
3. 安装并打开 PaperNote。
4. 默认情况下，单指用于移动页面、双指用于缩放；如需手指书写，可在设置中开启。

详见 [Android 安装与使用](docs/ANDROID.md)。

## 数据与兼容性

- 单个笔记本文件扩展名为 `.papernote`。
- 当前数据格式版本为 **14**。
- Windows 保留原生 ISF 墨迹，同时写入跨平台 PaperInk 数据。
- Android 使用 PaperInk，Windows 可继续打开和编辑。
- Android 应用私有目录会随卸载被系统删除，卸载前请先导出笔记或整库备份。

详见 [数据格式说明](docs/DATA-FORMAT.md)。

## 从源码构建

需要 .NET SDK `10.0.302` 及相应工作负载。

```powershell
# 完整后台测试（包含已连接的 Android 模拟器或设备）
.\scripts\test.ps1

# 仅构建签名 Android APK
.\scripts\build-android.ps1

# 不运行 Android 设备测试
.\scripts\test.ps1 -SkipAndroidRuntime
```

Android 工具链对非 ASCII 路径兼容性有限；脚本会使用 ASCII 构建目录。详见 [Android 构建指南](docs/BUILD-ANDROID.md)。生成 Windows 与 Android 正式安装包时，请同时遵循 [统一发布指南](docs/RELEASE.md)。

## 安全与隐私

PaperNote 不要求注册登录，不内置网络权限，不上传笔记内容。请通过仓库的 Security 页面报告安全问题，不要公开包含私人笔记的样本文件。

## 参与贡献

欢迎提交问题、改进建议和代码贡献。开始前请阅读 [贡献指南](CONTRIBUTING.md) 与 [行为准则](CODE_OF_CONDUCT.md)。

## 许可证

项目代码按 [MIT License](LICENSE) 开源。第三方组件及其许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
