# 验证记录

公开版已脱敏；私网地址、SSID 和设备标识已替换为占位符。文中 build/ 证据文件为当时本机产物，未全部公开；可用测试脚本重新生成。结构化摘要见 [验收日志](logs/acceptance-2026-11-16.json)。

日期：2026-11-16。

## 已确认环境

- 已连接 BOOX Max：Android 4.4.4，armeabi-v7a，1200×1600。
- 已安装 BOOX 阅读器 `com.onyx.kreader` 与 KOReader。
- 电脑已有 Microsoft IPP Class Driver。
- Wi-Fi 初始状态关闭；未修改网络设置、默认打印机或 ADB 端口映射。
- 初始 BOOX 存储状态为 `shared`（USB 存储共享）；用户关闭后已确认变为 `mounted`，设备可正常读写文档。

## 已通过

- IPP UTF-8 文档标题、属性多值、二进制文档边界解析。
- 畸形属性和截断数据拒绝。
- 只声明 PDF 支持，其他文档格式返回 IPP 格式错误。
- Create-Job / Send-Document / Get-Job-Attributes 流程。
- 重复 Send-Document 拒绝，避免重复提交。
- HTTP 接收与落盘字节一致（测试模式；尚不代表真机传输）。
- 禁止浏览器跨来源请求。
- `node --test tests/*.test.mjs`：6 项通过，包括无线接收端身份验证、PDF 字节传输与错误回执拒绝。

## 端到端验证已通过

- Windows 管理员授权后成功添加 **BOOX Max**，驱动为 Microsoft IPP Class Driver，打印机状态正常。
- 两页中英文图文测试文档通过 Windows 系统打印生成 PDF，大小 133614 字节。
- 测试任务 2 通过 USB 传送，设备与电脑 MD5 一致，任务返回 `job-completed-successfully`。
- BOOX 自带阅读器自动打开文件；设备截图确认中文、英文、图形及第二页正常显示。
- 用户于 2026-11-16 实测确认：**浏览器网页打印正常，使用 BOOX Max 自带软件显示 PDF，超过屏幕的内容可以下翻阅读。**

结论：当前电脑的浏览器／系统打印 → USB → BOOX 自带阅读器链路已完成端到端验收。Word/WPS 各应用尚未单独验收。

## 兼容性修复

- Android 4.4.4 的 shell 无 `wc`，最初文件已送达但大小校验误报失败。
- 改用设备已有的 `md5` 命令，与电脑本地摘要比较；重跑真实打印成功。
- 用户关闭 USB 存储共享后保留 USB 调试连接。未修改 Wi-Fi、VPN、DNS 或 ADB 端口映射。

## Wi-Fi 与自启动验收（2026-11-16）

- Windows 当前用户启动目录已创建 `BOOX Max Print.lnk`。停止原服务后实际执行该快捷方式，隐藏启动成功，健康接口正常，随后系统打印成功；未重启整台电脑。
- BOOX 已连接用户指定 SSID `<SSID>`，地址 `<BOOX_IP>`；电脑 WLAN 地址 `<PC_IP>`。
- 无线接收 APK 使用 Java 原生 API，最低 Android API 19，v1 签名验证通过。
- 内部安装遇到 `INSTALL_FAILED_INSUFFICIENT_STORAGE`。设备应用分区空间低，但大存储分区剩余约 9.9 GB；改为该分区安装后成功。未卸载应用或删除用户文档。
- 通过 USB 保护广播完成配对，真实 HMAC 身份验证与 UDP 自动发现均成功。
- 任务 5 在有 USB 连接时选择 Wi-Fi；SHA-256 一致，状态成功。
- 用户明确拔下 USB 后，ADB 设备列表为空；再次通过 Windows 虚拟打印机提交两页中英文图文测试。
- 任务 6：133614 字节，`transport=wifi`、`job-completed-successfully`，接收端返回 PDF 已保存且已启动 BOOX 自带阅读器。
- 任务 6 文件：`/storage/sdcard/Documents/BooxMaxPrint/BOOX-20261116032616-6.pdf`；SHA-256：`6625f3be11740a306acaadea56f62d67c3eaa26110ae82a367b1eaf862742d15`。
- 网络配置沿用用户已连接状态；未启用无线 ADB，未新增 ADB 端口映射，未修改 VPN、DNS 或防火墙。

