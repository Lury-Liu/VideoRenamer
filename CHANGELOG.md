# Changelog

## V1.0.10.0 — 2026-08-29

### Removed

- **彻底删除视频播放器功能**：删除 VideoPlayerControl 和所有 VLC 相关代码
  - 删除 `src/App/Controls/VideoPlayerControl.cs`（经历了 VLC → WMP → WebBrowser 三次重写，均未稳定工作）
  - 删除 VLC 运行时（libvlc.dll + 365 个插件，~133 MB）
  - 删除播放器相关的测试和诊断脚本
- **删除抽帧预览功能**：删除 `VideoFrameStripProvider`、`VideoThumbnailProvider`、`ThumbnailCache`

### Changed

- 详情面板简化为纯信息显示（文件名、大小、分辨率、时长、路径）
- 方法重命名：`ShowVideoDetails` → `ShowMaterialDetails` 等，消除播放器命名残留
- MediaLoadScheduler 简化为单队列
- EXE 体积减少至 104 MB（无外部运行时依赖）

### Rationale

多次尝试不同播放器方案（VLC P/Invoke、WMP COM、WebBrowser HTML5）均无法稳定工作。
决定回归核心功能：视频命名和导出。

## V1.0.9.0 — 2026-08-13

### Added

- 授权系统：激活码验证、试用期、到期提示
- 授权管理器 (LicenseManager)：纯函数验证 + DPAPI 加密存储

### Changed

- 状态文本集中管理：PlanStatusText.FormatStatusDetail() 统一格式化
- 授权密钥工具：生成授权密钥.ps1

## V1.0.8.0 — 2026-07-27

### Added

- 自动更新检测与下载（GitHub Releases）
- 更新提示对话框，支持跳过版本
- 启动图标轮换系统（9 个图标，每次启动随机选择）

### Changed

- 应用名称统一为 VideoRenamer（之前为 MaterialRenamer）
- 启动画面显示 1 秒最小时长
- 发布流程：同时提供安装包和裸 EXE

### Fixed

- 清除划过预览帧缓存（当素材列表清空时）
- 工作区徽章左对齐
- 启动图标正确解码（从 ICO 提取最大 PNG 帧）

### Security

- 更新清单必须匹配 VideoRenamer 应用标识
- 更新清单必须包含有效的 SHA-256 哈希
- 下载的更新在替换前进行哈希验证

### Upgrade note

旧版本的应用标识和本地存储不会迁移。请卸载旧版本后全新安装 V1.0.8.0。
