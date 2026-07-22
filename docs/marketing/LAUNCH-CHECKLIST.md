# PaperNote 发布宣传检查清单

## 发布包

- [ ] Windows Release 压缩包可以在干净电脑启动
- [ ] Android APK 与 Android ZIP 已通过签名验证/完整性验证并记录 SHA-256
- [ ] 覆盖安装不会意外清除用户数据
- [ ] 下载文件名、版本号和更新日志一致
- [ ] 发布签名已在仓库外安全备份

## 仓库

- [ ] `scripts/test.ps1` 全部通过
- [ ] GitHub 与 Gitee 指向同一提交
- [ ] README 同时说明 Windows 和 Android
- [ ] Android 安装、数据位置和卸载前备份提醒清楚
- [ ] 没有密钥、密码、私人笔记、个人绝对路径或调试产物
- [ ] LICENSE、第三方许可、隐私和安全文档完整

## 素材

- [ ] Windows 截图来自真实应用
- [ ] Android 手机和平板截图来自真实应用
- [ ] 图片不含姓名、文件路径、通知或私人笔记
- [ ] 视频中的版本和界面与发布包一致
- [ ] 封面在移动端缩略图尺寸下仍可读

## 文案

- [ ] 只描述已验证功能
- [ ] 明确当前没有账号云同步、多人协作和 OCR
- [ ] 不使用来源混淆、比较式或绝对化措辞
- [ ] 下载位置同时包含 Windows 包、Android ZIP 和可选的 Android APK
- [ ] 提醒 Android 用户卸载前备份

## 发布后

- [ ] 从公开页面重新下载并核对 SHA-256
- [ ] 安装公开 APK 做一次新建、书写、关闭和重启测试
- [ ] 收集设备型号、Android 版本和触控笔兼容性反馈
- [ ] 为已确认问题建立 Issue 并更新已知限制

## Implementation status — 2026-07-22

This section is the source of truth for the current offline scope.

### Implemented and covered by repository tests

- Cross-platform PaperInk stores pressure, tilt, smoothing, partial/stroke erasing, opacity, and layer membership.
- Android supports rectangle lasso, multi-object selection, move, resize, rotate, duplicate, delete, group/ungroup, z-order, lock, and batch style changes.
- Page layers support create, activate, show/hide, lock, opacity, rename, merge, and delete-with-content-migration. Hidden content remains in the document.
- Text, image, and shape objects preserve rotation, opacity, lock, hidden, group, and layer fields across serialization.
- Offline search indexes notebook/page titles, tags, text objects, stored OCR text, stored handwriting-recognition text, and source names.
- Saving writes and parses a temporary document before replacing the live file. Library backup format 2 records file length and SHA-256 and verifies every entry before import.
- Notebook format version is 15 and migration preserves legacy ISF/PaperInk and pages created before layers existed.
- Windows thumbnails and object overlays, plus the Android renderer, honor layer visibility and effective opacity.

### Scope boundaries

- OCR and handwriting-recognition result fields are searchable, but the repository does not currently bundle an OCR or recognition engine.
- Audio timeline and cue data models exist; recording capture and player UI are not yet complete.
- Accounts, cloud sync, network AI, telemetry, advertising, and multi-user collaboration are intentionally out of scope.
- APK, ZIP, signing keys, build output, and private notes are release artifacts or local data and are not committed.

### Verification

```text
dotnet run --project tests/PaperNote.Core.Tests/PaperNote.Core.Tests.csproj -c Release
dotnet run --project tests/SmokeTest/SmokeTest.csproj -c Release
dotnet run --project tests/BackgroundUiTest/BackgroundUiTest.csproj -c Release
scripts/build-android.ps1
scripts/test-android.ps1 -SkipUi
```