## 尚未验证及边界

- 网络断开失败提示、重连恢复和长时间连续打印。
- Windows 整机重启后的自启动尚未做重启测试；已验证实际自启动入口。
- BOOX 开机自动接收已通过下述真实重启测试；旧版外部安装限制已解决。
- BOOX 深度休眠及长时间待机；当前接收中保持 Wi-Fi/CPU 唤醒锁。
- 手机系统打印和 BOOX 独立 IPP 服务仍未实现。

## BOOX 开机自动接收验收（0.3.0）

- 接收器已从外部存储迁移至 `/data/app/net.booxmax.print-2.apk`，保留配对凭据、启用状态及用户文档。
- 最初内部安装受 Android 低存储预留阈值限制。迁移期间临时使用 8% 预留，完成后删除临时设置并重启；已验证 `sys_storage_threshold_percentage=null`、实际阈值恢复默认 `99.18 MB`，恢复记录位于 `.local/storage-reserve-baseline.json`，状态 `restored`。
- Android 4.4 的 `settings` 命令不支持 `delete`；恢复默认值使用 `content delete --uri content://settings/global/sys_storage_threshold_percentage`，只删除该临时键。
- 此 BOOX 固件（2017-05-02）的 `ActivityManagerService` 调用 `android.util.ActivityManagerHelper.bootCompletedFilter`，按系统应用标志过滤第三方开机广播。内部安装后仅注册 `BOOT_COMPLETED` 仍不能启动，已通过真实重启确认。
- 静态证据来自本机只读提取的 `/system/framework/services.odex` 与 `framework.odex`，使用已有 baksmali/dexlib2 读取；未修改固件。对应本地反汇编位于 `build/boox-services` 与 `build/boox-framework/android/util/ActivityManagerHelper.smali`。
- 兼容处理同时监听 `CONNECTIVITY_CHANGE`，已配对且启用接收时，网络连接成功自动启动前台服务；手动停止会持久化关闭状态。
- 最终真实重启后没有打开 MainActivity，也没有通过 ADB 启动服务。日志确认 `Starting receiver after android.net.conn.CONNECTIVITY_CHANGE`；服务为前台接收状态，HMAC 验证、UDP 发现、存储就绪均成功。旧固件派发广播需要等待，看到桌面不代表接收器立即就绪。
- 重启后的实际 Windows 打印任务 **7**：`transport=wifi`，133614 字节，状态 `job-completed-successfully`，回执为 `PDF saved and reader opened`；系统活动记录确认原生阅读器收到该 PDF。
- 文件：`/storage/sdcard/Documents/BooxMaxPrint/BOOX-20261116040622-7.pdf`；SHA-256：`d098966b813012eb3dc3fbc4c5652cf57c05a2b89ace37e06c96071fbcfa0d70`。
- 本次重启验收保留 USB 调试连接用于观察日志，但任务明确走 Wi-Fi；此前任务 6 已验证拔掉 USB 的无线链路。
- 全程只操作 BOOX 序列号 `<BOOX_SERIAL>`，没有操作同时连接的三星设备，没有改变 VPN、DNS、路由或 ADB 端口映射。

## 低频图片显示验收（0.4.0）

