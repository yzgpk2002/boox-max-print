# 技术架构与协议

对应公开基线 0.4.2：Windows 图片发送器 0.4.2.0 / Android 接收器 0.4.0。目标设备是实测 Android 4.4.4、1200×1600 的 BOOX Max，不代表所有 BOOX 固件均兼容。

## 1. 两条独立数据通道

```text
应用打印菜单 → Windows IPP 驱动 → 127.0.0.1:8631 → PDF 队列
                                                     ├→ Wi-Fi → BOOX 接收服务 → 原生 PDF 阅读器
                                                     └→ USB ADB → BOOX 文件与原生阅读器

WinForms 发送器 → 截图/文件 → 后台处理 → 127.0.0.1:8633
                                               → Wi-Fi → BOOX 接收服务 → 内存图片页面
```

BOOX 不充当 Android PrintService 插件，不调用 PrintManager 向实体打印机输出。虚拟打印机安装在 Windows。BOOX 网络端点是本项目的接收协议，不是独立 IPP/AirPrint 服务。图片通道只有 Wi-Fi，没有 USB 图片传输后备。

| 模块 | 文件 | 职责 |
|---|---|---|
| IPP 编解码 | `desktop/ipp.mjs` | 属性、能力、请求与回执 |
| 打印桥接 | `desktop/server.mjs` | PDF 验证、落盘、串行投递、任务状态 |
| 无线传输 | `desktop/wifi.mjs` | 配对配置、发现、HMAC 探测、PDF 回执 |
| 图片桥接 | `desktop/image-server.mjs`、`images.mjs` | 本机鉴权、远端会话与图片请求 |
| 发送界面 | `desktop/ImageSender.cs` | 状态、定时、快捷键、预览、暂停顺序 |
| 图像处理 | `ImagePipeline.cs`、`ImageEnhancement.cs`、`ScreenCapture.cs` | 缩放、增强、无损编码、原生光标 |
| 设置 | `desktop/ImageSettings.cs` | 校验、默认值、原子持久化 |
| Android 服务 | `PrintReceiverService.java` | 有界 HTTP、UDP、身份校验、PDF 与图片接收 |
| Android 图片 | `ImageSession.java`、`ImageActivity.java` | 主线程会话状态、Canvas、用户阅读操作 |
| 启动/配对 | `BootReceiver.java`、`ProvisionReceiver.java` | 启用状态恢复、受 DUMP 权限保护的配对广播 |

## 2. 端口与信任边界

| 地址 | 用途 | 访问限制 |
|---|---|---|
| `127.0.0.1:8631` TCP | Windows IPP / 健康状态 | 回环监听、拒绝 Origin；没有本机 token |
| `127.0.0.1:8633` TCP | 图片控制与上传 | 固定 Host、拒绝 Origin、`X-Boox-Client` |
| BOOX `:8632` TCP | PDF / 图片接收 | 随机 Bearer 配对 token；健康探测使用 HMAC |
| BOOX `:8632` UDP | 同网段发现 | 发现结果仅是候选地址，还需 HMAC 确认身份 |

传输是局域网 HTTP，不包含 TLS。HMAC 验证设备身份、摘要验证内容完整性，但不加密文件或 Bearer token。适用于可信局域网，不能把接收端口转发到公网。本机登录账户下的其他程序可以读取本机配置，不属于本方案隔离范围。

## 3. 配对与发现

1. 首次通过已授权 USB ADB，生成 32 字节随机 token（64 个十六进制字符）和随机 deviceId。
2. 显式向 `net.booxmax.print/.ProvisionReceiver` 发送配对广播；接收器要求调用方具有 `android.permission.DUMP`。
3. Android 将配置写入私有 SharedPreferences，并开启前台接收服务；电脑保存 `.local/wifi.json`。
4. 请求 `GET /health?challenge=<32hex>`，设备计算 `HMAC-SHA256(token, deviceId + ':' + challenge)`，回传 deviceId、proof、存储 ready、images、version。
5. 优先探测已知 IP；失败后 UDP 广播 `BOOXMAXPRINT/1 <deviceId>`，收集地址，再逐个认证。UDP 回复不直接视作认证成功。

`mode=auto` 时，PDF 在没有发现可用 Wi-Fi 接收器的情况下尝试 USB。一旦无线上传开始，不因应答丢失自动切换 USB，避免文档被打开两次。

## 4. 打印任务生命周期

IPP 端点 `POST /ipp/print`，Content-Type 为 `application/ipp`。实现 Print-Job、Validate-Job、Create-Job、Send-Document、Cancel-Job、Get-Job-Attributes、Get-Jobs、Get-Printer-Attributes。只声明 PDF、单文档、单份、单面；A4/A5/Letter。页面范围应由应用在输出 PDF 前完成。

```text
4 等待文档 → 3 已入队 → 5 投递中 → 9 完成
                   └→ 7 取消（未开始投递）
任何格式/传输失败 → 8 中止
```

