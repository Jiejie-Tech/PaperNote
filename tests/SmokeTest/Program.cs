using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaperNote.Core.Ink;
using PaperNote.Core.Models;
using PaperNote.Core.Services;
using PaperNote.Desktop.Services;
using SkiaSharp;

internal static class Program
{
    [STAThread]
    private static async Task Main()
    {
        Assert(PageThumbnailService.Deserialize("not-base64").Count == 0, "非法 Base64 应退化为空白页");
        Assert(PageThumbnailService.Deserialize(Convert.ToBase64String([1, 2, 3, 4, 5])).Count == 0, "损坏 ISF 应退化为空白页");
        Assert(PageThumbnailService.ParsePaperColor("bad-color") == Colors.White, "非法纸张颜色应回退为白色");
        Assert(NotebookStorageService.NormalizeCoverStyle("unknown") == "Blue", "非法封面应回退为蓝色");
        Assert(NotebookStorageService.NormalizeFolderName("  课程  ") == "课程", "文件夹名称应清理首尾空格");
        Assert(NotebookStorageService.NormalizePageTitle("  第一章  ") == "第一章", "页面标题应清理首尾空格");
        Assert(NotebookStorageService.NormalizePageTitle(new string('长', 80)).Length == 60, "页面标题最多保留 60 个字");
        Assert(PdfImportService.ParsePageSelection("1-3,5,8-10", 10).SequenceEqual(new[] { 1, 2, 3, 5, 8, 9, 10 }), "PDF 页码范围应正确展开。");
        Assert(PdfImportService.ParsePageSelection("1，2；2,4", 5).SequenceEqual(new[] { 1, 2, 4 }), "PDF 页码选择应支持中文分隔符并去重。");
        Assert(PdfImportService.ParsePageSelection("全部", 3).SequenceEqual(new[] { 1, 2, 3 }), "PDF 全部页预设应正确展开。");
        AssertThrows<InvalidDataException>(() => PdfImportService.ParsePageSelection("3-1", 3), "反向 PDF 页码范围应被拒绝。");
        AssertThrows<InvalidDataException>(() => PdfImportService.ParsePageSelection("0", 3), "PDF 页码 0 应被拒绝。");
        AssertThrows<InvalidDataException>(() => PdfImportService.ParsePageSelection("4", 3), "超出 PDF 总页数的页码应被拒绝。");

        var pdfTestPath = Path.Combine(Path.GetTempPath(), $"papernote-pdf-import-{Guid.NewGuid():N}.pdf");
        IReadOnlyList<ImportedPdfPage> importedPdfPages;
        try
        {
            CreateTestPdf(pdfTestPath);
            Assert(PdfImportService.GetPageCount(pdfTestPath) == 3, "PDF page count should be read before range selection.");
            importedPdfPages = PdfImportService.Import(pdfTestPath);
            Assert(importedPdfPages.Count == 3, "PDF import should return all three pages.");
            Assert(importedPdfPages.Select(item => item.PageNumber).SequenceEqual(new[] { 1, 2, 3 }), "PDF import page numbers are incorrect.");
            Assert(importedPdfPages.All(item => PageThumbnailService.DecodeImageData(item.ImageData) is BitmapSource { PixelWidth: 840, PixelHeight: 1188 }), "Imported PDF pages should be self-contained 840 x 1188 PNG backgrounds.");
            var selectedPdfPages = PdfImportService.Import(pdfTestPath, new[] { 2, 3 });
            Assert(selectedPdfPages.Select(item => item.PageNumber).SequenceEqual(new[] { 2, 3 }), "Selected PDF import should preserve requested page numbers.");
            Assert(selectedPdfPages[0].ImageData == importedPdfPages[1].ImageData && selectedPdfPages[1].ImageData == importedPdfPages[2].ImageData, "Selected PDF rendering should use the correct zero-based source page indexes.");
        }
        finally
        {
            if (File.Exists(pdfTestPath)) File.Delete(pdfTestPath);
        }
        Assert(!File.Exists(pdfTestPath), "PDF import test should not leave a source file behind.");

        var storageTestRoot = Path.Combine(Path.GetTempPath(), $"papernote-storage-smoke-{Guid.NewGuid():N}");
        var testService = new NotebookStorageService(Path.Combine(storageTestRoot, "Notebooks"), Path.Combine(storageTestRoot, "Backups"));
        Directory.CreateDirectory(testService.NotebooksDirectory);

        var legacyFirstPageId = Guid.NewGuid();
        var legacySecondPageId = Guid.NewGuid();
        var sampleInkData = PageThumbnailService.Serialize(new StrokeCollection
        {
            new Stroke(new StylusPointCollection
            {
                new StylusPoint(40, 40),
                new StylusPoint(180, 140),
                new StylusPoint(320, 80)
            })
        });
        var legacyStrokes = PageThumbnailService.Deserialize(sampleInkData);
        legacyStrokes[0].DrawingAttributes.Color = Color.FromRgb(49, 87, 213);
        legacyStrokes[0].DrawingAttributes.Width = 5;
        legacyStrokes[0].DrawingAttributes.Height = 5;
        var portableInk = WpfInkAdapter.ToPaperInk(legacyStrokes);
        Assert(portableInk.Strokes.Count == 1 && portableInk.Strokes[0].Points.Count == 3, "WPF ISF should convert to PaperInk");
        Assert(portableInk.Strokes[0].Color == "#3157D5" && portableInk.Strokes[0].Width == 5, "WPF ink style should survive PaperInk conversion");
        var restoredWpf = WpfInkAdapter.ToStrokeCollection(portableInk);
        Assert(restoredWpf.Count == 1 && restoredWpf[0].StylusPoints.Count == 3, "PaperInk should convert back to WPF strokes");
        Assert(restoredWpf[0].DrawingAttributes.Color.R == 49 && restoredWpf[0].DrawingAttributes.Width == 5, "PaperInk style should convert back to WPF");
        var portableOnlyPage = new NotebookPage { Ink = portableInk.Clone(), InkData = string.Empty };
        Assert(WpfInkAdapter.GetPageStrokes(portableOnlyPage).Count == 1, "Windows should read Android PaperInk");
        Assert(PageThumbnailService.CreatePageBitmap(portableOnlyPage, 150, 212) is RenderTargetBitmap, "PaperInk-only pages should render Windows thumbnails");
        var legacyMigrationPage = new NotebookPage { InkData = sampleInkData };
        Assert(WpfInkAdapter.GetPageStrokes(legacyMigrationPage, migrateLegacyInk: true).Count == 1 && !legacyMigrationPage.Ink.IsEmpty, "Legacy ISF should migrate to PaperInk when opened on Windows");

        var legacyDocument = new
        {
            FormatVersion = 1,
            Id = Guid.NewGuid(),
            Title = "课程笔记",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ModifiedAt = DateTimeOffset.UtcNow,
            CurrentPageId = legacyFirstPageId,
            Pages = new[]
            {
                new { Id = legacyFirstPageId, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), ModifiedAt = DateTimeOffset.UtcNow, InkData = sampleInkData },
                new { Id = legacySecondPageId, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), ModifiedAt = DateTimeOffset.UtcNow, InkData = sampleInkData }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(testService.NotebooksDirectory, "legacy-course.papernote"),
            JsonSerializer.Serialize(legacyDocument));