- Windows 发送器 `dist/BooxImages.exe` 编译通过，入口 `Start-Images.cmd`；Android 0.4.0 内部更新安装成功，v1 签名验证通过，保留原配对。
- 刷新检查间隔为 0.5、1、2、5、10 秒，默认 5 秒。提供发送端横屏 1600×1200 / 竖屏 1200×1600 切换，默认竖屏。
- 电脑控制窗口已通过可访问性检查，中文控件及默认间隔 5 可读。桌面截图工具出现 `FrameArrived timed out` 和几何信息缺失，随后用户以物理 Esc 停止 Computer Use；此后没有继续桌面输入。框选、快捷键、完整发送器按钮流程及长图拖动尚未完成交互验收。
- `node --test tests/*.test.mjs` 共 7 项通过，覆盖原打印协议、无线身份与回执、新图片协议校验和方向参数。
- 真实设备经 `127.0.0.1:8633` 图片桥接向已配对 BOOX 发送，验证重复帧幂等、暂停拒绝、旧序号拒绝、10 张不同画面按 0.5 秒目标间隔调度、可见图片页面、横竖屏控制保持暂停状态、本机未认证及跨来源请求拒绝。
- 该批测试图片约 39–40 KB，1200×1600，接收/解码提交回执耗时约 202–796 ms。该指标不包含物理墨水屏刷新完成时间，不等于稳定 2 FPS；网络慢时不会无限积压旧帧。
- 证据：`build/image-verification.json`，`build/boox-image-test.png`，`build/boox-image-landscape-final.png`。真机截图确认中文、图形与灰阶显示；横屏稳定后系统窗口为 `[0,0][1600,1200]`，保持完整图像及留白。旧固件方向转换曾需约 5 秒，过早截图会捕获过渡画面。
- 图片接收进行时提交 Windows PDF 打印任务 **8**，133615 字节，`transport=wifi`、`job-completed-successfully`；接收端暂停图片模式并打开原生阅读器，此时后续图帧被拒绝，显式恢复后可再次显示图片。
- 修复了暂停时提前关闭未读完上传内容的连接引发 TCP reset、错误回执丢失的问题：读取有界请求体后检查会话，提交显示前再次检查会话与序号。
- 图片只保留当前内存画面；未开启无人值守截图，没有更改网络、VPN、DNS、防火墙或 ADB 端口映射。长时间稳定性、真实断网后恢复、物理屏幕残影/闪烁及 0.4.0 整机重启仍需后续实测。

## 用户实测反馈及发送器 0.4.1（2026-11-16）

- 用户确认横屏、竖屏切换均正常；0.5 秒间隔正常显示，主观感觉比 1 秒更快。此为用户实测反馈，不外推为所有图片均达到稳定 2 FPS。
- 新发送器默认主显示器整屏，直接开始，无需先框选；保留局部框选和切回整屏。
- 新增四档显示增强：原图、文字清晰（默认）、文字加深、黑白高对比。快捷键说明移至控制菜单下方，并补充到托盘菜单底部。
- 暂停按钮、快捷键和托盘暂停共用流程：先收起控制窗口，等待在途发送结束，补送最新整屏/所选区域截图并等待回执，再暂停。仅单张文件显示时不改为屏幕截图；选区调整等内部暂停不触发补送。
- `tests/ImageEnhancementTests.cs` 编译及执行通过：细字边缘强化、加深模式笔画增强、白背景不变、纯黑白输出、奇数宽度行对齐、单像素边界，以及 Graphics 存活时的实际处理路径。生成样例位于 `build/enhancement-verification`，已查看增强输出。
- 缓存 Bitmap 尺寸，避免像素循环反复调用 GDI+；1200×1600 文字清晰处理耗时从本机初测约 1259 ms 降至约 128 ms。此为单次算法测量，不含截图、PNG 编码、网络或墨水屏刷新。
- 新发送器编译通过，文件版本 0.4.1.0；仅更新 Windows 端，已验收的 Android 接收器与横竖屏协议不变。新整屏按钮及暂停操作顺序尚待用户界面实测，未继续桌面自动操作。

## 发送器 0.4.2（2026-11-16）

