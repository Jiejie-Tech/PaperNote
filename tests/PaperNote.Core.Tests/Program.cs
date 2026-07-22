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

    var editableLayer = new PageLayer { Name = "可编辑" };
    var lockedLayer = new PageLayer { Name = "锁定", IsLocked = true };
    var selectionPage = new NotebookPage { Layers = [editableLayer, lockedLayer], ActiveLayerId = editableLayer.Id };
    var trianglePen = new PaperInkStroke
    {
        Tool = PaperInkTool.Pen, LayerId = editableLayer.Id, Width = 4,
        Points = [new() { X = 30, Y = 30 }, new() { X = 55, Y = 55 }, new() { X = 80, Y = 45 }]
    };
    var highlighter = new PaperInkStroke
    {
        Tool = PaperInkTool.Highlighter, LayerId = editableLayer.Id, Width = 18,
        Points = [new() { X = 230, Y = 70 }, new() { X = 270, Y = 90 }]
    };
    var crossingStroke = new PaperInkStroke
    {
        Tool = PaperInkTool.Pen, LayerId = editableLayer.Id, Width = 3,
        Points = [new() { X = 0, Y = 160 }, new() { X = 320, Y = 160 }]
    };
    var lockedStroke = new PaperInkStroke
    {
        Tool = PaperInkTool.Pen, LayerId = lockedLayer.Id, Color = "#111111", Width = 5,
        Points = [new() { X = 60, Y = 70 }, new() { X = 90, Y = 80 }]
    };
    selectionPage.Ink.Strokes.AddRange([trianglePen, highlighter, crossingStroke, lockedStroke]);
    var textObject = new PageObject { Kind = "Text", X = 30, Y = 25, Width = 80, Height = 55, LayerId = editableLayer.Id };
    var imageObject = new PageObject { Kind = "Image", X = 230, Y = 45, Width = 80, Height = 80, LayerId = editableLayer.Id };
    var rotatedShape = new PageObject { Kind = "Shape", X = 130, Y = 120, Width = 80, Height = 40, Rotation = 45, LayerId = editableLayer.Id };
    var lockedLayerObject = new PageObject { Kind = "Shape", X = 400, Y = 400, Width = 90, Height = 70, LayerId = lockedLayer.Id, StrokeColor = "#224466" };
    selectionPage.Objects.AddRange([textObject, imageObject, rotatedShape, lockedLayerObject]);

    var triangle = new[] { new PaperInkPoint { X = 10, Y = 10 }, new PaperInkPoint { X = 125, Y = 10 }, new PaperInkPoint { X = 10, Y = 125 } };
    var triangleSelection = PageSelectionService.SelectByPolygon(selectionPage, triangle);
    Assert(triangleSelection.StrokeIds.Contains(trianglePen.Id) && triangleSelection.ObjectIds.Contains(textObject.Id), "自由三角套索应同时命中笔迹和文字对象");
    Assert(!triangleSelection.StrokeIds.Contains(highlighter.Id) && !triangleSelection.ObjectIds.Contains(imageObject.Id), "自由套索不应命中范围外内容");

    var crossingPolygon = new[]
    {
        new PaperInkPoint { X = 120, Y = 140 }, new PaperInkPoint { X = 200, Y = 140 },
        new PaperInkPoint { X = 200, Y = 180 }, new PaperInkPoint { X = 120, Y = 180 }
    };
    Assert(InkSelectionService.SelectByPolygon(selectionPage.Ink, crossingPolygon).Contains(crossingStroke.Id), "长笔迹穿过套索时即使端点在外也应被选中");
    Assert(PageSelectionService.SelectByPolygon(selectionPage, triangle, PageSelectionFilter.Pen).StrokeIds.Contains(trianglePen.Id), "钢笔筛选应保留钢笔");
    Assert(!PageSelectionService.SelectByPolygon(selectionPage, triangle, PageSelectionFilter.Highlighter).StrokeIds.Contains(trianglePen.Id), "荧光笔筛选不应选择钢笔");
    Assert(PageSelectionService.SelectByPolygon(selectionPage, triangle, PageSelectionFilter.Text).ObjectIds.SequenceEqual([textObject.Id]), "文字筛选应只选择文字对象");
    Assert(PageSelectionService.SelectByPolygon(selectionPage, new[]
    {
        new PaperInkPoint { X = 115, Y = 100 }, new PaperInkPoint { X = 230, Y = 100 },
        new PaperInkPoint { X = 230, Y = 205 }, new PaperInkPoint { X = 115, Y = 205 }
    }, PageSelectionFilter.Shape).ObjectIds.Contains(rotatedShape.Id), "旋转对象应使用旋转后的轮廓参与套索命中");

    Assert(InkSelectionService.UpdateStyle(selectionPage, [trianglePen.Id, lockedStroke.Id], color: "#AA0000", width: 9, opacity: .4), "选中笔迹应可批量改色、改粗细和透明度");
    Assert(trianglePen.Color == "#AA0000" && trianglePen.Width == 9 && trianglePen.Opacity == .4, "可编辑笔迹样式应更新");
    Assert(lockedStroke.Color == "#111111" && lockedStroke.Width == 5, "锁定图层笔迹不应被批量修改");
    var lockedObjectSnapshot = lockedLayerObject.Clone();
    Assert(!PageObjectEditingService.Move(selectionPage, [lockedLayerObject.Id], 20, 20), "锁定图层对象不应被移动");
    Assert(!PageObjectEditingService.Resize(selectionPage, [lockedLayerObject.Id], 180, 140), "锁定图层对象不应被缩放");
    Assert(!PageObjectEditingService.Rotate(selectionPage, [lockedLayerObject.Id], 90), "锁定图层对象不应被旋转");
    Assert(PageObjectEditingService.Duplicate(selectionPage, [lockedLayerObject.Id]).Count == 0, "锁定图层对象不应被复制");
    Assert(!PageObjectEditingService.UpdateStyle(selectionPage, [lockedLayerObject.Id], strokeColor: "#FF0000", opacity: .25), "锁定图层对象不应被批量修改样式");
    Assert(!PageObjectEditingService.Delete(selectionPage, [lockedLayerObject.Id]), "锁定图层对象不应被删除");
    Assert(!PageObjectEditingService.SetLocked(selectionPage, [lockedLayerObject.Id], true), "锁定图层内不应修改对象锁定状态");
    Assert(lockedLayerObject.X == lockedObjectSnapshot.X && lockedLayerObject.Y == lockedObjectSnapshot.Y && lockedLayerObject.Width == lockedObjectSnapshot.Width && lockedLayerObject.StrokeColor == lockedObjectSnapshot.StrokeColor, "锁定图层对象属性必须保持不变");

    var combinedBefore = PageSelectionService.GetCombinedBounds(selectionPage, [trianglePen.Id], [imageObject.Id]);
    Assert(combinedBefore is not null && combinedBefore.Value.X < imageObject.X && combinedBefore.Value.Right >= imageObject.X + imageObject.Width, "混合选区边界应覆盖笔迹和对象");
    var imageXBeforeMove = imageObject.X;
    var penXBeforeMove = trianglePen.Points[0].X;
    Assert(PageSelectionService.Move(selectionPage, [trianglePen.Id], [imageObject.Id], 20, 30), "混合选区应可整体移动");
    Assert(Math.Abs((imageObject.X - imageXBeforeMove) - (trianglePen.Points[0].X - penXBeforeMove)) < .001, "混合移动应对笔迹和对象应用同一位移");
    var combinedMoved = PageSelectionService.GetCombinedBounds(selectionPage, [trianglePen.Id], [imageObject.Id])!.Value;
    Assert(PageSelectionService.Resize(selectionPage, [trianglePen.Id], [imageObject.Id], combinedMoved.Width * 1.5, combinedMoved.Height * 1.5), "混合选区应使用联合矩阵缩放");
    var objectCenterBeforeRotate = (X: imageObject.X + imageObject.Width / 2, Y: imageObject.Y + imageObject.Height / 2);
    Assert(PageSelectionService.Rotate(selectionPage, [trianglePen.Id], [imageObject.Id], 90) && imageObject.Rotation == 90, "混合选区应绕联合中心旋转");
    Assert(Math.Abs(imageObject.X + imageObject.Width / 2 - objectCenterBeforeRotate.X) > .01 || Math.Abs(imageObject.Y + imageObject.Height / 2 - objectCenterBeforeRotate.Y) > .01, "混合旋转应移动对象中心而非各自原地旋转");

    var transferTarget = new NotebookPage { Title = "目标页" };
    var copied = PageSelectionService.Transfer(selectionPage, transferTarget, [trianglePen.Id], [textObject.Id], move: false);
    Assert(copied.Count == 2 && selectionPage.Ink.Strokes.Any(item => item.Id == trianglePen.Id) && selectionPage.Objects.Any(item => item.Id == textObject.Id), "跨页复制应保留源内容");
    Assert(copied.StrokeIds.All(id => id != trianglePen.Id) && copied.ObjectIds.All(id => id != textObject.Id), "跨页复制应生成新的内容 ID");
    Assert(transferTarget.Ink.Strokes.Where(item => copied.StrokeIds.Contains(item.Id)).All(item => item.LayerId == transferTarget.ActiveLayerId) && transferTarget.Objects.Where(item => copied.ObjectIds.Contains(item.Id)).All(item => item.LayerId == transferTarget.ActiveLayerId), "跨页内容应映射到目标页活动图层");
    var moved = PageSelectionService.Transfer(selectionPage, transferTarget, [highlighter.Id], [imageObject.Id], move: true);
    Assert(moved.Count == 2 && selectionPage.Ink.Strokes.All(item => item.Id != highlighter.Id) && selectionPage.Objects.All(item => item.Id != imageObject.Id), "跨页移动应删除源内容并保留目标副本");
    var largeInk = new PaperInkDocument();
    for (var index = 0; index < 10_000; index++)
    {
        var x = (index % 100) * 8d;
        var y = (index / 100) * 8d;
        largeInk.Strokes.Add(new PaperInkStroke
        {
            Width = 2,
            Points = [new PaperInkPoint { X = x, Y = y }, new PaperInkPoint { X = x + 3, Y = y + 3 }]
        });
    }
    var longStroke = new PaperInkStroke
    {
        Width = 4,
        Points = [new PaperInkPoint { X = -20, Y = 410 }, new PaperInkPoint { X = 900, Y = 410 }]
    };
    largeInk.Strokes.Insert(321, longStroke);
    var spatialTimer = System.Diagnostics.Stopwatch.StartNew();
    var spatialIndex = new InkSpatialIndex();
    spatialIndex.Rebuild(largeInk);
    var localStrokes = spatialIndex.Query(0, 0, 40, 40);
    spatialTimer.Stop();
    Assert(spatialIndex.Count == 10_001, "Spatial index should include every valid stroke");
    Assert(localStrokes.Count is > 0 and < 250, "Small viewport should return only nearby strokes");
    Assert(spatialIndex.Query(420, 400, 20, 20).Contains(longStroke), "Long stroke crossing grid cells should be found");
    Assert(spatialIndex.QueryCircle(422, 410, 12).Contains(longStroke), "Circular eraser query should include crossing strokes");
    var orderedIds = spatialIndex.Query(0, 0, 840, 820).Select(stroke => largeInk.Strokes.IndexOf(stroke)).ToArray();
    Assert(orderedIds.SequenceEqual(orderedIds.Order()), "Spatial queries should preserve drawing order");
    Assert(spatialTimer.Elapsed < TimeSpan.FromSeconds(15), "Building and querying 10,000 strokes should remain practical");

    var indexedEraseDocument = new PaperInkDocument
    {
        Strokes =
        [
            new PaperInkStroke { LayerId = Guid.NewGuid(), Points = [new() { X = 0, Y = 20 }, new() { X = 35, Y = 20 }, new() { X = 50, Y = 20 }, new() { X = 65, Y = 20 }, new() { X = 100, Y = 20 }] },
            new PaperInkStroke { Points = [new() { X = 500, Y = 500 }, new() { X = 520, Y = 520 }] }
        ]
    };
    var indexedErase = new InkSpatialIndex();
    indexedErase.Rebuild(indexedEraseDocument);
    var eraseCandidates = indexedErase.QueryCircle(50, 20, 8);
    var detailedErase = InkEditingService.ErasePartialDetailed(indexedEraseDocument, 50, 20, 8, eraseCandidates);
    indexedErase.Update(indexedEraseDocument, detailedErase.RemovedStrokes, detailedErase.AddedStrokes);
    Assert(detailedErase.ChangedCount == 1 && detailedErase.AddedStrokes.All(stroke => stroke.LayerId.HasValue), "Partial erase should preserve layer identity");
    Assert(indexedErase.Count == indexedEraseDocument.Strokes.Count && indexedErase.Query(490, 490, 50, 50).Count == 1, "Incremental spatial update should remain consistent");

    var storage = new NotebookStorageService(Path.Combine(root, "Notebooks"), Path.Combine(root, "Backups"));
    Directory.CreateDirectory(storage.NotebooksDirectory);
    var stressStorage = new NotebookStorageService(Path.Combine(root, "StressNotebooks"), Path.Combine(root, "StressBackups"));
    var stressDocument = NotebookDocument.Create("Large offline round trip");
    stressDocument.Pages.Clear();
    var random = new Random(20260722);
    for (var pageIndex = 0; pageIndex < 60; pageIndex++)
    {
        var stressLayer = new PageLayer { Name = $"Layer {pageIndex}" };
        var stressPage = new NotebookPage
        {
            Title = $"Page {pageIndex + 1}",
            Layers = [stressLayer],
            ActiveLayerId = stressLayer.Id,
            OcrText = $"offline searchable text {pageIndex}",
            RecognizedText = $"handwriting {pageIndex}"
        };
        for (var strokeIndex = 0; strokeIndex < 80; strokeIndex++)
        {
            var stroke = new PaperInkStroke
            {
                LayerId = stressLayer.Id,
                Width = 1 + random.NextDouble() * 12,
                Opacity = .2 + random.NextDouble() * .8,
                Color = strokeIndex % 2 == 0 ? "#3157D5" : "#1D2530"
            };
            var startX = random.NextDouble() * 800;
            var startY = random.NextDouble() * 1140;
            for (var pointIndex = 0; pointIndex < 8; pointIndex++)
                stroke.Points.Add(new PaperInkPoint
                {
                    X = Math.Clamp(startX + pointIndex * 3 + random.NextDouble(), 0, 840),
                    Y = Math.Clamp(startY + Math.Sin(pointIndex) * 5, 0, 1188),
                    Pressure = random.NextDouble(),
                    TimestampMilliseconds = pageIndex * 100_000L + strokeIndex * 100L + pointIndex
                });
            stressPage.Ink.Strokes.Add(stroke);
        }
        for (var objectIndex = 0; objectIndex < 10; objectIndex++)
            stressPage.Objects.Add(new PageObject
            {
                Kind = objectIndex % 2 == 0 ? "Text" : "Shape",
                Text = $"Object {pageIndex}-{objectIndex}",
                X = random.NextDouble() * 700,
                Y = random.NextDouble() * 1050,
                Width = 80 + random.NextDouble() * 100,
                Height = 50 + random.NextDouble() * 100,
                LayerId = stressLayer.Id
            });
        stressPage.AudioRecordings.Add(new AudioRecording
        {
            DisplayName = $"Recording {pageIndex}",
            DurationMilliseconds = 120_000,
            Cues = [new AudioCue { OffsetMilliseconds = 10_000, StrokeId = stressPage.Ink.Strokes[0].Id, Label = "writing" }]
        });
        stressDocument.Pages.Add(stressPage);
    }
    stressDocument.CurrentPageId = stressDocument.Pages[37].Id;
    var stressTimer = System.Diagnostics.Stopwatch.StartNew();
    var stressStored = await stressStorage.CreateAsync(stressDocument);
    var stressLoadedOnce = await stressStorage.LoadAsync(stressStored.FilePath);
    await stressStorage.SaveAsync(stressLoadedOnce, stressStored.FilePath);
    var stressLoadedTwice = await stressStorage.LoadAsync(stressStored.FilePath);
    stressTimer.Stop();
    Assert(stressLoadedTwice.Id == stressDocument.Id && stressLoadedTwice.Pages.Count == 60, "Large notebook identity and page count should survive repeated saves");
    Assert(stressLoadedTwice.Pages.Sum(page => page.Ink.Strokes.Count) == 4_800 && stressLoadedTwice.Pages.Sum(page => page.Objects.Count) == 600, "Large notebook ink and objects should round trip");
    Assert(stressLoadedTwice.Pages.All(page => page.Layers.Count == 1 && page.Ink.Strokes.All(stroke => stroke.LayerId == page.ActiveLayerId)), "Large notebook layer relationships should round trip");
    Assert(stressLoadedTwice.Pages.Sum(page => page.AudioRecordings.Sum(recording => recording.Cues.Count)) == 60, "Audio timeline metadata should round trip");
    Assert(stressTimer.Elapsed < TimeSpan.FromSeconds(45), "Repeated large-notebook saves should finish within a practical background-test limit");

    var duplicateId = Guid.NewGuid();
    var invalidLayerId = Guid.NewGuid();
    var malformedDocument = new NotebookDocument
    {
        Title = "Malformed normalization",
        CurrentPageId = duplicateId,
        Pages =
        [
            new NotebookPage
            {
                Id = duplicateId,
                Layers = [new PageLayer { Id = Guid.Empty, Name = "" }, new PageLayer { Id = Guid.Empty, Name = "duplicate" }],
                Ink = new PaperInkDocument
                {
                    Strokes =
                    [
                        new PaperInkStroke { Id = Guid.Empty, LayerId = invalidLayerId, Width = double.NaN, Opacity = double.PositiveInfinity, Points = [new PaperInkPoint { X = double.NaN, Y = double.PositiveInfinity, Pressure = double.NaN }] },
                        new PaperInkStroke { Id = Guid.Empty, Points = [] }
                    ]
                },
                Objects = [new PageObject { Id = Guid.Empty, X = double.NaN, Width = double.PositiveInfinity }],
                AudioRecordings = [new AudioRecording { Id = Guid.Empty, DurationMilliseconds = -4, FileSize = -8, Cues = [new AudioCue { Id = Guid.Empty, OffsetMilliseconds = -2 }] }]
            },
            new NotebookPage { Id = duplicateId }
        ]
    };
    var malformedStored = await stressStorage.CreateAsync(malformedDocument);
    var normalizedMalformed = await stressStorage.LoadAsync(malformedStored.FilePath);
    Assert(normalizedMalformed.Pages.Select(page => page.Id).Distinct().Count() == 2 && normalizedMalformed.Pages.All(page => page.Id != Guid.Empty), "Duplicate or empty page IDs should be repaired");
    Assert(normalizedMalformed.Pages[0].Ink.Strokes.Select(stroke => stroke.Id).Distinct().Count() == 2 && normalizedMalformed.Pages[0].Ink.Strokes.All(stroke => stroke.Id != Guid.Empty), "Duplicate or empty stroke IDs should be repaired");
    Assert(normalizedMalformed.Pages[0].Ink.Strokes.SelectMany(stroke => stroke.Points).All(point => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Pressure)), "Non-finite ink values should be normalized");
    Assert(normalizedMalformed.Pages[0].Ink.Strokes.All(stroke => stroke.LayerId is null || stroke.LayerId == normalizedMalformed.Pages[0].ActiveLayerId), "Ink that references a missing layer should move to the active layer");
    Assert(normalizedMalformed.Pages[0].AudioRecordings[0].DurationMilliseconds == 0 && normalizedMalformed.Pages[0].AudioRecordings[0].Cues[0].OffsetMilliseconds == 0, "Invalid audio timeline values should be normalized");

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
var recording = AudioTimelineService.AddRecording(secondPage, "", 10_000, "课堂录音", fileSize: 0, mimeType: "audio/wav");
    var audioRelativePath = AudioAttachmentService.CreateRelativePath(document.Id, secondPage.Id, recording.Id, ".wav");
    var audioAbsolutePath = AudioAttachmentService.ResolvePath(storage.NotebooksDirectory, audioRelativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(audioAbsolutePath)!);
    var audioBytes = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
    await File.WriteAllBytesAsync(audioAbsolutePath, audioBytes);
    recording.LocalFilePath = audioRelativePath;
    recording.FileSize = audioBytes.LongLength;
