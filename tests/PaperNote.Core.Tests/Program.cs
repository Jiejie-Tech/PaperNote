using System.IO.Compression;
using System.Text.Json;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;
using PaperNote.Core.Services;

var root = Path.Combine(Path.GetTempPath(), $"papernote-core-tests-{Guid.NewGuid():N}");
try
{
    var eraseDocument = new PaperInkDocument();
    var eraseStroke = new PaperInkStroke { Points = [new() { X = 0, Y = 0 }, new() { X = 10, Y = 0 }, new() { X = 20, Y = 0 }, new() { X = 50, Y = 0 }, new() { X = 80, Y = 0 }, new() { X = 90, Y = 0 }, new() { X = 100, Y = 0 }] };
    eraseDocument.Strokes.Add(eraseStroke);
    Assert(InkEditingService.ErasePartial(eraseDocument, 50, 0, 10) == 1 && eraseDocument.Strokes.Count == 2, "局部橡皮应将一条笔迹切成两段");
    var smoothStroke = new PaperInkStroke { Points = [new() { X = 0, Y = 0 }, new() { X = 10, Y = 20 }, new() { X = 20, Y = 0 }] };
    Assert(InkEditingService.SmoothStroke(smoothStroke) && smoothStroke.Points[1].Y < 20, "平滑应降低中间点的尖峰");

    var original = new PaperInkDocument
    {
        Strokes =
        [
            new PaperInkStroke
            {
                Id = Guid.NewGuid(), Tool = PaperInkTool.Pen, Color = "#3157D5", Width = 4.5,
                Opacity = 0.82, PressureEnabled = true,
                Points =
                [
                    new PaperInkPoint { X = 10.25, Y = 20.5, Pressure = 0.2, TiltX = -0.3, TiltY = 0.4, TimestampMilliseconds = 123 },
                    new PaperInkPoint { X = 80, Y = 90, Pressure = 0.9, TiltX = 0.2, TiltY = -0.1, TimestampMilliseconds = 456 }
                ]
            },
            new PaperInkStroke
            {
                Tool = PaperInkTool.Highlighter, Color = "#F0B429", Width = 18, Opacity = 0.35,
                Points = [new PaperInkPoint { X = 1, Y = 2, Pressure = 0.5 }]
            }
        ]
    };
    var roundTrip = PaperInkSerializer.Deserialize(PaperInkSerializer.Serialize(original));
    Assert(roundTrip.Strokes.Count == 2, "PaperInk 笔迹数量应往返一致");
    Assert(roundTrip.Strokes[0].Id == original.Strokes[0].Id, "PaperInk 应保留笔迹 ID");
    Assert(roundTrip.Strokes[0].Points[1].Pressure == 0.9, "PaperInk 应保留压感");
    Assert(roundTrip.Strokes[0].Points[0].TiltX == -0.3, "PaperInk 应保留倾角");
    Assert(roundTrip.Strokes[1].Tool == PaperInkTool.Highlighter && roundTrip.Strokes[1].Opacity == 0.35, "荧光笔属性应保留");

    var invalid = new PaperInkDocument
    {
        Strokes = [new PaperInkStroke { Width = -1, Opacity = 9, Color = "", Points = [new PaperInkPoint { Pressure = 5, TiltX = -9, TiltY = 9 }] }]
    };
    PaperInkSerializer.Normalize(invalid);
    Assert(invalid.Strokes[0].Width == 0.25 && invalid.Strokes[0].Opacity == 1, "非法墨迹宽度和透明度应被约束");
    Assert(invalid.Strokes[0].Points[0].Pressure == 1 && invalid.Strokes[0].Points[0].TiltX == -1, "非法压感和倾角应被约束");

    var page = new NotebookPage
    {
        Title = "原页面", Ink = original, InkData = "legacy-isf",
        Objects = [new PageObject { Kind = "Text", Text = "可检索文字", LinkTargetPageId = Guid.NewGuid() }]
    };
    var clone = page.Clone();
    Assert(clone.Id != page.Id && clone.Ink.Strokes[0].Id == page.Ink.Strokes[0].Id, "复制页面应生成新页面 ID 并保留笔迹身份");
    clone.Ink.Strokes[0].Points[0].X = 999;
    clone.Objects[0].Text = "已修改";
    Assert(page.Ink.Strokes[0].Points[0].X != 999 && page.Objects[0].Text == "可检索文字", "页面复制必须深拷贝墨迹和对象");

    var objectPage = new NotebookPage
    {
        Objects =
        [
            new PageObject { Kind = "Shape", ShapeKind = "Rectangle", X = 10, Y = 20, Width = 100, Height = 80 },
            new PageObject { Kind = "Text", Text = "组合对象", X = 150, Y = 50, Width = 160, Height = 90 }
        ]
    };
    Assert(PageObjectEditingService.HitTest(objectPage, 20, 30)?.Id == objectPage.Objects[0].Id, "对象命中测试应返回最上层命中对象");
    var group = PageObjectEditingService.Group(objectPage, objectPage.Objects.Select(item => item.Id));
    Assert(group.HasValue && objectPage.Objects.All(item => item.GroupId == group), "对象应可组合");
    var originalObjectIds = objectPage.Objects.Select(item => item.Id).ToArray();
    Assert(PageObjectEditingService.Move(objectPage, [originalObjectIds[0]], 30, 40) && objectPage.Objects[1].X == 180, "移动组合内任一对象应移动整组");
    Assert(PageObjectEditingService.Resize(objectPage, [originalObjectIds[0]], 600, 400), "对象组合应可按包围框缩放");
    Assert(PageObjectEditingService.Rotate(objectPage, [originalObjectIds[0]], 90) && objectPage.Objects.All(item => item.Rotation == 90), "对象组合应可整体旋转");
    var duplicates = PageObjectEditingService.Duplicate(objectPage, [originalObjectIds[0]]);
    Assert(duplicates.Count == 2 && objectPage.Objects.Count == 4 && objectPage.Objects.Skip(2).Select(item => item.GroupId).Distinct().Count() == 1, "复制组合应生成新的对象和组 ID");
    Assert(PageObjectEditingService.SetLocked(objectPage, duplicates, true), "复制对象应可锁定");
    Assert(!PageObjectEditingService.Delete(objectPage, duplicates), "锁定对象不应被删除");
    Assert(PageObjectEditingService.SetLocked(objectPage, duplicates, false) && PageObjectEditingService.Delete(objectPage, duplicates), "解锁后对象应可删除");
    Assert(PageObjectEditingService.Ungroup(objectPage, [originalObjectIds[0]]) && objectPage.Objects.All(item => item.GroupId is null), "对象应可取消组合");
    Assert(PageObjectEditingService.UpdateStyle(objectPage, originalObjectIds, strokeColor: "#FF0000", strokeThickness: 7, opacity: .5) && objectPage.Objects.All(item => item.StrokeColor == "#FF0000" && item.Opacity == .5), "对象样式应可批量更新");
    var storage = new NotebookStorageService(Path.Combine(root, "Notebooks"), Path.Combine(root, "Backups"));
    Directory.CreateDirectory(storage.NotebooksDirectory);
    var legacyPageId = Guid.NewGuid();
    var legacyPath = Path.Combine(storage.NotebooksDirectory, "legacy.papernote");
    await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(new
    {
        FormatVersion = 13,
        Id = Guid.NewGuid(),
        Title = "旧格式",
        CurrentPageId = legacyPageId,
        Pages = new[] { new { Id = legacyPageId, InkData = "old-isf" } }
    }));
    var legacy = await storage.LoadAsync(legacyPath);
    Assert(legacy.Pages.Count == 1 && legacy.Pages[0].Ink is not null && legacy.Pages[0].Ink.IsEmpty, "格式 13 应补齐空 PaperInk 并保留旧 ISF");
    Assert(legacy.Pages[0].InkData == "old-isf", "旧 ISF 字段不得在迁移时丢失");

    var document = NotebookDocument.Create("跨平台课程笔记");
    document.Pages[0].Ink = original.Clone();
    document.Pages[0].Objects.Add(new PageObject { Kind = "Text", Text = "矩阵分解与特征值" });
    var stored = await storage.CreateAsync(document);
    var loaded = await storage.LoadAsync(stored.FilePath);
    Assert(loaded.FormatVersion >= 15 && loaded.Pages[0].Ink.Strokes.Count == 2, "保存后应使用格式 15 并保留 PaperInk");
    Assert(NotebookContentService.TryMatch(loaded, "特征值", out var summary) && summary.Length > 0, "全局搜索应匹配文本对象");

    loaded.Tags = ["数学", "复习"];
    loaded.Pages[0].Tags = ["重点"];
    loaded.Pages[0].OcrText = "扫描文本 特征值";
    loaded.Pages[0].RecognizedText = "手写识别 矩阵";
    loaded.Pages[0].Objects.Add(new PageObject { Kind = "Text", Text = "矩阵分解" });
    var search = new OfflineSearchService();
    search.Index(loaded);
    Assert(search.Search("特征值").Any(hit => hit.Source == "OCR"), "离线搜索应命中 OCR 文本");
    Assert(search.Search("矩阵").Any(hit => hit.Source == "手写识别"), "离线搜索应命中手写识别文本");
    var secondPage = new NotebookPage { Title = "错题整理" };
    loaded.Pages.Add(secondPage);
    Assert(PageBatchService.Move(loaded, [secondPage.Id], 0) && loaded.Pages[0].Id == secondPage.Id, "页面批量移动应改变顺序");
    Assert(PageBatchService.SetBookmark(secondPage, true) && secondPage.IsBookmarked, "页面书签应可设置");
    var layer = PageLayerService.EnsureDefault(secondPage);
    var extraLayer = PageLayerService.Add(secondPage, "批注");
    secondPage.Objects.Add(new PageObject { Kind = "Shape", LayerId = extraLayer.Id });
    Assert(PageLayerService.SetVisibility(secondPage, extraLayer.Id, false) && !secondPage.Layers.Single(item => item.Id == extraLayer.Id).IsVisible, "图层可见性应可切换");
    Assert(PageLayerService.MergeInto(secondPage, extraLayer.Id, layer.Id) && secondPage.Objects.All(item => item.LayerId == layer.Id), "图层合并应迁移内容");
    var recording = AudioTimelineService.AddRecording(secondPage, "recordings/class.m4a", 10_000, "课堂录音");
    Assert(AudioTimelineService.AddCue(recording, 1_000, label: "开始") && AudioTimelineService.GetCues(secondPage, 1_100).Count == 1, "音频时间轴应支持 cue");
    var encryption = new NotebookEncryptionService();
    var encrypted = encryption.Encrypt(loaded, "correct horse battery");
    Assert(NotebookEncryptionService.IsEncrypted(encrypted) && encryption.Decrypt(encrypted, "correct horse battery").Title == loaded.Title, "笔记本加密应可往返");
    var digest = DocumentIntegrityService.ComputeSha256(encrypted);
    Assert(DocumentIntegrityService.Verify(encrypted, digest) && !DocumentIntegrityService.Verify(encrypted, digest[..^1] + "0"), "内容哈希应可校验");

    var backup = await storage.CreateBackupAsync(stored.FilePath);
    loaded.Title = "已更改";
    await storage.SaveAsync(loaded, stored.FilePath);
    var restored = await storage.RestoreBackupAsync(stored.FilePath, backup.FilePath);
    Assert(restored.Title == "跨平台课程笔记", "手动快照应可恢复");

    var package = new LibraryBackupPackageService();
    var packagePath = Path.Combine(root, "library.pnbak");
    var manifest = await package.ExportAsync(packagePath, storage.NotebooksDirectory, storage.BackupsDirectory);
    Assert(manifest.FormatVersion == 2 && manifest.Files.Count >= manifest.NotebookCount && manifest.Files.All(item => item.Sha256.Length == 64), "Backup manifest should contain SHA-256 hashes");
    Assert(File.Exists(packagePath) && manifest.NotebookCount >= 1, "整库备份应包含笔记本");
    var importRoot = Path.Combine(root, "Imported");
    var import = await package.ImportAsync(packagePath, Path.Combine(importRoot, "Notebooks"), Path.Combine(importRoot, "Backups"));
    Assert(import.ImportedNotebooks >= 1, "整库备份应能恢复到新资料库");

    var tamperedPath = Path.Combine(root, "library-tampered.pnbak");
    File.Copy(packagePath, tamperedPath);
    using (var archive = ZipFile.Open(tamperedPath, ZipArchiveMode.Update))
    {
        var entry = archive.Entries.First(item => item.FullName.StartsWith("notebooks/", StringComparison.OrdinalIgnoreCase));
        byte[] bytes;
        using (var input = entry.Open()) { using var memory = new MemoryStream(); input.CopyTo(memory); bytes = memory.ToArray(); }
        var path = entry.FullName;
        entry.Delete();
        bytes[bytes.Length / 2] ^= 0x01;
        var replacement = archive.CreateEntry(path);
        using var output = replacement.Open(); output.Write(bytes);
    }
    var rejected = false;
    try { await package.ImportAsync(tamperedPath, Path.Combine(root, "Tampered", "Notebooks"), Path.Combine(root, "Tampered", "Backups")); }
    catch (InvalidDataException) { rejected = true; }
    Assert(rejected, "Tampered backup package must be rejected before import");

    Console.WriteLine("PAPERNOTE CORE TESTS PASS");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
