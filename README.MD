# NCRE白菜课堂一键离线授权工具

## 简介
本工具专为“白菜课堂计算机等级考试学习系统”设计，能够自动完成数据下载、数据文件生成、资源修补以及授权文件生成。激活后软件将显示为**高级VIP**，有效期至**9999年12月31日**。

## 功能特点
- **一键式操作**：四个步骤自动顺序执行，无需人工干预。
- **模拟官方激活**：生成完全符合格式的 `licence.dat`，程序自认为是通过合法渠道激活。
- **数据完整性**：从官方服务器下载最新题库（`topic.*.dat`），并生成配套的 `data.*.dat`（用于全文搜索）。
- **资源修补**：检测并下载缺失的资源文件，确保所有功能正常。
- **动态获取地址**：资源下载地址从服务器动态获取，适应官方变更。
- **断点续传**：下载支持断点续传，网络中断后重新运行可继续。
- **仓库自带依赖**：所有必需的 DLL 均已包含在仓库中，无需额外下载。

## 系统要求
- 操作系统：Windows 7 SP1 及以上（x86/x64）
- 依赖框架：.NET Framework 4.5 或更高版本（Windows 系统通常自带）
- 已安装“白菜课堂计算机等级考试学习系统”（目标程序目录需有 `skin.dat` 文件）

## 使用方法
1. **获取工具**  
   从 [Releases](https://github.com/NCREActivateTool/releases) 页面下载最新版本的压缩包。

2. **解压并放置文件**  
   将压缩包解压，得到 `FinalTool_Merged.exe`。**将这个文件复制到白菜课堂的安装根目录**（例如 `C:\白菜课堂计算机等级考试学习系统`）。

3. **以管理员身份运行**  
   右键点击 `FinalTool_Merged.exe`，选择 **“以管理员身份运行”**（部分系统需权限才能写入文件）。

4. **等待执行完成**  
   工具将依次执行四个步骤，命令行窗口会显示实时进度：
   - 步骤1：下载数据（从服务器获取最新题库）
   - 步骤2：生成 data 文件（基于 `topic.*.dat` 生成 `data.*.dat`）
   - 步骤3：修补 data 文件（下载缺失资源并重建索引）
   - 步骤4：生成授权文件（创建 `licence.dat`）
   整个过程根据网络速度和机器性能可能需要几分钟到十几分钟。

5. **启动学习系统**  
   关闭工具窗口后，直接运行白菜课堂主程序，即可看到VIP状态已变为“高级VIP”，到期时间为“9999年12月31日”。所有功能已完全解锁。

## 编译指南
如果您希望自行从源代码编译，仓库已包含所有必需的依赖库，无需额外下载。

### 准备工作
- 安装 [.NET Framework 4.5 SDK](https://www.microsoft.com/zh-cn/download/details.aspx?id=30653) 或 Visual Studio（含 C# 编译器）。
- 克隆本仓库到本地。

### 编译普通版本
在仓库目录打开命令行，执行：
```bash
csc /platform:x86 /reference:System.Data.SQLCipher.dll /reference:Newtonsoft.Json.dll /reference:System.Net.Http.dll /target:exe /out:FinalTool.exe FinalTool.cs
```
生成 `FinalTool.exe`，运行时需要同目录下的 `System.Data.SQLCipher.dll` 和 `Newtonsoft.Json.dll`。

### 编译单文件版本（合并托管库）
如需生成单文件版，需使用 [ILMerge](https://github.com/dotnet/ILMerge) 工具。安装 ILMerge 后，在生成 `FinalTool.exe` 的基础上执行：
```bash
ilmerge /target:exe /targetplatform:v4 /out:FinalTool_Merged.exe FinalTool.exe Newtonsoft.Json.dll /log
```

## 注意事项
- 本工具仅供学习交流使用，请勿用于商业用途。
- 使用前建议备份原程序目录，以防意外丢失数据。
- 如果杀毒软件误报，请添加信任或暂时关闭（工具涉及文件读写和网络请求）。
- 更换硬件或重装系统后，需重新运行工具生成新的 `licence.dat`。
- 如果官方更新了激活文件校验方式，此工具可能失效，请关注后续版本。

## 免责声明
本工具是出于技术研究和学习目的开发的，不承担任何因使用本工具而产生的法律责任。请用户遵守软件许可协议，在合法范围内使用。

---

如有问题或建议，欢迎提交 Issue 或 Pull Request。