Windows IPP HTTP 请求总量限制为 64MiB（含 IPP 属性）；设备 PDF 限制为 64MiB。检查 `%PDF-` 与末尾 4096 字节中的 `%%EOF`，不进行完整 PDF 语义验证。电脑先写 `.part` 再改名，保存 `job-N.json`，串行交付。服务重启把未完成任务标为中止，不静默重发，原 PDF 保留。

无线上传：`POST /jobs/BOOX-<14位时间>-<任务号>.pdf`，头包括 Authorization、Content-Type、Content-Length、X-Content-Sha256。设备校验摘要及外部存储空间，临时文件完整后改名；同名不同摘要拒绝，同名相同内容可幂等重试。回执包含 deviceId、bytes、sha256、path、opened、duplicate，电脑校验后才标记成功。

设备启动阅读器组件固定为 `com.onyx.kreader/.ui.ReaderTabHostActivity`。`opened=true` 表示启动流程成功返回，不是对每页渲染的机器视觉确认。USB 备用使用 `.part`、ADB push/mv、设备 `md5` 校验及 `am start -W`；MD5 仅作复制校验。

## 5. 图片会话协议

本机 `/control`、`/frame`、`/state` 经桥接转到设备 `/images/control`、`/images/frame`、`/images/state`。本机 `/health` 不要求 token。

控制 JSON 字段：`action`、`session`、可选 `orientation`。action 允许 start/pause/resume/stop/orientation；session 为 32 位小写十六进制；orientation 为 portrait/landscape。orientation 保留暂停状态；stop 与 pause 当前都保留最后画面，不销毁图片。

图片上传接受 PNG/JPEG，1 张最多 8MiB。头包括 `X-Image-Session`、`X-Image-Sequence`（正整数）及远端 `X-Content-Sha256`。桥接为远端请求添加 SHA-256。状态/回执字段含 session、sequence、sha256、paused、reason、orientation、viewerVisible；成功上传另含 accepted、bytes、duplicate、deviceId。

- 同一会话序号递增；旧序号拒绝；相同序号必须同摘要。
- 先读完有界请求体，再检查暂停状态，避免未读上传字节导致 TCP reset 吞掉错误回执。
- 图像尺寸边长不超过 16384，输入最多 1600 万像素，按采样约束解码规模至约 400 万；不是无限图片接收器。
- 解码在服务工作线程，会话检查与提交在 Android 主线程；提交前再次校验暂停/会话，避免旧图抢回前台。
- 回执表示接受并提交显示状态，不表示墨水屏已经完成物理刷新。

## 6. 发送端并发与暂停

定时器间隔是检查频率。截图和编码使用一个后台任务，不重叠；每次任务捕获设置、会话和 generation。方向、设置、暂停或新会话使 generation 变化，已在处理的旧结果不会入队。

发送最多一张在途、一张等待；新图替换旧等待图。SHA-256 相同不重复发送。错误后保留最新等待帧，退避约 3 秒，由后续检查/轮询触发；没有无限历史队列。设备状态每 2 秒轮询，回执正常时直接驱动发送，不必等待轮询。

用户暂停截图流程：停止定时 → 最小化/隐藏窗口 → 等待原处理与在途请求 → 捕获干净的最后画面 → 等待发送 → 远端 pause。网络失败时保留最后成功画面。单文件暂停不补发截图。PDF 到达会暂停图片并打开阅读器；图片页面退出后，后台帧不会自行重新抢回，须显式继续。

## 7. 图像与字体

- 竖屏目标 1200×1600，横屏 1600×1200；截图等比适配，不上采样；文件按宽度适配，可形成长图，总量最多约 400 万像素。
- 先缩放和灰度/对比度转换，再做加权反锐化；加深模式额外加强临近暗笔画；黑白模式以亮度阈值生成二值图。
- 最多四个并行行任务，灰度仅计算一个通道；灰度及黑白保存为无损索引 PNG，彩色保持 RGB PNG。
- 光标使用 GetCursorInfo/CopyIcon/GetIconInfo/DrawIconEx，扣除选区原点与热点后叠加，随后一起缩放；释放复制的图标和位图句柄。
- 隐藏控制窗口跳过预览解码，重新打开才生成预览。
- 截图包含已栅格化的字体；不能在接收端替换字体。纯黑白去掉抗锯齿灰度，可能更显锯齿。实测字体偏好为 Noto Sans SC Medium，源应用字号和缩小比例同样重要。

## 8. 启动、存储与边界

Windows 启动目录快捷方式只在当前用户登录后运行，同时启动两个桥接，不自动截图。Android 应用内部安装，监听 BOOT_COMPLETED、MY_PACKAGE_REPLACED、CONNECTIVITY_CHANGE；在已测旧固件上使用连网广播弥补第三方开机广播过滤。用户手动停用接收会持久化；系统强行停止后需重新启动应用。

PDF 在电脑 spool 和设备 Documents 中持久保存。图片仅保留当前位图，主动点“保存”才写入 Pictures。服务持有前台通知和 Wi-Fi/CPU 唤醒锁，有耗电成本。详见 [部署](DEPLOYMENT.md)、[维护与日志](OPERATIONS.md)、[验收范围](VALIDATION.md)。
