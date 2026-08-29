# Handoff — VideoRenamer V1.0.12.0

**交接日期：2026-08-29**
**项目目录：`D:\BaiduNetdiskDownload\VideoRename`**
**交接状态：功能实现完成，文档已同步，当前工作树尚未创建新的 Git 提交**

## 1. 本次完成内容

本次优化围绕“新下载素材与已有命名素材如何安全合并”展开，核心代码已经落在命名规划和目录编排层，而不是在 UI 中散落特殊判断。

### 跨文件夹对比

- UI 可以选择一个对比文件夹。
- 软件读取该目录第一层文件的文件名，使用大小写不敏感的集合进行比较。
- 对比只看文件名，不递归子目录；这是当前明确边界，不应在文档中误写成递归扫描。
- 目标文件若与对比目录已有文件同名，预览会标记冲突。

### 输出目录

- 未指定输出目录：每个目标文件使用源文件所在目录。
- 指定输出目录：本批次目标统一写入该目录。
- 外部输出目录中的导出成功后，UI 行模型会同步更新路径。
- 导出失败、取消或源文件不可用时，不删除源文件。

### 冲突自动递增

冲突来源包括：

1. 目标目录已经存在同名文件；
2. 当前批次内部生成了同名目标；
3. 对比文件夹存在同名文件；
4. 目标文件被占用或不可写。

开启自动递增后：

```text
数字尾段：T1 → T2 → T3 → … → T100
自定义尾段：补手机 → 补手机_2 → 补手机_3
```

- `T` 数字尾段的最大候选是 `T100`。
- `T100` 以内没有空位时，状态继续保持阻塞，执行按钮不会强行覆盖。
- 自定义尾段使用下划线分隔序号，避免 `TT1` + `1` 这类视觉粘连。
- 冲突检查和比较均为大小写不敏感。

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

## 3. 已验证结果

- `SelfTest 89/89`：通过。
- `SmokeTest OK`：通过。
- 6 个构建门：通过。
- 产物完整性/版本/资源校验：通过。
- 本地 EXE：`dist\VideoRenamer.exe`。
  - 文件版本：`1.0.12.0`
  - 文件大小：`104,074,240` bytes（约 104 MB）
  - 包含内嵌 FFmpeg；无额外 DLL 依赖。
- 本地更新清单：`updates\latest.json`，版本为 `1.0.12.0`。

建议的最小回归命令：

```powershell
Set-Location "D:\BaiduNetdiskDownload\VideoRename"
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1" -SelfTest
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1" -SmokeTest
```

如果源代码再次改动，再运行：

```powershell
powershell -ExecutionPolicy Bypass -File "构建EXE.ps1"
```

## 4. 产物与发布状态

- 可直接交付的本地运行文件：`dist\VideoRenamer.exe`。
- Inno Setup 安装包：本次没有确认 `ISCC.exe` 可用，因此不要在外部说明中称安装包已经验证完成。
- `updates\latest.json` 已生成本地版本和 SHA-256；线上 GitHub Release 是否可下载，应以实际 Release 页面或发布脚本结果为准。
- `dist/`、`installer/`、`updates/` 是被 Git 忽略的产物目录。

## 5. 文档同步结果

当前根目录及 docs 中的 Markdown 已统一到 V1.0.12.0：

- `AGENTS.md`
- `README.md`
- `CHANGELOG.md`
- `PROJECT_STATUS.md`
- `docs/HEALTH_ASSESSMENT.md`
- `handoff.md`（本文件）

构建产物中的 `dist\README.md` 与根目录 `README.md` 同步，`dist\CHANGELOG.md` 与根目录 `CHANGELOG.md` 同步。`handoff.md` 是开发交接文件，保留在项目根目录，不作为发布 EXE 必需内容。

## 6. 已知边界与后续事项

### 已知边界

- 对比文件夹只扫描第一层，不递归子目录。
- 不进行视频内容识别；真实镜号仍由用户提供或确认。
- `T` 自动递增上限固定为 100，这是有意的安全边界。
- 源码 loader 不嵌入 FFmpeg，正式媒体处理应使用构建后的 EXE。

### 后续可选项

1. 安装 Inno Setup 6 后运行 `打包安装程序.ps1`，再验证安装包。
2. 如需对外发布，先确认 GitHub Release、裸 EXE 文件名和 `latest.json` 三者一致。
3. 如未来要支持递归对比目录，应同时补充 UI 文案、命名引擎测试、性能边界和本文件中的限制说明。
4. 提交前查看完整 diff，确保没有把 `tools\ffmpeg.exe`、授权密钥工具或构建产物加入提交。

## 7. 下一位维护者的入口

1. 先阅读 `AGENTS.md`，遵守旧版 C# 编译器和构建门约束。
2. 再阅读 `README.md` 的命名/目录行为和 `PROJECT_STATUS.md` 的当前状态。
3. 修改命名逻辑时优先看 `src/Core/Naming/RenamePlanBuilder.cs` 及其测试，不要先在 WinForms 中追加规则。
4. 修改 UI 目录行为时检查 `src/App/MainForm/MaterialRenamerForm.Directories.cs` 与相关 partial 的路径同步。
5. 完成改动后先跑 SelfTest；UI 改动再跑 SmokeTest；最后才重新构建 EXE。