# 参与贡献

感谢你帮助改进 PaperNote。

## 开始之前

- 阅读 [行为准则](CODE_OF_CONDUCT.md)。
- 搜索现有 Issue，避免重复提交。
- Bug 报告请写明平台、系统版本、复现步骤、预期结果和实际结果。
- 不要上传真实私人笔记、签名密钥、密码、访问令牌或其他敏感信息。

## 开发环境

- .NET SDK `10.0.302`
- Windows 桌面开发工作负载
- 开发 Android 客户端时需安装 MAUI Android 工作负载与 Android SDK

```powershell
dotnet restore PaperNote.sln
.\scripts\test.ps1 -SkipAndroidRuntime
```

连接 Android 模拟器或设备后，运行完整回归：

```powershell
.\scripts\test.ps1
```

## 分支与提交

1. 从最新主分支创建功能分支。
2. 每个提交集中解决一个主题。
3. 提交信息应清楚说明结果，而不是只写“更新”或“修复”。
4. 不提交 `bin`、`obj`、签名文件、密码、私人数据和生成的调试证据。
5. 提交前运行测试与 `git diff --check`。

## 代码要求

- 保持可空引用类型检查开启。
- 跨平台业务逻辑优先放入 `PaperNote.Core`。
- 平台 API 应限制在 Desktop 或 Mobile 平台层。
- 文件写入使用明确 UTF-8 编码，避免破坏中文文档。
- 数据格式调整必须更新格式版本、兼容测试和 `docs/DATA-FORMAT.md`。
- UI 改动应同时考虑手机、平板、触控笔和键鼠路径。

## Pull Request 清单

- [ ] 说明了变更目的和用户影响。
- [ ] 新功能有测试或可复现的验证步骤。
- [ ] Windows 版没有回归。
- [ ] Android 改动已在模拟器或真机验证。
- [ ] 文档和更新日志已同步。
- [ ] 没有引入密钥、密码、个人路径或私人内容。
