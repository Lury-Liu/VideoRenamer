# Handoff — VideoRenamer V1.0.11.0

**交接日期：2026-08-31**
**项目目录：`D:\VideoRename`**
**交接状态：已移除 Inno Setup 安装包功能，保留 GitHub 自动更新、自替换和临时文件清理；SelfTest、SmokeTest 和最终 EXE 构建均已完成；当前工作树尚未创建新的 Git 提交。**


## 本轮便携式分发调整（2026-08-31）

- 已删除 `installer.iss`、`打包安装程序.ps1`、`assets/ChineseSimplified.isl` 以及旧 `installer/` 安装包产物。
- 构建门不再读取安装器配置；`构建EXE.ps1` 继续生成唯一的便携式 `dist/VideoRenamer.exe`。
- GitHub 自动更新保持不变：应用下载并校验 Release 裸 EXE，旧进程退出后运行替换脚本覆盖目标 EXE，随后自动删除下载文件和替换脚本。
- 本轮验证：SelfTest 86/86、SmokeTest OK、构建EXE.ps1 的 6 个质量门及产物校验均通过。

## 1. 本轮代码优化

本轮目标是缩短启动等待、删除启动图标轮换，并统一应用图标，同时调整导出区域文案。

### 启动时间

- `src/App/Forms/SplashForm.cs` 中启动计时由 `4000ms` 改为 `3000ms`。
- 启动画面提示文案由“4 秒”改为“3 秒”。
- 启动画面图标直接使用统一的 `AppIcon`。
- `src/App/Program.cs` 中删除启动图标轮换初始化调用，注释同步为固定 3 秒。

### 删除图标轮换

已删除以下源码：

- `src/Core/StartupIconRotation.cs`
- `src/App/Theme/StartupIconManager.cs`
- `src/App/Theme/StartupIconPreview.cs`
- `src/Services/StartupIconStateStore.cs`
- `src/Services/IconFileChangeNotifier.cs`

已删除以下资源：

- `assets/startup-icons/01.ico` 至 `assets/startup-icons/09.ico`

相关清理已经完成：

- `src/App/Theme/AppIcon.cs` 不再包含轮换初始化、会话图标、预览等逻辑，只保留统一图标加载和应用入口。
- `VideoRenamer.csproj` 只保留 `assets\app.ico` 作为应用图标，不再嵌入启动轮换资源。
- `构建EXE.ps1`、`scripts/build-common.ps1`、`scripts/verify-artifact.ps1` 不再收集、校验或打包启动轮换资源。
- `src/Tests/AppTests.cs` 已删除轮换、PNG 预览和 `current.ico` 代理测试。

按当前 Git diff 统计，本轮轮换功能删除约清理 **511 行代码/资源变更**。

### 关于窗口图标

- `src/App/Forms/AboutForm.cs` 中的 `AppIcon.Apply(this);` 已删除。
- `ShowInTaskbar = false;` 后已添加 `ShowIcon = false;`，显式关闭标题栏左上角图标显示。

### 导出区域文案

`src/App/MainForm/MaterialRenamerForm.Ui.cs` 已更新：

- `仅导出高清` → `仅高清`
- `导出并重命名` → `高清命名`

## 2. 当前命名规则

```text
E{集数}-S{场号}-{镜号}{后缀}-{尾段}{扩展名}
```

示例：

```text
E1-S2-28A-T1.mp4
```

- `E`：顶部设置的集数。
- `S`：默认场号，或在按行场号模式下使用行场号。
- 镜号：表格中的数字，可带 1–2 位英文字母后缀，统一大写。
- 尾段：默认按当前行的主要素材、备用素材顺序生成 `T1/T2/T3...`。
- 扩展名：默认小写，也可选择保持原大小写。

用户通常只需要确认正确的 **E、S 和镜号**；T 编号、常见重名和跨文件夹占用由软件处理。但软件不会分析视频内容来猜测真实镜号，因此“只知道 E/S、不知道镜号”时仍需要用户填写或确认镜号。

