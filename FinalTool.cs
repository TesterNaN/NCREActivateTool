using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLCipher;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NCREIntegratedTool
{
    // 共享工具类
    static class SharedUtils
    {
        public const string Password = "@#Company:www.xiaoyuan.com Author:Lough Ma$%";
        public const string Token = "@#$www.xiaoyuan.com*&^";
        public const int ProjectId = 1;
        public const int Platform = 1;

        public static readonly byte[] SerializeKey = new byte[]
        {
            25,81,195,122,118,64,194,131,129,182,252,118,100,195,24,209,
            243,101,38,178,58,215,204,51,90,252,42,93,171,99,137,52
        };
        public static readonly byte[] SerializeIV = new byte[]
        {
            174,70,250,197,9,139,36,226,154,55,26,12,216,241,24,251
        };

        public static string ComputeMd5(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        public static string ComputeFileMd5(string filePath)
        {
            using (MD5 md5 = MD5.Create())
            using (FileStream stream = File.OpenRead(filePath))
            {
                byte[] hash = md5.ComputeHash(stream);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string ComputeSha1(byte[] data)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(data);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2").ToUpper());
                return sb.ToString();
            }
        }

        public static byte[] Compress(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream())
            using (RijndaelManaged rij = new RijndaelManaged())
            using (CryptoStream cs = new CryptoStream(ms, rij.CreateEncryptor(SerializeKey, SerializeIV), CryptoStreamMode.Write))
            using (DeflateStream ds = new DeflateStream(cs, CompressionMode.Compress, true))
            {
                ds.Write(data, 0, data.Length);
                ds.Close();
                cs.FlushFinalBlock();
                return ms.ToArray();
            }
        }

        public static byte[] Decompress(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream(data))
            using (RijndaelManaged rij = new RijndaelManaged())
            using (CryptoStream cs = new CryptoStream(ms, rij.CreateDecryptor(SerializeKey, SerializeIV), CryptoStreamMode.Read))
            using (DeflateStream ds = new DeflateStream(cs, CompressionMode.Decompress))
            using (MemoryStream outMs = new MemoryStream())
            {
                try
                {
                    byte[] buffer = new byte[1024];
                    int count;
                    while ((count = ds.Read(buffer, 0, buffer.Length)) > 0)
                        outMs.Write(buffer, 0, count);
                    return outMs.ToArray();
                }
                catch { return null; }
            }
        }

        public static SQLiteConnection GetConnection(string filePath)
        {
            SQLiteConnection conn = new SQLiteConnection("Data Source=" + filePath + ";FailIfMissing=false;Pooling=false");
            conn.SetPassword(Password);
            return conn;
        }

        public static DataTable ExecuteQuery(SQLiteConnection conn, string sql)
        {
            using (SQLiteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd))
                {
                    DataTable dt = new DataTable();
                    adapter.Fill(dt);
                    return dt;
                }
            }
        }
    }

    // 步骤1：下载器（优化版）
    static class Downloader
    {
        private const string BaseUrl = "https://www.youxaan.com/ajax/";
        private static readonly HttpClient _httpClient;

        static Downloader()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        private static async Task<string> PostJsonAsync(string url, object data)
        {
            string json = JsonConvert.SerializeObject(data);
            string tokenHeader = SharedUtils.ComputeMd5(json + SharedUtils.Token);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("token", tokenHeader);
            request.Content = content;

            HttpResponseMessage response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        private static int GetOrgIdFromSkin()
        {
            string skinFile = Path.Combine(Environment.CurrentDirectory, "skin.dat");
            if (!File.Exists(skinFile))
                throw new Exception("skin.dat 不存在，请将文件放置于当前目录。");

            using (var conn = SharedUtils.GetConnection(skinFile))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT data FROM skin WHERE [path] = @path";
                    cmd.Parameters.Add("@path", DbType.String).Value = "app.json";
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new Exception("skin.dat 中未找到 app.json");

                        byte[] encryptedCompressed = (byte[])reader["data"];
                        byte[] decompressed = SharedUtils.Decompress(encryptedCompressed);
                        string appJson = Encoding.UTF8.GetString(decompressed);

                        dynamic obj = JsonConvert.DeserializeObject(appJson);
                        return (int)obj.org_id;
                    }
                }
            }
        }

        private static async Task<List<SubjectInfo>> GetSubjectList(int orgId)
        {
            string url = BaseUrl + "ncre/get_subject_list.ashx";
            var postData = new
            {
                orgid = orgId,
                _p = SharedUtils.ProjectId,
                _o = orgId,
                _f = SharedUtils.Platform
            };
            string json = await PostJsonAsync(url, postData);

            try
            {
                var subjects = JsonConvert.DeserializeObject<List<SubjectInfo>>(json);
                if (subjects != null)
                    return subjects;
            }
            catch { }

            var errorObj = JsonConvert.DeserializeObject<ReplyBase>(json);
            if (errorObj != null && errorObj._r == false)
            {
                string msg = errorObj._m ?? "未知错误";
                throw new Exception("获取科目列表失败: " + msg);
            }

            throw new Exception("获取科目列表失败：未知响应格式");
        }

        private static async Task<ProjectFilesResponse> GetSubjectFiles(int subjectId, int orgId)
        {
            string url = BaseUrl + "project/get_project_files.ashx";
            var postData = new
            {
                _p = SharedUtils.ProjectId,
                _o = orgId,
                _s = subjectId,
                _f = SharedUtils.Platform
            };
            string json = await PostJsonAsync(url, postData);

            var result = JsonConvert.DeserializeObject<ProjectFilesResponse>(json);
            if (result == null || result._r == false)
            {
                string error = (result == null || result._m == null) ? "未知错误" : result._m;
                throw new Exception("获取文件列表失败: " + error);
            }
            return result;
        }

        private static async Task DownloadFileAsync(FileItem file, string saveDir, IProgress<string> progress)
        {
            string filePath = Path.Combine(saveDir, file.path);
            string tempPath = filePath + ".tmp";

            if (File.Exists(filePath) && new FileInfo(filePath).Length == file.length)
                return;

            long existingLength = 0;
            if (File.Exists(tempPath))
            {
                existingLength = new FileInfo(tempPath).Length;
                if (existingLength > file.length)
                {
                    File.Delete(tempPath);
                    existingLength = 0;
                }
            }

            if (progress != null)
                progress.Report("开始: " + file.path);

            string downloadUrl;
            HttpMethod method;
            object postData = null;

            if (!string.IsNullOrEmpty(file.url))
            {
                downloadUrl = file.url;
                method = HttpMethod.Get;
            }
            else
            {
                downloadUrl = BaseUrl + "project/get_file_content.ashx";
                method = HttpMethod.Post;
                postData = new { id = file.id };
            }

            var request = new HttpRequestMessage(method, downloadUrl);
            if (method == HttpMethod.Post && postData != null)
            {
                string json = JsonConvert.SerializeObject(postData);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.Add("token", SharedUtils.ComputeMd5(json + SharedUtils.Token));
            }
            if (existingLength > 0)
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);

            using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                using (var fs = new FileStream(tempPath, FileMode.Append, FileAccess.Write, FileShare.None, 65536, true))
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    byte[] buffer = new byte[65536];
                    int read;
                    long total = file.length;
                    long current = existingLength;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, read);
                        current += read;
                        if (progress != null && (current - existingLength) % (1024 * 1024) < buffer.Length)
                            progress.Report(".");
                    }
                }
            }

            if (new FileInfo(tempPath).Length != file.length)
                throw new Exception("文件大小不匹配: " + file.path);

            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tempPath, filePath);

            if (progress != null)
                progress.Report("完成: " + file.path);
        }

        public static async Task RunDownloadAsync()
        {
            Console.WriteLine("步骤1：下载数据");
            Console.WriteLine("正在初始化...");

            int orgId;
            try
            {
                orgId = GetOrgIdFromSkin();
                Console.WriteLine("机构ID: " + orgId);
            }
            catch (Exception ex)
            {
                Console.WriteLine("读取 skin.dat 失败: " + ex.Message);
                return;
            }

            Console.WriteLine("正在获取科目列表...");
            List<SubjectInfo> subjects;
            try
            {
                subjects = await GetSubjectList(orgId);
                Console.WriteLine("共找到 " + subjects.Count + " 个科目：");
                foreach (var s in subjects)
                {
                    Console.WriteLine("  " + s.id + " - " + s.name);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("获取科目列表失败: " + ex.Message);
                return;
            }

            string saveDir = Environment.CurrentDirectory;
            Console.WriteLine("文件将保存到: " + saveDir);

            var progress = new Progress<string>(msg =>
            {
                lock (Console.Out)
                {
                    if (msg == ".")
                        Console.Write(".");
                    else
                        Console.WriteLine(msg);
                }
            });

            int maxConcurrency = 5;
            using (var semaphore = new SemaphoreSlim(maxConcurrency))
            {
                foreach (var subject in subjects)
                {
                    Console.WriteLine("\n开始下载科目 " + subject.id + " - " + subject.name);
                    try
                    {
                        var files = await GetSubjectFiles(subject.id, orgId);
                        Console.WriteLine("版本 " + files.ver + "，共 " + files.files.Count + " 个文件");

                        var downloadTasks = new List<Task>();
                        foreach (var file in files.files)
                        {
                            if (!file.path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            await semaphore.WaitAsync();
                            downloadTasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    await DownloadFileAsync(file, saveDir, progress);
                                }
                                catch (Exception ex)
                                {
                                    lock (Console.Out)
                                    {
                                        Console.WriteLine("\n文件 {0} 下载失败: {1}", file.path, ex.Message);
                                    }
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }));
                        }
                        await Task.WhenAll(downloadTasks);
                        Console.WriteLine("科目 " + subject.id + " 下载完成！");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("下载失败: " + ex.Message);
                    }
                }
            }

            Console.WriteLine("\n下载步骤完成。");
        }

        private class ReplyBase
        {
            [JsonProperty("_r")]
            public bool _r { get; set; }
            [JsonProperty("_m")]
            public string _m { get; set; }
        }

        private class SubjectInfo
        {
            public int id { get; set; }
            public int grade { get; set; }
            public bool hot { get; set; }
            public string name { get; set; }
        }

        private class FileItem
        {
            public int id { get; set; }
            public int length { get; set; }
            public string path { get; set; }
            public string md5 { get; set; }
            public string url { get; set; }
        }

        private class ProjectFilesResponse : ReplyBase
        {
            public int ver { get; set; }
            public List<FileItem> files { get; set; }
        }
    }

    // 步骤2：生成 data 文件（基于 topic，优化版）
    static class DataGeneratorStep2
    {
        public static void RunGenerate()
        {
            Console.WriteLine("\n步骤2：生成 data 文件（基于 topic）");
            string targetDir = Environment.CurrentDirectory;

            string[] topicFiles = Directory.GetFiles(targetDir, "topic.*.dat");
            if (topicFiles.Length == 0)
            {
                Console.WriteLine("未找到任何 topic.{id}.dat 文件，跳过此步骤。");
                return;
            }

            foreach (string topicPath in topicFiles)
            {
                try
                {
                    string fileName = Path.GetFileName(topicPath);
                    string idStr = Regex.Match(fileName, @"topic\.(\d+)\.dat").Groups[1].Value;
                    if (string.IsNullOrEmpty(idStr))
                    {
                        Console.WriteLine("跳过无法解析ID的文件: {0}", fileName);
                        continue;
                    }

                    string dataFileName = string.Format("data.{0}.dat", idStr);
                    string dataPath = Path.Combine(targetDir, dataFileName);

                    Console.WriteLine("正在处理: {0} -> {1}", fileName, dataFileName);
                    GenerateDataFile(topicPath, dataPath, int.Parse(idStr));
                    Console.WriteLine("完成。");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("处理文件时出错: {0}", ex.Message);
                }
            }
        }

        private static void GenerateDataFile(string topicFilePath, string dataFilePath, int subjectId)
        {
            ExecuteDataScript(dataFilePath);

            long topicSize = new FileInfo(topicFilePath).Length;
            string topicMd5 = SharedUtils.ComputeFileMd5(topicFilePath);

            using (SQLiteConnection dataConn = SharedUtils.GetConnection(dataFilePath))
            {
                dataConn.Open();

                bool needUpdate = true;
                using (SQLiteCommand cmd = dataConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT size, md5 FROM [ver] WHERE subjectid = @sid";
                    cmd.Parameters.AddWithValue("@sid", subjectId);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int dbSize = reader.GetInt32(0);
                            string dbMd5 = reader.GetString(1);
                            if (dbSize == topicSize && string.Equals(dbMd5, topicMd5, StringComparison.OrdinalIgnoreCase))
                                needUpdate = false;
                        }
                    }
                }

                if (!needUpdate)
                {
                    Console.WriteLine("  data 文件已是最新，跳过。");
                    return;
                }

                using (SQLiteCommand cmd = dataConn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM [keys]";
                    cmd.ExecuteNonQuery();
                }

                using (SQLiteConnection topicConn = SharedUtils.GetConnection(topicFilePath))
                {
                    topicConn.Open();
                    DataTable topics = SharedUtils.ExecuteQuery(topicConn, "SELECT id, content FROM topic");

                    using (SQLiteCommand insertCmd = dataConn.CreateCommand())
                    {
                        insertCmd.CommandText = "INSERT INTO [keys] (topicid, content) VALUES (@tid, @content)";
                        insertCmd.Parameters.Add("@tid", DbType.Int32);
                        insertCmd.Parameters.Add("@content", DbType.String);

                        Regex htmlTagRegex = new Regex("<\\/?.+?\\/?>", RegexOptions.Compiled | RegexOptions.Singleline);
                        int total = topics.Rows.Count;
                        int processed = 0;

                        using (SQLiteTransaction trans = dataConn.BeginTransaction())
                        {
                            foreach (DataRow row in topics.Rows)
                            {
                                int topicId = Convert.ToInt32(row["id"]);
                                string json = row["content"].ToString();

                                string plainText = ExtractPlainTextFromTopicJson(json, htmlTagRegex);

                                insertCmd.Parameters["@tid"].Value = topicId;
                                insertCmd.Parameters["@content"].Value = plainText;
                                insertCmd.ExecuteNonQuery();

                                processed++;
                                if (processed % 50 == 0)
                                    Console.Write("  已处理 {0}/{1} 题\r", processed, total);
                            }
                            trans.Commit();
                        }
                        Console.WriteLine("  已处理 {0}/{1} 题", processed, total);
                    }
                }

                using (SQLiteCommand cmd = dataConn.CreateCommand())
                {
                    cmd.CommandText = "INSERT OR REPLACE INTO [ver] (subjectid, size, md5) VALUES (@sid, @size, @md5)";
                    cmd.Parameters.AddWithValue("@sid", subjectId);
                    cmd.Parameters.AddWithValue("@size", topicSize);
                    cmd.Parameters.AddWithValue("@md5", topicMd5);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static string ExtractPlainTextFromTopicJson(string json, Regex htmlTagRegex)
        {
            if (string.IsNullOrEmpty(json))
                return "";

            JObject obj = JObject.Parse(json);
            StringBuilder sb = new StringBuilder();

            JToken contentToken;
            if (obj.TryGetValue("content", out contentToken) && contentToken.Type == JTokenType.String)
            {
                string content = contentToken.ToString();
                if (!string.IsNullOrEmpty(content))
                    sb.Append(content);
            }

            JToken subToken;
            if (obj.TryGetValue("sub", out subToken) && subToken.Type == JTokenType.Array)
            {
                foreach (var subItem in (JArray)subToken)
                {
                    JObject subObj = subItem as JObject;
                    if (subObj != null)
                    {
                        JToken subContentToken;
                        if (subObj.TryGetValue("content", out subContentToken) && subContentToken.Type == JTokenType.String)
                        {
                            string subContent = subContentToken.ToString();
                            if (!string.IsNullOrEmpty(subContent))
                                sb.Append(subContent);
                        }

                        int type = 0;
                        JToken typeToken;
                        if (subObj.TryGetValue("type", out typeToken) && typeToken.Type == JTokenType.Integer)
                            type = typeToken.Value<int>();

                        if (type == 11)
                        {
                            JToken interToken;
                            if (subObj.TryGetValue("inter", out interToken) && interToken.Type == JTokenType.Array)
                            {
                                foreach (var interItem in (JArray)interToken)
                                {
                                    JObject interObj = interItem as JObject;
                                    if (interObj != null)
                                    {
                                        int interType = 0;
                                        JToken interTypeToken;
                                        if (interObj.TryGetValue("type", out interTypeToken) && interTypeToken.Type == JTokenType.Integer)
                                            interType = interTypeToken.Value<int>();

                                        if (interType == 1)
                                        {
                                            JToken optionsToken;
                                            if (interObj.TryGetValue("options", out optionsToken) && optionsToken.Type == JTokenType.Array)
                                            {
                                                foreach (var opt in (JArray)optionsToken)
                                                {
                                                    if (opt.Type == JTokenType.String)
                                                        sb.Append(opt.ToString());
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            string plainText = htmlTagRegex.Replace(sb.ToString(), "");
            plainText = plainText.Replace("\r\n", "")
                                 .Replace("\r", "")
                                 .Replace("&nbsp;", "")
                                 .Replace(" ", "");
            return plainText;
        }

        private static void ExecuteDataScript(string dataFilePath)
        {
            string[] createTables = new string[]
            {
                "CREATE TABLE IF NOT EXISTS [ver] (subjectid int PRIMARY KEY, size int, md5 char(40))",
                "CREATE TABLE IF NOT EXISTS [data] (id int PRIMARY KEY, size int, [hash] char(40), data image)",
                "CREATE TABLE IF NOT EXISTS [keys] (topicid int primary key, content text)"
            };

            using (SQLiteConnection conn = SharedUtils.GetConnection(dataFilePath))
            {
                conn.Open();
                foreach (string sql in createTables)
                {
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }
    }

    // 步骤3：修补 data 文件（下载缺失资源，重建 keys，优化版）
    static class DataPatcherStep3
    {
        private static string _resourceBaseUrl;
        private static readonly HttpClient _httpClient;
        private static object _errorLock = new object();
        private static List<string> _errorMessages = new List<string>();

        static DataPatcherStep3()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // 初始化资源下载URL
            try
            {
                _resourceBaseUrl = GetResourceBaseUrl().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine("警告：无法从服务器获取资源下载地址，将使用默认地址。错误：" + ex.Message);
                _resourceBaseUrl = "https://ncre-data-1259003227.file.myqcloud.com/data.{0}.dat";
            }
        }

        private static async Task<string> GetResourceBaseUrl()
        {
            string url = "https://www.youxaan.com/ajax/project/get_project_format.ashx";
            var postData = new { id = 1 };
            string json = JsonConvert.SerializeObject(postData);
            string tokenHeader = SharedUtils.ComputeMd5(json + SharedUtils.Token);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("token", tokenHeader);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using (var response = await _httpClient.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                dynamic result = JsonConvert.DeserializeObject(responseBody);
                if (result._r == true)
                {
                    return result.content.ToString();
                }
                else
                {
                    throw new Exception("服务器返回错误: " + (result._m ?? "未知错误"));
                }
            }
        }

        public static void RunPatch()
        {
            Console.WriteLine("\n步骤3：修补 data 文件（下载资源，重建 keys）");
            Console.WriteLine("正在扫描 topic.*.dat 文件...");

            string[] topicFiles = Directory.GetFiles(Environment.CurrentDirectory, "topic.*.dat");
            if (topicFiles.Length == 0)
            {
                Console.WriteLine("未找到 topic.*.dat 文件。");
                return;
            }

            foreach (string topicFile in topicFiles)
            {
                ProcessTopicFile(topicFile);
            }

            Console.WriteLine("\n修补步骤完成。");
            if (_errorMessages.Count > 0)
            {
                Console.WriteLine("错误详情：");
                foreach (string err in _errorMessages)
                {
                    Console.WriteLine("  " + err);
                }
            }
        }

        private static void ProcessTopicFile(string topicFile)
        {
            string fileName = Path.GetFileName(topicFile);
            Match match = Regex.Match(fileName, @"topic\.(\d+)\.dat");
            if (!match.Success)
            {
                Console.WriteLine(string.Format("文件名格式不正确: {0}，跳过。", fileName));
                return;
            }
            int subjectId = int.Parse(match.Groups[1].Value);
            string dataFile = Path.Combine(Environment.CurrentDirectory, string.Format("data.{0}.dat", subjectId));

            if (!File.Exists(dataFile))
            {
                Console.WriteLine(string.Format("\n科目 {0} 的 data 文件不存在，跳过修补。", subjectId));
                return;
            }

            Console.WriteLine(string.Format("\n处理科目 {0}...", subjectId));

            List<ResourceMeta> resourceMetaList = new List<ResourceMeta>();
            List<TopicRow> topicRows = new List<TopicRow>();

            using (SQLiteConnection conn = SharedUtils.GetConnection(topicFile))
            {
                conn.Open();

                try
                {
                    using (SQLiteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT id, size, hash FROM data";
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                ResourceMeta meta = new ResourceMeta();
                                meta.Id = reader.GetInt32(0);
                                meta.Size = reader.GetInt32(1);
                                meta.Hash = reader.GetString(2);
                                resourceMetaList.Add(meta);
                            }
                        }
                    }
                }
                catch (SQLiteException ex)
                {
                    Console.WriteLine(string.Format("  读取资源元数据失败: {0}", ex.Message));
                    return;
                }

                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT rowid, id, content FROM topic ORDER BY rowid";
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            TopicRow topic = new TopicRow();
                            topic.Id = reader.GetInt32(1);
                            topic.Content = reader.GetString(2);
                            topicRows.Add(topic);
                        }
                    }
                }

                conn.Close();
            }

            Console.WriteLine(string.Format("  从 topic 读取到 {0} 条资源记录，{1} 条题目记录。", resourceMetaList.Count, topicRows.Count));

            using (SQLiteConnection conn = SharedUtils.GetConnection(dataFile))
            {
                conn.Open();

                EnsureTables(conn);

                HashSet<int> existingIds = new HashSet<int>();
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id FROM data";
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            existingIds.Add(reader.GetInt32(0));
                        }
                    }
                }

                List<ResourceMeta> missingResources = new List<ResourceMeta>();
                foreach (var meta in resourceMetaList)
                {
                    if (!existingIds.Contains(meta.Id))
                    {
                        missingResources.Add(meta);
                    }
                }

                Console.WriteLine(string.Format("  已有资源：{0}，需要下载：{1}", resourceMetaList.Count - missingResources.Count, missingResources.Count));

                if (missingResources.Count > 0)
                {
                    int maxConcurrency = 8;
                    Console.WriteLine(string.Format("  开始下载缺失的资源（并发数：{0}）...", maxConcurrency));

                    var downloadedResources = new ConcurrentBag<ResourceData>();
                    var semaphore = new SemaphoreSlim(maxConcurrency);
                    var tasks = missingResources.Select(async meta =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var data = await DownloadResourceAsync(meta);
                            if (data != null)
                            {
                                downloadedResources.Add(data);
                                lock (Console.Out) { Console.Write("."); }
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }).ToArray();

                    Task.WaitAll(tasks);

                    Console.WriteLine(string.Format("\n  下载完成，成功 {0} 个，开始写入数据。", downloadedResources.Count));

                    using (var trans = conn.BeginTransaction())
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO data (id, size, hash, data) VALUES (@id, @size, @hash, @data)";
                        var paramId = cmd.Parameters.Add("@id", DbType.Int32);
                        var paramSize = cmd.Parameters.Add("@size", DbType.Int32);
                        var paramHash = cmd.Parameters.Add("@hash", DbType.String);
                        var paramData = cmd.Parameters.Add("@data", DbType.Binary);

                        foreach (var data in downloadedResources)
                        {
                            paramId.Value = data.Id;
                            paramSize.Value = data.Size;
                            paramHash.Value = data.Hash;
                            paramData.Value = data.Data;
                            cmd.ExecuteNonQuery();
                        }
                        trans.Commit();
                    }

                    if (downloadedResources.Count != missingResources.Count)
                    {
                        Console.WriteLine("  警告：部分资源下载失败，请检查错误列表。");
                    }
                }
                else
                {
                    Console.WriteLine("  所有资源已存在，无需下载。");
                }

                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM [keys]";
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine("  并行生成 keys 表...");
                int totalTopics = topicRows.Count;
                Tuple<int, string>[] keyResults = new Tuple<int, string>[totalTopics];
                Regex htmlRegex = new Regex("<\\/?.+?\\/?>", RegexOptions.Compiled);

                Parallel.ForEach(topicRows, (row, state, index) =>
                {
                    string plain = ExtractPlainText(row.Content);
                    string noHtml = htmlRegex.Replace(plain, "");
                    noHtml = noHtml.Replace("\r\n", "").Replace("\r", "").Replace("\n", "")
                                   .Replace("&nbsp;", "").Replace(" ", "");
                    keyResults[index] = Tuple.Create(row.Id, noHtml);
                });

                using (SQLiteTransaction trans = conn.BeginTransaction())
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO [keys] (topicid, content) VALUES (@tid, @content)";
                    SQLiteParameter pTid = cmd.Parameters.Add("@tid", DbType.Int32);
                    SQLiteParameter pContent = cmd.Parameters.Add("@content", DbType.String);

                    for (int i = 0; i < totalTopics; i++)
                    {
                        var result = keyResults[i];
                        pTid.Value = result.Item1;
                        pContent.Value = result.Item2;
                        cmd.ExecuteNonQuery();
                    }
                    trans.Commit();
                }

                long topicSize = new FileInfo(topicFile).Length;
                string topicMd5 = SharedUtils.ComputeFileMd5(topicFile);
                using (SQLiteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT OR REPLACE INTO [ver] (subjectid, size, md5) VALUES (@sid, @size, @md5)";
                    cmd.Parameters.AddWithValue("@sid", subjectId);
                    cmd.Parameters.AddWithValue("@size", topicSize);
                    cmd.Parameters.AddWithValue("@md5", topicMd5);
                    cmd.ExecuteNonQuery();
                }

                conn.Close();
            }

            Console.WriteLine(string.Format("  已更新 {0}，keys 表包含 {1} 条记录。", dataFile, topicRows.Count));
        }

        private static async Task<ResourceData> DownloadResourceAsync(ResourceMeta meta)
        {
            string url = string.Format(_resourceBaseUrl, meta.Id);
            try
            {
                byte[] data = await _httpClient.GetByteArrayAsync(url);
                if (data.Length != meta.Size)
                    throw new Exception(string.Format("大小不匹配 (期望 {0}，实际 {1})", meta.Size, data.Length));

                string fileHash = SharedUtils.ComputeSha1(data);
                if (!string.Equals(fileHash, meta.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new Exception(string.Format("哈希不匹配 (期望 {0}，实际 {1})", meta.Hash, fileHash));

                return new ResourceData
                {
                    Id = meta.Id,
                    Size = meta.Size,
                    Hash = meta.Hash,
                    Data = data
                };
            }
            catch (Exception ex)
            {
                lock (_errorLock)
                {
                    _errorMessages.Add(string.Format("资源 ID {0} 下载/验证失败: {1}", meta.Id, ex.Message));
                }
                return null;
            }
        }

        private static string ExtractPlainText(string json)
        {
            JObject obj = JObject.Parse(json);
            StringBuilder sb = new StringBuilder();

            Action<JToken> extract = null;
            extract = (token) =>
            {
                if (token.Type == JTokenType.Object)
                {
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        if (prop.Name == "content" && prop.Value.Type == JTokenType.String)
                        {
                            sb.Append(prop.Value.Value<string>());
                        }
                        else if (prop.Name == "options" && prop.Value.Type == JTokenType.Array)
                        {
                            foreach (var opt in prop.Value)
                            {
                                if (opt.Type == JTokenType.String)
                                    sb.Append(opt.Value<string>());
                            }
                        }
                        else
                        {
                            extract(prop.Value);
                        }
                    }
                }
                else if (token.Type == JTokenType.Array)
                {
                    foreach (var item in token)
                    {
                        extract(item);
                    }
                }
            };

            extract(obj);
            return sb.ToString();
        }

        private static void EnsureTables(SQLiteConnection conn)
        {
            using (SQLiteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS [ver] (
                        subjectid INTEGER PRIMARY KEY,
                        size INTEGER,
                        md5 TEXT
                    )";
                cmd.ExecuteNonQuery();
            }

            using (SQLiteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS [data] (
                        id INTEGER PRIMARY KEY,
                        size INTEGER,
                        [hash] TEXT,
                        data BLOB
                    )";
                cmd.ExecuteNonQuery();
            }

            using (SQLiteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS [keys] (
                        topicid INTEGER PRIMARY KEY,
                        content TEXT
                    )";
                cmd.ExecuteNonQuery();
            }
        }

        class ResourceMeta
        {
            public int Id { get; set; }
            public int Size { get; set; }
            public string Hash { get; set; }
        }

        class ResourceData
        {
            public int Id { get; set; }
            public int Size { get; set; }
            public string Hash { get; set; }
            public byte[] Data { get; set; }
        }

        class TopicRow
        {
            public int Id { get; set; }
            public string Content { get; set; }
        }
    }

    // 步骤4：生成授权文件 licence.dat
    static class LicenceGeneratorStep4
    {
        private static string skinFile = "skin.dat";

        public static void RunGenerate()
        {
            Console.WriteLine("\n步骤4：生成授权文件 licence.dat");

            int orgID = 154;
            try
            {
                string appJson = GetAppJsonFromSkin();
                if (!string.IsNullOrEmpty(appJson))
                {
                    Match match = Regex.Match(appJson, "\"org_id\"\\s*:\\s*\"?(\\d+)\"?");
                    if (match.Success)
                    {
                        orgID = int.Parse(match.Groups[1].Value);
                        Console.WriteLine(string.Format("从 skin.dat 读取 org_id = {0}", orgID));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("读取 skin.dat 出错：{0}，使用默认值 {1}", ex.Message, orgID));
            }

            string uuid = GetMachineUuidMd5();
            string macName = GetMacName();
            string version = GetMacVersion();
            Console.WriteLine(string.Format("本机 UUID MD5: {0}", uuid));
            Console.WriteLine(string.Format("系统: {0} {1}", macName, version));

            int maxTopicVer = 0;
            string[] topicFiles = Directory.GetFiles(Environment.CurrentDirectory, "topic.*.dat");
            foreach (string file in topicFiles)
            {
                try
                {
                    int ver = GetTopicVersion(file);
                    if (ver > maxTopicVer) maxTopicVer = ver;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format("读取 {0} 失败: {1}", Path.GetFileName(file), ex.Message));
                }
            }
            Console.WriteLine(string.Format("当前最大题库版本: {0}", maxTopicVer));

            int licenceVer = maxTopicVer + 1;
            Console.WriteLine(string.Format("许可证版本设置为: {0}", licenceVer));

            StringBuilder sb = new StringBuilder();
            sb.Append("<root>");
            sb.Append("<active>true</active>");
            sb.AppendFormat("<orgid>{0}</orgid>", orgID);
            sb.AppendFormat("<serial>{0}</serial>", 10086);
            sb.AppendFormat("<projectid>{0}</projectid>", 1);
            sb.AppendFormat("<subid>{0}</subid>", 42);
            sb.AppendFormat("<vip>{0}</vip>", 3);
            sb.AppendFormat("<lastdate>{0}</lastdate>", "9999-12-31");
            sb.AppendFormat("<ver>{0}</ver>", licenceVer);
            sb.Append("<token></token>");
            sb.AppendFormat("<macid>{0}</macid>", 0);
            sb.AppendFormat("<uuid>{0}</uuid>", uuid);
            sb.Append("<device>windows</device>");
            sb.AppendFormat("<macname>{0}</macname>", macName);
            sb.AppendFormat("<version>{0}</version>", version);
            sb.AppendFormat("<today>{0}</today>", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("</root>");

            string xml = sb.ToString();
            byte[] xmlBytes = Encoding.UTF8.GetBytes(xml);
            byte[] compressed = SharedUtils.Compress(xmlBytes);

            string outFile = "licence.dat";
            File.WriteAllBytes(outFile, compressed);
            Console.WriteLine(string.Format("已生成 {0}，大小 {1} 字节。", outFile, compressed.Length));
        }

        private static int GetTopicVersion(string topicFile)
        {
            using (var conn = SharedUtils.GetConnection(topicFile))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT ver FROM subject LIMIT 1";
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return Convert.ToInt32(result);
                }
                conn.Close();
            }
            return 0;
        }

        private static string GetAppJsonFromSkin()
        {
            if (!File.Exists(skinFile)) return null;
            using (var conn = SharedUtils.GetConnection(skinFile))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT data FROM skin WHERE [path] = @path";
                    cmd.Parameters.Add("@path", DbType.String).Value = "app.json";
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            byte[] encryptedCompressed = (byte[])reader["data"];
                            byte[] decompressed = SharedUtils.Decompress(encryptedCompressed);
                            if (decompressed != null)
                                return Encoding.UTF8.GetString(decompressed);
                        }
                    }
                }
                conn.Close();
            }
            return null;
        }

        private static string GetMachineUuidMd5()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object uuidObj = obj["UUID"];
                        if (uuidObj != null)
                        {
                            string uuid = uuidObj.ToString();
                            if (!string.IsNullOrEmpty(uuid))
                            {
                                byte[] bytes = Encoding.UTF8.GetBytes(uuid);
                                using (MD5 md5 = MD5.Create())
                                {
                                    byte[] hash = md5.ComputeHash(bytes);
                                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("获取 UUID 出错: {0}", ex.Message));
            }
            return "fallback_uuid_md5";
        }

        private static string GetMacName()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Caption, OSArchitecture FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string caption = obj["Caption"] != null ? obj["Caption"].ToString() : "";
                        string arch = obj["OSArchitecture"] != null ? obj["OSArchitecture"].ToString() : "";
                        return (caption + " " + arch).Trim();
                    }
                }
            }
            catch { }
            return "Unknown Windows";
        }

        private static string GetMacVersion()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Version FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (obj["Version"] != null)
                            return obj["Version"].ToString();
                    }
                }
            }
            catch { }
            return "0.0";
        }
    }

    // 主程序
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("NCRE 一体化工具 (下载 + 生成data + 修补data + 生成授权)");
            Console.WriteLine("========================================================\n");

            try
            {
                // 步骤1：下载数据
                Downloader.RunDownloadAsync().GetAwaiter().GetResult();

                // 步骤2：生成data文件
                DataGeneratorStep2.RunGenerate();

                // 步骤3：修补data文件
                DataPatcherStep3.RunPatch();

                // 步骤4：生成授权文件
                LicenceGeneratorStep4.RunGenerate();
            }
            catch (Exception ex)
            {
                Console.WriteLine("执行过程中出现错误: " + ex.Message);
            }

            Console.WriteLine("\n所有步骤执行完毕。按任意键退出...");
            Console.ReadKey();
        }
    }
}