        var notebooks = await testService.ListAsync();
        var course = notebooks.Single(item => item.Document.Title == "课程笔记");
        Assert(course.Document.Pages.Count == 2, "课程笔记应恢复为 2 页");
        Assert(course.Document.Pages.Any(page => page.Id == course.Document.CurrentPageId), "当前页 ID 必须有效");
        Assert(course.Document.Pages.All(page => !string.IsNullOrWhiteSpace(page.InkData)), "两页都应包含笔迹数据");
        Assert(course.Document.Pages.All(page => PageThumbnailService.Deserialize(page.InkData).Count > 0), "两页 ISF 笔迹都应可恢复");
        Assert(course.Document.Pages.All(page => page.PaperTemplate == "Dotted" && page.PaperColor == "#FFFFFF"), "旧版页面应补齐默认点阵白纸");
        Assert(course.Document.CoverStyle == "Blue" && course.Document.FolderName == string.Empty && !course.Document.IsInTrash, "旧格式应补齐默认封面、未分类和非回收站状态");
        Assert(course.Document.Pages.All(page => page.Objects.Count == 0), "旧格式页面应补齐空对象列表");
        Assert(course.Document.Pages.All(page => page.Title == string.Empty && !page.IsBookmarked), "旧格式页面应补齐空标题和未加书签状态");
        Assert(course.Document.PaperPresets.Count == 0 && course.Document.Pages.All(page => page.OutlineLevel == 0), "旧格式应补齐空常用纸张和未加入目录状态");
            Assert(course.Document.Pages.All(page => page.BackgroundImageData == string.Empty && page.BackgroundSourceType == string.Empty && page.BackgroundSourceName == string.Empty && page.BackgroundPageNumber == 0 && page.BackgroundRotation == 0 && page.BackgroundCropLeft == 0 && page.BackgroundCropTop == 0 && page.BackgroundCropRight == 0 && page.BackgroundCropBottom == 0), "Legacy pages should get empty background fields.");

        foreach (var template in new[] { "Blank", "Dotted", "Lined", "Grid" })
        {
            var thumbnail = PageThumbnailService.Create(new StrokeCollection(), template, "#FFFBEA");
            Assert(thumbnail is RenderTargetBitmap { PixelWidth: 150, PixelHeight: 212 }, $"{template} 缩略图尺寸错误");
        }