Assert(AudioTimelineService.AddCue(recording, 1_000, label: "书写") && AudioTimelineService.GetCues(secondPage, 1_100).Count == 1, "audio timeline should deduplicate nearby cues");
    var encryption = new NotebookEncryptionService();
    var encrypted = encryption.Encrypt(loaded, "correct horse battery");
    Assert(NotebookEncryptionService.IsEncrypted(encrypted) && encryption.Decrypt(encrypted, "correct horse battery").Title == loaded.Title, "笔记本加密应可往返");
    var digest = DocumentIntegrityService.ComputeSha256(encrypted);
    var invalidDigest = digest[..^1] + (digest[^1] == '0' ? "1" : "0");
    Assert(DocumentIntegrityService.Verify(encrypted, digest) && !DocumentIntegrityService.Verify(encrypted, invalidDigest), "内容哈希应可校验");

    var backup = await storage.CreateBackupAsync(stored.FilePath);
    loaded.Title = "已更改";
    await storage.SaveAsync(loaded, stored.FilePath);
    var restored = await storage.RestoreBackupAsync(stored.FilePath, backup.FilePath);
    Assert(restored.Title == "跨平台课程笔记", "手动快照应可恢复");

    var package = new LibraryBackupPackageService();
    var packagePath = Path.Combine(root, "library.pnbak");
    var manifest = await package.ExportAsync(packagePath, storage.NotebooksDirectory, storage.BackupsDirectory);
    Assert(manifest.FormatVersion == 3 && manifest.AudioCount == 1 && manifest.Files.Count >= manifest.NotebookCount + manifest.AudioCount && manifest.Files.All(item => item.Sha256.Length == 64), "Backup manifest should contain SHA-256 hashes and audio attachments");
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

    var duplicateEntryPath = Path.Combine(root, "library-duplicate-entry.pnbak");
    File.Copy(packagePath, duplicateEntryPath);
    using (var archive = ZipFile.Open(duplicateEntryPath, ZipArchiveMode.Update))
    {
        var existing = archive.Entries.First(item => item.FullName.StartsWith("notebooks/", StringComparison.OrdinalIgnoreCase));
        archive.CreateEntry(existing.FullName);
    }
    var duplicateRejected = false;
    try { await package.ImportAsync(duplicateEntryPath, Path.Combine(root, "Duplicate", "Notebooks"), Path.Combine(root, "Duplicate", "Backups")); }
    catch (InvalidDataException) { duplicateRejected = true; }
    Assert(duplicateRejected, "A backup package with duplicate archive paths must be rejected");

    var traversalPath = Path.Combine(root, "library-path-traversal.pnbak");
    File.Copy(packagePath, traversalPath);
    using (var archive = ZipFile.Open(traversalPath, ZipArchiveMode.Update))
    {
        using var output = archive.CreateEntry("../escape.txt").Open();
        output.WriteByte(1);
    }
    var traversalRejected = false;
    try { await package.ImportAsync(traversalPath, Path.Combine(root, "Traversal", "Notebooks"), Path.Combine(root, "Traversal", "Backups")); }
    catch (InvalidDataException) { traversalRejected = true; }
    Assert(traversalRejected && !File.Exists(Path.Combine(root, "escape.txt")), "A backup package with path traversal must be rejected without writing outside the library");

    var tamperedAudioPath = Path.Combine(root, "library-tampered-audio.pnbak");
    File.Copy(packagePath, tamperedAudioPath);
    using (var archive = ZipFile.Open(tamperedAudioPath, ZipArchiveMode.Update))
    {
        var entry = archive.Entries.First(item => item.FullName.StartsWith("audio/", StringComparison.OrdinalIgnoreCase));
        byte[] bytes;
        using (var input = entry.Open()) { using var memory = new MemoryStream(); input.CopyTo(memory); bytes = memory.ToArray(); }
        var entryPath = entry.FullName;
        entry.Delete();
        bytes[bytes.Length / 2] ^= 0x01;
        var replacement = archive.CreateEntry(entryPath);
        using var output = replacement.Open();
        output.Write(bytes);
    }
    var audioRejected = false;
    try { await package.ImportAsync(tamperedAudioPath, Path.Combine(root, "TamperedAudio", "Notebooks"), Path.Combine(root, "TamperedAudio", "Backups")); }
    catch (InvalidDataException) { audioRejected = true; }
    Assert(audioRejected, "An audio attachment with an invalid hash must be rejected before import");

    var draft = NotebookDocument.Create("后台恢复草稿");
    draft.Id = loaded.Id;
    draft.Pages[0].Ink.Strokes.Add(new PaperInkStroke { Points = [new PaperInkPoint { X = 1, Y = 2 }, new PaperInkPoint { X = 3, Y = 4 }] });
    var draftBytes = JsonSerializer.SerializeToUtf8Bytes(draft);
    var draftPath = stored.FilePath + ".tmp";
    await File.WriteAllBytesAsync(draftPath, draftBytes);
    File.SetLastWriteTimeUtc(draftPath, DateTime.UtcNow.AddSeconds(2));
    var recoveryResults = await storage.RecoverTemporaryDraftsAsync();
    Assert(recoveryResults.Any(item => item.Recovered) && (await storage.LoadAsync(stored.FilePath)).Title == "后台恢复草稿", "启动时应自动恢复较新的临时草稿");

    var corruptPath = Path.Combine(storage.NotebooksDirectory, "损坏-只读抢救.papernote");
    await File.WriteAllTextAsync(corruptPath, "{ \"Title\": \"抢救标题\", \"Pages\": [ { \"Id\": 123 } ] }");
    var recoveryCandidates = await storage.InspectRecoveryAsync();
    Assert(recoveryCandidates.Any(item => item.Kind == NotebookRecoveryKind.CorruptedNotebook && item.FilePath == corruptPath), "损坏笔记应出现在恢复候选列表");
    var readOnlyRecovery = await storage.ReadForRecoveryAsync(corruptPath);
    Assert(readOnlyRecovery.IsReadable && readOnlyRecovery.Document is not null && readOnlyRecovery.Error is not null, "结构部分损坏的笔记应支持只读抢救");
    var recoveryCopy = await storage.SaveRecoveryCopyAsync(corruptPath);
    Assert(recoveryCopy.Document.Id != readOnlyRecovery.Document!.Id && recoveryCopy.Document.Title.EndsWith("(抢救副本)", StringComparison.Ordinal) && File.Exists(recoveryCopy.FilePath), "Recovery should save a new notebook without overwriting the source");
    Assert(File.Exists(corruptPath), "Saving a recovery copy must preserve the damaged source file");

    var emptyRecoveryPath = Path.Combine(storage.NotebooksDirectory, "empty-recovery.papernote");
    await File.WriteAllBytesAsync(emptyRecoveryPath, []);
    var emptyRecovery = await storage.ReadForRecoveryAsync(emptyRecoveryPath);
    Assert(!emptyRecovery.IsReadable && emptyRecovery.Document is null, "An empty notebook should remain available as an unreadable recovery candidate");

    var truncatedRecoveryPath = Path.Combine(storage.NotebooksDirectory, "truncated-recovery.papernote");
    await File.WriteAllTextAsync(truncatedRecoveryPath, "{ \"Title\": \"truncated preview\", \"Pages\": [");
    var truncatedRecovery = await storage.ReadForRecoveryAsync(truncatedRecoveryPath);
    Assert(!truncatedRecovery.IsReadable && truncatedRecovery.RawPreview.Contains("truncated preview", StringComparison.Ordinal), "A truncated notebook should expose a raw read-only preview");

    var arrayRecoveryPath = Path.Combine(storage.NotebooksDirectory, "array-recovery.papernote");
    await File.WriteAllTextAsync(arrayRecoveryPath, "[]");
    var arrayRecovery = await storage.ReadForRecoveryAsync(arrayRecoveryPath);
    Assert(!arrayRecovery.IsReadable && arrayRecovery.Document is null, "A top-level JSON array must not be treated as a notebook");

    var missingPagesPath = Path.Combine(storage.NotebooksDirectory, "missing-pages.papernote");
    await File.WriteAllTextAsync(missingPagesPath, "{ \"Title\": \"title-only recovery\" }");
    var missingPagesRecovery = await storage.ReadForRecoveryAsync(missingPagesPath);
    Assert(missingPagesRecovery.IsReadable && missingPagesRecovery.Document?.Pages.Count == 1, "A title-only notebook should be normalized with a safe blank page");

    var outsideRecoveryPath = Path.Combine(root, "outside-recovery.papernote");
    await File.WriteAllTextAsync(outsideRecoveryPath, "{ \"Title\": \"outside\" }");
    var outsideRejected = false;
    try { await storage.SaveRecoveryCopyAsync(outsideRecoveryPath); }
    catch (InvalidOperationException) { outsideRejected = true; }
    Assert(outsideRejected, "Recovery copies must reject files outside the notebook directory");

    File.Delete(corruptPath);
    File.Delete(emptyRecoveryPath);
    File.Delete(truncatedRecoveryPath);
    File.Delete(arrayRecoveryPath);
    File.Delete(missingPagesPath);


    var parsedPdfPages = PdfPageRangeService.Parse("1-3，5；7—9", 10);
    Assert(parsedPdfPages.SequenceEqual(new[] { 1, 2, 3, 5, 7, 8, 9 }), "PDF 页码范围应支持中文标点和长横线");
    Assert(PdfPageRangeService.Parse("1-500", 500).Count == 500, "PDF 导入应支持 500 页压力目标");
    var tooManyPdfPagesRejected = false;
    try { PdfPageRangeService.Parse("1-501", 501); }
    catch (InvalidDataException) { tooManyPdfPagesRejected = true; }
    Assert(tooManyPdfPagesRejected, "PDF 单次导入应限制为 500 页");

    var pdfSourcePath = Path.Combine(root, "large-import-source.pdf");
    var pdfSourceBytes = Enumerable.Range(0, 2 * 1024 * 1024).Select(index => (byte)(index % 251)).ToArray();
    await File.WriteAllBytesAsync(pdfSourcePath, pdfSourceBytes);
    var pdfCacheRoot = Path.Combine(root, "PdfImportCache");
    var pdfCache = new PdfImportCacheService(pdfCacheRoot, 32L * 1024 * 1024);
    var pdfProgressEvents = new List<PdfImportProgress>();
    var pdfProgress = new Progress<PdfImportProgress>(item => pdfProgressEvents.Add(item));
    var pdfJob = await pdfCache.PrepareAsync(pdfSourcePath, "large.pdf", [1, 3, 5], 10, 840, 1188, pdfProgress);
    await pdfCache.SavePageAsync(pdfJob, 1, new byte[] { 1, 2, 3 }, ".jpg");
    await pdfCache.SavePageAsync(pdfJob, 3, new byte[] { 4, 5, 6 }, ".jpg");
    await pdfCache.MarkCancelledAsync(pdfJob);
    var resumedPdfJob = await pdfCache.PrepareAsync(pdfSourcePath, "large.pdf", [1, 3, 5], 10, 840, 1188);
    Assert(resumedPdfJob.JobId == pdfJob.JobId && resumedPdfJob.CachedPages.Count == 2, "取消后的 PDF 导入应复用相同任务和已完成页面");
    Assert(pdfCache.TryReadPage(resumedPdfJob, 1, out var cachedPdfPage) && cachedPdfPage.SequenceEqual(new byte[] { 1, 2, 3 }), "PDF 页面缓存应可正确读取");
    Assert(pdfCache.GetMissingPages(resumedPdfJob).SequenceEqual(new[] { 5 }), "PDF 续接应只渲染缺失页面");
    await pdfCache.SavePageAsync(resumedPdfJob, 5, new byte[] { 7, 8, 9 }, ".jpg");
    await pdfCache.MarkCompletedAsync(resumedPdfJob);
    var completedPdfJob = await pdfCache.LoadAsync(resumedPdfJob.JobId);
    Assert(completedPdfJob?.Status == PdfImportJobStatus.Completed && completedPdfJob.CachedPages.Count == 3, "PDF 完成状态和缓存清单应原子持久化");

    var cancelledBeforePdfPrepare = false;
    using (var cancelledPdfToken = new CancellationTokenSource())
    {
        cancelledPdfToken.Cancel();
        try { await pdfCache.PrepareAsync(pdfSourcePath, "large.pdf", [2], 10, 840, 1188, cancellationToken: cancelledPdfToken.Token); }
        catch (OperationCanceledException) { cancelledBeforePdfPrepare = true; }
    }
    Assert(cancelledBeforePdfPrepare, "PDF 缓存准备应响应后台取消令牌");

    pdfSourceBytes[0] ^= 0x5A;
    await File.WriteAllBytesAsync(pdfSourcePath, pdfSourceBytes);
    var changedPdfJob = await pdfCache.PrepareAsync(pdfSourcePath, "large.pdf", [1, 3, 5], 10, 840, 1188);
    Assert(changedPdfJob.JobId != resumedPdfJob.JobId && changedPdfJob.CachedPages.Count == 0, "PDF 内容变化后必须失效旧缓存");
    changedPdfJob.CachedPages[1] = "../escape.jpg";
    var traversalCacheRejected = false;
    try { pdfCache.TryReadPage(changedPdfJob, 1, out _); }
    catch (InvalidDataException) { traversalCacheRejected = true; }
    Assert(traversalCacheRejected, "PDF 缓存清单必须拒绝路径越界");
    await pdfCache.PruneAsync(changedPdfJob.JobId);

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
