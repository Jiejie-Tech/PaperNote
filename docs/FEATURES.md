# PaperNote 功能清单

> 当前版本：1.0.0 · 数据格式：15 · 平台：Windows、Android

## 资料库

- 新建、打开、重命名和删除笔记本
- 最近笔记与标题搜索
- 回收站恢复和永久删除
- 本地自动保存
- 单笔记导入导出
- 整库备份与恢复

## 手写与画布

- 钢笔和荧光笔
- 颜色与粗细调整
- 整笔橡皮擦
- 撤销与重做
- 触控笔压力输入
- Android 触控笔倾角和橡皮端识别
- 页面平移与双指缩放
- 手机端默认手指书写，可在编辑器工具栏或设置中切换
- PaperInk 跨平台墨迹
- Windows ISF 兼容保留

## 页面

- 新增、复制、删除和重命名
- 缩略图、页码与快速跳转
- 多种纸张模板
- 手机底部页面入口
- 平板和桌面侧栏

## 页面对象

- 文本
- 图片
- 直线
- 箭头
- 矩形
- 圆形
- 三角形
- 菱形
- 星形

## PDF

- 导入 PDF 为页面背景
- 在 PDF 页面上书写和添加对象
- 导出包含批注的扁平 PDF

## 平台能力

### Windows

- 完整 WPF 桌面界面
- 适配鼠标、触控和数位笔
- 本地资料库与工作区恢复
- 隐藏窗口后台 UI 回归测试

### Android

- Android 6.0（API 23）及以上
- 手机和平板响应式布局
- 原生 MotionEvent 墨迹采集
- 系统文件选择器导入导出
- 无 INTERNET 权限
- 四种 ABI：ARMv7、ARM64、x86、x64

## 暂未包含

- 账号和云同步
- 多人实时协作
- OCR 与全文手写识别
- 音频录制与时间轴
- iOS/macOS 客户端

后续方向见 [ROADMAP.md](../ROADMAP.md)。

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
