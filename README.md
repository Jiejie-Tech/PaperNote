<div align="center">

<img src="docs/brand/papernote-avatar.png" width="96" alt="PaperNote 图标">

# PaperNote

### 把手写、讲义和灵感，安静地留在自己的设备里

面向 Windows 笔记本电脑、触控设备和 Android 手机/平板的本地优先手写笔记应用。

<p>
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-3157D5?style=flat-square&logo=windows11&logoColor=white">
  <img alt="Android 6.0+" src="https://img.shields.io/badge/Android-6.0%2B-168B80?style=flat-square&logo=android&logoColor=white">
  <img alt="本地优先" src="https://img.shields.io/badge/%E6%9C%AC%E5%9C%B0%E4%BC%98%E5%85%88-%E6%97%A0%E9%9C%80%E8%B4%A6%E5%8F%B7-0E7C72?style=flat-square">
  <img alt="MIT License" src="https://img.shields.io/badge/License-MIT-103134?style=flat-square">
</p>

**[GitHub 下载](https://github.com/Jiejie-Tech/PaperNote/releases)** · **[Gitee 下载](https://gitee.com/aa20234350104/paper-note/releases)** · **[使用指南](docs/USER-GUIDE.md)** · **[功能全览](docs/FEATURES.md)**

</div>

![PaperNote 产品概览](docs/brand/product-hero.png)

## 一个更专注的数字笔记工作台

PaperNote 把资料库、分页纸张、自然手写和 PDF 批注放在同一个工作区里。无需注册账号，打开应用就能开始记录；笔记默认保存在设备本地，也可以通过 `.papernote` 文件、导出文件和整库备份在电脑与 Android 设备之间传递。

| ✍️ 自然书写 | 📚 分页整理 | 📄 PDF 批注 |
| --- | --- | --- |
| 钢笔、荧光笔、压力输入、倾角采样、整笔/局部橡皮擦、图层与撤销重做。 | 新建、复制、删除、重命名、缩略图跳转和多种纸张模板。 | 导入 PDF 作为页面背景，在页面上书写并导出包含批注的 PDF。 |
| **🧩 丰富内容** | **💻 Windows + Android** | **🔒 本地优先** |
| 文本、图片、直线、箭头、矩形、圆形、三角形、菱形和星形。 | 桌面端适配鼠标、触控和数位笔；移动端适配手机、平板与触控笔。 | 不要求账号，不包含广告和遥测；Android 客户端不申请网络权限。 |

## 产品实拍

### Windows 资料库

集中管理最近笔记、搜索结果、回收站和本地文件，让桌面上的资料保持清晰。

![PaperNote Windows 资料库](docs/screenshots/desktop-library.png)

### Windows 编辑器

在宽屏工作区中同时使用手写、页面管理、文本、图片、形状和 PDF 批注工具。

![PaperNote Windows 编辑器](docs/screenshots/desktop-editor.png)

### Android 手机与平板

移动端保留核心书写与阅读流程：手机端默认可直接用手指书写，两排固定工具栏完整显示钢笔、荧光笔、橡皮擦、平移、粗细、颜色、手指开关、撤销和重做，并支持触控笔、双指缩放、对象编辑、图层、本地录音、恢复中心、导入导出和备份。大笔迹页面会按当前视口绘制，减少无关笔迹扫描。

<p align="center">
  <img src="docs/screenshots/android-editor.png" width="300" alt="PaperNote Android 编辑器">
</p>

## 适合这些场景

- **课堂与自习**：按课程建立笔记本，在讲义或 PDF 上直接标记重点。
- **会议与项目**：用分页整理讨论记录、草图、流程和后续事项。
- **数位板书写**：在 Windows 笔记本或台式机上使用数位笔完成长时间记录。
- **跨设备阅读**：把 `.papernote` 文件传到 Android 手机或平板，继续查看和编辑。
- **离线资料管理**：不依赖账号或内置云服务，自行决定文件和备份放在哪里。

## 下载与安装

| 平台 | 系统要求 | 获取方式 | 安装说明 |
| --- | --- | --- | --- |
| Windows | Windows 10/11 x64 | [GitHub Releases](https://github.com/Jiejie-Tech/PaperNote/releases) / [Gitee Releases](https://gitee.com/aa20234350104/paper-note/releases) | 下载 Windows 压缩包，解压后运行 `PaperNote.Desktop.exe`。 |
| Android | Android 6.0（API 23）及以上 | [GitHub Releases](https://github.com/Jiejie-Tech/PaperNote/releases) / [Gitee Releases](https://gitee.com/aa20234350104/paper-note/releases) | 推荐下载 Android ZIP，解压后安装其中的 APK；也可直接下载 APK。仅为当前文件来源临时授权“安装未知应用”。 |

> 正式安装包应从 Releases 页面获取。下载后可结合发布页提供的 SHA-256 校验值确认文件完整性。

Android 的权限、手势、导入导出和卸载前备份说明见 [Android 使用指南](docs/ANDROID.md)。

## 数据属于你

- 笔记默认保存在设备本地，无需创建账户。
- 单个笔记本使用开放归档结构的 `.papernote` 文件。
- Windows 和 Android 共用 PaperInk 墨迹与页面对象模型。
- 支持单笔记导入导出、带 SHA-256 校验的整库备份与恢复。
- 启动时可恢复较新的临时草稿；恢复中心可以另存损坏文件中的可读内容且不覆盖原文件。
- 录音附件与笔记一起留在本地资料库，并纳入整库备份。
- Android 应用私有数据会随卸载被系统删除，卸载前请先导出或备份。
- 当前不提供内置云同步；你可以自行使用网盘、数据线或局域网工具传递文件。

更多兼容性细节见 [数据格式说明](docs/DATA-FORMAT.md)，隐私承诺见 [隐私说明](PRIVACY.md)。

## 文档中心

| 文档 | 内容 |
| --- | --- |
| [用户指南](docs/USER-GUIDE.md) | Windows 与 Android 的资料库、编辑器、页面、PDF、搜索与备份操作 |
| [Android 指南](docs/ANDROID.md) | 安装、权限、手势、文件传递和数据安全 |
| [功能全览](docs/FEATURES.md) | 当前已经实现的功能与尚未包含的能力 |
| [数据格式](docs/DATA-FORMAT.md) | `.papernote`、PaperInk、备份格式和跨平台兼容性 |
| [Android 构建](docs/BUILD-ANDROID.md) | Android 开发环境、编译、签名和设备测试 |
| [发布指南](docs/RELEASE.md) | Windows 与 Android 安装包的验证和发布流程 |
| [开发路线](ROADMAP.md) | 后续版本方向与优先级 |
| [参与贡献](CONTRIBUTING.md) | 开发约定、提交要求和协作方式 |

<details>
<summary><strong>从源码构建</strong></summary>

### 环境要求

- Windows 10/11
- .NET SDK `10.0.302`
- 构建 Android 客户端时需要对应的 .NET for Android / MAUI 工作负载与 Android SDK

### 获取与验证

```powershell
git clone https://github.com/Jiejie-Tech/PaperNote.git
cd PaperNote
dotnet restore PaperNote.sln
.\scripts\test.ps1 -SkipAndroidRuntime
```

有可用 Android 模拟器或设备时，可运行完整后台验证：

```powershell
.\scripts\test.ps1
```

生成 Windows 发布包、Android ZIP/APK 和签名文件前，请阅读 [发布指南](docs/RELEASE.md)。密钥、密码、真实笔记和构建产物不得提交到仓库。

</details>

## 参与 PaperNote

欢迎提交问题、改进文档或贡献代码。在开始较大的功能改动前，建议先阅读 [贡献指南](CONTRIBUTING.md) 和 [开发路线](ROADMAP.md)，并通过 Issue 说明使用场景和预期行为。

如果 PaperNote 对你有帮助，欢迎点亮 Star，让更多需要本地手写笔记的人发现它。

## 许可

PaperNote 使用 [MIT License](LICENSE) 开源。第三方组件及字体许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## Implementation status — 2026-07-22

本节是当前离线版本能力边界的统一说明。

### 已实现并纳入仓库测试

- Windows 与 Android 共用 PaperInk；支持压力、倾角、平滑、整笔/局部擦除、透明度和图层归属。
- Android 支持矩形套索、多对象选择、移动、缩放、旋转、复制、删除、组合/取消组合、层级顺序、锁定和批量样式修改。
- 页面图层支持新增、激活、显隐、锁定、透明度、重命名、合并和删除时迁移内容；隐藏内容不会从文件中丢失。
- 文本、图片和形状对象的旋转、透明度、锁定、隐藏、组合和图层字段可跨平台保存。
- 离线搜索覆盖笔记/页面标题、标签、文本对象、已存 OCR 文本、已存手写识别文本和来源名称。
- 保存采用“临时文件写入并验证后替换”；启动时可恢复较新的临时草稿。Windows 与 Android 都提供恢复中心，可只读检查损坏文件并另存为抢救副本，原文件保持不变。
- 读取时会修复空或重复 ID、无效图层引用、非有限墨迹数值及异常录音时间数据。
- 大墨迹页面使用空间索引；Android 按可见视口绘制并用局部候选执行橡皮命中，避免每帧扫描全部笔迹。
- 整库备份格式 3 包含笔记、历史版本和音频附件，记录长度与 SHA-256，并在导入前检查重复路径、越界路径、大小和内容完整性。
- Windows 与 Android 均支持页面级本地录音、播放/暂停、重命名、删除、命名时间标记和书写时自动标记。Windows 使用 WAV，Android 使用 MPEG-4/AAC。
- 压力测试覆盖 10,000+ 笔迹空间查询，以及 60 页、4,800 笔迹、600 对象、录音标记和图层关系的重复保存往返。

### 当前明确边界

- OCR 和手写识别结果可以保存并搜索，但仓库尚未内置真正的离线 OCR、手写转文字或数学识别引擎。
- 尚未提供自由形状套索、几何吸附、自动形状识别、标尺和大批量墨迹样式修改。
- 录音暂不含波形视图、播放时笔迹高亮、麦克风设备选择和压缩质量控制。
- PDF 尚不含大型文档页面缓存、导入进度/取消、表单编辑、测量和文档内文字搜索。
- 完整屏幕阅读器语义、高对比度专项适配、加密设置界面和本地插件机制仍待完善。
- 账号、云同步、联网 AI、遥测、广告和多人协作不在离线版本范围内。
- APK、ZIP、签名密钥、构建输出和私人笔记属于发布产物或本地数据，不提交到源码仓库。

### 验证入口

```powershell
.\scripts\build-android.ps1
.\scripts\test.ps1 -SkipAndroidRuntime
```