        var tempPath = Path.Combine(testService.NotebooksDirectory, $"phase3-smoke-{Guid.NewGuid():N}.papernote");
        try
        {
            var document = NotebookDocument.Create("第三阶段管理验证", course.Document.Pages[0].InkData);
            document.FolderName = "课程";
            document.CoverStyle = "Purple";
            document.Pages[0].Title = "  第一章 导论  ";
            document.Pages[0].IsBookmarked = true;
            document.Pages[0].OutlineLevel = 1;
            document.PaperPresets.Add(new PaperPreset { PaperTemplate = "Grid", PaperColor = "#F2F7FF" });
            document.PaperPresets.Add(new PaperPreset { PaperTemplate = "Grid", PaperColor = "#F2F7FF" });
            document.Pages[0].PaperTemplate = "Lined";
            document.Pages[0].PaperColor = "#FFFBEA";
            document.Pages[0].BackgroundImageData = importedPdfPages[0].ImageData;
            document.Pages[0].BackgroundSourceType = "PDF";
            document.Pages[0].BackgroundSourceName = "course-handout.pdf";
            document.Pages[0].BackgroundPageNumber = 1;
            document.Pages[0].BackgroundRotation = 90;
            document.Pages[0].BackgroundCropLeft = 0.05;
            document.Pages[0].BackgroundCropTop = 0.05;
            document.Pages[0].BackgroundCropRight = 0.05;
            document.Pages[0].BackgroundCropBottom = 0.05;
            var savedGroupId = Guid.NewGuid();
            document.Pages[0].Objects.Add(new PageObject
            {
                Kind = "Text",
                X = 120,
                Y = 160,
                Width = 300,
                Height = 120,
                Text = "文本框保存验证",
                StrokeColor = "#202124",
                FontSize = 32,
                Opacity = 0.75,
                Rotation = 90,
                IsLocked = true,
                GroupId = savedGroupId
            });
            document.Pages[0].Objects.Add(new PageObject
            {
                Kind = "Shape",
                ShapeKind = "Ellipse",
                X = 440,
                Y = 180,
                Width = 180,
                Height = 140,
                StrokeColor = "#3978F6",
                FillColor = "#183978F6",
                StrokeThickness = 4,
                Opacity = 0.5,
                GroupId = savedGroupId
            });
            document.Pages[0].Objects.Add(new PageObject
            {
                Kind = "Image",
                X = 180,
                Y = 360,
                Width = 220,
                Height = 140,
                ImageData = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZbYQAAAAASUVORK5CYII=",
                Opacity = 0.25
            });
            foreach (var shapeKind in new[] { "RoundedRectangle", "Triangle", "Diamond", "Arrow" })
            {
                document.Pages[0].Objects.Add(new PageObject
                {
                    Kind = "Shape",
                    ShapeKind = shapeKind,
                    X = 80 + document.Pages[0].Objects.Count * 70,
                    Y = 560,
                    Width = 150,
                    Height = 110,
                    StrokeColor = "#E5484D",
                    FillColor = "#30E5484D",
                    StrokeThickness = 5
                });
            }
            document.Pages.Add(new NotebookPage
            {
                Title = new string('长', 80),
                OutlineLevel = 9,
                InkData = course.Document.Pages[1].InkData,
                PaperTemplate = "Grid",
                PaperColor = "#F2F7FF",
                BackgroundImageData = "not-base64",
                BackgroundSourceType = "PDF",
                BackgroundSourceName = " damaged.pdf ",
                BackgroundPageNumber = 0,
                BackgroundRotation = 47,
                BackgroundCropLeft = -2,
                BackgroundCropTop = 0.7,
                BackgroundCropRight = 0.8,
                BackgroundCropBottom = 0.8,
                Objects =
                [
                    new PageObject
                    {
                        Kind = "Unknown",
                        ShapeKind = "Hexagon",
                        X = -40,
                        Y = 5000,
                        Width = 0,
                        Height = 5000,
                        StrokeColor = "",
                        FillColor = "",
                        StrokeThickness = 0,
                        FontSize = 500,
                        Opacity = -5,
                        Rotation = -450,
                        GroupId = Guid.NewGuid()
                    }
                ]
            });
            document.Pages[0].Objects[2].LinkTargetPageId = document.Pages[1].Id;
            document.Pages[1].Objects[0].LinkTargetPageId = Guid.NewGuid();
            document.LastOpenedAt = new DateTimeOffset(2026, 7, 18, 8, 30, 0, TimeSpan.Zero);
            document.CurrentPageId = document.Pages[1].Id;
            await testService.SaveAsync(document, tempPath);

            var loaded = await testService.LoadAsync(tempPath);
            Assert(loaded.FormatVersion == 14, "Format version should be 14.");
            Assert(loaded.LastOpenedAt == document.LastOpenedAt, "最近打开时间应往返保持。");
            Assert(loaded.FolderName == "课程" && loaded.CoverStyle == "Purple", "文件夹与封面应往返保持");
            Assert(!loaded.IsInTrash && loaded.TrashedAt is null, "新笔记本默认不在回收站");
            Assert(loaded.Pages.Count == 2, "模板验证笔记应恢复为 2 页");
            Assert(loaded.Pages[0].Title == "第一章 导论" && loaded.Pages[0].IsBookmarked, "页面标题和书签应往返保持");
            Assert(loaded.Pages[1].Title.Length == 60 && !loaded.Pages[1].IsBookmarked, "过长页面标题应被安全截断，书签默认应为关闭");
            Assert(loaded.Pages[0].OutlineLevel == 1 && loaded.Pages[1].OutlineLevel == 3, "目录级别应往返保持并约束到 0 至 3。");
            Assert(loaded.PaperPresets.Count == 1 && loaded.PaperPresets[0].PaperTemplate == "Grid" && loaded.PaperPresets[0].PaperColor == "#F2F7FF", "常用纸张应往返保持并自动去重。");
            Assert(loaded.Pages[0].PaperTemplate == "Lined" && loaded.Pages[0].PaperColor == "#FFFBEA", "横线米黄纸应往返保持");
            Assert(loaded.Pages[0].BackgroundImageData == importedPdfPages[0].ImageData && loaded.Pages[0].BackgroundSourceType == "PDF" && loaded.Pages[0].BackgroundSourceName == "course-handout.pdf" && loaded.Pages[0].BackgroundPageNumber == 1, "PDF background data and source metadata should round-trip.");
            Assert(loaded.Pages[0].BackgroundRotation == 90 && loaded.Pages[0].BackgroundCropLeft == 0.05 && loaded.Pages[0].BackgroundCropBottom == 0.05, "PDF rotation and crop settings should round-trip.");
            Assert(PageBackgroundService.CreateImageSource(loaded.Pages[0]) is BitmapSource { PixelWidth: > 1000, PixelHeight: > 700 }, "Rotated and cropped PDF background should remain renderable.");
            Assert(loaded.Pages[0].Objects.Count == 7, "文本、图片和扩展形状对象应往返保持");
            Assert(loaded.Pages[0].Objects[0].Kind == "Text" && loaded.Pages[0].Objects[0].Text == "文本框保存验证", "文本框内容应往返保持");
            Assert(loaded.Pages[0].Objects[0].FontSize == 32 && loaded.Pages[0].Objects[0].Opacity == 0.75 && loaded.Pages[0].Objects[0].StrokeColor == "#202124", "文字字号、颜色和透明度应往返保持");
            Assert(loaded.Pages[0].Objects[0].Rotation == 90 && loaded.Pages[0].Objects[0].IsLocked, "对象旋转角度和锁定状态应往返保持");
            Assert(loaded.Pages[0].Objects[1].Kind == "Shape" && loaded.Pages[0].Objects[1].ShapeKind == "Ellipse", "基础形状应往返保持");
            Assert(loaded.Pages[0].Objects[1].StrokeThickness == 4 && loaded.Pages[0].Objects[1].Opacity == 0.5, "形状线宽和透明度应往返保持");
            Assert(loaded.Pages[0].Objects[2].Kind == "Image" && !string.IsNullOrWhiteSpace(loaded.Pages[0].Objects[2].ImageData), "图片数据应往返保持");
            Assert(loaded.Pages[0].Objects[2].Opacity == 0.25, "图片透明度应往返保持");
            Assert(loaded.Pages[0].Objects.Skip(3).Select(item => item.ShapeKind).SequenceEqual(new[] { "RoundedRectangle", "Triangle", "Diamond", "Arrow" }), "圆角矩形、三角形、菱形和箭头应完整往返保持。");
            Assert(loaded.Pages[0].Objects.Skip(3).All(item => item.Kind == "Shape" && item.StrokeThickness == 5), "扩展形状样式应完整往返保持。");
            Assert(loaded.Pages[0].Objects[0].GroupId == savedGroupId && loaded.Pages[0].Objects[1].GroupId == savedGroupId, "文本和形状的组合关系应往返保持");
            Assert(loaded.Pages[0].Objects[2].GroupId is null, "未组合图片应保持独立");
            Assert(loaded.Pages[0].Objects[2].LinkTargetPageId == loaded.Pages[1].Id, "对象的内部页面链接应往返保持。");
            var normalizedObject = loaded.Pages[1].Objects.Single();
            Assert(normalizedObject.Kind == "Text" && normalizedObject.ShapeKind == "Rectangle", "非法对象类型应回退为安全默认值");
            Assert(normalizedObject.X >= 0 && normalizedObject.Y >= 0 && normalizedObject.Width >= 30 && normalizedObject.Height <= 1188, "非法对象坐标和尺寸应被约束到页面内");
            Assert(normalizedObject.StrokeThickness >= 1 && !string.IsNullOrWhiteSpace(normalizedObject.StrokeColor), "非法对象样式应回退为安全默认值");
            Assert(normalizedObject.FontSize == 96 && normalizedObject.Opacity == 0.1, "非法字号和透明度应约束到安全范围");
            Assert(normalizedObject.Rotation == 270, "负旋转角度应规范化到 0 至 359 度");
            Assert(normalizedObject.GroupId is null, "只有一个成员的孤立组合应被清除");
            Assert(normalizedObject.LinkTargetPageId is null, "指向不存在页面的内部链接应自动清除。");
            var objectThumbnail = PageThumbnailService.CreateFromInkData(loaded.Pages[0].InkData, loaded.Pages[0].PaperTemplate, loaded.Pages[0].PaperColor, loaded.Pages[0].Objects, loaded.Pages[0].BackgroundImageData);
            Assert(objectThumbnail is RenderTargetBitmap { PixelWidth: 150, PixelHeight: 212 }, "对象缩略图尺寸错误");
            Assert(loaded.Pages[1].PaperTemplate == "Grid" && loaded.Pages[1].PaperColor == "#F2F7FF", "方格浅蓝纸应往返保持");
            Assert(loaded.Pages[1].BackgroundImageData == "not-base64" && loaded.Pages[1].BackgroundSourceType == "PDF" && loaded.Pages[1].BackgroundSourceName == "damaged.pdf" && loaded.Pages[1].BackgroundPageNumber == 1, "Damaged background metadata should be normalized safely.");
            Assert(loaded.Pages[1].BackgroundRotation == 90 && loaded.Pages[1].BackgroundCropLeft == 0 && loaded.Pages[1].BackgroundCropTop == 0.45 && loaded.Pages[1].BackgroundCropRight == 0.45 && loaded.Pages[1].BackgroundCropBottom == 0.45, "Invalid PDF transforms should be normalized safely.");
            Assert(PageThumbnailService.CreateFromInkData(loaded.Pages[1].InkData, loaded.Pages[1].PaperTemplate, loaded.Pages[1].PaperColor, loaded.Pages[1].Objects, loaded.Pages[1].BackgroundImageData) is RenderTargetBitmap, "Damaged background data should not block thumbnail rendering.");
            Assert(loaded.CurrentPageId == document.CurrentPageId, "当前页应往返保持");
            Assert(!File.Exists(tempPath + ".tmp"), "原子保存后不应残留临时文件");

            var imagePaperPath = Path.Combine(testService.NotebooksDirectory, $"image-paper-{Guid.NewGuid():N}.papernote");
            var imagePaperDocument = NotebookDocument.Create("图片纸张验证");
            imagePaperDocument.Pages[0].PaperTemplate = "Blank";
            imagePaperDocument.Pages[0].BackgroundImageData = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZbYQAAAAASUVORK5CYII=";
            imagePaperDocument.Pages[0].BackgroundSourceType = "Image";
            imagePaperDocument.Pages[0].BackgroundSourceName = " custom-paper.png ";
            await testService.SaveAsync(imagePaperDocument, imagePaperPath);
            var loadedImagePaper = await testService.LoadAsync(imagePaperPath);
            Assert(loadedImagePaper.Pages[0].BackgroundSourceType == "Image" && loadedImagePaper.Pages[0].BackgroundSourceName == "custom-paper.png" && loadedImagePaper.Pages[0].BackgroundPageNumber == 0, "图片纸张来源信息应往返保持。");
            Assert(PageBackgroundService.CreateImageSource(loadedImagePaper.Pages[0]) is BitmapSource, "图片纸张应能在后台解码渲染。");
            testService.PermanentlyDelete(imagePaperPath);
            Assert(!File.Exists(imagePaperPath), "图片纸张测试文件应精确清理。");

            var exportPath = Path.Combine(Path.GetTempPath(), $"papernote-export-{Guid.NewGuid():N}.pdf");
            try
            {
                await PdfExportService.ExportAsync(exportPath, loaded.Pages);
                Assert(File.Exists(exportPath) && new FileInfo(exportPath).Length > 1000, "Flattened PDF export should create a non-empty file.");
                var exportedPages = PdfImportService.Import(exportPath);
                Assert(exportedPages.Count == loaded.Pages.Count, "Flattened PDF export should keep the selected page count.");
                Assert(!File.Exists(exportPath + ".tmp"), "PDF export should not leave a temporary file behind.");
            }
            finally
            {
                if (File.Exists(exportPath)) File.Delete(exportPath);
            }
            Assert(!File.Exists(exportPath), "PDF export test should clean up its output precisely.");


            var highQualityExportPath = Path.Combine(Path.GetTempPath(), $"papernote-export-letter-high-{Guid.NewGuid():N}.pdf");
            try
            {
                var highQualityOptions = new PdfExportOptions { Quality = "High", PageSize = "Letter" };
                await PdfExportService.ExportAsync(highQualityExportPath, loaded.Pages, highQualityOptions);
                Assert(File.Exists(highQualityExportPath) && new FileInfo(highQualityExportPath).Length > 1000, "High-quality Letter PDF export should create a non-empty file.");
                var highQualityPages = PdfImportService.Import(highQualityExportPath);
                Assert(highQualityPages.Count == loaded.Pages.Count, "High-quality Letter PDF export should keep the selected page count.");
                Assert(!File.Exists(highQualityExportPath + ".tmp"), "High-quality PDF export should not leave a temporary file behind.");
            }
            finally
            {
                if (File.Exists(highQualityExportPath)) File.Delete(highQualityExportPath);
            }
            Assert(!File.Exists(highQualityExportPath), "High-quality PDF export test should clean up its output precisely.");

            await testService.MoveToTrashAsync(tempPath);
            var trashed = await testService.LoadAsync(tempPath);
            Assert(trashed.IsInTrash && trashed.TrashedAt is not null, "移到回收站应记录状态和时间");
            Assert((await testService.ListAsync()).Any(item => string.Equals(item.FilePath, tempPath, StringComparison.OrdinalIgnoreCase) && item.Document.IsInTrash), "回收站笔记本仍应可列出");

            await testService.RestoreAsync(tempPath);
            var restored = await testService.LoadAsync(tempPath);
            Assert(!restored.IsInTrash && restored.TrashedAt is null, "恢复应清除回收站状态和时间");

            var source = loaded.Pages[0];
            var duplicate = new NotebookPage
            {
                Title = source.Title,
                IsBookmarked = source.IsBookmarked,
                InkData = source.InkData,
                Ink = source.Ink.Clone(),
                PaperTemplate = source.PaperTemplate,
                PaperColor = source.PaperColor,
                BackgroundImageData = source.BackgroundImageData,
                BackgroundSourceType = source.BackgroundSourceType,
                BackgroundSourceName = source.BackgroundSourceName,
                BackgroundPageNumber = source.BackgroundPageNumber,
                BackgroundRotation = source.BackgroundRotation,
                BackgroundCropLeft = source.BackgroundCropLeft,
                BackgroundCropTop = source.BackgroundCropTop,
                BackgroundCropRight = source.BackgroundCropRight,
                BackgroundCropBottom = source.BackgroundCropBottom,
                Objects = source.Objects.Select(item => item.Clone()).ToList()
            };
            Assert(duplicate.Id != source.Id, "复制页面必须生成新 ID");
            Assert(duplicate.InkData == source.InkData, "复制页面应保留笔迹");
            Assert(duplicate.Title == source.Title && duplicate.IsBookmarked == source.IsBookmarked, "复制页面应保留页面标题和书签");
            Assert(duplicate.PaperTemplate == source.PaperTemplate && duplicate.PaperColor == source.PaperColor, "复制页面应保留纸张外观");
            Assert(duplicate.BackgroundImageData == source.BackgroundImageData && duplicate.BackgroundSourceType == source.BackgroundSourceType && duplicate.BackgroundSourceName == source.BackgroundSourceName && duplicate.BackgroundPageNumber == source.BackgroundPageNumber && duplicate.BackgroundRotation == source.BackgroundRotation && duplicate.BackgroundCropLeft == source.BackgroundCropLeft && duplicate.BackgroundCropBottom == source.BackgroundCropBottom, "Page duplication should keep PDF background metadata and transforms.");
            Assert(duplicate.Objects.Count == source.Objects.Count && duplicate.Objects.All(item => source.Objects.All(sourceItem => sourceItem.Id != item.Id)), "复制页面应深复制页面对象并生成新对象 ID");
            Assert(duplicate.Objects[0].GroupId == savedGroupId && duplicate.Objects[1].GroupId == savedGroupId && duplicate.Objects[2].GroupId is null, "复制页面应保留对象组合关系");
            Assert(duplicate.Objects[0].FontSize == 32 && duplicate.Objects[0].Opacity == 0.75 && duplicate.Objects[1].Opacity == 0.5 && duplicate.Objects[2].Opacity == 0.25, "复制页面应保留对象样式");
            Assert(duplicate.Objects[0].Rotation == 90 && duplicate.Objects[0].IsLocked, "复制页面应保留对象旋转和锁定状态");

            var outsidePath = Path.Combine(AppContext.BaseDirectory, "outside.papernote");
            var rejected = false;
            try { await testService.SaveAsync(NotebookDocument.Create("越界验证"), outsidePath); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "存储服务必须拒绝数据目录之外的路径");
        }
        finally
        {
            if (File.Exists(tempPath)) testService.PermanentlyDelete(tempPath);
        }

