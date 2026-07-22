# PaperNote 用户指南

PaperNote 是一款本地优先的手写笔记与文档批注应用。Windows 与 Android 可以通过 `.papernote` 文件或整库备份交换数据。

## 1. 创建与打开笔记

- 在资料库点击“新建笔记本”。
- 输入标题后进入编辑器。
- 资料库支持按标题搜索、打开最近笔记和进入回收站。
- 删除的笔记先进入回收站，可恢复或永久删除。

## 2. 书写与导航

### Windows

- 使用触控笔、鼠标或触控输入书写。
- 可选择钢笔、荧光笔、橡皮擦并调整颜色和粗细。
- 支持撤销、重做、页面缩放与平移。

### Android

- 触控笔默认用于书写，支持压力和倾角信息；触控笔橡皮端会切换为橡皮擦。
- 手机端默认开启手指书写，选择钢笔或荧光笔后可直接用手指落笔；双指用于缩放和移动页面。
- 顶部两排工具栏完整显示笔型、粗细、颜色、手指开关、撤销和重做。
- 点击“手指：开/关”可快速切换：关闭时单指平移，开启时单指书写；该状态与“设置”同步。
- 使用系统返回键可从编辑器或设置页回到资料库；在资料库根页返回会将应用放到后台。

## 3. 页面管理

编辑器支持：

- 新增空白页或指定模板页；
- 复制、删除和重命名页面；
- 通过缩略图或页码快速跳转；
- 在手机底部页面面板或平板侧栏中管理页面。

## 4. 内容工具

- **钢笔**：普通不透明墨迹。
- **荧光笔**：半透明标记。
- **橡皮擦**：按整条笔画擦除。
- **文本**：插入并编辑文本内容。
- **图片**：从设备选择图片放入页面。
- **形状**：支持直线、箭头、矩形、圆形、三角形、菱形和星形。

## 5. PDF 工作流

1. 在资料库或编辑器选择“导入 PDF”。
2. PaperNote 将 PDF 页面作为笔记页面背景。
3. 在页面上书写、添加文本、图片或形状。
4. 选择“导出 PDF”生成包含批注的扁平化 PDF。

导出文件不会改变原始 PDF。

## 6. 保存与恢复

- 编辑过程会自动保存，也可手动保存。
- 快速切换笔记时，保存任务会绑定到正确的目标文件。
- 如果设备存储空间不足，导出和保存可能失败；请先释放空间并重试。

## 7. 导入、导出和备份

- **导出笔记**：生成单个 `.papernote` 文件，适合发送给另一台设备。
- **导入笔记**：选择 `.papernote` 文件加入本地资料库。
- **整库备份**：包含资料库中的笔记和相关状态，适合迁移或重装前保存。
- **恢复备份**：会把备份内容恢复到当前设备，请先确认现有数据是否需要另行备份。

## 8. 重要数据提醒

Android 笔记位于应用私有目录。卸载应用、清除应用数据或恢复出厂设置都可能删除这些内容。执行上述操作前，请务必导出重要笔记或创建整库备份，并把文件复制到其他位置。

## 9. 常见问题

### 手指画不出线

先确认已选择钢笔或荧光笔，并检查编辑器工具栏是否显示“手指：开”。若显示“手指：关”，点击该按钮即可恢复手指书写；也可在“设置”中开启。

### 触控笔没有压力变化

部分设备或触控笔不会向 Android 提供压力数据，PaperNote 会自动使用普通线宽。

### Windows 与 Android 页面显示略有差异

字体、PDF 渲染和平台绘图实现可能产生轻微差异，但笔记内容和跨平台墨迹数据会保留。

### 安装新版会丢失笔记吗

使用相同应用标识正常覆盖安装时通常会保留数据，但仍建议升级前创建备份。若卸载旧版后再安装，Android 会清除原应用私有数据。

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
