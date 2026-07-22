# PaperNote 路线图

路线图表达当前方向，不构成发布时间承诺。

## 已完成

- Windows 本地资料库与分页手写
- 纸张模板、页面对象和 PDF 批注
- 搜索、回收站、自动保存、导入导出和整库备份
- 跨平台 `PaperNote.Core`
- PaperInk 跨平台墨迹
- Android 手机和平板客户端
- 触控笔压力、倾角和橡皮端支持
- Android 后台自动化测试与签名 APK 构建

## 近期

- 扩充真实设备兼容性测试矩阵
- 优化超长笔记和高笔画量页面性能
- 改进 Android 大型 PDF 导入进度与取消体验
- 增加崩溃恢复和备份完整性检查
- 提升键盘、屏幕阅读器和高对比度可访问性
- 完善发布自动化和可复现构建说明

## 中期

- 套索选择与跨平台对象变换统一
- 自定义模板管理
- 页面批量操作增强
- 可选 OCR 和文本索引
- 局域网或用户自选存储的同步方案研究

## 长期探索

- iOS/iPadOS 客户端
- macOS 客户端
- 插件与扩展机制
- 端到端加密的多设备同步

## 明确不做

- 强制账号体系
- 广告或出售笔记内容
- 默认上传用户数据

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
