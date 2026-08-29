# AGENTS.md

## 项目定位

VideoRenamer 是一个面向 Windows 的视频素材批量命名与导出工具。它不是标准的 .NET SDK 项目：发布构建使用 Windows 自带的 .NET Framework `csc.exe`，通过 PowerShell 脚本编译 `src/` 下全部 C# 源码。

**文档核对日期：2026-08-29**
**当前版本：V1.0.12.0**
**当前验证结果：SelfTest 89/89、SmokeTest OK、构建产物校验通过**

## 构建与测试

```powershell
# 从源码运行（内存编译 src/，不嵌入 ffmpeg）
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1"

# 逻辑回归测试，必须打印 SelfTest OK
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1" -SelfTest

# UI 冒烟测试，必须打印 SmokeTest OK
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1" -SmokeTest

# 构建单文件 EXE（嵌入 tools\ffmpeg.exe 和图标）
powershell -ExecutionPolicy Bypass -File "构建EXE.ps1"

# 构建 EXE 并打包 Inno Setup 安装程序
powershell -ExecutionPolicy Bypass -File "打包安装程序.ps1"

# 发布 GitHub Release 更新（需要已登录 gh CLI）
powershell -ExecutionPolicy Bypass -File "发布更新到GitHub.ps1"
```

改动后先运行 `-SelfTest`。涉及 WinForms/UI 的改动再运行 `-SmokeTest`。`构建EXE.ps1` 会执行全部构建门和产物校验，是最完整的本地验证入口。

## 构建约束

- `VideoRenamer.csproj` 是 IDE/未来 CI 使用的影子项目，不是当前发布路径；不要以 `dotnet build` 代替正式构建。
- 版本号以 `src/App/AssemblyInfo.cs` 的 `AssemblyFileVersion` 为准，且必须与 `src/App/AppInfo.cs` 一致。
- C# 使用旧版编译器兼容范围：禁止 `using static`、字符串插值、表达式体成员、`?.`、`nameof` 和元组解构。
- `.ps1`、`.iss`、`.bat` 中含中文时使用 UTF-8 with BOM；`.cs` 使用 UTF-8 without BOM。

## 分层规则

- `src/Core/`：纯领域逻辑，禁止 `System.Windows.Forms` 和 `System.Drawing`。
- `src/Services/`：服务逻辑，禁止 `System.Windows.Forms` 和 `System.Drawing`。
- `src/Media/`：媒体元数据、FFmpeg 调用和导出，可使用 `System.Drawing`，但不直接依赖 WinForms。
- 颜色只能放在 `src/App/Theme/`、`src/App/Controls/` 以及测试中的 palette pin。
- 计划状态中文文案集中在 `PlanStatusText.cs` / `PlanStatus.cs` / 测试文件，其他代码使用 `PlanStatus` 枚举。
- 命名规则集中在 `src/Core/Naming/RenamePlanBuilder.cs`，目录选择与 UI 编排位于 `src/App/MainForm/` partial 类。

## 当前命名契约

目标文件名格式为：

```text
E{集数}-S{场号}-{镜号}{后缀}-{尾段}{扩展名}
```

- 集数 `E` 来自顶部设置；场号 `S` 使用默认场号或行场号。
- 镜号由表格填写，可带 1–2 位英文字母后缀，统一转大写。
- 默认尾段按当前行素材顺序生成 `T1`、`T2`、`T3`……，主要素材先于备用素材。
- 冲突自动解决时，数字尾段最多尝试到 `T100`；`T100` 以内都被占用时保留阻塞状态，不强行覆盖。
- 自定义尾段发生冲突时使用 `_2`、`_3` 等分隔序号，避免诸如 `TT1` 与数字直接粘连造成误读。
- 扩展名默认转小写，也可以选择保持原大小写。

## 跨文件夹与冲突规则

- 可指定“对比文件夹”。软件读取该目录**第一层**文件名，大小写不敏感，用于阻止与已命名素材重名；当前不会递归扫描子目录。
- 可指定输出文件夹。指定后，本批次目标文件统一生成到该目录；未指定时，每个文件仍在自己的源目录中重命名。
- 冲突检查同时覆盖：目标目录已有文件、当前批次内部重复、对比文件夹重名和目标文件占用。
- 开启“冲突自动递增”后，软件先尝试当前名称，再按可用序号生成候选名；所有候选都不可用时，预览保持阻塞并禁止执行。

## 冻结契约

- 嵌入资源名必须为 `VideoRenamer.ffmpeg.exe`。
- 发布 tag 为 `v{FileVersion}`，裸 EXE 资产名为 `VideoRenamer-v{version}.exe`，更新清单为 `updates/latest.json`。
- 许可证/免责声明状态目录为 `%LocalAppData%\VideoRenamer`，现有状态键和文件名不得随意改变。
- `-SelfTest` / `-SmokeTest` 必须分别打印 `SelfTest OK` / `SmokeTest OK`。
- `installer.iss` 中的 AppId GUID 是冻结值，不得重新生成。

## 目录与提交约定

- 远程主分支为 `main`。
- 提交前缀使用 `feat:`、`perf:`、`refactor:` 或 `build:`。
- `dist/`、`installer/`、`updates/` 是构建/发布产物目录，通常被 Git 忽略；`tools/ffmpeg.exe` 体积较大，也不提交。
- `生成授权密钥工具.ps1` 仅供开发使用，绝不随软件发布。

## 文档索引

- `README.md`：用户功能、命名规则、构建和使用说明。
- `CHANGELOG.md`：版本变更记录。
- `PROJECT_STATUS.md`：当前版本、验证结果和未完成事项。
- `docs/HEALTH_ASSESSMENT.md`：代码健康度与质量评估。
- `handoff.md`：本次功能优化、测试、产物和后续交接信息。