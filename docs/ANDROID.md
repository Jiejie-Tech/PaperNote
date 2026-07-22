# PaperNote Android 安装与使用

## 系统要求

- Android 6.0（API 23）或更高版本
- 建议预留至少 300 MB 可用空间，导入大型 PDF 时需要更多临时空间
- 支持手机和平板
- APK 包含 ARMv7、ARM64、x86 和 x64 ABI

## 获取与安装

正式发布页提供两种 Android 文件：

- 推荐下载：`PaperNote-Android-1.0.0.zip`，内含 APK、SHA-256 校验文件、安装说明和许可文件；
- 直接安装：`PaperNote-Android-1.0.0.apk`，适合已经明确需要单独 APK 的用户。

### 从压缩包安装

1. 下载并解压 `PaperNote-Android-1.0.0.zip`。
2. 在 Android 手机或平板上打开其中的 `PaperNote-Android-1.0.0.apk`。
3. 若系统阻止安装，只为当前文件管理器或浏览器临时开启“安装未知应用”。
4. 安装完成后关闭该来源的安装权限。
5. 可使用同目录的 `PaperNote-Android-1.0.0.apk.sha256` 校验 APK 完整性。

### 直接安装 APK

1. 下载 `PaperNote-Android-1.0.0.apk`。
2. 在文件管理器中打开 APK。
3. 若系统阻止安装，只为当前文件管理器或浏览器临时开启“安装未知应用”。
4. 安装完成后关闭该来源的安装权限。

首次启动后即可离线使用，无需注册。升级时请直接覆盖安装，不要先卸载旧版本。重要升级前仍建议先创建整库备份。

## 基本操作

### 资料库

- 点击“新建”创建笔记本。
- 点击笔记卡片进入编辑器。
- 使用搜索查找标题。
- 删除的笔记会进入回收站，可恢复或永久删除。

### 书写

- 手机端默认开启手指书写；选择钢笔或荧光笔后即可直接落笔，也支持触控笔书写。
- 支持压力输入；设备提供倾角信息时会一并记录。
- 触控笔橡皮端自动执行擦除。
- 编辑器顶部两排工具栏始终显示笔型、粗细、颜色、手指开关、撤销和重做。
- 使用撤销、重做修正操作。

### 手势

- 手指书写默认开启，单指直接书写。
- 点击工具栏“手指：开/关”可快速切换；关闭后单指用于移动页面。
- 双指始终可缩放和移动页面；也可选择“平移”工具专门导航。
- “设置”中的“允许手指书写”与编辑器工具栏保持同步。

### 页面与内容

- 手机通过底部页面入口管理页面。
- 平板横向空间足够时显示侧栏。
- 可新增、复制、删除、重命名和跳转页面。
- 可插入文本、图片和常用形状。

## 文件交换

- 导出 `.papernote` 后，可通过聊天工具、数据线、网盘或局域网传给 Windows/其他 Android 设备。
- 导入 `.papernote` 会在本地资料库创建笔记副本。
- PDF 可导入为页面背景，批注后再导出为普通 PDF。

PaperNote 本身不依赖网络，也不申请 INTERNET 权限；文件如何传输由用户选择的系统应用负责。

## 备份与卸载

Android 将资料库存放在应用私有目录。以下操作可能删除全部本地笔记：

- 卸载 PaperNote；
- 在系统设置中清除 PaperNote 数据；
- 恢复出厂设置；
- 使用部分系统清理工具删除应用数据。

执行这些操作前：

1. 打开“备份”；
2. 创建整库备份；
3. 使用系统文件选择器把备份保存到下载目录、移动存储或其他安全位置；
4. 最好在另一台设备上确认文件能够读取。

## 返回键行为

- 编辑器、搜索、设置、备份和回收站等子页面：返回资料库或上一页。
- 资料库根页：将应用移入后台，不触发异常退出。

## 故障排查

### 无法安装

确认 Android 版本符合要求、APK 下载完整，并为打开 APK 的应用授予临时安装权限。

### 导入文件后没有出现

返回资料库并刷新搜索条件；若文件损坏或格式不受支持，应用会显示错误提示。

### 大型 PDF 导入较慢

PDF 页面需要逐页转换。保持足够存储空间并等待处理完成，期间不要强制结束应用。

### 书写有明显延迟

关闭省电模式、减少后台高负载应用，并尝试降低页面缩放级别。低端设备处理大量笔画时可能需要拆分页面。

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