## 3. 本轮验证与构建结果

### 已完成

1. `AboutForm` 已在 `ShowInTaskbar = false;` 后添加 `ShowIcon = false;`。
2. SelfTest 已通过：`Self-test cases: 86 total, 86 passed, 0 failed.`，并输出 `SelfTest OK`。
3. SmokeTest 已通过：输出 `SmokeTest OK`。
4. 已执行 `git diff --check`，无格式错误。
5. 已检查 `StartupIcon`、`startup-icons`、`current.ico`：仅交接/状态文档保留历史说明，源码及构建配置无残留。
6. 已执行 `构建EXE.ps1`：6 个质量门、版本一致性和产物校验全部通过。

### 本轮构建产物

- 本地 EXE：`dist\VideoRenamer.exe`。
  - 文件版本：`1.0.11.0`
  - 文件大小：`101,919,744` bytes（约 102 MB）
  - SHA-256：`B7569B9D07371B40B7A738B9CEB0ED297AB4417EA343D8F3A36D9411FD4A8993`
  - 包含内嵌 FFmpeg；无额外 DLL 依赖。

## 4. 产物与发布状态

- 可直接交付的本地运行文件：`dist\VideoRenamer.exe`；本轮修改后的 EXE 已于 2026-08-31 重新构建并验证。
- 软件不再提供 Inno Setup 安装包；对外分发使用便携式单文件 EXE。
- 发布时由 `发布更新到GitHub.ps1` 重新生成与本地 EXE 一致的 `updates\latest.json`，并与裸 EXE 一同上传到 GitHub Release。
- 当前最终版为 `V1.0.11.0`；发布时通过 `发布更新到GitHub.ps1` 生成与此 EXE 一致的 Release 资产和更新清单。
- `dist/`、`updates/` 是被 Git 忽略的产物目录。
## 5. 文档同步结果

本次已更新：

- `PROJECT_STATUS.md`
- `handoff.md`

项目核心文档仍包括：

- `AGENTS.md`
- `README.md`
- `CHANGELOG.md`
- `docs/HEALTH_ASSESSMENT.md`

`handoff.md` 是开发交接文件，保留在项目根目录，不作为发布 EXE 必需内容。

## 6. 已知边界与后续事项

### 已知边界

- 对比文件夹只扫描第一层，不递归子目录。
- 不进行视频内容识别；真实镜号仍由用户提供或确认。
- `T` 自动递增上限固定为 100，这是有意的安全边界。
- 源码 loader 不嵌入 FFmpeg，正式媒体处理应使用构建后的 EXE。

### 后续可选项

1. 当前最终版为 V1.0.11.0；后续新增功能时再递增版本、重新构建并发布匹配的更新清单。
2. 如需发布新 EXE，先运行构建脚本并确认没有进程占用产物。
3. 软件仅分发裸 EXE；如需对外发布，使用 `发布更新到GitHub.ps1` 上传裸 EXE 和匹配的 `latest.json`。
4. 如需对外发布，先确认 GitHub Release、裸 EXE 文件名和 `latest.json` 三者一致。
5. 提交前查看完整 diff，确保没有把 `tools\ffmpeg.exe`、授权密钥工具或构建产物加入提交。

## 7. 下一位维护者的入口

1. 先阅读 `AGENTS.md`，遵守旧版 C# 编译器和构建门约束。
2. 再阅读 `README.md` 的命名/目录行为和 `PROJECT_STATUS.md` 的当前状态。
3. 修改命名逻辑时优先看 `src/Core/Naming/RenamePlanBuilder.cs` 及其测试，不要先在 WinForms 中追加规则。
4. 修改 UI 目录行为时检查 `src/App/MainForm/MaterialRenamerForm.Directories.cs` 与相关 partial 的路径同步。
5. 本轮源码、回归测试和本地 EXE 已完成；后续如有新改动，仍应先运行 SelfTest，涉及 UI 时运行 SmokeTest，最后重新构建 EXE。