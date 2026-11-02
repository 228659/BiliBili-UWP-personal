# 哔哩（BiliBili-UWP 个人维护版）

> 基于 [Richasy/BiliBili-UWP](https://github.com/Richasy/BiliBili-UWP) 的个人自用修改版。上游项目已停止维护并从 Microsoft Store 下架。
> 本仓库只做兼容性修复，不提供商业服务，与哔哩哔哩官方无关。

当前版本：**1.3.14.0**（2026-11-02） · 支持 **Windows 10 2004 及以上（含 Windows 11）/ x64**

## 这个版本改了什么

### 1.3.14.0（2026-11-02）

- “每列视频数”设置扩展到全部视频列表：首页/推荐、历史、排行榜、分区、稍后再看、收藏夹
- 收藏夹视频列表在首屏数据加载完成前保持加载占位，避免空白跳动
- 修复视频详情页信息区被占位层覆盖、不显示亚克力的问题

### 1.3.13.0（2026-11-02）

- 排行榜分区改为卡片式选择对话框，可在设置中勾选要显示的分区
- 排行榜每列视频数可配置（3–10），修复数字输入框上下箭头错位
- 新增亚克力通透度滑杆（0–90%），修复重启后通透度与保存值不一致
- 播放器新增“铺满应用”显示模式（画面按比例裁切填满应用窗口）

### 1.3.12.0（2026-11-01）

- 仅保留**扫码登录**（TV 二维码），移除密码登录
- 适配 B 站 64 位 ID（aid / mid / cid / 播放量），修复推荐、排行榜、动态的溢出与解析失败
- 排行榜改用网页当前使用的频道接口（`ranking/v2` 新频道 ID），数据与网页排行榜一致，不再返回 2025 年的冻结榜单
- 修复动态页在接口无数据时的 `NullReferenceException`，动态请求改用与登录令牌匹配的 Android appkey 签名
- 动态卡片中 `acl` 等超出 Int32 范围的字段改用 64 位类型，避免整页数据反序列化失败
- GitHub Actions 云端编译（x64 Debug / Release），本机无需安装 Visual Studio

### 已知限制

- 排行榜中番剧、国创、纪录片、电影、电视剧、综艺等 PGC 榜单尚未接入新的 PGC 接口
- 生活、资讯、剧情等分区已在网页排行榜下架，应用内仍保留旧入口
- 不支持直播、视频下载
- 安装包未签名，需要开启开发人员模式后以注册方式安装

## 安装

1. 在本仓库 [Releases](https://github.com/228659/BiliBili-UWP-personal/releases) 下载 `BiliBili-UWP-1.3.14.0-x64-AppX.zip`
2. 解压到任意目录
3. 开启 Windows 的“开发人员模式”（设置 → 系统 → 开发者选项）
4. 右键 `Install.ps1` → 使用 PowerShell 运行，或在 PowerShell 中执行：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install.ps1
   ```

   脚本会备份旧版 `LocalState`，卸载旧包并注册新版本。

> 未签名的 `.msix` 不能直接双击安装，请使用上面的 AppX 布局注册方式。Release 中的 `.msix` 仅作归档。

## 构建

推送到 `master` 会触发 GitHub Actions（`.github/workflows/build-uwp.yml`），在 `windows-2025-vs2026` 上使用 MSBuild 编译 x64 Debug / Release。

产物：

- `BiliBili-UWP-x64-Debug` / `BiliBili-UWP-x64-Release`
  - `artifact/AppPackages`：原始 MSIX、依赖与符号
  - `artifact/AppX`：解包后的布局，可直接执行 `Add-AppxPackage -Register AppX\AppxManifest.xml`

本地构建需要 Visual Studio 2022、Windows SDK 10.0.26100 和 UWP 工作负载。

## 开发环境

| 项目 | 版本 |
|-|-|
| 目标 SDK | Windows 10.0.26100 |
| 最低版本 | Windows 10.0.19041 |
| 平台 | x64 |
| 运行时 | Debug：.NET CoreCLR；Release：.NET Native |

## 项目结构

- `BiliBili-Controls`：自定义控件
- `BiliBili-Lib`：API 模型、服务与工具
- `BiliBili-Notification`：动态后台通知组件
- `BiliBili-UWP`：主程序（桌面 / 平板 / 共享页面）

## 免责声明与许可证

- 本项目仅供个人学习与自用，不内置任何账号凭据，登录令牌仅保存在本机
- 视频、番剧等内容版权归哔哩哔哩及原作者所有
- 本项目基于 Richasy/BiliBili-UWP 修改，遵循 **GPL-3.0** 许可证；修改内容见上文“这个版本改了什么”