- 默认检查间隔 500ms；横竖屏、增强、灰度、对比度、光标开关立即原子保存，下次启动恢复。坏 JSON、空配置与越界值回退；启动不会自动截图。
- 原生光标按实际形状、屏幕坐标与热点叠加，支持选区和缩放；文件发送不叠加。默认关闭，开启状态会保存。真实交互桌面测试确认光标绘制产生 80 个非白像素。
- 新后台捕获/增强/编码任务不会重叠；generation 变化后丢弃旧任务结果；等待帧只保留最新一张。隐藏窗口不再逐帧解码预览。暂停仍等待干净的最后一帧处理及发送，再发暂停指令。
- 增强改为最多四个并行行任务，灰度单通道处理；灰度/黑白输出采用无损 8 位索引 PNG。随机彩色/灰色样本、三种边界尺寸、四模式逐像素与旧串行算法一致；生成文字页的 PNG 解码像素与旧版一致，横竖屏尺寸检查通过。
- 同轮基准（1200×1600 合成文字页，毫秒，三次热运行中位数）：旧版原图 108、文字清晰 134、文字加深 164、黑白高对比 176；新版对应 91、118、109、88。旧流程包含原来每帧必做的预览解码，新流程对应托盘发送。该结果不含截图、网络和物理刷新，不代表任何页面都有固定耗时。机器负载变化明显，较早一轮旧增强为 273–394ms、新增强为 131–242ms。
- 对应原图/清晰/加深/黑白 PNG 大小从 36377/47347/47819/26777 字节降至 23784/28663/28880/17053 字节。Android 4.4 真机接受全部新 PNG。旧样例暖机回执大多 207–542ms；新样例 11/12 次为 135–202ms，但一次为 2215ms，说明仍有无线或设备端偶发长尾；无法据此保证物理屏幕每 0.5 秒完成刷新。
- `tools/test-images.ps1` 和独立桌面生命周期测试通过；Node 7 项测试通过。生命周期测试覆盖默认 0.5 秒、不自动截图、设置重开恢复、单任务、过期画面丢弃与最新等待帧替换。首次在隔离会话执行原生光标测试因没有可见光标失败并产生 .NET 异常框；已关闭该测试并修正异常处理，实际桌面会话复测通过。没有继续操作用户鼠标或窗口。
- 应用/托盘内嵌多尺寸猫头鹰电子书图标，源为 `tools/BuildImageIcons.cs`。Android 接收器未改版；没有 USB 连接，不更改网络或无线 ADB。
- 用户指出测试页文字有锯齿；当时最后留屏的是纯黑白测试模式。补发 `build/font-comparison/fonts.png` 灰度字体对照页并暂停，接收回执成功（104276 字节）；用户选择 **Noto Sans SC Medium**。字体样例选择不意味着已替换第三方网页字体。
- 证据：`build/image-performance.txt`、`build/baseline-frames/transfer.json`、`build/optimized-frames/transfer.json`、`build/sender-lifecycle/cursor.png`。物理屏幕的连续跟手体验、最终新界面的按钮布局及实际截图光标位置仍需用户实测确认。
- 最终已替换 `dist/BooxImages.exe`，部署文件 SHA-256 与构建文件一致，文件版本 0.4.2.0；新版进程启动并响应。仅重启图片桥接，健康接口默认间隔确认为 0.5；项目中创建 `BOOX Images.lnk` 使用新图标。BOOX 保留已暂停的字体对照页，未开启自动截图。

## 首次公开发布检查（2026-11-16）

- 整理 README、架构、部署、维护/日志、版本记录和脱敏验收 JSON；保留开发期事实并标明没有独立覆盖的场景。
- 暂存 64 个源码/文档/图标文件；排除配置、签名、PDF、截图、原始日志、产物和快捷方式。索引审计检查 3 项本机真实凭据/设备身份值，没有进入公开内容；文档链接及 Git 空白检查通过。
- Node 7 项测试再次通过；全部 PowerShell 脚本语法解析通过；Windows 发送器重新构建成功。
- Android 新增 SDK 路径覆盖参数后，实际构建 APK 成功，v1/v2/v3 签名验证成功；没有重新安装设备 APK。
- 从 Git 索引导出干净目录，未复制任何 .local、dist、build 或 spool；其中独立通过 Node 测试、Windows 发送器构建和图像/设置回归。干净构建测试不访问真机，也不替换当前运行程序。