        Assert(!File.Exists(tempPath), "测试笔记本应精确清理");
        if (Directory.Exists(storageTestRoot)) Directory.Delete(storageTestRoot, recursive: true);
        Assert(!Directory.Exists(storageTestRoot), "存储测试目录应完全清理，不能修改正式笔记目录");
        var backupTestRoot = Path.Combine(Path.GetTempPath(), $"papernote-backup-smoke-{Guid.NewGuid():N}");
        var backupNotebooksDirectory = Path.Combine(backupTestRoot, "Notebooks");
        var backupDirectory = Path.Combine(backupTestRoot, "Backups");
        var backupService = new NotebookStorageService(backupNotebooksDirectory, backupDirectory);
        string? backupNotebookPath = null;
        try
        {
            var originalDocument = NotebookDocument.Create("版本一");
            var created = await backupService.CreateAsync(originalDocument);
            backupNotebookPath = created.FilePath;
            originalDocument.Title = "版本二";
            originalDocument.Pages.Add(new NotebookPage { PaperTemplate = "Grid" });
            await backupService.SaveAsync(originalDocument, backupNotebookPath);

            var automaticBackups = await backupService.ListBackupsAsync(backupNotebookPath);
            Assert(automaticBackups.Count == 1 && automaticBackups[0].Kind == "自动备份", "首次覆盖保存应自动保护旧版本。");
            var originalBackup = automaticBackups[0];
            var manualBackup = await backupService.CreateBackupAsync(backupNotebookPath);
            Assert(manualBackup.Kind == "手动版本" && manualBackup.PageCount == 2, "手动版本应记录当前页数。");

            originalDocument.Title = "版本三";
            originalDocument.Pages.Add(new NotebookPage());
            await backupService.SaveAsync(originalDocument, backupNotebookPath);
            var restoredVersion = await backupService.RestoreBackupAsync(backupNotebookPath, originalBackup.FilePath);
            Assert(restoredVersion.Title == "版本一" && restoredVersion.Pages.Count == 1, "历史版本恢复应还原标题和页面。");
            var backupsAfterRestore = await backupService.ListBackupsAsync(backupNotebookPath);
            Assert(backupsAfterRestore.Any(item => item.Kind == "恢复前保护点" && item.PageCount == 3), "恢复前必须自动保护当前内容。");
            Assert(!Directory.EnumerateFiles(backupTestRoot, "*.tmp", SearchOption.AllDirectories).Any(), "备份和恢复不应留下临时文件。");

            backupService.PermanentlyDelete(backupNotebookPath);
            Assert(!File.Exists(backupNotebookPath), "永久删除应删除版本测试笔记本。");
            Assert(!Directory.Exists(Path.Combine(backupDirectory, Path.GetFileNameWithoutExtension(backupNotebookPath))), "永久删除应同步清理历史版本目录。");
        }
        finally
        {
            if (backupNotebookPath is not null && File.Exists(backupNotebookPath)) backupService.PermanentlyDelete(backupNotebookPath);
            if (Directory.Exists(backupTestRoot)) Directory.Delete(backupTestRoot, recursive: true);
        }
        Assert(!Directory.Exists(backupTestRoot), "版本测试目录应精确清理。");

