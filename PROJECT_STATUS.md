# Project Status — VideoRenamer

| | |
| --- | --- |
| **Source version** | **V1.0.10.0** |
| **Published release** | **v1.0.8.0** |
| **Branch policy** | `main` is the only remote branch |
| **Status generated** | 2026-08-29 |
| **Overall state** | 核心功能稳定 · 视频命名与导出工具 |

## Current Version (V1.0.10.0)

### Core Features
- ✅ 视频素材批量重命名（场号 + 镜号 + 标签）
- ✅ 素材信息显示（大小、分辨率、时长、修改时间）
- ✅ FFmpeg 视频导出（可选 1080p 缩放、水印）
- ✅ 重命名历史记录与撤销
- ✅ 双主题（护眼模式 / 暗色模式）
- ✅ 自动更新检测

### Technical Details
- **EXE 大小**: 104 MB（单文件，内嵌 FFmpeg）
- **依赖**: .NET Framework 4.x（Windows 内置）
- **测试覆盖**: 79 个自测用例全部通过
- **构建门**: 6 个质量门全部通过

## Build & Test

```powershell
# 自测（必须通过）
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1" -SelfTest

# 构建 EXE
powershell -ExecutionPolicy Bypass -File "构建EXE.ps1"

# 打包安装程序（需要 Inno Setup 6）
powershell -ExecutionPolicy Bypass -File "打包安装程序.ps1"
```

## Documentation

- [README.md](README.md) — 产品说明、构建和打包说明
- [CHANGELOG.md](CHANGELOG.md) — 版本更新日志
- [AGENTS.md](AGENTS.md) — 开发环境和构建约束
- [docs/HEALTH_ASSESSMENT.md](docs/HEALTH_ASSESSMENT.md) — 项目健康评估
