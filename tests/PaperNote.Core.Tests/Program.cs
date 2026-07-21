using System.Text.Json;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;
using PaperNote.Core.Services;

var root = Path.Combine(Path.GetTempPath(), $"papernote-core-tests-{Guid.NewGuid():N}");
try
{
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
    Assert(loaded.FormatVersion >= 14 && loaded.Pages[0].Ink.Strokes.Count == 2, "保存后应使用格式 14 并保留 PaperInk");
    Assert(NotebookContentService.TryMatch(loaded, "特征值", out var summary) && summary.Length > 0, "全局搜索应匹配文本对象");

    var backup = await storage.CreateBackupAsync(stored.FilePath);
    loaded.Title = "已更改";
    await storage.SaveAsync(loaded, stored.FilePath);
    var restored = await storage.RestoreBackupAsync(stored.FilePath, backup.FilePath);
    Assert(restored.Title == "跨平台课程笔记", "手动快照应可恢复");

    var package = new LibraryBackupPackageService();
    var packagePath = Path.Combine(root, "library.pnbak");
    var manifest = await package.ExportAsync(packagePath, storage.NotebooksDirectory, storage.BackupsDirectory);
    Assert(File.Exists(packagePath) && manifest.NotebookCount >= 1, "整库备份应包含笔记本");
    var importRoot = Path.Combine(root, "Imported");
    var import = await package.ImportAsync(packagePath, Path.Combine(importRoot, "Notebooks"), Path.Combine(importRoot, "Backups"));
    Assert(import.ImportedNotebooks >= 1, "整库备份应能恢复到新资料库");

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
