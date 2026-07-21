# Android 构建指南

## 1. 环境

- Windows 10/11
- .NET SDK `10.0.302`
- .NET MAUI Android 工作负载
- Android SDK（目标 API 36）
- Java/JDK（由 Android 工作负载支持的版本）
- 可选：Android 模拟器或开启 USB 调试的真机

仓库根目录的 `global.json` 固定 SDK 版本。

## 2. 路径要求

Android 资源工具在包含非 ASCII 字符的路径下可能失败。仓库脚本会使用 ASCII 路径作为构建工作区。默认联接为：

```text
C:\PaperNoteWorkspace
```

不要把签名文件或密码放进仓库。

## 3. 安装工作负载

```powershell
dotnet workload install maui-android
```

如果已安装但环境损坏，可先运行：

```powershell
dotnet workload repair
```

## 4. 构建发布 APK

```powershell
.\scripts\build-android.ps1
```

脚本会：

1. 检查 SDK、Android 工具链和 ASCII 构建路径；
2. 在用户本地签名目录创建或读取发布密钥；
3. 构建 Release APK；
4. 使用 Android 工具验证签名；
5. 将最终 APK、SHA-256 和元数据复制到 `artifacts\android`。

首次创建密钥后应安全备份。以后若丢失该密钥，将无法用同一签名覆盖安装已发布的应用。

## 5. 后台设备测试

启动模拟器或连接设备后运行：

```powershell
.\scripts\test-android.ps1 -SkipBuild
```

测试使用 ADB 直接驱动应用，不占用用户鼠标。覆盖：

- 全新启动和资料库；
- 新建笔记与原生墨迹；
- 页面管理与形状插入；
- 设置中的手指书写选项；
- 子页面返回和根页退到后台；
- 重启后的内容持久化；
- logcat 中无致命异常。

失败时会保留 UI XML、截图和日志，便于定位；成功时会清理临时调试证据。

## 6. 完整回归

```powershell
.\scripts\test.ps1
```

该命令依次运行开源审计、解决方案构建、Core 测试、存储冒烟测试、隐藏 WPF UI 测试、Android 构建和设备测试。

没有可用设备时：

```powershell
.\scripts\test.ps1 -SkipAndroidRuntime
```

## 7. 发布安全

以下内容不得提交：

- `*.keystore`、`*.jks`；
- 签名密码或明文配置；
- 用户目录中的 DPAPI 密码文件；
- 临时调试数据库、私人笔记和测试导出内容；
- `bin`、`obj` 和本地构建缓存。

正式发布前保存 APK 的 SHA-256，并确认 v1/v2/v3 签名验证通过。

Windows 与 Android 同版本发布、覆盖升级和双托管平台核对流程见 [统一发布指南](RELEASE.md)。
