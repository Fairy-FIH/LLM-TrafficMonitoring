# LLM用量统计

多平台大模型 API 用量与花费监控器。WPF / .NET 8，发布为**单文件 exe**，深/浅色主题，支持网页登录抓取用量、本地代理精确统计、预算告警、Token 预计算、报表导出与 AES 加密配置。

## 功能一览

- **实时统计**：请求次数、输入/输出/缓存 Token、总花费；美元 / 人民币一键切换。
- **汇率**：联网自动刷新（er-api，frankfurter 兜底），可手动锁定 / 覆盖。
- **多平台用量采集**
  - 官方用量 API：OpenAI、Anthropic、OpenRouter（含增量游标与去重）。
  - 本地代理模式：内置 OpenAI / Anthropic 兼容转发服务，对**任意平台**精确记录请求与 Token。
  - 余额查询：DeepSeek、Moonshot、SiliconFlow、OpenAI 计费接口等。
  - **网页登录抓取**：内置 WebView2 浏览器登录平台控制台，自动拦截并识别用量 JSON 后导入（适合没有官方用量 API 的平台）。
- **定价引擎**：联网拉取 LiteLLM 定价库 + OpenRouter 实时价，内置离线兜底价，支持手工定价（永不被覆盖）。
- **多级缓存**：内存 + SQLite 两级，分级 TTL、ETag 增量、请求合并（单飞）、失败时使用陈旧数据降级。
- **现代 UI**：WPF-UI Fluent，Mica 材质，深/浅色主题，**每个模块独立配色**。
- **其它**：多 Key 分组管理、预算告警（桌面通知）、Token 预计算、HTTP/SOCKS5 代理、报表导出（xlsx/csv/json）、配置 AES-256 加密落盘。

## 目录结构

```
src/
  LlmUsageMonitor.App             WPF 界面、MVVM、DI 宿主、主题、图表控件
  LlmUsageMonitor.Core            领域模型、抽象接口、成本与预算算法、平台目录
  LlmUsageMonitor.Infrastructure  SQLite、缓存、加密、HTTP、定价、汇率、代理、导出
  LlmUsageMonitor.Providers       各平台适配器与注册表
tests/
  LlmUsageMonitor.Tests           成本 / 预算 / 缓存 / 加密 单元测试
```

## 构建与打包

需要 .NET SDK 8+（本仓库在 SDK 10 上验证）。

```powershell
# 运行测试
dotnet test

# 生成两种单文件 exe 到 .\dist
pwsh -File .\build.ps1
```

产物：

| 版本 | 体积 | 运行前提 |
|---|---|---|
| `dist/self-contained/LlmUsageMonitor.App.exe` | ~78 MB | 无需运行时，双击即用 |
| `dist/framework-dependent/LlmUsageMonitor.App.exe` | ~31 MB | 需安装 .NET 8 Desktop Runtime |

> 注意：框架依赖版不可用单文件压缩（.NET 限制）；两次发布请顺序执行，避免共享 obj 造成产物异常。

## 使用

1. **本地代理（精确统计，推荐）**：「本地代理」页 → 设置默认平台与上游 Base URL（默认 DeepSeek）→ 启动服务 → 把客户端 Base URL 指向 `http://127.0.0.1:8787/v1`：
   - 客户端使用自己的 API Key 即可（默认**透传** `Authorization`）。
   - 多平台可用请求头 `X-Llm-Provider: DeepSeek` 指定上游。
   - 页面上有「打开外部浏览器」按钮可直达代理地址。
2. **网页登录抓取**（无官方用量 API 的平台）：「网页登录」页 → 选平台 → 在内置浏览器登录并打开“用量/账单”页 → 程序拦截 JSON 并自动识别、导入用量；登录态持久保存。需要系统安装 **WebView2 运行时**（Win11 自带；Win10 若缺失会提示下载）。
3. **仪表盘**：请求数、输入/输出/总 Token、总花费（随币种切换），花费趋势可**悬停查看数值**，平台分布与模型花费排行（含服务商名）。
4. **同步**：左下角“同步”按钮刷新在线定价。
5. **预算告警**：在“预算告警”页按全局 / 分组设置周期限额与阈值。
6. **报表与工具**：导出 Excel/CSV/JSON，或用内置分词器做 Token 花费预计算。

## 数据与安全

- 数据目录：`%LOCALAPPDATA%\LlmUsageMonitor\`（`data.db`、`settings.dat`、`key.bin`）。
- 密钥与设置使用 **AES-256-GCM** 加密；数据密钥默认由 Windows **DPAPI**（当前用户）包裹，也支持主密码 PBKDF2 派生（可跨机器）。
- 应用不记录明文密钥；界面仅显示掩码。

## 支持的平台

| 平台 | 用量采集方式 | 备注 |
|---|---|---|
| DeepSeek | 本地代理 / 网页登录 | 默认上游，透传客户端 Key |
| OpenAI / Anthropic / OpenRouter | 本地代理 / 网页登录 | 数据成熟、模型齐全 |
| 智谱 / 阿里百炼 / 火山方舟 / Moonshot / SiliconFlow | 本地代理 / 网页登录 | OpenAI 兼容转发 |
| Gemini / Azure / 自定义 | 本地代理 | 需填写对应 Base URL |

> 说明：本项目已移除「密钥管理」界面，用量统一通过**本地代理**（客户端自带 Key，透传）或**网页登录抓取**获得，无需在应用内保存 API Key。

## 已知限制

- WPF 不支持 IL 裁剪，自包含体积偏大；已启用单文件压缩。
- 部分平台无用量接口，需依赖本地代理才能精确统计。
- 平台用量/定价接口可能调整，适配器与定价源均做了失败降级，不影响应用启动。


## 免责：
1. 本工具仅调用各大平台官方用量接口，费用估算为预估值，以服务商后台账单为准；
2. 请妥善保管API密钥，本软件会AES加密存储密钥，但不保证绝对安全；
3. 请勿将密钥提交到代码仓库。