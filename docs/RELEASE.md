# PaperNote 发布指南

本文档用于从同一个已验证的提交生成 Windows 与 Android 公开安装包。开发构建说明见 [README](../README.md)，Android 工具链细节见 [Android 构建指南](BUILD-ANDROID.md)。

## 1. 发布前准备

1. 确认工作区干净，版本号、`CHANGELOG.md` 和用户文档已经同步。
2. 确认 Windows 与 Android 使用一致的公开版本号和兼容的数据格式；Android 的显示版本与递增版本号在 `src/PaperNote.Mobile/PaperNote.Mobile.csproj` 中维护。
3. 确认发布签名密钥及其密码只保存在仓库外，并至少有一份安全备份。
4. 不得使用 CI 临时密钥或新生成的替代密钥覆盖正式发布；签名密钥变化会导致 Android 用户无法直接升级。
5. 确认仓库中不包含真实笔记、测试备份、设备日志、密钥、密码或本机绝对路径。

## 2. 后台验证

有 Android 模拟器或已连接设备时运行完整验证：

```powershell
.\scripts\test.ps1
```

没有可用设备时可以先运行静态构建与 Windows 回归：

```powershell
.\scripts\test.ps1 -SkipAndroidRuntime
```

正式发布前仍应至少在一台 Android 设备或模拟器上完成一次 `scripts/test-android.ps1` 设备测试。

## 3. 生成 Windows 发布包

```powershell
.\scripts\build-release.ps1 -Version 1.0.0 -Runtime win-x64 -SkipTests
```

输出位于 `artifacts\releases`，包含 Windows 自包含压缩包和对应的 SHA-256 文件。`build-release.ps1` 只生成 Windows 包。

如需 Windows on Arm，可另外运行：

```powershell
.\scripts\build-release.ps1 -Version 1.0.0 -Runtime win-arm64 -SkipTests
```

## 4. 生成 Android 发布包

```powershell
.\scripts\build-android.ps1
```

脚本生成两组产物：

- `artifacts\android`：已签名 APK、APK SHA-256 和构建元数据；
- `artifacts\releases`：可直接分发的 Android ZIP 压缩包及 ZIP SHA-256。

压缩包内含 APK、APK 校验文件、安装说明、Android 使用指南、许可证、隐私说明、安全策略和第三方许可。Android 构建会使用 ASCII 工作目录，避免资源工具在包含非 ASCII 字符的路径下失败。

发布前必须确认：

- APK 使用长期保存的正式密钥签名；
- Android 签名验证通过；
- APK 的应用 ID 与版本信息正确；
- 在已安装上一公开版本的设备上可以覆盖升级；
- 升级后已有笔记仍可打开、编辑、导出和备份。

## 5. 发布检查

1. 比较 Windows 包、Android ZIP、Android APK 和源码标签，确认全部来自同一提交。
2. 记录并发布 Windows ZIP、Android ZIP 和 Android APK 的 SHA-256；不要只依赖文件名。
3. 在 GitHub 与 Gitee 使用一致的版本号、变更说明、安装说明和校验值。
4. 上传安装包前再次运行开源审计，并确认 `artifacts`、`dist`、签名文件和密码没有被提交。
5. 发布后从公开页面重新下载安装包，在干净环境中完成一次启动、创建笔记、保存、重启和导出检查。
6. Android 发布后再验证一次旧版本覆盖升级，不要向用户分发使用临时签名的 CI 包。

## 6. 回滚原则

- 已公开的 Android 版本号不得复用；修复后递增版本并重新发布。
- 不要删除用户可能依赖的数据兼容代码；必要时保留旧格式读取能力。
- 若安装包或签名存在问题，立即撤下受影响附件、保留说明，并发布使用相同正式签名的新版本。
- 若发现数据安全问题，按 [安全策略](../SECURITY.md) 处理，并避免在修复前公开可利用细节。