        var settingsTestRoot = Path.Combine(Path.GetTempPath(), $"papernote-settings-smoke-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(settingsTestRoot);
            var workspacePath = Path.Combine(settingsTestRoot, "workspace-state.json");
            var workspaceService = new WorkspaceStateService(workspacePath);
            var workspaceTabs = Enumerable.Range(0, 9)
                .Select(index => new WorkspaceNotebookTab { FilePath = Path.Combine(settingsTestRoot, $"note-{index}.papernote"), Title = $"笔记 {index}" })
                .ToList();
            workspaceTabs.Add(new WorkspaceNotebookTab { FilePath = Path.Combine(settingsTestRoot, "note-8.papernote"), Title = "更新后的标题" });
            await workspaceService.SaveAsync(new WorkspaceState
            {
                Tabs = workspaceTabs,
                ActiveNotebookPath = Path.Combine(settingsTestRoot, "note-8.papernote"),
                LibrarySort = "Title"
            });
            var workspaceState = await workspaceService.LoadAsync();
            Assert(workspaceState.Tabs.Count == 8 && workspaceState.Tabs.Select(item => item.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 8, "工作区标签应去重并最多保留 8 本。");
            Assert(workspaceState.Tabs.Any(item => item.Title == "更新后的标题") && workspaceState.LibrarySort == "Title", "工作区应恢复最新标签标题和书架排序。");
            Assert(workspaceState.ActiveNotebookPath.EndsWith("note-8.papernote", StringComparison.OrdinalIgnoreCase), "工作区应恢复最后活动笔记路径。");
            Assert(WorkspaceStateService.Normalize(new WorkspaceState { LibrarySort = "Invalid" }).LibrarySort == "Modified", "非法书架排序应回退为最近修改。");
            Assert(!File.Exists(workspacePath + ".tmp"), "工作区状态保存不应残留临时文件。");

            var templatesPath = Path.Combine(settingsTestRoot, "paper-templates.json");
            var templateService = new PaperTemplateLibraryService(templatesPath);
            var duplicateTemplateId = Guid.NewGuid();
            var templates = new List<SharedPaperTemplate>
            {
                new() { Id = duplicateTemplateId, Name = "横线米黄", PaperTemplate = "Lined", PaperColor = "#FFFBEA" },
                new() { Id = duplicateTemplateId, Name = "图片纸张", PaperTemplate = "Blank", PaperColor = "#FFFFFF", BackgroundImageData = Convert.ToBase64String([1, 2, 3]), SourceName = @"C:\素材\paper.png" },
                new() { Name = "非法模板", PaperTemplate = "Unknown", PaperColor = "", BackgroundImageData = "not-base64" }
            };
            templates.AddRange(Enumerable.Range(0, 30).Select(index => new SharedPaperTemplate { Name = $"模板 {index}", PaperTemplate = "Grid", PaperColor = "#F2F7FF" }));
            await templateService.SaveAsync(templates);
            var loadedTemplates = await templateService.LoadAsync();
            Assert(loadedTemplates.Count == PaperTemplateLibraryService.MaximumTemplates, "共享纸张模板库应限制为 24 个模板。");
            Assert(loadedTemplates.Select(item => item.Id).Distinct().Count() == loadedTemplates.Count, "共享纸张模板的重复 ID 应自动修复。");
            Assert(loadedTemplates.Any(item => item.Name == "横线米黄" && item.PaperTemplate == "Lined" && item.PaperColor == "#FFFBEA"), "普通共享纸张模板应完整往返。");
            Assert(loadedTemplates.Any(item => item.Name == "图片纸张" && item.BackgroundImageData == Convert.ToBase64String([1, 2, 3]) && item.SourceName == "paper.png"), "图片纸张模板应保留图片数据并清理来源文件名。");
            Assert(loadedTemplates.Any(item => item.Name == "非法模板" && item.PaperTemplate == PaperPageDefaults.Template && item.PaperColor == PaperPageDefaults.Color && string.IsNullOrEmpty(item.BackgroundImageData)), "非法共享模板应安全回退并清除损坏图片数据。");
            Assert(!File.Exists(templatesPath + ".tmp"), "共享纸张模板保存不应残留临时文件。");
        }
        finally
        {
            if (Directory.Exists(settingsTestRoot)) Directory.Delete(settingsTestRoot, recursive: true);
        }
        Assert(!Directory.Exists(settingsTestRoot), "工作区与模板测试目录应精确清理。");

        var packageTestRoot = Path.Combine(Path.GetTempPath(), $"papernote-library-package-smoke-{Guid.NewGuid():N}");
        try
        {
            var sourceNotebooks = Path.Combine(packageTestRoot, "source-notebooks");
            var sourceBackups = Path.Combine(packageTestRoot, "source-backups");
            var restoreNotebooks = Path.Combine(packageTestRoot, "restore-notebooks");
            var restoreBackups = Path.Combine(packageTestRoot, "restore-backups");
            Directory.CreateDirectory(sourceNotebooks);
            Directory.CreateDirectory(sourceBackups);
            var alphaPath = Path.Combine(sourceNotebooks, "alpha.papernote");
            var betaPath = Path.Combine(sourceNotebooks, "beta.papernote");
            await File.WriteAllBytesAsync(alphaPath, JsonSerializer.SerializeToUtf8Bytes(NotebookDocument.Create("Alpha 资料")));
            await File.WriteAllBytesAsync(betaPath, JsonSerializer.SerializeToUtf8Bytes(NotebookDocument.Create("Beta 资料")));
            Directory.CreateDirectory(Path.Combine(sourceBackups, "alpha"));
            Directory.CreateDirectory(Path.Combine(sourceBackups, "beta"));
            await File.WriteAllBytesAsync(Path.Combine(sourceBackups, "alpha", "alpha-version.papernote"), JsonSerializer.SerializeToUtf8Bytes(NotebookDocument.Create("Alpha 历史")));
            await File.WriteAllBytesAsync(Path.Combine(sourceBackups, "beta", "beta-version.papernote"), JsonSerializer.SerializeToUtf8Bytes(NotebookDocument.Create("Beta 历史")));

            var packagePath = Path.Combine(packageTestRoot, "library.papernote-library.zip");
            var packageService = new LibraryBackupPackageService();
            var exportedManifest = await packageService.ExportAsync(packagePath, sourceNotebooks, sourceBackups);
            Assert(exportedManifest.NotebookCount == 2 && exportedManifest.BackupCount == 2 && File.Exists(packagePath), "整库备份包应包含两本笔记和两个历史版本。");
            using (var packageArchive = ZipFile.OpenRead(packagePath))
            {
                Assert(packageArchive.GetEntry("manifest.json") is not null, "整库备份包应包含清单。");
                Assert(packageArchive.Entries.Count(entry => entry.FullName.StartsWith("notebooks/", StringComparison.OrdinalIgnoreCase)) == 2, "整库备份包的笔记条目数量应正确。");
                Assert(packageArchive.Entries.Count(entry => entry.FullName.StartsWith("backups/", StringComparison.OrdinalIgnoreCase)) == 2, "整库备份包的历史版本条目数量应正确。");
            }

            var firstImport = await packageService.ImportAsync(packagePath, restoreNotebooks, restoreBackups);
            Assert(firstImport.ImportedNotebooks == 2 && firstImport.SkippedNotebooks == 0 && firstImport.ImportedBackups == 2, "空资料库应完整恢复所有笔记和历史版本。");
            Assert(Directory.EnumerateFiles(restoreNotebooks, "*.papernote").Count() == 2 && Directory.EnumerateFiles(restoreBackups, "*.papernote", SearchOption.AllDirectories).Count() == 2, "整库恢复后的文件数量应正确。");

            var secondImport = await packageService.ImportAsync(packagePath, restoreNotebooks, restoreBackups);
            Assert(secondImport.ImportedNotebooks == 0 && secondImport.SkippedNotebooks == 2 && secondImport.ImportedBackups == 0, "重复恢复相同备份包应跳过相同笔记和历史版本。");

            await File.WriteAllBytesAsync(Path.Combine(restoreNotebooks, "alpha.papernote"), JsonSerializer.SerializeToUtf8Bytes(NotebookDocument.Create("本地 Alpha 冲突版本")));
            var collisionImport = await packageService.ImportAsync(packagePath, restoreNotebooks, restoreBackups);
            Assert(collisionImport.ImportedNotebooks == 1 && collisionImport.SkippedNotebooks == 1 && collisionImport.ImportedBackups == 1, "同名不同内容的笔记应作为副本恢复并带回对应历史版本。");
            Assert(Directory.EnumerateFiles(restoreNotebooks, "alpha-imported-*.papernote").Count() == 1, "同名冲突恢复应生成唯一副本文件。");

            var corruptPackagePath = Path.Combine(packageTestRoot, "corrupt.papernote-library.zip");
            using (var corruptArchive = ZipFile.Open(corruptPackagePath, ZipArchiveMode.Create))
            {
                var corruptEntry = corruptArchive.CreateEntry("notebooks/corrupt.papernote");
                await using var corruptStream = corruptEntry.Open();
                await using var writer = new StreamWriter(corruptStream);
                await writer.WriteAsync("{not-valid-json");
            }
            var notebookCountBeforeCorruptImport = Directory.EnumerateFiles(restoreNotebooks, "*.papernote").Count();
            var corruptRejected = false;
            try { await packageService.ImportAsync(corruptPackagePath, restoreNotebooks, restoreBackups); }
            catch (InvalidDataException) { corruptRejected = true; }
            Assert(corruptRejected && Directory.EnumerateFiles(restoreNotebooks, "*.papernote").Count() == notebookCountBeforeCorruptImport, "损坏备份包应在写入任何笔记前被拒绝。");
            Assert(!Directory.EnumerateFiles(packageTestRoot, "*.tmp", SearchOption.AllDirectories).Any(), "整库备份导出与恢复不应残留临时文件。");
        }
        finally
        {
            if (Directory.Exists(packageTestRoot)) Directory.Delete(packageTestRoot, recursive: true);
        }
        Assert(!Directory.Exists(packageTestRoot), "整库备份包测试目录应精确清理。");

        Console.WriteLine("PAPERNOTE CORE BACKGROUND SMOKE TEST PASS");
        Console.WriteLine("整库搜索与文字提取、更多形状、工作区标签恢复、共享纸张模板、整库备份包、重复导入跳过、冲突副本恢复、损坏包拒绝和临时目录隔离均通过");
    }

    private static void CreateTestPdf(string filePath)
    {
        using var stream = File.Create(filePath);
        using var document = SKDocument.CreatePdf(stream);
        for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            var canvas = document.BeginPage(595, 842);
            canvas.Clear(SKColors.White);
            var headerColor = pageNumber switch { 1 => SKColors.CornflowerBlue, 2 => SKColors.IndianRed, _ => SKColors.SeaGreen };
            var bodyColor = pageNumber switch { 1 => SKColors.LightBlue, 2 => SKColors.MistyRose, _ => SKColors.Honeydew };
            using var headerPaint = new SKPaint { Color = headerColor, IsAntialias = true };
            using var bodyPaint = new SKPaint { Color = bodyColor, IsAntialias = true };
            canvas.DrawRect(new SKRect(40, 40, 555, 150), headerPaint);
            canvas.DrawRect(new SKRect(70, 220, 525, 760), bodyPaint);
            document.EndPage();
        }
        document.Close();
    }

    private static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

