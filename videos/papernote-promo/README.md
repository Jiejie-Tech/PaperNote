# PaperNote 宣传视频工程

这是 PaperNote 45 秒横版宣传视频的可重复生成工程，采用 HyperFrames HTML 时间线制作，最终视频位于：

```text
renders/papernote-promo.mp4
```

## 参数

- 画面：1920 × 1080
- 帧率：30 fps
- 时长：45 秒
- 形式：真实应用截图、动态图形与中文字幕

## 本地复现

需要 Node.js、npm、Chromium/Chrome 和 FFmpeg。

```powershell
npm install
npm run check
npm run render
```

脚本固定使用 HyperFrames `0.7.64`，依赖版本记录在 `package-lock.json` 中。渲染前可阅读 `BRIEF.md`、`STORYBOARD.md`、`SCRIPT.md` 和 `frame.md`。

## 素材与许可

应用截图和 PaperNote 品牌素材来自本仓库。工程内的 `assets/vendor/gsap.min.js` 为 GSAP 3.14.2，适用 GreenSock Standard “No Charge” License；相关说明已加入仓库根目录的 `THIRD-PARTY-NOTICES.md`。